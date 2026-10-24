/**
 * 여러 번 해야 끝나는 숙제의 수동 횟수 (2026-10-24 승운): 일부만 했을 수 있어, 칸을 누를 때마다 0 → 1 → 2 → … → 목표(완료) → 0으로 돈다.
 * 서버(HomeworkDefinition.RunGoal)의 값과 같아야 한다. 뱅가드 브리치 주 3회가 첫 사례다.
 */
export const STEP_GOALS: Readonly<Record<string, number>> = { weekly_vanguard_breach: 3 };

/** 지금 몇 번 했나. 완료면 목표, 미완료인데 남은 횟수를 알면 목표 − 남은 횟수, 모르면 0 */
export function stepOf(goal: number, done: boolean, left: number | null | undefined): number {
  if (done) return goal;
  return left == null ? 0 : Math.min(goal, Math.max(0, goal - left));
}

/** 누르면 다음 횟수: 0 → 1 → … → goal → 0 */
export const nextStep = (cur: number, goal: number): number => (cur + 1) % (goal + 1);
