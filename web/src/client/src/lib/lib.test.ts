import { AdaptiveRefresh } from './refresh';
import { remaining, short, until } from './format';

// Core InteractiveFeaturesTests의 AdaptiveRefreshController 테스트 벡터와 같다 (FR-RF-01).
describe('AdaptiveRefresh', () => {
  it('기본 15초', () => {
    expect(new AdaptiveRefresh().current).toBe(15);
  });

  it('활동이 없으면 15초씩 늘어 최대 300초', () => {
    const r = new AdaptiveRefresh();
    expect(r.onTick()).toBe(30);
    expect(r.onTick()).toBe(45);
    for (let i = 0; i < 30; i++) r.onTick();
    expect(r.current).toBe(300);
  });

  it('활동이 있으면 즉시 15초, 다음 틱도 15초', () => {
    const r = new AdaptiveRefresh();
    r.onTick(); r.onTick();
    r.recordActivity();
    expect(r.current).toBe(15);
    expect(r.onTick()).toBe(15);
    expect(r.onTick()).toBe(30);
  });

  it('설정한 최대 주기를 넘지 않는다', () => {
    const r = new AdaptiveRefresh(60);
    for (let i = 0; i < 10; i++) r.onTick();
    expect(r.current).toBe(60);
  });
});

describe('format', () => {
  it('남은 시간 (FR-DT-05)', () => {
    expect(remaining(0)).toBe('수거 대기');
    expect(remaining(145)).toBe('2분 25초');
    expect(remaining(10995)).toBe('3시간 3분 15초');
  });

  it('축약 (글랜스 모드)', () => {
    expect(short(9999)).toBe('9,999');
    expect(short(124017)).toBe('124K');
    expect(short(11144590)).toBe('11.1M');
  });

  it('리셋까지 남은 시간', () => {
    const now = Date.parse('2026-09-25T00:00:00Z');
    expect(until('2026-09-25T03:12:00Z', now)).toBe('3시간 12분');
    expect(until('2026-09-27T05:00:00Z', now)).toBe('2일 5시간');
  });
});
