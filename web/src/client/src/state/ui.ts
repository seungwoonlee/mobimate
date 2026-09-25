import { create } from 'zustand';
import type { SseState } from '../api/sse';

export type ToastLevel = 'ok' | 'warn' | 'danger' | 'info';
export interface Toast { id: number; level: ToastLevel; message: string }

export type DockTab = 'game' | 'ai';

interface UiState {
  // 도크: 라우트가 아니라 UI 상태다. 화면을 오가거나 접었다 펴도 유지된다 (상세설계 §4.2, FR-MB-03).
  dockOpen: boolean;
  dockTab: DockTab;
  setDock: (open: boolean, tab?: DockTab) => void;

  toasts: Toast[];
  toast: (message: string, level?: ToastLevel) => void;
  dismiss: (id: number) => void;

  sse: SseState;
  /** 다음 재연결 시각(ms) */
  retryAt?: number;
  /** 한 번이라도 연결된 적이 있는가. 이후 open이 아니면 끊김으로 본다 (FR-MB-12) */
  everOpen: boolean;
  setSse: (s: SseState, retryAt?: number) => void;

  /** 401을 받았다 → 인증 안내 화면 */
  unauthorized: boolean;
  setUnauthorized: (v: boolean) => void;

  /** 가장 최근 네트워크 오류(PC 연결 끊김) */
  offline: boolean;
  setOffline: (v: boolean) => void;

  /** 수동 새로고침·SSE로 올라가는 번호. 적응형 갱신 훅이 이 값이 바뀌면 주기를 리셋한다. */
  refreshTick: number;
  bumpRefresh: () => void;
}

let nextId = 1;

export const useUi = create<UiState>(set => ({
  dockOpen: false,
  dockTab: 'game',
  setDock: (open, tab) => set(s => ({ dockOpen: open, dockTab: tab ?? s.dockTab })),

  toasts: [],
  toast: (message, level = 'ok') => {
    const id = nextId++;
    set(s => ({ toasts: [...s.toasts.slice(-3), { id, level, message }] }));
    window.setTimeout(() => set(s => ({ toasts: s.toasts.filter(t => t.id !== id) })), level === 'danger' ? 6000 : 3800);
  },
  dismiss: id => set(s => ({ toasts: s.toasts.filter(t => t.id !== id) })),

  sse: 'connecting',
  everOpen: false,
  setSse: (sse, retryAt) => set(s => ({ sse, retryAt, everOpen: s.everOpen || sse === 'open' })),

  unauthorized: false,
  setUnauthorized: v => set({ unauthorized: v }),

  offline: false,
  setOffline: v => set({ offline: v }),

  refreshTick: 0,
  bumpRefresh: () => set(s => ({ refreshTick: s.refreshTick + 1 })),
}));

/** PC와 끊겼는가: 한 번 연결된 뒤로 SSE가 열려 있지 않으면 끊김이다. 조작(전송·채집·정지·수거)은 이때 보내지 않는다 (FR-MB-12). */
export const isOffline = (s: Pick<UiState, 'everOpen' | 'sse'>) => s.everOpen && s.sse !== 'open';
export const useOffline = () => useUi(isOffline);
