import { COOK_RECIPES, MISSION_SLOTS, PROCESSES, RECIPES } from './missionData';

/**
 * 계정 재료 현황에서 기본으로 관리하는 재료 묶음 (2026-11-22 승운 요청).
 * 1차 원재료·2차 가공재·임무 재료는 임무 데이터(제작·가공 레시피)에서 끌어온다. 새 레시피를 missionData에 더하면 여기도 따라온다.
 * 잔영·해연 장비 재료는 게임이 장비 레시피를 알려 주지 않아 이름에 '잔영'·'해연'이 들어간 재료를 자동으로 모으고,
 * 정확한 목록(RELIC_MATERIALS)은 확인된 대로 더한다.
 */
export type GroupId = 'mission' | 'raw' | 'made' | 'relic' | 'fav' | 'timed';
export const GROUPS: { id: GroupId; label: string; hint: string }[] = [
  { id: 'mission', label: '임무 재료', hint: '마을 임무(채집·요리·제작)에 쓰는 재료' },
  { id: 'raw', label: '1차 원재료', hint: '가공 재료의 원료(광석·통나무·생가죽·양털 등)' },
  { id: 'made', label: '2차 가공재', hint: '가공해서 만드는 재료(철괴·강철괴·목재·가죽·옷감 등)' },
  { id: 'relic', label: '잔영·해연 장비', hint: '잔영·해연 장비 제작에 쓰는 재료' },
  { id: 'fav', label: '★ 찜', hint: '별을 눌러 직접 고른 재료' },
  { id: 'timed', label: '기간제', hint: '사용기간 제한이 있다고 표시한 것' },
];

/** 잔영·해연 장비 재료 이름(확인된 것). 이름에 '잔영'·'해연'이 든 재료는 여기 없어도 같은 묶음으로 본다. */
export const RELIC_MATERIALS: readonly string[] = [];
export const isRelicName = (n: string): boolean => RELIC_MATERIALS.includes(n) || n.includes('잔영') || n.includes('해연');

const made = new Set(PROCESSES.map(p => p.name));
const raw = new Set(PROCESSES.flatMap(p => p.needs.map(([n]) => n)).filter(n => !made.has(n)));
const mission = new Set([
  ...MISSION_SLOTS.map(s => s.name),
  ...[...RECIPES, ...COOK_RECIPES].flatMap(r => r.needs.map(([n]) => n)),
  ...COOK_RECIPES.map(r => r.name),
].filter(n => !made.has(n) && !raw.has(n)));

/** 이름이 속한 기본 묶음. 2차 → 1차 → 임무 → 잔영·해연 순으로 먼저 맞는 하나만. 어디에도 없으면 null. */
export function baseGroupOf(name: string): GroupId | null {
  if (made.has(name)) return 'made';
  if (raw.has(name)) return 'raw';
  if (mission.has(name)) return 'mission';
  if (isRelicName(name)) return 'relic';
  return null;
}

/** 보유가 0이어도 항상 보여 줄 기본 관리 대상 (묶음 순서대로) */
export const MANAGED: readonly { name: string; group: GroupId }[] = [
  ...[...mission].map(name => ({ name, group: 'mission' as const })),
  ...[...raw].map(name => ({ name, group: 'raw' as const })),
  ...[...made].map(name => ({ name, group: 'made' as const })),
  ...RELIC_MATERIALS.map(name => ({ name, group: 'relic' as const })),
];
