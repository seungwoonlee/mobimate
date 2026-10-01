import { bagJustFull, newlyCompletedWorks, vibrate, weeklyResetDue } from './notices';
import type { Work } from '../api/types';

const w = (name: string, done: boolean): Work => ({ name, facility: '베틀', remainingSeconds: done ? 0 : 60, done, remainingText: '', kind: 'cloth', kindLabel: '옷감 가공' });

describe('알림 판정 (FR-MB-14·FR-HW-15)', () => {
  it('새로 완료된 가공만 알린다', () => {
    expect(newlyCompletedWorks([w('실크', false), w('옷감', true)], [w('실크', true), w('옷감', true)])).toEqual(['실크']);
    expect(newlyCompletedWorks(undefined, [w('실크', true)])).toEqual([]);   // 처음 조회는 알리지 않는다
    expect(newlyCompletedWorks([w('실크', false)], [w('실크', false)])).toEqual([]);
    expect(newlyCompletedWorks([], [w('새 작업', true)])).toEqual([]);        // 처음 보는 작업이 이미 완료면 알리지 않는다
  });

  it('가방은 100%를 넘는 순간에만 알린다', () => {
    expect(bagJustFull(99.9, 100)).toBe(true);
    expect(bagJustFull(100, 101)).toBe(false);
    expect(bagJustFull(undefined, 100)).toBe(false);
    expect(bagJustFull(80, 90)).toBe(false);
  });

  it('주간 리셋 1시간 전 + 남은 숙제가 있을 때만', () => {
    const now = Date.parse('2026-10-05T14:00:00Z');
    expect(weeklyResetDue('2026-10-05T14:59:00Z', 3, now)).toBe(true);
    expect(weeklyResetDue('2026-10-05T15:01:00Z', 3, now)).toBe(false);   // 아직 1시간 넘게 남음
    expect(weeklyResetDue('2026-10-05T14:30:00Z', 0, now)).toBe(false);   // 다 했음
    expect(weeklyResetDue('2026-10-05T13:59:00Z', 3, now)).toBe(false);   // 이미 지남
  });

  it('진동을 지원하지 않으면 false (iOS)', () => {
    expect(vibrate()).toBe(false);
  });

  it('진동을 지원하고 사용자 조작이 있었으면 진동한다', () => {
    const v = vi.fn(() => true);
    vi.stubGlobal('navigator', { vibrate: v, userActivation: { hasBeenActive: true } });
    expect(vibrate([100])).toBe(true);
    expect(v).toHaveBeenCalledWith([100]);
    vi.stubGlobal('navigator', { vibrate: v, userActivation: { hasBeenActive: false } });
    expect(vibrate()).toBe(false);
    vi.unstubAllGlobals();
  });
});
