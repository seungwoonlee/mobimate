import { nextStep, STEP_GOALS, stepOf } from './homeworkSteps';

describe('숙제 수동 횟수 순환', () => {
  it('0 → 1 → 2 → 3(완료) → 0으로 돈다', () => {
    const seq = [0];
    for (let i = 0; i < 4; i++) seq.push(nextStep(seq[i], 3));
    expect(seq).toEqual([0, 1, 2, 3, 0]);
  });
  it('뱅가드는 3회다', () => expect(STEP_GOALS.weekly_vanguard_breach).toBe(3));
  it('현재 횟수: 완료면 목표, 남은 횟수가 있으면 목표에서 뺀 값, 모르면 0', () => {
    expect(stepOf(3, true, null)).toBe(3);
    expect(stepOf(3, false, 2)).toBe(1);
    expect(stepOf(3, false, 3)).toBe(0);
    expect(stepOf(3, false, null)).toBe(0);
    expect(stepOf(3, false, 9)).toBe(0);   // 이상한 값은 0 아래로 내려가지 않는다
  });
});
