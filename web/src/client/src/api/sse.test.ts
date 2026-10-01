import { connectSse } from './sse';

/** 가짜 EventSource: 열린 연결 수를 센다 */
class FakeEs {
  static all: FakeEs[] = [];
  static readonly CLOSED = 2;
  readyState = 0;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  constructor(public url: string) { FakeEs.all.push(this); }
  addEventListener() { /* 이벤트는 이 테스트에서 쓰지 않는다 */ }
  close() { this.readyState = FakeEs.CLOSED; }
  static open() { return FakeEs.all.filter(e => e.readyState !== FakeEs.CLOSED); }
}

describe('connectSse (FR-MB-12)', () => {
  beforeEach(() => {
    FakeEs.all = [];
    vi.useFakeTimers();
    vi.stubGlobal('EventSource', FakeEs);
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('재연결을 기다리는 중에 화면이 다시 보여도 연결은 하나만 남는다', async () => {
    const states: string[] = [];
    const stop = connectSse({ onEvent: () => {}, onState: s => states.push(s) });

    FakeEs.all[0].onerror!();                                  // 끊김 → 재시도 예약
    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));     // 폰이 깨어남 → 바로 연결
    await vi.advanceTimersByTimeAsync(2000);                   // 예약된 재시도가 와도 하나만 남아야 한다 (감시 타이머 45초까지는 가지 않는다)

    expect(FakeEs.open().length).toBe(1);
    expect(states).toContain('retrying');
    stop();
    expect(FakeEs.open().length).toBe(0);
  });
});
