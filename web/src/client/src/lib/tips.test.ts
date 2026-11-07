import { describe, expect, it } from 'vitest';
import { TIPS, tipsFor } from './tips';

describe('상단 팁', () => {
  it('게임 PC가 아니면 PC 전용 팁(음성 입력 등)은 빠진다', () => {
    const phone = tipsFor(false);
    expect(phone.length).toBeGreaterThan(0);
    expect(phone.every(t => !t.pc)).toBe(true);
    expect(tipsFor(true)).toHaveLength(TIPS.length);
  });
  it('한 줄에 들어가도록 너무 길지 않고, 중복이 없다', () => {
    for (const t of TIPS) expect(t.text.length).toBeLessThanOrEqual(80);
    expect(new Set(TIPS.map(t => t.text)).size).toBe(TIPS.length);
  });
  it('사용자가 정한 대표 팁 세 가지가 들어 있다', () => {
    const all = TIPS.map(t => t.text).join('\n');
    expect(all).toContain('gemma4:e4b');
    expect(all).toContain('페르소나');
    expect(all).toContain('왼쪽 Ctrl + Space');
  });
});
