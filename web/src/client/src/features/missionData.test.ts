import { COOK_RECIPES, isMissionItem, MISSION_ITEMS, MISSION_SLOTS, missionInfo, RECIPES, TOWNS } from './missionData';

// 임무용 아이템 (2026-10-24 승운): 이름이 정확히 같은 것만 임무용으로 묶는다.
describe('임무용 아이템', () => {
  it('채집 20·요리 5종이 들어 있다(표기 변형 1개 포함 26)', () => expect(MISSION_ITEMS.size).toBe(26));
  it('화면에 보이는 칸은 25개이고, 표기 변형도 같은 칸으로 센다', () => {
    expect(MISSION_SLOTS).toHaveLength(25);
    expect(MISSION_SLOTS.filter(s => s.kind === '채집')).toHaveLength(20);
    expect(missionInfo('달걀프라이')?.canon).toBe('달걀 프라이');
  });
  it('채집물·요리를 알아본다', () => {
    for (const n of ['옥수수', '끈적 풀', '상급 통나무+', '쑥쑥 버섯', '조개찜', '마요네즈 고기볶음', '양털', '철 광석', '화살꽃', '헤이즐넛', '달걀 프라이', '달걀프라이'])
      expect(isMissionItem(n), n).toBe(true);
  });
  it('마을과 종류를 알려 준다', () => {
    expect(missionInfo('옥수수')).toMatchObject({ town: '던바튼', kind: '채집' });
    expect(missionInfo('조개찜')).toMatchObject({ town: '콜헨', kind: '요리' });
    expect(missionInfo('마요네즈 고기볶음')).toMatchObject({ town: '이멘마하', kind: '요리' });
    expect(missionInfo('통나무')).toBeUndefined();
  });
  it('이름 앞뒤 공백과 유니티 색 태그는 무시한다', () => {
    expect(isMissionItem('  콩 ')).toBe(true);
    expect(isMissionItem('<color=#FF0000>밀</color>')).toBe(true);
  });
  it('이름이 정확히 같지 않으면 일반 아이템이다', () => {
    for (const n of ['옥수수가루', '밀가루', '통나무', '상급 통나무', '감자튀김', '론 엣지소드S', '마법 유탄 부품'])
      expect(isMissionItem(n), n).toBe(false);
  });
});

describe('제작 레시피', () => {
  it('14종이고 마을 순서(티르코네일→이멘마하)로 놓여 있다', () => {
    expect(RECIPES).toHaveLength(14);
    const order = RECIPES.map(r => TOWNS.indexOf(r.town as (typeof TOWNS)[number]));
    expect(order).toEqual([...order].sort((a, b) => a - b));
    expect(new Set(RECIPES.map(r => r.name)).size).toBe(14);
  });
  it('마법 유탄 부품만 1회에 5개 나온다', () => {
    expect(RECIPES.filter(r => r.produces !== 1).map(r => r.name)).toEqual(['마법 유탄 부품']);
  });
});

describe('요리 레시피', () => {
  it('마을마다 하나씩 5종이고 모두 확정이다', () => {
    expect(COOK_RECIPES.map(r => r.town)).toEqual([...TOWNS]);
    expect(COOK_RECIPES.some(r => r.unverified)).toBe(false);
    expect(COOK_RECIPES.find(r => r.name === '조개찜')?.needs).toEqual([['조개', 10], ['물이 든 병', 5], ['레몬', 4], ['허브', 3]]);
  });
  it('요리 이름은 임무용 아이템(완성품) 이름과 같아서 가진 개수를 셀 수 있다', () => {
    for (const r of COOK_RECIPES) expect(isMissionItem(r.name), r.name).toBe(true);
  });
});
