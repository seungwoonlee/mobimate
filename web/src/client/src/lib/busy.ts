import { useSyncExternalStore } from 'react';

/**
 * 진행 중인 조작 표시 (중복 전송 방지).
 * 같은 키의 조작이 끝나기 전에는 다시 시작하지 않고, 화면의 버튼은 그동안 눌리지 않게 한다.
 * 게임에 가는 조작은 응답이 느릴 수 있어서, 연달아 눌러 같은 명령이 두 번 나가는 일을 막는다.
 */
const running = new Map<string, number>();
const subs = new Set<() => void>();
let version = 0;
const emit = () => { version++; subs.forEach(f => f()); };

export const isBusy = (key?: string) => (key ? (running.get(key) ?? 0) > 0 : running.size > 0);

/** 같은 키가 진행 중이면 실행하지 않고 undefined를 돌려준다. */
export async function runExclusive<T>(key: string, fn: () => Promise<T>): Promise<T | undefined> {
  if (isBusy(key)) return undefined;
  running.set(key, 1);
  emit();
  try {
    return await fn();
  } finally {
    running.delete(key);
    emit();
  }
}

/** 키(생략하면 아무 조작이나)가 진행 중인가. 버튼의 disabled에 쓴다. */
export function useBusy(key?: string): boolean {
  useSyncExternalStore(cb => { subs.add(cb); return () => { subs.delete(cb); }; }, () => version);
  return isBusy(key);
}
