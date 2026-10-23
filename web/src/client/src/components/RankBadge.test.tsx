import { render, screen } from '@testing-library/react';
import { NameBadge, rankLabel, TierDot } from './RankBadge';
import { agoText } from '../features/rankings';

describe('NameBadge (인게임 딱지 예측)', () => {
  it('색은 등급, 숫자는 그 등급에 든 분류 수를 로마숫자로', () => {
    const { container } = render(<>
      <NameBadge badge={{ tier: 'purple', count: 1 }} />
      <NameBadge badge={{ tier: 'pink', count: 2 }} />
      <NameBadge badge={{ tier: 'orange', count: 3 }} />
      <NameBadge badge={{ tier: 'gold', count: 4 }} />
    </>);
    const els = [...container.querySelectorAll('.rank-badge')];
    expect(els.map(e => e.textContent)).toEqual(['Ⅰ', 'Ⅱ', 'Ⅲ', 'Ⅳ']);   // Ⅰ Ⅱ Ⅲ Ⅳ 한 글자 기호
    expect(els.map(e => [...e.classList].find(c => ['gold', 'orange', 'pink', 'purple'].includes(c)))).toEqual(['purple', 'pink', 'orange', 'gold']);
  });

  it('접근 가능한 이름으로 색과 숫자를 말한다', () => {
    render(<NameBadge badge={{ tier: 'pink', count: 2 }} />);
    expect(screen.getByRole('img', { name: '예상 딱지 핑크 2' })).toBeTruthy();
  });

  it('딱지를 못 받으면(null) 아무것도 그리지 않는다', () => {
    const { container } = render(<><NameBadge badge={null} /><NameBadge badge={undefined} /></>);
    expect(container.innerHTML).toBe('');
  });

  it('숫자는 1~4로 맞춘다', () => {
    const { container } = render(<><NameBadge badge={{ tier: 'gold', count: 0 }} /><NameBadge badge={{ tier: 'gold', count: 9 }} /></>);
    expect([...container.querySelectorAll('.rank-badge')].map(e => e.textContent)).toEqual(['Ⅰ', 'Ⅳ']);
  });
});

describe('TierDot·rankLabel', () => {
  it('등급 밖이면 점을 그리지 않는다', () => {
    const { container } = render(<><TierDot tier="none" /><TierDot tier="gold" /></>);
    expect(container.querySelectorAll('.tier-dot').length).toBe(1);
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
