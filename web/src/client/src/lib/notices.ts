/**
 * 인앱 알림 + 진동 (FR-MB-14, FR-HW-15): 서비스 워커 없이는 백그라운드 알림을 못 쓰므로
 * 화면이 켜져 있는 동안 토스트로 알리고, 가능한 기기(안드로이드)에서는 진동을 더한다.
 * `navigator.vibrate`는 iOS에서 지원되지 않고, 페이지에서 사용자 조작이 한 번 있은 뒤에만 동작한다. 지원하지 않으면 토스트만 띄운다.
 */
import type { Life } from '../api/types';

export function vibrate(pattern: number | number[] = [140, 80, 140]): boolean {
  try {
    const nav = navigator as Navigator & { userActivation?: { hasBeenActive: boolean } };
    if (typeof nav.vibrate !== 'function') return false;
    if (nav.userActivation && !nav.userActivation.hasBeenActive) return false;   // 조작 전에는 막힌다
    return nav.vibrate(pattern);
  } catch {
    return false;
  }
}

const keyOf = (w: { facility: string; name: string }) => `${w.facility}|${w.name}`;

/** 이전 조회와 비교해 새로 완료된 가공을 찾는다. 처음 조회(prev가 없음)는 알리지 않는다. */
export function newlyCompletedWorks(prev: Life['works'] | undefined, next: Life['works'] | undefined): string[] {
  if (!prev || !next) return [];
  const wasDone = new Map(prev.map(w => [keyOf(w), w.done]));
  return next.filter(w => w.done && wasDone.get(keyOf(w)) === false).map(w => w.name);
}

/** 가방이 가득(100%)이 되는 순간인가: 이전에는 100% 미만이었고 지금은 100% 이상. */
export function bagJustFull(prevPct: number | undefined, nextPct: number | undefined): boolean {
  return prevPct !== undefined && nextPct !== undefined && prevPct < 100 && nextPct >= 100;
}

/** 주간 리셋 1시간 전 알림이 필요한가 (FR-HW-15): 1시간 이내이고 미완료 주간 숙제가 있다. */
export function weeklyResetDue(nextResetIso: string, weeklyLeft: number, now = Date.now()): boolean {
  const ms = new Date(nextResetIso).getTime() - now;
  return weeklyLeft > 0 && ms > 0 && ms <= 3_600_000;
}
