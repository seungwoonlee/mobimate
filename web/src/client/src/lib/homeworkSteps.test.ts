import { entryShort, nextStep, prevStep, STEP_GOALS, stepOf, TABLE_ONLY_IDS } from './homeworkSteps';

describe('숙제 수동 횟수 순환', () => {
  it('0 → 1 → 2 → 3(완료) → 0으로 돈다', () => {
    const seq = [0];
    for (let i = 0; i < 4; i++) seq.push(nextStep(seq[i], 3));
    expect(seq).toEqual([0, 1, 2, 3, 0]);
  });
  it('뱅가드는 주 1회라 횟수 칸이 아니다', () => expect(STEP_GOALS.weekly_vanguard_breach).toBeUndefined());
  it('현재 횟수: 완료면 목표, 남은 횟수가 있으면 목표에서 뺀 값, 모르면 0', () => {
    expect(stepOf(3, true, null)).toBe(3);
    expect(stepOf(3, false, 2)).toBe(1);
    expect(stepOf(3, false, 3)).toBe(0);
    expect(stepOf(3, false, null)).toBe(0);
    expect(stepOf(3, false, 9)).toBe(0);   // 이상한 값은 0 아래로 내려가지 않는다
  });
});

describe('단계 카운터와 뒤로 가기', () => {
  it('소환의 결계는 7단계, 검은 구멍은 14회다', () => {
    expect(STEP_GOALS.weekly_barrier_1_7).toBe(7);
    expect(STEP_GOALS.weekly_black_hole_1_7).toBe(14);
    expect(TABLE_ONLY_IDS.has('weekly_barrier_1_7')).toBe(true);
    expect(TABLE_ONLY_IDS.has('weekly_vanguard_breach')).toBe(false);   // 뱅가드는 전체 탭 카드 칩에도 보인다
  });
  it('한 단계 뒤로는 0 아래로 내려가지 않는다', () => {
    expect(prevStep(3)).toBe(2);
    expect(prevStep(0)).toBe(0);
  });
  it('7단계는 7번 눌러야 완료이고 한 번 더 누르면 0', () => {
    let s = 0;
    for (let i = 0; i < 7; i++) s = nextStep(s, 7);
    expect(s).toBe(7);
    expect(nextStep(s, 7)).toBe(0);
  });
});

describe('입장 전투력 미달 비활성화', () => {
  const rec = [{ id: 'scars', name: '적월의 스카스', status: 'locked' as const, tier: null, maxEntry: null, entry: 104500, entryLevel: 0 }, { id: 'cavrak', name: '카브락', status: 'near' as const, tier: '입문', maxEntry: '입문', entry: 65000 }];
  it('입장 전투력에 못 미치면 필요한 전투력을 돌려준다', () => {
    expect(entryShort({ combat: 100000, recommend: rec }, 'raid_scars')).toBe('입장 전투력 104,500 필요 (현재 100,000)');
    expect(entryShort({ combat: 104500, recommend: rec }, 'raid_scars')).toBeNull();
    expect(entryShort({ combat: 100000, recommend: rec }, 'raid_cavrak')).toBeNull();
  });
  it('입장 레벨에 못 미쳐도 비활성화한다', () => {
    const ws = [{ id: 'white_succubus', name: '화이트 서큐버스', status: 'locked' as const, tier: null, maxEntry: null, entry: 27000, entryLevel: 65 }];
    expect(entryShort({ combat: 50000, level: 60, recommend: ws }, 'raid_white_succubus')).toBe('입장 레벨 65 필요 (현재 60)');
    expect(entryShort({ combat: 50000, level: 65, recommend: ws }, 'raid_white_succubus')).toBeNull();
  });
  it('전투력을 모르거나 전투력과 무관한 숙제는 건드리지 않는다', () => {
    expect(entryShort({ combat: 0, recommend: rec }, 'raid_scars')).toBeNull();
    expect(entryShort({ combat: 1, recommend: rec }, 'weekly_guild_regular')).toBeNull();
  });
});
