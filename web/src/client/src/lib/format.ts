const nf = new Intl.NumberFormat('ko-KR');

export const fmt = (n: number | null | undefined) => (n == null ? '—' : nf.format(n));

/** 좁은 화면(글랜스 모드)용 축약 (FR-MB-06): 124,017 → 124K, 11,144,590 → 11.1M. */
export function short(n: number): string {
  const a = Math.abs(n);
  if (a >= 1_000_000) return `${(n / 1_000_000).toFixed(a >= 100_000_000 ? 0 : 1)}M`;
  if (a >= 10_000) return `${Math.round(n / 1000)}K`;
  return nf.format(n);
}

export const WEATHER: Record<string, string> = {
  Sunny: '맑음', Clear: '맑음', Cloudy: '흐림', Rain: '비', Rainy: '비', Snow: '눈', Snowy: '눈', Storm: '폭풍', Fog: '안개', Foggy: '안개',
};
export const weather = (w: string | null | undefined) => (w ? WEATHER[w] ?? w : null);

/** 남은 시간: 3시간 3분 15초 (FR-DT-05). 0 이하 = 수거 대기. */
export function remaining(sec: number): string {
  if (sec <= 0) return '수거 대기';
  const h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = Math.floor(sec % 60);
  if (h > 0) return `${h}시간 ${m}분 ${s}초`;
  if (m > 0) return `${m}분 ${s}초`;
  return `${s}초`;
}

/** 리셋까지 남은 시간: 2일 5시간 / 3시간 12분 / 12분. */
export function until(iso: string, now = Date.now()): string {
  const ms = new Date(iso).getTime() - now;
  if (ms <= 0) return '곧';
  const m = Math.floor(ms / 60000), h = Math.floor(m / 60), d = Math.floor(h / 24);
  if (d > 0) return `${d}일 ${h % 24}시간`;
  if (h > 0) return `${h}시간 ${m % 60}분`;
  return `${Math.max(1, m)}분`;
}

/** 신선도: 방금 / 12초 전 / 3분 전 (FR-CN-04). */
export function ago(iso: string | undefined, now = Date.now()): string {
  if (!iso) return '';
  const s = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000));
  if (s < 5) return '방금';
  if (s < 60) return `${s}초 전`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}분 전`;
  return `${Math.floor(m / 60)}시간 전`;
}

/** 신선도 (ms 기준, 이 기기 시계) */
export function agoMs(at: number, now = Date.now()): string {
  return ago(new Date(at).toISOString(), now);
}

export const pct = (done: number, total: number) => (total > 0 ? Math.round((done / total) * 100) : 0);
