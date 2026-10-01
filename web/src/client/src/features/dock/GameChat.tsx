import { useEffect, useRef, useState } from 'react';
import { api, ApiError } from '../../api/http';
import { keys, keys5, useChatLog, useEngines, usePersonas, useSettings } from '../../api/queries';
import type { ChatLogEntry, ChatPreview } from '../../api/types';
import { Icon, Pill } from '../../components/ui';
import { queryClient } from '../../lib/queryClient';
import { useDevice } from '../../state/device';
import { isOffline, useUi } from '../../state/ui';
import { PersonaManager } from './PersonaManager';

/** 기본 페르소나 5종 (FR-CH-01). 값은 서버 ChatterPersona 이름이다. */
export const BUILTIN_PERSONAS: [string, string][] = [
  ['Villainess', '🌹 악덕영애'],
  ['Scrooge', '💰 구두쇠 영감'],
  ['MorningSpirit', '☀️ 안녕하닝 모닝이야'],
  ['GyeongsangAhjussi', '🌊 갱상도 아재'],
  ['IdolDancer', '✨ 아이돌 댄서'],
];

const MAX = 50;
const cp = (s: string) => [...s].length;

/** 게임 채팅 도크 (FR-GC-01~06, FR-CH-01~04): 페르소나 바 → 로그 → 입력(카운터·미리보기·전송). */
export function GameChatPanel() {
  return (
    <>
      <PersonaBar />
      <ChatLog />
      <Composer />
    </>
  );
}

/** 현재 페르소나를 서버 설정에 저장한다: 여러 기기가 같은 페르소나를 본다 (FR-MB-15) */
export async function setPersona(value: string) {
  try {
    await api.put('/api/settings', { chatterPersona: value });
  } catch (e) {
    useUi.getState().toast(e instanceof ApiError ? e.message : '페르소나를 바꾸지 못했습니다', 'warn');
  } finally {
    void queryClient.invalidateQueries({ queryKey: keys.settings });
  }
}

function PersonaBar() {
  const settings = useSettings();
  const persona = settings.data?.data.chatterPersona ?? 'Villainess';
  const personas = usePersonas();
  const engines = useEngines();
  const setDraft = useUi(s => s.setChatDraft);
  const toast = useUi(s => s.toast);
  const [busy, setBusy] = useState(false);
  const [manage, setManage] = useState(false);
  // 엔진 목록을 받기 전에는 판단하지 않는다(받는 동안 내장으로 보고 커스텀 선택을 지우지 않게)
  const engineKnown = !!engines.data;
  const builtinEngine = (engines.data?.data.current ?? 'builtin:guide').startsWith('builtin');

  // 내장 가이드 모드에서는 커스텀 페르소나의 고유 말투가 나오지 않으므로 기본 페르소나로 돌린다 (WPF 규칙, D-03)
  useEffect(() => {
    if (engineKnown && settings.data && builtinEngine && persona.startsWith('custom:')) void setPersona('Villainess');
  }, [engineKnown, builtinEngine, persona, settings.data]);

  const label = persona.startsWith('custom:')
    ? personas.data?.data.find(p => `custom:${p.id}` === persona)?.displayName ?? '커스텀'
    : BUILTIN_PERSONAS.find(([v]) => v === persona)?.[1] ?? persona;

  const line = async () => {
    setBusy(true);
    try {
      const custom = persona.startsWith('custom:');
      const r = await api.post<{ text: string; usedFallback: boolean }>('/api/chatter/line', {
        persona: custom ? 'Custom' : persona, customId: custom ? persona.slice(7) : undefined,
      });
      setDraft(r.data.text, `아무말 · ${label.replace(/^\S+\s/u, '')}`);
      if (r.data.usedFallback && custom) toast('AI 응답이 늦어 기본 대사를 넣었습니다', 'warn');
      window.setTimeout(() => document.querySelector<HTMLInputElement>('#chatIn')?.focus(), 30);
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '대사를 만들지 못했습니다', 'warn');
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <div className="persona">
        <div className="select">
          <select aria-label="아무말 페르소나" value={persona}
            onChange={e => e.target.value === '__manage' ? setManage(true) : void setPersona(e.target.value)}>
            {BUILTIN_PERSONAS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            {!builtinEngine && personas.data?.data.map(p => <option key={p.id} value={`custom:${p.id}`}>{p.displayName}</option>)}
            {!builtinEngine && <option value="__manage">＋ 페르소나 관리…</option>}
          </select>
          <Icon name="down" />
        </div>
        <button type="button" className="btn" onClick={line} disabled={busy} title="상황에 맞는 대사를 만들어 입력창에 채웁니다(자동 전송 안 함)">
          <Icon name="wand" />{busy ? '만드는 중…' : '한마디'}
        </button>
      </div>
      {builtinEngine && <p className="hint">AI 엔진을 연결하면 나만의 페르소나를 추가할 수 있습니다.</p>}
      {manage && <PersonaManager onClose={() => setManage(false)} />}
    </>
  );
}

function ChatLog() {
  const q = useChatLog();
  const box = useRef<HTMLDivElement>(null);
  const entries = q.data?.data ?? [];
  useEffect(() => { box.current?.scrollTo({ top: box.current.scrollHeight }); }, [entries.length]);
  return (
    <div className="log" ref={box} aria-live="polite" aria-label="게임 채팅 전송 기록" tabIndex={0}>
      {entries.length === 0 && <p className="faint small">아직 보낸 채팅이 없습니다. 입력창에 쓰고 Enter를 누르면 게임 전체 채팅으로 갑니다.</p>}
      {entries.map((e, i) => <LogItem key={`${e.at}-${i}`} e={e} />)}
    </div>
  );
}

function LogItem({ e }: { e: ChatLogEntry }) {
  const time = new Date(e.at).toLocaleTimeString('ko-KR', { hour12: false });
  return (
    <div className={`msg ${e.ok ? '' : 'fail'}`}>
      <div className="meta">
        {e.ok ? <span className="ok">✓ 전송</span> : <span className="fail">✗ 실패</span>}
        <span className="num">{time}</span><span>· {e.source}</span>
        {e.behaviour && <Pill tone="info">{e.behaviour}</Pill>}
      </div>
      <div>{e.message}</div>
      {!e.ok && <>
        {e.error && <div className="faint small">{e.error}</div>}
        {/* 이미 이모지가 붙은 최종 문장이므로 자동 이모티콘 없이 다시 보낸다 */}
        <button type="button" className="btn retry" onClick={() => sendChat(e.message, false, e.source === '직접' ? null : e.source)} data-needs-conn>다시 보내기</button>
      </>}
    </div>
  );
}

let sending = false;

/** 게임 채팅 전송 (FR-GC-04·06). 성공·실패 로그는 서버가 SSE "chat.logged"로도 알린다. */
async function sendChat(text: string, autoEmote: boolean, source: string | null = null): Promise<boolean> {
  const ui = useUi.getState();
  if (isOffline(ui)) { ui.toast('PC와 연결이 끊겨 보내지 않았습니다', 'warn'); return false; }
  if (sending) return false;
  sending = true;
  try {
    await api.post('/api/chat/game', { text, autoEmote, source: source ?? undefined });
    return true;
  } catch (e) {
    const err = e as ApiError;
    ui.toast(err.code === 'DUPLICATE' ? '같은 문장을 방금 보냈습니다' : `채팅 전송 실패: ${err.message}`, 'warn');
    return false;
  } finally {
    sending = false;
    void queryClient.invalidateQueries({ queryKey: keys5.chatLog });
  }
}

function Composer() {
  const draft = useUi(s => s.chatDraft);
  const source = useUi(s => s.chatDraftSource);
  const setDraft = useUi(s => s.setChatDraft);
  const devAuto = useDevice(s => s.autoEmote);
  const setDev = useDevice(s => s.set);
  const settings = useSettings();
  const autoEmote = devAuto ?? settings.data?.data.autoEmoteDefault ?? true;
  const [preview, setPreview] = useState<ChatPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const composing = useRef(false);

  // 미리보기(최종 문장·이모지·행동)는 서버 Core 규칙으로 계산한다 (FR-GC-02·03). 입력이 멈추면 한 번 묻는다.
  useEffect(() => {
    if (!draft.trim()) { setPreview(null); return; }
    const t = window.setTimeout(async () => {
      try {
        const r = await api.post<ChatPreview>('/api/chat/game/preview', { text: draft, autoEmote });
        setPreview(r.data);
      } catch { /* 미리보기 실패는 무시 */ }
    }, 180);
    return () => window.clearTimeout(t);
  }, [draft, autoEmote]);

  // 본문 한도: 이모지를 붙이면 그만큼 줄어든다. 조합 중에는 자르지 않는다(iOS·삼성 키보드 글자 중복 방지)
  const limit = autoEmote ? MAX - 2 : MAX;
  const clamp = (v: string) => (cp(v) > limit ? [...v].slice(0, limit).join('') : v);

  const send = async () => {
    const text = draft.replace(/\s*\n\s*/g, ' ').trim();
    if (!text || busy) return;
    setBusy(true);
    const ok = await sendChat(text, autoEmote, source);
    setBusy(false);
    // 보내는 동안 새로 친 글자는 지우지 않는다 (보낸 문장 그대로일 때만 비운다)
    if (ok && useUi.getState().chatDraft === draft) { setDraft('', null); setPreview(null); }
  };

  const count = preview?.count ?? cp(draft);
  return (
    <div className="composer">
      <div className="comp-row">
        <input id="chatIn" type="text" autoComplete="off" enterKeyHint="send" placeholder="게임 전체 채팅으로 보낼 말 (Enter 전송)" aria-label="게임 채팅 입력"
          value={draft}
          onCompositionStart={() => { composing.current = true; }}
          onCompositionEnd={e => { composing.current = false; setDraft(clamp(e.currentTarget.value)); }}
          onChange={e => setDraft(composing.current ? e.target.value : clamp(e.target.value))}
          onKeyDown={e => { if (e.key === 'Enter' && !e.nativeEvent.isComposing) { e.preventDefault(); void send(); } }} />
        <button type="button" className="send" onClick={send} disabled={busy || !draft.trim()} aria-label="게임으로 전송" data-needs-conn>
          <Icon name="send" />
        </button>
      </div>
      <div className="comp-meta">
        <span className="pv">
          <label className="auto-emote"><input type="checkbox" checked={autoEmote} onChange={e => {
            setDev({ autoEmote: e.target.checked });
            if (e.target.checked && cp(draft) > MAX - 2) setDraft([...draft].slice(0, MAX - 2).join(''));   // 켜면 이모지 자리를 남긴다
          }} /> 자동 이모티콘</label>
          {preview && autoEmote && preview.emoji && <> · {preview.emoji}{preview.behaviour ? ` ${preview.behaviour}` : ''}</>}
        </span>
        <span className={`cnt num ${count >= 45 ? 'warn' : ''}`} aria-label={`글자 수 ${count} / ${MAX}`}>{count} / {MAX}</span>
      </div>
    </div>
  );
}
