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

  it('재연결 확인 중에 화면이 다시 보여도 연결은 하나만 남는다', async () => {
    let release!: (r: Response) => void;
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(r => { release = r; })));
    const states: string[] = [];
    const stop = connectSse({ onEvent: () => {}, onState: s => states.push(s) });

    FakeEs.all[0].onerror!();                                  // 끊김 → /api/session 확인 중
    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));     // 폰이 깨어남 → 바로 연결
    release(new Response('{}', { status: 200 }));              // 늦게 끝난 확인
    await vi.advanceTimersByTimeAsync(2000);   // 감시 타이머(45초)까지는 가지 않는다

    expect(FakeEs.open().length).toBe(1);
    stop();
    expect(FakeEs.open().length).toBe(0);
  });

  it('세션이 없으면(401) 재시도를 멈춘다', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 401 })));
    const states: string[] = [];
    const stop = connectSse({ onEvent: () => {}, onState: s => states.push(s) });
    FakeEs.all[0].onerror!();
    await vi.runAllTimersAsync();
    expect(states.at(-1)).toBe('unauthorized');
    expect(FakeEs.open().length).toBe(0);
    stop();
  });
});
