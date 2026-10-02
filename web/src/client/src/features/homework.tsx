import { useState } from 'react';
import { useHomework, useMissions } from '../api/queries';
import type { HomeworkCard, MissionGroup } from '../api/types';
import { CardHead, Dialog, ErrorCard, Fresh, Icon, Pill, Ring, Skeleton } from '../components/ui';
import { useNow } from '../hooks/layout';
import { resetHomework, setHomework } from '../lib/actions';
import { until } from '../lib/format';
import { useRouter } from '../state/router';

/** 탭 (FR-HW-12). 일일·주간은 주기 기준, 나머지는 분류 기준. */
const TABS: { id: string; label: string; match: (c: HomeworkCard) => boolean }[] = [
  { id: 'all', label: '전체', match: () => true },
  { id: 'daily', label: '일일', match: c => c.period === 'daily' },
  { id: 'weekly', label: '주간', match: c => c.period === 'weekly' },
  { id: 'fieldBoss', label: '필드 보스', match: c => c.category === 'fieldBoss' },
  { id: 'raid', label: '레이드', match: c => c.category === 'raid' },
  { id: 'abyss', label: '어비스', match: c => c.category === 'abyss' },
  { id: 'etc', label: '생활·계정', match: c => ['life', 'account', 'guild', 'goal'].includes(c.category) },
  { id: 'shop', label: '상점·교환', match: c => c.category === 'shop' },
  { id: 'missions', label: '인게임 미션', match: () => false },
];

const STATUS: Record<HomeworkCard['status'], { label: string; tone: 'ok' | 'info' | 'plain' | 'cyan' | 'warn' }> = {
  autoDone: { label: '⚡ 자동 완료', tone: 'ok' },
  manualDone: { label: '✍ 수동 완료', tone: 'ok' },
  poolDone: { label: '택1 완료', tone: 'plain' },
  inProgress: { label: '● 진행 중', tone: 'info' },
  pending: { label: '미완료', tone: 'plain' },
};

/** 스마트 숙제 트래커 (FR-HW-12): 진행 링 + 리셋 카운트다운, 탭, 카드 그리드(카드 전체가 토글), 전체 초기화. */
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
      {q.data && (
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
      {tab === 'missions' ? <MissionsTab /> : q.isPending ? <Skeleton lines={6} /> : q.isError ? <ErrorCard error={q.error} onRetry={() => q.refetch()} /> : (
        <div className="hw-grid">
          {q.data.data.cards.filter((TABS.find(t => t.id === tab) ?? TABS[0]).match).map(c => <HomeworkCardView key={c.id} c={c} />)}
        </div>
      )}
      {confirm && <ResetDialog onClose={() => setConfirm(false)} />}
    </>
  );
}

function HomeworkCardView({ c }: { c: HomeworkCard }) {
  const st = STATUS[c.status];
  const done = c.status === 'autoDone' || c.status === 'manualDone';
  const counter = c.mode === 'manualStepCounter';
  const label = `${c.title} ${st.label}${c.suggestion ? ' · 클리어 추정' : ''}`;

  // 단계 카운터 항목은 +/-로, 나머지는 카드 전체를 한 번 눌러 토글한다 (FR-HW-12)
  const body = (
    <>
      <div className="hw-h">
        <span className="hw-title">{c.title}</span>
        {c.share === 'account' && <Pill>계정</Pill>}
      </div>
      <p className="hw-sub">{c.subtitle}</p>
      <div className="hw-foot">
        <Pill tone={st.tone}>{st.label}</Pill>
        {c.goal > 1 && <span className="num muted">{c.count} / {c.goal}</span>}
        {c.reward && <span className="faint small">{c.reward}</span>}
      </div>
      {c.suggestion && !done && (
        <p className="hw-suggest"><Icon name="search" size={14} />클리어 추정 — 눌러서 확인 <span className="faint">({c.suggestion.item} {c.suggestion.from} → {c.suggestion.to})</span></p>
      )}
      {c.evidence && done && <p className="hw-evidence" title={c.evidence}>근거: {c.evidence}</p>}
      {c.evidence && !done && (c.mode === 'questSuffix' || c.mode === 'questVanish') && <p className="hw-warn small" title={c.evidence}>{c.evidence}</p>}
      {c.needsMeasurement && !done && <p className="faint small" title={c.needsMeasurement}>수동 체크 항목 (자동 판정 실측 전)</p>}
    </>
  );

  if (c.status === 'poolDone') {
    // 같은 풀(필드 보스 택1)의 다른 항목이 완료됐다. 이 카드를 따로 완료로 세지 않는다 (FR-HW-10)
    return <div className="card hw pool">{body}</div>;
  }
  if (counter) {
    return (
      <div className={`card hw ${done ? 'done' : ''}`} role="group" aria-label={label}>
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
      aria-pressed={done} onClick={() => setHomework(c.id, { completed: !done })}>
      {body}
    </button>
  );
}

function ResetDialog({ onClose }: { onClose: () => void }) {
  const [account, setAccount] = useState(false);
  return (
    <Dialog
      title="전체 체크 초기화"
      onClose={onClose}
      actions={<>
        <button type="button" className="btn" onClick={onClose}>취소</button>
        <button type="button" className="btn danger" onClick={async () => { await resetHomework(account ? 'characterAndAccount' : 'character'); onClose(); }}>초기화</button>
      </>}
    >
      <p>현재 캐릭터의 이번 주기 체크를 모두 지웁니다. 되돌릴 수 없습니다.</p>
      <label className="check"><input type="checkbox" checked={account} onChange={e => setAccount(e.target.checked)} /> 계정 공통 항목도 함께 지우기</label>
    </Dialog>
  );
}

/** 인게임 미션 탭 (FR-DT-04): 일일·주간 진행 바, 미완료만(토글로 완료 포함), 모두 완료 배너. */
function MissionsTab() {
  const q = useMissions();
  const [showDone, setShowDone] = useState(false);
  if (q.isPending) return <Skeleton lines={6} />;
  if (q.isError) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  return (
    <>
      <div className="toolbar"><button type="button" className="chip" aria-pressed={showDone} onClick={() => setShowDone(v => !v)}>완료 포함 보기</button></div>
      <div className="two">
        <MissionBlock title="일일 미션" g={q.data.data.daily} showDone={showDone} />
        <MissionBlock title="주간 미션" g={q.data.data.weekly} showDone={showDone} />
      </div>
    </>
  );
}

function MissionBlock({ title, g, showDone }: { title: string; g: MissionGroup; showDone: boolean }) {
  const done = (m: MissionGroup['items'][number]) => m.IsCompleted || (m.GoalCount > 0 && m.CurrentCount >= m.GoalCount);
  const rows = g.items.filter(m => showDone || !done(m));
  return (
    <div className="card">
      <CardHead title={title} right={<span className="num muted">{g.done} / {g.total}</span>} />
      <div className="gauge"><i style={{ width: `${g.total ? (g.done / g.total) * 100 : 0}%`, background: 'var(--accent)' }} /></div>
      {g.total > 0 && g.done === g.total && <p className="banner ok"><Icon name="check" />모두 완료했습니다!</p>}
      {rows.length > 0 && (
        <div className="list mt-10">
          {rows.map(m => (
            <div className="row" key={m.Title}>
              <div className="t"><span className={done(m) ? 'faint' : ''}>{m.Title}</span></div>
              <div className="r">{done(m) ? <Pill tone="ok">완료</Pill> : <span className="num muted">{m.CurrentCount} / {m.GoalCount}</span>}</div>
              <div className="m">{m.Description}</div>
              {!done(m) && <div className="m full"><span className="bar"><i style={{ width: `${m.GoalCount ? (m.CurrentCount / m.GoalCount) * 100 : 0}%` }} /></span></div>}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
