import { create } from 'zustand';

/** 화면 경로 (상세설계 §4.2). 도크는 경로가 아니라 UI 상태다. */
export type RouteName = 'overview' | 'stats' | 'homework' | 'inventory' | 'currencies' | 'life' | 'nearby' | 'settings';

export const ROUTES: { name: RouteName; path: string; label: string; icon: string; key?: string }[] = [
  { name: 'overview', path: '/', label: '개요', icon: 'home', key: '1' },
  { name: 'stats', path: '/stats', label: '스탯', icon: 'stats', key: '2' },
  { name: 'inventory', path: '/inventory', label: '가방', icon: 'bag', key: '3' },
  { name: 'currencies', path: '/currencies', label: '재화', icon: 'coins', key: '4' },
  { name: 'homework', path: '/homework', label: '숙제', icon: 'list', key: '5' },
  { name: 'life', path: '/life', label: '생활', icon: 'leaf', key: '6' },
  { name: 'nearby', path: '/nearby', label: '레이더', icon: 'radar', key: '7' },
  { name: 'settings', path: '/settings', label: '설정', icon: 'gear' },
];

export interface Location { name: RouteName; params: URLSearchParams }

function parse(): Location {
  // 호환: 미션 화면은 숙제의 탭으로 옮겼다. 주소도 바꿔 두어야 탭을 옮길 수 있다.
  if (window.location.pathname.replace(/\/+$/, '') === '/missions') window.history.replaceState(null, '', '/homework?tab=missions');
  const path = window.location.pathname.replace(/\/+$/, '') || '/';
  const r = ROUTES.find(x => x.path === path);
  return { name: r?.name ?? 'overview', params: new URLSearchParams(window.location.search) };
}

interface RouterState {
  loc: Location;
  go: (name: RouteName, params?: Record<string, string>) => void;
  /** 서버가 준 경로(예: AI 명령의 "/homework?tab=daily")로 이동. 모르는 경로는 무시한다. */
  goPath: (path: string) => void;
  setParam: (key: string, value: string | null) => void;
}

export const useRouter = create<RouterState>((set, get) => ({
  loc: parse(),
  go: (name, params) => {
    const r = ROUTES.find(x => x.name === name)!;
    const q = params ? `?${new URLSearchParams(params)}` : '';
    window.history.pushState(null, '', r.path + q);
    set({ loc: parse() });
  },
  goPath: path => {
    const u = new URL(path, window.location.origin);
    const r = ROUTES.find(x => x.path === u.pathname);
    if (!r) return;
    window.history.pushState(null, '', r.path + u.search);
    set({ loc: parse() });
  },
  setParam: (key, value) => {
    const p = new URLSearchParams(get().loc.params);
    if (value == null || value === '') p.delete(key); else p.set(key, value);
    const q = p.toString();
    window.history.replaceState(null, '', window.location.pathname + (q ? `?${q}` : ''));
    set({ loc: parse() });
  },
}));

window.addEventListener('popstate', () => useRouter.setState({ loc: parse() }));
