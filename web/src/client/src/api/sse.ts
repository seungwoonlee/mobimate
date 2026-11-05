/**
 * SSE 연결 (상세설계 §3.5, FR-MB-12). EventSource 기본 재연결 대신 직접 지수 백오프(1→2→4…최대 30초)로 다시 붙는다.
 * 화면이 다시 보이면 바로 한 번 시도하고, 45초 동안 아무 이벤트(ping 포함)가 없으면 끊긴 것으로 보고 다시 연결한다.
 *
 * 연결은 언제나 하나만 둔다: 새로 열기 전에 이전 연결·타이머를 정리하고, 세대 번호로 늦게 끝난 재시도 예약을 버린다.
 */
export type SseState = 'connecting' | 'open' | 'retrying';
export interface SseHandlers {
  onEvent: (type: string, data: unknown) => void;
  onState: (s: SseState, retryAt?: number) => void;
}

const EVENTS = ['hello', 'status', 'header', 'toast', 'gather', 'chat.logged', 'state.changed', 'homework.changed', 'stt.state', 'stt.partial', 'stt.final', 'ping'];
const SILENCE_MS = 45_000;

function connectDirect(h: SseHandlers): () => void {
  let es: EventSource | null = null;
  let delay = 1;
  let timer: number | undefined;
  let watchdog: number | undefined;
  let gen = 0;
  let closed = false;

  const kick = (g: number) => {
    window.clearTimeout(watchdog);
    watchdog = window.setTimeout(() => { if (g === gen) schedule(); }, SILENCE_MS);
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
    src.onerror = () => { if (g === gen) schedule(); };
  };

  const schedule = () => {
    if (closed) return;
    ++gen;
    window.clearTimeout(watchdog);
    window.clearTimeout(timer);
    es?.close();
    es = null;
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

/**
 * 탭이 여러 개여도 서버 연결(SSE)은 한 탭만 둔다. 브라우저는 호스트당 동시 연결을 6개로 제한하는데(HTTP/1.1)
 * 탭마다 SSE를 하나씩 잡으면 일곱 번째 탭부터 모든 요청이 대기열에 갇혀 "무한 로딩"이 된다 (Chrome에서 확인).
 * Web Locks로 대표 탭 하나를 정하고, 대표 탭이 받은 이벤트·연결 상태를 BroadcastChannel로 나머지 탭에 나눠 준다.
 * 대표 탭이 닫히면(또는 절전으로 버려지면) 락이 풀려 기다리던 다른 탭이 이어받는다.
 * Web Locks·BroadcastChannel이 없는 환경에서는 예전처럼 탭마다 직접 연결한다.
 */
export function connectSse(h: SseHandlers): () => void {
  if (typeof navigator === 'undefined' || !('locks' in navigator) || typeof BroadcastChannel === 'undefined') return connectDirect(h);

  const bc = new BroadcastChannel('mm-sse');
  const abort = new AbortController();
  let stopDirect: (() => void) | null = null;
  let release: (() => void) | null = null;
  let last: { s: SseState; retryAt?: number } | null = null;
  let disposed = false;

  bc.onmessage = ev => {
    const m = ev.data as { k: string; t?: string; d?: unknown; s?: SseState; retryAt?: number };
    if (stopDirect) {   // 대표 탭: 새로 열린 탭이 현재 상태를 물으면 알려 준다
      if (m.k === 'who' && last) bc.postMessage({ k: 'state', ...last });
      return;
    }
    if (m.k === 'event' && m.t) h.onEvent(m.t, m.d);
    else if (m.k === 'state' && m.s) h.onState(m.s, m.retryAt);
  };

  h.onState('connecting');
  bc.postMessage({ k: 'who' });

  navigator.locks.request('mm-sse-leader', { signal: abort.signal }, () => new Promise<void>(resolve => {
    if (disposed) return resolve();
    release = resolve;
    stopDirect = connectDirect({
      onEvent: (t, d) => { bc.postMessage({ k: 'event', t, d }); h.onEvent(t, d); },
      onState: (s, retryAt) => { last = { s, retryAt }; bc.postMessage({ k: 'state', s, retryAt }); h.onState(s, retryAt); },
    });
  })).catch(() => { /* 기다리다 취소됨 */ });

  return () => {
    disposed = true;
    abort.abort();
    stopDirect?.();
    stopDirect = null;
    release?.();
    bc.close();
  };
}
