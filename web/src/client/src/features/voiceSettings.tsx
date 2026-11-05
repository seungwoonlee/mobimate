import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, ApiError } from '../api/http';
import { keys5, useSttStatus } from '../api/queries';
import type { SttDevices, SttMicTest, SttSettings } from '../api/types';
import { Pill, Row, Seg } from '../components/ui';
import { queryClient } from '../lib/queryClient';
import { comboFromKeyEvent, formatMb, installPercent, micLevelMessage, sttErrorMessage } from '../lib/voice';
import { useUi } from '../state/ui';

const SETTINGS_KEY = [...keys5.stt, 'settings'] as const;
const DEVICES_KEY = [...keys5.stt, 'devices'] as const;

/**
 * 설정 > 음성 입력 (FR-VC). 값을 바꾸는 것은 게임 PC에서만 가능하다(서버도 PC 본체 요청만 받는다).
 * 폰·태블릿에서는 상태만 보여 준다.
 */
export function VoiceSettings({ local }: { local: boolean }) {
  const status = useSttStatus();
  const st = status.data?.data;
  const settingsQ = useQuery({ queryKey: SETTINGS_KEY, queryFn: () => api.get<SttSettings>('/api/stt/settings'), enabled: local, retry: false });
  const devicesQ = useQuery({ queryKey: DEVICES_KEY, queryFn: () => api.get<SttDevices>('/api/stt/devices'), enabled: local, staleTime: 10_000, retry: false });
  const toast = useUi(s => s.toast);
  const s = settingsQ.data?.data;

  const save = async (body: Record<string, unknown>) => {
    try {
      const r = await api.put<{ restartRequired: boolean; hotkey: { registered: boolean; error: string | null; key: string | null } }>('/api/stt/settings', body);
      await queryClient.invalidateQueries({ queryKey: keys5.stt });
      if (r.data.restartRequired) toast('스레드 수는 앱을 다시 켠 뒤에 적용됩니다', 'info');
      if ('hotkey' in body && body.hotkey && !r.data.hotkey.registered) toast(r.data.hotkey.error ?? '단축키를 등록하지 못했습니다', 'warn');
      return true;
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '설정을 저장하지 못했습니다', 'warn');
      return false;
    }
  };

  if (!st) return <p className="faint small">불러오는 중…</p>;

  return (
    <>
      <ModelRow local={local} />
      {!local && <p className="faint small">음성 입력 설정은 게임 PC에서만 바꿀 수 있습니다.</p>}
      {local && s && (
        <>
          <MicRow s={s} devices={devicesQ.data?.data} onPick={id => void save({ deviceId: id })} />
          <HotkeyRow s={s} status={st.hotkey} onSave={save} />
          <Row label="단축키 방식">
            <Seg value={s.hotkeyMode} options={[['hold', '누르는 동안 듣기'], ['toggle', '눌러서 켜고 끄기']]} onChange={v => void save({ hotkeyMode: v })} />
          </Row>
          <Row label="신호음">
            <Seg value={s.cue ? 'on' : 'off'} options={[['on', '켜기'], ['off', '끄기']]} onChange={v => void save({ cue: v === 'on' })} />
          </Row>
          <p className="faint small">전체 화면 게임에서는 화면이 보이지 않으니 소리로 알립니다. 시작음 뒤에 말씀하세요.</p>
          <Row label="한 번 듣는 시간">
            <Seg value={String(s.maxSeconds)} options={[['10', '10초'], ['15', '15초'], ['30', '30초'], ['60', '60초']]} onChange={v => void save({ maxSeconds: Number(v) })} />
          </Row>
          <Row label="말이 끝나면 바로 전송">
            <Seg value={s.autoSend ? 'on' : 'off'} options={[['off', '끄기'], ['on', '켜기']]} onChange={v => void save({ autoSend: v === 'on' })} />
          </Row>
          <p className={s.autoSend ? 'warn-text small' : 'faint small'}>
            {s.autoSend ? '잘못 알아들은 말도 그대로 게임 채팅으로 나갑니다. 50자를 넘는 문장은 보내지 않고 입력창에 남깁니다.' : '끄면 입력창에 적어 주고, 확인한 뒤 Enter로 보냅니다. (권장)'}
          </p>
          <Row label="폰·태블릿에서 시작">
            <Seg value={s.allowLanStart ? 'on' : 'off'} options={[['off', '허용 안 함'], ['on', '허용']]} onChange={v => void save({ allowLanStart: v === 'on' })} />
          </Row>
          <p className={s.allowLanStart ? 'warn-text small' : 'faint small'}>
            {s.allowLanStart ? '같은 와이파이의 다른 기기도 PC 마이크를 켤 수 있습니다. 공용 와이파이에서는 끄세요.' : '끄면 PC 단축키와 PC 화면의 마이크 버튼으로만 시작합니다. (권장)'}
          </p>
        </>
      )}
      <p className="faint small">헤드셋 마이크를 권장합니다. 스피커 소리가 마이크로 들어가면 게임 속 대화가 채팅으로 나갈 수 있습니다. 인식은 이 PC 안에서만 하며 음성은 어디로도 보내지 않습니다.</p>
    </>
  );
}

/** 모델 설치 상태와 내려받기 */
function ModelRow({ local }: { local: boolean }) {
  const status = useSttStatus();
  const toast = useUi(s => s.toast);
  const st = status.data!.data;
  const inst = st.install;
  const downloading = inst.phase === 'downloading';

  const start = async () => {
    try {
      await api.post('/api/stt/model/install', {});
      await queryClient.invalidateQueries({ queryKey: keys5.stt });
    } catch (e) {
      const err = e as ApiError;
      toast(sttErrorMessage(err.code, err.message), 'warn');
    }
  };

  return (
    <>
      <div className="kv">
        <span className="muted">음성 인식 모델</span>
        <span>{st.modelInstalled ? <Pill tone="ok">설치됨</Pill> : downloading ? <Pill tone="warn">내려받는 중</Pill> : <Pill tone="danger">설치 필요</Pill>}</span>
      </div>
      {downloading && (
        <>
          <div className="bar" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={installPercent(inst.done, inst.total)} aria-label="모델 내려받기 진행률">
            <i style={{ width: `${installPercent(inst.done, inst.total)}%` }} />
          </div>
          <p className="faint small num">{formatMb(inst.done)} / {formatMb(inst.total)}</p>
        </>
      )}
      {inst.phase === 'failed' && !st.modelInstalled && <p className="warn-text small">설치하지 못했습니다: {inst.error} 다시 시도해 주세요.</p>}
      {!st.modelInstalled && !downloading && (local
        ? <button type="button" className="btn primary mt-8" onClick={start}>모델 설치 (약 160MB, 이 PC에만 저장)</button>
        : <p className="faint small">모델 설치는 게임 PC에서 하세요.</p>)}
      {!st.modelInstalled && <p className="faint small">처음 한 번만 내려받습니다. 인식은 이 PC 안에서만 합니다.</p>}
    </>
  );
}

/** 마이크 고르기와 시험 */
function MicRow({ s, devices, onPick }: { s: SttSettings; devices: SttDevices | undefined; onPick: (id: string) => void }) {
  const toast = useUi(s2 => s2.toast);
  const [testing, setTesting] = useState(false);
  const [result, setResult] = useState<SttMicTest | null>(null);
  const list = devices?.devices ?? [];
  const selectedMissing = !!s.deviceId && !list.some(d => d.id === s.deviceId);

  const test = async () => {
    setTesting(true);
    setResult(null);
    try {
      const r = await api.post<SttMicTest>('/api/stt/devices/test', {});
      setResult(r.data);
    } catch (e) {
      const err = e as ApiError;
      toast(sttErrorMessage(err.code, err.message), 'warn');
    } finally {
      setTesting(false);
    }
  };

  return (
    <>
      <div className="kv wrap">
        <span className="muted">마이크</span>
        <select aria-label="마이크 선택" value={s.deviceId ?? ''} onChange={e => onPick(e.target.value)}>
          <option value="">시스템 기본 마이크</option>
          {selectedMissing && <option value={s.deviceId!}>(연결되지 않은 마이크)</option>}
          {list.map(d => <option key={d.id} value={d.id}>{d.name}{d.isDefault ? ' · 기본' : ''}</option>)}
        </select>
      </div>
      <div className="acts">
        <button type="button" className="btn" disabled={testing} onClick={test}>{testing ? '듣는 중… 말씀하세요' : '마이크 테스트'}</button>
      </div>
      {!result && !testing && <p className="faint small">누른 뒤 1.5초 동안 평소처럼 말해 보세요.</p>}
      {result && (() => {
        const m = micLevelMessage(result.level);
        return (
          <p className={m.tone === 'ok' ? 'small' : 'warn-text small'} role="status">
            <Pill tone={m.tone}>{result.device}</Pill> <span className="num">최대 {result.peakDb}dB</span> · {m.text}
            {result.fallback && ' (고른 마이크가 없어 기본 마이크로 시험했습니다)'}
          </p>
        );
      })()}
    </>
  );
}

/** 전역 단축키 지정: "지정" → 원하는 키 조합을 누르면 저장 */
function HotkeyRow({ s, status, onSave }: { s: SttSettings; status: { key: string | null; registered: boolean; error: string | null }; onSave: (body: Record<string, unknown>) => Promise<boolean> }) {
  const [capturing, setCapturing] = useState(false);
  const [hint, setHint] = useState<string | null>(null);

  useEffect(() => {
    if (!capturing) return;
    const onKey = (e: KeyboardEvent) => {
      e.preventDefault();
      e.stopPropagation();
      if (e.code === 'Escape') { setCapturing(false); setHint(null); return; }
      const combo = comboFromKeyEvent(e);
      if (!combo) { setHint('Ctrl·Alt·Shift 중 하나와 글자·숫자를 함께 누르세요. (F1~F24는 혼자 눌러도 됩니다)'); return; }
      setCapturing(false);
      setHint(null);
      void onSave({ hotkey: combo });
    };
    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  }, [capturing, onSave]);

  return (
    <>
      <div className="kv wrap">
        <span className="muted">전역 단축키</span>
        <span>
          <kbd className={`kbd${capturing ? ' capturing' : ''}`}>{capturing ? '키를 누르세요…' : s.hotkey ?? '없음'}</kbd>{' '}
          {s.hotkey && !capturing && (status.registered ? <Pill tone="ok">등록됨</Pill> : <Pill tone="danger">등록 안 됨</Pill>)}
        </span>
      </div>
      <div className="acts">
        <button type="button" className="btn" onClick={() => { setCapturing(c => !c); setHint(null); }}>{capturing ? '취소 (Esc)' : s.hotkey ? '다른 키로 바꾸기' : '단축키 지정'}</button>
        {s.hotkey && !capturing && <button type="button" className="btn" onClick={() => void onSave({ hotkey: '' })}>지우기</button>}
      </div>
      {hint && <p className="warn-text small" role="status">{hint}</p>}
      {s.hotkey && !status.registered && status.error && <p className="warn-text small">{status.error}</p>}
      <p className="faint small">게임이 쓰지 않는 조합을 고르세요. 등록한 조합은 모든 프로그램에서 이 기능이 먼저 가져갑니다.</p>
    </>
  );
}
