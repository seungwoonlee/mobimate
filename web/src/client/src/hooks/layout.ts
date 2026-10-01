import { useEffect, useState, useSyncExternalStore } from 'react';

/** 크기 클래스 (REQUIREMENTS §6.5): Compact < 600 ≤ Medium < 840 ≤ Expanded < 1200 ≤ Large. */
export type SizeClass = 'compact' | 'medium' | 'expanded' | 'large';
export type Posture = 'flat' | 'book' | 'tabletop';

export interface Layout {
  size: SizeClass;
  short: boolean;     // 높이 < 480
  coarse: boolean;    // 터치 위주 (FR-MB-07)
  narrow: boolean;    // 폭 < 360: 글랜스 모드 (FR-MB-06)
  landscape: boolean; // 가로 화면(폭 > 높이): 폰이 아니면 채팅을 기본으로 연다 (v1.5)
  posture: Posture;
  width: number;
}

export function sizeOf(w: number): SizeClass {
  if (w < 600) return 'compact';
  if (w < 840) return 'medium';
  if (w < 1200) return 'expanded';
  return 'large';
}

function mq(q: string) {
  try { return window.matchMedia(q).matches; } catch { return false; }
}

function read(): Layout {
  const w = window.innerWidth, h = window.innerHeight;
  // 화면 구역(Viewport Segments) 지원 기기에서만 자세를 판정한다. 미지원이면 flat (상세설계 §4.5, M5).
  const posture: Posture = mq('(horizontal-viewport-segments: 2)') ? 'book' : mq('(vertical-viewport-segments: 2)') ? 'tabletop' : 'flat';
  return { size: sizeOf(w), short: h < 480, coarse: mq('(pointer: coarse)'), narrow: w < 360, landscape: w > h, posture, width: w };
}

let cache = read();
const subs = new Set<() => void>();
function onResize() {
  const next = read();
  if (next.size !== cache.size || next.short !== cache.short || next.coarse !== cache.coarse || next.narrow !== cache.narrow || next.landscape !== cache.landscape || next.posture !== cache.posture) {
    cache = next;
    subs.forEach(f => f());
  } else {
    cache.width = next.width;
  }
}
window.addEventListener('resize', onResize);

/** 레이아웃이 바뀔 때만 다시 그린다(폭 1px 변화마다 다시 그리지 않음). 상태는 zustand에 있어 레이아웃이 바뀌어도 유지된다 (FR-MB-03). */
export function useLayout(): Layout {
  return useSyncExternalStore(cb => { subs.add(cb); return () => { subs.delete(cb); }; }, () => cache);
}

/** 1초마다 바뀌는 현재 시각 (카운트다운용). */
export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(t);
  }, [intervalMs]);
  return now;
}
