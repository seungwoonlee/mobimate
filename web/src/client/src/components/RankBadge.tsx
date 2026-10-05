import type { RankBadgeData, RankTierName } from '../api/types';

const TIER_LABEL: Record<RankTierName, string> = { gold: '골드', orange: '주황', pink: '핑크', purple: '보라', none: '' };

/** 서버 순위 등급 문구: 10위 이내 골드, 100위 이내 주황, 1000위 이내 핑크, 10000위 이내 보라 */
export const TIER_RULE = '10위 이내 골드 · 100위 이내 주황 · 1000위 이내 핑크 · 10000위 이내 보라';

export function rankLabel(rank: number, tier: RankTierName, stale = false): string {
  return `서버 ${rank.toLocaleString('ko-KR')}위${tier !== 'none' ? ` · ${TIER_LABEL[tier]}` : ''}${stale ? ' (오래된 값)' : ''}`;
}

/** 전투력 옆에 다는 작은 마름모 뱃지. 등급 색은 보라·핑크·주황·골드. 순위를 모르면 아무것도 그리지 않는다. */
export function RankBadge({ badge, tier, rank, stale }: { badge?: RankBadgeData | null; tier?: RankTierName; rank?: number | null; stale?: boolean }) {
  const t = badge?.tier ?? tier ?? 'none';
  const r = badge?.rank ?? rank ?? null;
  if (t === 'none' || r == null) return null;
  const label = rankLabel(r, t, badge?.stale ?? stale ?? false);
  return (
    <span className={`rank-badge ${t}${(badge?.stale ?? stale) ? ' stale' : ''}`} role="img" aria-label={label} title={label}>
      <svg viewBox="0 0 12 12" aria-hidden="true"><path d="M6 .8 11.2 6 6 11.2.8 6z" fill="currentColor" /></svg>
    </span>
  );
}
