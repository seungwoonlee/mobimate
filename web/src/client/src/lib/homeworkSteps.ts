import type { CharacterCard } from '../api/types';
/**
 * 여러 번 해야 끝나는 숙제의 수동 횟수 (2026-10-24 승운): 일부만 했을 수 있어, 칸을 누를 때마다 0 → 1 → 2 → … → 목표(완료) → 0으로 돈다.
 * 서버(HomeworkDefinition.RunGoal)의 값과 같아야 한다. 뱅가드 브리치는 2026-11-10부터 주 1회라 여기서 뺐다(완료/미완료 칸).
 */
export const STEP_GOALS: Readonly<Record<string, number>> = { weekly_barrier_1_7: 7, weekly_black_hole_1_7: 14, weekly_nightmare_drill: 30 };

/** 단계 칸의 단위 (없으면 `N/목표`로 보인다): 나이트메어 드릴은 `12구역` */
export const STEP_UNITS: Readonly<Record<string, string>> = { weekly_nightmare_drill: '구역' };

/** 손으로 수치를 입력하는 칸(나이트메어 드릴 랭킹): 처음엔 `-`, 눌러서 순위를 입력하면 완료처럼 보인다 */
export const VALUE_UNITS: Readonly<Record<string, string>> = { weekly_nightmare_rank: '위', weekly_memorial_saga: '층' };
export const MAX_VALUE = 99999;

/** 숙제 화면의 칸에만 보이고 전체 탭 카드의 칩으로는 보이지 않는 단계 카운터(소환의 결계 1~7단계, 검은 구멍 주간 14회) */
export const TABLE_ONLY_IDS: ReadonlySet<string> = new Set(['weekly_barrier_1_7', 'weekly_black_hole_1_7', 'weekly_nightmare_drill', 'weekly_nightmare_rank', 'weekly_memorial_saga']);

/** 지금 몇 번 했나. 완료면 목표, 미완료인데 남은 횟수를 알면 목표 − 남은 횟수, 모르면 0 */
export function stepOf(goal: number, done: boolean, left: number | null | undefined): number {
  if (done) return goal;
  return left == null ? 0 : Math.min(goal, Math.max(0, goal - left));
}

/** 누르면 다음 횟수: 0 → 1 → … → goal → 0 */
export const nextStep = (cur: number, goal: number): number => (cur + 1) % (goal + 1);

/** 한 단계 뒤로(Shift·우클릭): 0 아래로는 내려가지 않는다. 잘못 눌러 한 바퀴 돌지 않게 한다. */
export const prevStep = (cur: number): number => Math.max(0, cur - 1);

/** 시즌제 칸(주간 리셋으로 지우지 않는다): 칸 설명에 "시즌 리셋 : 기한 모름"을 붙인다 */
export const SEASON_NOTE = '시즌 리셋 : 기한 모름';
export const SEASON_IDS: ReadonlySet<string> = new Set(['weekly_nightmare_drill', 'weekly_nightmare_rank', 'weekly_memorial_saga']);

/** 숙제 칸 → 추천(전투력 기준) 콘텐츠. 입장 전투력에 못 미치는 캐릭터는 그 칸을 비활성화한다 */
export const HW_CUTOFF: Record<string, string> = {
  weekly_vanguard_breach: 'vanguard_breach',
  raid_scars: 'scars', raid_cavrak: 'cavrak', raid_airel: 'airel', raid_white_succubus: 'white_succubus',
  abyss_illusory_anchorage: 'abyss', abyss_madness_cave: 'abyss', abyss_scattered_waterway: 'abyss',
};

/** 이 캐릭터가 그 숙제의 입장 조건(전투력·레벨)에 못 미치면 이유 글, 아니면 null (전투력·레벨을 아직 모르면 null) */
export function entryShort(c: Partial<Pick<CharacterCard, 'combat' | 'level' | 'recommend' | 'homework'>>, id: string): string | null {
  // 게임이 직접 알려 주는 잠금(성장 가이드의 UnlockHint)이 가장 확실하다: "85레벨에 사용할 수 있습니다."
  const locked = c.homework?.find(h => h.id === id)?.locked;
  if (locked) return locked;
  const cut = HW_CUTOFF[id];
  if (!cut) return null;
  const r = c.recommend?.find(x => x.id === cut);
  const need = r?.entry ?? 0;
  if (c.combat && need > c.combat) return `입장 전투력 ${need.toLocaleString()} 필요 (현재 ${c.combat.toLocaleString()})`;
  const lv = r?.entryLevel ?? 0;
  if (c.level && lv > c.level) return `입장 레벨 ${lv} 필요 (현재 ${c.level})`;
  return null;
}
