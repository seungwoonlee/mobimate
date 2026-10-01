import { useDeferredValue, useState } from 'react';
import { useLife } from '../api/queries';
import type { Gatherable, Work } from '../api/types';
import { CardHead, Dialog, ErrorCard, Fresh, Icon, Pill, Skeleton } from '../components/ui';
import { useNow } from '../hooks/layout';
import { collect, collectAll, setFavorite, startGather } from '../lib/actions';
import { fmt, remaining } from '../lib/format';

const CATS = ['전체', '벌목', '채광', '농축산', '약초', '기타'] as const;
const PRESETS = [5, 50, 100, 200, 300];
/** 채집 목표 수량 기본값 (FR-DT-22) */
export const DEFAULT_GATHER_TARGET = 100;

/** 가공 종류 순서 (서버 WorkKinds.Order와 같다) */
const WORK_KIND_ORDER = ['metal', 'wood', 'leather', 'cloth', 'medicine', 'food', 'other'];

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

/** 가공 대기열을 가공 종류별로 묶는다 (FR-DT-20): 종류마다 카드 하나, 안에 진행 중·수거 대기 목록. */
function Works({ works, at }: { works: Work[]; at: number }) {
  const now = useNow();
  const base = at;   // 이 기기가 받은 시각 기준 (M3)
  const left = (s: number) => Math.max(0, s - Math.floor((now - base) / 1000));
  const isDone = (w: Work) => w.done || left(w.remainingSeconds) === 0;
  const ready = works.filter(isDone).map(w => w.name);
  const groups = WORK_KIND_ORDER.map(kind => [kind, works.filter(w => (WORK_KIND_ORDER.includes(w.kind) ? w.kind : 'other') === kind)] as const).filter(([, list]) => list.length > 0);
  return (
    <div className="card">
      <CardHead icon="clock" title="가공 대기열"
        right={<button type="button" className="btn primary" disabled={!ready.length} onClick={() => collectAll(ready)} data-needs-conn><Icon name="inbox" />완료분 모두 수거{ready.length ? ` (${ready.length})` : ''}</button>} />
      {works.length === 0 ? <p className="muted">진행 중인 가공이 없습니다.</p> : (
        <div className="wk-groups">
          {groups.map(([kind, list]) => {
            const waiting = list.filter(isDone);
            return (
              <section className="wk" key={kind} aria-label={list[0].kindLabel}>
                <div className="wk-h">
                  <h4>{list[0].kindLabel} <span className="faint num">{list.length}건</span></h4>
                  {waiting.length > 0 && <button type="button" className="btn" onClick={() => collectAll(waiting.map(w => w.name))} data-needs-conn>수거 {waiting.length}</button>}
                </div>
                <div className="list">
                  {list.map(w => {
                    const s = left(w.remainingSeconds);
                    const done = isDone(w);
                    return (
                      <div className="row" key={w.facility + w.name}>
                        <div className="t"><span>{w.name}</span></div>
                        <div className="r">
                          {done
                            ? <button type="button" className="btn" onClick={() => collect(w.name)} data-needs-conn>수거</button>
                            : <span className="num strong">{remaining(s)}</span>}
                        </div>
                        <div className="m">{w.facility} · {done ? <Pill tone="ok">수거 대기 ✓</Pill> : <Pill>진행 중</Pill>}</div>
                      </div>
                    );
                  })}
                </div>
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
}

/** 채집 도우미: 즐겨찾기가 맨 위, 줄마다 보유량을 먼저 보여 준다. 목표 수량 기본값은 100개 (FR-DT-21·22). */
function GatherHelper({ items }: { items: Gatherable[] }) {
  const [cat, setCat] = useState<(typeof CATS)[number]>('전체');
  const [text, setText] = useState('');
  const search = useDeferredValue(text.trim());
  const [target, setTarget] = useState(DEFAULT_GATHER_TARGET);
  const [confirm, setConfirm] = useState<{ name: string; need: number } | null>(null);

  const rows = items
    .filter(g => cat === '전체' || gatherCategory(g.name) === cat)
    .filter(g => !search || g.name.includes(search))
    .sort((a, b) => Number(b.favorite) - Number(a.favorite));   // 안정 정렬: 즐겨찾기만 앞으로

  return (
    <div className="card">
      <CardHead icon="leaf" title="스마트 채집 도우미" right={<span className="faint small">시작할 때마다 정령의 날개 5개</span>} />
      {/* 검색창을 분류 칩 줄 맨 앞에 둔다 (v1.1.0, FR-DT-05) */}
      <div className="toolbar mb-10">
        <label className="search grow-0">
          <Icon name="search" />
          <input type="search" placeholder="채집물 검색" value={text} onChange={e => setText(e.target.value)} aria-label="채집물 검색" />
        </label>
        <div className="seg" role="group" aria-label="분류">
          {CATS.map(c => <button key={c} type="button" aria-pressed={cat === c} onClick={() => setCat(c)}>{c}</button>)}
        </div>
      </div>
      <div className="toolbar mb-10">
        <span className="muted small">목표 수량</span>
        <div className="stepper">
          {[-5, -1].map(d => <button key={d} type="button" onClick={() => setTarget(t => Math.max(1, t + d))}>{d}</button>)}
          <span className="v num" aria-live="polite">{target}</span>
          {[1, 5].map(d => <button key={d} type="button" onClick={() => setTarget(t => Math.min(999, t + d))}>+{d}</button>)}
        </div>
        {PRESETS.map(v => <button key={v} type="button" className="chip" aria-pressed={target === v} onClick={() => setTarget(v)}>{v}개</button>)}
      </div>
      {rows.length === 0 ? <p className="muted">조건에 맞는 채집물이 없습니다.</p> : (
        <div className="list gather">
          {rows.map(g => {
            const need = Math.max(0, target - g.inBag);
            return (
              <div className={`row ${g.favorite ? 'fav' : ''}`} key={g.name}>
                <div className="t">
                  <button type="button" className="star" aria-pressed={g.favorite} aria-label={`${g.name} 즐겨찾기`} title={g.favorite ? '즐겨찾기 해제' : '즐겨찾기'}
                    onClick={() => void setFavorite('gather', g.name, !g.favorite)}>{g.favorite ? '★' : '☆'}</button>
                  <span>{g.name}</span>
                </div>
                <div className="r">
                  {!g.toolOk ? <Pill tone="danger">도구 없음</Pill>
                    : need === 0 ? <Pill tone="ok">목표 달성</Pill>
                    : <button type="button" className="btn primary" onClick={() => setConfirm({ name: g.name, need })} data-needs-conn>채집</button>}
                </div>
                <div className="m">
                  <span className="have">보유 <b className="num">{fmt(g.inBag)}</b></span>
                  <span className="num faint">/ 목표 {fmt(target)}</span>
                  {need > 0 && <span className="num warn-text">부족 {fmt(need)}</span>}
                  <Pill>{gatherCategory(g.name)}</Pill>
                </div>
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
            <button type="button" className="btn primary" onClick={() => { void startGather(confirm.name, confirm.need); setConfirm(null); }}>채집 시작</button>
          </>}
        >
          <p>목표 {fmt(target)}개 · 부족한 <b>{fmt(confirm.need)}개</b>를 채집합니다.</p>
          <p className="warn-text">정령의 날개 5개를 씁니다. 채집 중에도 긴급 정지와 다른 조회는 그대로 쓸 수 있습니다.</p>
        </Dialog>
      )}
    </div>
  );
}
