/**
 * 여러 번 해야 끝나는 숙제의 수동 횟수 (2026-10-24 승운): 일부만 했을 수 있어, 칸을 누를 때마다 0 → 1 → 2 → … → 목표(완료) → 0으로 돈다.
 * 서버(HomeworkDefinition.RunGoal)의 값과 같아야 한다. 뱅가드 브리치 주 3회가 첫 사례다.
 */
export const STEP_GOALS: Readonly<Record<string, number>> = { weekly_vanguard_breach: 3, weekly_barrier_1_7: 7, weekly_black_hole_1_7: 7 };

/** 숙제 화면의 칸에만 보이고 전체 탭 카드의 칩으로는 보이지 않는 단계 카운터(소환의 결계·검은 구멍 주간 1~7단계) */
export const TABLE_ONLY_IDS: ReadonlySet<string> = new Set(['weekly_barrier_1_7', 'weekly_black_hole_1_7']);

/** 지금 몇 번 했나. 완료면 목표, 미완료인데 남은 횟수를 알면 목표 − 남은 횟수, 모르면 0 */
export function stepOf(goal: number, done: boolean, left: number | null | undefined): number {
  if (done) return goal;
  return left == null ? 0 : Math.min(goal, Math.max(0, goal - left));
}

/** 누르면 다음 횟수: 0 → 1 → … → goal → 0 */
export const nextStep = (cur: number, goal: number): number => (cur + 1) % (goal + 1);

/** 한 단계 뒤로(Shift·우클릭): 0 아래로는 내려가지 않는다. 잘못 눌러 한 바퀴 돌지 않게 한다. */
export const prevStep = (cur: number): number => Math.max(0, cur - 1);
