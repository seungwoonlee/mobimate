import { useDeferredValue, useEffect, useState } from 'react';
import { useLife } from '../api/queries';
import type { Gatherable, Work } from '../api/types';
import { CardHead, Dialog, ErrorCard, Fresh, Icon, Pill, Skeleton } from '../components/ui';
import { useNow } from '../hooks/layout';
import { collect, collectAll, setFavorite, startGather } from '../lib/actions';
import { useBusy } from '../lib/busy';
import { finishClock, fmt, remaining } from '../lib/format';
export { finishClock };
import { useRouter } from '../state/router';
import { useUi } from '../state/ui';

const CATS = ['전체', '벌목', '채광', '농축산', '약초', '기타'] as const;
/** 게임은 채집 한 번에 최대 100개만 캔다. 더 큰 목표는 서버가 100개씩 자리 비움 없이 이어서 채집한다. 호출마다 정령의 날개 5개. */
export const GATHER_BATCH = 100;
export const WINGS_PER_BATCH = 5;
export const MAX_GATHER_TARGET = 5000;
export const gatherBatches = (need: number) => Math.ceil(need / GATHER_BATCH);
/** 채집 목표 수량 기본값 (FR-DT-22) */
export const DEFAULT_GATHER_TARGET = 100;

/** 가공 종류 순서 (서버 WorkKinds.Order와 같다) */
const WORK_KIND_ORDER = ['metal', 'wood', 'leather', 'cloth', 'medicine', 'food', 'other'];
/** 가공 종류 이름 (서버 work_categories.json의 labels와 같다). 비어 있는 종류도 카드로 보여 주려고 클라이언트에도 둔다. */
const WORK_KIND_LABEL: Record<string, string> = { metal: '금속 가공', wood: '목재 가공', leather: '가죽 가공', cloth: '옷감 가공', medicine: '약품 가공', food: '식재료 가공', other: '기타 가공' };
/**
 * 열어 둔 가공 칸 수는 사람마다 다르고(2~7칸) 게임 CLI가 알려 주지 않는다. 대신 종류별로 지금까지 본 최대 동시 건수를
 * 칸 수로 쓴다(칸을 다 채운 적이 있으면 정확하다). 이 기기에 기억한다.
 */
const SLOT_KEY = 'mm.workSlotsSeen';
function readSeen(): Record<string, number> {
  try { return JSON.parse(localStorage.getItem(SLOT_KEY) ?? '{}') as Record<string, number>; } catch { return {}; }
}
function useSeenSlots(counts: Record<string, number>): Record<string, number> {
  const [seen, setSeen] = useState(readSeen);
  const sig = Object.entries(counts).map(([k, v]) => `${k}:${v}`).join(',');
  useEffect(() => {
    setSeen(prev => {
      let changed = false;
      const next = { ...prev };
      for (const [k, v] of Object.entries(counts)) if (v > (next[k] ?? 0)) { next[k] = v; changed = true; }
      if (!changed) return prev;
      try { localStorage.setItem(SLOT_KEY, JSON.stringify(next)); } catch { /* 저장 못 해도 이번 화면에서는 동작한다 */ }
      return next;
    });
  }, [sig]); // eslint-disable-line react-hooks/exhaustive-deps
  return seen;
}

/**
 * 한 종류의 가공은 앞에서부터 차례로 끝난다. 줄마다 "그 품목이 나오기까지" 걸리는 시간을 보이려고
 * 아직 안 끝난 줄의 남은 시간을 앞에서부터 누적한다. 수거 대기(끝난) 줄은 0이다.
 */
export function cumulativeSeconds(remaining: number[], done: boolean[]): number[] {
  let sum = 0;
  return remaining.map((r, i) => (done[i] ? 0 : (sum += r)));
}

/** 채집물 분류: WPF판과 같은 이름 규칙 (ClassifyGatherCategory). */
export function gatherCategory(name: string): string {
  const has = (...k: string[]) => k.some(x => name.includes(x));
  if (has('장작', '나무', '가지', '통나무')) return '벌목';
  if (has('광석', '철', '구리', '은', '금', '보석', '석영', '유황', '돌멩이')) return '채광';
  if (has('사과', '달걀', '양털', '우유', '감자', '옥수수', '보리', '밀')) return '농축산';
  if (has('초', '풀', '꽃', '버섯', '클로버')) return '약초';
  return '기타';
}

/** 생활 (FR-DT-05·07·20~22): 가공 대기열(종류별 묶음·1초 카운트다운·개별/일괄 수거) + 스마트 채집 도우미(즐겨찾기·보유량 먼저). */
export function LifeView() {
  const q = useLife();
  if (q.isPending) return <Skeleton lines={6} />;
  if (q.isError) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const { works, gatherables } = q.data.data;
  return (
    <>
      <div className="view-head">
        <h2>생활</h2>
        <span className="sub">가공 {works?.length ?? 0}건 · 채집 가능 {gatherables?.length ?? 0}종 · <Fresh at={q.dataUpdatedAt} /></span>
      </div>
      <Works works={works ?? []} at={q.dataUpdatedAt} />
      <GatherHelper items={gatherables ?? []} />
    </>
  );
}

/** 가공 대기열을 가공 종류별로 묶는다 (FR-DT-20): 종류마다 카드 하나, 안에 진행 중·수거 대기 목록(항목은 2줄). */
function Works({ works, at }: { works: Work[]; at: number }) {
  const collecting = useBusy('collect');
  const now = useNow();
  const base = at;   // 이 기기가 받은 시각 기준 (M3)
  const left = (s: number) => Math.max(0, s - Math.floor((now - base) / 1000));
  const isDone = (w: Work) => w.done || left(w.remainingSeconds) === 0;
  const ready = works.filter(isDone).map(w => w.name);
  // 여섯 가지 가공은 비어 있어도 항상 보여 주고(칸 사용량 확인용), 기타 가공은 있을 때만 보여 준다.
  const groups = WORK_KIND_ORDER
    .map(kind => [kind, works.filter(w => (WORK_KIND_ORDER.includes(w.kind) ? w.kind : 'other') === kind)] as const)
    .filter(([kind, list]) => kind !== 'other' || list.length > 0);
  const seen = useSeenSlots(Object.fromEntries(groups.map(([k, l]) => [k, l.length])));
  return (
    <div className="card">
      <CardHead icon="clock" title="가공 대기열"
        right={<button type="button" className="btn primary" disabled={!ready.length || collecting} onClick={() => collectAll(ready)} data-needs-conn><Icon name="inbox" />완료분 모두 수거{ready.length ? ` (${ready.length})` : ''}</button>} />
      <div className="wk-groups">
        {groups.map(([kind, list]) => {
          const waiting = list.filter(isDone);
          // 수거 대기를 위로, 진행 중은 게임이 준 순서(= 끝나는 순서) 그대로
          const rows = [...list.filter(isDone), ...list.filter(w => !isDone(w))];
          const cum = cumulativeSeconds(rows.map(w => left(w.remainingSeconds)), rows.map(isDone));
          const firstActive = rows.findIndex(w => !isDone(w));
          const label = WORK_KIND_LABEL[kind] ?? list[0]?.kindLabel ?? kind;
          const max = Math.max(seen[kind] ?? 0, list.length);
          return (
            <section className={`wk ${list.length === 0 ? 'empty' : ''}`} key={kind} aria-label={label}>
              <div className="wk-h">
                <h4>{label} <span className="faint num" title="칸 수는 지금까지 본 최대 동시 건수입니다">{max > list.length ? `${list.length}/${max}칸` : `${list.length}건`}</span></h4>
                {waiting.length > 0 && <button type="button" className="btn" disabled={collecting} onClick={() => collectAll(waiting.map(w => w.name))} data-needs-conn>수거 {waiting.length}</button>}
              </div>
              {list.length === 0 ? <p className="muted small wk-empty">비어 있습니다.</p> : (
                <div className="wk-list">
                  {rows.map((w, i) => {
                    const done = isDone(w);
                    return (
                      <div className="wk-row" key={w.facility + w.name + i}>
                        <span className="n">{w.name}</span>
                        {done
                          ? <button type="button" className="btn wk-take" disabled={collecting} onClick={() => collect(w.name)} data-needs-conn>수거</button>
                          : <b className="tm num" aria-label={`완료까지 ${remaining(cum[i])}`}>{remaining(cum[i])}</b>}
                        <span className="s">{done ? <Pill tone="ok">수거 대기 ✓</Pill> : <Pill>{i === firstActive ? '진행 중' : '대기'}</Pill>}</span>
                        {!done && <small className="at num">{finishClock(now, cum[i])} 완료</small>}
                      </div>
                    );
                  })}
                </div>
              )}
            </section>
          );
        })}
      </div>
    </div>
  );
}

/** 채집 도우미: 즐겨찾기가 맨 위, 줄마다 보유량을 먼저 보여 준다. 목표 수량 기본값은 100개 (FR-DT-21·22). */
function GatherHelper({ items }: { items: Gatherable[] }) {
  const busy = useBusy('gather');   // 훅은 항상 같은 순서로 호출해야 하므로 ||로 잇지 않는다
  const jobRunning = useUi(s => s.gatherJob?.state === 'running');
  const gathering = busy || jobRunning;
  const [cat, setCat] = useState<(typeof CATS)[number]>('전체');
  const [text, setText] = useState('');
  const search = useDeferredValue(text.trim());
  const [target, setTarget] = useState(DEFAULT_GATHER_TARGET);
  const [confirm, setConfirm] = useState<{ name: string; need: number } | null>(null);
  const wanted = useRouter(s => s.loc.params.get('gather'));
  const setParam = useRouter(s => s.setParam);

  // 빠른 실행(Ctrl+K)에서 채집 재료를 고르면 이 화면으로 와서 바로 확인 창을 연다 (FR-AC-04)
  useEffect(() => {
    if (!wanted) return;
    const g = items.find(x => x.name === wanted);
    if (g && g.toolOk) setConfirm({ name: g.name, need: Math.max(1, target - g.inBag) });
    setParam('gather', null);
  }, [wanted]); // eslint-disable-line react-hooks/exhaustive-deps

  const rows = items
    .filter(g => cat === '전체' || gatherCategory(g.name) === cat)
    .filter(g => !search || g.name.includes(search))
    .sort((a, b) => Number(b.favorite) - Number(a.favorite) || b.inBag - a.inBag);   // 즐겨찾기 먼저, 그다음 보유 수량이 많은 순

  return (
    <div className="card">
      <CardHead icon="leaf" title="스마트 채집 도우미" right={<span className="faint small">100개마다 정령의 날개 5개</span>} />
      {/* 검색창을 분류 칩 줄 맨 앞에 둔다 (v1.1.0, FR-DT-05) */}
      <div className="toolbar gx-bar">
        <label className="search grow-0">
          <Icon name="search" />
          <input type="search" placeholder="채집물 검색" value={text} onChange={e => setText(e.target.value)} aria-label="채집물 검색" />
        </label>
        <div className="seg" role="group" aria-label="분류">
          {CATS.map(c => <button key={c} type="button" aria-pressed={cat === c} onClick={() => setCat(c)}>{c}</button>)}
        </div>
        <span className="muted small">목표 수량</span>
        <div className="stepper">
          <button type="button" onClick={() => setTarget(t => Math.max(GATHER_BATCH, t - GATHER_BATCH))} aria-label="100 줄이기">-100</button>
          <span className="v num" aria-live="polite">{target}</span>
          <button type="button" onClick={() => setTarget(t => Math.min(MAX_GATHER_TARGET, t + GATHER_BATCH))} aria-label="100 늘리기">+100</button>
        </div>
      </div>
      {rows.length === 0 ? <p className="muted">조건에 맞는 채집물이 없습니다.</p> : (
        <div className="gx-list">
          {rows.map(g => {
            const need = Math.max(0, target - g.inBag);
            return (
              <div className={`gx-row ${g.favorite ? 'fav' : ''}`} key={g.name}>
                <button type="button" className="star" aria-pressed={g.favorite} aria-label={`${g.name} 즐겨찾기`} title={g.favorite ? '즐겨찾기 해제' : '즐겨찾기'}
                  onClick={() => void setFavorite('gather', g.name, !g.favorite)}>{g.favorite ? '★' : '☆'}</button>
                <span className="n">{g.name}</span>
                <span className="m">
                  <span className="have">보유 <b className="num">{fmt(g.inBag)}</b></span>
                  {need > 0 && <span className="num warn-text">부족 {fmt(need)}</span>}
                </span>
                {!g.toolOk ? <Pill tone="danger">도구 없음</Pill>
                  : need === 0 ? <Pill tone="ok">목표 달성</Pill>
                  : <button type="button" className="btn primary" onClick={() => setConfirm({ name: g.name, need })} data-needs-conn>채집</button>}
              </div>
            );
          })}
        </div>
      )}
      {confirm && (
        <Dialog
          title={`${confirm.name} 채집`}
          onClose={() => setConfirm(null)}
          actions={<>
            <button type="button" className="btn" onClick={() => setConfirm(null)}>취소</button>
            <button type="button" className="btn primary" disabled={gathering} onClick={() => { void startGather(confirm.name, confirm.need); setConfirm(null); }}>채집 시작</button>
          </>}
        >
          <p>목표 {fmt(target)}개 · 부족한 <b>{fmt(confirm.need)}개</b>를 채집합니다.</p>
          <p className="warn-text">
            정령의 날개 {fmt(gatherBatches(confirm.need) * WINGS_PER_BATCH)}개를 씁니다 ({gatherBatches(confirm.need)}회 × {WINGS_PER_BATCH}개).
            {gatherBatches(confirm.need) > 1 && <> 게임은 한 번에 {GATHER_BATCH}개씩만 캐므로, 끝나면 자리를 비워도 알아서 이어서 채집합니다. 100개 단위라 목표보다 조금 더 캘 수 있습니다.</>}
            {' '}채집 중에도 긴급 정지와 다른 조회는 그대로 쓸 수 있습니다.
          </p>
        </Dialog>
      )}
    </div>
  );
}
