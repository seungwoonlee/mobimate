import { useSyncExternalStore } from 'react';

/**
 * 직업 이미지가 서버에 있는지 (한 번만 확인해 모든 아이콘이 함께 쓴다).
 * 이미지는 서버가 첫 실행 때 내려받아 둔다. 없으면(아직·실패) SVG 아이콘으로 보인다. 화면을 새로 열면 다시 확인한다.
 */
type State = 'loading' | 'ok' | 'no';
const status = new Map<string, State>();
const subs = new Set<() => void>();
const notify = () => subs.forEach(f => f());

function load(kind: string) {
  if (status.has(kind)) return;
  status.set(kind, 'loading');
  const img = new Image();
  img.onload = () => { status.set(kind, 'ok'); notify(); };
  img.onerror = () => { status.set(kind, 'no'); notify(); };
  img.src = `/api/class-image/${kind}`;
}

export function useClassImage(id: string | null): State {
  if (id) load(id);
  return useSyncExternalStore(
    cb => { subs.add(cb); return () => { subs.delete(cb); }; },
    () => (id ? status.get(id) ?? 'loading' : 'no'),
  );
}
