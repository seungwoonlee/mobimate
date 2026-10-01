import { reportError, resetErrorReportLimit } from './errorReport';

// FR-ST-03: 분당 10건, 메시지·출처만 (스택·개인 데이터 없음)
describe('화면 오류 보고', () => {
  beforeEach(() => {
    resetErrorReportLimit();
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{}')));
  });
  afterEach(() => vi.unstubAllGlobals());

  it('서버 로그로 메시지와 출처를 보낸다', () => {
    expect(reportError('boom', 'app.js:10', 1000)).toBe(true);
    expect(fetch).toHaveBeenCalledWith('/api/client-errors', expect.objectContaining({ method: 'POST' }));
    const body = JSON.parse((vi.mocked(fetch).mock.calls[0][1] as RequestInit).body as string);
    expect(body).toEqual({ message: 'boom', source: 'app.js:10' });
  });

  it('분당 10건을 넘기면 보내지 않고, 1분 뒤에는 다시 보낸다', () => {
    for (let i = 0; i < 10; i++) expect(reportError(`e${i}`, 's', 5000)).toBe(true);
    expect(reportError('e11', 's', 5001)).toBe(false);
    expect(fetch).toHaveBeenCalledTimes(10);
    expect(reportError('later', 's', 5000 + 60_001)).toBe(true);
  });

  it('긴 메시지는 잘라 보낸다', () => {
    reportError('가'.repeat(1000), 'x'.repeat(500), 1000);
    const body = JSON.parse((vi.mocked(fetch).mock.calls[0][1] as RequestInit).body as string);
    expect(body.message.length).toBe(300);
    expect(body.source.length).toBe(100);
  });

  it('보내기가 실패해도 예외를 내지 않는다', () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new Error('offline'))));
    expect(() => reportError('x', 'y', 1000)).not.toThrow();
  });
});
