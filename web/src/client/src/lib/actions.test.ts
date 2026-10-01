import { collect, setHomework, startGather, stopAction } from './actions';
import { useUi } from '../state/ui';

// FR-MB-12: 끊긴 동안 조작은 보내지 않고 알린다. 긴급 정지는 예외로 보낸다(안전 기능).
describe('끊김 중 조작', () => {
  beforeEach(() => {
    useUi.setState({ everOpen: true, sse: 'retrying', toasts: [] });
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ data: { collected: 'a' } }), { status: 200, headers: { 'content-type': 'application/json' } })));
  });
  afterEach(() => vi.unstubAllGlobals());

  it('수거·채집은 보내지 않는다', async () => {
    expect(await collect('질긴 가죽')).toBe(false);
    expect(await startGather('사과', 10)).toBe(false);
    await setHomework('raid_cavrak', { completed: true });
    expect(fetch).not.toHaveBeenCalled();
    expect(useUi.getState().toasts.at(-1)?.message).toContain('보내지 않았습니다');
  });

  it('긴급 정지는 보낸다', async () => {
    await stopAction();
    expect(fetch).toHaveBeenCalledWith('/api/actions/stop', expect.objectContaining({ method: 'POST' }));
  });
});
