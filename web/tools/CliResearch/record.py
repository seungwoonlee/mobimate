"""CLI 블랙박스 녹화기 (조사용, 읽기 전용).

게임이 켜져 있는 동안 공개 조회 명령(get_*)을 주기적으로 모두 호출해, 값이 바뀐 필드만 JSONL로 남긴다.
레이드·어비스·필드 보스·요일 던전 클리어 같은 사건 전후에 "게임이 직접 알려 주는" 신호가 무엇인지 찾는 것이 목적이다.

- 상태를 바꾸는 명령(execute_/complete_/play_/change_/stop_/stand_/write_)은 절대 호출하지 않는다.
- 빠른 명령은 2초, 보통은 10초, 무거운 명령(목록류)은 60초마다 읽는다.
- CLI 실행 파일이 바뀌면(게임 업데이트) capabilities와 숨은 명령 후보를 다시 확인해 기록한다.
- 출력: web/tools/CliResearch/records/YYYY-MM-DD.jsonl (git에 넣지 않는다)  (한 줄 = {"t", "cmd", "changes": [[path, old, new], ...]} 또는 {"t", "event", ...})
"""
import concurrent.futures as cf
import datetime as dt
import json
import os
import subprocess
import sys
import time

CLI = r'C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe'
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'records')

FAST = ['get_activity', 'get_current_environment', 'get_quests', 'get_currencies']                       # 2초
NORMAL = ['get_my_info', 'get_daily_missions', 'get_weekly_missions', 'get_inventory', 'get_near_pcs']   # 10초
SLOW = ['get_items', 'get_altering_works', 'get_near_npcs', 'get_instruments', 'get_music_scores',
        'get_social_actions', 'get_gatherable_items', 'get_craftable_items', 'get_alterable_items']      # 60초
# 게임 데이터에 이름은 있지만 2026-10-22 현재 unknown_command인 조회 명령: 열리는지 감시한다
HIDDEN = ['get_growth_guide', 'get_chats', 'get_camera_state', 'get_dialogue_host_state', 'get_login_state',
          'get_quick_navigation_shortcuts', 'get_housing_inventory', 'get_housing_placements',
          'get_housing_placement_mode_state', 'get_photo_mode_state', 'list_dev_actions', 'game_list_destinations']
# 매 순간 바뀌어 사건 분석에 방해만 되는 필드(기록은 하되 10초에 한 번만)
NOISY = ('Vitals.', 'WorldPosition.', 'ErinnNow', '.Distance', 'Performance.ElapsedSeconds', 'Performance.RemainingSeconds')


def call(cmd):
    try:
        r = subprocess.run([CLI, cmd], capture_output=True, timeout=15)
        raw = r.stdout
        try:
            text = raw.decode('utf-8')
        except UnicodeDecodeError:
            text = raw.decode('cp949', 'replace')
        return json.loads(text)
    except Exception as e:  # 시간 초과·파싱 실패는 기록만 하고 넘어간다
        return {'__error__': str(e)[:200]}


def flatten(v, path='', out=None):
    out = {} if out is None else out
    if isinstance(v, dict):
        for k, x in v.items():
            flatten(x, f'{path}.{k}' if path else k, out)
    elif isinstance(v, list):
        # 목록은 식별자로 쓸 만한 이름 필드가 있으면 그것을 키로, 없으면 순번으로
        for i, x in enumerate(v):
            key = None
            if isinstance(x, dict):
                for name in ('QuestTitle', 'DisplayName', 'Title', 'Name', 'Description', 'DisplayTitle'):
                    if isinstance(x.get(name), str) and x[name]:
                        key = x[name]
                        if name == 'DisplayName' and 'Location' in x:
                            key = f"{x['Location']}:{key}"
                        break
            flatten(x, f'{path}[{key if key is not None else i}]', out)
        if not v:
            out[path + '[]'] = '<empty>'
    else:
        out[path] = v
    return out


def diff(old, new):
    changes = []
    for k in sorted(set(old) | set(new)):
        a, b = old.get(k, '<none>'), new.get(k, '<none>')
        if a != b:
            changes.append([k, a, b])
    return changes


def main():
    os.makedirs(OUT, exist_ok=True)
    last = {}            # cmd -> flattened
    last_noisy = {}      # cmd -> time of last noisy write
    due = {c: 0.0 for c in FAST + NORMAL + SLOW}
    period = {**{c: 2 for c in FAST}, **{c: 10 for c in NORMAL}, **{c: 60 for c in SLOW}}
    exe_mtime = None
    connected = None

    def write(obj):
        now = dt.datetime.now()
        obj = {'t': now.strftime('%H:%M:%S.%f')[:-3], **obj}
        with open(os.path.join(OUT, now.strftime('%Y-%m-%d') + '.jsonl'), 'a', encoding='utf-8') as f:
            f.write(json.dumps(obj, ensure_ascii=False) + '\n')

    write({'event': 'recorder_start'})
    with cf.ThreadPoolExecutor(max_workers=6) as pool:
        while True:
            now = time.time()
            st = call('status')
            is_on = st.get('pipe') == 'connected'
            if is_on != connected:
                write({'event': 'pipe', 'connected': is_on, 'detail': st})
                connected = is_on
            if not is_on:
                time.sleep(10)
                continue

            m = os.path.getmtime(CLI)
            if m != exe_mtime:   # 시작 때와 CLI가 갱신될 때마다 명령 목록·숨은 명령을 다시 확인
                caps = call('capabilities')
                names = sorted(c.get('Command') for c in caps.get('commands', []))
                hidden = {h: ('unknown' if call(h).get('error') == 'unknown_command' else 'OPEN') for h in HIDDEN}
                write({'event': 'capabilities', 'cli_mtime': dt.datetime.fromtimestamp(m).isoformat(), 'commands': names,
                       'loading': caps.get('loading'), 'hidden': hidden})
                exe_mtime = m

            run = [c for c in due if due[c] <= now]
            for c in run:
                due[c] = now + period[c]
            results = dict(zip(run, pool.map(call, run)))
            for c, v in results.items():
                if isinstance(v, dict) and v.get('pipe') == 'disconnected':
                    continue
                flat = flatten(v)
                old = last.get(c)
                if old is None:
                    write({'cmd': c, 'snapshot': v})
                else:
                    ch = diff(old, flat)
                    quiet = [x for x in ch if not any(n in x[0] for n in NOISY)]
                    noisy = [x for x in ch if any(n in x[0] for n in NOISY)]
                    if noisy and now - last_noisy.get(c, 0) >= 10:
                        quiet += noisy
                        last_noisy[c] = now
                    elif noisy:
                        for k, a, _ in noisy:   # 기록하지 않은 잡음은 이전 값을 유지해 다음에 몰아서 남긴다
                            flat[k] = a
                    if quiet:
                        write({'cmd': c, 'changes': quiet})
                last[c] = flat
            time.sleep(max(0.2, 2 - (time.time() - now)))


if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)
