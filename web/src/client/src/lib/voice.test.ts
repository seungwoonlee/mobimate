import { useUi } from '../state/ui';
import {
  applyVoiceFinal, joinDraft, micAvailability, remember, sttErrorMessage, sttWarnMessage, VOICE_SOURCE, voiceKey, type MicInput,
} from './voice';

// FR-VC-03·04·08·12: 음성 결과를 채팅 초안에 합치는 규칙
describe('초안 합치기', () => {
  it('두 글을 한 칸 띄어 붙이고, 한쪽이 비면 다른 쪽만 남긴다', () => {
    expect(joinDraft('안녕하세요', '반갑습니다')).toBe('안녕하세요 반갑습니다');
    expect(joinDraft('', '반갑습니다')).toBe('반갑습니다');
    expect(joinDraft('안녕하세요', '  ')).toBe('안녕하세요');
    expect(joinDraft(' 앞뒤 공백 ', ' 줄\n바꿈 ')).toBe('앞뒤 공백 줄 바꿈');
  });

  it('초안이 비어 있으면 음성 초안이 되고, 인식 글자를 함께 기억한다', () => {
    const r = applyVoiceFinal({ draft: '', source: null, original: null }, { text: '힐러 한 분만 더 구합니다', original: '힐러한분만더구합니다' });
    expect(r).toEqual({ draft: '힐러 한 분만 더 구합니다', source: VOICE_SOURCE, original: '힐러한분만더구합니다' });
  });

  it('이미 음성 초안이면 이어 붙이고 인식 글자도 이어 붙인다 (두 문장을 말한 경우)', () => {
    const first = applyVoiceFinal({ draft: '', source: null, original: null }, { text: '지금 갑니다', original: '지금갑니다' });
    const second = applyVoiceFinal(first, { text: '준비해 주세요', original: '준비해주세요' });
    expect(second).toEqual({ draft: '지금 갑니다 준비해 주세요', source: VOICE_SOURCE, original: '지금갑니다 준비해주세요' });
  });

  it('직접 쓴 글이 있는 초안에 붙이면 학습 대상에서 뺀다 (섞인 글은 비교할 수 없다)', () => {
    const r = applyVoiceFinal({ draft: '안녕하세요', source: null, original: null }, { text: '반갑습니다', original: '반갑습니다' });
    expect(r).toEqual({ draft: '안녕하세요 반갑습니다', source: null, original: null });
  });

  it('한마디(페르소나) 대사 뒤에 붙여도 출처를 바꾸지 않고 학습 대상에서 뺀다', () => {
    const r = applyVoiceFinal({ draft: '대사입니다', source: '아무말 · 악덕영애', original: null }, { text: '추가', original: '추가' });
    expect(r.source).toBe('아무말 · 악덕영애');
    expect(r.original).toBeNull();
  });

  it('빈 결과는 초안을 바꾸지 않는다', () => {
    const cur = { draft: '쓰던 글', source: null, original: null };
    expect(applyVoiceFinal(cur, { text: '   ', original: '' })).toBe(cur);
  });

  it('인식 글자가 비어 있어도 확정 글자는 버리지 않는다', () => {
    const r = applyVoiceFinal({ draft: '', source: null, original: null }, { text: '확정된 글', original: '' });
    expect(r.draft).toBe('확정된 글');
    expect(r.original).toBe('확정된 글');
  });
});

describe('같은 결과를 두 번 반영하지 않기', () => {
  it('열쇠는 세션과 글을 함께 본다', () => {
    expect(voiceKey('a', '글')).not.toBe(voiceKey('b', '글'));
    expect(voiceKey('a', '글')).not.toBe(voiceKey('a', '다른 글'));
  });

  it('최근 N개만 기억하고, 다시 본 것은 맨 뒤로 보낸다', () => {
    const list = ['1', '2', '3'];
    expect(remember(list, '4', 3)).toEqual(['2', '3', '4']);
    expect(remember(list, '1', 3)).toEqual(['2', '3', '1']);
  });
});

describe('오류·경고 문구', () => {
  it('알려진 코드는 사용자가 할 일을 알려 준다', () => {
    expect(sttErrorMessage('STT_MIC_DENIED')).toContain('개인정보');
    expect(sttErrorMessage('STT_LAN_DISABLED')).toContain('PC 설정');
    expect(sttErrorMessage('STT_MODEL_MISSING')).toContain('모델');
  });

  it('모르는 코드는 서버 문구를, 둘 다 없으면 기본 문구를 쓴다', () => {
    expect(sttErrorMessage('UNKNOWN', '서버가 준 문구')).toBe('서버가 준 문구');
    expect(sttErrorMessage('UNKNOWN')).toBe('음성 입력 중 오류가 났습니다.');
    expect(sttErrorMessage(null, null)).toBe('음성 입력 중 오류가 났습니다.');
  });

  it('소리가 안 들어오는 경고만 문구가 있다', () => {
    expect(sttWarnMessage('NO_SIGNAL')).toContain('소리가 들어오지 않습니다');
    expect(sttWarnMessage('OTHER')).toBeNull();
    expect(sttWarnMessage(null)).toBeNull();
  });
});

describe('마이크 버튼 사용 조건', () => {
  const base: MicInput = { modelInstalled: true, allowLanStart: false, kind: 'local', voiceState: 'idle', offline: false };

  it('PC 본체에서 모델이 있으면 쓸 수 있다', () => {
    expect(micAvailability(base)).toEqual({ enabled: true, reason: null });
  });

  it('PC와 연결이 끊기면 쓸 수 없다', () => {
    const r = micAvailability({ ...base, offline: true });
    expect(r.enabled).toBe(false);
    expect(r.reason).toContain('연결');
  });

  it('모델이 없으면 쓸 수 없고 이유를 알려 준다', () => {
    const r = micAvailability({ ...base, modelInstalled: false });
    expect(r).toEqual({ enabled: false, reason: expect.stringContaining('모델') });
  });

  it('폰·태블릿은 PC 설정이 꺼져 있으면 시작할 수 없다 (기본값)', () => {
    const r = micAvailability({ ...base, kind: 'lan' });
    expect(r.enabled).toBe(false);
    expect(r.reason).toContain('PC 설정');
    expect(micAvailability({ ...base, kind: 'lan', allowLanStart: true }).enabled).toBe(true);
  });

  it('상태를 아직 못 받았으면 막지 않는다 (받으면 바로 갱신된다)', () => {
    expect(micAvailability({ ...base, modelInstalled: undefined, allowLanStart: undefined, kind: undefined }).enabled).toBe(true);
  });

  it('듣는 중에는 어느 기기든 끝낼 수 있다', () => {
    expect(micAvailability({ ...base, kind: 'lan', voiceState: 'listening' })).toEqual({ enabled: true, reason: null });
    expect(micAvailability({ ...base, modelInstalled: false, voiceState: 'listening' }).enabled).toBe(true);
  });

  it('시작·마무리 중에는 누를 수 없다', () => {
    expect(micAvailability({ ...base, voiceState: 'starting' }).enabled).toBe(false);
    expect(micAvailability({ ...base, voiceState: 'finishing' }).enabled).toBe(false);
  });
});

// 상태 저장소: SSE로 온 확정 결과가 초안에 붙는 과정
describe('채팅 초안 저장소', () => {
  beforeEach(() => {
    useUi.setState({ chatDraft: '', chatDraftSource: null, chatDraftOriginal: null, voiceSeen: [] });
  });

  it('확정 결과를 초안에 붙이고, 같은 결과가 또 오면(재연결) 무시한다', () => {
    const ev = { sessionId: 's1', text: '힐러 한 분만 더 구합니다', original: '힐러한분만더구합니다' };
    expect(useUi.getState().receiveVoiceFinal(ev)).toBe(true);
    expect(useUi.getState().chatDraft).toBe('힐러 한 분만 더 구합니다');
    expect(useUi.getState().chatDraftSource).toBe(VOICE_SOURCE);
    expect(useUi.getState().chatDraftOriginal).toBe('힐러한분만더구합니다');

    expect(useUi.getState().receiveVoiceFinal(ev)).toBe(false);
    expect(useUi.getState().chatDraft).toBe('힐러 한 분만 더 구합니다');   // 두 번 붙지 않는다
  });

  it('같은 문장을 다른 세션에서 또 말하면 따로 붙는다', () => {
    useUi.getState().receiveVoiceFinal({ sessionId: 's1', text: '가자', original: '가자' });
    expect(useUi.getState().receiveVoiceFinal({ sessionId: 's2', text: '가자', original: '가자' })).toBe(true);
    expect(useUi.getState().chatDraft).toBe('가자 가자');
  });

  it('초안을 다 지우면 출처와 인식 글자도 지운다', () => {
    useUi.getState().receiveVoiceFinal({ sessionId: 's1', text: '가자', original: '가자' });
    useUi.getState().setChatDraft('');
    expect(useUi.getState().chatDraftSource).toBeNull();
    expect(useUi.getState().chatDraftOriginal).toBeNull();
  });

  it('음성 초안을 고쳐 쓰면 인식 글자는 그대로 둔다 (서버가 고친 부분을 배운다)', () => {
    useUi.getState().receiveVoiceFinal({ sessionId: 's1', text: '일러 한 번만', original: '일러한번만' });
    useUi.getState().setChatDraft('힐러 한 분만');
    expect(useUi.getState().chatDraftSource).toBe(VOICE_SOURCE);
    expect(useUi.getState().chatDraftOriginal).toBe('일러한번만');
  });

  it('한마디 대사로 초안을 바꾸면 인식 글자를 버린다', () => {
    useUi.getState().receiveVoiceFinal({ sessionId: 's1', text: '가자', original: '가자' });
    useUi.getState().setChatDraft('대사', '아무말 · 악덕영애');
    expect(useUi.getState().chatDraftOriginal).toBeNull();
  });

  it('기억 목록은 최근 20개만 둔다', () => {
    for (let i = 0; i < 30; i++) useUi.getState().receiveVoiceFinal({ sessionId: `s${i}`, text: `문장${i}`, original: `문장${i}` });
    expect(useUi.getState().voiceSeen).toHaveLength(20);
  });
});
