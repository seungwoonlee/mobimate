/**
 * 점수 색상 규칙 (FR-DT-11, 게임 안의 색과 같게).
 * 전투력 103,000 이상 = 연보라 / 마도저항 4,400 이상 = 붉은 계통 /
 * 매력 8,000 미만 = 파랑, 8,000~15,000 미만 = 보라, 15,000~37,000 미만 = 핑크, 37,000 이상 = 골드.
 * 기준 아래의 전투력·마도저항은 기본 글자색이다. 색은 CSS 클래스(sc-*)로 입히고 값은 tokens.css에 있다.
 */
export type ScoreKind = 'combat' | 'mdef' | 'attract';
export type ScoreTone = 'base' | 'combat' | 'mdef' | 'att-blue' | 'att-purple' | 'att-pink' | 'att-gold';

export const COMBAT_HIGH = 103_000;
export const MDEF_HIGH = 4_400;
export const ATTRACT_STEPS = [8_000, 15_000, 37_000] as const;

export function scoreTone(kind: ScoreKind, value: number | null | undefined): ScoreTone {
  const v = value ?? 0;
  if (kind === 'combat') return v >= COMBAT_HIGH ? 'combat' : 'base';
  if (kind === 'mdef') return v >= MDEF_HIGH ? 'mdef' : 'base';
  if (v < ATTRACT_STEPS[0]) return 'att-blue';
  if (v < ATTRACT_STEPS[1]) return 'att-purple';
  if (v < ATTRACT_STEPS[2]) return 'att-pink';
  return 'att-gold';
}

/** 값 옆에 붙일 CSS 클래스 이름 */
export const scoreClass = (kind: ScoreKind, value: number | null | undefined) => `sc-${scoreTone(kind, value)}`;
