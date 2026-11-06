import { applyVoiceEvent, recoverVoiceFinal } from './useServerEvents';
import { useUi } from '../state/ui';

const idleVoice = { state: 'idle' as const, sessionId: null, byDevice: null, device: null, partial: '', warn: null };
const state = (patch: Record<string, unknown>) => ({ sessionId: 's1', state: 'listening', byDevice: '192.168.0.50', error: null, message: null, warn: null, device: 'Yeti X', ...patch });

// FR-VC-03·10·12: 서버의 음성 이벤트가 화면 상태에 반영되는 방식
describe('음성 입력 SSE 이벤트', () => {
  beforeEach(() => {
    useUi.setState({ chatDraft: '', chatDraftSource: null, chatDraftOriginal: null, voiceSeen: [], voice: idleVoice, toasts: [] });
  });

  it('상태 이벤트가 모든 기기의 "듣는 중" 표시를 만든다', () => {
    applyVoiceEvent('stt.state', state({}));
    expect(useUi.getState().voice).toMatchObject({ state: 'listening', device: 'Yeti X', byDevice: '192.168.0.50' });
  });

  it('인식 중인 글자는 확정되면 비우고, 세션이 끝나도 비운다', () => {
    applyVoiceEvent('stt.state', state({}));
    applyVoiceEvent('stt.partial', { sessionId: 's1', text: '힐러한번만' });
    expect(useUi.getState().voice.partial).toBe('힐러한번만');
    applyVoiceEvent('stt.partial', { sessionId: 's1', text: '힐러한번만더구합니다' });
    applyVoiceEvent('stt.final', { sessionId: 's1', text: '힐러 한 분만 더 구합니다', original: '힐러한번만더구합니다', applied: [] });
    expect(useUi.getState().voice.partial).toBe('');
    applyVoiceEvent('stt.partial', { sessionId: 's2', text: '또' });
    applyVoiceEvent('stt.state', state({ sessionId: 's2', state: 'idle' }));
    expect(useUi.getState().voice.partial).toBe('');
    expect(useUi.getState().voice.state).toBe('idle');
  });

  it('확정 글자는 초안에 붙고 인식 글자를 함께 기억한다', () => {
    applyVoiceEvent('stt.final', { sessionId: 's1', text: '힐러 한 분만 더 구합니다', original: '힐러한번만더구합니다', applied: [] });
    expect(useUi.getState().chatDraft).toBe('힐러 한 분만 더 구합니다');
    expect(useUi.getState().chatDraftOriginal).toBe('힐러한번만더구합니다');
  });

  it('서버가 이미 보낸 문장(자동 전송)은 초안에 붙이지 않는다 — 붙이면 다시 보내게 된다', () => {
    applyVoiceEvent('stt.final', { sessionId: 's1', text: '이미 보냄', original: '이미보냄', applied: [], autoSent: true });
    expect(useUi.getState().chatDraft).toBe('');
    expect(useUi.getState().voice.partial).toBe('');
  });

  it('한도를 넘어 보내지 못한 자동 전송 문장은 초안에 남는다', () => {
    applyVoiceEvent('stt.final', { sessionId: 's1', text: '가'.repeat(60), original: '가'.repeat(60), applied: [], autoSent: false });
    expect(useUi.getState().chatDraft).toHaveLength(60);
  });

  it('오류는 세션이 끝날 때 한 번만 알린다', () => {
    applyVoiceEvent('stt.state', state({ state: 'listening' }));
    applyVoiceEvent('stt.state', state({ state: 'finishing', error: 'STT_MIC_REMOVED', message: '끊김' }));   // 중간 상태: 알리지 않는다
    expect(useUi.getState().toasts).toHaveLength(0);
    applyVoiceEvent('stt.state', state({ state: 'idle', error: 'STT_MIC_REMOVED', message: '끊김' }));
    expect(useUi.getState().toasts).toHaveLength(1);
    expect(useUi.getState().toasts[0].message).toContain('마이크 연결이 끊겨');
  });

  it('오류 없이 끝나면 알림이 없다', () => {
    applyVoiceEvent('stt.state', state({ state: 'idle' }));
    expect(useUi.getState().toasts).toHaveLength(0);
  });

  it('경고(소리 없음)는 듣는 동안 유지되고 끝나면 지운다', () => {
    applyVoiceEvent('stt.state', state({ warn: 'NO_SIGNAL' }));
    expect(useUi.getState().voice.warn).toBe('NO_SIGNAL');
    applyVoiceEvent('stt.state', state({ state: 'finishing', warn: null }));   // 서버가 경고를 다시 안 보내도 유지
    expect(useUi.getState().voice.warn).toBe('NO_SIGNAL');
    applyVoiceEvent('stt.state', state({ state: 'idle', warn: null }));
    expect(useUi.getState().voice.warn).toBeNull();
  });

  it('재연결 뒤 받은 마지막 결과는 한 번만 붙는다', () => {
    const last = { sessionId: 's1', text: '놓친 문장', original: '놓친문장', applied: [], at: new Date().toISOString() };
    recoverVoiceFinal(last);
    recoverVoiceFinal(last);   // 또 재연결해도
    expect(useUi.getState().chatDraft).toBe('놓친 문장');
  });

  it('이미 SSE로 받아 붙인 문장은 재연결 복구가 다시 붙이지 않는다', () => {
    applyVoiceEvent('stt.final', { sessionId: 's1', text: '받은 문장', original: '받은문장', applied: [] });
    recoverVoiceFinal({ sessionId: 's1', text: '받은 문장', original: '받은문장', applied: [], at: new Date().toISOString() });
    expect(useUi.getState().chatDraft).toBe('받은 문장');
  });

  it('자동 전송했거나 결과가 없으면 복구하지 않는다', () => {
    recoverVoiceFinal({ sessionId: 's1', text: '보낸 문장', original: '보낸문장', applied: [], autoSent: true, at: new Date().toISOString() });
    recoverVoiceFinal(null);
    recoverVoiceFinal(undefined);
    expect(useUi.getState().chatDraft).toBe('');
  });
});
