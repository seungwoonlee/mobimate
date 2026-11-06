/**
 * 음성 입력 (요구사양 FR-VC) 화면 규칙. 서버가 보낸 인식 결과를 채팅 초안에 합치는 순수 함수, 오류 문구, 마이크 버튼 사용 가능 여부.
 * 화면 상태·네트워크와 떨어져 있어 단위 테스트가 쉽다.
 */

export const VOICE_SOURCE = '음성';

export type VoiceState = 'idle' | 'starting' | 'listening' | 'finishing';

export interface DraftState {
  draft: string;
  /** 초안의 출처: null(직접) / "아무말 · 페르소나" / "음성" */
  source: string | null;
  /** 음성으로 채운 초안이면 후처리 전 인식 글자. 고쳐 보냈을 때 서버가 틀린 글자를 배운다(FR-VC-08). 섞인 초안이면 null */
  original: string | null;
}

export interface VoiceFinal {
  text: string;
  original: string;
}

const clean = (s: string) => s.replace(/\s*\n\s*/g, ' ').trim();

/** 기존 초안 뒤에 한 칸 띄어 붙인다 */
export function joinDraft(draft: string, text: string): string {
  const a = clean(draft);
  const b = clean(text);
  return a && b ? `${a} ${b}` : a || b;
}

/**
 * 확정된 문장을 초안에 합친다 (FR-VC-03). 확정 글자를 버리는 경우는 없고, 빈 결과는 초안을 바꾸지 않는다.
 * 초안이 비어 있으면 음성 초안이 되고, 이미 음성 초안이면 인식 글자도 이어 붙인다. 직접 쓴 글과 섞이면 학습 대상에서 뺀다.
 */
export function applyVoiceFinal(cur: DraftState, ev: VoiceFinal): DraftState {
  const text = clean(ev.text);
  if (!text) return cur;
  if (!clean(cur.draft)) return { draft: text, source: VOICE_SOURCE, original: clean(ev.original) || text };
  const fromVoice = cur.source === VOICE_SOURCE && cur.original != null;
  return {
    draft: joinDraft(cur.draft, text),
    source: cur.source,
    original: fromVoice ? joinDraft(cur.original!, ev.original) : null,
  };
}

/** 같은 확정 결과를 두 번 반영하지 않기 위한 열쇠 (SSE 재연결 뒤 마지막 결과를 다시 받을 때) */
export const voiceKey = (sessionId: string, text: string) => `${sessionId}\u0000${text}`;

/** 최근 N개만 남기는 기억 목록 */
export function remember(list: readonly string[], key: string, cap = 20): string[] {
  return [...list.filter(k => k !== key), key].slice(-cap);
}

const ERRORS: Record<string, string> = {
  STT_MIC_DENIED: 'Windows 설정 → 개인정보 → 마이크에서 데스크톱 앱의 마이크 접근을 켜 주세요.',
  STT_MIC_BUSY: '다른 프로그램이 마이크를 독점하고 있어 열 수 없습니다.',
  STT_MIC_NONE: '사용할 수 있는 마이크가 없습니다.',
  STT_MIC_REMOVED: '마이크 연결이 끊겨 듣기를 마쳤습니다.',
  STT_MIC_FAILED: '마이크를 열지 못했습니다.',
  STT_MODEL_MISSING: '음성 인식 모델이 설치되어 있지 않습니다.',
  PC_ONLY: '음성 입력은 게임 PC에서만 쓸 수 있습니다.',
  STT_BUSY: '이미 듣고 있습니다.',
  STT_FORBIDDEN: '요청이 거절되었습니다. 화면을 새로고침해 주세요.',
  STT_ENGINE_FAILED: '음성 인식을 시작하지 못했습니다.',
};

export function sttErrorMessage(code?: string | null, fallback?: string | null): string {
  return (code && ERRORS[code]) || fallback || '음성 입력 중 오류가 났습니다.';
}

export function sttWarnMessage(warn?: string | null): string | null {
  if (warn === 'NO_SIGNAL') return '마이크 소리가 들어오지 않습니다. 연결과 Windows 개인정보 설정을 확인해 주세요.';
  return null;
}

export interface MicInput {
  /** 상태를 아직 못 받았으면 undefined */
  modelInstalled: boolean | undefined;
  kind: 'local' | 'lan' | undefined;
  voiceState: VoiceState;
  offline: boolean;
}

export interface MicAvailability {
  enabled: boolean;
  /** 못 쓰는 이유(툴팁). 쓸 수 있으면 null */
  reason: string | null;
}

/** 마이크 버튼을 누를 수 있는지. 듣는 중이면 어느 기기든 끝낼 수 있다. */
export function micAvailability(i: MicInput): MicAvailability {
  if (i.offline) return { enabled: false, reason: 'PC와 연결이 끊겨 있습니다' };
  if (i.voiceState === 'listening') return { enabled: true, reason: null };
  if (i.voiceState !== 'idle') return { enabled: false, reason: '잠시만 기다려 주세요' };
  if (i.modelInstalled === false) return { enabled: false, reason: '음성 인식 모델이 설치되어 있지 않습니다' };
  if (i.kind === 'lan') return { enabled: false, reason: '음성 입력은 게임 PC에서만 쓸 수 있습니다' };
  return { enabled: true, reason: null };
}

const NAMED_KEYS: Record<string, string> = {
  Space: 'Space', Insert: 'Insert', Delete: 'Delete', Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown',
  ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right',
};

export interface KeyComboInput {
  code: string;
  ctrlKey: boolean;
  altKey: boolean;
  shiftKey: boolean;
  metaKey: boolean;
}

/**
 * 눌린 키를 서버가 읽는 단축키 글("Ctrl+Alt+V", "LCtrl+Space")로 바꾼다 (FR-VC-02). 키 위치(code)로 읽어 자판 배열과 한/영 상태에 영향받지 않는다.
 * <paramref name="down"/>(지금 눌려 있는 키의 code 집합)을 주면 왼쪽만 눌린 Ctrl·Alt·Shift는 "LCtrl"처럼 왼쪽 전용으로 만든다
 * (브라우저의 수정키 상태는 좌우를 구분하지 못해서 눌림을 따로 추적해야 한다). 양쪽을 다 눌렀거나 모르면 좌우 구분 없는 이름이다.
 * 아직 주 키를 안 눌렀거나(수정키만), Ctrl·Alt·Win 없이 글자·숫자·스페이스만(Shift만 포함) 누르면 null — 서버도 같은 규칙으로 거절한다.
 */
export function comboFromKeyEvent(e: KeyComboInput, down?: ReadonlySet<string>): string | null {
  let key: string | null = null;
  if (/^Key[A-Z]$/.test(e.code)) key = e.code.slice(3);
  else if (/^Digit[0-9]$/.test(e.code)) key = e.code.slice(5);
  else if (/^F([1-9]|1[0-9]|2[0-4])$/.test(e.code)) key = e.code;
  else if (NAMED_KEYS[e.code]) key = NAMED_KEYS[e.code];
  if (!key) return null;
  const side = (on: boolean, name: string, left: string, right: string) => {
    if (!on) return null;
    return down && down.has(left) && !down.has(right) ? `L${name}` : name;
  };
  const mods = [
    side(e.ctrlKey, 'Ctrl', 'ControlLeft', 'ControlRight'),
    side(e.altKey, 'Alt', 'AltLeft', 'AltRight'),
    side(e.shiftKey, 'Shift', 'ShiftLeft', 'ShiftRight'),
    e.metaKey ? 'Win' : null,
  ].filter(Boolean) as string[];
  const isFunctionKey = /^F\d+$/.test(key);
  // Ctrl·Alt·Win 중 하나가 있어야 한다. 수정키가 없거나 Shift뿐이면 그 글자·숫자·기호 입력이 모든 프로그램에서 가로채인다 (서버 규칙과 같다)
  if (!isFunctionKey && !(e.ctrlKey || e.altKey || e.metaKey)) return null;
  return [...mods, key].join('+');
}

export type MicLevel = 'none' | 'low' | 'ok' | 'loud';

/** 마이크 시험 결과 안내 문구 (FR-VC-05 ⑦) */
export function micLevelMessage(level: MicLevel): { tone: 'ok' | 'warn' | 'danger'; text: string } {
  switch (level) {
    case 'ok': return { tone: 'ok', text: '알맞게 들립니다' };
    case 'low': return { tone: 'warn', text: '소리가 작습니다. Windows 마이크 볼륨을 올리거나 마이크에 더 가까이 말해 보세요' };
    case 'loud': return { tone: 'warn', text: '소리가 너무 커서 찌그러질 수 있습니다. 마이크 볼륨을 낮춰 보세요' };
    default: return { tone: 'danger', text: '소리가 거의 들어오지 않습니다. 음소거, Windows 개인정보(마이크 접근), 연결을 확인하세요. 시험하는 동안 말씀하셨는지도 확인해 주세요' };
  }
}

/** 바이트 → "150.5MB" */
export function formatMb(bytes: number): string {
  return `${(bytes / 1_000_000).toFixed(1)}MB`;
}

/** 내려받기 진행률 0~100 (전체 크기를 모르면 0) */
export function installPercent(done: number, total: number): number {
  return total > 0 ? Math.min(100, Math.floor((done / total) * 100)) : 0;
}

/** 학습한 쌍의 상태 표시 (FR-VC-08): 2회 확인되면 적용, 2회 되돌리면 자동으로 꺼진다 */
export function pairState(p: { count: number; off: boolean; active: boolean; reverts: number }): { tone: 'ok' | 'warn' | 'neutral'; text: string } {
  if (p.off) return { tone: 'neutral', text: p.reverts >= 2 ? '꺼짐 (두 번 되돌려서)' : '꺼짐' };
  if (p.active) return { tone: 'ok', text: p.reverts > 0 ? `적용 중 (되돌림 ${p.reverts}/2)` : '적용 중' };
  if (p.count >= 2) return { tone: 'warn', text: '짧아서 적용 안 함' };   // 인식 글자가 2음절 미만
  return { tone: 'warn', text: `확인 중 (${p.count}/2)` };
}

/**
 * 게임이 관리자 권한(상승)으로 실행 중인데 이 앱이 일반 권한이면 Windows 보안 규칙(UIPI) 때문에 게임 화면이 앞에 있는 동안 전역 단축키가 전달되지 않는다.
 * 앱 화면이 앞에 있을 때만 동작하는 것처럼 보인다 (2026-11-06 실사용). 이 앱도 관리자 권한으로 실행해야 한다.
 */
export function elevationNotice(e: { app: boolean; game: boolean | null; mismatch: boolean } | undefined): { tone: 'ok' | 'warn'; text: string } | null {
  if (!e) return null;
  if (e.mismatch) {
    return {
      tone: 'warn',
      text: '게임이 관리자 권한으로 실행 중인데 이 앱은 일반 권한이라, 게임 화면이 앞에 있을 때는 단축키가 동작하지 않습니다(Windows 보안 규칙). 앱을 끄고 MobiMateWeb.exe를 마우스 오른쪽 → "관리자 권한으로 실행"으로 다시 켜세요.',
    };
  }
  if (e.app && e.game === true) return { tone: 'ok', text: '앱이 게임과 같은 관리자 권한으로 실행 중이라 게임 화면이 앞에 있어도 단축키가 동작합니다.' };
  return null;
}
