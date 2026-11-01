import { describe, expect, it } from 'vitest';
import { planItem } from './missions';

describe('채집 임무 준비 계획', () => {
  it('가방에 충분하면 아무것도 꺼내거나 채집하지 않는다', () => {
    expect(planItem(10, { bag: 12 })).toEqual({ bag: 12, fromAccount: 0, fromCharacter: 0, gather: 0 });
  });
  it('모자란 만큼을 계정 창고에서 먼저 꺼낸다', () => {
    expect(planItem(30, { bag: 5, account: 40, character: 9 })).toEqual({ bag: 5, fromAccount: 25, fromCharacter: 0, gather: 0 });
  });
  it('계정 창고가 모자라면 캐릭터 창고, 그래도 모자라면 채집한다', () => {
    expect(planItem(50, { bag: 5, account: 10, character: 15 })).toEqual({ bag: 5, fromAccount: 10, fromCharacter: 15, gather: 20 });
  });
  it('창고가 비어 있으면 모두 채집한다', () => {
    expect(planItem(20, {})).toEqual({ bag: 0, fromAccount: 0, fromCharacter: 0, gather: 20 });
  });
});
