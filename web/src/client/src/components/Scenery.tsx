import { Icon } from './ui';

/** 날씨 그림 종류 */
export type WeatherKind = 'sunny' | 'cloudy' | 'rain' | 'snow' | 'storm' | 'fog';

/** 서버가 주는 날씨 원문(영문 코드 또는 한글)을 그림 종류로 바꾼다. 모르는 값은 흐림으로 본다. */
export function weatherKind(w: string | null | undefined): WeatherKind | null {
  if (!w) return null;
  const s = w.toLowerCase();
  if (/sun|clear|맑/.test(s)) return 'sunny';
  if (/snow|눈/.test(s)) return 'snow';
  if (/storm|thunder|폭풍|뇌우|천둥/.test(s)) return 'storm';
  if (/rain|비/.test(s)) return 'rain';
  if (/fog|mist|안개/.test(s)) return 'fog';
  return 'cloudy';
}

const CLOUD = 'M7.5 17.5h9.2a3.8 3.8 0 0 0 .5-7.57A5.2 5.2 0 0 0 7.2 9.4 4.1 4.1 0 0 0 7.5 17.5z';

/** 날씨 일러스트 (색을 가진 인라인 SVG). 색은 CSS 변수라 테마에 따라 바뀐다. */
export function WeatherIcon({ kind, size = 40 }: { kind: WeatherKind; size?: number }) {
  return (
    <svg className={`wx wx-${kind}`} viewBox="0 0 24 24" width={size} height={size} role="img" aria-label={WEATHER_TEXT[kind]}>
      {kind === 'sunny' && (
        <>
          <circle className="wx-sun" cx="12" cy="12" r="4.6" />
          <path className="wx-ray" d="M12 2.8v2.4M12 18.8v2.4M2.8 12h2.4M18.8 12h2.4M5.5 5.5l1.7 1.7M16.8 16.8l1.7 1.7M5.5 18.5l1.7-1.7M16.8 7.2l1.7-1.7" />
        </>
      )}
      {kind !== 'sunny' && <path className="wx-cloud" d={CLOUD} />}
      {kind === 'rain' && <path className="wx-drop" d="M9 19.2l-.8 2M13 19.2l-.8 2M17 19.2l-.8 2" />}
      {kind === 'snow' && <path className="wx-flake" d="M9 20h.01M13 21h.01M17 20h.01M11 19h.01M15 19h.01" />}
      {kind === 'storm' && <path className="wx-bolt" d="M12.8 15.5l-2.2 3.2h2.2l-1.2 3 3.4-4.2h-2.3l1.1-2z" />}
      {kind === 'fog' && <path className="wx-fog" d="M6 19.5h12M8 21.8h8" />}
    </svg>
  );
}

export const WEATHER_TEXT: Record<WeatherKind, string> = { sunny: '맑음', cloudy: '흐림', rain: '비', snow: '눈', storm: '폭풍', fog: '안개' };

export interface ErinnTime { date: string; hh: number; mm: number; day: boolean }

/** "에린 시간 2959-4-23 15:51 ☀️ (낮)" 같은 서버 표시 문자열에서 날짜·시각을 읽는다. 해석하지 못하면 null. */
export function parseErinn(s: string | null | undefined): ErinnTime | null {
  const m = s?.match(/(\d+-\d+-\d+)\s+(\d{1,2}):(\d{2})/);
  if (!m) return null;
  const hh = Number(m[2]);
  return { date: m[1], hh, mm: Number(m[3]), day: hh >= 6 && hh < 18 };
}

/** 하루 중 위치를 하늘 호(0~1)로: 낮 6~18시는 해, 밤 18~6시는 달이 왼쪽에서 오른쪽으로 지나간다. */
export function skyProgress(t: ErinnTime): number {
  const h = t.hh + t.mm / 60;
  return t.day ? (h - 6) / 12 : ((h < 6 ? h + 24 : h) - 18) / 12;
}

/**
 * 위치·날씨·에린 시간 그림 (FR-LY-04): 하늘 호를 지나가는 해·달, 에린 시계, 날씨 그림, 지역 이름.
 * 낮에는 밝은 하늘, 밤에는 어두운 하늘색이 깔린다.
 */
export function LocationScene({ place, weather, erinn }: { place: string | null | undefined; weather: string | null | undefined; erinn: string | null | undefined }) {
  const t = parseErinn(erinn);
  const kind = weatherKind(weather);
  const p = t ? Math.min(1, Math.max(0, skyProgress(t))) : 0.5;
  const W = 120, H = 34, x = 8 + p * (W - 16), y = H - Math.sin(Math.PI * p) * (H - 8);
  return (
    <div className="scene" data-phase={t ? (t.day ? 'day' : 'night') : 'unknown'}>
      <svg className="scene-sky" viewBox={`0 0 ${W} ${H + 4}`} aria-hidden="true">
        <path className="arc" d={`M8 ${H} Q ${W / 2} ${-H + 12} ${W - 8} ${H}`} />
        <line className="horizon" x1="2" y1={H} x2={W - 2} y2={H} />
        {t && (t.day
          ? <circle className="body sun" cx={x} cy={y} r="5" />
          : <path className="body moon" transform={`translate(${x - 5} ${y - 5})`} d="M8.5 1.2A5 5 0 1 0 9.8 8.6 4.2 4.2 0 0 1 8.5 1.2z" />)}
      </svg>
      <div className="scene-time">
        <span className="clock num" aria-label={t ? `에린 시간 ${t.hh}시 ${t.mm}분` : '에린 시간'}>{t ? `${String(t.hh).padStart(2, '0')}:${String(t.mm).padStart(2, '0')}` : '--:--'}</span>
        <span className="date">{t ? `에린 ${t.date}` : ''}{t ? <> · {t.day ? '낮' : '밤'}</> : null}</span>
      </div>
      <div className="scene-wx">
        {kind && <WeatherIcon kind={kind} size={34} />}
        <span>{kind ? WEATHER_TEXT[kind] : ''}</span>
      </div>
      <div className="scene-place" title={place ?? undefined}><Icon name="pin" />{place ?? '위치 확인 중'}</div>
    </div>
  );
}
