"""docs/CLI_COMMAND_TABLE.md를 다시 만든다.

- 게임에서 capabilities와 공개 조회 명령(get_*) 응답을 새로 받아(snapshots/에 저장) 필드 목록을 뽑는다. 게임이 꺼져 있으면 마지막 스냅샷을 쓴다.
- 숨은 명령 후보(게임 데이터에 이름만 있는 것)를 다시 불러 열렸는지 확인한다(조회형만).
- notes.json(사람이 쓴 실측 메모)과 records/(녹화기 기록)의 통계를 합친다.
상태를 바꾸는 명령은 호출하지 않는다.  사용: python make_doc.py
"""
import datetime as dt
import glob
import json
import os
import sys
import re
import subprocess

CLI = r'C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe'
HERE = os.path.dirname(os.path.abspath(__file__))
SNAP = os.path.join(HERE, 'snapshots')
DOC = os.path.normpath(os.path.join(HERE, '..', '..', '..', 'docs', 'CLI_COMMAND_TABLE.md'))
HIDDEN = ['get_growth_guide', 'get_chats', 'get_camera_state', 'get_dialogue_host_state', 'get_login_state',
          'get_quick_navigation_shortcuts', 'get_housing_inventory', 'get_housing_placements',
          'get_housing_placement_mode_state', 'get_photo_mode_state', 'list_dev_actions', 'game_list_destinations']
# 게임 데이터에 이름만 있고 상태를 바꾸는 것으로 보이는 후보(호출하지 않는다, 목록만 남긴다)
HIDDEN_MUTATING = ['execute_mission_shortcut', 'execute_quest_tracker', 'execute_quick_navigation_shortcut', 'run_dev_action',
                   'game_move_to_named_location', 'start_housing_edit_mode', 'end_housing_edit_mode', 'move_housing_prop',
                   'cancel_housing_placement', 'set_photo_mode_blur', 'set_photo_mode_zoom', 'take_photo', 'set_camera']


def call(cmd):
    r = subprocess.run([CLI, cmd], capture_output=True, timeout=20)
    try:
        text = r.stdout.decode('utf-8')
    except UnicodeDecodeError:
        text = r.stdout.decode('cp949', 'replace')
    return json.loads(text)


def fields_of(v, path='', out=None):
    out = {} if out is None else out
    if isinstance(v, dict):
        for k, x in v.items():
            fields_of(x, f'{path}.{k}' if path else k, out)
    elif isinstance(v, list):
        out.setdefault(path + '[]', 'array')
        for x in v[:300]:
            fields_of(x, path + '[]', out)
    else:
        t = 'null' if v is None else {'bool': 'bool', 'int': 'int', 'float': 'number', 'str': 'string'}.get(type(v).__name__, type(v).__name__)
        if out.get(path) in (None, 'null', 'array'):
            out[path] = t
    return out


META = r'C:\Nexon\MabinogiMobile\MabinogiMobile_Data\il2cpp_data\Metadata\global-metadata.dat'
DEV_FIRST = 'CaptureScreen'   # 이 클래스부터 뒤는 개발·테스트용 묶음(화면 캡처·치트·UI 덤프 등)


def snake(name):
    return re.sub(r'(?<!^)(?=[A-Z])', '_', name).lower()


def command_classes():
    """IL2CPP 식별자 표에서 Mcp<Name>Command 클래스와, 그 뒤에 이어지는 필드·메서드 이름을 뽑는다(정의 순서대로라 다음 클래스 전까지가 그 명령 몫)."""
    ids_path = os.path.join(SNAP, 'meta', 'identifiers.json')
    if not os.path.exists(ids_path) or os.path.getmtime(ids_path) < os.path.getmtime(META):
        subprocess.run([sys.executable, '-I', os.path.join(HERE, 'il2cpp_names.py'), META, os.path.join(SNAP, 'meta')], check=True)
    ids = json.load(open(ids_path, encoding='utf-8'))
    pos = [(i, x) for i, x in enumerate(ids) if re.fullmatch(r'Mcp[A-Za-z]+Command', x)]
    out = []
    dev = False
    for k, (i, x) in enumerate(pos):
        end = pos[k + 1][0] if k + 1 < len(pos) else i + 80
        core = x[3:-7]
        dev = dev or core == DEV_FIRST
        words = [w for w in ids[i + 1:min(end, i + 80)]
                 if re.fullmatch(r'[A-Za-z][A-Za-z0-9_]{1,40}', w) and not w.startswith(('get_', 'set_', 'System')) and w != x]
        out.append({'class': x, 'command': snake(core), 'dev': dev, 'names': list(dict.fromkeys(words))[:40]})
    return out


def main():
    os.makedirs(SNAP, exist_ok=True)
    live = call('status').get('pipe') == 'connected'
    if live:
        caps = call('capabilities')
        json.dump(caps, open(os.path.join(SNAP, 'capabilities.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        for c in caps['commands']:
            if c['Command'].startswith('get_'):
                json.dump(call(c['Command']), open(os.path.join(SNAP, c['Command'] + '.json'), 'w', encoding='utf-8'), ensure_ascii=False)
        public = {c['Command'] for c in caps['commands']}
        reads = sorted({c['command'] for c in command_classes() if not c['dev'] and c['command'].startswith(('get_', 'list_', 'game_list_')) and c['command'] not in public} | set(HIDDEN))
        hidden = {h: ('열림' if call(h).get('error') != 'unknown_command' else 'unknown_command') for h in reads}
        json.dump({'checked': dt.datetime.now().isoformat(timespec='minutes'), 'result': hidden},
                  open(os.path.join(SNAP, 'hidden.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    caps = json.load(open(os.path.join(SNAP, 'capabilities.json'), encoding='utf-8'))
    hidden = json.load(open(os.path.join(SNAP, 'hidden.json'), encoding='utf-8'))
    notes = json.load(open(os.path.join(HERE, 'notes.json'), encoding='utf-8'))
    cli_mtime = dt.datetime.fromtimestamp(os.path.getmtime(CLI)).strftime('%Y-%m-%d %H:%M')

    # 녹화기 통계: 명령별로 값이 바뀐 필드와 횟수
    changed = {}
    for f in sorted(glob.glob(os.path.join(HERE, 'records', '*.jsonl'))):
        for line in open(f, encoding='utf-8'):
            try:
                r = json.loads(line)
            except ValueError:
                continue
            for k, _, _ in r.get('changes', []):
                base = re.sub(r'\[[^\]]*\]', '[]', k)   # 목록 항목 이름을 [] 로 모은다
                changed.setdefault(r['cmd'], {}).setdefault(base, 0)
                changed[r['cmd']][base] += 1

    L = []
    w = L.append
    w('# 게임 CLI 명령 표 (MabinogiMobile_CLI)')
    w('')
    w(f'> 자동 생성: `web/tools/CliResearch/make_doc.py` — 마지막 생성 {dt.datetime.now():%Y-%m-%d %H:%M}'
      f' ({"게임에서 새로 읽음" if live else "게임이 꺼져 있어 마지막 스냅샷 사용"}). CLI 실행 파일 {cli_mtime}.')
    w('> 손으로 쓰는 실측 메모는 `notes.json`에 더하고 다시 생성한다. 게임하는 동안 `record.py`(녹화기)를 켜 두면 값이 바뀐 필드가 `records/`에 쌓인다.')
    w('')
    w('## 개요')
    w('')
    for g in notes['general']:
        w(f'- {g}')
    w('')
    cmds = caps['commands']
    w(f'## 명령 목록 ({len(cmds)}개)')
    w('')
    w('| 명령 | 범위 | 종류 | 앱 사용 | 설명 |')
    w('|---|---|---|---|---|')
    for c in cmds:
        n = notes['commands'].get(c['Command'], {})
        kind = '변경' if n.get('mutates') else '조회'
        w(f"| `{c['Command']}` | {n.get('scope', '?')} | {kind} | {'✓' if n.get('used') else ''} | {c.get('Description', '').replace('|', '/')} |")
    w('')
    w('## 명령별 상세')
    for c in cmds:
        name = c['Command']
        n = notes['commands'].get(name, {})
        w('')
        w(f'### `{name}`')
        w('')
        w(f"- 설명: {c.get('Description', '')}")
        if c.get('BodyExample'):
            w(f"- 입력(body) 예: `{c['BodyExample']}`")
        if c.get('OutputExample'):
            w(f"- 출력(명세): {c['OutputExample']}")
        if c.get('Note'):
            w(f"- 명세 메모: {c['Note']}")
        for x in n.get('notes', []):
            w(f'- 실측: {x}')
        snap = os.path.join(SNAP, name + '.json')
        if os.path.exists(snap):
            try:
                fs = fields_of(json.load(open(snap, encoding='utf-8')))
            except ValueError:
                fs = {}
            if fs:
                w('')
                w('| 필드 | 타입 | 녹화 중 바뀐 횟수 |')
                w('|---|---|---|')
                ch = changed.get(name, {})
                for p, t in fs.items():
                    hits = ch.get(p, 0)
                    w(f"| `{p}` | {t} | {hits or ''} |")
    w('')
    w('## 숨은 명령 (게임 데이터에 이름만 있음)')
    w('')
    w('게임의 IL2CPP 문자열 표(`MabinogiMobile_Data/il2cpp_data/Metadata/global-metadata.dat`)에서 뽑은 이름이다. 공개 목록(capabilities)에는 없다. 조회형만 실제로 불러 확인한다.')
    w('')
    w(f"| 명령 | 상태 (확인 {hidden['checked']}) |")
    w('|---|---|')
    for h, s in hidden['result'].items():
        w(f'| `{h}` | {s} |')
    w('')
    w('상태를 바꾸는 것으로 보이는 후보(호출하지 않음): ' + ', '.join(f'`{x}`' for x in HIDDEN_MUTATING))
    w('')
    classes = command_classes()
    public = {c['Command'] for c in cmds}
    w('## 게임 코드 안의 명령 클래스 전체 (정적 분석)')
    w('')
    w('게임 코드(`MM.Client.Game.Integration.MCP.Commands` 이름공간)에 있는 `Mcp…Command` 클래스다. 공개 여부는 게임 데이터 표(`cliForAiCommandSheetId`)와 등급(`McpCliTier`: CliChannel / Dev)으로 정해진다(`McpCommandCatalog.IsCommandEnabled`). '
      '그래서 잠긴 명령은 게임 업데이트 없이 서버 데이터만으로 열릴 수 있다. 명령 이름은 클래스 이름에서 만든 추정이라 실제와 다를 수 있다. 오른쪽 열은 그 클래스 근처의 필드·메서드 이름(응답 구조의 단서)이다.')
    w('')
    w('| 구분 | 명령(추정) | 상태 | 관련 이름 |')
    w('|---|---|---|---|')
    for c in classes:
        cmd = c['command']
        kind = '개발·테스트' if c['dev'] else ('공개' if cmd in public else '잠김')
        state = '공개' if cmd in public else hidden['result'].get(cmd, '-' if c['dev'] else '확인 안 함(상태 변경형)')
        w(f"| {kind} | `{cmd}` | {state} | {' '.join(c['names'][:25])} |")
    w('')
    w('### 정적 분석으로 본 공통 구조')
    w('')
    for x in notes.get('internals', []):
        w(f'- {x}')
    w('')
    w('## 변경 이력')
    w('')
    for x in notes.get('history', []):
        w(f'- {x}')
    os.makedirs(os.path.dirname(DOC), exist_ok=True)
    open(DOC, 'w', encoding='utf-8').write('\n'.join(L) + '\n')
    print('wrote', DOC, 'live' if live else 'snapshot')


if __name__ == '__main__':
    main()
