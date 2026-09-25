import { useState } from 'react';
import { useHomework, useOverview } from '../api/queries';
import type { CutoffContent, Header, HomeworkCard, Life, Nearby, Overview as OverviewData } from '../api/types';
import { CardHead, Delta, Dialog, ErrorCard, Fresh, Gauge, Icon, LevelState, LinkCard, Pill, Ring, Skeleton } from '../components/ui';
import { useLayout, useNow } from '../hooks/layout';
import { collectAll, stopAction } from '../lib/actions';
import { fmt, remaining, short, until } from '../lib/format';
import { useDevice } from '../state/device';
import { useRouter } from '../state/router';
import { CUTOFF_STATUS } from './cutoffs';

/** 개요 (FR-OV-01~03): 경고 스트립 → 핵심 수치 → 진행 카드 → 콘텐츠 추천 → 퀵 액션. */
export function OverviewView() {
  const q = useOverview();
  const layout = useLayout();
  const glancePref = useDevice(s => s.glance);
  const [peek, setPeek] = useState<'daily' | 'weekly' | null>(null);
  const [glanceOff, setGlanceOff] = useState(false);

  if (q.isPending) return <><Skeleton /><Skeleton /></>;
  if (q.isError) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const d = q.data.data;

  if ((layout.narrow && glancePref && !glanceOff) || layout.posture === 'tabletop') {
    return <Glance d={d} onPeek={setPeek} onFull={() => setGlanceOff(true)} tabletop={layout.posture === 'tabletop'} peek={peek} closePeek={() => setPeek(null)} />;
  }

  return (
    <>
      <div className="view-head compact-head"><h2 className="sr-only">개요</h2><Fresh at={q.dataUpdatedAt} /></div>
      <Alerts d={d} />
      <section className="g-hero">
        <ScoresCard h={d.header} />
        <WeightCard h={d.header} />
        <CurrencyCard d={d} />
      </section>
      <section className="g-prog">
        <HomeworkRingCard kind="daily" d={d} onOpen={() => setPeek('daily')} />
        <HomeworkRingCard kind="weekly" d={d} onOpen={() => setPeek('weekly')} />
        <WorksCard life={d.life} at={q.dataUpdatedAt} />
        <NearbyCard nearby={d.nearby} />
      </section>
      {d.cutoffs && <CutoffSummary contents={d.cutoffs.contents} />}
      <QuickActions life={d.life} />
      {peek && <HomeworkPeek kind={peek} onClose={() => setPeek(null)} />}
    </>
  );
}

function Alerts({ d }: { d: OverviewData }) {
  const go = useRouter(s => s.go);
  const w = d.header.weight;
  const ready = d.life?.works?.filter(x => x.done) ?? [];
  const dailyLeft = d.homework.daily.total - d.homework.daily.done;
  const chips = [
    w && w.pct >= 95 && (
      <button key="w" type="button" className={`chip ${w.level}`} onClick={() => go('inventory', { loc: 'diet' })}><Icon name="scale" />가방 {w.pct.toFixed(1)}% · 다이어트</button>
    ),
    ready.length > 0 && (
      <button key="c" type="button" className="chip ok" onClick={() => collectAll(ready.map(x => x.name))} data-needs-conn><Icon name="inbox" />수거 가능 {ready.length}건</button>
    ),
    dailyLeft > 0 && (
      <button key="h" type="button" className="chip info" onClick={() => go('homework', { tab: 'daily' })}><Icon name="list" />오늘 숙제 {dailyLeft}개 남음</button>
    ),
  ].filter(Boolean);
  if (!chips.length) return null;
  return <div className="alerts" role="group" aria-label="확인이 필요한 항목">{chips}</div>;
}

/** 4대 점수 (WPF v1.2.0): 전투력·마도저항은 세션 변화량 포함 */
function ScoresCard({ h }: { h: Header }) {
  const s = h.scores;
  return (
    <LinkCard to="stats" className="scores">
      <CardHead icon="sword" title="전투력" right={<Delta value={s.combatDelta} />} />
      <div className="big num">{fmt(s.combat)}</div>
      <div className="score-row">
        <span><Icon name="shield" size={14} />마도저항 <b className="num">{fmt(s.mdef)}</b> <Delta value={s.mdefDelta} hideZero /></span>
        <span><Icon name="hammer" size={14} />생활력 <b className="num">{fmt(s.living)}</b></span>
        <span><Icon name="heart" size={14} />매력 <b className="num">{fmt(s.attract)}</b></span>
      </div>
    </LinkCard>
  );
}

function WeightCard({ h }: { h: Header }) {
  const w = h.weight;
  return (
    <LinkCard to="inventory">
      <CardHead icon="bag" title="가방 무게" right={w && <Delta value={w.delta} inverse />} />
      {w ? (
        <>
          <div className="big num">{w.pct.toFixed(1)}<span className="muted unit">%</span></div>
          <Gauge pct={w.pct} level={w.level} label="가방 무게" />
          <div className="spread"><LevelState level={w.level} /><span className="faint num">{w.current.toFixed(1)} / {fmt(w.max)}</span></div>
        </>
      ) : <p className="muted">무게 정보가 없습니다.</p>}
    </LinkCard>
  );
}

function CurrencyCard({ d }: { d: OverviewData }) {
  const items = d.currencies?.items ?? [];
  const amount = (n: string) => items.find(c => c.DisplayName === n)?.Amount;
  const s = d.header.session;
  return (
    <LinkCard to="currencies" className="gold">
      <CardHead icon="coins" title={<>보유 재화 <span className="faint weak">· 이번 접속</span></>} />
      <div className="gold-body">
        <div>
          <div className="big num gold-text">{fmt(amount('골드'))}<span className="unit"> G</span></div>
          <div className="mt-6"><Delta value={s.gold} unit=" G" /></div>
        </div>
        <div className="mt-10">
          <div className="kv"><span className="muted">정령의 날개</span><span className="v num">{fmt(amount('정령의 날개'))} <Delta value={s.wings} hideZero /></span></div>
          <div className="kv"><span className="muted">냥 토큰</span><span className="v num">{fmt(amount('냥 토큰'))} <Delta value={s.nyang} hideZero /></span></div>
        </div>
      </div>
    </LinkCard>
  );
}

/** 숙제 진행 링 (FR-OV-01·02): 누르면 개요를 벗어나지 않고 남은 숙제를 띄운다. */
function HomeworkRingCard({ kind, d, onOpen }: { kind: 'daily' | 'weekly'; d: OverviewData; onOpen: () => void }) {
  const now = useNow(30_000);
  const p = d.homework[kind];
  const left = p.total - p.done;
  const reset = kind === 'daily' ? d.homework.nextDailyReset : d.homework.nextWeeklyReset;
  const title = kind === 'daily' ? '일일 숙제' : '주간 숙제';
  return (
    <button type="button" className="card link" onClick={onOpen} aria-haspopup="dialog">
      <CardHead title={title} />
      <div className="ring-row">
        <Ring done={p.done} total={p.total} label={title} />
        <div className="muted small">
          {left ? `${left}개 남음` : '모두 완료'}<br />
          <span className="faint"><Icon name="clock" size={13} /> 리셋 {until(reset, now)}</span>
        </div>
      </div>
    </button>
  );
}

function WorksCard({ life, at }: { life: Life | null; at: number }) {
  const now = useNow();
  const base = at;   // 남은 시간은 이 기기가 받은 시각 기준으로 1초씩 줄인다 (기기·서버 시계 차이 영향 없음)
  const works = life?.works ?? [];
  const ready = works.filter(w => w.done).length;
  const left = (s: number) => Math.max(0, s - Math.floor((now - base) / 1000));
  const next = works.filter(w => !w.done).sort((a, b) => a.remainingSeconds - b.remainingSeconds)[0];
  return (
    <LinkCard to="life">
      <CardHead icon="clock" title="가공 대기열" right={ready > 0 && <Pill tone="ok">수거 {ready}</Pill>} />
      {works.length === 0 && <p className="muted small">진행 중인 가공이 없습니다.</p>}
      {works.slice(0, 3).map(w => (
        <div className="work" key={w.facility + w.name}>
          <span className="n">{w.name}</span>
          {w.done || left(w.remainingSeconds) === 0 ? <Pill tone="ok">수거 대기</Pill> : <span className="num muted">{remaining(left(w.remainingSeconds))}</span>}
        </div>
      ))}
      {next && <div className="faint small mt-4">다음 완료 · {next.name}</div>}
    </LinkCard>
  );
}

function NearbyCard({ nearby }: { nearby: Nearby | null }) {
  const p = nearby?.players ?? [];
  const n = (r: string) => p.filter(x => x.relation === r).length;
  return (
    <LinkCard to="nearby">
      <CardHead icon="users" title="주변" />
      <div className="mid num">{nearby?.count ?? 0}<span className="muted unit"> 명</span></div>
      <div className="pills mt-8">
        {n('party') > 0 && <Pill tone="info">파티 {n('party')}</Pill>}
        {n('friend') > 0 && <Pill tone="cyan">친구 {n('friend')}</Pill>}
        {n('guild') > 0 && <Pill tone="ok">길드원 {n('guild')}</Pill>}
        {p.some(x => x.isStronger) && <Pill tone="warn">강한 유저 {p.filter(x => x.isStronger).length}</Pill>}
      </div>
    </LinkCard>
  );
}

/** 콘텐츠 추천 요약 (FR-CO-07): 콘텐츠당 한 줄 */
function CutoffSummary({ contents }: { contents: CutoffContent[] }) {
  return (
    <LinkCard to="stats" params={{ tab: 'cutoffs' }}>
      <CardHead icon="trophy" title="콘텐츠 추천" right={<span className="faint small">전투력·마도저항 기준</span>} />
      <div className="cut-mini">
        {contents.map(c => {
          const st = CUTOFF_STATUS[c.status];
          return (
            <div key={c.id} className="cut-mini-row">
              <span className="nm">{c.name}</span>
              <span className="tier">{c.recommendedTier ?? '입장 불가'}</span>
              <Pill tone={st.tone}>{st.label}</Pill>
            </div>
          );
        })}
      </div>
    </LinkCard>
  );
}

function QuickActions({ life }: { life: Life | null }) {
  const go = useRouter(s => s.go);
  const ready = life?.works?.filter(w => w.done).map(w => w.name) ?? [];
  return (
    <section className="qa" aria-label="퀵 액션">
      <button type="button" onClick={() => go('homework', { tab: 'daily' })}><Icon name="list" /><span><b>일일 숙제 점검</b><span>남은 숙제와 인게임 미션</span></span></button>
      <button type="button" onClick={() => go('inventory', { loc: 'diet' })}><Icon name="scale" /><span><b>가방 다이어트</b><span>이번 접속에 늘어난 잡템 순</span></span></button>
      <button type="button" onClick={() => collectAll(ready)} data-needs-conn><Icon name="inbox" /><span><b>작업대 수거</b><span>{ready.length ? `완료된 가공물 ${ready.length}건` : '완료된 가공물 없음'}</span></span></button>
      <button type="button" className="danger" onClick={stopAction}><Icon name="stop" /><span><b>행동 정지</b><span>채집·이동·자동사냥 중단</span></span></button>
    </section>
  );
}

/** 남은 숙제 (FR-OV-02): PC·태블릿은 대화상자, Compact는 하단 시트 모양 */
function HomeworkPeek({ kind, onClose }: { kind: 'daily' | 'weekly'; onClose: () => void }) {
  const q = useHomework();
  const go = useRouter(s => s.go);
  const cards: HomeworkCard[] = q.data?.data.cards.filter(c => c.period === kind && !c.isDone && c.category !== 'shop') ?? [];
  const title = kind === 'daily' ? '남은 일일 숙제' : '남은 주간 숙제';
  return (
    <Dialog
      title={title}
      onClose={onClose}
      actions={<>
        <button type="button" className="btn" onClick={() => { onClose(); go('homework', { tab: kind }); }}>숙제 화면에서 전체 보기</button>
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
      <div className="card"><CardHead icon="sword" title="전투력" right={<Delta value={d.header.scores.combatDelta} hideZero />} /><div className="big num">{short(d.header.scores.combat)}</div></div>
      {w && <div className="card"><CardHead icon="bag" title="가방" /><div className="big num">{w.pct.toFixed(1)}%</div><Gauge pct={w.pct} level={w.level} label="가방 무게" /></div>}
      <button type="button" className="card link" onClick={() => onPeek('daily')} aria-haspopup="dialog">
        <CardHead title="일일 숙제" /><div className="ring-row"><Ring done={d.homework.daily.done} total={d.homework.daily.total} size={56} label="일일 숙제" /></div>
      </button>
      {!tabletop && <>
        <button type="button" className="stop-btn glance-stop" onClick={stopAction}><Icon name="stop" />긴급 정지</button>
        <button type="button" className="btn" onClick={onFull}>전체 개요 보기</button>
      </>}
      {peek && <HomeworkPeek kind={peek} onClose={closePeek} />}
    </div>
  );
}
