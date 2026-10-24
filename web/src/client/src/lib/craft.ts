import { RECIPES, type Process, type Recipe } from '../features/missionData';
import type { InvItem } from '../api/types';
import { plain } from './format';

/** 재료 이름 → 가진 개수 */
export type Stock = ReadonlyMap<string, number>;

/** 가진 개수를 이름별로 합친다. 창고 재료도 이동 비용(분해된 장비 부품)을 내면 쓸 수 있어 기본은 창고까지 센다. */
export function buildStock(items: readonly InvItem[], includeStorage: boolean): Map<string, number> {
  const m = new Map<string, number>();
  for (const i of items) {
    if (!includeStorage && i.location !== 'bag') continue;
    const k = plain(i.name).trim();
    m.set(k, (m.get(k) ?? 0) + i.count);
  }
  return m;
}

/** 위치별로 가진 개수(가방·캐릭터 창고·계정 창고). 이름은 색 태그를 떼고 센다. */
export interface Where { bag: number; character: number; account: number }
export function buildStockBy(items: readonly InvItem[]): Map<string, Where> {
  const m = new Map<string, Where>();
  for (const i of items) {
    const k = plain(i.name).trim();
    const w = m.get(k) ?? m.set(k, { bag: 0, character: 0, account: 0 }).get(k)!;
    if (i.location === 'bag') w.bag += i.count;
    else if (i.location === 'character') w.character += i.count;
    else if (i.location === 'account') w.account += i.count;
  }
  return m;
}

export interface CraftNow {
  crafts: number;          // 지금 재료로 제작할 수 있는 횟수
  items: number;           // 그때 나오는 개수 (횟수 × 1회 생산)
  limiting: string[];      // 횟수를 막는 재료(가장 먼저 바닥나는 것)
}

/** 지금 가진 재료만으로 이 레시피를 몇 번(몇 개) 만들 수 있나. 다른 레시피와 재료를 나누지 않는 단독 계산이다. */
export function craftableNow(r: Recipe, stock: Stock): CraftNow {
  const per = r.needs.map(([n, q]) => ({ n, c: Math.floor((stock.get(n) ?? 0) / q) }));
  const crafts = Math.min(...per.map(p => p.c));
  return { crafts, items: crafts * r.produces, limiting: per.filter(p => p.c === crafts).map(p => p.n) };
}

export interface PlanLine {
  name: string; need: number; have: number; short: number;
  /** 가공으로 만드는 2차 재료면 true. 모자란 만큼 가공해야 하고, 그 원재료는 아래 줄들에 합쳐진다. */
  made?: boolean;
  /** (made일 때) 가공 횟수와 걸리는 분 */
  processCrafts?: number; minutes?: number;
}
export interface Plan {
  /** 제작 횟수(목표 개수를 1회 생산량으로 올림한 값). 목표가 0인 레시피는 없다. */
  crafts: Record<string, number>;
  /** 필요한 재료 합계. 여러 레시피가 같은 재료를 쓰면 합쳐서 센다. 부족한 것이 위로 온다. */
  lines: PlanLine[];
  /** 목표 때문에 재료가 모자라는 레시피 이름 */
  blocked: ReadonlySet<string>;
  totalCrafts: number;
  /** 모자란 재료 종류 수. 가공으로 만들 수 있는 재료는 원재료까지 풀어서 센다(원재료가 모자란 것만). */
  shortKinds: number;
  /** 가공 횟수 합계 */
  totalProcess: number;
}

/** 가공 레시피를 소비하는 쪽이 앞서도록 늘어놓는다(합금강괴 → 강철괴 → 철괴). 순환이 있으면 남은 것을 그대로 뒤에 붙인다. */
function processOrder(processes: readonly Process[]): Process[] {
  const names = new Set(processes.map(p => p.name));
  const consumed = new Map<string, number>();   // 아직 처리하지 않은 소비자 수
  for (const p of processes) for (const [n] of p.needs) if (names.has(n)) consumed.set(n, (consumed.get(n) ?? 0) + 1);
  const left = [...processes], out: Process[] = [];
  while (left.length) {
    const i = left.findIndex(p => !consumed.get(p.name));
    const p = left.splice(i < 0 ? 0 : i, 1)[0];
    out.push(p);
    for (const [n] of p.needs) if (consumed.has(n)) consumed.set(n, consumed.get(n)! - 1);
  }
  return out;
}

/**
 * 목표 개수(퀘스트가 요구하는 만들 개수, made = 이미 가진 완성품)를 다 채우려면 재료가 얼마나 드는지, 가진 것과 비교해 모자란 만큼을 구한다.
 * processes를 주면 2차 재료(강철괴·목재+ 등)가 모자랄 때 그만큼을 가공하는 데 드는 원재료까지 풀어서 센다:
 * 높은 단계부터(합금강괴 → 강철괴 → 철괴) 필요량 − 가진 개수 = 모자란 개수를 가공 횟수(올림)로 바꿔 그 재료를 아래 단계의 필요량에 더한다.
 * 같은 재료를 여러 곳에서 쓰면(석탄, 강철괴) 합쳐서 한 번에 센다.
 */
export function planTargets(targets: Readonly<Record<string, number>>, stock: Stock, recipes: readonly Recipe[] = RECIPES, made: Stock = new Map(), processes: readonly Process[] = []): Plan {
  const crafts: Record<string, number> = {};
  const need = new Map<string, number>();
  for (const r of recipes) {
    if (r.unverified) continue;   // 필요 개수를 모르면 계산하지 않는다
    // 이미 만들어 둔 완성품(요리는 개수를 알 수 있다)은 목표에서 뺀다
    const t = Math.max(0, Math.floor(targets[r.name] ?? 0) - (made.get(r.name) ?? 0));
    if (t === 0) continue;
    const c = Math.ceil(t / r.produces);
    crafts[r.name] = c;
    for (const [n, q] of r.needs) need.set(n, (need.get(n) ?? 0) + q * c);
  }
  // 모자란 2차 재료를 가공으로 채운다
  const byName = new Map(processes.map(p => [p.name, p]));
  const processCrafts = new Map<string, number>();
  for (const p of processOrder(processes)) {
    const want = need.get(p.name) ?? 0;
    const short = Math.max(0, want - (stock.get(p.name) ?? 0));
    if (short === 0) continue;
    const c = Math.ceil(short / p.produces);
    processCrafts.set(p.name, c);
    for (const [n, q] of p.needs) need.set(n, (need.get(n) ?? 0) + q * c);
  }
  const lines: PlanLine[] = [...need].map(([name, n]) => {
    const have = stock.get(name) ?? 0;
    const line: PlanLine = { name, need: n, have, short: Math.max(0, n - have) };
    const p = byName.get(name);
    if (p) { line.made = true; line.processCrafts = processCrafts.get(name) ?? 0; line.minutes = (processCrafts.get(name) ?? 0) * p.minutes; }
    return line;
  }).sort((a, b) => Number(b.short > 0) - Number(a.short > 0) || b.short - a.short || a.name.localeCompare(b.name, 'ko'));
  // 구해야 할 것 = 가공할 수 없는 재료(원재료)가 모자란 것. 가공으로 채울 수 있는 2차 재료의 부족은 원재료 줄로 옮겨 갔다.
  const rawShort = new Set(lines.filter(l => !l.made && l.short > 0).map(l => l.name));
  const reachesShort = (name: string, seen = new Set<string>()): boolean => {
    if (rawShort.has(name)) return true;
    const p = byName.get(name);
    if (!p || seen.has(name) || !processCrafts.has(name)) return false;
    seen.add(name);
    return p.needs.some(([n]) => reachesShort(n, seen));
  };
  const blocked = new Set(recipes.filter(r => crafts[r.name] && r.needs.some(([n]) => reachesShort(n))).map(r => r.name));
  return {
    crafts, lines, blocked,
    totalCrafts: Object.values(crafts).reduce((a, b) => a + b, 0),
    shortKinds: rawShort.size,
    totalProcess: [...processCrafts.values()].reduce((a, b) => a + b, 0),
  };
}
