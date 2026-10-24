import { useState } from 'react';
import { useCharacter, useHomework, useInventory, useOverview } from '../api/queries';
import type { CutoffContent, Header, HomeworkCard, InvItem, Life, Nearby, Overview as OverviewData } from '../api/types';
import { CardHead, Delta, Dialog, ErrorCard, Fresh, Gauge, Icon, JobIcon, LevelState, LinkCard, Pill, Ring, Skeleton } from '../components/ui';
import { useLayout, useNow } from '../hooks/layout';
import { collectAll, stopAction } from '../lib/actions';
import { useBusy } from '../lib/busy';
import { fmt, plain, remaining, short, until } from '../lib/format';
import { scoreClass, vitalLevel } from '../lib/score';
import { useDevice } from '../state/device';
import { useRouter } from '../state/router';
import { CUTOFF_STATUS } from './cutoffs';
import { AutoBadge, autoStyle, jobLabel, MissingLine, REL_TAG } from './players';
import { RankPanel } from './rankings';

/** 가공 종류 순서 (서버 WorkKinds.Order와 같다) */
const WORK_KIND_ORDER = ['metal', 'wood', 'leather', 'cloth', 'medicine', 'food', 'other'];

/**
 * 캐릭(옛 개요, FR-OV-01~03): 폭이 일정한 작은 상자를 화면 폭에 맞춰 여러 열로 쌓는다(세로 스크롤만, 가로로 늘어지는 줄·가로 스크롤 없음, 2026-10-22).
 * 상자 순서: 점수 → 가방(늘어난 아이템) → 재화 → 주간 숙제 체크 목록(일일 숙제 상자는 2026-10-22 뺐다) → 서버 랭킹 → 가공 → 주변 → 콘텐츠 추천(어비스·레이드) → 일반 던전 추천(영혼석).
 * 퀵 액션 버튼 4개는 2026-10-22 뺐다(같은 기능은 알림 칩·각 화면에 있다).
 */
export function OverviewView() {
  const q = useOverview();
  const layout = useLayout();
  const glancePref = useDevice(s => s.glance);
  const [peek, setPeek] = useState<'daily' | 'weekly' | null>(null);
  const [glanceOff, setGlanceOff] = useState(false);

  if (q.isPending) return <><Skeleton /><Skeleton /></>;
  if (q.isError && !q.data) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const d = q.data.data;

  if ((layout.narrow && glancePref && !glanceOff) || layout.posture === 'tabletop') {
    return <Glance d={d} onPeek={setPeek} onFull={() => setGlanceOff(true)} tabletop={layout.posture === 'tabletop'} peek={peek} closePeek={() => setPeek(null)} />;
  }

  return (
    <div className="ov">
      <div className="ov-top"><Alerts d={d} /><Fresh at={q.dataUpdatedAt} /></div>
      <div className="ov-grid">
        <ScoresCard h={d.header} />
        <WeightCard h={d.header} />
        <CurrencyCard d={d} />
        <HomeworkChecklist kind="weekly" d={d} />
        <div className="ov-rank"><RankPanel /></div>
        <WorksCard life={d.life} at={q.dataUpdatedAt} />
        <NearbyCard nearby={d.nearby} />
        {d.cutoffs && <CutoffSummary contents={d.cutoffs.contents} />}
        <DungeonPick />
      </div>
    </div>
  );
}

function Alerts({ d }: { d: OverviewData }) {
  const collecting = useBusy('collect');
  const go = useRouter(s => s.go);
  const w = d.header.weight;
  const ready = d.life?.works?.filter(x => x.done) ?? [];
  const dailyLeft = d.homework.daily.total - d.homework.daily.done;
  const chips = [
    w && w.pct >= 95 && (
      <button key="w" type="button" className={`chip ${w.level}`} onClick={() => go('inventory')}><Icon name="scale" />가방 {w.pct.toFixed(1)}% · 다이어트</button>
    ),
    ready.length > 0 && (
      <button key="c" type="button" className="chip ok" disabled={collecting} onClick={() => collectAll(ready.map(x => x.name))} data-needs-conn><Icon name="inbox" />수거 가능 {ready.length}건</button>
    ),
    dailyLeft > 0 && (
      <button key="h" type="button" className="chip info" onClick={() => go('homework')}><Icon name="list" />오늘 숙제 {dailyLeft}개 남음</button>
    ),
  ].filter(Boolean);
  if (!chips.length) return null;
  return <div className="alerts" role="group" aria-label="확인이 필요한 항목">{chips}</div>;
}

/** 점수 (FR-OV-10): 전투력·마도저항을 가장 크게, 그다음 생활력·매력. 색은 게임과 같은 기준(FR-DT-11) */
function ScoresCard({ h }: { h: Header }) {
  const s = h.scores;
  return (
    <LinkCard to="stats" className="scores">
      <CardHead icon="sword" title="점수" />
      <div className="score-main">
        <div><span className="lbl">전투력</span><div className={`big num ${scoreClass('combat', s.combat)}`}>{fmt(s.combat)}</div><Delta value={s.combatDelta} /></div>
        <div><span className="lbl">마도저항</span><div className={`big num ${scoreClass('mdef', s.mdef)}`}>{fmt(s.mdef)}</div><Delta value={s.mdefDelta} /></div>
      </div>
      <div className="score-row">
        <span><Icon name="hammer" size={14} />생활력 <b className="num">{fmt(s.living)}</b></span>
        <span><Icon name="heart" size={14} />매력 <b className="num">{fmt(s.attract)}</b></span>
      </div>
    </LinkCard>
  );
}

/** 가방 무게 + 이번 접속에 가방에서 가장 많이 늘어난 아이템 (다이어트 대상 후보) */
function WeightCard({ h }: { h: Header }) {
  const w = h.weight;
  const inv = useInventory();
  const sat = useCharacter().data?.data.character?.Vitals;
  const satPct = sat?.SatietyMax ? (sat.SatietyValue / sat.SatietyMax) * 100 : 0;
  const grown = merged(inv.data?.data.items ?? [])
    .filter(x => x.location === 'bag' && !x.locked && x.sessionDelta > 0)
    .sort((a, b) => b.sessionDelta - a.sessionDelta || b.count - a.count)
    .slice(0, 5);
  return (
    <LinkCard to="inventory" className="weight">
      <CardHead icon="bag" title="가방 무게" right={w && <Delta value={w.delta} inverse />} />
      {w ? (
        <>
          <div className="big num">{w.pct.toFixed(1)}<span className="muted unit">%</span></div>
          <Gauge pct={w.pct} level={w.level} label="가방 무게" />
          <div className="spread"><LevelState level={w.level} /><span className="faint num">{w.current.toFixed(1)} / {fmt(w.max)}</span></div>
        </>
      ) : <p className="muted">무게 정보가 없습니다.</p>}
      {sat && (
        <div className="sat">
          <div className="spread"><span className="muted small">만복도</span><span className="num small">{fmt(sat.SatietyValue)} / {fmt(sat.SatietyMax)}</span></div>
          <Gauge pct={satPct} level={vitalLevel(satPct)} label="만복도" />
        </div>
      )}
      <div className="grown">
        <div className="grown-h faint small">최근 늘어난 아이템 <span className="weak">· 이번 접속</span></div>
        {grown.length === 0
          ? <p className="faint small m0">{inv.isPending ? '불러오는 중…' : '늘어난 아이템이 없어요.'}</p>
          : <ul className="grown-list">{grown.map(x => (
              <li key={x.name}><span className="nm">{plain(x.name)}</span><span className="num up">+{fmt(x.sessionDelta)}</span><span className="num faint">{fmt(x.count)}개</span></li>
            ))}</ul>}
      </div>
    </LinkCard>
  );
}

/** 같은 이름이 여러 칸에 나뉘어 있으면 합친다 (인벤토리 화면과 같은 규칙: 개수는 합, 변화량은 가장 큰 값) */
function merged(items: InvItem[]): InvItem[] {
  const m = new Map<string, InvItem>();
  for (const i of items) {
    const k = `${i.location}|${i.name}`;
    const e = m.get(k);
    m.set(k, e ? { ...e, count: e.count + i.count, locked: e.locked || i.locked, sessionDelta: Math.max(e.sessionDelta, i.sessionDelta) } : i);
  }
  return [...m.values()];
}

function CurrencyCard({ d }: { d: OverviewData }) {
  const items = d.currencies?.items ?? [];
  const amount = (...names: string[]) => items.find(c => names.includes(c.DisplayName))?.Amount;
  const s = d.header.session;
  return (
    <LinkCard to="currencies" className="gold">
      <CardHead icon="coins" title={<>보유 재화 <span className="faint weak">· 이번 접속</span></>} />
      <div className="big num gold-text">{fmt(amount('골드'))}<span className="unit"> G</span></div>
      <div className="mt-4"><Delta value={s.gold} unit=" G" /></div>
      <div className="mt-8">
        <div className="kv"><span className="muted">데카</span><span className="v num">{fmt(amount('데카'))}</span></div>
        <div className="kv"><span className="muted">M캐시</span><span className="v num">{fmt(amount('M캐시', 'M캐쉬'))}</span></div>
        <div className="kv"><span className="muted">정령의 날개</span><span className="v num">{fmt(amount('정령의 날개'))} <Delta value={s.wings} hideZero /></span></div>
      </div>
    </LinkCard>
  );
}

/** 숙제 체크 목록 (FR-OV-01·02): 일일·주간 상자를 따로 두고 숙제마다 ✓ 표시로 끝낸 것을 보여 준다. 누르면 숙제 화면으로 간다. */
function HomeworkChecklist({ kind, d }: { kind: 'daily' | 'weekly'; d: OverviewData }) {
  const q = useHomework();
  const now = useNow(30_000);
  const p = d.homework[kind];
  const left = p.total - p.done;
  const reset = kind === 'daily' ? d.homework.nextDailyReset : d.homework.nextWeeklyReset;
  const title = kind === 'daily' ? '일일 숙제' : '주간 숙제';
  const cards = q.data?.data.cards.filter(c => c.period === kind) ?? [];
  return (
    <LinkCard to="homework" className={`hwlist ${left === 0 ? 'all-done' : ''}`} label={`${title} ${p.done}/${p.total} 완료`}>
      <CardHead icon="list" title={<>{title} <span className="num faint weak">{p.done}/{p.total}</span></>}
        right={<span className="faint small hw-reset"><Icon name="clock" size={12} /> {until(reset, now)}</span>} />
      {q.isPending ? <p className="faint small m0">불러오는 중…</p> : cards.length === 0 ? <p className="faint small m0">표시할 숙제가 없어요.</p> : (
        <ul className="hw-ck">
          {checklistRows(cards).map(r => (
            <li key={r.key} className={r.cls}>
              <span className="ck" aria-hidden="true">{r.done && <Icon name="check" size={12} />}</span>
              <span className="t">{r.title}{r.done && <span className="sr-only"> 완료</span>}</span>
              <span className="r num faint">{r.right}</span>
            </li>
          ))}
        </ul>
      )}
    </LinkCard>
  );
}

export interface CheckRow { key: string; title: string; done: boolean; cls: string; right: string }

/**
 * 체크 목록의 줄. 택1 풀(필드 보스 여섯)은 한 줄로 묶는다(2026-10-22): 하나만 넘기면 되므로 보스마다 줄을 두면 여섯 줄이 한꺼번에 체크된다.
 * 완료면 넘긴 보스 이름을 오른쪽에 쓰고, 미션 카운터로만 확인돼 이름을 모르면 비워 둔다.
 */
export function checklistRows(cards: HomeworkCard[]): CheckRow[] {
  const rows: CheckRow[] = [];
  const seenPools = new Set<string>();
  for (const c of cards) {
    if (c.pool) {
      if (seenPools.has(c.pool)) continue;
      seenPools.add(c.pool);
      const group = cards.filter(x => x.pool === c.pool);
      const done = group.some(x => x.isDone);
      const picked = group.find(x => x.status === 'autoDone' || x.status === 'manualDone');
      const doing = !done && group.some(x => x.status === 'inProgress');
      rows.push({ key: `pool:${c.pool}`, title: `${c.categoryTitle} (택1)`, done, cls: done ? 'done' : doing ? 'doing' : '', right: done ? picked?.title ?? '' : doing ? '진행 중' : '' });
      continue;
    }
    const guess = c.suggestion?.code === 'missionCleared';
    rows.push({
      key: c.id, title: c.title, done: c.isDone,
      cls: c.isDone ? 'done' : guess ? 'guess' : c.status === 'inProgress' ? 'doing' : '',
      right: c.isDone ? '' : guess ? '클리어 추정' : c.remaining != null ? `남은 ${c.remaining}회` : c.goal > 1 ? `${c.count}/${c.goal}` : c.status === 'inProgress' ? '진행 중' : '',
    });
  }
  return rows;
}

/** 가공 대기열 (FR-OV-12): 가공 종류별로 묶어 건수·수거 가능·가장 먼저 끝나는 시간만 보여 준다. */
function WorksCard({ life, at }: { life: Life | null; at: number }) {
  const now = useNow();
  const base = at;   // 남은 시간은 이 기기가 받은 시각 기준으로 1초씩 줄인다 (기기·서버 시계 차이 영향 없음)
  const works = life?.works ?? [];
  const left = (sec: number) => Math.max(0, sec - Math.floor((now - base) / 1000));
  const groups = WORK_KIND_ORDER.map(kind => {
    const list = works.filter(w => (WORK_KIND_ORDER.includes(w.kind) ? w.kind : 'other') === kind);
    const waiting = list.filter(w => w.done || left(w.remainingSeconds) === 0).length;
    const next = list.filter(w => !(w.done || left(w.remainingSeconds) === 0)).map(w => left(w.remainingSeconds)).sort((a, b) => a - b)[0];
    return { kind, label: list[0]?.kindLabel ?? kind, total: list.length, waiting, next };
  }).filter(g => g.total > 0);
  const ready = groups.reduce((n, g) => n + g.waiting, 0);
  return (
    <LinkCard to="life" className="works">
      <CardHead icon="clock" title="가공 대기열" right={ready > 0 && <Pill tone="ok">수거 {ready}</Pill>} />
      {groups.length === 0 && <p className="muted small">진행 중인 가공이 없습니다.</p>}
      {groups.map(g => (
        <div className="work" key={g.kind}>
          <span className="n">{g.label} <span className="faint num">{g.total}건</span></span>
          {g.waiting > 0 && <Pill tone="ok">수거 {g.waiting}</Pill>}
          {g.next !== undefined && <span className="num muted">{remaining(g.next)}</span>}
        </div>
      ))}
    </LinkCard>
  );
}

function NearbyCard({ nearby }: { nearby: Nearby | null }) {
  const p = nearby?.players ?? [];
  const shown = p.slice(0, 6);
  return (
    <LinkCard to="nearby" className="near">
      <CardHead icon="users" title="주변" right={<span className="num muted">{nearby?.count ?? 0}명</span>} />
      {p.length === 0 && <p className="muted small m0">주변에 플레이어가 없습니다.</p>}
      <ul className="near-list">
        {shown.map((x, i) => (
          <li key={`${x.job}|${x.level}|${x.combatScore}|${i}`} className={`pl ${x.relation}${x.autoSuspect ? ' auto-suspect' : ''}`} style={x.autoSuspect ? autoStyle(x.autoSuspect.intensity) : undefined}>
            <JobIcon job={x.job} size={17} />
            <span className="pl-who">
              <span className="pl-job">{REL_TAG[x.relation] && <span className="pl-tag">{REL_TAG[x.relation]}</span>}{jobLabel(x.job, x.realm, nearby?.myRealm)}{x.autoSuspect && <AutoBadge reason={x.autoSuspect.reason} seconds={x.autoSuspect.seconds} intensity={x.autoSuspect.intensity} />}</span>
              {(x.title || x.inCombat) && <span className="pl-sub small">{x.title}{x.inCombat && <span className="pl-combat">{x.title ? ' · ' : ''}전투 중</span>}</span>}
            </span>
            <span className="nums">
              <span className="num muted">Lv.{x.level}</span>
              <span className={`pl-cs num ${x.isStronger ? 'stronger' : ''}`}>{fmt(x.combatScore)}{x.isStronger && <span className="sr-only"> (나보다 강함)</span>}</span>
            </span>
          </li>
        ))}
      </ul>
      {(nearby?.missing?.length ?? 0) > 0 && (
        <ul className="near-list near-missing" aria-label="한동안 안 보이는 사람">
          {nearby!.missing!.map((m, i) => <li key={`${m.job}|${m.level}|${m.combatScore}|${i}`} className="auto-suspect" style={autoStyle(m.intensity)}><MissingLine m={m} myRealm={nearby?.myRealm} /></li>)}
        </ul>
      )}
      {p.length > shown.length && <div className="faint small mt-4">외 {p.length - shown.length}명</div>}
    </LinkCard>
  );
}

/** 콘텐츠 추천 요약 (FR-CO-07, FR-OV-14): 콘텐츠마다 이름+상태, 추천 난이도를 상태 색으로 크게, 그 아래에 입장 가능 난이도 */
function CutoffSummary({ contents }: { contents: CutoffContent[] }) {
  return (
    <LinkCard to="stats" params={{ tab: 'cutoffs' }} className="cutsum">
      <CardHead icon="trophy" title="어비스/레이드 추천" right={<span className="faint small">전투력·마도저항 기준</span>} />
      <ul className="cut-list">
        {contents.map(c => {
          const st = CUTOFF_STATUS[c.status];
          return (
            <li key={c.id} className={`tone-${st.tone}`}>
              <span className="nm">{c.name}</span>
              <Pill tone={st.tone}>{st.label}</Pill>
              {c.status === 'locked'
                ? <span className="rec locked"><Icon name="lock" size={13} />입장 불가</span>
                : <>
                    <span className="rec"><span className="rec-l">추천</span><b>{c.recommendedTier}</b></span>
                    <span className="tier">입장 가능: <b>{c.maxEntryTier}</b></span>
                  </>}
            </li>
          );
        })}
      </ul>
    </LinkCard>
  );
}

/** 일반 던전별 영혼석: 이 영혼석이 나오는 던전. 순서는 같은 개수일 때의 우선순위이기도 하다. */
const SOULS = [
  { stone: '야생의 영혼석', dungeon: '피오드 1층' },
  { stone: '삼림의 영혼석', dungeon: '피오드 2층' },
  { stone: '공명의 영혼석', dungeon: '룬다 1층' },
  { stone: '파동의 영혼석', dungeon: '룬다 2층' },
];

/**
 * 일반 던전 추천(2026-10-22): 영혼석 4종의 보유 수(가방 + 캐릭터 창고 + 공용 창고 합산)가 가장 적은 던전부터 추천한다.
 * 모든 영혼석을 골고루 얻을 확률을 높이려는 목적이다. 같은 수면 위 목록 순서(피오드 1층 → 룬다 2층).
 */
function DungeonPick() {
  const inv = useInventory();
  const items = inv.data?.data.items ?? [];
  const rows = SOULS.map((x, order) => {
    const mine = items.filter(i => i.name === x.stone);
    return { ...x, order, total: mine.reduce((n, i) => n + i.count, 0) };
  }).sort((a, b) => a.total - b.total || a.order - b.order);
  return (
    <LinkCard to="inventory" params={{ q: '영혼석' }} className="dgpick">
      <CardHead icon="dungeon" title="일반 던전 추천" right={<span className="faint small">영혼석 적은 순</span>} />
      {inv.isPending ? <p className="faint small m0">불러오는 중…</p> : (
        <ol className="dg-list">
          {rows.map((r, i) => (
            <li key={r.stone} className={i === 0 ? 'pick' : ''}>
              <span className="no">{i === 0 ? '추천' : i + 1}</span>
              <span className="nm">{r.dungeon}</span>
              <span className="st">{r.stone} <b className="num">{fmt(r.total)}개</b></span>
            </li>
          ))}
        </ol>
      )}
    </LinkCard>
  );
}

/** 남은 숙제 (FR-OV-02): PC·태블릿은 대화상자, Compact는 하단 시트 모양 */
function HomeworkPeek({ kind, onClose }: { kind: 'daily' | 'weekly'; onClose: () => void }) {
  const q = useHomework();
  const go = useRouter(s => s.go);
  const cards: HomeworkCard[] = q.data?.data.cards.filter(c => c.period === kind && !c.isDone) ?? [];
  const title = kind === 'daily' ? '남은 일일 숙제' : '남은 주간 숙제';
  return (
    <Dialog
      title={title}
      onClose={onClose}
      actions={<>
        <button type="button" className="btn" onClick={() => { onClose(); go('homework'); }}>숙제 화면에서 전체 보기</button>
        <button type="button" className="btn primary" onClick={onClose}>닫기</button>
      </>}
    >
      {q.isPending ? <p className="muted">불러오는 중…</p> : cards.length === 0 ? <p>모두 완료했습니다.</p> : (
        <div className="list">
          {cards.map(c => (
            <div className="row" key={c.id}>
              <div className="t"><span>{c.title}</span>{c.share === 'account' && <Pill>계정</Pill>}</div>
              <div className="r"><span className="num muted">{c.count} / {c.goal}</span></div>
              <div className="m">{c.subtitle}</div>
            </div>
          ))}
        </div>
      )}
    </Dialog>
  );
}

/** 글랜스 모드 (FR-MB-06): 폭 360px 미만 · 테이블톱 자세 */
function Glance({ d, onPeek, onFull, tabletop, peek, closePeek }: {
  d: OverviewData; onPeek: (k: 'daily' | 'weekly') => void; onFull: () => void; tabletop: boolean; peek: 'daily' | 'weekly' | null; closePeek: () => void;
}) {
  const w = d.header.weight;
  return (
    <div className="glance">
      <div className="card"><CardHead icon="sword" title="전투력" right={<Delta value={d.header.scores.combatDelta} hideZero />} /><div className={`big num ${scoreClass('combat', d.header.scores.combat)}`}>{short(d.header.scores.combat)}</div></div>
      {w && <div className="card"><CardHead icon="bag" title="가방" /><div className="big num">{w.pct.toFixed(1)}%</div><Gauge pct={w.pct} level={w.level} label="가방 무게" /></div>}
      <button type="button" className="card link" onClick={() => onPeek('daily')} aria-haspopup="dialog">
        <CardHead title="일일 숙제" /><div className="ring-row"><Ring done={d.homework.daily.done} total={d.homework.daily.total} size={56} label="일일 숙제" /></div>
      </button>
      {!tabletop && <>
        <button type="button" className="stop-btn glance-stop" onClick={stopAction}><Icon name="stop" />긴급 정지</button>
        <button type="button" className="btn" onClick={onFull}>전체 보기</button>
      </>}
      {peek && <HomeworkPeek kind={peek} onClose={closePeek} />}
    </div>
  );
}
