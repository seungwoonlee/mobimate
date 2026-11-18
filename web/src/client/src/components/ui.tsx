import { useEffect, useId, useRef, type ReactNode } from 'react';
import { ApiError } from '../api/http';
import { ICONS } from './icons';
import { fmt } from '../lib/format';
import { useOffline, useUi } from '../state/ui';
import { useNow as useNowTick } from '../hooks/layout';
import { agoMs } from '../lib/format';
import type { Level } from '../api/types';
import { ROUTES, useRouter, type RouteName } from '../state/router';
import { classImageId, figureStyle, jobIconName } from '../lib/job';
import { useClassImage } from '../lib/classImage';

/** 아이콘 스프라이트. 앱 루트에 한 번만 둔다. */
export function IconSprite() {
  return (
    <svg width="0" height="0" style={{ position: 'absolute' }} aria-hidden="true">
      {Object.entries(ICONS).map(([id, body]) => (
        <symbol key={id} id={`i-${id}`} viewBox="0 0 24 24" dangerouslySetInnerHTML={{ __html: body }} />
      ))}
    </svg>
  );
}

export function Icon({ name, size, label }: { name: string; size?: number; label?: string }) {
  return (
    <svg className="ic" style={size ? { width: size, height: size } : undefined} role={label ? 'img' : undefined} aria-label={label} aria-hidden={label ? undefined : true}>
      <use href={`#i-${name}`} />
    </svg>
  );
}

/**
 * 변화 배지 (상세설계 §4.7): ▲ +150(ok) / ▼ -50(danger). inverse = 줄어드는 게 좋은 값(가방 무게).
 */
export function Delta({ value, unit = '', inverse = false, hideZero = false }: { value: number; unit?: string; inverse?: boolean; hideZero?: boolean }) {
  if (!value) return hideZero ? null : <span className="delta zero">±0</span>;
  const up = value > 0;
  const cls = inverse ? (up ? 'bad-up' : 'good-down') : up ? 'up' : 'down';
  const n = Math.abs(Math.round(value * 10) / 10);
  return (
    <span className={`delta ${cls}`} aria-label={`${up ? '증가' : '감소'} ${fmt(n)}${unit}`}>
      {up ? '▲' : '▼'} {fmt(n)}{unit}
    </span>
  );
}

const LEVEL_TEXT: Record<Level, [string, string]> = {
  ok: ['check', '정상'],
  warn: ['alert', '주의 · 다이어트 권장'],
  danger: ['alert', '과적 · 페널티'],
};

/** 게이지 (상세설계 §4.7): 색 + 아이콘 + 문구를 함께 쓴다. */
export function Gauge({ pct, level, label }: { pct: number; level: Level; label: string }) {
  return (
    <div className={`gauge ${level}`} role="meter" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(pct * 10) / 10}>
      <i style={{ width: `${Math.min(100, Math.max(0, pct))}%` }} />
    </div>
  );
}

export function LevelState({ level }: { level: Level }) {
  const [icon, text] = LEVEL_TEXT[level];
  return <span className={`state ${level}`}><Icon name={icon} />{text}</span>;
}

/** 진행 링: 가운데에 done/total (상세설계 §4.7). */
export function Ring({ done, total, size = 64, label }: { done: number; total: number; size?: number; label: string }) {
  const r = 26, c = 2 * Math.PI * r, f = total ? done / total : 0;
  return (
    <svg className="ring" viewBox="0 0 64 64" style={{ width: size, height: size }} role="img" aria-label={`${label} ${done}/${total} 완료`}>
      <circle className="bg" cx="32" cy="32" r={r} />
      <circle className="fg" cx="32" cy="32" r={r} strokeDasharray={`${c * f} ${c}`} />
      <text x="32" y="38" textAnchor="middle">{done}/{total}</text>
    </svg>
  );
}

export function Pill({ tone = 'plain', children, title }: { tone?: 'ok' | 'warn' | 'danger' | 'info' | 'gold' | 'cyan' | 'plain' | 'lime' | 'violet'; children: ReactNode; title?: string }) {
  return <span className={`pill ${tone}`} title={title}>{children}</span>;
}

/** 직업별 아이콘 (직업 이름으로 계열을 고른다). 장식이라 스크린 리더는 건너뛴다. */
export function JobIcon({ job, size }: { job: string | null | undefined; size?: number }) {
  const id = classImageId(job);
  const has = useClassImage(id);
  if (!id || has !== 'ok') return <Icon name={jobIconName(job)} size={size} />;
  const box = Math.round((size ?? 16) * 1.9);   // 얼굴·상반신이 알아보이는 정사각형 칸 (모든 직업이 같은 크기)
  return <span className="jobimg" role="img" aria-label={`${job ?? ''} 클래스`} style={{ height: box, width: box, ...figureStyle(id) }} />;
}

/** 카드 머리글 */
export function CardHead({ icon, title, right }: { icon?: string; title: ReactNode; right?: ReactNode }) {
  return (
    <div className="card-h">
      <h3>{icon && <Icon name={icon} />}{title}</h3>
      {right}
    </div>
  );
}

/** 불러오는 중·오류·빈 상태 */
export function Skeleton({ lines = 3 }: { lines?: number }) {
  return (
    <div className="card skel" role="status" aria-busy="true" aria-label="불러오는 중">
      {Array.from({ length: lines }, (_, i) => <i key={i} style={{ width: `${90 - i * 15}%` }} />)}
    </div>
  );
}

/**
 * 게임 CLI의 일시적인 실패(연결 끊김·시작·캐릭터 선택·로딩 중 응답 지연 등)인가. 이런 실패는 화면에 오류로 보여 줄 필요가 없다(2026-10-22 승운 요청):
 * 곧 저절로 풀리고 사용자가 할 수 있는 일도 없다. CLI를 찾지 못함(CLI_MISSING)처럼 사용자가 고쳐야 하는 오류는 그대로 보인다.
 */
export const isTransientCliError = (e: unknown): boolean => e instanceof ApiError && (e.code === 'CLI_TIMEOUT' || e.code === 'CLI_FAILED' || e.code === 'PARSE_ERROR');

export function ErrorCard({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const quiet = isTransientCliError(error);
  // 일시적인 CLI 실패: 빨간 오류 대신 불러오는 모양을 두고 몇 초 뒤 알아서 다시 시도한다
  useEffect(() => {
    if (!quiet || !onRetry) return;
    const t = window.setTimeout(onRetry, 3000);
    return () => window.clearTimeout(t);
  }, [quiet, onRetry, error]);
  if (quiet) return <Skeleton />;
  const msg = error instanceof Error ? error.message : '데이터를 불러오지 못했습니다.';
  return (
    <div className="card err" role="alert">
      <span className="state danger"><Icon name="alert" />{msg}</span>
      {onRetry && <button type="button" className="btn" onClick={onRetry}><Icon name="refresh" />다시 시도</button>}
    </div>
  );
}

export function Toasts() {
  const toasts = useUi(s => s.toasts);
  const dismiss = useUi(s => s.dismiss);
  const icon = { ok: 'check', warn: 'alert', danger: 'alert', info: 'spark' } as const;
  const urgents = toasts.filter(t => t.level === 'danger' || t.level === 'warn');
  const urgent = urgents[urgents.length - 1];   // Array.prototype.at은 iOS 15.4 이전에 없다
  return (
    <>
      <div className="toasts" aria-live="polite">
        {toasts.map(t => (
          <button type="button" key={t.id} className={`toast ${t.level}`} onClick={() => dismiss(t.id)} aria-label={`${t.message} (눌러서 닫기)`}>
            <Icon name={icon[t.level]} />{t.message}
          </button>
        ))}
      </div>
      <div className="sr-only" aria-live="assertive">{urgent?.message ?? ''}</div>
    </>
  );
}

/**
 * 대화상자 (상세설계 §4.7): 네이티브 `<dialog>`를 `showModal()`로 연다.
 * 포커스 가두기·Esc·뒤로 가기 닫기·닫은 뒤 포커스 복귀·나머지 화면 비활성은 브라우저가 한다(modern-web-guidance `dialog`).
 * 바깥을 눌러 닫기는 `closedby="any"`, 지원하지 않는 브라우저(Safari)에서는 같은 동작을 포인터 위치로 대신한다.
 * Compact에서는 CSS가 하단 시트 모양으로 바꾼다.
 */
export function Dialog({ title, onClose, children, actions, wide }: { title: string; onClose: () => void; children: ReactNode; actions?: ReactNode; /** 목록·표처럼 가로로 넓게 보여야 하는 창 */ wide?: boolean }) {
  const id = useId();
  const box = useRef<HTMLDialogElement>(null);
  const close = useRef(onClose);
  close.current = onClose;   // 부모가 매번 새 함수를 넘겨도 다시 열지 않게 한다
  useEffect(() => {
    const el = box.current!;
    if (typeof el.showModal === 'function') { if (!el.open) el.showModal(); } else el.setAttribute('open', '');
    const onNativeClose = () => close.current();
    el.addEventListener('close', onNativeClose);
    // closedby 미지원: 누르기와 떼기가 모두 대화상자 밖일 때만 닫는다 (글자 선택 드래그가 배경에서 끝나도 닫히지 않게)
    let downOutside = false;
    const outside = (e: PointerEvent) => { const r = el.getBoundingClientRect(); return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom; };
    const down = (e: PointerEvent) => { downOutside = outside(e); };
    const up = (e: PointerEvent) => { if (downOutside && outside(e)) el.close(); };
    const needFallback = !('closedBy' in HTMLDialogElement.prototype);
    if (needFallback) { el.addEventListener('pointerdown', down); el.addEventListener('pointerup', up); }
    return () => { el.removeEventListener('close', onNativeClose); el.removeEventListener('pointerdown', down); el.removeEventListener('pointerup', up); if (el.open) el.close(); };
  }, []);
  return (
    <dialog className={`dlg${wide ? ' wide' : ''}`} aria-labelledby={id} ref={box} {...({ closedby: 'any' } as object)}>
      <h3 id={id}>{title}</h3>
      {children}
      <div className="acts">{actions ?? <button type="button" className="btn" onClick={onClose}>닫기</button>}</div>
    </dialog>
  );
}

/**
 * 설명 말풍선: 마우스를 올리거나 포커스·길게 누르기(interestfor 지원 브라우저), 그 밖에는 눌러서 연다(popovertarget, 모든 최신 브라우저).
 * `title=`은 터치에서 볼 수 없어 중요한 안내(잠금 사유·판정 근거)에 쓰지 않는다 (modern-web-guidance `interest-triggered-tooltips`).
 */
export function Tip({ text, className, label, children }: { text: string; className?: string; label?: string; children: ReactNode }) {
  const id = useId();
  return (
    <>
      <button type="button" className={`tip-btn${className ? ` ${className}` : ''}`} aria-label={label} aria-describedby={id}
        {...({ popovertarget: id, interestfor: id } as object)}>{children}</button>
      <span id={id} className="tip-pop" role="tooltip" {...({ popover: 'hint' } as object)}>{text}</span>
    </>
  );
}

/**
 * 신선도 (FR-CN-03·04): "12초 전". 끊긴 동안에는 "N분 전 데이터"를 경고색으로 보여 준다.
 * at = 이 기기가 데이터를 받은 시각(ms). 서버 시계와 섞지 않는다.
 */
export function Fresh({ at }: { at: number | undefined }) {
  const now = useNowTick();
  const offline = useOffline();
  if (!at) return null;
  const text = agoMs(at, now);
  return offline
    ? <span className="pill warn" role="status">{text} 데이터</span>
    : <span className="faint small fresh">{text}</span>;
}

/** 화면 이동 카드: <a>로 만든다(새 탭 열기·접근성). 누르면 앱 안에서 이동한다 (상세설계 §4.9). */
export function LinkCard({ to, params, className = '', label, children }: { to: RouteName; params?: Record<string, string>; className?: string; label?: string; children: ReactNode }) {
  const go = useRouter(s => s.go);
  const path = ROUTES.find(r => r.name === to)!.path + (params ? `?${new URLSearchParams(params)}` : '');
  return (
    <a className={`card link ${className}`} href={path} aria-label={label}
      onClick={e => { if (e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return; e.preventDefault(); go(to, params); }}>
      {children}
    </a>
  );
}

/** 설정 한 줄: 이름 + 값 */
export function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="kv wrap"><span className="muted">{label}</span>{children}</div>;
}

/** 여러 선택지 중 하나 고르기 */
export function Seg({ value, options, onChange, disabled }: { value: string; options: [string, string][]; onChange: (v: string) => void; disabled?: boolean }) {
  return (
    <span className="seg" role="group">
      {options.map(([v, l]) => <button key={v} type="button" aria-pressed={value === v} disabled={disabled} onClick={() => onChange(v)}>{l}</button>)}
    </span>
  );
}
