import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, ApiError } from '../api/http';
import { keys5, useSttStatus } from '../api/queries';
import type { SttDevices, SttElevation, SttHistoryItem, SttMicTest, SttRefineModels, SttRefineStatus, SttSettings, SttVocab } from '../api/types';
import { Pill, Row, Seg } from '../components/ui';
import { queryClient } from '../lib/queryClient';
import { comboFromKeyEvent, elevationNotice, formatMb, installPercent, micLevelMessage, pairState, sttErrorMessage } from '../lib/voice';
import { useUi } from '../state/ui';

const SETTINGS_KEY = [...keys5.stt, 'settings'] as const;
const DEVICES_KEY = [...keys5.stt, 'devices'] as const;
const VOCAB_KEY = [...keys5.stt, 'vocab'] as const;
const HISTORY_KEY = [...keys5.stt, 'history'] as const;
const REFINE_MODELS_KEY = [...keys5.stt, 'refine-models'] as const;

/**
 * 설정 > 음성 입력 (FR-VC). 값을 바꾸는 것은 게임 PC에서만 가능하다(서버도 PC 본체 요청만 받는다).
 * 폰·태블릿에서는 상태만 보여 준다.
 */
export function VoiceSettings({ local }: { local: boolean }) {
  const status = useSttStatus(local);
  const st = status.data?.data;
  const settingsQ = useQuery({ queryKey: SETTINGS_KEY, queryFn: () => api.get<SttSettings>('/api/stt/settings'), enabled: local, retry: false });
  const devicesQ = useQuery({ queryKey: DEVICES_KEY, queryFn: () => api.get<SttDevices>('/api/stt/devices'), enabled: local, staleTime: 10_000, retry: false });
  const toast = useUi(s => s.toast);
  const s = settingsQ.data?.data;

  const save = async (body: Record<string, unknown>) => {
    try {
      const r = await api.put<{ restartRequired: boolean; hotkey: { registered: boolean; error: string | null; key: string | null } }>('/api/stt/settings', body);
      await queryClient.invalidateQueries({ queryKey: keys5.stt });
      if (r.data.restartRequired) toast('이 설정은 앱을 다시 켠 뒤에 적용됩니다', 'info');
      if ('hotkey' in body && body.hotkey && !r.data.hotkey.registered) toast(r.data.hotkey.error ?? '단축키를 등록하지 못했습니다', 'warn');
      return true;
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '설정을 저장하지 못했습니다', 'warn');
      return false;
    }
  };

  if (!local) return <p className="faint small">음성 입력은 게임 PC에서만 쓸 수 있습니다. 폰·태블릿에서는 설정도 사용도 할 수 없습니다.</p>;
  if (!st) return <p className="faint small">불러오는 중…</p>;

  return (
    <>
      <ModelRow local={local} />
      {local && s && (
        <>
          <MicRow s={s} devices={devicesQ.data?.data} onPick={id => void save({ deviceId: id })} />
          <HotkeyRow s={s} status={st.hotkey} elevation={st.elevation} onSave={save} />
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
          <Row label="말 끝 무음 시간">
            <Seg value={String(s.endSilenceMs)} options={[['300', '0.3초 (아주 빠름)'], ['400', '0.4초'], ['500', '0.5초 (기본)'], ['600', '0.6초'], ['700', '0.7초 (느긋)']]} onChange={v => void save({ endSilenceMs: Number(v) })} />
          </Row>
          <p className="faint small">말한 뒤 이만큼 조용하면 한 문장이 끝난 것으로 봅니다. 짧을수록 빨리 끊기고, 말하다 쉬면 문장이 쪼개질 수 있습니다. 바꾸면 앱을 다시 켠 뒤에 적용됩니다.</p>
          <Row label="말이 끝나면 바로 전송">
            <Seg value={s.autoSend ? 'on' : 'off'} options={[['off', '끄기'], ['on', '켜기']]} onChange={v => void save({ autoSend: v === 'on' })} />
          </Row>
          <p className={s.autoSend ? 'warn-text small' : 'faint small'}>
            {s.autoSend ? '잘못 알아들은 말도 그대로 게임 채팅으로 나갑니다. 50자를 넘는 문장은 보내지 않고 입력창에 남깁니다.' : '끄면 이 PC의 채팅 입력창에 적어 주고 Enter로 보냅니다. 게임을 전체 화면으로 하는 중에는 입력창이 보이지 않으니, 게임 중에 쓰려면 켜세요.'}
          </p>
          <RefineSection s={s} status={st.refine} onSave={save} />
          <Row label="띄어쓰기 넣기 (실험)">
            <Seg value={s.spacing ? 'on' : 'off'} options={[['off', '끄기'], ['on', '켜기']]} onChange={v => void save({ spacing: v === 'on' })} />
          </Row>
          {s.spacing && (
            <Row label="띄어쓰기 강도">
              <Seg value={String(s.spacingLevel)} options={[['0', '보수적'], ['1', '보통'], ['2', '적극적']]} onChange={v => void save({ spacingLevel: Number(v) })} />
            </Row>
          )}
          <p className="faint small">
            인식 글자는 띄어쓰기 없이 나옵니다("지금레이드들어갑니다"). 켜면 아는 낱말 사이에 공백을 넣고, 모르는 부분은 붙여 둡니다.
            높일수록 공백을 더 넣어 놓치는 곳이 줄지만(처음 보는 문장 기준 적극적: 넣은 공백의 80%가 맞고 62%를 찾음, 보수적: 85%·56%) 틀린 공백도 조금 늘어납니다. 게임의 채팅 확인창에서 최종 문장을 보고 취소할 수 있어 기본은 적극적입니다. 내가 보낸 낱말이 사전에 더해져 쓸수록 나아집니다.
          </p>
          <LearningSection />
          <HistorySection enabled={s.history} onToggle={on => void save({ history: on })} />
          <Row label="자주 쓰는 낱말 우선 인식 (실험)">
            <Seg value={s.hotwords ? 'on' : 'off'} options={[['off', '끄기'], ['on', '켜기']]} onChange={v => void save({ hotwords: v === 'on' })} />
          </Row>
          <p className="faint small">자주 쓴 낱말을 인식기에 미리 알려 줍니다. 효과가 크지 않고 일부 낱말만 고쳐졌습니다(실측). 켜거나 끄면 앱을 다시 켠 뒤에 적용됩니다.</p>
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
        <span>{st.modelInstalled ? <Pill tone="ok">설치됨</Pill> : downloading ? <Pill tone="warn">내려받는 중</Pill> : <Pill tone="warn">설치 필요</Pill>}</span>
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
      // 시험 중 409는 "이미 듣고 있습니다"가 아니라 서버가 준 문구("듣는 중이거나 시험 중입니다")가 맞다
      toast(err.code === 'STT_BUSY' ? err.message : sttErrorMessage(err.code, err.message), 'warn');
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
            <Pill tone={m.tone === 'danger' ? 'warn' : m.tone}>{result.device}</Pill> <span className="num">최대 {result.peakDb}dB</span> · {m.text}
            {result.fallback && ' (고른 마이크가 없어 기본 마이크로 시험했습니다)'}
          </p>
        );
      })()}
    </>
  );
}

/**
 * 전역 단축키 지정: "지정" → 원하는 키 조합을 누르면 저장.
 * 캡처 중에는 눌린 키를 이 화면의 다른 단축키로 새지 않게 막되, Tab은 막지 않고(키보드로 나갈 수 있게) 캡처를 끝낸다.
 * Esc, 다른 곳 클릭, 창 포커스 이탈로도 끝난다 — 끝나지 않은 채 남으면 나중에 누른 Ctrl+S 같은 조합이 단축키로 저장된다.
 */
function HotkeyRow({ s, status, elevation, onSave }: { s: SttSettings; status: { key: string | null; registered: boolean; error: string | null }; elevation: SttElevation | undefined; onSave: (body: Record<string, unknown>) => Promise<boolean> }) {
  const [capturing, setCapturing] = useState(false);
  const [hint, setHint] = useState<string | null>(null);
  const box = useRef<HTMLDivElement>(null);
  const saveRef = useRef(onSave);
  saveRef.current = onSave;
  const down = useRef(new Set<string>());   // 지금 눌려 있는 키 (브라우저의 수정키 상태는 왼쪽·오른쪽을 구분하지 못한다)   // onSave는 렌더마다 바뀌므로 effect가 매번 다시 등록되지 않게 ref로 읽는다

  useEffect(() => {
    if (!capturing) return;
    const stop = () => { setCapturing(false); setHint(null); down.current.clear(); };
    const onKey = (e: KeyboardEvent) => {
      if (e.code === 'Tab') { stop(); return; }   // 막지 않는다
      e.preventDefault();
      e.stopPropagation();
      down.current.add(e.code);
      if (e.code === 'Escape') { stop(); return; }
      const combo = comboFromKeyEvent(e, down.current);
      if (!combo) { setHint('Ctrl·Alt 중 하나와 글자·숫자를 함께 누르세요. (F1~F24는 혼자 눌러도 됩니다)'); return; }
      stop();
      void saveRef.current({ hotkey: combo });
    };
    const onPointer = (e: PointerEvent) => { if (!box.current?.contains(e.target as Node)) stop(); };
    const onUp = (e: KeyboardEvent) => { down.current.delete(e.code); };
    window.addEventListener('keydown', onKey, true);
    window.addEventListener('keyup', onUp, true);
    window.addEventListener('pointerdown', onPointer, true);
    window.addEventListener('blur', stop);
    return () => {
      window.removeEventListener('keydown', onKey, true);
      window.removeEventListener('keyup', onUp, true);
      window.removeEventListener('pointerdown', onPointer, true);
      window.removeEventListener('blur', stop);
    };
  }, [capturing]);

  return (
    <div ref={box}>
      <div className="kv wrap">
        <span className="muted">전역 단축키</span>
        <span>
          <kbd className={`kbd${capturing ? ' capturing' : ''}`}>{capturing ? '키를 누르세요…' : s.hotkey ?? '없음'}</kbd>{' '}
          {s.hotkey && !capturing && (status.registered ? <Pill tone="ok">등록됨</Pill> : <Pill tone="warn">등록 안 됨</Pill>)}
        </span>
      </div>
      <div className="acts">
        <button type="button" className="btn" onClick={() => { setCapturing(c => !c); setHint(null); }}>{capturing ? '취소 (Esc)' : s.hotkey ? '다른 키로 바꾸기' : '단축키 지정'}</button>
        {s.hotkey && !capturing && <button type="button" className="btn" onClick={() => void onSave({ hotkey: '' })}>지우기</button>}
      </div>
      {(() => { const n = elevationNotice(elevation); return n && <p className={n.tone === 'ok' ? 'small' : 'warn-text small'} role="status">{n.text}</p>; })()}
      {hint && <p className="warn-text small" role="status">{hint}</p>}
      {s.hotkey && !status.registered && status.error && <p className="warn-text small">{status.error}</p>}
      <p className="faint small">게임은 채팅을 보낼 때마다 확인창을 띄우고 Space로 보내고 ESC로 취소합니다. 단축키에 Space가 있으면 Ctrl을 누른 채 확인하려는 순간 보내기 대신 새 녹음이 시작될 수 있으니, Space가 없는 조합(예: LCtrl+Alt+V)이 더 안전합니다. 게임이 쓰지 않는 조합을 고르세요. 왼쪽 Ctrl만 누르면 "LCtrl+…"로 저장되어 오른쪽 Ctrl로는 동작하지 않습니다. 등록한 조합은 모든 프로그램에서 이 기능이 먼저 가져갑니다(예: Ctrl+Space는 한/영 전환이나 편집기 자동완성과 겹칠 수 있습니다).</p>
    </div>
  );
}

/**
 * 지난 음성 기록 보정 (FR-VC-16): 인식된 문장을 보고 직접 고치면 "인식 글자 → 고친 글자"를 바로 배운다.
 * 문장을 저장하는 것이라 최근 100개·7일만 이 PC에 두고, 끄거나 지울 수 있다.
 */
function HistorySection({ enabled, onToggle }: { enabled: boolean; onToggle: (on: boolean) => void }) {
  const toast = useUi(s => s.toast);
  const q = useQuery({ queryKey: HISTORY_KEY, queryFn: () => api.get<SttHistoryItem[]>('/api/stt/history'), retry: false, refetchInterval: 15_000 });
  const vocab = useQuery({ queryKey: VOCAB_KEY, queryFn: () => api.get<SttVocab>('/api/stt/vocab'), retry: false });
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState<string | null>(null);
  const [confirmClear, setConfirmClear] = useState(false);
  const items = q.data?.data ?? [];
  const learningOn = vocab.data?.data.learningOn ?? true;
  const refresh = () => Promise.all([queryClient.invalidateQueries({ queryKey: HISTORY_KEY }), queryClient.invalidateQueries({ queryKey: VOCAB_KEY })]);

  const learn = async (it: SttHistoryItem, text: string) => {
    setBusy(it.id);
    try {
      const r = await api.post<{ learned: number }>('/api/stt/history/correct', { id: it.id, text });
      toast(r.data.learned > 0 ? `배웠어요 (고친 부분 ${r.data.learned}곳, 다음부터 바로 적용)` : '낱말을 바꾼 부분이 없어 배울 것이 없습니다(띄어쓰기만 다른 경우)', r.data.learned > 0 ? 'info' : 'warn');
      setDrafts(d => { const { [it.id]: _gone, ...rest } = d; return rest; });
      await refresh();
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '배우지 못했습니다', 'warn');
    } finally { setBusy(null); }
  };
  const remove = async (id: string) => {
    try { await api.post('/api/stt/history/delete', { id }); await refresh(); } catch (e) { toast(e instanceof ApiError ? e.message : '지우지 못했습니다', 'warn'); }
  };
  const clear = async () => {
    setConfirmClear(false);
    try { await api.post('/api/stt/history/clear', {}); await refresh(); } catch (e) { toast(e instanceof ApiError ? e.message : '지우지 못했습니다', 'warn'); }
  };
  const when = (iso: string) => {
    const d = new Date(iso), t = new Date();
    const hm = d.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hour12: false });
    return d.toDateString() === t.toDateString() ? hm : `${d.getMonth() + 1}/${d.getDate()} ${hm}`;
  };

  return (
    <>
      <Row label="지난 음성 기록 보정">
        <Seg value={enabled ? 'on' : 'off'} options={[['on', '켜기'], ['off', '끄기']]} onChange={v => onToggle(v === 'on')} />
      </Row>
      <p className="faint small">
        인식한 문장을 최근 100개, 7일까지만 이 PC에 보관합니다(폰에서는 볼 수 없습니다). 잘못 알아들은 문장을 직접 고쳐서 [배우기]를 누르면 다음부터 바로 고쳐 줍니다.
        {!learningOn && <> <b className="warn-text">누적 학습이 꺼져 있어 배울 수 없습니다.</b></>}
      </p>
      {enabled && (
        <details className="hist">
          <summary>지난 음성 {items.length}개 — 보고 고치기</summary>
          {items.length === 0
            ? <p className="faint small">아직 기록이 없습니다. 음성으로 말하면 여기에 쌓입니다.</p>
            : (
              <ul className="hist-list" aria-label="지난 음성 기록">
                {items.map(it => {
                  const base = it.corrected ?? it.text;
                  const val = drafts[it.id] ?? base;
                  const changed = val.trim() !== '' && val.trim() !== base;
                  return (
                    <li key={it.id}>
                      <div className="hist-meta small">
                        <span className="num faint">{when(it.at)}</span>
                        {it.autoSent ? <Pill>전송됨</Pill> : <Pill tone="warn">초안</Pill>}
                        {it.corrected && <Pill tone="ok">배움</Pill>}
                        <button type="button" className="chip-x" aria-label="이 기록 지우기" onClick={() => void remove(it.id)}>×</button>
                      </div>
                      <div className="hist-edit">
                        <input type="text" value={val} maxLength={200} aria-label={`${it.text} 고치기`} onChange={e => setDrafts(d => ({ ...d, [it.id]: e.target.value }))}
                          onKeyDown={e => { if (e.key === 'Enter' && !e.nativeEvent.isComposing && changed) void learn(it, val); }} />
                        <button type="button" className="btn primary" disabled={!changed || busy === it.id || !learningOn} onClick={() => void learn(it, val)}>배우기</button>
                      </div>
                      <p className="faint small m0">인식한 글자: <span className="num">{it.original}</span></p>
                    </li>
                  );
                })}
              </ul>
            )}
          {items.length > 0 && (confirmClear
            ? <div className="acts"><span className="warn-text small">지난 음성 기록을 모두 지울까요? (배운 내용은 남습니다)</span>
                <button type="button" className="btn" onClick={() => setConfirmClear(false)}>아니요</button>
                <button type="button" className="btn primary" onClick={() => void clear()}>모두 지우기</button></div>
            : <div className="acts"><button type="button" className="btn" onClick={() => setConfirmClear(true)}>기록 모두 지우기</button></div>)}
        </details>
      )}
    </>
  );
}

/** 누적 학습 (FR-VC-08): 쓸수록 내 말투와 게임 용어에 맞춰진다. 보기·끄기·지우기는 게임 PC에서만 */
function LearningSection() {
  const toast = useUi(s => s.toast);
  const q = useQuery({ queryKey: VOCAB_KEY, queryFn: () => api.get<SttVocab>('/api/stt/vocab'), retry: false });
  const [confirmClear, setConfirmClear] = useState(false);
  const v = q.data?.data;
  if (!v) return null;

  const run = async (call: () => Promise<{ data: SttVocab }>) => {
    try {
      queryClient.setQueryData(VOCAB_KEY, await call());
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '학습 내용을 바꾸지 못했습니다', 'warn');
    }
  };
  const pair = (p: { from: string; to: string }, action: 'on' | 'off' | 'delete') => run(() => api.post<SttVocab>('/api/stt/vocab/pair', { from: p.from, to: p.to, action }));

  return (
    <>
      <Row label="누적 학습">
        <Seg value={v.learningOn ? 'on' : 'off'} options={[['on', '켜기'], ['off', '끄기']]} onChange={x => void run(() => api.put<SttVocab>('/api/stt/vocab', { learningOn: x === 'on' }))} />
      </Row>
      <p className="faint small">
        보낸 채팅의 낱말을 세고, 인식된 글자를 고쳐 보내면 그 고친 부분을 배웁니다. 같은 부분을 두 번 고치면 다음부터 자동으로 고쳐 줍니다.
        문장 전체는 저장하지 않고 낱말과 고친 부분만 이 PC에 남기며, 90일 안 쓴 것은 지웁니다.
      </p>
      {v.warning && <p className="warn-text small" role="status">{v.warning}</p>}

      {v.pairs.length > 0 && (
        <ul className="vocab-list" aria-label="고친 부분">
          {v.pairs.map(p => {
            const st = pairState(p);
            return (
              <li key={`${p.from}>${p.to}`}>
                <span className="num">{p.from} → {p.to}</span> <Pill tone={st.tone === 'ok' ? 'ok' : st.tone === 'warn' ? 'warn' : undefined}>{st.text}</Pill>
                <span className="acts">
                  <button type="button" className="btn" onClick={() => void pair(p, p.off ? 'on' : 'off')}>{p.off ? '켜기' : '끄기'}</button>
                  <button type="button" className="btn" onClick={() => void pair(p, 'delete')} aria-label={`${p.from}→${p.to} 지우기`}>지우기</button>
                </span>
              </li>
            );
          })}
        </ul>
      )}

      {v.words.length > 0 && (
        <details className="vocab-words">
          <summary>자주 쓰는 낱말 {v.words.length}개</summary>
          <ul className="vocab-chips">
            {v.words.slice(0, 60).map(w => (
              <li key={w.word}>
                <span>{w.word}</span> <span className="faint num">{w.count}</span>
                <button type="button" className="chip-x" aria-label={`${w.word} 지우기`} onClick={() => void run(() => api.post<SttVocab>('/api/stt/vocab/word', { word: w.word }))}>×</button>
              </li>
            ))}
          </ul>
        </details>
      )}

      {(v.pairs.length > 0 || v.words.length > 0) && (
        confirmClear
          ? <div className="acts"><span className="warn-text small">학습한 내용을 모두 지울까요?</span>
              <button type="button" className="btn" onClick={() => setConfirmClear(false)}>아니요</button>
              <button type="button" className="btn primary" onClick={() => { setConfirmClear(false); void run(() => api.post<SttVocab>('/api/stt/vocab/clear', {})); }}>모두 지우기</button></div>
          : <div className="acts"><button type="button" className="btn" onClick={() => setConfirmClear(true)}>학습 내용 모두 지우기</button></div>
      )}
    </>
  );
}

/**
 * AI로 다듬기: 인식한 말을 보내기 전에 이 PC의 Ollama가 잘못 알아들은 낱말과 띄어쓰기를 고친다.
 * 모델 이름이 cloud로 끝나면 말한 내용이 Ollama 클라우드로 나가므로 경고한다.
 */
function RefineSection({ s, status, onSave }: { s: SttSettings; status: SttRefineStatus | undefined; onSave: (b: Record<string, unknown>) => Promise<boolean> }) {
  const modelsQ = useQuery({ queryKey: REFINE_MODELS_KEY, queryFn: () => api.get<SttRefineModels>('/api/stt/refine/models'), staleTime: 15_000, retry: false, enabled: s.refine });
  const m = modelsQ.data?.data;
  const names = m?.models.map(x => x.name) ?? [];
  const selectedMissing = !!m && m.online && !names.includes(s.refineModel);
  const cloud = /(:|-)cloud$/i.test(s.refineModel);
  return (
    <>
      <Row label="AI로 다듬기">
        <Seg value={s.refine ? 'on' : 'off'} options={[['on', '켜기'], ['off', '끄기']]} onChange={v => void onSave({ refine: v === 'on' })} />
      </Row>
      {s.refine && (
        <>
          <div className="kv wrap">
            <span className="muted">다듬기 모델</span>
            <select aria-label="다듬기 모델 선택" value={s.refineModel} onChange={e => void onSave({ refineModel: e.target.value })}>
              {!names.includes(s.refineModel) && <option value={s.refineModel}>{s.refineModel}{m?.online ? ' (설치되지 않음)' : ''}</option>}
              {m?.models.filter(x => !x.cloud).map(x => <option key={x.name} value={x.name}>{x.name}</option>)}
              {m?.models.some(x => x.cloud) && <optgroup label="클라우드 (말한 내용이 밖으로 나감)">{m.models.filter(x => x.cloud).map(x => <option key={x.name} value={x.name}>{x.name}</option>)}</optgroup>}
            </select>
          </div>
          {m && !m.online && <p className="warn-text small" role="status">Ollama가 켜져 있지 않아 지금은 다듬지 않고 사전 방식으로 보냅니다. Ollama를 켜면 자동으로 쓰입니다.</p>}
          {selectedMissing && <p className="warn-text small" role="status">'{s.refineModel}' 모델이 설치되어 있지 않습니다. 터미널에서 ollama pull {s.refineModel} 으로 받으세요.</p>}
          {cloud && <p className="warn-text small" role="status">클라우드 모델입니다. 말한 문장이 Ollama 클라우드 서버로 전송되고 인터넷 지연이 붙습니다. 개인 정보를 말하지 않도록 주의하세요.</p>}
          {status?.lastError && !selectedMissing && <p className="faint small" role="status">마지막 시도: {status.lastError}</p>}
          {status && !status.lastError && status.lastMs != null && <p className="faint small">마지막 다듬기 {status.lastMs}ms</p>}
        </>
      )}
      <p className="faint small">
        모델이 띄어쓰기 없이 내놓는 글자에서 잘못 알아들은 낱말(예: 일러→힐러)과 띄어쓰기를 문맥으로 고친 뒤 게임에 보냅니다. 이 PC의 Ollama만 부르고, 2.5초 안에 끝나지 않거나 이상한 결과가 나오면 버리고 사전 방식 결과를 보냅니다.
        측정(문장 30개, 이 PC): gemma4:e4b는 29개를 맞히고 0.17초, gemma4:12b도 29개지만 4초라 느립니다.
      </p>
    </>
  );
}
