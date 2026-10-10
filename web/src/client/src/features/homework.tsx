import { useState } from 'react';
import { useHomework } from '../api/queries';
import type { HomeworkBoard, HomeworkCard } from '../api/types';
import { CardHead, Dialog, ErrorCard, Fresh, Icon, Pill, Ring, Skeleton } from '../components/ui';
import { useNow } from '../hooks/layout';
import { resetHomework, setHomework } from '../lib/actions';
import { useBusy } from '../lib/busy';
import { until } from '../lib/format';
import { useRouter } from '../state/router';

/** 탭 (FR-HW-12). 일일·주간은 주기 기준, 나머지는 분류 기준. 상점·교환과 인게임 미션 탭은 없앴다(2026-10-11). */
const TABS: { id: string; label: string; match: (c: HomeworkCard) => boolean }[] = [
  { id: 'all', label: '전체', match: () => true },
  { id: 'daily', label: '일일', match: c => c.period === 'daily' },
  { id: 'weekly', label: '주간', match: c => c.period === 'weekly' },
  { id: 'fieldBoss', label: '필드 보스', match: c => c.category === 'fieldBoss' },
  { id: 'raid', label: '레이드', match: c => c.category === 'raid' },
  { id: 'abyss', label: '어비스', match: c => c.category === 'abyss' },
  { id: 'etc', label: '생활·계정', match: c => ['life', 'account', 'guild', 'goal'].includes(c.category) },
];

/** 한눈에 볼 중요 숙제 (2026-10-11 승운 지정). 필드 보스는 여섯 중 하나만 넘기면 되므로 따로 묶어 넘긴 보스 이름을 보여 준다. */
const KEY_GROUPS: { title: string; items: { id: string; label: string }[] }[] = [
  { title: '일일·주간', items: [{ id: 'daily_day_dungeon', label: '요일던전' }, { id: 'weekly_guild_regular', label: '길드의뢰' }] },
  { title: '레이드', items: [{ id: 'raid_cavrak', label: '카브락' }, { id: 'raid_airel', label: '에이렐' }, { id: 'raid_white_succubus', label: '화이트서큐' }] },
  { title: '어비스', items: [{ id: 'abyss_illusory_anchorage', label: '허상' }, { id: 'abyss_madness_cave', label: '광기' }, { id: 'abyss_scattered_waterway', label: '물길' }] },
];

/** 분류 막대의 순서 */
const CATEGORY_ORDER = ['daily', 'weekly', 'goal', 'raid', 'abyss', 'fieldBoss', 'guild', 'life', 'account'];

const STATUS: Record<HomeworkCard['status'], { label: string; tone: 'ok' | 'info' | 'plain' | 'cyan' | 'warn' }> = {
  autoDone: { label: '⚡ 자동 완료', tone: 'ok' },
  manualDone: { label: '✍ 수동 완료', tone: 'ok' },
  poolDone: { label: '택1 완료', tone: 'plain' },
  inProgress: { label: '● 진행 중', tone: 'info' },
  pending: { label: '미완료', tone: 'plain' },
};

const isDoneStatus = (c: HomeworkCard) => c.status === 'autoDone' || c.status === 'manualDone';

/** 진척 단위: 같은 풀(필드 보스 택1)은 1건으로 센다 (서버 통계와 같은 규칙). */
function progressUnits(cards: HomeworkCard[]) {
  const singles = cards.filter(c => c.pool == null).map(c => ({ category: c.category, title: c.categoryTitle, done: c.isDone }));
  const pools = new Map<string, HomeworkCard[]>();
  for (const c of cards) if (c.pool != null) pools.set(c.pool, [...(pools.get(c.pool) ?? []), c]);
  const pooled = [...pools.values()].map(g => ({ category: g[0].category, title: g[0].categoryTitle, done: g.some(c => c.isDone) }));
  return [...singles, ...pooled];
}

/** 카드 설명은 주 화면에 두지 않고 마우스를 올리거나 키보드로 포커스하면 말풍선으로 보인다. */
function tipOf(c: HomeworkCard, done: boolean): string {
  const parts = [c.subtitle];
  if (c.evidence && (done || c.mode === 'questSuffix' || c.mode === 'questVanish')) parts.push(done ? `근거: ${c.evidence}` : c.evidence);
  if (c.needsMeasurement && !done) parts.push('수동 체크 항목 (자동 판정 실측 전)');
  return parts.join('\n');
}

/** 스마트 숙제 트래커 (FR-HW-12): 첫 화면 요약, 탭, 카드 그리드(카드 전체가 토글), 전체 초기화. */
export function HomeworkView() {
  const q = useHomework();
  const tab = useRouter(s => s.loc.params.get('tab')) ?? 'all';
  const setParam = useRouter(s => s.setParam);
  const now = useNow(30_000);
  const [confirm, setConfirm] = useState(false);

  return (
    <>
      <div className="view-head">
        <h2>숙제</h2>
        <span className="sub"><Fresh at={q.dataUpdatedAt} /> <button type="button" className="btn" onClick={() => setConfirm(true)}><Icon name="reset" />전체 체크 초기화</button></span>
      </div>
      {q.data && tab === 'all' && <Dashboard board={q.data.data} now={now} />}
      {q.data && tab !== 'all' && (
        <div className="hw-top">
          <div className="card ring-row"><Ring done={q.data.data.daily.done} total={q.data.data.daily.total} label="일일 숙제" /><div><b>일일</b><div className="faint small"><Icon name="clock" size={13} /> 리셋 {until(q.data.data.nextDailyResetUtc, now)}</div></div></div>
          <div className="card ring-row"><Ring done={q.data.data.weekly.done} total={q.data.data.weekly.total} label="주간 숙제" /><div><b>주간</b><div className="faint small"><Icon name="clock" size={13} /> 리셋 {until(q.data.data.nextWeeklyResetUtc, now)}</div></div></div>
        </div>
      )}
      <div className="seg tabs-seg" role="tablist" aria-label="숙제 분류">
        {TABS.map(t => (
          <button key={t.id} type="button" role="tab" aria-selected={tab === t.id} onClick={() => setParam('tab', t.id === 'all' ? null : t.id)}>{t.label}</button>
        ))}
      </div>
      {q.isPending ? <Skeleton lines={6} /> : q.isError ? <ErrorCard error={q.error} onRetry={() => q.refetch()} /> : (
        <div className="hw-grid">
          {q.data.data.cards.filter((TABS.find(t => t.id === tab) ?? TABS[0]).match).map(c => <HomeworkCardView key={c.id} c={c} />)}
        </div>
      )}
      {confirm && <ResetDialog onClose={() => setConfirm(false)} />}
    </>
  );
}

/** 첫 화면 요약 (2026-10-11): 전체 진척 링 + 분류별 막대 그래프 + 중요 숙제 한눈에. */
function Dashboard({ board, now }: { board: HomeworkBoard; now: number }) {
  const units = progressUnits(board.cards);
  const done = units.filter(u => u.done).length;
  const total = units.length;
  const pct = total ? Math.round((done / total) * 100) : 0;

  const byCat = new Map<string, { title: string; done: number; total: number }>();
  for (const u of units) {
    const e = byCat.get(u.category) ?? { title: u.title, done: 0, total: 0 };
    e.total += 1;
    if (u.done) e.done += 1;
    byCat.set(u.category, e);
  }
  const cats = [...byCat.entries()].sort((a, b) => {
    const ia = CATEGORY_ORDER.indexOf(a[0]), ib = CATEGORY_ORDER.indexOf(b[0]);
    return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib);
  });

  const byId = new Map(board.cards.map(c => [c.id, c]));
  const bosses = board.cards.filter(c => c.category === 'fieldBoss');
  const bossCleared = bosses.find(isDoneStatus);
  const bossDone = bossCleared != null || bosses.some(c => c.isDone);
  const keyCards = KEY_GROUPS.flatMap(g => g.items).map(i => byId.get(i.id)).filter((c): c is HomeworkCard => c != null);
  const keyTotal = keyCards.length + (bosses.length > 0 ? 1 : 0);
  const keyDone = keyCards.filter(c => c.isDone).length + (bossDone ? 1 : 0);

  return (
    <div className="hw-dash">
      <div className="card hw-overall">
        <CardHead title="전체 진척" right={<span className="num muted">{done} / {total}</span>} />
        <div className="hw-overall-body">
          <div className="hw-bigring">
            <Ring done={done} total={total} size={92} label="전체 숙제" />
            <b className="num" aria-hidden="true">{pct}%</b>
          </div>
          <div className="hw-bars">
            {cats.map(([key, v]) => (
              <div className="hw-bar-row" key={key}>
                <span className="hw-bar-name">{v.title}</span>
                <span className={`hw-bar ${v.done === v.total ? 'full' : ''}`} role="meter" aria-label={`${v.title} ${v.done}/${v.total}`} aria-valuemin={0} aria-valuemax={v.total} aria-valuenow={v.done}>
                  <i style={{ width: `${v.total ? (v.done / v.total) * 100 : 0}%` }} />
                </span>
                <span className="num hw-bar-num">{v.done}/{v.total}</span>
              </div>
            ))}
          </div>
        </div>
        <div className="hw-resets faint small">
          <span><Icon name="clock" size={13} /> 일일 {board.daily.done}/{board.daily.total} · 리셋 {until(board.nextDailyResetUtc, now)}</span>
          <span><Icon name="clock" size={13} /> 주간 {board.weekly.done}/{board.weekly.total} · 리셋 {until(board.nextWeeklyResetUtc, now)}</span>
        </div>
      </div>

      <div className="card hw-key">
        <CardHead title="중요 숙제" right={<span className="num muted">{keyDone} / {keyTotal}</span>} />
        <div className="hw-key-groups">
          {KEY_GROUPS.map(g => (
            <div className="hw-key-group" key={g.title}>
              <span className="hw-key-title">{g.title}</span>
              <div className="hw-key-tiles">
                {g.items.map(i => {
                  const c = byId.get(i.id);
                  if (!c) return null;
                  return <KeyTile key={i.id} label={i.label} state={keyState(c)} note={c.remaining != null && !c.isDone ? `남은 ${c.remaining}회` : undefined} />;
                })}
              </div>
            </div>
          ))}
          {bosses.length > 0 && (
            <div className="hw-key-group">
              <span className="hw-key-title">필드 보스 (택1)</span>
              <div className="hw-key-tiles">
                <KeyTile label="필드보스" state={bossDone ? 'done' : bosses.some(c => c.status === 'inProgress') ? 'progress' : 'todo'} note={bossCleared?.title} />
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

type KeyState = 'done' | 'progress' | 'todo';
const keyState = (c: HomeworkCard): KeyState => (c.isDone ? 'done' : c.status === 'inProgress' || c.suggestion ? 'progress' : 'todo');

function KeyTile({ label, state, note }: { label: string; state: KeyState; note?: string }) {
  const text = state === 'done' ? '완료' : state === 'progress' ? '진행 중' : '미완료';
  return (
    <div className={`hw-key-tile ${state}`} role="group" aria-label={`${label} ${text}${note ? ` ${note}` : ''}`}>
      <span className="hw-key-name">{label}</span>
      <span className="hw-key-state">{state === 'done' ? <Icon name="check" size={14} /> : state === 'progress' ? '●' : '○'} {text}</span>
      {note && <span className="hw-key-note">{note}</span>}
    </div>
  );
}

function HomeworkCardView({ c }: { c: HomeworkCard }) {
  const st = STATUS[c.status];
  const done = c.status === 'autoDone' || c.status === 'manualDone';
  const counter = c.mode === 'manualStepCounter';
  const label = `${c.title} ${st.label}${c.suggestion ? ' · 클리어 추정' : ''}`;
  const tip = tipOf(c, done);

  // 단계 카운터 항목은 +/-로, 나머지는 카드 전체를 한 번 눌러 토글한다 (FR-HW-12)
  const body = (
    <>
      <div className="hw-h">
        <span className="hw-title">{c.title}</span>
        {c.share === 'account' && <Pill>계정</Pill>}
      </div>
      <div className="hw-foot">
        <Pill tone={st.tone}>{st.label}</Pill>
        {c.goal > 1 && <span className="num muted">{c.count} / {c.goal}</span>}
        {c.remaining != null && !done && <Pill tone="warn">남은 {c.remaining}회</Pill>}
        {c.reward && <span className="faint small">{c.reward}</span>}
      </div>
      {c.suggestion && !done && (
        <p className="hw-suggest"><Icon name="search" size={14} />클리어 추정 — 눌러서 확인 <span className="faint">({c.suggestion.code === 'raidMapEntered' ? `${c.suggestion.item} 진입` : `${c.suggestion.item} ${c.suggestion.from} → ${c.suggestion.to}`})</span></p>
      )}
    </>
  );

  if (c.status === 'poolDone') {
    // 같은 풀(필드 보스 택1)의 다른 항목이 완료됐다. 이 카드를 따로 완료로 세지 않는다 (FR-HW-10)
    return <div className="card hw pool" data-tip={tip}>{body}</div>;
  }
  if (counter) {
    return (
      <div className={`card hw ${done ? 'done' : ''}`} role="group" aria-label={label} data-tip={tip}>
        {body}
        <div className="stepper mt-8" role="group" aria-label={`${c.title} 단계`}>
          <button type="button" onClick={() => setHomework(c.id, { count: Math.max(0, c.count - 1) })} disabled={c.count <= 0} aria-label="한 단계 빼기"><Icon name="minus" size={16} /></button>
          <span className="v num">{c.count}</span>
          <button type="button" onClick={() => setHomework(c.id, { count: Math.min(c.goal, c.count + 1) })} disabled={c.count >= c.goal} aria-label="한 단계 더하기"><Icon name="plus" size={16} /></button>
        </div>
      </div>
    );
  }
  return (
    <button type="button" className={`card hw link ${done ? 'done' : ''} ${c.suggestion && !done ? 'suggest' : ''}`}
      aria-pressed={done} data-tip={tip} onClick={() => setHomework(c.id, { completed: !done })}>
      {body}
    </button>
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
