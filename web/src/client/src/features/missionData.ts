import { plain } from '../lib/format';

/**
 * 임무 데이터 (2026-10-24 승운): 마을별 채집·요리 임무용 아이템과 제작 레시피. 새로 필요한 것은 여기에 더한다.
 * 이름이 정확히 같은 것만 임무용으로 본다(유니티 색 태그는 떼고 비교).
 * 이름에 '|'가 있으면 앞이 화면에 보이는 이름이고 뒤는 게임이 달리 부를 수 있는 표기다(어느 쪽이든 같은 아이템으로 센다).
 */
export const TOWNS = ['티르코네일', '던바튼', '콜헨', '반호르', '이멘마하'] as const;
export type Kind = '채집' | '요리';
export interface MissionInfo { town: string; kind: Kind; canon: string }

const MISSION_TABLE: Record<string, Record<Kind, string[]>> = {
  '티르코네일': { '채집': ['거미줄', '양털', '블러디 허브', '밀', '감자'], '요리': ['달걀 프라이|달걀프라이'] },
  '던바튼': { '채집': ['쑥쑥 버섯', '우유', '철 광석', '옥수수', '양파'], '요리': ['사과 주스'] },
  '콜헨': { '채집': ['화살꽃', '달걀', '상급 통나무+', '조개'], '요리': ['조개찜'] },
  '반호르': { '채집': ['콩', '파스닙'], '요리': ['통감자구이'] },
  '이멘마하': { '채집': ['쌀', '호박', '헤이즐넛', '끈적 풀'], '요리': ['마요네즈 고기볶음'] },
};

/** 목록 순서대로의 임무용 아이템(화면에 보이는 이름 기준). */
export const MISSION_SLOTS: readonly { town: string; kind: Kind; name: string }[] = Object.entries(MISSION_TABLE)
  .flatMap(([town, kinds]) => (Object.entries(kinds) as [Kind, string[]][]).flatMap(([kind, names]) => names.map(n => ({ town, kind, name: n.split('|')[0] }))));

const MISSION_INFO: ReadonlyMap<string, MissionInfo> = new Map(
  Object.entries(MISSION_TABLE).flatMap(([town, kinds]) => (Object.entries(kinds) as [Kind, string[]][]).flatMap(([kind, names]) =>
    names.flatMap(n => { const [canon, ...alias] = n.split('|'); return [canon, ...alias].map(a => [a, { town, kind, canon }] as const); }))));
export const MISSION_ITEMS: ReadonlySet<string> = new Set(MISSION_INFO.keys());
export const missionInfo = (name: string): MissionInfo | undefined => MISSION_INFO.get(plain(name).trim());
export const isMissionItem = (name: string): boolean => MISSION_INFO.has(plain(name).trim());

/**
 * 제작 레시피 (2026-10-24 승운이 게임에서 확인해 확정, docs/MISSION_CRAFT_RECIPES.md). 1회 제작 기준이다.
 * produces = 1회에 나오는 개수. needs = [재료 이름, 1회에 드는 개수].
 * 장비류는 게임이 개수를 알려 주지 않아(get_items에 장비가 없다) 만든 개수는 세지 않는다. 재료만 센다.
 */
export interface Recipe {
  town: string; name: string; produces: number; needs: readonly (readonly [string, number])[];
  /** 게임이 다른 이름으로 부를 수 있는 표기(완성품 개수를 셀 때) */
  alias?: string;
  /** true면 필요 개수를 아직 확정하지 못했다. 알려진 재료만 힌트로 보이고 계산에서는 뺀다. */
  unverified?: boolean;
}
export const RECIPES: readonly Recipe[] = [
  { town: '티르코네일', name: '전문 캠프파이어 키트', produces: 1, needs: [['나뭇가지', 5], ['상급 통나무+', 4], ['돌멩이', 10]] },
  { town: '티르코네일', name: '마법 유탄 부품', produces: 5, needs: [['분해된 장비 부품', 50], ['연금술 부스러기', 50], ['마나 허브', 10], ['협곡의 마나석', 1]] },
  { town: '던바튼', name: '크레센트 엣지소드', produces: 1, needs: [['강철괴', 2], ['가죽+', 2]] },
  { town: '던바튼', name: '그랜드 크로스보우', produces: 1, needs: [['강철괴', 2], ['목재+', 2]] },
  { town: '던바튼', name: '로터스 힐링 완드', produces: 1, needs: [['강철괴', 2], ['목재+', 2]] },
  { town: '콜헨', name: '사슬 갑옷 신발', produces: 1, needs: [['강철괴', 2], ['가죽+', 1]] },
  { town: '콜헨', name: '두꺼운 가죽 갑옷 신발', produces: 1, needs: [['강철괴', 1], ['가죽+', 2]] },
  { town: '콜헨', name: '두꺼운 전투복 신발', produces: 1, needs: [['강철괴', 1], ['옷감+', 2]] },
  { town: '반호르', name: '론 엣지소드S', produces: 1, needs: [['합금강괴', 3], ['가죽+', 3]] },
  { town: '반호르', name: '라이트 크로스보우S', produces: 1, needs: [['강철괴', 3], ['상급 목재', 3]] },
  { town: '반호르', name: '마블 힐링 완드S', produces: 1, needs: [['강철괴', 3], ['상급 목재', 3]] },
  { town: '이멘마하', name: '비늘 갑옷 신발S', produces: 1, needs: [['합금강괴', 3], ['가죽+', 2]] },
  { town: '이멘마하', name: '가죽 갑옷 신발S', produces: 1, needs: [['상급 가죽', 3], ['강철괴', 2]] },
  { town: '이멘마하', name: '전투복 신발S', produces: 1, needs: [['상급 옷감', 3], ['강철괴', 2]] },
];

/**
 * 요리 레시피 (2026-10-24 승운이 게임 제작 화면에서 확인해 확정, docs/MISSION_CRAFT_RECIPES.md). 1회 제작 기준, 1회에 1개 나온다.
 * 요리 이름은 임무용 아이템(완성품) 이름과 같아서 가진 개수를 셀 수 있다. CLI 이름은 '달걀프라이'(띄어쓰기 없음)다.
 */
export const COOK_RECIPES: readonly Recipe[] = [
  { town: '티르코네일', name: '달걀 프라이', alias: '달걀프라이', produces: 1, needs: [['달걀', 1], ['소금', 1]] },
  { town: '던바튼', name: '사과 주스', produces: 1, needs: [['사과', 4], ['허브', 2], ['물이 든 병', 1], ['설탕', 2]] },
  { town: '콜헨', name: '조개찜', produces: 1, needs: [['조개', 10], ['물이 든 병', 5], ['레몬', 4], ['허브', 3]] },
  { town: '반호르', name: '통감자구이', produces: 1, needs: [['감자', 4], ['허브', 2], ['설탕', 2]] },
  { town: '이멘마하', name: '마요네즈 고기볶음', produces: 1, needs: [['고기', 4], ['양배추', 1], ['마요네즈', 1]] },
];

/**
 * 가공 레시피 (2026-10-25 승운이 가공 화면에서 확인, docs/MISSION_CRAFT_RECIPES.md). 제작 재료(강철괴·목재+ 같은 2차 재료)를 만드는 법이다.
 * 한 번 가공하면 3개가 나온다. 모자란 2차 재료는 이 레시피로 원재료까지 풀어서 계산한다.
 * 가공법을 모르는 재료는 원재료처럼 센다.
 */
export interface Process { name: string; produces: number; needs: readonly (readonly [string, number])[]; facility: string; minutes: number }
export const PROCESSES: readonly Process[] = [
  { name: '합금강괴', produces: 3, needs: [['강철괴', 3], ['동 광석', 15], ['석탄', 8]], facility: '금속 가공 시설 Lv.2', minutes: 100 },
  { name: '강철괴', produces: 3, needs: [['철괴', 3], ['석탄', 4]], facility: '금속 가공 시설 Lv.1', minutes: 10 },
  { name: '철괴', produces: 3, needs: [['철 광석', 10]], facility: '금속 가공 시설 Lv.1', minutes: 1 },
  { name: '상급 목재', produces: 3, needs: [['목재+', 3], ['상급 통나무', 15], ['나무 진액', 8]], facility: '목재 가공 시설 Lv.2', minutes: 100 },
  { name: '목재+', produces: 3, needs: [['목재', 3], ['나무 진액', 4]], facility: '목재 가공 시설 Lv.1', minutes: 12 },
  { name: '목재', produces: 3, needs: [['통나무', 10]], facility: '목재 가공 시설 Lv.1', minutes: 1.5 },
  { name: '상급 가죽', produces: 3, needs: [['가죽+', 3], ['상급 생가죽', 15], ['타닌 가루', 8]], facility: '가죽 가공 시설 Lv.2', minutes: 100 },
  { name: '가죽+', produces: 3, needs: [['가죽', 3], ['타닌 가루', 4]], facility: '가죽 가공 시설 Lv.1', minutes: 10 },
  { name: '가죽', produces: 3, needs: [['생가죽', 10]], facility: '가죽 가공 시설 Lv.1', minutes: 1 },
  { name: '상급 옷감', produces: 3, needs: [['옷감+', 3], ['상급 양털', 15], ['양털', 8]], facility: '옷감 가공 시설 Lv.2', minutes: 100 },
  { name: '옷감+', produces: 3, needs: [['옷감', 3], ['양털', 4]], facility: '옷감 가공 시설 Lv.1', minutes: 12 },
  { name: '옷감', produces: 3, needs: [['양털', 10]], facility: '옷감 가공 시설 Lv.1', minutes: 1.5 },
];
