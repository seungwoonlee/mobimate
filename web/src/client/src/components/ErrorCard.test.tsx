import { act, render, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { ApiError } from '../api/http';
import { ErrorCard, isTransientCliError } from './ui';

// 2026-10-22 승운 요청: 게임 CLI의 일시적인 실패는 화면에 오류로 보여 줄 필요가 없다. 사용자가 고쳐야 하는 오류는 그대로 보인다.
describe('ErrorCard', () => {
  afterEach(() => { vi.useRealTimers(); });

  it.each(['CLI_TIMEOUT', 'CLI_FAILED', 'PARSE_ERROR'])('일시적인 CLI 실패(%s)는 오류 카드를 띄우지 않는다', code => {
    expect(isTransientCliError(new ApiError(502, code, '게임 CLI 응답 시간 초과 (타임아웃)'))).toBe(true);
    render(<ErrorCard error={new ApiError(502, code, '게임 CLI 응답 시간 초과 (타임아웃)')} onRetry={() => {}} />);
    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.queryByText(/타임아웃/)).toBeNull();
  });

  it.each([['CLI_MISSING', 502], ['UNAUTHORIZED', 401], ['NOT_FOUND', 404]])('사용자가 알아야 하는 오류(%s)는 그대로 보인다', (code, status) => {
    expect(isTransientCliError(new ApiError(status as number, code as string, '고쳐야 하는 문제'))).toBe(false);
    render(<ErrorCard error={new ApiError(status as number, code as string, '고쳐야 하는 문제')} onRetry={() => {}} />);
    expect(screen.getByRole('alert').textContent).toContain('고쳐야 하는 문제');
  });

  it('API 오류가 아닌 일반 오류도 그대로 보인다', () => {
    render(<ErrorCard error={new Error('네트워크 끊김')} />);
    expect(screen.getByRole('alert').textContent).toContain('네트워크 끊김');
  });

  it('일시적인 실패는 3초 뒤 저절로 다시 시도한다', () => {
    vi.useFakeTimers();
    const retry = vi.fn();
    render(<ErrorCard error={new ApiError(502, 'CLI_TIMEOUT', 'x')} onRetry={retry} />);
    expect(retry).not.toHaveBeenCalled();
    act(() => { vi.advanceTimersByTime(3100); });
    expect(retry).toHaveBeenCalledTimes(1);
  });
});
