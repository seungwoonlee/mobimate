/**
 * 화면 오류 보고 (FR-ST-03): 처리되지 않은 오류를 PC 서버 로그로 보낸다. 개인 데이터는 보내지 않고
 * (메시지·출처만, 스택 없음), 분당 10건까지만 보낸다. 보고하다 실패해도 조용히 넘어간다(오류 보고가 오류를 만들지 않게).
 */
const MAX_PER_MINUTE = 10;
let windowStart = 0;
let sent = 0;

export function reportError(message: string, source: string, now = Date.now()): boolean {
  if (now - windowStart >= 60_000) { windowStart = now; sent = 0; }
  if (sent >= MAX_PER_MINUTE) return false;
  sent++;
  try {
    void fetch('/api/client-errors', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, keepalive: true,
      body: JSON.stringify({ message: message.slice(0, 300), source: source.slice(0, 100) }),
    }).catch(() => { /* 서버에 닿지 않으면 버린다 */ });
  } catch { /* 보고 실패는 무시 */ }
  return true;
}

/** 전역 오류 처리기를 한 번 단다. */
export function installErrorReporting() {
  window.addEventListener('error', e => { reportError(e.message || '알 수 없는 오류', `${e.filename || 'window'}:${e.lineno ?? 0}`); });
  window.addEventListener('unhandledrejection', e => {
    const r = e.reason;
    reportError(r instanceof Error ? r.message : String(r), 'unhandledrejection');
  });
}

/** 테스트용: 분당 제한 상태 초기화 */
export function resetErrorReportLimit() { windowStart = 0; sent = 0; }
