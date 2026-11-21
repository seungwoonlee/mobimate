import { COOK_RECIPES, MISSION_SLOTS, PROCESSES, RECIPES } from './missionData';

/**
 * 계정 재료 현황에서 기본으로 관리하는 재료 묶음 (2026-11-22 승운).
 * - 장비 재료: 잔영·해연 장비 제작에 쓰는 재료. 2026-11-22 CLI 제작 레시피(get_craftable_items, 잔영·해연 76종)의 재료와
 *   그 재료를 만드는 가공 레시피(get_alterable_items, docs/RELIC_EQUIP_RECIPES.md)를 원재료까지 따라 내려가 골랐다.
 *   단계: 영혼석(던전·어비스에서 얻는 것) / 2차 가공재 / 1차 원재료.
 * - 임무 재료: 마을 임무(채집·요리·제작)에 쓰는 것.
 * 그 밖의 아이템은 게임이 주는 분류(재료·장비 성장·조각·음식 …)로 고른다.
 */
export type GroupId = 'soul' | 'made' | 'raw' | 'mission' | 'fav';
export type Tier = '영혼석' | '2차' | '1차';
const TIER_GROUP: Record<Tier, GroupId> = { '영혼석': 'soul', '2차': 'made', '1차': 'raw' };
export const GROUPS: { id: GroupId; label: string; hint: string }[] = [
  { id: 'soul', label: '영혼석', hint: '잔영·해연 장비 재료: 영혼석·마력석·세공된 페리도트' },
  { id: 'made', label: '2차 재료', hint: '잔영·해연 장비 재료: 가공해서 만드는 것(백금강괴·특급 목재·특급 가죽 …)' },
  { id: 'raw', label: '1차 원재료', hint: '잔영·해연 장비 재료: 가공의 원료(광석·통나무·생가죽·양털 …)' },
  { id: 'mission', label: '임무 재료', hint: '마을 임무(채집·요리·제작)에 쓰는 재료' },
  { id: 'fav', label: '★ 찜', hint: '별을 눌러 직접 고른 재료' },
];

/** 잔영·해연 장비 재료 (CLI 레시피에서 고른 것, 위에서 아래로 = 장비에 직접 들어가는 것 → 원재료) */
export const EQUIP_MATERIALS: Readonly<Record<Tier, readonly string[]>> = {
  '영혼석': [
    '망령의 영혼석', '원념의 영혼석', '야생의 영혼석', '삼림의 영혼석', '공명의 영혼석', '파동의 영혼석',
    '포식의 마력석', '허상의 마력석', '심해의 마력석', '세공된 페리도트ZZ',
  ],
  '2차': [
    // 금속: 백금강괴 ← 운철괴 ← 은합금괴 ← 특수강괴 ← 합금강괴 ← 강철괴 ← 철괴
    '백금강괴', '운철괴', '은합금괴', '특수강괴', '합금강괴', '강철괴', '철괴',
    // 목재
    '특급 목재', '최상급 목재+', '최상급 목재', '상급 목재+', '상급 목재', '목재+', '목재',
    // 가죽
    '특급 가죽', '최상급 가죽+', '최상급 가죽', '상급 가죽+', '상급 가죽', '가죽+', '가죽',
    // 옷감·실크
    '특급 옷감', '최상급 옷감+', '최상급 옷감', '상급 옷감+', '상급 옷감', '옷감+', '옷감',
    '특급 실크', '최상급 실크', '상급 실크', '실크', '튼튼 버섯 진액', '타닌 가루',
  ],
  '1차': [
    '은 광석', '백동 광석', '동 광석', '철 광석', '광석', '석탄',
    '통나무', '상급 통나무', '나무 진액',
    '생가죽', '상급 생가죽',
    '양털', '상급 양털', '상급 양털+', '최상급 양털', '최상급 양털+', '특급 양털', '두꺼운 양털', '밀랍',
    '거미줄', '튼튼 버섯 포자', '나뭇가지',
  ],
};

const tierByName = new Map<string, Tier>(
  (Object.entries(EQUIP_MATERIALS) as [Tier, readonly string[]][]).flatMap(([t, names]) => names.map(n => [n, t] as const)));
// 임무 제작용 가공 레시피(missionData)의 재료도 장비 재료로 본다 (목록에 없으면 가공품 = 2차, 원료 = 1차)
const made = new Set(PROCESSES.map(p => p.name));
for (const p of PROCESSES) {
  if (!tierByName.has(p.name)) tierByName.set(p.name, '2차');
  for (const [n] of p.needs) if (!tierByName.has(n) && !made.has(n)) tierByName.set(n, '1차');
}
export const isRelicName = (n: string): boolean => n.includes('잔영') || n.includes('해연');

const mission = new Set([
  ...MISSION_SLOTS.map(s => s.name),
  ...[...RECIPES, ...COOK_RECIPES].flatMap(r => r.needs.map(([n]) => n)),
  ...COOK_RECIPES.map(r => r.name),
].filter(n => !tierByName.has(n)));

/** 장비 재료 안의 단계 */
export function tierOf(name: string): Tier | null {
  return tierByName.get(name) ?? (isRelicName(name) ? '영혼석' : null);
}

/** 이름이 속한 기본 묶음(장비 재료 단계가 임무 재료보다 먼저). 어디에도 없으면 null. */
export function baseGroupOf(name: string): GroupId | null {
  const t = tierOf(name);
  if (t) return TIER_GROUP[t];
  if (mission.has(name)) return 'mission';
  return null;
}

const TIER_ORDER: Tier[] = ['영혼석', '2차', '1차'];
/** 보유가 0이어도 항상 보여 줄 기본 관리 대상 (장비 재료는 영혼석 → 2차 → 1차 순) */
export const MANAGED: readonly { name: string; group: GroupId }[] = [
  ...TIER_ORDER.flatMap(t => [...tierByName].filter(([, x]) => x === t).map(([name]) => ({ name, group: TIER_GROUP[t] }))),
  ...[...mission].map(name => ({ name, group: 'mission' as const })),
];
/** 표 정렬용: 관리 목록 순서 */
export const MANAGED_ORDER: ReadonlyMap<string, number> = new Map(MANAGED.map((m, i) => [m.name, i]));
