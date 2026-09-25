import { useEffect, useRef, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Delta, Icon, Toasts } from '../components/ui';
import { useNow } from '../hooks/layout';
import { keys, useHeader, useStatus } from '../api/queries';
import { useLayout } from '../hooks/layout';
import { useAdaptiveRefresh } from '../hooks/useAdaptiveRefresh';
import { useDevice } from '../state/device';
import { ROUTES, useRouter, type RouteName } from '../state/router';
import { useOffline, useUi } from '../state/ui';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/http';
import { stopAction } from '../lib/actions';
import { fmt, weather } from '../lib/format';
import { Dock } from './Dock';

/** 하단 탭(Compact)에 둘 화면 (시안: 개요·가방·숙제·생활 + 채팅) */
const TAB_ROUTES: RouteName[] = ['overview', 'inventory', 'homework', 'life'];

export function AppShell({ children }: { children: ReactNode }) {
  const layout = useLayout();
  const scale = useDevice(s => s.scale);
  // 선택자로 필요한 값만 구독한다(토스트마다 셸 전체를 다시 그리지 않게)
  const dockOpen = useUi(s => s.dockOpen);
  const dockTab = useUi(s => s.dockTab);
  const setDock = useUi(s => s.setDock);
  const offline = useOffline();
  const route = useRouter(s => s.loc.name);
  const main = useRef<HTMLElement>(null);

  // 도크: Expanded 이상은 옆 패널(기본 열림), 그 아래·높이가 낮으면 시트 (상세설계 §4.5)
  const dockMode = layout.posture === 'book' ? 'split' : (layout.size === 'expanded' || layout.size === 'large') && !layout.short ? 'side' : 'sheet';
  const dockVisible = dockMode === 'split' || dockOpen;

  useShortcuts(dockMode === 'sheet' && dockOpen);

  // 화면을 바꾸면 맨 위로, 시트 도크는 닫는다
  useEffect(() => {
    main.current?.scrollTo({ top: 0 });
    if (dockMode === 'sheet') setDock(false);
  }, [route]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div
      className="app"
      style={{ ['--scale' as string]: scale }}
      data-size={layout.size}
      data-short={layout.short}
      data-touch={layout.coarse}
      data-posture={layout.posture}
      data-dock-mode={dockMode}
      data-dock-open={dockVisible}
      data-dock-tab={dockTab}
      data-offline={offline}
    >
      <TopBar compact={layout.size === 'compact'} />
      {offline && <OfflineBanner />}
      <NavRail onChat={() => setDock(true)} />
      <main className="main" ref={main}>
        <div className="main-inner">{children}</div>
      </main>
      <div className="hinge" aria-hidden="true" />
      {dockMode === 'sheet' && dockOpen && <div className="scrim" onClick={() => setDock(false)} />}
      <Dock mode={dockMode} onClose={() => setDock(false)} />
      <BottomTabs onChat={() => setDock(true)} />
      <button type="button" className="fab-stop" onClick={stopAction} aria-label="긴급 정지">
        <Icon name="stop" />
      </button>
      <Toasts />
    </div>
  );
}

/** 끊김 배너 (FR-MB-12): 다음 재시도까지 남은 초를 센다. */
function OfflineBanner() {
  const retryAt = useUi(s => s.retryAt);
  const now = useNow();
  const left = retryAt ? Math.max(0, Math.ceil((retryAt - now) / 1000)) : 0;
  return (
    <div className="offline-banner" role="status">
      <Icon name="alert" size={16} />PC와 연결 끊김 · {left > 0 ? `${left}초 후 다시 연결` : '다시 연결 중'}
    </div>
  );
}

function TopBar({ compact }: { compact: boolean }) {
  const header = useHeader();
  const status = useStatus();
  // 자동 갱신 최대 주기는 PC 서버 설정을 따른다 (FR-RF-01, FR-ST)
  const settings = useQuery({ queryKey: keys.settings, queryFn: () => api.get<{ maxRefreshSec: number }>('/api/settings'), staleTime: 60_000 });
  const refresh = useAdaptiveRefresh(settings.data?.data.maxRefreshSec ?? 300);
  const qc = useQueryClient();
  const bump = useUi(s => s.bumpRefresh);
  const h = header.data?.data;
  const state = status.data?.data.state ?? 'unknown';
  const conn = state === 'connected' ? 'ok' : state === 'unknown' ? 'plain' : 'danger';
  const name = h ? (h.character.nickname ?? h.character.realm) : '…';
  const w = weather(h?.location.weather);

  const manualRefresh = () => {
    void qc.invalidateQueries({ type: 'active' });
    bump();
  };

  return (
    <header className="top">
      <div className="who">
        <span className={`dot ${conn}`} title={state === 'connected' ? '게임 연결됨' : state === 'cli_missing' ? '게임 CLI를 찾을 수 없음' : '게임과 연결 안 됨'} />
        <div className="who-txt">
          <div className="who-name">
            {name} <span className="job">{h ? `${h.character.job} Lv.${h.character.level}` : ''}</span>
            {h?.character.title && <span className="who-title">“{h.character.title}”</span>}
          </div>
          <div className="who-where">
            <Icon name="pin" />
            {[h?.location.space ?? h?.location.channel, w].filter(Boolean).join(' · ')}
            {h && <> · <Icon name={/🌙/.test(h.location.erinn) ? 'moon' : 'sun'} />{h.location.erinn.replace(/^에린 시간 /, '에린 ').replace(/ [☀️🌙].*$/u, '')}</>}
          </div>
        </div>
      </div>
      {h && (
        <div className="top-kpi" title="이번 접속 누적 변화">
          <span className="lbl">전투력</span><span className="v num">{fmt(h.scores.combat)}</span><Delta value={h.scores.combatDelta} hideZero />
          <span className="lbl" style={{ marginLeft: 8 }}>마도저항</span><span className="v num">{fmt(h.scores.mdef)}</span><Delta value={h.scores.mdefDelta} hideZero />
        </div>
      )}
      <div className="top-actions">
        <button type="button" className="refresh" onClick={manualRefresh} title={`자동 갱신: 활동이 없으면 15초씩 늘어납니다 (지금 ${refresh.period}초 주기)`} aria-label={`지금 새로고침 (다음 자동 갱신 ${refresh.left}초 후)`}>
          <Icon name="refresh" /><span className="refresh-txt num">{refresh.left}초</span>
        </button>
        {!compact && (
          <button type="button" className="stop-btn" onClick={stopAction}>
            <Icon name="stop" />긴급 정지 <kbd>Esc</kbd>
          </button>
        )}
      </div>
    </header>
  );
}

function NavRail({ onChat }: { onChat: () => void }) {
  const cur = useRouter(s => s.loc.name);
  const go = useRouter(s => s.go);
  return (
    <nav className="rail" aria-label="화면">
      {ROUTES.filter(r => r.name !== 'settings').map(r => (
        <button key={r.name} type="button" onClick={() => go(r.name)} aria-current={cur === r.name ? 'page' : undefined} title={r.key ? `${r.label} (${r.key})` : r.label}>
          <Icon name={r.icon} />{r.label}
        </button>
      ))}
      <span className="sp" />
      <button type="button" className="chat-toggle" onClick={onChat}><Icon name="chat" />채팅</button>
      <button type="button" onClick={() => go('settings')} aria-current={cur === 'settings' ? 'page' : undefined}><Icon name="gear" />설정</button>
    </nav>
  );
}

function BottomTabs({ onChat }: { onChat: () => void }) {
  const cur = useRouter(s => s.loc.name);
  const go = useRouter(s => s.go);
  return (
    <nav className="tabs" aria-label="화면">
      {TAB_ROUTES.map(n => {
        const r = ROUTES.find(x => x.name === n)!;
        return (
          <button key={n} type="button" onClick={() => go(n)} aria-current={cur === n ? 'page' : undefined}>
            <Icon name={r.icon} />{r.label}
          </button>
        );
      })}
      <button type="button" onClick={onChat}><Icon name="chat" />채팅</button>
    </nav>
  );
}

/**
 * 단축키 (FR-AC-03): Esc = 긴급 정지(단, 대화상자·입력창이 먼저), 1~7 = 화면 전환, / = 채팅 입력.
 * 1~7과 /는 입력 요소에 포커스가 있으면 동작하지 않는다.
 */
function useShortcuts(sheetOpen: boolean) {
  const go = useRouter(s => s.go);
  const setDock = useUi(s => s.setDock);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
      const t = e.target as HTMLElement;
      const typing = t.closest('input, textarea, select, [contenteditable="true"]') != null;
      if (e.key === 'Escape') {
        if (document.querySelector('.dlg-wrap.open')) return;   // 대화상자가 먼저 닫힌다
        if (typing) { t.blur(); return; }
        if (sheetOpen) { setDock(false); return; }                 // 도크 시트가 먼저 닫힌다
        void stopAction();
        return;
      }
      if (typing) return;
      const r = ROUTES.find(x => x.key === e.key);
      if (r) { e.preventDefault(); go(r.name); return; }
      if (e.key === '/') {
        e.preventDefault();
        setDock(true, 'game');
        window.setTimeout(() => document.querySelector<HTMLInputElement>('#chatIn')?.focus(), 50);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [go, setDock, sheetOpen]);
}
