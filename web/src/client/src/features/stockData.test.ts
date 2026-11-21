import { describe, expect, it } from 'vitest';
import { baseGroupOf, MANAGED, tierOf } from './stockData';

describe('재료 묶음', () => {
  it('잔영·해연 장비 재료는 영혼석·2차·1차 단계로 한 묶음이다', () => {
    expect(baseGroupOf('백금강괴')).toBe('equip');
    expect(tierOf('백금강괴')).toBe('2차');
    expect(tierOf('망령의 영혼석')).toBe('영혼석');
    expect(tierOf('은 광석')).toBe('1차');
    expect(tierOf('강철괴')).toBe('2차');
  });
  it('임무 아이템은 임무 재료', () => {
    expect(baseGroupOf('쌀')).toBe('mission');
    expect(baseGroupOf('알 수 없는 것')).toBeNull();
  });
  it('기본 관리 목록에 같은 재료가 두 번 나오지 않는다', () => {
    const names = MANAGED.map(m => m.name);
    expect(new Set(names).size).toBe(names.length);
  });
});
