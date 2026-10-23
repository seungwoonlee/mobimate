import type { NameBadgeData, RankTierName } from '../api/types';

const TIER_LABEL: Record<RankTierName, string> = { gold: '골드', orange: '주황', pink: '핑크', purple: '보라', none: '' };
/** 한 글자짜리 로마숫자 기호(Ⅰ Ⅱ Ⅲ Ⅳ, U+2160~2163): 인게임 딱지처럼 알파벳 여러 글자(III)로 쓰지 않는다 */
const NUMERAL = ['', 'Ⅰ', 'Ⅱ', 'Ⅲ', 'Ⅳ'];

/** 서버 순위 등급: 10위 이내 골드 · 100위 이내 주황 · 1000위 이내 핑크 · 10000위 이내 보라 */
export const TIER_RULE = '10위 이내 골드 · 100위 이내 주황 · 1000위 이내 핑크 · 10000위 이내 보라';

export const tierLabel = (t: RankTierName) => TIER_LABEL[t];

export function rankLabel(rank: number, tier: RankTierName): string {
  return `서버 ${rank.toLocaleString('ko-KR')}위${tier !== 'none' ? ` · ${TIER_LABEL[tier]}` : ''}`;
}

/**
 * 인게임에서 캐릭터 이름 옆에 붙는 "딱지" 예측 (2026-10-23): 캐릭터당 하나.
 * 색 = 전투력·생활력·매력·종합 네 분류의 서버 순위 중 가장 높은 등급, 로마숫자 = 그 색 등급에 든 분류의 개수.
 * 어느 분류도 1만 위 안이 아니면 null이라 아무것도 그리지 않는다 = 딱지를 못 받는다.
 */
export function NameBadge({ badge, big }: { badge: NameBadgeData | null | undefined; big?: boolean }) {
  if (!badge) return null;
  const n = Math.min(Math.max(badge.count, 1), 4);
  const label = `예상 딱지 ${TIER_LABEL[badge.tier]} ${n}`;
  return <span className={`rank-badge ${badge.tier}${big ? ' big' : ''}`} role="img" aria-label={label} title={`${label} (${n}개 분류가 ${TIER_LABEL[badge.tier]} 등급)`}><span aria-hidden="true">{NUMERAL[n]}</span></span>;
}

/**
 * 분류별 서버 순위 숫자("6,827위"). 인게임 딱지와 맞추려고 숫자 자체를 등급 색으로 크고 굵게 보인다
 * (10위 이내 골드·100위 이내 주황·1000위 이내 핑크·10000위 이내 보라). 등급 밖이면 일반 글자색이다.
 */
export function RankText({ rank, tier, className = '' }: { rank: number; tier: RankTierName; className?: string }) {
  return <b className={`rk-n num ${tier === 'none' ? '' : `t-${tier}`} ${className}`.trim()} title={tier === 'none' ? undefined : `${TIER_LABEL[tier]} 등급`}>{rank.toLocaleString('ko-KR')}위</b>;
}
