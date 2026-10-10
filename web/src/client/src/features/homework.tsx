import { useState } from 'react';
import { useCharacters, useHomework } from '../api/queries';
import type { AccountGroup, CharacterCard, HomeworkAuto } from '../api/types';
import { Dialog, ErrorCard, Fresh, Icon, JobIcon, Pill, Skeleton } from '../components/ui';
import { useNow } from '../hooks/layout';
import { resetHomework, setHomework } from '../lib/actions';
import { useBusy } from '../lib/busy';
import { until } from '../lib/format';
import { charName } from './characters';

/**
 * 숙제 화면 (2026-10-11 개편): 계정의 캐릭터마다 가로로 긴 박스 한 줄, 그 안에 중요 숙제의 완료 여부.
 * 세로로 쌓아 두어 어떤 캐릭터가 어떤 숙제를 아직 못 했는지 한눈에 본다.
 * 지금 접속한 캐릭터의 칸은 눌러서 수동으로 고칠 수 있다(자동 판정이 놓친 경우). 다른 캐릭터는 마지막으로 본 값이다.
 */
export const HW_GROUPS: { title: string; cells: { id: string; label: string }[] }[] = [
  { title: '일일·주간', cells: [{ id: 'daily_day_dungeon', label: '요일던전' }, { id: 'weekly_guild_regular', label: '길드의뢰' }] },
  { title: '레이드', cells: [{ id: 'raid_cavrak', label: '카브락' }, { id: 'raid_airel', label: '에이렐' }, { id: 'raid_white_succubus', label: '화이트서큐' }] },
  { title: '어비스', cells: [{ id: 'abyss_illusory_anchorage', label: '허상' }, { id: 'abyss_madness_cave', label: '광기' }, { id: 'abyss_scattered_waterway', label: '물길' }] },
];

type State = 'done' | 'todo' | 'unknown';

interface Cell { key: string; label: string; state: State; note?: string; toggleId?: string; boss?: boolean }

/** 한 캐릭터의 칸들: 일반 숙제 8개 + 필드 보스 1개 */
export function cellsOf(c: Pick<CharacterCard, 'homework' | 'fieldBoss'>): Cell[] {
  const byId = new Map<string, HomeworkAuto>(c.homework.map(h => [h.id, h]));
  const cells: Cell[] = HW_GROUPS.flatMap(g => g.cells.map(x => {
    const h = byId.get(x.id);
    return { key: x.id, label: x.label, state: h?.state ?? 'unknown', toggleId: x.id } as Cell;
  }));
  cells.push({ key: 'field_boss', label: '필드보스', state: c.fieldBoss.state, note: c.fieldBoss.item ?? undefined, boss: true });
  return cells;
}

const STATE_TEXT: Record<State, string> = { done: '완료', todo: '미완료', unknown: '미확인' };

export function HomeworkView() {
  const q = useCharacters();
  const hw = useHomework();
  const now = useNow(30_000);
  const [confirm, setConfirm] = useState(false);
  const [bossFor, setBossFor] = useState(false);

  const accounts: AccountGroup[] = q.data ? [...q.data.data.accounts].sort((a, b) => Number(b.hasCurrent) - Number(a.hasCurrent)) : [];
  const total = accounts.reduce((n, a) => n + a.members.length, 0);

  return (
    <>
      <div className="view-head">
        <h2>숙제 <span className="num muted">({total}명)</span></h2>
        <span className="sub"><Fresh at={q.dataUpdatedAt} /> <button type="button" className="btn" onClick={() => setConfirm(true)}><Icon name="reset" />전체 체크 초기화</button></span>
      </div>
      {hw.data && (
        <p className="hw-resets faint small m0">
          <span><Icon name="clock" size={13} /> 일일 리셋 {until(hw.data.data.nextDailyResetUtc, now)}</span>
          <span><Icon name="clock" size={13} /> 주간 리셋 {until(hw.data.data.nextWeeklyResetUtc, now)}</span>
        </p>
      )}
      {q.isPending ? <Skeleton lines={6} /> : q.isError ? <ErrorCard error={q.error} onRetry={() => q.refetch()} /> : total === 0 ? (
        <div className="card muted">아직 확인한 캐릭터가 없습니다. 게임에 접속하면 여기에 모입니다.</div>
      ) : (
        <>
          {accounts.map(a => <AccountBoard key={a.id} a={a} onBoss={() => setBossFor(true)} showName={accounts.length > 1} />)}
          <p className="faint small m0">
            <span className="hw-legend done">✓ 완료</span> <span className="hw-legend todo">○ 미완료(이번 주기에 확인)</span> <span className="hw-legend unknown">· 미확인(아직 확인 전, 미완료로 봅니다)</span><br />
            지금 접속한 캐릭터만 실시간이고, 칸을 눌러 수동으로 고칠 수 있습니다. 다른 캐릭터는 마지막으로 접속했을 때 본 값입니다.
          </p>
        </>
      )}
      {confirm && <ResetDialog onClose={() => setConfirm(false)} />}
      {bossFor && <BossDialog onClose={() => setBossFor(false)} />}
    </>
  );
}

function AccountBoard({ a, onBoss, showName }: { a: AccountGroup; onBoss: () => void; showName: boolean }) {
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

function CharacterRow({ c, onBoss }: { c: CharacterCard; onBoss: () => void }) {
  const cells = cellsOf(c);
  const done = cells.filter(x => x.state === 'done').length;
  const all = done === cells.length;
  const name = charName(c);
  return (
    <div className={`card hw-row ${all ? 'all-done' : ''} ${c.isCurrent ? 'current' : ''}`} role="group" aria-label={`${name} 숙제 ${done}/${cells.length} 완료`}>
      <div className="hw-who">
        <JobIcon job={c.job} size={34} />
        <span className="hw-who-t">
          <span className="hw-who-name">{name}{c.isCurrent && <Pill tone="ok">접속 중</Pill>}</span>
          <span className="small muted">{c.job} Lv.{c.level}</span>
        </span>
        <span className={`hw-count num ${all ? 'ok' : ''}`} aria-hidden="true">{done}/{cells.length}</span>
      </div>
      <div className="hw-cells">
        {HW_GROUPS.map(g => (
          <div className="hw-cg" key={g.title}>
            {cells.filter(x => g.cells.some(y => y.id === x.key)).map(x => <CellView key={x.key} x={x} editable={c.isCurrent} />)}
          </div>
        ))}
        <div className="hw-cg">
          {cells.filter(x => x.boss).map(x => <CellView key={x.key} x={x} editable={c.isCurrent} onBoss={onBoss} />)}
        </div>
      </div>
    </div>
  );
}

function CellView({ x, editable, onBoss }: { x: Cell; editable: boolean; onBoss?: () => void }) {
  const text = STATE_TEXT[x.state];
  const label = `${x.label} ${text}${x.note ? ` (${x.note})` : ''}`;
  const inner = (
    <>
      <span className="hw-cell-name">{x.label}</span>
      <span className="hw-cell-state">{x.state === 'done' ? <Icon name="check" size={13} /> : x.state === 'todo' ? '○' : '·'} {x.note && x.state === 'done' ? x.note : text}</span>
    </>
  );
  if (!editable) return <span className={`hw-cell ${x.state}`} role="img" aria-label={label}>{inner}</span>;
  if (x.boss) return <button type="button" className={`hw-cell ${x.state} btn-cell`} aria-label={`${label} — 눌러서 수정`} onClick={onBoss}>{inner}</button>;
  return (
    <button type="button" className={`hw-cell ${x.state} btn-cell`} aria-pressed={x.state === 'done'} aria-label={label}
      onClick={() => void setHomework(x.toggleId!, { completed: x.state !== 'done' })}>{inner}</button>
  );
}

/** 필드 보스: 여섯 중 하나만 넘기면 된다. 넘긴 보스를 고르거나 완료를 되돌린다 (지금 접속한 캐릭터). */
function BossDialog({ onClose }: { onClose: () => void }) {
  const hw = useHomework();
  const bosses = (hw.data?.data.cards ?? []).filter(c => c.category === 'fieldBoss');
  const cleared = bosses.filter(c => c.status === 'autoDone' || c.status === 'manualDone');
  return (
    <Dialog title="필드 보스 (주 1회, 택1)" onClose={onClose}
      actions={<button type="button" className="btn" onClick={onClose}>닫기</button>}>
      {hw.isPending ? <Skeleton lines={3} /> : (
        <>
          <p className="small muted m0">이번 주에 넘긴 보스를 고르세요. 여섯 중 하나만 넘기면 됩니다.</p>
          <div className="hw-boss-list">
            {bosses.map(b => {
              const on = cleared.some(x => x.id === b.id);
              return (
                <button key={b.id} type="button" className={`btn ${on ? 'primary' : ''}`} aria-pressed={on}
                  onClick={async () => {
                    // 한 번에 하나만: 다른 보스에 체크가 남아 있으면 먼저 거둔다
                    for (const o of cleared) if (o.id !== b.id) await setHomework(o.id, { completed: false });
                    await setHomework(b.id, { completed: !on });
                  }}>{on && <Icon name="check" size={14} />}{b.title}</button>
              );
            })}
          </div>
        </>
      )}
    </Dialog>
  );
}

function ResetDialog({ onClose }: { onClose: () => void }) {
  const resetting = useBusy('hw-reset');
  const [account, setAccount] = useState(false);
  return (
    <Dialog
      title="전체 체크 초기화"
      onClose={onClose}
      actions={<>
        <button type="button" className="btn" onClick={onClose}>취소</button>
        <button type="button" className="btn danger" disabled={resetting} onClick={async () => { await resetHomework(account ? 'characterAndAccount' : 'character'); onClose(); }}>초기화</button>
      </>}
    >
      <p>현재 캐릭터의 이번 주기 체크를 모두 지웁니다. 되돌릴 수 없습니다.</p>
      <label className="check"><input type="checkbox" checked={account} onChange={e => setAccount(e.target.checked)} /> 계정 공통 항목도 함께 지우기</label>
    </Dialog>
  );
}
