import type { Envelope, ErrorBody, Session } from './types';

/** 서버 오류 (REQUIREMENTS §7: { error: { code, message } }). status 0 = 네트워크 오류. */
export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) {
    super(message);
  }
  get unauthorized() { return this.status === 401; }
}

let csrf: string | null = null;
let sessionPromise: Promise<Session> | null = null;
const listeners = new Set<(e: ApiError) => void>();

/** 401 등 전역 오류를 화면(인증 안내·연결 끊김 배너)에 알린다. */
export function onApiError(fn: (e: ApiError) => void) {
  listeners.add(fn);
  return () => { listeners.delete(fn); };
}

async function parse<T>(res: Response): Promise<Envelope<T>> {
  const text = await res.text();
  let body: unknown = null;
  try { body = text ? JSON.parse(text) : null; } catch { /* HTML 등 */ }
  if (!res.ok) {
    const err = (body as ErrorBody | null)?.error;
    const e = new ApiError(res.status, err?.code ?? `HTTP_${res.status}`, err?.message ?? `요청이 실패했습니다 (${res.status})`);
    listeners.forEach(l => l(e));
    throw e;
  }
  return body as Envelope<T>;
}

async function send<T>(method: string, path: string, body?: unknown, signal?: AbortSignal, retried = false): Promise<Envelope<T>> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (method !== 'GET') headers['X-MobiMate-Csrf'] = csrf ?? (await session()).csrf;
  let res: Response;
  try {
    res = await fetch(path, {
      method, headers, signal, credentials: 'same-origin',
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch (err) {
    if ((err as Error).name === 'AbortError') throw err;
    const e = new ApiError(0, 'NETWORK', 'PC와 연결할 수 없습니다.');
    listeners.forEach(l => l(e));
    throw e;
  }
  if (res.status === 403 && method !== 'GET' && !retried && res.headers.get('content-type')?.includes('json')) {
    const peek = await res.clone().json().catch(() => null) as ErrorBody | null;
    if (peek?.error?.code === 'CSRF') {
      csrf = null;
      await session(true);
      return send<T>(method, path, body, signal, true);
    }
  }
  return parse<T>(res);
}

/** 세션(기기·CSRF 값)을 한 번 읽어 둔다. 상태를 바꾸는 요청은 이 CSRF 값을 헤더로 보낸다 (SEC-04). */
export function session(refresh = false): Promise<Session> {
  if (!sessionPromise || refresh) {
    const p = send<Session>('GET', '/api/session').then(r => { csrf = r.data.csrf; return r.data; });
    p.catch(() => { if (sessionPromise === p) sessionPromise = null; });
    sessionPromise = p;
  }
  return sessionPromise;
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => send<T>('GET', path, undefined, signal),
  post: <T>(path: string, body?: unknown) => send<T>('POST', path, body),
  put: <T>(path: string, body?: unknown) => send<T>('PUT', path, body),
  del: <T>(path: string) => send<T>('DELETE', path),
};

/** 스트리밍 요청처럼 fetch를 직접 쓸 때 필요한 CSRF 값 (SEC-04) */
export async function csrfToken(): Promise<string> {
  return csrf ?? (await session()).csrf;
}
