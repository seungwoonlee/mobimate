import { create } from 'zustand';

/**
 * 기기별 설정 (FR-MB-16). localStorage에 두며, 읽기·쓰기가 막힌 환경(사생활 보호 창 등)에서도 기본값으로 동작한다.
 */
export type Theme = 'system' | 'dark' | 'light';
/** 다크 모드 색상 패턴 (FR-LY-05). navy가 기본이다. */
export type Palette = 'navy' | 'ink' | 'violet' | 'forest';
export const PALETTES: [Palette, string, string][] = [['navy', '바다', '#4FC4BA'], ['ink', '먹물', '#86B4FF'], ['violet', '보라', '#BFA2FF'], ['forest', '숲', '#6FD296']];
export interface DevicePrefs {
  theme: Theme;
  palette: Palette;
  scale: 1 | 1.15 | 1.3;
  glance: boolean;
  dockWidth: number;
  /** 게임 채팅 자동 이모티콘 (없으면 서버 기본값) */
  autoEmote: boolean | null;
  /** 별칭 안내 말풍선을 이미 닫았는가 */
  nickHintSeen: boolean;
}

const KEY = 'mobimate.device.v1';
const DEFAULTS: DevicePrefs = { theme: 'system', palette: 'navy', scale: 1, glance: true, dockWidth: 360, autoEmote: null, nickHintSeen: false };

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
    const { theme, palette, scale, glance, dockWidth, autoEmote, nickHintSeen } = { ...get(), ...p };
    try { localStorage.setItem(KEY, JSON.stringify({ theme, palette, scale, glance, dockWidth, autoEmote, nickHintSeen })); } catch { /* 저장 불가: 이번 세션만 */ }
  },
}));
