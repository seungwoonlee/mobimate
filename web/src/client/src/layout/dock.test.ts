import { describe, expect, it } from 'vitest';
import { clampDock, DOCK_MIN } from './Dock';

describe('채팅 폭 제한', () => {
  it('너무 좁거나 넓게 끌어도 범위 안으로 맞춘다', () => {
    expect(clampDock(100, 1600)).toBe(DOCK_MIN);
    expect(clampDock(5000, 3000)).toBe(760);
    expect(clampDock(500, 1600)).toBe(500);
  });
  it('창이 좁으면 본문(360px)과 레일(72px)이 남도록 상한을 줄인다', () => {
    expect(clampDock(700, 1000)).toBe(444);
    expect(clampDock(700, 700)).toBe(DOCK_MIN);   // 아주 좁아도 최소 폭 밑으로는 내리지 않는다
  });
});
