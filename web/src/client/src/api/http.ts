import type { Envelope, ErrorBody, Session } from './types';

/** 서버 오류 (REQUIREMENTS §7: { error: { code, message } }). status 0 = 네트워크 오류. */
export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) {
    super(message);
  }
}

async function parse<T>(res: Response): Promise<Envelope<T>> {
  const text = await res.text();
  let body: unknown = null;
  try { body = text ? JSON.parse(text) : null; } catch { /* HTML 등 */ }
  if (!res.ok) {
    const err = (body as ErrorBody | null)?.error;
    throw new ApiError(res.status, err?.code ?? `HTTP_${res.status}`, err?.message ?? `요청이 실패했습니다 (${res.status})`);
  }
  return body as Envelope<T>;
}

async function send<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<Envelope<T>> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  let res: Response;
  try {
    res = await fetch(path, { method, headers, signal, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch (err) {
    if ((err as Error).name === 'AbortError') throw err;
    throw new ApiError(0, 'NETWORK', 'PC와 연결할 수 없습니다.');
  }
  return parse<T>(res);
}

/** 이 화면을 연 곳이 게임 PC(local)인지 폰·태블릿(lan)인지. 폰에서는 PC 전용 버튼을 숨긴다. */
export const session = (): Promise<Session> => send<Session>('GET', '/api/session').then(r => r.data);

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => send<T>('GET', path, undefined, signal),
  post: <T>(path: string, body?: unknown) => send<T>('POST', path, body),
  put: <T>(path: string, body?: unknown) => send<T>('PUT', path, body),
  del: <T>(path: string) => send<T>('DELETE', path),
};
