import { render, screen } from '@testing-library/react';
import { RankBadge, rankLabel } from './RankBadge';
import { agoText } from '../features/rankings';

describe('RankBadge', () => {
  it('등급 이름과 순위를 접근 가능한 이름으로 준다', () => {
    render(<RankBadge badge={{ rank: 8, tier: 'gold', stale: false, at: '2026-10-05T00:00:00Z' }} />);
    expect(screen.getByRole('img', { name: '서버 8위 · 골드' }).classList.contains('gold')).toBe(true);
  });

  it('오래된 값은 표시한다', () => {
    render(<RankBadge badge={{ rank: 523, tier: 'pink', stale: true, at: '2026-10-05T00:00:00Z' }} />);
    expect(screen.getByRole('img', { name: '서버 523위 · 핑크 (오래된 값)' }).classList.contains('stale')).toBe(true);
  });

  it('순위를 모르면 아무것도 그리지 않는다', () => {
    const { container } = render(<><RankBadge badge={null} /><RankBadge tier="none" rank={20000} /><RankBadge tier="gold" rank={null} /></>);
    expect(container.innerHTML).toBe('');
  });

  it('천 단위 쉼표', () => expect(rankLabel(2400, 'purple')).toBe('서버 2,400위 · 보라'));
});

describe('agoText', () => {
  const t = (min: number) => new Date(Date.UTC(2026, 9, 5, 12, 0) - min * 60_000).toISOString();
  const now = Date.UTC(2026, 9, 5, 12, 0);
  it('경과 시간을 말로 바꾼다', () => {
    expect(agoText(t(0), now)).toBe('방금');
    expect(agoText(t(5), now)).toBe('5분 전');
    expect(agoText(t(125), now)).toBe('2시간 전');
    expect(agoText(t(60 * 49), now)).toBe('2일 전');
  });
});
