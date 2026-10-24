import { useState, type CSSProperties } from 'react';
import { useCharacters, useHomework } from '../api/queries';
import type { AccountGroup, CharacterCard, HomeworkAuto } from '../api/types';
import { Dialog, ErrorCard, Fresh, Icon, JobIcon, Pill, Skeleton } from '../components/ui';
import { useNow } from '../hooks/layout';
import { setHomework } from '../lib/actions';
import { nextStep, STEP_GOALS, stepOf } from '../lib/homeworkSteps';
import { until } from '../lib/format';
import { charName } from './characters';

/**
 * 숙제 화면 (2026-10-11 개편): 계정의 캐릭터마다 가로로 긴 박스 한 줄, 그 안에 중요 숙제의 완료 여부.
 * 세로로 쌓아 두어 어떤 캐릭터가 어떤 숙제를 아직 못 했는지 한눈에 본다.
 * 칸을 누르면 미완료 ↔ 완료를 수동으로 바꾼다(캐릭터마다, 자동 판정이 놓친 경우). 접속 안 한 캐릭터는 마지막으로 본 값에 수동 체크가 겹친다.
 */
export const HW_GROUPS: { title: string; cells: { id: string; label: string }[] }[] = [
  { title: '일일·주간', cells: [{ id: 'daily_day_dungeon', label: '요일던전' }, { id: 'weekly_guild_regular', label: '길드의뢰' }, { id: 'weekly_vanguard_breach', label: '뱅가드' }] },
  { title: '레이드', cells: [{ id: 'raid_cavrak', label: '카브락' }, { id: 'raid_airel', label: '에이렐' }, { id: 'raid_white_succubus', label: '화이트 서큐버스' }] },
  { title: '어비스', cells: [{ id: 'abyss_illusory_anchorage', label: '허상의 정박지' }, { id: 'abyss_madness_cave', label: '광기의 동굴' }, { id: 'abyss_scattered_waterway', label: '흩어진 물길' }] },
];

type State = 'done' | 'todo' | 'unknown';

interface Cell { key: string; label: string; state: State; note?: string; /** 클리어 추정(미션 카운터는 올랐는데 어느 항목인지 모름): 눌러서 확인 */ guess?: boolean; /** 남은 횟수 (뱅가드, 미완료일 때) */ left?: number; /** 여러 번 해야 끝나는 숙제의 목표 횟수와 지금 횟수: 누를 때마다 0→1→…→목표→0 */ steps?: number; step?: number; toggleId?: string; boss?: boolean }

/** 한 캐릭터의 칸들: 일반 숙제 8개 + 필드 보스 1개 */
export function cellsOf(c: Pick<CharacterCard, 'homework' | 'fieldBoss'>): Cell[] {
  const byId = new Map<string, HomeworkAuto>(c.homework.map(h => [h.id, h]));
  const cells: Cell[] = HW_GROUPS.flatMap(g => g.cells.map(x => {
    const h = byId.get(x.id);
    const state = h?.state ?? 'unknown';
    const steps = STEP_GOALS[x.id];
    const left = state === 'todo' && h?.remaining != null ? h.remaining : undefined;
    return { key: x.id, label: x.label, state, left, guess: h?.guess, toggleId: x.id, steps, step: steps ? stepOf(steps, state === 'done', left) : undefined } as Cell;
  }));
  cells.push({ key: 'field_boss', label: '필드보스', state: c.fieldBoss.state, note: c.fieldBoss.item ?? undefined, guess: c.fieldBoss.guess, boss: true });
  return cells;
}

const STATE_TEXT: Record<State, string> = { done: '완료', todo: '미완료', unknown: '미확인' };

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
  const left = a.members.reduce((n, m) => n + cellsOf(m).filter(c => c.state !== 'done').length, 0);
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
  const done = cells.filter(x => x.state === 'done').length;
  const all = done === cells.length;
  const name = charName(c);
  return (
    <div className={`hw-row ${all ? 'all-done' : ''} ${c.isCurrent ? 'current' : ''}`} role="group" aria-label={`${name} 숙제 ${done}/${cells.length} 완료`}>
      <div className="hw-who">
        <JobIcon job={c.job} size={22} />
        <span className="hw-who-t">
          <span className="hw-who-name">{name}{c.isCurrent && <Pill tone="ok">접속 중</Pill>}</span>
          <span className="small muted">{c.job} Lv.{c.level}</span>
        </span>
        <span className={`hw-count num ${all ? 'ok' : ''}`} aria-hidden="true">{done}/{cells.length}</span>
      </div>
      <div className="hw-cells">
        {orderedCells(cells).map(x => <CellView key={x.key} x={x} c={c} onBoss={onBoss} />)}
      </div>
    </div>
  );
}

/** 칸을 누르면 미완료 ↔ 완료를 바꾼다. 필드 보스는 완료로 바꿀 때 어느 보스인지 고른다. */
function CellView({ x, c, onBoss }: { x: Cell; c: CharacterCard; onBoss: (c: CharacterCard) => void }) {
  const done = x.state === 'done';
  const guess = !!x.guess && !done;   // 미션 카운터는 올랐는데 어느 항목인지 못 가렸다
  const stepped = x.steps != null && x.step != null;
  const text = stepped ? `${x.step}/${x.steps}` : guess ? '클리어 추정' : x.left != null ? `남은 ${x.left}회` : STATE_TEXT[x.state];
  const label = `${x.label} ${text}${x.note ? ` (${x.note})` : ''}`;
  const characterKey = c.isCurrent ? undefined : c.key;
  const onClick = () => {
    if (stepped) { void setHomework(x.toggleId!, { count: nextStep(x.step!, x.steps!), characterKey }); return; }   // 일부만 했을 수 있어 0→1→2→3(완료)→0
    if (!x.boss) { void setHomework(x.toggleId!, { completed: !done, characterKey }); return; }
    if (!done) { onBoss(c); return; }
    // 완료 → 미완료: 넘긴 보스의 체크를 거둔다 (이름을 모르면 여섯 모두)
    const cleared = BOSSES.filter(b => b.title === x.note);
    for (const b of cleared.length ? cleared : BOSSES) void setHomework(b.id, { completed: false, characterKey });
  };
  return (
    <button type="button" className={`hw-cell ${x.state}${guess ? ' guess' : ''} btn-cell`} aria-pressed={done} aria-label={`${label} — 눌러서 ${stepped ? `${nextStep(x.step!, x.steps!)}/${x.steps}회로` : done ? '미완료로' : '완료로'} 바꾸기`} onClick={onClick}>
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
