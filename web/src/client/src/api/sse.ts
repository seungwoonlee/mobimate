/**
 * SSE 연결 (상세설계 §3.5, FR-MB-12). EventSource 기본 재연결 대신 직접 지수 백오프(1→2→4…최대 30초)로 다시 붙는다.
 * 화면이 다시 보이면 바로 한 번 시도하고, 45초 동안 아무 이벤트(ping 포함)가 없으면 끊긴 것으로 보고 다시 연결한다.
 * EventSource는 401을 구분해 주지 않으므로 끊기면 /api/session으로 확인하고, 401이면 재시도를 멈춘다.
 *
 * 연결은 언제나 하나만 둔다: 새로 열기 전에 이전 연결·타이머를 정리하고, 세대 번호로 늦게 끝난 재시도 예약을 버린다.
 */
export type SseState = 'connecting' | 'open' | 'retrying' | 'unauthorized';
export interface SseHandlers {
  onEvent: (type: string, data: unknown) => void;
  onState: (s: SseState, retryAt?: number) => void;
}

const EVENTS = ['hello', 'status', 'header', 'toast', 'gather', 'chat.logged', 'state.changed', 'homework.changed', 'ping'];
const SILENCE_MS = 45_000;

export function connectSse(h: SseHandlers): () => void {
  let es: EventSource | null = null;
  let delay = 1;
  let timer: number | undefined;
  let watchdog: number | undefined;
  let gen = 0;
  let closed = false;

  const kick = (g: number) => {
    window.clearTimeout(watchdog);
    watchdog = window.setTimeout(() => { if (g === gen) void schedule(); }, SILENCE_MS);
  };

  const open = () => {
    if (closed) return;
    const g = ++gen;
    window.clearTimeout(timer);
    es?.close();
    h.onState('connecting');
    const src = new EventSource('/api/events');
    es = src;
    kick(g);   // 연결 중에 멈춘 경우도 잡는다
    src.onopen = () => { if (g !== gen) return; delay = 1; h.onState('open'); kick(g); };
    for (const t of EVENTS) {
      src.addEventListener(t, ev => {
        if (g !== gen) return;
        kick(g);
        let data: unknown = null;
        try { data = JSON.parse((ev as MessageEvent).data); } catch { /* ping 등 */ }
        h.onEvent(t, data);
      });
    }
    src.onerror = () => { if (g === gen) void schedule(); };
  };

  const schedule = async () => {
    if (closed) return;
    const g = ++gen;
    window.clearTimeout(watchdog);
    window.clearTimeout(timer);
    es?.close();
    es = null;
    try {
      const r = await fetch('/api/session', { credentials: 'same-origin' });
      if (g !== gen || closed) return;
      if (r.status === 401) { h.onState('unauthorized'); return; }
    } catch {
      if (g !== gen || closed) return;   // 네트워크 오류: 재시도
    }
    h.onState('retrying', Date.now() + delay * 1000);
    timer = window.setTimeout(open, delay * 1000);
    delay = Math.min(30, delay * 2);
  };

  const onVisible = () => {
    if (document.visibilityState === 'visible' && (!es || es.readyState === EventSource.CLOSED)) {
      delay = 1;
      open();
    }
  };
  document.addEventListener('visibilitychange', onVisible);
  open();

  return () => {
    closed = true;
    gen++;
    window.clearTimeout(timer);
    window.clearTimeout(watchdog);
    document.removeEventListener('visibilitychange', onVisible);
    es?.close();
  };
}
