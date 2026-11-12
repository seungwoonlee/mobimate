import { collect, onePerFacility, setHomework, startGather, stopAction } from './actions';
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

describe('수거는 시설마다 한 번만 보낸다', () => {
  const works = [
    { name: '실크', facility: '옷감 가공 시설' }, { name: '가죽', facility: '가죽 가공 시설' },
    { name: '새록 버섯 진액', facility: '약품 가공 시설' }, { name: '튼튼 버섯 진액', facility: '약품 가공 시설' },
  ];
  it('같은 시설의 두 번째 이름은 뺀다(게임이 시설의 완료분을 한 번에 거둔다)', () => {
    expect(onePerFacility(['실크', '가죽', '새록 버섯 진액', '튼튼 버섯 진액'], works)).toEqual(['실크', '가죽', '새록 버섯 진액']);
  });
  it('시설을 모르는 이름은 그대로 보낸다', () => {
    expect(onePerFacility(['처음 보는 것', '실크'], works)).toEqual(['처음 보는 것', '실크']);
    expect(onePerFacility(['실크', '실크'], undefined)).toEqual(['실크', '실크']);
  });
});
