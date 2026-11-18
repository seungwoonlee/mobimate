import { useDeferredValue, useMemo, useState } from 'react';
import { useInventory, useLife } from '../api/queries';
import type { InvItem } from '../api/types';
import { Dialog, ErrorCard, Fresh, Icon, Pill, Skeleton } from '../components/ui';
import { startGatherQueue } from '../lib/actions';
import { gatherBatches, WINGS_PER_BATCH } from './life';
import { buildStock, buildStockBy, craftableNow, planTargets, type PlanLine, type Where } from '../lib/craft';
import { fmt, plain } from '../lib/format';
import { useRouter } from '../state/router';
import { groupItems, ItemList, SORT_FN, type InvGroup } from './inventory';
import { COOK_RECIPES, isMissionItem, MISSION_SLOTS, missionInfo, PROCESSES, RECIPES, TOWNS, type Recipe } from './missionData';

type Mode = 'gather' | 'cook' | 'craft';
const MODES: [Mode, string][] = [['gather', '채집'], ['cook', '요리'], ['craft', '제작']];
const GATHER_SORTS: [string, string][] = [['table', '마을 목록 순'], ['count', '개수 많은 순'], ['countAsc', '개수 적은 순'], ['name', '이름순'], ['delta', '이번 접속 늘어난 순'], ['deltaAsc', '이번 접속 줄어든 순']];

/**
 * 임무 탭 (2026-10-24): 마을별 임무 준비를 돕는 세 가지 보기.
 * - 채집: 채집 임무용 아이템 20종을 가진 개수가 0이어도 모두 보여 준다.
 * - 요리: 제작과 같은 계산기. 이미 가진 요리는 목표에서 빼고 계산한다(재료 개수가 확정되기 전까지 계산 제외).
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
  if (q.isPending) return <>{modeBar}<Skeleton lines={6} /></>;
  if (q.isError && !q.data) return <>{modeBar}<ErrorCard error={q.error} onRetry={() => q.refetch()} /></>;
  const items = q.data.data.items;
  return (
    <>
      {modeBar}
      {mode === 'gather' && <GatherPane items={items} at={q.dataUpdatedAt} />}
      {mode === 'cook' && <RecipePane items={items} at={q.dataUpdatedAt} recipes={COOK_RECIPES} kind="요리" />}
      {mode === 'craft' && <RecipePane items={items} at={q.dataUpdatedAt} recipes={RECIPES} kind="제작" />}
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

  const all = useMemo(() => {
    const have = new Map(groupItems(items.filter(x => isMissionItem(x.name)).map(x => ({ ...x, name: missionInfo(x.name)!.canon }))).map(g => [g.name, g]));
    return MISSION_SLOTS.filter(s => s.kind === '채집')
      .map(s => have.get(s.name) ?? { name: s.name, category: '', favorite: false, locked: false, sessionDelta: 0, total: 0, by: {} } as InvGroup);
  }, [items]);
  const rows = useMemo(() => {
    let r: InvGroup[] = [...all].sort(SORT_FN[sort] ?? SORT_FN.table);
    if (search) r = r.filter(x => plain(x.name).includes(search) || (missionInfo(x.name)?.town.includes(search) ?? false));
    return r;
  }, [all, search, sort]);

  // 준비 계획: 카드를 눌러 고르고 목표 개수를 넣으면 창고에서 꺼낼 것과 더 채집할 것을 계산한다 (2026-11-01)
  const [plan, setPlan] = useState(loadPlan);
  const picked = useMemo(() => new Set(Object.keys(plan)), [plan]);
  const savePlan = (next: Record<string, number>) => { setPlan(next); try { localStorage.setItem(PLAN_KEY, JSON.stringify(next)); } catch { /* 저장이 막혀도 이번 접속에서는 동작한다 */ } };
  const pick = (name: string) => {
    const next = { ...plan };
    if (name in next) delete next[name]; else next[name] = DEFAULT_PLAN_TARGET;
    savePlan(next);
  };

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
      <GatherPlan all={all} plan={plan} onChange={savePlan} />
      {rows.length === 0 ? <div className="card muted">조건에 맞는 아이템이 없습니다.</div> : TOWNS.map(t => {
        const list = rows.filter(r => missionInfo(r.name)?.town === t);
        if (list.length === 0) return null;
        return (
          <section key={t} aria-label={t}>
            <h4 className="town-h">{t}</h4>
            <ItemList rows={list} tall picked={picked} onPick={pick} />
          </section>
        );
      })}
    </>
  );
}


const PLAN_KEY = 'mobimate.gatherPlan';
const DEFAULT_PLAN_TARGET = 10;
function loadPlan(): Record<string, number> {
  try {
    const v = JSON.parse(localStorage.getItem(PLAN_KEY) ?? '{}') as Record<string, unknown>;
    return Object.fromEntries(Object.entries(v).filter(([, n]) => typeof n === 'number' && n > 0)) as Record<string, number>;
  } catch { return {}; }
}

/** 한 재료의 준비 계산: 가방에서 모자란 만큼을 계정 창고 → 캐릭터 창고 순으로 꺼내고, 그래도 모자라면 채집한다. */
export function planItem(target: number, by: Record<string, number>): { bag: number; fromAccount: number; fromCharacter: number; gather: number } {
  const bag = by.bag ?? 0;
  const need = Math.max(0, target - bag);
  const fromAccount = Math.min(need, by.account ?? 0);
  const fromCharacter = Math.min(need - fromAccount, by.character ?? 0);
  return { bag, fromAccount, fromCharacter, gather: need - fromAccount - fromCharacter };
}

/**
 * 채집 임무 준비 계획: 고른 재료마다 목표 개수를 넣으면 "창고에서 꺼낼 개수(계정 먼저)"와 "더 채집할 개수"를 보여 준다.
 * 게임 CLI에는 창고에서 꺼내는 명령이 없어 꺼내기는 게임에서 직접 해야 하고, 부족한 만큼의 채집은 [채집 시작]으로 큐에 넣는다.
 */
function GatherPlan({ all, plan, onChange }: { all: InvGroup[]; plan: Record<string, number>; onChange: (p: Record<string, number>) => void }) {
  const life = useLife();
  const gatherables = life.data?.data.gatherables ?? null;
  const [confirm, setConfirm] = useState(false);
  const squash = (n: string) => plain(n).replace(/\s/g, '');
  const canGather = (name: string) => gatherables?.find(g => squash(g.name) === squash(name));
  const lines = all.filter(r => r.name in plan).map(r => ({ r, target: plan[r.name], ...planItem(plan[r.name], r.by) }));
  const withdraw = lines.filter(l => l.fromAccount + l.fromCharacter > 0);
  const toGather = lines.filter(l => l.gather > 0).map(l => ({ ...l, g: canGather(l.r.name) }));
  const runnable = toGather.filter(l => l.g?.toolOk);
  const wings = runnable.reduce((n, l) => n + gatherBatches(l.gather) * WINGS_PER_BATCH, 0);
  const setTarget = (name: string, n: number) => {
    const next = { ...plan };
    if (n > 0) next[name] = Math.min(9999, Math.floor(n)); else delete next[name];
    onChange(next);
  };
  return (
    <div className="card gp">
      <h3 className="m0">임무 준비 계획</h3>
      {lines.length === 0 ? <p className="muted small m0">아래 카드에서 임무에 필요한 재료를 고르고 목표 개수를 넣으면, 창고에서 꺼낼 개수(계정 창고 먼저)와 더 채집해야 할 개수를 계산합니다.</p> : (
        <>
          <table className="sum-table gp-table">
            <thead><tr><th scope="col">재료</th><th scope="col" className="n">목표</th><th scope="col" className="n">가방</th><th scope="col" className="n">창고에서 꺼내기</th><th scope="col" className="n">더 채집</th><th scope="col" /></tr></thead>
            <tbody>
              {lines.map(l => (
                <tr key={l.r.name} className={l.gather > 0 ? 'bad' : ''}>
                  <td>{plain(l.r.name)}</td>
                  <td className="n">
                    <div className="goal">
                      <button type="button" aria-label={`${plain(l.r.name)} 목표 줄이기`} onClick={() => setTarget(l.r.name, l.target - 1)}>−</button>
                      <input type="number" inputMode="numeric" min={0} max={9999} aria-label={`${plain(l.r.name)} 목표 개수`} value={l.target} onChange={e => setTarget(l.r.name, Number(e.target.value))} />
                      <button type="button" aria-label={`${plain(l.r.name)} 목표 늘리기`} onClick={() => setTarget(l.r.name, l.target + 1)}>+</button>
                    </div>
                  </td>
                  <td className="n num">{fmt(l.bag)}</td>
                  <td className="n num">
                    {l.fromAccount + l.fromCharacter === 0 ? '—' : <>{l.fromAccount > 0 && <span>계정 {fmt(l.fromAccount)}</span>}{l.fromAccount > 0 && l.fromCharacter > 0 && ' · '}{l.fromCharacter > 0 && <span>캐릭터 {fmt(l.fromCharacter)}</span>}</>}
                  </td>
                  <td className="n num">{l.gather > 0 ? <b className="bad-text">{fmt(l.gather)}{!canGather(l.r.name) && ' (채집 불가)'}</b> : '✓'}</td>
                  <td className="n"><button type="button" className="btn" aria-label={`${plain(l.r.name)} 계획에서 빼기`} onClick={() => setTarget(l.r.name, 0)}>×</button></td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="small m0 num">
            창고에서 꺼낼 재료 {fmt(withdraw.length)}종 · 더 채집할 재료 {fmt(toGather.length)}종{runnable.length > 0 && <> · 정령의 날개 {fmt(wings)}개</>}
          </p>
          <div className="gp-act">
            <button type="button" className="btn primary" disabled={runnable.length === 0} onClick={() => setConfirm(true)} data-needs-conn>부족분 채집 시작{runnable.length > 0 ? ` (${runnable.length})` : ''}</button>
            <button type="button" className="btn" onClick={() => onChange({})}>계획 비우기</button>
          </div>
          <p className="faint small m0">게임 CLI에는 창고에서 꺼내는 기능이 없어서, 꺼내기는 게임에서 직접 해야 합니다. 창고 재료를 가방으로 옮길 때는 이동 비용이 듭니다.</p>
        </>
      )}
      {confirm && (
        <Dialog title="부족분 채집 시작" onClose={() => setConfirm(false)}
          actions={<>
            <button type="button" className="btn" onClick={() => setConfirm(false)}>취소</button>
            <button type="button" className="btn primary" onClick={() => { void startGatherQueue(runnable.map(l => ({ name: l.g!.name, count: l.gather }))); setConfirm(false); }}>채집 시작</button>
          </>}>
          <p>창고에서 꺼낸 뒤에도 모자란 만큼만, 아래 순서대로 채집합니다.</p>
          <ol className="gx-confirm">{runnable.map(l => <li key={l.r.name}>{plain(l.r.name)} <b className="num">{fmt(l.gather)}개</b></li>)}</ol>
          <p className="warn-text">정령의 날개 {fmt(wings)}개를 씁니다. 창고에서 먼저 꺼내지 않고 시작하면 그만큼 더 채집하게 됩니다. 도중에 실패하거나 정지하면 남은 재료는 취소됩니다.</p>
        </Dialog>
      )}
    </div>
  );
}

const TARGET_KEY = 'mobimate.craftTargets';
const targetKey = (kind: string) => (kind === '제작' ? TARGET_KEY : `${TARGET_KEY}.${kind}`);   // 제작은 처음 만든 저장 이름을 그대로 쓴다
function loadTargets(key: string): Record<string, number> {
  try {
    const v = JSON.parse(localStorage.getItem(key) ?? '{}') as Record<string, unknown>;
    return Object.fromEntries(Object.entries(v).filter(([, n]) => typeof n === 'number' && n > 0)) as Record<string, number>;
  } catch { return {}; }
}
function saveTargets(key: string, t: Record<string, number>) {
  try { localStorage.setItem(key, JSON.stringify(t)); } catch { /* 저장이 막힌 브라우저에서는 이번 접속 동안만 기억한다 */ }
}

/** 가진 곳을 "가방 3 · 캐릭터 2 · 계정 5"로. 없으면 "없음". */
export function whereText(w: Where | undefined): string {
  if (!w) return '없음';
  const parts = [w.bag > 0 && `가방 ${fmt(w.bag)}`, w.character > 0 && `캐릭터 ${fmt(w.character)}`, w.account > 0 && `계정 ${fmt(w.account)}`].filter(Boolean);
  return parts.length ? parts.join(' · ') : '없음';
}
const minutesText = (m: number): string => {
  const h = Math.floor(m / 60), r = Math.round(m % 60);
  return h > 0 ? `${h}시간${r ? ` ${r}분` : ''}` : r === 0 && m > 0 ? `${Math.round(m * 60)}초` : `${Number.isInteger(m) ? m : m.toFixed(1)}분`;
};

/**
 * 제작·요리 계산기(같은 화면). 위: 레시피 카드(마을별) — 지금 몇 개 만들 수 있나 + 목표 개수 입력 + 재료마다 필요 개수와 가진 곳(가방·캐릭터·계정).
 * 옆(좁으면 아래): 목표를 다 만들려면 필요한 재료 합계와 모자란 만큼. 여러 레시피가 같은 재료를 쓰면 합쳐서 센다.
 * 2차 재료(강철괴·목재+ 등)가 모자라면 그만큼을 가공하는 데 드는 원재료까지 풀어서 센다(PROCESSES).
 * 요리는 완성품 개수도 가방에서 알 수 있어, 이미 가진 요리는 목표에서 빼고 계산한다. 필요 개수가 미확정(unverified)인 레시피는 계산에서 뺀다.
 */
function RecipePane({ items, at, recipes, kind }: { items: InvItem[]; at: number; recipes: readonly Recipe[]; kind: '제작' | '요리' }) {
  const params = useRouter(s => s.loc.params);
  const setParam = useRouter(s => s.setParam);
  const bagOnly = params.get('src') === 'bag';
  const key = targetKey(kind);
  const [targets, setTargets] = useState(() => loadTargets(key));
  const stock = useMemo(() => buildStock(items, !bagOnly), [items, bagOnly]);
  const where = useMemo(() => buildStockBy(items), [items]);
  const cook = kind === '요리';
  // 요리 완성품 개수: 게임이 달리 부르는 표기도 같은 요리로 센다
  const made = useMemo(() => new Map(cook ? recipes.map(r => [r.name, (stock.get(r.name) ?? 0) + (r.alias ? stock.get(r.alias) ?? 0 : 0)] as const) : []), [cook, recipes, stock]);
  const plan = useMemo(() => planTargets(targets, stock, recipes, made, PROCESSES), [targets, stock, recipes, made]);
  const short = useMemo(() => new Set(plan.lines.filter(l => !l.made && l.short > 0).map(l => l.name)), [plan]);   // 구해야 하는 원재료
  const allPending = recipes.every(r => r.unverified);

  const setTarget = (name: string, n: number) => {
    const next = { ...targets };
    if (n > 0) next[name] = Math.min(9999, Math.floor(n)); else delete next[name];
    setTargets(next);
    saveTargets(key, next);
  };
  const clearAll = () => { setTargets({}); saveTargets(key, {}); };
  const hasTargets = plan.totalCrafts > 0;
  const hasGoal = Object.keys(targets).length > 0;
  const madeLines = plan.lines.filter(l => l.made);
  const rawLines = plan.lines.filter(l => !l.made);

  const row = (l: PlanLine) => (
    <tr key={l.name} className={l.short > 0 && !l.made ? 'bad' : ''}>
      <td>
        <div className="sum-n">{l.name}</div>
        <div className="sum-w faint">{whereText(where.get(l.name))}</div>
        {l.made && l.processCrafts! > 0 && <div className="sum-p" title={l.note}>{l.verb ?? '가공'} {fmt(l.processCrafts!)}회{l.minutes! > 0 && <> · {minutesText(l.minutes!)}</>}{l.verb === '분해' && <> · 평균값</>}</div>}
      </td>
      <td className="n num">{fmt(l.need)}</td><td className="n num">{fmt(l.have)}</td>
      <td className="n num">{l.short > 0 ? fmt(l.short) : '✓'}</td>
    </tr>
  );

  return (
    <>
      <div className="view-head">
        <h2>{kind} 계산</h2>
        <span className="sub num">레시피 {fmt(recipes.length)}종 · <Fresh at={at} /></span>
      </div>
      <div className="toolbar">
        <button type="button" className="chip" aria-pressed={!bagOnly} onClick={() => setParam('src', bagOnly ? null : 'bag')}
          title={`창고 재료도 이동 비용(분해된 장비 부품)을 내면 ${kind}에 쓸 수 있습니다`}>계정·캐릭터 창고 재료 포함</button>
        <button type="button" className="chip" onClick={clearAll} disabled={!hasGoal}>목표 모두 지우기</button>
      </div>
      <div className="craft-layout">
        <div className="craft-cards">
          {TOWNS.map(t => (
            <section key={t} aria-label={t}>
              <h4 className="town-h">{t}</h4>
              <div className="rc-grid" role="list">
                {recipes.filter(r => r.town === t).map(r => {
                  const goal = targets[r.name] ?? 0;
                  if (r.unverified) {
                    return (
                      <div className="rc-card pending" role="listitem" key={r.name}>
                        <div className="rc-t">
                          <span className="rc-name" title={r.name}>{r.name}</span>
                          {cook && <span className="rc-x" title="이미 가진 개수">보유 {fmt(made.get(r.name) ?? 0)}</span>}
                        </div>
                        <div className="rc-now">재료 개수 확인 중 · 계산 제외</div>
                        <div className="rc-need" title="알려진 재료 이름만 적었습니다. 필요한 개수는 확인되면 채웁니다.">{r.needs.map(([n]) => <div key={n} className="rc-m"><span>{n}</span></div>)}</div>
                      </div>
                    );
                  }
                  const now = craftableNow(r, stock);
                  const blocked = plan.blocked.has(r.name);
                  const done = goal > 0 && !plan.crafts[r.name];   // 이미 가진 요리로 목표가 채워짐
                  return (
                    <div className={`rc-card ${goal > 0 ? (blocked ? 'blocked' : 'ok') : ''}`} role="listitem" key={r.name}>
                      <div className="rc-t">
                        <span className="rc-name" title={r.name}>{r.name}</span>
                        {r.produces > 1 && <span className="rc-x" title={`1회에 ${r.produces}개씩 나옵니다`}>×{r.produces}{goal > 0 && !done && <> · {fmt(plan.crafts[r.name])}회</>}</span>}
                        {cook && <span className="rc-x" title="이미 가진 개수">보유 {fmt(made.get(r.name) ?? 0)}</span>}
                        {goal > 0 && <Pill tone={blocked ? 'danger' : 'ok'}>{done ? '이미 충분' : blocked ? '재료 부족' : '재료 충분'}</Pill>}
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
                          const w = whereText(where.get(n));
                          return (
                            <div key={n} className={`rc-m ${bad ? 'bad' : ''}`} title={`${n}: 1회에 ${q}개 · 가진 것 ${fmt(have)}개 (${w})`}>
                              <span className="rc-mn">{n} {q}</span><span className="rc-mw">{w}</span>
                            </div>
                          );
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
          {allPending ? (
            <p className="muted small m0">{kind} 재료의 필요 개수를 아직 확인하지 못해 계산하지 않습니다. 확인되는 대로 채웁니다.</p>
          ) : !hasTargets ? (
            <p className="muted small m0">레시피 카드에 목표 개수(퀘스트가 요구하는 개수)를 넣으면, 다 만드는 데 필요한 재료와 모자란 만큼을 여기서 계산합니다.</p>
          ) : (
            <>
              <p className="small m0 num">
                {kind} {fmt(plan.totalCrafts)}회{plan.totalProcess > 0 && <> · 가공·분해 {fmt(plan.totalProcess)}회</>} · {plan.shortKinds > 0 ? <b className="bad-text">모자란 원재료 {fmt(plan.shortKinds)}종</b> : <b className="ok-text">재료 충분</b>}
              </p>
              {madeLines.length > 0 && (
                <>
                  <h4 className="sum-h">가공해서 쓰는 재료</h4>
                  <table className="sum-table">
                    <thead><tr><th scope="col">재료</th><th scope="col" className="n">필요</th><th scope="col" className="n">보유</th><th scope="col" className="n">부족</th></tr></thead>
                    <tbody>{madeLines.map(row)}</tbody>
                  </table>
                </>
              )}
              <h4 className="sum-h">{madeLines.length > 0 ? '원재료 (가공에 드는 것 포함)' : '재료'}</h4>
              <table className="sum-table">
                <thead><tr><th scope="col">재료</th><th scope="col" className="n">필요</th><th scope="col" className="n">보유</th><th scope="col" className="n">부족</th></tr></thead>
                <tbody>{rawLines.map(row)}</tbody>
              </table>
            </>
          )}
          <p className="faint small m0">보유는 {bagOnly ? '가방' : '가방·계정 창고·캐릭터 창고'}의 합이고, 아래 작은 글씨는 가진 곳입니다. 창고 재료를 쓰면 이동 비용(분해된 장비 부품)이 듭니다.{cook && ' 이미 가진 요리는 목표에서 뺍니다.'} 시설 가공은 한 번에 3개가 나오고, 타닌 가루는 나뭇가지 분해(평균 1.5개)로, 물이 든 병은 빈 병 1개로 만듭니다. 만드는 법을 모르는 재료는 원재료로 셉니다.</p>
        </aside>
      </div>
    </>
  );
}
