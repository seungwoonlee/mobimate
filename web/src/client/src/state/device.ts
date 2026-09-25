import { create } from 'zustand';

/**
 * 기기별 설정 (FR-MB-16). localStorage에 두며, 읽기·쓰기가 막힌 환경(사생활 보호 창 등)에서도 기본값으로 동작한다.
 */
export type Theme = 'system' | 'dark' | 'light';
export interface DevicePrefs {
  theme: Theme;
  scale: 1 | 1.15 | 1.3;
  glance: boolean;
  dockWidth: number;
  /** 게임 채팅 자동 이모티콘 (없으면 서버 기본값) */
  autoEmote: boolean | null;
}

const KEY = 'mobimate.device.v1';
const DEFAULTS: DevicePrefs = { theme: 'system', scale: 1, glance: true, dockWidth: 360, autoEmote: null };

function load(): DevicePrefs {
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? { ...DEFAULTS, ...(JSON.parse(raw) as Partial<DevicePrefs>) } : DEFAULTS;
  } catch {
    return DEFAULTS;
  }
}

interface DeviceState extends DevicePrefs {
  set: (p: Partial<DevicePrefs>) => void;
}

export const useDevice = create<DeviceState>((set, get) => ({
  ...load(),
  set: p => {
    set(p);
    const { theme, scale, glance, dockWidth, autoEmote } = { ...get(), ...p };
    try { localStorage.setItem(KEY, JSON.stringify({ theme, scale, glance, dockWidth, autoEmote })); } catch { /* 저장 불가: 이번 세션만 */ }
  },
}));
