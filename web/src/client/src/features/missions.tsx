import { useDeferredValue, useMemo, useState } from 'react';
import { useInventory } from '../api/queries';
import type { InvItem } from '../api/types';
import { ErrorCard, Fresh, Icon, Pill, Skeleton } from '../components/ui';
import { buildStock, craftableNow, planTargets } from '../lib/craft';
import { fmt, plain } from '../lib/format';
import { useRouter } from '../state/router';
import { groupItems, ItemList, SORT_FN, type InvGroup } from './inventory';
import { isMissionItem, MISSION_SLOTS, missionInfo, RECIPES, TOWNS } from './missionData';

type Mode = 'gather' | 'cook' | 'craft';
const MODES: [Mode, string][] = [['gather', '채집'], ['cook', '요리'], ['craft', '제작']];
const GATHER_SORTS: [string, string][] = [['table', '마을 목록 순'], ['count', '개수 많은 순'], ['countAsc', '개수 적은 순'], ['name', '이름순'], ['delta', '이번 접속 늘어난 순'], ['deltaAsc', '이번 접속 줄어든 순']];

/**
 * 임무 탭 (2026-10-24): 마을별 임무 준비를 돕는 세 가지 보기.
 * - 채집: 채집 임무용 아이템 20종을 가진 개수가 0이어도 모두 보여 준다.
 * - 요리: 준비 중(CLI는 요리 레시피를 `get_craftable_items`로 줄 수 있어 나중에 붙인다).
 * - 제작: 가진 재료로 몇 개 만들 수 있는지, 목표(퀘스트) 개수를 만들려면 재료가 얼마나 더 필요한지 계산한다.
 */
export function MissionView() {
  const q = useInventory();
  const params = useRouter(s => s.loc.params);
  const setParam = useRouter(s => s.setParam);
  const mode: Mode = params.get('mode') === 'craft' ? 'craft' : params.get('mode') === 'cook' ? 'cook' : 'gather';

  const modeBar = (
    <div className="seg mode-seg" role="group" aria-label="임무 종류">
      {MODES.map(([k, l]) => (
        <button key={k} type="button" aria-pressed={mode === k} onClick={() => setParam('mode', k === 'gather' ? null : k)}>{l}</button>
      ))}
    </div>
  );
  if (mode === 'cook') {
    return (
      <>
        {modeBar}
        <div className="view-head"><h2>요리 임무</h2></div>
        <div className="card muted">요리는 준비 중입니다. 재료 정보를 게임에서 확인한 뒤 붙일 예정입니다.</div>
      </>
    );
  }
  if (q.isPending) return <>{modeBar}<Skeleton lines={6} /></>;
  if (q.isError && !q.data) return <>{modeBar}<ErrorCard error={q.error} onRetry={() => q.refetch()} /></>;
  const items = q.data.data.items;
  return (
    <>
      {modeBar}
      {mode === 'gather' ? <GatherPane items={items} at={q.dataUpdatedAt} /> : <CraftPane items={items} at={q.dataUpdatedAt} />}
    </>
  );
}

/** 채집 임무용 아이템: 마을 순, 0개도 흐리게 보인다. 게임이 달리 부르는 표기는 화면 이름으로 모아 센다. */
function GatherPane({ items, at }: { items: InvItem[]; at: number }) {
  const params = useRouter(s => s.loc.params);
  const setParam = useRouter(s => s.setParam);
  const [text, setText] = useState(params.get('q') ?? '');
  const search = useDeferredValue(text.trim());
  const rawSort = params.get('sort');
  const sort = GATHER_SORTS.some(([k]) => k === rawSort) ? rawSort! : 'table';

  const rows = useMemo(() => {
    const have = new Map(groupItems(items.filter(x => isMissionItem(x.name)).map(x => ({ ...x, name: missionInfo(x.name)!.canon }))).map(g => [g.name, g]));
    let r: InvGroup[] = MISSION_SLOTS.filter(s => s.kind === '채집')
      .map(s => have.get(s.name) ?? { name: s.name, category: '', favorite: false, locked: false, sessionDelta: 0, total: 0, by: {} });
    r = r.sort(SORT_FN[sort] ?? SORT_FN.table);
    if (search) r = r.filter(x => plain(x.name).includes(search) || (missionInfo(x.name)?.town.includes(search) ?? false));
    return r;
  }, [items, search, sort]);

  const haveCount = rows.filter(r => r.total > 0).length;
  return (
    <>
      <div className="view-head">
        <h2>채집 임무</h2>
        <span className="sub num">보유 {fmt(haveCount)} / {fmt(rows.length)}종 · <Fresh at={at} /></span>
      </div>
      <div className="toolbar">
        <label className="search">
          <Icon name="search" />
          <input type="search" placeholder="이름·마을 검색" value={text} aria-label="아이템 검색"
            onChange={e => { setText(e.target.value); setParam('q', e.target.value || null); }} />
        </label>
        <label className="inv-sort">정렬
          <select value={sort} aria-label="정렬 순서" onChange={e => setParam('sort', e.target.value === 'table' ? null : e.target.value)}>
            {GATHER_SORTS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}
          </select>
        </label>
      </div>
      {rows.length === 0 ? <div className="card muted">조건에 맞는 아이템이 없습니다.</div> : TOWNS.map(t => {
        const list = rows.filter(r => missionInfo(r.name)?.town === t);
        if (list.length === 0) return null;
        return (
          <section key={t} aria-label={t}>
            <h4 className="town-h">{t}</h4>
            <ItemList rows={list} />
          </section>
        );
      })}
    </>
  );
}

const TARGET_KEY = 'mobimate.craftTargets';
function loadTargets(): Record<string, number> {
  try {
    const v = JSON.parse(localStorage.getItem(TARGET_KEY) ?? '{}') as Record<string, unknown>;
    return Object.fromEntries(Object.entries(v).filter(([, n]) => typeof n === 'number' && n > 0)) as Record<string, number>;
  } catch { return {}; }
}
function saveTargets(t: Record<string, number>) {
  try { localStorage.setItem(TARGET_KEY, JSON.stringify(t)); } catch { /* 저장이 막힌 브라우저에서는 이번 접속 동안만 기억한다 */ }
}

/**
 * 제작 계산기. 위: 레시피 카드(마을별) — 지금 몇 개 만들 수 있나 + 목표 개수 입력.
 * 옆(좁으면 아래): 목표를 다 만들려면 필요한 재료 합계와 모자란 만큼. 여러 레시피가 같은 재료를 쓰면 합쳐서 센다.
 */
function CraftPane({ items, at }: { items: InvItem[]; at: number }) {
  const params = useRouter(s => s.loc.params);
  const setParam = useRouter(s => s.setParam);
  const bagOnly = params.get('src') === 'bag';
  const [targets, setTargets] = useState(loadTargets);
  const stock = useMemo(() => buildStock(items, !bagOnly), [items, bagOnly]);
  const plan = useMemo(() => planTargets(targets, stock), [targets, stock]);
  const short = useMemo(() => new Set(plan.lines.filter(l => l.short > 0).map(l => l.name)), [plan]);

  const setTarget = (name: string, n: number) => {
    const next = { ...targets };
    if (n > 0) next[name] = Math.min(9999, Math.floor(n)); else delete next[name];
    setTargets(next);
    saveTargets(next);
  };
  const clearAll = () => { setTargets({}); saveTargets({}); };
  const hasTargets = plan.totalCrafts > 0;

  return (
    <>
      <div className="view-head">
        <h2>제작 계산</h2>
        <span className="sub num">레시피 {fmt(RECIPES.length)}종 · <Fresh at={at} /></span>
      </div>
      <div className="toolbar">
        <button type="button" className="chip" aria-pressed={!bagOnly} onClick={() => setParam('src', bagOnly ? null : 'bag')}
          title="창고 재료도 이동 비용(분해된 장비 부품)을 내면 제작에 쓸 수 있습니다">계정·캐릭터 창고 재료 포함</button>
        <button type="button" className="chip" onClick={clearAll} disabled={!hasTargets}>목표 모두 지우기</button>
      </div>
      <div className="craft-layout">
        <div className="craft-cards">
          {TOWNS.map(t => (
            <section key={t} aria-label={t}>
              <h4 className="town-h">{t}</h4>
              <div className="rc-grid" role="list">
                {RECIPES.filter(r => r.town === t).map(r => {
                  const now = craftableNow(r, stock);
                  const goal = targets[r.name] ?? 0;
                  const blocked = plan.blocked.has(r.name);
                  return (
                    <div className={`rc-card ${goal > 0 ? (blocked ? 'blocked' : 'ok') : ''}`} role="listitem" key={r.name}>
                      <div className="rc-t">
                        <span className="rc-name" title={r.name}>{r.name}</span>
                        {r.produces > 1 && <span className="rc-x" title={`1회에 ${r.produces}개씩 나옵니다`}>×{r.produces}{goal > 0 && <> · {fmt(plan.crafts[r.name])}회</>}</span>}
                        {goal > 0 && <Pill tone={blocked ? 'danger' : 'ok'}>{blocked ? '재료 부족' : '재료 충분'}</Pill>}
                      </div>
                      <div className="rc-row">
                        <span className="rc-now">지금 <b className="num">{fmt(now.items)}</b>개 가능</span>
                        <div className="rc-goal">
                          <span className="muted small">목표</span>
                          <div className="goal">
                            <button type="button" aria-label={`${r.name} 목표 줄이기`} onClick={() => setTarget(r.name, goal - 1)} disabled={goal === 0}>−</button>
                            <input type="number" inputMode="numeric" min={0} max={9999} placeholder="0" aria-label={`${r.name} 목표 개수`} value={goal || ''}
                              onChange={e => setTarget(r.name, Number(e.target.value))} />
                            <button type="button" aria-label={`${r.name} 목표 늘리기`} onClick={() => setTarget(r.name, goal + 1)}>+</button>
                          </div>
                        </div>
                      </div>
                      <div className="rc-need">
                        {r.needs.map(([n, q]) => {
                          const have = stock.get(n) ?? 0;
                          const bad = goal > 0 ? short.has(n) : have < q;
                          return <span key={n} className={`rc-m ${bad ? 'bad' : ''}`} title={`${n}: 1회에 ${q}개, 가진 것 ${fmt(have)}개`}>{n} {q}</span>;
                        })}
                      </div>
                    </div>
                  );
                })}
              </div>
            </section>
          ))}
        </div>
        <aside className="card craft-sum" aria-label="필요한 재료">
          <h3 className="m0">필요한 재료</h3>
          {!hasTargets ? (
            <p className="muted small m0">레시피 카드에 목표 개수(퀘스트가 요구하는 개수)를 넣으면, 다 만드는 데 필요한 재료와 모자란 만큼을 여기서 계산합니다.</p>
          ) : (
            <>
              <p className="small m0 num">제작 {fmt(plan.totalCrafts)}회 · {plan.shortKinds > 0 ? <b className="bad-text">모자란 재료 {fmt(plan.shortKinds)}종</b> : <b className="ok-text">재료 충분</b>}</p>
              <table className="sum-table">
                <thead><tr><th>재료</th><th className="n">필요</th><th className="n">보유</th><th className="n">부족</th></tr></thead>
                <tbody>
                  {plan.lines.map(l => (
                    <tr key={l.name} className={l.short > 0 ? 'bad' : ''}>
                      <td>{l.name}</td><td className="n num">{fmt(l.need)}</td><td className="n num">{fmt(l.have)}</td>
                      <td className="n num">{l.short > 0 ? fmt(l.short) : '✓'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
          <p className="faint small m0">보유는 {bagOnly ? '가방' : '가방·계정 창고·캐릭터 창고'}의 합입니다. 창고 재료를 쓰면 이동 비용(분해된 장비 부품)이 듭니다.</p>
        </aside>
      </div>
    </>
  );
}
