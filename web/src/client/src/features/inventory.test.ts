import { isMissionItem, MISSION_ITEMS } from './inventory';

// 임무용 아이템 묶음 (2026-10-24 승운): 이름이 정확히 같은 것만 임무용으로 묶는다.
describe('임무용 아이템', () => {
  it('승운이 준 21개가 모두 들어 있다', () => expect(MISSION_ITEMS.size).toBe(21));
  it('채집물·요리·도구·장비를 알아본다', () => {
    for (const n of ['옥수수', '끈적 풀', '상급 통나무+', '쑥쑥 버섯', '조개찜', '마요네즈 고기볶음', '전문 캠프파이어 키트', '마법 유탄 부품', '비늘 갑옷 신발S', '론 엣지소드S'])
      expect(isMissionItem(n), n).toBe(true);
  });
  it('이름 앞뒤 공백과 유니티 색 태그는 무시한다', () => {
    expect(isMissionItem('  콩 ')).toBe(true);
    expect(isMissionItem('<color=#FF0000>밀</color>')).toBe(true);
  });
  it('이름이 정확히 같지 않으면 일반 아이템이다', () => {
    for (const n of ['옥수수가루', '밀가루', '통나무', '상급 통나무', '비늘 갑옷 신발', '론 엣지소드', '감자튀김'])
      expect(isMissionItem(n), n).toBe(false);
  });
});
