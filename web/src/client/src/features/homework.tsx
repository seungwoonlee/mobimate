import { useRef, useState, type CSSProperties, type MouseEvent } from 'react';
import { useCharacters, useHomework } from '../api/queries';
import type { AccountGroup, CharacterCard, HomeworkAuto } from '../api/types';
import { Dialog, ErrorCard, Fresh, Icon, JobIcon, Pill, Skeleton } from '../components/ui';
import { useNow } from '../hooks/layout';
import { setHomework } from '../lib/actions';
import { entryShort, MAX_VALUE, nextStep, prevStep, SEASON_IDS, SEASON_NOTE, STEP_GOALS, STEP_UNITS, stepOf, VALUE_UNITS } from '../lib/homeworkSteps';
import { until } from '../lib/format';
import { charName } from './characters';

/**
 * 숙제 화면 (2026-10-11 개편): 계정의 캐릭터마다 가로로 긴 박스 한 줄, 그 안에 중요 숙제의 완료 여부.
 * 세로로 쌓아 두어 어떤 캐릭터가 어떤 숙제를 아직 못 했는지 한눈에 본다.
 * 칸을 누르면 미완료 ↔ 완료를 수동으로 바꾼다(캐릭터마다, 자동 판정이 놓친 경우). 접속 안 한 캐릭터는 마지막으로 본 값에 수동 체크가 겹친다.
 */
export const HW_GROUPS: { title: string; cells: { id: string; label: string }[] }[] = [
  { title: '일일·주간', cells: [{ id: 'daily_day_dungeon', label: '요일던전' }, { id: 'weekly_guild_regular', label: '길드의뢰' }, { id: 'weekly_vanguard_breach', label: '뱅가드' }, { id: 'weekly_barrier_1_7', label: '소환의 결계' }, { id: 'weekly_black_hole_1_7', label: '검은 구멍' }, { id: 'weekly_nightmare_drill', label: '나이트메어 드릴' }, { id: 'weekly_nightmare_rank', label: '드릴 랭킹' }] },
  { title: '레이드', cells: [{ id: 'raid_scars', label: '적월의 스카스' }, { id: 'raid_cavrak', label: '카브락' }, { id: 'raid_airel', label: '에이렐' }, { id: 'raid_white_succubus', label: '화이트 서큐버스' }] },
  { title: '어비스', cells: [{ id: 'abyss_illusory_anchorage', label: '허상의 정박지' }, { id: 'abyss_madness_cave', label: '광기의 동굴' }, { id: 'abyss_scattered_waterway', label: '흩어진 물길' }] },
];

type State = 'done' | 'todo' | 'unknown' | 'locked';

interface Cell { key: string; label: string; state: State; note?: string; /** 클리어 추정(미션 카운터는 올랐는데 어느 항목인지 모름): 눌러서 확인 */ guess?: boolean; /** 남은 횟수 (뱅가드, 미완료일 때) */ left?: number; /** 여러 번 해야 끝나는 숙제의 목표 횟수와 지금 횟수: 누를 때마다 0→1→…→목표→0 */ steps?: number; step?: number; /** 손으로 입력하는 수치 칸(랭킹): 단위와 지금 값 */ unit?: string; value?: number; toggleId?: string; boss?: boolean }

/** 한 캐릭터의 칸들: 일반 숙제 8개 + 필드 보스 1개 */
export function cellsOf(c: Pick<CharacterCard, 'homework' | 'fieldBoss'> & Partial<Pick<CharacterCard, 'combat' | 'level' | 'recommend'>>): Cell[] {
  const byId = new Map<string, HomeworkAuto>(c.homework.map(h => [h.id, h]));
  const cells: Cell[] = HW_GROUPS.flatMap(g => g.cells.map(x => {
    const h = byId.get(x.id);
    const why = entryShort(c, x.id);
    if (why != null) return { key: x.id, label: x.label, state: 'locked', note: why } as Cell;
    const state = h?.state ?? 'unknown';
    const steps = STEP_GOALS[x.id];
    const left = state === 'todo' && h?.remaining != null ? h.remaining : undefined;
    const unit = VALUE_UNITS[x.id];
    return { key: x.id, label: x.label, state, left, guess: h?.guess, toggleId: x.id, steps, step: steps ? stepOf(steps, state === 'done', left) : undefined, unit, value: unit ? (h?.value ?? 0) : undefined } as Cell;
  }));
  cells.push({ key: 'field_boss', label: '필드보스', state: c.fieldBoss.state, note: c.fieldBoss.item ?? undefined, guess: c.fieldBoss.guess, boss: true });
  return cells;
}

const STATE_TEXT: Record<State, string> = { done: '완료', todo: '미완료', unknown: '미확인', locked: '입장 불가' };

/** 필드 보스 여섯 (카탈로그 `field_boss_weekly` 풀). 다른 캐릭터도 고칠 수 있어야 해서 숙제 보드(현재 캐릭터)와 별개로 둔다 */
const BOSSES: { id: string; title: string }[] = [
  { id: 'fieldboss_peri', title: '페리' }, { id: 'fieldboss_crabbach', title: '크라브바흐' }, { id: 'fieldboss_krama', title: '크라마' },
  { id: 'fieldboss_drochenem', title: '드로흐에넴' }, { id: 'fieldboss_tormog', title: '토르모그' }, { id: 'fieldboss_angrbahan', title: '앙그르바한' },
];

export function HomeworkView() {
  const q = useCharacters();
  const hw = useHomework();
  const now = useNow(30_000);
  const [bossFor, setBossFor] = useState<CharacterCard | null>(null);

  const accounts: AccountGroup[] = q.data ? [...q.data.data.accounts].sort((a, b) => Number(b.hasCurrent) - Number(a.hasCurrent)) : [];
  const total = accounts.reduce((n, a) => n + a.members.length, 0);

  return (
    <>
      <div className="view-head">
        <h2>숙제 <span className="num muted">({total}명)</span></h2>
        <span className="sub"><Fresh at={q.dataUpdatedAt} /></span>
      </div>
      {hw.data && (
        <p className="hw-resets faint small m0">
          <span><Icon name="clock" size={13} /> 일일 리셋 {until(hw.data.data.nextDailyResetUtc, now)}</span>
          <span><Icon name="clock" size={13} /> 주간 리셋 {until(hw.data.data.nextWeeklyResetUtc, now)}</span>
        </p>
      )}
      {q.isPending ? <Skeleton lines={6} /> : q.isError && !q.data ? <ErrorCard error={q.error} onRetry={() => q.refetch()} /> : total === 0 ? (
        <div className="card muted">아직 확인한 캐릭터가 없습니다. 게임에 접속하면 여기에 모입니다.</div>
      ) : (
        <>
          <section className="card hw-table" aria-label="캐릭터별 숙제">
            <div className="hw-all" style={{ '--hwcols': orderedCells(cellsOf(accounts[0].members[0])).length } as CSSProperties}>
              <div className="hw-row hw-head" aria-hidden="true">
                <span>캐릭터</span>
                {orderedCells(cellsOf(accounts[0].members[0])).map(x => <span key={x.key}>{x.label}</span>)}
              </div>
              {accounts.map(a => <AccountBoard key={a.id} a={a} onBoss={setBossFor} showName={accounts.length > 1} />)}
            </div>
          </section>
          <p className="faint small m0">
            칸을 누르면 <b>미완료 ↔ 완료</b>가 바뀝니다(수동 체크).<br />
            횟수 칸(뱅가드 3회, 소환의 결계 7단계, 검은 구멍 14회, 나이트메어 드릴 30구역)은 누를 때마다 <b>0 → 1 → 2 → … → 완료 → 0</b>으로 돕니다. 한 단계 뒤로는 Shift+클릭이나 우클릭입니다.<br />
            드릴 랭킹 칸은 처음엔 <b>-</b>이고, 누르면 순위를 직접 입력합니다(Enter나 다른 곳을 누르면 저장, 비우면 다시 -).<br />
            <b>나이트메어 드릴·드릴 랭킹</b>은 <b>{SEASON_NOTE}</b>입니다. 주간 리셋으로 지우지 않으니 시즌이 바뀌면 직접 0으로 돌려 주세요.<br />
            <span className="hw-legend done">✓ 완료</span> <span className="hw-legend todo">○ 미완료(이번 주기에 확인)</span> <span className="hw-legend unknown">· 미확인(아직 확인 전, 미완료로 봅니다)</span> ·
            지금 접속한 캐릭터만 실시간이고, 다른 캐릭터는 마지막으로 접속했을 때 본 값입니다.
          </p>
        </>
      )}
      {bossFor && <BossDialog c={bossFor} onClose={() => setBossFor(null)} />}
    </>
  );
}

function AccountBoard({ a, onBoss, showName }: { a: AccountGroup; onBoss: (c: CharacterCard) => void; showName: boolean }) {
  const left = a.members.reduce((n, m) => n + cellsOf(m).filter(c => c.unit == null && c.state !== 'done' && c.state !== 'locked').length, 0);
  return (
    <section className="hw-acct" aria-label={`${a.name} 계정 숙제`}>
      {showName && (
        <div className="hw-acct-h">
          <h3>{a.name} 계정 {a.hasCurrent && <Pill tone="ok">접속 중</Pill>}</h3>
          <span className="small muted">{left === 0 ? '모두 완료' : `남은 숙제 ${left}개`}</span>
        </div>
      )}
      <div className="hw-rows">
        {a.members.map(m => <CharacterRow key={m.key} c={m} onBoss={onBoss} />)}
      </div>
    </section>
  );
}

/** 칸 순서: 묶음(요일던전·길드의뢰·뱅가드 …) 순서대로, 필드 보스는 맨 끝 */
function orderedCells(cells: Cell[]): Cell[] {
  return [...HW_GROUPS.flatMap(g => cells.filter(x => g.cells.some(y => y.id === x.key))), ...cells.filter(x => x.boss)];
}

function CharacterRow({ c, onBoss }: { c: CharacterCard; onBoss: (c: CharacterCard) => void }) {
  const cells = cellsOf(c);
  const counted = cells.filter(x => x.unit == null && x.state !== 'locked');   // 수치 입력 칸(랭킹)은 완료 수에 넣지 않는다
  const done = counted.filter(x => x.state === 'done').length;
  const all = done === counted.length;
  const name = charName(c);
  return (
    <div className={`hw-row ${all ? 'all-done' : ''} ${c.isCurrent ? 'current' : ''}`} role="group" aria-label={`${name} 숙제 ${done}/${counted.length} 완료`}>
      <div className="hw-who">
        <JobIcon job={c.job} size={22} />
        <span className="hw-who-t">
          <span className="hw-who-name">{name}{c.isCurrent && <Pill tone="ok">접속 중</Pill>}</span>
          <span className="small muted">{c.job} Lv.{c.level}</span>
        </span>
        <span className={`hw-count num ${all ? 'ok' : ''}`} aria-hidden="true">{done}/{counted.length}</span>
      </div>
      <div className="hw-cells">
        {orderedCells(cells).map(x => <CellView key={x.key} x={x} c={c} onBoss={onBoss} />)}
      </div>
    </div>
  );
}

/** 칸을 누르면 미완료 ↔ 완료를 바꾼다. 필드 보스는 완료로 바꿀 때 어느 보스인지 고른다. */
function CellView({ x, c, onBoss }: { x: Cell; c: CharacterCard; onBoss: (c: CharacterCard) => void }) {
  if (x.unit != null) return <ValueCell x={x} c={c} />;
  if (x.state === 'locked') {   // 입장 전투력 미달: 누를 수 없고 완료 수에도 넣지 않는다
    return (
      <div className="hw-cell locked" title={x.note} aria-label={`${x.label} 입장 불가 — ${x.note}`}>
        <span className="hw-cell-name">{x.label}</span>
        <span className="hw-cell-state">입장 불가</span>
      </div>
    );
  }
  const done = x.state === 'done';
  const guess = !!x.guess && !done;   // 미션 카운터는 올랐는데 어느 항목인지 못 가렸다
  const stepped = x.steps != null && x.step != null;
  const stepUnit = STEP_UNITS[x.key];
  const text = stepped ? (stepUnit ? `${x.step}${stepUnit}` : `${x.step}/${x.steps}`) : guess ? '클리어 추정' : x.left != null ? `남은 ${x.left}회` : STATE_TEXT[x.state];
  const label = `${x.label} ${text}${x.note ? ` (${x.note})` : ''}`;
  const characterKey = c.isCurrent ? undefined : c.key;
  const onClick = (e: MouseEvent<HTMLButtonElement>) => {
    if (stepped) {   // 일부만 했을 수 있어 0→1→2→…→목표(완료)→0. Shift를 누르고 누르면 한 단계 뒤로
      void setHomework(x.toggleId!, { count: e.shiftKey ? prevStep(x.step!) : nextStep(x.step!, x.steps!), characterKey });
      return;
    }
    if (!x.boss) { void setHomework(x.toggleId!, { completed: !done, characterKey }); return; }
    if (!done) { onBoss(c); return; }
    // 완료 → 미완료: 넘긴 보스의 체크를 거둔다 (이름을 모르면 여섯 모두)
    const cleared = BOSSES.filter(b => b.title === x.note);
    for (const b of cleared.length ? cleared : BOSSES) void setHomework(b.id, { completed: false, characterKey });
  };
  return (
    <button type="button" className={`hw-cell ${x.state}${guess ? ' guess' : ''}${SEASON_IDS.has(x.key) ? ' season' : ''} btn-cell`} title={SEASON_IDS.has(x.key) ? SEASON_NOTE : undefined} aria-pressed={done} onContextMenu={stepped ? (e => { e.preventDefault(); void setHomework(x.toggleId!, { count: prevStep(x.step!), characterKey }); }) : undefined} aria-label={`${label} — 눌러서 ${stepped ? `${nextStep(x.step!, x.steps!)}/${x.steps}회로` : done ? '미완료로' : '완료로'} 바꾸기`} onClick={onClick}>
      <span className="hw-cell-name">{x.label}</span>
      <span className="hw-cell-state">{done ? <Icon name="check" size={13} /> : x.state === 'todo' ? '○' : '·'} {x.note && done ? x.note : text}</span>
    </button>
  );
}

/** 필드 보스: 여섯 중 하나만 넘기면 된다. 이번 주에 넘긴 보스를 고른다. */
function BossDialog({ c, onClose }: { c: CharacterCard; onClose: () => void }) {
  const characterKey = c.isCurrent ? undefined : c.key;
  return (
    <Dialog title={`필드 보스 (주 1회, 택1) — ${charName(c)}`} onClose={onClose}
      actions={<button type="button" className="btn" onClick={onClose}>닫기</button>}>
      <p className="small muted m0">이번 주에 넘긴 보스를 고르세요. 여섯 중 하나만 넘기면 됩니다.</p>
      <div className="hw-boss-list">
        {BOSSES.map(b => (
          <button key={b.id} type="button" className="btn" onClick={() => { void setHomework(b.id, { completed: true, characterKey }); onClose(); }}>{b.title}</button>
        ))}
      </div>
    </Dialog>
  );
}

/**
 * 손으로 입력하는 수치 칸(드릴 랭킹): 처음엔 `-`이고, 누르면 그 자리에서 순위를 입력한다.
 * Enter나 다른 곳을 누르면 저장해 `✓ N위`(완료)로 보이고, 비우거나 0이면 다시 `-`이다. Esc는 입력을 버린다.
 */
function ValueCell({ x, c }: { x: Cell; c: CharacterCard }) {
  const characterKey = c.isCurrent ? undefined : c.key;
  const done = x.state === 'done' && (x.value ?? 0) > 0;
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState('');
  const skip = useRef(false);   // Esc로 버릴 때 blur가 저장하지 않게 한다
  const start = () => { setDraft(done ? String(x.value) : ''); skip.current = false; setEditing(true); };
  const commit = () => {
    setEditing(false);
    if (skip.current) return;
    const n = Math.min(MAX_VALUE, Math.max(0, Math.floor(Number(draft) || 0)));
    if (n !== (done ? x.value : 0)) void setHomework(x.toggleId!, { count: n, characterKey });
  };
  if (!editing) {
    return (
      <button type="button" className={`hw-cell ${done ? 'done' : 'unknown'} btn-cell value-cell`} title={SEASON_NOTE} aria-label={`${x.label} ${done ? `${x.value}${x.unit}` : '입력 전'} — 눌러서 순위 입력`} onClick={start}>
        <span className="hw-cell-name">{x.label}</span>
        <span className="hw-cell-state">{done ? <><Icon name="check" size={13} /> {x.value}{x.unit}</> : '-'}</span>
      </button>
    );
  }
  return (
    <div className={`hw-cell ${done ? 'done' : 'unknown'} value-cell editing`} title={SEASON_NOTE}>
      <span className="hw-cell-name">{x.label}</span>
      <span className="hw-cell-state">
        <input type="number" inputMode="numeric" min={0} max={MAX_VALUE} autoFocus value={draft} aria-label={`${x.label} 순위 입력`}
          onChange={e => setDraft(e.target.value)} onBlur={commit}
          onKeyDown={e => { if (e.key === 'Enter') e.currentTarget.blur(); else if (e.key === 'Escape') { skip.current = true; e.currentTarget.blur(); } }} />{x.unit}
      </span>
    </div>
  );
}
