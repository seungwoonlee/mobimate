import { useEffect, useRef, useState } from 'react';
import { create } from 'zustand';
import { api, ApiError } from '../../api/http';
import { keys, keys5, useEngines, useLife } from '../../api/queries';
import type { AskEvent, Engine, Envelope, Life } from '../../api/types';
import { Dialog, Icon, Pill } from '../../components/ui';
import { collectAll, startGather } from '../../lib/actions';
import { WINGS_PER_BATCH, gatherBatches } from '../life';
import { Markdown } from '../../lib/markdown';
import { queryClient } from '../../lib/queryClient';
import { useRouter } from '../../state/router';
import { isOffline, useUi } from '../../state/ui';

/**
 * 빠른 실행 (최대 5개): 누르면 바로 게임에서 실행한다(확인 카드 없음). 앱 안 화면으로 가는 버튼은 두지 않는다.
 * 고정 2개(가공물 수거·행동 정지) + 즐겨찾기 채집물 최대 3개(100개씩). 즐겨찾기가 없으면 있는 것만 보인다.
 */
export const QUICK_GATHER_COUNT = 100;
export function quickActions(life: Life | undefined): { label: string; hint: string; icon: string; text: string }[] {
  const favs = (life?.gatherables ?? []).filter(g => g.favorite && g.toolOk).slice(0, 3);
  return [
    { label: '가공물 모두 수거', hint: '완료된 가공물 한 번에 받기', icon: 'inbox', text: '가공물 수거해줘' },
    ...favs.map(g => ({ label: `${g.name} 채집`, hint: `목표 ${QUICK_GATHER_COUNT}개 · 가방 ${g.inBag}개`, icon: 'leaf', text: `${g.name} ${QUICK_GATHER_COUNT}개 채집해줘` })),
    { label: '행동 정지', hint: '채집·행동 멈추기', icon: 'stop', text: '정지' },
  ];
}

const COST: Record<Engine['costTier'], [string, 'plain' | 'ok' | 'warn']> = {
  builtin: ['무료·내장', 'plain'],
  localFree: ['무료·로컬', 'ok'],
  paid: ['계정 과금', 'warn'],
};

type Msg =
  | { id: number; who: 'me'; text: string }
  | { id: number; who: 'ai'; text: string; streaming: boolean; engine?: string; error?: boolean }
  | { id: number; who: 'note'; text: string; ok: boolean };

/** id를 뺀 메시지 (합집합의 각 형태마다 Omit) */
type NewMsg = Msg extends infer M ? (M extends Msg ? Omit<M, 'id'> : never) : never;

/** 대화는 메모리에 둔다: 도크를 닫거나 레이아웃이 바뀌어도 이어진다 (FR-MB-03). AI 대화는 이 기기에만 있다 (FR-AI-04). */
interface AiState {
  msgs: Msg[];
  busy: boolean;
  push: (m: NewMsg) => number;
  patch: (id: number, p: Partial<Msg>) => void;
  setBusy: (b: boolean) => void;
}
let nextId = 1;
const useAi = create<AiState>(set => ({
  msgs: [{ id: 0, who: 'ai', text: '모비노기 AI도우미가 준비됐습니다. 가이드 카드를 누르거나 자유롭게 물어보세요.', streaming: false }],
  busy: false,
  push: m => { const id = nextId++; set(s => ({ msgs: [...s.msgs.slice(-80), { ...m, id } as Msg] })); return id; },
  patch: (id, p) => set(s => ({ msgs: s.msgs.map(m => (m.id === id ? ({ ...m, ...p } as Msg) : m)) })),
  setBusy: busy => set({ busy }),
}));

let abort: AbortController | null = null;

/** 질문 보내기: 응답 본문을 NDJSON 줄 단위로 읽는다 (FR-AI-04). 중지하면 요청을 끊는다 (FR-AI-08). */
async function ask(text: string) {
  const ai = useAi.getState();
  if (ai.busy) return;
  ai.push({ who: 'me', text });
  ai.setBusy(true);
  let aiId: number | null = null;
  const ensureAi = () => (aiId ??= ai.push({ who: 'ai', text: '', streaming: true }));
  abort = new AbortController();
  try {
    const res = await fetch('/api/ai/ask', {
      method: 'POST', signal: abort.signal,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text }),
    });
    if (!res.ok || !res.body) {
      const body = await res.json().catch(() => null) as { error?: { message?: string } } | null;
      throw new ApiError(res.status, 'ASK', body?.error?.message ?? `질문을 보내지 못했습니다 (${res.status})`);
    }
    const reader = res.body.getReader();
    const dec = new TextDecoder();
    let buf = '';
    let acc = '';
    for (;;) {
      const { done, value } = await reader.read();
      if (done) { buf += dec.decode() + '\n'; }   // 남은 바이트·마지막 줄까지 처리
      else buf += dec.decode(value, { stream: true });
      let nl: number;
      while ((nl = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, nl).trim();
        buf = buf.slice(nl + 1);
        if (!line) continue;
        let ev: AskEvent;
        try { ev = JSON.parse(line) as AskEvent; } catch { continue; }
        switch (ev.type) {
          case 'token':
            acc += ev.t;
            useAi.getState().patch(ensureAi(), { text: acc });
            break;
          case 'done':
            if (aiId != null) useAi.getState().patch(aiId, { streaming: false, engine: ev.engine });
            break;
          case 'error':
            useAi.getState().patch(ensureAi(), { text: ev.message, streaming: false, error: true });
            break;
          case 'action':
            useAi.getState().push({ who: 'note', text: ev.message, ok: ev.ok });
            break;
          case 'navigate':
            useRouter.getState().goPath(ev.to);
            break;
          case 'intent':
            await runIntent(ev);   // 확인 카드 없이 바로 실행하고, 무엇을 한다고만 알린다 (이상하면 사용자가 정지한다)
            break;
        }
      }
      if (done) break;
    }
  } catch (e) {
    const stopped = (e as Error).name === 'AbortError';
    useAi.getState().patch(ensureAi(), {
      text: stopped ? `${partial(aiId)}\n\n(중지했습니다)`.trim() : (e as Error).message,
      streaming: false, error: !stopped,
    });
  } finally {
    if (aiId != null) useAi.getState().patch(aiId, { streaming: false });
    useAi.getState().setBusy(false);
    abort = null;
  }
}

function partial(id: number | null): string {
  const m = useAi.getState().msgs.find(x => x.id === id);
  return m && m.who === 'ai' ? m.text : '';
}

/** 채집 확인 카드용 가방 보유 수 (최근 생활 조회 값) */
function bagCount(item: string): number | null {
  const life = queryClient.getQueryData<Envelope<Life>>(keys.life);
  return life?.data.gatherables?.find(g => g.name === item)?.inBag ?? null;
}

/** 채집·수거 의도를 바로 실행한다. 무엇을 하는지 대화창에 한 줄로 알린다. */
async function runIntent(ev: Extract<AskEvent, { type: 'intent' }>) {
  const note = (text: string, ok = true) => useAi.getState().push({ who: 'note', text, ok });
  if (ev.kind === 'collect') {
    if (!ev.items.length) { note('수거할 가공물이 없습니다', false); return; }
    note(`가공물 수거: ${ev.items.join(', ')}`);
    if (!(await collectAll(ev.items))) note('수거하지 못했습니다', false);
    return;
  }
  const have = bagCount(ev.item);
  const need = ev.count != null && have != null ? Math.max(0, ev.count - have) : ev.count;
  if (ev.count != null && need === 0) { note(`${ev.item}: 이미 목표 ${ev.count}개 이상 갖고 있어(가방 ${have}개) 채집하지 않았습니다`, false); return; }
  note(`${ev.item} 채집 시작: ${need != null ? `${need}개 · 정령의 날개 ${gatherBatches(need) * WINGS_PER_BATCH}개 사용` : '목표 없이'}`);
  if (!(await startGather(ev.item, need ?? null))) note(`${ev.item} 채집을 시작하지 못했습니다`, false);
}

/** 빠른 실행 버튼 (최대 5개). 누르면 바로 실행한다. */
function QuickActions({ busy, onPick }: { busy: boolean; onPick: (text: string) => void }) {
  const life = useLife().data?.data;
  return (
    <nav className="guide-nav ask" aria-label="빠른 실행">
      {quickActions(life).map(g => (
        <button key={g.label} type="button" onClick={() => onPick(g.text)} disabled={busy} data-needs-conn>
          <Icon name={g.icon} />
          <span><b>{g.label}</b><span className="gh">{g.hint}</span></span>
        </button>
      ))}
    </nav>
  );
}

export function AiPanel() {
  const draft = useUi(s => s.aiDraft);
  const setDraft = useUi(s => s.setAiDraft);
  const { msgs, busy } = useAi();
  const log = useRef<HTMLDivElement>(null);

  useEffect(() => { log.current?.scrollTo({ top: log.current.scrollHeight }); }, [msgs]);
  // 채집 확인 카드의 보유 수를 채우기 위해 생활 정보를 한 번 받아 둔다
  useEffect(() => { void queryClient.prefetchQuery({ queryKey: keys.life, queryFn: () => api.get<Life>('/api/life') }); }, []);

  const submit = (text = draft) => {
    const t = text.trim();
    if (!t || busy) return;
    if (t.length > 500) { useUi.getState().toast('질문은 500자까지입니다', 'warn'); return; }
    setDraft('');
    void ask(t);
  };

  return (
    <>
      <EnginePicker />
      <QuickActions busy={busy} onPick={submit} />
      <div className="log" ref={log} aria-live="polite" aria-busy={busy} aria-label="AI 대화" tabIndex={0}>
        {msgs.map(m => <MsgView key={m.id} m={m} />)}
      </div>
      <div className="composer">
        <div className="comp-row">
          <input id="aiIn" type="text" autoComplete="off" enterKeyHint="send" placeholder="예: 사과 20개 채집해줘, 오늘 숙제 뭐 남았어?" aria-label="AI 질문 입력"
            value={draft} maxLength={500} onChange={e => setDraft(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter' && !e.nativeEvent.isComposing) { e.preventDefault(); submit(); } }} />
          {busy
            ? <button type="button" className="send stop" onClick={() => abort?.abort()} aria-label="답변 중지"><Icon name="stop" /></button>
            : <button type="button" className="send" onClick={() => submit()} disabled={!draft.trim()} aria-label="질문 보내기"><Icon name="send" /></button>}
        </div>
      </div>
    </>
  );
}

function MsgView({ m }: { m: Msg }) {
  const [copied, setCopied] = useState(false);
  switch (m.who) {
    case 'me':
      return <div className="bubble me">{m.text}</div>;
    case 'note':
      return <p className={`ai-note ${m.ok ? 'ok' : 'warn'}`}><Icon name={m.ok ? 'check' : 'alert'} size={14} />{m.text}</p>;
    case 'ai':
      return (
        <div className={`bubble ai ${m.streaming ? 'cursor' : ''} ${m.error ? 'err' : ''}`}>
          {m.text ? <Markdown text={m.text} /> : <span className="faint">생각하는 중…</span>}
          {!m.streaming && m.text && m.id !== 0 && !m.error && (
            <button type="button" className="copy" onClick={async () => { try { await navigator.clipboard.writeText(m.text); setCopied(true); } catch { /* 복사 불가 */ } }}>
              {copied ? '복사됨' : '복사'}
            </button>
          )}
        </div>
      );
  }
}

/** 엔진 선택 (FR-AI-01~03): 비용 배지, 유료 엔진은 1회 확인, 다시 찾기. 전환 알림은 토스트로만 (FR-AI-09). */
function EnginePicker() {
  const q = useEngines();
  const toast = useUi(s => s.toast);
  const [open, setOpen] = useState(false);
  const [confirm, setConfirm] = useState<Engine | null>(null);
  const [busy, setBusy] = useState(false);
  const d = q.data?.data;
  const cur = d?.engines.find(e => e.id === d.current);

  const pick = async (e: Engine, confirmPaid = false) => {
    if (e.costTier === 'paid' && !confirmPaid) { setConfirm(e); setOpen(false); return; }
    setBusy(true);
    try {
      await api.put('/api/ai/engines/current', { id: e.id, confirmPaid });
      await queryClient.invalidateQueries({ queryKey: keys5.engines });
      toast(`AI 엔진: ${e.name}`, 'info');
    } catch (err) {
      toast(err instanceof ApiError ? err.message : '엔진을 바꾸지 못했습니다', 'warn');
    } finally {
      setBusy(false);
      setOpen(false);
      setConfirm(null);
    }
  };

  const rediscover = async () => {
    setBusy(true);
    try {
      await api.post('/api/ai/engines/discover');
      await queryClient.invalidateQueries({ queryKey: keys5.engines });
      toast('AI 엔진을 다시 찾았습니다', 'info');
    } catch (err) {
      toast(err instanceof ApiError ? err.message : '엔진을 찾지 못했습니다', 'warn');
    } finally { setBusy(false); }
  };

  return (
    <div className="engine">
      <button type="button" className="engine-btn" aria-haspopup="listbox" aria-expanded={open} onClick={() => setOpen(v => !v)} disabled={busy || isOffline(useUi.getState())}>
        <Icon name="spark" /><span className="nm">{cur?.name ?? '엔진 확인 중…'}</span>
        {cur?.recommended && <Pill tone="ok">추천</Pill>}
        {cur && <Pill tone={COST[cur.costTier][1]}>{COST[cur.costTier][0]}</Pill>}
        <Icon name="down" />
      </button>
      {open && d && (
        <div className="engine-menu" role="listbox" aria-label="AI 엔진" onKeyDown={e => { if (e.key === 'Escape') { e.stopPropagation(); setOpen(false); } }}>
          {d.engines.map(e => (
            <button key={e.id} type="button" role="option" aria-selected={e.id === d.current} onClick={() => pick(e)} title={e.description}>
              <span className="nm">{e.name}</span>{e.recommended && <Pill tone="ok">추천</Pill>}<Pill tone={COST[e.costTier][1]}>{COST[e.costTier][0]}</Pill>{e.id === d.current && <Icon name="check" />}
            </button>
          ))}
          {d.recommendedInstalled === false && (
            <p className="engine-rec small muted m0">추천: Ollama의 <b>{d.recommendedModel ?? 'gemma4:e4b'}</b> (4B급, 무료·로컬). 설치: <code>ollama pull {d.recommendedModel ?? 'gemma4:e4b'}</code></p>
          )}
          <button type="button" onClick={rediscover}><Icon name="refresh" /><span className="nm">엔진 다시 찾기</span></button>
        </div>
      )}
      {confirm && (
        <Dialog title="계정 과금 엔진" onClose={() => setConfirm(null)} actions={<>
          <button type="button" className="btn" onClick={() => setConfirm(null)}>취소</button>
          <button type="button" className="btn primary" onClick={() => pick(confirm, true)}>사용하기</button>
        </>}>
          <p><b>{confirm.name}</b>은(는) 로그인한 계정의 사용량·요금이 나갈 수 있습니다. 이 엔진을 쓸까요?</p>
        </Dialog>
      )}
    </div>
  );
}
