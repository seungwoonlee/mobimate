import { useEffect, useRef, useState } from 'react';
import { create } from 'zustand';
import { api, ApiError } from '../../api/http';
import { keys, keys5, useEngines } from '../../api/queries';
import type { AskEvent, Engine, Envelope, Life } from '../../api/types';
import { Dialog, Icon, Pill } from '../../components/ui';
import { collectAll, startGather } from '../../lib/actions';
import { Markdown } from '../../lib/markdown';
import { queryClient } from '../../lib/queryClient';
import { useRouter, type RouteName } from '../../state/router';
import { isOffline, useUi } from '../../state/ui';

/**
 * 내장 도우미 바로 가기 (FR-AI-20·21): 글로 된 준비된 답변 대신 앱의 해당 화면으로 바로 이동한다.
 * 한 줄에 버튼 하나씩 전체 폭으로 쌓는다(가로 스크롤 없음).
 */
const GUIDE: { label: string; hint: string; icon: string; to: RouteName; params?: Record<string, string> }[] = [
  { label: '내 캐릭터 전체 현황', hint: '캐릭터별 전투력·재화', icon: 'users', to: 'characters' },
  { label: '오늘 한눈에 보기', hint: '점수·가방·가공·주변', icon: 'home', to: 'overview' },
  { label: '콘텐츠 추천', hint: '입장 가능·추천 난이도', icon: 'trophy', to: 'stats', params: { tab: 'cutoffs' } },
  { label: '점수와 스탯', hint: '전투력·마도저항·생활력·매력', icon: 'stats', to: 'stats' },
  { label: '즐겨찾기 아이템', hint: '눈여겨보는 아이템의 개수 변화', icon: 'bag', to: 'inventory', params: { loc: 'fav' } },
  { label: '가방 다이어트', hint: '무게 줄이기', icon: 'scale', to: 'inventory', params: { loc: 'diet' } },
  { label: '재화', hint: '골드·데카·M캐시', icon: 'coins', to: 'currencies' },
  { label: '일일 미션과 요일 던전', hint: '오늘 남은 숙제', icon: 'list', to: 'homework', params: { tab: 'daily' } },
  { label: '가공 대기열과 수거', hint: '종류별 진행 상황', icon: 'clock', to: 'life' },
  { label: '채집 도우미', hint: '즐겨찾기 재료 채집', icon: 'leaf', to: 'life' },
  { label: '주변 플레이어', hint: '친구·길드원 먼저', icon: 'radar', to: 'nearby' },
  { label: '설정', hint: '테마·색상·LAN', icon: 'gear', to: 'settings' },
];

/** 가이드 쉘프 6개 (FR-AI-06, AI 엔진 연결 시 질문 예시) */
const SHELF = [
  '현재 전투력 기준 1티어 룬 세팅 및 무기 각인 공략',
  '일일 미션과 요일 던전 보상으로 골드 빠르게 모으기',
  '가공 시설 쿨타임 관리법과 철광석·약초·목재 채집 명당',
  '보스 브레이크 게이지 파훼법 및 장판 회피 요령',
  '현재 캐릭터 실시간 스탯, 전투력, 가방, 미션 정밀 진단',
  '가방 무게 초과 방지 및 계정 창고/캐릭터 창고 정리법',
];

const COST: Record<Engine['costTier'], [string, 'plain' | 'ok' | 'warn']> = {
  builtin: ['무료·내장', 'plain'],
  localFree: ['무료·로컬', 'ok'],
  paid: ['계정 과금', 'warn'],
};

type Msg =
  | { id: number; who: 'me'; text: string }
  | { id: number; who: 'ai'; text: string; streaming: boolean; engine?: string; error?: boolean }
  | { id: number; who: 'note'; text: string; ok: boolean }
  | { id: number; who: 'gather'; item: string; count: number | null; have: number | null; state: 'pending' | 'started' | 'cancelled' }
  | { id: number; who: 'collect'; items: string[]; state: 'pending' | 'done' | 'cancelled' };

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
            if (ev.kind === 'collect') useAi.getState().push({ who: 'collect', items: ev.items, state: 'pending' });
            else useAi.getState().push({ who: 'gather', item: ev.item, count: ev.count, have: bagCount(ev.item), state: 'pending' });
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

/** 바로 가기 목록. 대화가 시작되면 접어서 대화 영역을 넓힌다. */
function GuideNav({ open }: { open: boolean }) {
  const go = useRouter(s => s.go);
  return (
    <details className="guide" open={open}>
      <summary>바로 가기</summary>
      <nav className="guide-nav" aria-label="바로 가기">
        {GUIDE.map(g => (
          <button key={g.label} type="button" onClick={() => go(g.to, g.params)}>
            <Icon name={g.icon} />
            <span><b>{g.label}</b><span className="gh">{g.hint}</span></span>
          </button>
        ))}
      </nav>
    </details>
  );
}

export function AiPanel() {
  const draft = useUi(s => s.aiDraft);
  const setDraft = useUi(s => s.setAiDraft);
  const { msgs, busy } = useAi();
  const engines = useEngines();
  const builtin = (engines.data?.data.current ?? 'builtin:guide').startsWith('builtin');
  // 내장 가이드 모드에서는 가이드 카드를 크게 보이되, 대화가 시작되면 작은 칩으로 줄여 대화 영역을 넓힌다 (FR-AI-06)
  const bigShelf = !!engines.data && builtin && !msgs.some(m => m.who === 'me');
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
      {builtin
        ? <GuideNav open={bigShelf} />
        : (
          <div className="guide-nav ask" role="group" aria-label="질문 예시">
            {SHELF.map(q => (
              <button key={q} type="button" onClick={() => submit(q)} disabled={busy} title={q}><span><b>{q}</b></span></button>
            ))}
          </div>
        )}
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
    case 'gather':
      return <GatherCard m={m} />;
    case 'collect':
      return <CollectCard m={m} />;
  }
}

/** 채집 확인 카드 (FR-AI-05, D-07): 사용자가 [시작]을 눌러야 실행한다. */
function GatherCard({ m }: { m: Extract<Msg, { who: 'gather' }> }) {
  const patch = useAi(s => s.patch);
  const need = m.count != null && m.have != null ? Math.max(0, m.count - m.have) : m.count;
  if (m.state === 'pending' && m.count != null && need === 0) {
    return <div className="intent done"><h4><Icon name="leaf" />{m.item} 채집</h4><p>이미 목표 {m.count}개 이상 갖고 있습니다 (가방 {m.have}개). 채집하지 않았습니다.</p></div>;
  }
  return (
    <div className={`intent ${m.state === 'pending' ? '' : 'done'}`}>
      <h4><Icon name="leaf" />{m.item} 채집</h4>
      {m.state === 'pending' ? <>
        <p>{m.count != null ? <>목표 {m.count}개{m.have != null && <> · 가방 {m.have}개 → <b>{need}개 더</b></>} 채집합니다.</> : '목표 없이 채집을 시작합니다.'} 정령의 날개 5개를 씁니다.</p>
        <div className="acts">
          <button type="button" className="btn primary" data-needs-conn onClick={async () => { if (await startGather(m.item, need ?? null)) patch(m.id, { state: 'started' }); }}>채집 시작</button>
          <button type="button" className="btn" onClick={() => patch(m.id, { state: 'cancelled' })}>취소</button>
        </div>
      </> : <p>{m.state === 'started' ? '채집을 시작했습니다. 결과는 알림으로 옵니다.' : '취소했습니다.'}</p>}
    </div>
  );
}

function CollectCard({ m }: { m: Extract<Msg, { who: 'collect' }> }) {
  const patch = useAi(s => s.patch);
  return (
    <div className={`intent ${m.state === 'pending' ? '' : 'done'}`}>
      <h4><Icon name="inbox" />가공물 수거</h4>
      {m.state === 'pending' ? <>
        <p>수거 가능: {m.items.length ? m.items.join(', ') : '없음'}</p>
        <div className="acts">
          <button type="button" className="btn primary" disabled={!m.items.length} data-needs-conn onClick={async () => { if (await collectAll(m.items)) patch(m.id, { state: 'done' }); }}>모두 수거</button>
          <button type="button" className="btn" onClick={() => patch(m.id, { state: 'cancelled' })}>취소</button>
        </div>
      </> : <p>{m.state === 'done' ? '수거를 요청했습니다.' : '취소했습니다.'}</p>}
    </div>
  );
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
        {cur && <Pill tone={COST[cur.costTier][1]}>{COST[cur.costTier][0]}</Pill>}
        <Icon name="down" />
      </button>
      {open && d && (
        <div className="engine-menu" role="listbox" aria-label="AI 엔진" onKeyDown={e => { if (e.key === 'Escape') { e.stopPropagation(); setOpen(false); } }}>
          {d.engines.map(e => (
            <button key={e.id} type="button" role="option" aria-selected={e.id === d.current} onClick={() => pick(e)} title={e.description}>
              <span className="nm">{e.name}</span><Pill tone={COST[e.costTier][1]}>{COST[e.costTier][0]}</Pill>{e.id === d.current && <Icon name="check" />}
            </button>
          ))}
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
