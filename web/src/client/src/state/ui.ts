import { create } from 'zustand';
import type { SseState } from '../api/sse';
import type { GatherJob } from '../api/types';
import { applyVoiceFinal, remember, VOICE_SOURCE, voiceKey, type VoiceState } from '../lib/voice';

export type ToastLevel = 'ok' | 'warn' | 'danger' | 'info';
export interface Toast { id: number; level: ToastLevel; message: string }

export type DockTab = 'game' | 'ai';

/** 음성 입력 화면 상태 (FR-VC-10: 듣는 중에는 모든 기기에 표시) */
export interface VoiceView {
  state: VoiceState;
  sessionId: string | null;
  /** 시작한 기기(서버가 본 주소) */
  byDevice: string | null;
  /** 쓰는 마이크 이름 */
  device: string | null;
  /** 인식 중인 글자 (확정되면 비운다) */
  partial: string;
  /** "NO_SIGNAL" 등 경고 코드 */
  warn: string | null;
}

interface UiState {
  // 도크: 라우트가 아니라 UI 상태다. 화면을 오가거나 접었다 펴도 유지된다 (상세설계 §4.2, FR-MB-03).
  /** 시트(오버레이)로 열린 상태. 이 세션에서만 유지한다. */
  dockOpen: boolean;
  /** 옆 패널 도크를 이 기기에서 열어 둘지 닫아 둘지 고른 값. 고른 적이 없으면 null = 기본값(가로 화면이면 열림). 기억해 둔다. */
  dockPref: boolean | null;
  dockTab: DockTab;
  /** 사용자가 열거나 닫는다: 시트 상태와 옆 패널 선택을 함께 바꾼다 */
  setDock: (open: boolean, tab?: DockTab) => void;
  /** 화면을 옮길 때 시트만 닫는다 (옆 패널 선택은 건드리지 않는다) */
  closeSheet: () => void;

  toasts: Toast[];
  toast: (message: string, level?: ToastLevel) => void;
  dismiss: (id: number) => void;

  sse: SseState;
  /** 다음 재연결 시각(ms) */
  retryAt?: number;
  /** 한 번이라도 연결된 적이 있는가. 이후 open이 아니면 끊김으로 본다 (FR-MB-12) */
  everOpen: boolean;
  setSse: (s: SseState, retryAt?: number) => void;

  /** 채집 작업: 진행 중이거나 방금 끝난 것. 끝난 작업은 사용자가 닫을 때까지 결과를 보여 준다. */
  gatherJob: GatherJob | null;
  setGatherJob: (j: GatherJob | null) => void;

  /** 가장 최근 네트워크 오류(PC 연결 끊김) */
  offline: boolean;
  setOffline: (v: boolean) => void;

  /** 수동 새로고침·SSE로 올라가는 번호. 적응형 갱신 훅이 이 값이 바뀌면 주기를 리셋한다. */
  refreshTick: number;
  bumpRefresh: () => void;

  // 입력 초안: 레이아웃이 바뀌어도(폴더블 접기·펴기) 유지한다 (FR-MB-03)
  chatDraft: string;
  /** 초안을 한마디로 만들었으면 "아무말 · 페르소나", 아니면 null(직접) */
  chatDraftSource: string | null;
  setChatDraft: (v: string, source?: string | null) => void;
  /** 음성으로 채운 초안이면 후처리 전 인식 글자 (FR-VC-08: 고쳐 보냈을 때 서버가 배운다) */
  chatDraftOriginal: string | null;

  /** 음성 입력 상태 (FR-VC). 모든 기기에 SSE로 같은 상태가 온다 */
  voice: VoiceView;
  setVoice: (patch: Partial<VoiceView>) => void;
  /** 이미 반영한 확정 결과 열쇠 (재연결 뒤 같은 결과를 두 번 붙이지 않는다) */
  voiceSeen: string[];
  /** 확정된 문장을 초안 뒤에 붙인다. 이미 반영한 문장이면 false */
  receiveVoiceFinal: (ev: { sessionId: string; text: string; original: string }) => boolean;
  aiDraft: string;
  setAiDraft: (v: string) => void;

  /** 빠른 실행(명령 팔레트, Ctrl+K) */
  paletteOpen: boolean;
  setPaletteOpen: (v: boolean) => void;

  /** 폰으로 보기(QR 페어링) 대화상자 */
  pairOpen: boolean;
  setPairOpen: (v: boolean) => void;
}

const DOCK_KEY = 'mobimate.dock.v1';
function loadDockPref(): boolean | null {
  try { const v = localStorage.getItem(DOCK_KEY); return v === 'open' ? true : v === 'closed' ? false : null; } catch { return null; }
}
function saveDockPref(open: boolean) {
  try { localStorage.setItem(DOCK_KEY, open ? 'open' : 'closed'); } catch { /* 저장 불가: 이번 세션만 */ }
}

let nextId = 1;

export const useUi = create<UiState>((set, get) => ({
  dockOpen: false,
  dockPref: loadDockPref(),
  dockTab: 'game',
  setDock: (open, tab) => { saveDockPref(open); set(s => ({ dockOpen: open, dockPref: open, dockTab: tab ?? s.dockTab })); },
  closeSheet: () => set({ dockOpen: false }),

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

  offline: false,
  setOffline: v => set({ offline: v }),
  gatherJob: null,
  setGatherJob: j => set({ gatherJob: j }),

  refreshTick: 0,
  bumpRefresh: () => set(s => ({ refreshTick: s.refreshTick + 1 })),

  chatDraft: '',
  chatDraftSource: null,
  chatDraftOriginal: null,
  // 다 지우면 출처도 지운다. 한마디 대사를 고쳐 쓰는 경우는 출처를 유지한다
  setChatDraft: (v, source) => set(s => ({
    chatDraft: v,
    chatDraftSource: source !== undefined ? source : v.trim() ? s.chatDraftSource : null,
    // 인식 글자는 음성 초안일 때만 둔다: 비우거나 다른 출처(한마디)로 바꾸면 버린다
    chatDraftOriginal: !v.trim() || (source !== undefined && source !== VOICE_SOURCE) ? null : s.chatDraftOriginal,
  })),
  voice: { state: 'idle', sessionId: null, byDevice: null, device: null, partial: '', warn: null },
  setVoice: patch => set(s => ({ voice: { ...s.voice, ...patch } })),
  voiceSeen: [],
  receiveVoiceFinal: ev => {
    const key = voiceKey(ev.sessionId, ev.text);
    if (get().voiceSeen.includes(key)) return false;
    set(s => {
      const next = applyVoiceFinal({ draft: s.chatDraft, source: s.chatDraftSource, original: s.chatDraftOriginal }, ev);
      return { chatDraft: next.draft, chatDraftSource: next.source, chatDraftOriginal: next.original, voiceSeen: remember(s.voiceSeen, key) };
    });
    return true;
  },
  aiDraft: '',
  setAiDraft: v => set({ aiDraft: v }),

  paletteOpen: false,
  setPaletteOpen: v => set({ paletteOpen: v }),

  pairOpen: false,
  setPairOpen: v => set({ pairOpen: v }),
}));

/** PC와 끊겼는가: 한 번 연결된 뒤로 SSE가 열려 있지 않으면 끊김이다. 조작(전송·채집·정지·수거)은 이때 보내지 않는다 (FR-MB-12). */
export const isOffline = (s: Pick<UiState, 'everOpen' | 'sse'>) => s.everOpen && s.sse !== 'open';
export const useOffline = () => useUi(isOffline);
