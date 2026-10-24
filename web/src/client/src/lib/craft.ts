import { RECIPES, type Recipe } from '../features/missionData';
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

export interface PlanLine { name: string; need: number; have: number; short: number }
export interface Plan {
  /** 제작 횟수(목표 개수를 1회 생산량으로 올림한 값). 목표가 0인 레시피는 없다. */
  crafts: Record<string, number>;
  /** 필요한 재료 합계. 여러 레시피가 같은 재료를 쓰면 합쳐서 센다. 부족한 것이 위로 온다. */
  lines: PlanLine[];
  /** 목표 때문에 재료가 모자라는 레시피 이름 */
  blocked: ReadonlySet<string>;
  totalCrafts: number;
  shortKinds: number;
}

/** 목표 개수(퀘스트가 요구하는 만들 개수, made = 이미 가진 완성품)를 다 채우려면 재료가 얼마나 드는지, 가진 것과 비교해 모자란 만큼을 구한다. */
export function planTargets(targets: Readonly<Record<string, number>>, stock: Stock, recipes: readonly Recipe[] = RECIPES, made: Stock = new Map()): Plan {
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
  const lines: PlanLine[] = [...need].map(([name, n]) => {
    const have = stock.get(name) ?? 0;
    return { name, need: n, have, short: Math.max(0, n - have) };
  }).sort((a, b) => Number(b.short > 0) - Number(a.short > 0) || b.short - a.short || a.name.localeCompare(b.name, 'ko'));
  const shortNames = new Set(lines.filter(l => l.short > 0).map(l => l.name));
  const blocked = new Set(recipes.filter(r => crafts[r.name] && r.needs.some(([n]) => shortNames.has(n))).map(r => r.name));
  return { crafts, lines, blocked, totalCrafts: Object.values(crafts).reduce((a, b) => a + b, 0), shortKinds: shortNames.size };
}
