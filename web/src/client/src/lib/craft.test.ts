import type { InvItem } from '../api/types';
import { RECIPES, type Recipe } from '../features/missionData';
import { buildStock, buildStockBy, craftableNow, planTargets } from './craft';
import { PROCESSES } from '../features/missionData';

const it_ = (location: string, name: string, count: number): InvItem => ({ location, name, category: '재료', count, locked: false, sessionDelta: 0, favorite: false });
const recipe = (name: string) => RECIPES.find(r => r.name === name)!;

describe('buildStock', () => {
  const items = [it_('bag', '강철괴', 2), it_('account', '강철괴', 5), it_('bag', '<color=#fff>가죽+</color>', 3), it_('character', '가죽+', 4)];
  it('창고까지 합친다', () => {
    const s = buildStock(items, true);
    expect(s.get('강철괴')).toBe(7);
    expect(s.get('가죽+')).toBe(7);   // 색 태그는 떼고 센다
  });
  it('가방만 셀 수도 있다', () => {
    const s = buildStock(items, false);
    expect(s.get('강철괴')).toBe(2);
    expect(s.get('가죽+')).toBe(3);
  });
});

describe('craftableNow', () => {
  it('가장 먼저 바닥나는 재료가 횟수를 정한다', () => {
    const r = craftableNow(recipe('크레센트 엣지소드'), new Map([['강철괴', 5], ['가죽+', 9]]));   // 강철괴 2, 가죽+ 2
    expect(r).toEqual({ crafts: 2, items: 2, limiting: ['강철괴'] });
  });
  it('재료가 하나도 없으면 0', () => {
    expect(craftableNow(recipe('론 엣지소드S'), new Map()).items).toBe(0);
  });
  it('1회에 5개 나오는 것은 횟수 × 5개', () => {
    const stock = new Map([['분해된 장비 부품', 120], ['연금술 부스러기', 500], ['마나 허브', 100], ['협곡의 마나석', 9]]);
    expect(craftableNow(recipe('마법 유탄 부품'), stock)).toMatchObject({ crafts: 2, items: 10, limiting: ['분해된 장비 부품'] });
  });
});

describe('planTargets', () => {
  it('목표 개수만큼 재료가 얼마나 모자란지 구한다', () => {
    const p = planTargets({ '크레센트 엣지소드': 3 }, new Map([['강철괴', 4], ['가죽+', 10]]));   // 필요: 강철괴 6, 가죽+ 6
    expect(p.lines).toEqual([
      { name: '강철괴', need: 6, have: 4, short: 2 },
      { name: '가죽+', need: 6, have: 10, short: 0 },
    ]);
    expect(p.blocked.has('크레센트 엣지소드')).toBe(true);
    expect(p.shortKinds).toBe(1);
  });
  it('같은 재료를 쓰는 레시피는 합쳐서 센다', () => {
    const p = planTargets({ '그랜드 크로스보우': 1, '로터스 힐링 완드': 1 }, new Map([['강철괴', 3], ['목재+', 9]]));
    expect(p.lines.find(l => l.name === '강철괴')).toEqual({ name: '강철괴', need: 4, have: 3, short: 1 });
    expect([...p.blocked].sort()).toEqual(['그랜드 크로스보우', '로터스 힐링 완드']);
  });
  it('재료가 충분하면 막힌 레시피가 없다', () => {
    const p = planTargets({ '사슬 갑옷 신발': 2 }, new Map([['강철괴', 4], ['가죽+', 2]]));
    expect(p.blocked.size).toBe(0);
    expect(p.shortKinds).toBe(0);
    expect(p.totalCrafts).toBe(2);
  });
  it('1회 생산량보다 목표가 작아도 한 번은 만들어야 한다(올림)', () => {
    const p = planTargets({ '마법 유탄 부품': 12 }, new Map());   // 12개 → 3회(15개)
    expect(p.crafts['마법 유탄 부품']).toBe(3);
    expect(p.lines.find(l => l.name === '분해된 장비 부품')?.need).toBe(150);
  });
  it('목표가 0이거나 음수·소수면 무시하거나 내림한다', () => {
    expect(planTargets({ '전투복 신발S': 0 }, new Map()).lines).toEqual([]);
    expect(planTargets({ '전투복 신발S': -2 }, new Map()).totalCrafts).toBe(0);
    expect(planTargets({ '전투복 신발S': 2.9 }, new Map()).crafts['전투복 신발S']).toBe(2);
  });
  it('부족한 재료가 위로 온다', () => {
    const p = planTargets({ '전투복 신발S': 1 }, new Map([['상급 옷감', 99]]));   // 상급 옷감 3(충분), 강철괴 2(없음)
    expect(p.lines.map(l => l.name)).toEqual(['강철괴', '상급 옷감']);
  });
});

describe('요리 계획', () => {
  const dish: Recipe = { town: '던바튼', name: '시험 요리', produces: 1, needs: [['사과', 4], ['설탕', 1]] };
  it('이미 가진 요리는 목표에서 뺀다', () => {
    const p = planTargets({ '시험 요리': 5 }, new Map([['사과', 8], ['설탕', 9]]), [dish], new Map([['시험 요리', 3]]));
    expect(p.crafts['시험 요리']).toBe(2);   // 5개 중 3개는 이미 있어 2번만 만들면 된다
    expect(p.lines.find(l => l.name === '사과')).toEqual({ name: '사과', need: 8, have: 8, short: 0 });
  });
  it('이미 가진 요리가 목표 이상이면 만들 것이 없다', () => {
    const p = planTargets({ '시험 요리': 2 }, new Map(), [dish], new Map([['시험 요리', 3]]));
    expect(p.totalCrafts).toBe(0);
    expect(p.lines).toEqual([]);
  });
  it('필요 개수가 미확정인 레시피는 계산에서 뺀다', () => {
    const p = planTargets({ '시험 요리': 5 }, new Map(), [{ ...dish, unverified: true }]);
    expect(p.totalCrafts).toBe(0);
  });
});

describe('buildStockBy', () => {
  it('가방·캐릭터·계정별로 따로 센다', () => {
    const w = buildStockBy([it_('bag', '강철괴', 2), it_('account', '강철괴', 5), it_('character', '<color=#fff>강철괴</color>', 4), it_('bag', '강철괴', 1)]).get('강철괴');
    expect(w).toEqual({ bag: 3, character: 4, account: 5 });
  });
});

describe('가공 연계 (2차 재료까지 계산)', () => {
  // 크레센트 엣지소드 1개 = 강철괴 2 + 가죽+ 2. 강철괴 1회 가공 = 철괴 3 + 석탄 4 → 3개, 철괴 1회 가공 = 철 광석 10 → 3개.
  const plan = (target: number, stock: [string, number][]) => planTargets({ '크레센트 엣지소드': target }, new Map(stock), RECIPES, new Map(), PROCESSES);
  const line = (p: ReturnType<typeof plan>, name: string) => p.lines.find(l => l.name === name);

  it('모자란 강철괴를 가공하는 데 드는 철괴·석탄·철 광석까지 풀어서 센다', () => {
    const p = plan(3, [['강철괴', 1], ['가죽+', 9]]);   // 필요 강철괴 6, 가진 1 → 5 모자라 2회 가공(6개)
    expect(line(p, '강철괴')).toMatchObject({ need: 6, have: 1, short: 5, made: true, processCrafts: 2 });
    expect(line(p, '철괴')).toMatchObject({ need: 6, short: 6, made: true, processCrafts: 2 });   // 강철괴 2회 × 3 = 6 → 철괴 2회 가공
    expect(line(p, '철 광석')).toMatchObject({ need: 20, short: 20 });                              // 철괴 2회 × 10
    expect(line(p, '석탄')).toMatchObject({ need: 8, short: 8 });                                   // 강철괴 2회 × 4
    expect(p.totalProcess).toBe(4);
    expect(p.shortKinds).toBe(2);   // 구해야 할 원재료 = 철 광석, 석탄
  });
  it('가진 철괴가 충분하면 철 광석은 필요 없다', () => {
    const p = plan(3, [['강철괴', 1], ['가죽+', 9], ['철괴', 9], ['석탄', 99]]);
    expect(line(p, '철괴')).toMatchObject({ need: 6, have: 9, short: 0, processCrafts: 0 });
    expect(line(p, '철 광석')).toBeUndefined();
    expect(p.shortKinds).toBe(0);
    expect(p.blocked.size).toBe(0);   // 가공만 하면 되니 막힌 것이 아니다
  });
  it('가공할 재료가 충분하면 가공 없이 끝난다', () => {
    const p = plan(3, [['강철괴', 6], ['가죽+', 6]]);
    expect(p.totalProcess).toBe(0);
    expect(line(p, '강철괴')).toMatchObject({ short: 0, processCrafts: 0 });
  });
  it('원재료가 모자라면 그 재료를 쓰는 완성품이 막힌다', () => {
    const p = plan(3, [['강철괴', 6], ['가죽+', 1]]);   // 가죽+ 5 모자라 가죽+ 가공(가죽 3·타닌 가루 4 → 가죽 → 생가죽)
    expect(line(p, '가죽+')).toMatchObject({ made: true, processCrafts: 2 });
    expect(line(p, '생가죽')).toBeDefined();
    expect(p.blocked.has('크레센트 엣지소드')).toBe(true);
  });
  it('여러 곳에서 쓰는 재료(석탄, 강철괴)는 합쳐서 센다: 합금강괴 → 강철괴 → 철괴', () => {
    // 론 엣지소드S 1개 = 합금강괴 3 + 가죽+ 3. 합금강괴 1회 = 강철괴 3 + 동 광석 15 + 석탄 8
    const p = planTargets({ '론 엣지소드S': 1 }, new Map([['가죽+', 9]]), RECIPES, new Map(), PROCESSES);
    expect(line(p, '합금강괴')).toMatchObject({ need: 3, short: 3, processCrafts: 1 });
    expect(line(p, '강철괴')).toMatchObject({ need: 3, short: 3, processCrafts: 1 });   // 합금강괴 가공에 드는 강철괴
    expect(line(p, '석탄')).toMatchObject({ need: 12 });                                // 합금강괴 8 + 강철괴 4
    expect(line(p, '철 광석')).toMatchObject({ need: 10 });
    expect(line(p, '동 광석')).toMatchObject({ need: 15 });
  });
  it('가공 레시피를 주지 않으면 예전처럼 2차 재료를 그대로 센다', () => {
    const p = planTargets({ '크레센트 엣지소드': 3 }, new Map([['강철괴', 1], ['가죽+', 9]]), RECIPES);
    expect(p.lines.map(l => l.name)).toEqual(['강철괴', '가죽+']);
    expect(p.totalProcess).toBe(0);
  });
});
