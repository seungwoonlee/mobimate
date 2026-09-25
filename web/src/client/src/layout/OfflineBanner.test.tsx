import { render, screen } from '@testing-library/react';
import { OfflineBanner } from './AppShell';
import { useUi } from '../state/ui';

// FR-MB-12: 끊김 배너는 다음 재시도까지 남은 초를 보여 주고, 재시도 시각이 지나면 "다시 연결 중"으로 바뀐다
describe('OfflineBanner', () => {
  it('남은 초를 센다', () => {
    useUi.setState({ everOpen: true, sse: 'retrying', retryAt: Date.now() + 4500 });
    render(<OfflineBanner />);
    expect(screen.getByRole('status').textContent).toMatch(/PC와 연결 끊김 · [45]초 후 다시 연결/);
  });

  it('재시도 중이면 다시 연결 중', () => {
    useUi.setState({ everOpen: true, sse: 'connecting', retryAt: undefined });
    render(<OfflineBanner />);
    expect(screen.getByRole('status').textContent).toContain('다시 연결 중');
  });
});
