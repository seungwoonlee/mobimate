import { useEffect, useId, useRef, type ReactNode } from 'react';
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
  const h = Math.round((size ?? 16) * 2.6);   // 실루엣이 알아보이도록 아이콘보다 훨씬 크게 (세로가 긴 전신)
  return <span className="jobimg" role="img" aria-label={`${job ?? ''} 클래스`} style={{ height: h, width: Math.round(h * 0.7), ...figureStyle(id) }} />;
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

export function ErrorCard({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
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
 * 대화상자 (상세설계 §4.7): 제목 연결, 열 때 첫 조작 요소에 포커스, Tab 순환 가두기, Esc로 닫기, 닫으면 연 요소로 포커스 복귀.
 * Compact에서는 CSS가 하단 시트 모양으로 바꾼다.
 */
export function Dialog({ title, onClose, children, actions, wide }: { title: string; onClose: () => void; children: ReactNode; actions?: ReactNode; /** 목록·표처럼 가로로 넓게 보여야 하는 창 */ wide?: boolean }) {
  const id = useId();
  const box = useRef<HTMLDivElement>(null);
  const close = useRef(onClose);
  close.current = onClose;   // 부모가 매번 새 함수를 넘겨도 포커스를 다시 잡지 않게 한다
  useEffect(() => {
    const ret = document.activeElement as HTMLElement | null;
    const el = box.current!;
    const focusables = () => Array.from(el.querySelectorAll<HTMLElement>('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])')).filter(x => !x.hasAttribute('disabled'));
    (focusables()[0] ?? el).focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { e.stopPropagation(); close.current(); }
      if (e.key === 'Tab') {
        const f = focusables();
        if (!f.length) return;
        const first = f[0], last = f[f.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
      }
    };
    el.addEventListener('keydown', onKey);
    return () => { el.removeEventListener('keydown', onKey); ret?.focus?.(); };
  }, []);
  return (
    <div className="dlg-wrap open" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className={`dlg${wide ? ' wide' : ''}`} role="dialog" aria-modal="true" aria-labelledby={id} ref={box} tabIndex={-1}>
        <h3 id={id}>{title}</h3>
        {children}
        <div className="acts">{actions ?? <button type="button" className="btn" onClick={onClose}>닫기</button>}</div>
      </div>
    </div>
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
