import { describe, expect, it } from 'vitest';
import { gatherBatches, GATHER_BATCH, MAX_GATHER_TARGET, WINGS_PER_BATCH } from './life';

// 게임은 채집 한 번에 최대 100개: 목표가 크면 100개씩 올림해서 이어서 호출한다 (서버 GatherJob.Batches와 같은 규칙)
describe('gatherBatches', () => {
  it.each([[1, 1], [100, 1], [101, 2], [200, 2], [300, 3], [500, 5], [1000, 10]])('%i개 → %i회', (need, calls) => {
    expect(gatherBatches(need)).toBe(calls);
  });
  it('날개는 호출마다 5개', () => {
    expect(gatherBatches(300) * WINGS_PER_BATCH).toBe(15);
  });
  it('상한은 서버와 같은 5000개', () => {
    expect(MAX_GATHER_TARGET).toBe(5000);
    expect(GATHER_BATCH).toBe(100);
  });
});

import { cumulativeSeconds, SLOT_CHOICES } from './life';

// 가공은 앞에서부터 차례로 끝난다: 줄마다 앞선 줄의 남은 시간을 더한 값이 "그 품목이 나오기까지"다
describe('cumulativeSeconds', () => {
  it('진행 중인 줄은 앞에서부터 누적한다', () => {
    expect(cumulativeSeconds([600, 2994, 2994], [false, false, false])).toEqual([600, 3594, 6588]);
  });
  it('수거 대기(끝난) 줄은 0이고 누적에 더하지 않는다', () => {
    expect(cumulativeSeconds([0, 0, 12, 24], [true, true, false, false])).toEqual([0, 0, 12, 36]);
  });
  it('가공 칸 수는 사람마다 달라 2~7칸 중 고른다', () => expect(SLOT_CHOICES).toEqual([2, 3, 4, 5, 6, 7]));
});
