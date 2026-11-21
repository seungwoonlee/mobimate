import { describe, expect, it } from 'vitest';
import { baseGroupOf, isRelicName, MANAGED } from './stockData';

describe('재료 묶음', () => {
  it('가공품은 2차, 그 원료는 1차, 임무 아이템은 임무로 나뉜다', () => {
    expect(baseGroupOf('강철괴')).toBe('made');
    expect(baseGroupOf('철 광석')).toBe('raw');
    expect(baseGroupOf('쌀')).toBe('mission');
    expect(baseGroupOf('알 수 없는 것')).toBeNull();
  });
  it('잔영·해연 이름은 자동으로 장비 재료 묶음', () => {
    expect(isRelicName('잔영의 파편')).toBe(true);
    expect(baseGroupOf('해연 결정')).toBe('relic');
  });
  it('기본 관리 목록에 같은 재료가 두 번 나오지 않는다', () => {
    const names = MANAGED.map(m => m.name);
    expect(new Set(names).size).toBe(names.length);
  });
});
