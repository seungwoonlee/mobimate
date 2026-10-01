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
