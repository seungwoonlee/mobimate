import type { CutoffContent, CutoffStatus } from '../api/types';
import { CardHead, Icon, Pill } from '../components/ui';
import { fmt } from '../lib/format';

/** 상태 배지 (FR-CO-04). 색은 테마 토큰으로 정한다(WPF판의 고정 색 대신). */
export const CUTOFF_STATUS: Record<CutoffStatus, { label: string; tone: 'violet' | 'warn' | 'gold' | 'danger' }> = {
  overwhelm: { label: '압도', tone: 'violet' },
  near: { label: '압도 근접', tone: 'gold' },
  marginal: { label: '턱걸이', tone: 'warn' },
  locked: { label: '입장 불가', tone: 'danger' },
};

/**
 * 콘텐츠 추천 3행 카드 (FR-CO-07, WPF v1.2.0):
 * 1행 입장·추천 난이도와 배지, 2행 압도 달성률과 남은 전투력, 3행 상위 난이도 가이드(강조색 lime).
 */
export function CutoffCard({ c }: { c: CutoffContent }) {
  const st = CUTOFF_STATUS[c.status];
  return (
    <article className="card cut" aria-label={`${c.name} 추천`}>
      <CardHead
        icon={c.id === 'abyss' ? 'dungeon' : 'trophy'}
        title={c.name}
        right={c.status === 'locked'
          ? <Pill tone="danger"><Icon name="lock" size={12} />입장 불가</Pill>
          : <span className="cut-tiers"><span className="faint">입장</span> <Pill tone="info">{c.maxEntryTier}</Pill> <span className="faint">추천</span> <Pill tone="plain">{c.recommendedTier}</Pill> <Pill tone={st.tone}>{st.label}</Pill></span>}
      />
      <p className="cut-line">{line2(c)}</p>
      <p className={`cut-next ${c.status === 'locked' ? 'danger' : c.next ? 'lime' : 'violet'}`}>{line3(c)}</p>
    </article>
  );
}

function line2(c: CutoffContent): string {
  if (c.status === 'locked' && c.entryShort) {
    const parts = [c.entryShort.combatShort > 0 && `투력 ${fmt(c.entryShort.combatShort)}`, c.entryShort.mdefShort > 0 && `저항 ${fmt(c.entryShort.mdefShort)}`].filter(Boolean);
    return `${c.entryShort.tier} 기준 ${parts.join(', ')} 부족`;
  }
  if (c.status === 'overwhelm') return '압도 달성';
  const base = `압도 전투력의 ${c.overwhelmPct}% (투력 ${fmt(c.combatToOverwhelm)} 더 채우면 압도)`;
  return c.mdefShort > 0 ? `${base}, 저항 ${fmt(c.mdefShort)} 부족` : base;
}

function line3(c: CutoffContent): string {
  if (c.status === 'locked') return `${c.entryShort?.tier ?? '첫'} 단계 입장 스펙 달성 필요`;
  if (!c.next) return '최고 난이도 도전 가능';
  const n = c.next;
  if (n.readyNow) return `상위(${n.tier}) 즉시 도전 가능`;
  if (n.combatShort > 0 && n.mdefShort > 0) return `투력 ${fmt(n.combatShort)}, 저항 ${fmt(n.mdefShort)} 올리면 상위(${n.tier}) 도전 가능`;
  if (n.mdefShort > 0) return `저항 ${fmt(n.mdefShort)}만 올리면 상위(${n.tier}) 도전 가능`;
  return `투력 ${fmt(n.combatShort)}만 올리면 상위(${n.tier}) 도전 가능`;
}
