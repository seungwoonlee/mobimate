import { groupItems, isMissionItem, MISSION_ITEMS, MISSION_SLOTS, missionInfo } from './inventory';
import type { InvItem } from '../api/types';

// 임무용 아이템 묶음 (2026-10-24 승운): 이름이 정확히 같은 것만 임무용으로 묶는다.
describe('임무용 아이템', () => {
  it('채집 20·요리 5종이 들어 있다(표기 변형 1개 포함 26)', () => expect(MISSION_ITEMS.size).toBe(26));
  it('채집물·요리를 알아본다', () => {
    for (const n of ['옥수수', '끈적 풀', '상급 통나무+', '쑥쑥 버섯', '조개찜', '마요네즈 고기볶음', '양털', '철 광석', '화살꽃', '헤이즐넛', '달걀 프라이', '달걀프라이'])
      expect(isMissionItem(n), n).toBe(true);
  });
  it('화면에 보이는 칸은 채집 20·요리 5종 25개이고, 표기 변형도 같은 칸으로 센다', () => {
    expect(MISSION_SLOTS).toHaveLength(25);
    expect(missionInfo('달걀프라이')?.canon).toBe('달걀 프라이');
  });
  it('마을과 종류를 알려 준다', () => {
    expect(missionInfo('옥수수')).toMatchObject({ town: '던바튼', kind: '채집' });
    expect(missionInfo('조개찜')).toMatchObject({ town: '콜헨', kind: '요리' });
    expect(missionInfo('마요네즈 고기볶음')).toMatchObject({ town: '이멘마하', kind: '요리' });
    expect(missionInfo('론 엣지소드S')).toBeUndefined();   // 제작품(장비)은 개수를 알 수 없어 뺐다
    expect(missionInfo('통나무')).toBeUndefined();
  });
  it('이름 앞뒤 공백과 유니티 색 태그는 무시한다', () => {
    expect(isMissionItem('  콩 ')).toBe(true);
    expect(isMissionItem('<color=#FF0000>밀</color>')).toBe(true);
  });
  it('이름이 정확히 같지 않으면 일반 아이템이다', () => {
    for (const n of ['옥수수가루', '밀가루', '통나무', '상급 통나무', '비늘 갑옷 신발', '론 엣지소드', '감자튀김', '론 엣지소드S', '마법 유탄 부품'])
      expect(isMissionItem(n), n).toBe(false);
  });
});


describe('groupItems', () => {
  test('변화량은 위치마다 한 번만 더하고, 줄어든 것은 음수로 남는다', () => {
    const row = (location: string, count: number, sessionDelta: number): InvItem => ({ location, name: '옥수수', category: '재료', count, locked: false, sessionDelta, favorite: false });
    const [g] = groupItems([row('bag', 2, -3), row('bag', 1, -3), row('account', 5, 4)]);
    expect(g.sessionDelta).toBe(1);
    expect(g.total).toBe(8);
  });
  const it = (location: string, name: string, count: number): InvItem => ({ location, name, category: '재료', count, locked: false, sessionDelta: 0, favorite: false });
  test('같은 이름을 위치별 개수와 총합으로 합친다', () => {
    const g = groupItems([it('bag', '옥수수', 3), it('account', '옥수수', 5), it('character', '옥수수', 2), it('bag', '옥수수', 1), it('bag', '밀', 9)]);
    const c = g.find(x => x.name === '옥수수')!;
    expect(c.total).toBe(11);
    expect(c.by).toEqual({ bag: 4, account: 5, character: 2 });
    expect(g).toHaveLength(2);
  });
});
