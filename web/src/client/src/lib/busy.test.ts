import { describe, expect, it } from 'vitest';
import { isBusy, runExclusive } from './busy';

describe('runExclusive', () => {
  it('같은 키가 진행 중이면 두 번째 호출은 실행하지 않는다', async () => {
    let calls = 0;
    let release!: () => void;
    const first = runExclusive('k', () => { calls++; return new Promise<string>(r => { release = () => r('a'); }); });
    expect(isBusy('k')).toBe(true);
    expect(await runExclusive('k', async () => { calls++; return 'b'; })).toBeUndefined();
    release();
    expect(await first).toBe('a');
    expect(calls).toBe(1);
    expect(isBusy('k')).toBe(false);
  });

  it('실패해도 잠금이 풀린다', async () => {
    await expect(runExclusive('e', async () => { throw new Error('x'); })).rejects.toThrow();
    expect(isBusy('e')).toBe(false);
  });
});
