import { isOffline, useUi } from './ui';

// FR-MB-12: 한 번 연결된 뒤 SSE가 열려 있지 않으면 끊김. 처음 연결 중에는 끊김이 아니다.
describe('isOffline', () => {
  it('처음 연결 중은 끊김이 아니다', () => {
    expect(isOffline({ everOpen: false, sse: 'connecting' })).toBe(false);
    expect(isOffline({ everOpen: false, sse: 'retrying' })).toBe(false);
  });
  it('한 번 연결된 뒤 open이 아니면 끊김', () => {
    expect(isOffline({ everOpen: true, sse: 'open' })).toBe(false);
    expect(isOffline({ everOpen: true, sse: 'connecting' })).toBe(true);
    expect(isOffline({ everOpen: true, sse: 'retrying' })).toBe(true);
  });
  it('setSse(open)이 everOpen을 켠다', () => {
    useUi.getState().setSse('open');
    useUi.getState().setSse('retrying', Date.now() + 2000);
    expect(isOffline(useUi.getState())).toBe(true);
  });
});
