/**
 * 점수 색상 규칙 (FR-DT-11, 게임 안의 색과 같게).
 * 전투력 103,000 이상 = 연보라 / 마도저항 4,400 이상 = 붉은 계통 /
 * 매력 점수에는 값 구간 색을 쓰지 않는다(2026-10-23 승운: 무과금은 골드 구간이 불가능해 구분이 무의미 — 서버 순위 색으로 대신한다).
 * 기준 아래의 전투력·마도저항은 기본 글자색이다. 색은 CSS 클래스(sc-*)로 입히고 값은 tokens.css에 있다.
 */
export type ScoreKind = 'combat' | 'mdef';
export type ScoreTone = 'base' | 'combat' | 'mdef';

export const COMBAT_HIGH = 103_000;
export const MDEF_HIGH = 4_400;

export function scoreTone(kind: ScoreKind, value: number | null | undefined): ScoreTone {
  const v = value ?? 0;
  if (kind === 'combat') return v >= COMBAT_HIGH ? 'combat' : 'base';
  if (kind === 'mdef') return v >= MDEF_HIGH ? 'mdef' : 'base';
  return 'base';
}

/** 값 옆에 붙일 CSS 클래스 이름 */
export const scoreClass = (kind: ScoreKind, value: number | null | undefined) => `sc-${scoreTone(kind, value)}`;

/** 생명·만복도 막대 색 (FR-DT-12): 넉넉함(ok) / 보통(warn) / 부족(danger) */
export function vitalLevel(pct: number): 'ok' | 'warn' | 'danger' {
  return pct >= 50 ? 'ok' : pct >= 25 ? 'warn' : 'danger';
}
