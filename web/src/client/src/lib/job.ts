/**
 * 직업 계열 분류 (WPF판 GetJobIcon과 같은 이름 규칙) → 직업별 SVG 아이콘 (이모지 대신, §6.4 아이콘 원칙).
 * 계열: 전사 / 궁수 / 마법 / 힐러 / 도적·격투 / 음유. 모르는 직업은 기본(별).
 */
export type JobKind = 'warrior' | 'archer' | 'mage' | 'healer' | 'rogue' | 'bard' | 'default';

export function jobKind(job: string | null | undefined): JobKind {
  if (!job || !job.trim()) return 'default';
  const j = job.toLowerCase();
  const has = (...k: string[]) => k.some(x => j.includes(x));
  if (has('전사', '대검', '검방', '기사', '검사', '워리어', '나이트')) return 'warrior';
  if (has('궁수', '장궁', '석궁', '아처', '헌터', '스나이퍼')) return 'archer';
  if (has('마법', '원소', '메이지', '위자드', '소서러', '술사')) return 'mage';
  if (has('힐러', '사제', '프리스트', '클레릭', '치유')) return 'healer';
  if (has('도적', '암살', '로그', '어쌔신', '시프', '격투')) return 'rogue';
  if (has('음유', '바드', '악사', '음악')) return 'bard';
  return 'default';
}

/** 아이콘 스프라이트의 이름 */
export const jobIconName = (job: string | null | undefined) => `job-${jobKind(job)}`;

/**
 * 직업별 이미지 (v1.5 요청 4): 넥슨 클래스 소개 페이지의 직업별 **전신 일러스트**. 직업은 얼굴이 아니라 전체 실루엣으로 구분하므로
 * 자르지 않고 전신을 그대로 보여 준다. 이미지는 서버가 첫 실행 때 내려받아 둔 것이고, 없으면 SVG 아이콘으로 대신한다.
 * 이름 → 이미지 id (서버 ClassImages.Known과 같은 id). 공백은 무시하고 비교한다.
 */
const CLASS_IMAGE_BY_NAME: Record<string, string> = {
  '견습 전사': 'warrior_1', '기사': 'warrior_2', '전사': 'warrior_3', '대검전사': 'warrior_4', '검술사': 'warrior_5',
  '견습 궁수': 'archer_1', '궁수': 'archer_2', '석궁사수': 'archer_3', '장궁병': 'archer_4',
  '견습 도적': 'thief_1', '도적': 'thief_2', '격투가': 'thief_3', '듀얼블레이드': 'thief_4',
  '견습 마법사': 'mage_1', '전격술사': 'mage_2', '마법사': 'mage_3', '화염술사': 'mage_4', '빙결술사': 'mage_5',
  '견습 음유시인': 'bard_1', '음유시인': 'bard_2', '댄서': 'bard_3', '악사': 'bard_4',
  '견습 힐러': 'healer_1', '암흑술사': 'healer_2', '힐러': 'healer_3', '사제': 'healer_4', '수도사': 'healer_5',
};
const norm = (s: string) => s.replace(/\s+/g, '');
const IMAGE_ID = new Map(Object.entries(CLASS_IMAGE_BY_NAME).map(([k, v]) => [norm(k), v]));

/** 직업 이름의 이미지 id. 모르는 직업이면 null */
export const classImageId = (job: string | null | undefined): string | null => (job ? IMAGE_ID.get(norm(job)) ?? null : null);

/** 전신 이미지를 비율 그대로 칸 안에 맞춰 보여 주는 배경 스타일 */
export const figureStyle = (id: string) => ({ backgroundImage: `url(/api/class-image/${id})` });
