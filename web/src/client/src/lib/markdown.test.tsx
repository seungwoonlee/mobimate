import { render } from '@testing-library/react';
import { Markdown } from './markdown';

describe('Markdown (FR-AI-07)', () => {
  it('HTML은 해석하지 않고 글자로 보인다', () => {
    const { container } = render(<Markdown text={'<img src=x onerror=alert(1)> **굵게**'} />);
    expect(container.querySelector('img')).toBeNull();
    expect(container.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(container.querySelector('strong')?.textContent).toBe('굵게');
  });

  it('목록·코드 블록·인라인 코드', () => {
    const { container } = render(<Markdown text={'- 사과\n- `철광석`\n\n```\nstop_action\n```'} />);
    expect(container.querySelectorAll('li').length).toBe(2);
    expect(container.querySelector('li code')?.textContent).toBe('철광석');
    expect(container.querySelector('pre code')?.textContent).toBe('stop_action');
  });
});
