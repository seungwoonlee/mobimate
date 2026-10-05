import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Delta, Icon, JobIcon, Toasts } from '../components/ui';
import { CommandPalette } from '../features/palette';
import { NicknameEditor } from '../features/nickname';
import { MembershipChip } from '../features/membership';
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
import { fmt } from '../lib/format';
import { scoreClass } from '../lib/score';
import { LocationScene } from '../components/Scenery';
import { Dock } from './Dock';
import { PairDialog } from '../features/pairing';
import { useSession } from '../api/queries';

/** 하단 탭(Compact)에 둘 화면 (전체·개요·가방·숙제·생활 + 채팅) */
const TAB_ROUTES: RouteName[] = ['characters', 'overview', 'inventory', 'homework', 'life'];

export function AppShell({ children }: { children: ReactNode }) {
  const layout = useLayout();
  const scale = useDevice(s => s.scale);
  // 선택자로 필요한 값만 구독한다(토스트마다 셸 전체를 다시 그리지 않게)
  const dockOpen = useUi(s => s.dockOpen);
  const dockPref = useUi(s => s.dockPref);
  const dockTab = useUi(s => s.dockTab);
  const setDock = useUi(s => s.setDock);
  const closeSheet = useUi(s => s.closeSheet);
  const offline = useOffline();
  const route = useRouter(s => s.loc.name);
  const main = useRef<HTMLElement>(null);

  // 상단 버튼(새로고침·빠른 실행·폰으로 보기·긴급 정지)은 좌측 바가 보이는 화면에서는 그 아래쪽에 아이콘으로 둔다 (v1.5).
  // 좌측 바가 없는 폰(Compact)·테이블톱 자세에서는 상단에 둔다.
  const settings = useQuery({ queryKey: keys.settings, queryFn: () => api.get<{ maxRefreshSec: number }>('/api/settings'), staleTime: 60_000 });
  const refresh = useAdaptiveRefresh(settings.data?.data.maxRefreshSec ?? 30);   // 자동 갱신 최대 주기는 PC 서버 설정을 따른다 (FR-RF-01)
  const qc = useQueryClient();
  const bump = useUi(s => s.bumpRefresh);
  const setPairOpen = useUi(s => s.setPairOpen);
  const setPaletteOpen = useUi(s => s.setPaletteOpen);
  const local = useSession().data?.kind === 'local';   // 폰으로 보기 버튼은 게임 PC에서만
  // 서버는 게임 조회를 3초 동안 재사용한다. 그 안에 다시 눌러도 같은 값이라 의미가 없으니 그동안 버튼을 막는다.
  const [refreshCooling, setRefreshCooling] = useState(false);
  const coolTimer = useRef<number>();
  useEffect(() => () => window.clearTimeout(coolTimer.current), []);
  const manualRefresh = useCallback(() => {
    if (refreshCooling) return;
    setRefreshCooling(true);
    coolTimer.current = window.setTimeout(() => setRefreshCooling(false), REFRESH_COOLDOWN_MS);
    void qc.invalidateQueries({ type: 'active' });
    bump();
  }, [qc, bump, refreshCooling]);
  const actions: Actions = {
    refresh, local, refreshCooling,
    manualRefresh,
    openPair: () => setPairOpen(true),
    openPalette: () => setPaletteOpen(true),
  };
  const railShown = layout.size !== 'compact' && layout.posture !== 'tabletop';

  // 도크: Expanded 이상은 옆 패널(기본 열림), 그 아래·높이가 낮으면 시트 (상세설계 §4.5)
  const dockMode = layout.posture === 'book' ? 'split' : (layout.size === 'expanded' || layout.size === 'large') && !layout.short ? 'side' : 'sheet';
  // 채팅 기본값 (v1.5): 가로 화면이면(폰 제외) 옆 패널이 기본으로 열려 있다. 세로 화면은 기본으로 닫혀 있다. 사용자가 고르면 그 선택을 기억한다.
  // 폰(Compact)은 채팅을 한 화면 가득 시트로 연다.
  const defaultOpen = dockMode === 'side' && layout.landscape && layout.size !== 'compact';
  const dockVisible = dockMode === 'split' || (dockMode === 'side' ? (dockPref ?? defaultOpen) : dockOpen);
  const toggleChat = () => setDock(!dockVisible);

  useShortcuts(dockMode === 'sheet' && dockOpen);

  // 화면을 바꾸면 맨 위로, 시트 도크는 닫는다
  useEffect(() => {
    main.current?.scrollTo({ top: 0 });
    if (dockMode === 'sheet') closeSheet();
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
      <TopBar compact={layout.size === 'compact'} actions={actions} showActions={!railShown} />
      {offline && <OfflineBanner />}
      <NavRail chatOpen={dockVisible} canToggle={dockMode !== 'split'} onChat={toggleChat} actions={actions} showActions={railShown} />
      <main className="main" ref={main} tabIndex={0} aria-label="본문">
        <div className="main-inner">{children}</div>
      </main>
      <div className="hinge" aria-hidden="true" />
      {dockMode === 'sheet' && dockOpen && <div className="scrim" onClick={() => setDock(false)} />}
      <Dock mode={dockMode} onClose={() => setDock(false)} />
      <BottomTabs chatOpen={dockVisible} onChat={toggleChat} />
      <button type="button" className="fab-stop" onClick={stopAction} aria-label="긴급 정지">
        <Icon name="stop" />
      </button>
      <Toasts />
      <PairHost />
      <CommandPalette />
    </div>
  );
}

/** 📱 폰으로 보기 대화상자. 트레이의 "폰으로 보기"는 #/pair를 붙여 연다. */
function PairHost() {
  const open = useUi(s => s.pairOpen);
  const setOpen = useUi(s => s.setPairOpen);
  useEffect(() => {
    if (window.location.hash === '#/pair') {
      window.history.replaceState(null, '', window.location.pathname + window.location.search);
      setOpen(true);
    }
  }, [setOpen]);
  return open ? <PairDialog onClose={() => setOpen(false)} /> : null;
}

/** 끊김 배너 (FR-MB-12): 다음 재시도까지 남은 초를 센다. */
export function OfflineBanner() {
  const retryAt = useUi(s => s.retryAt);
  const now = useNow();
  const left = retryAt ? Math.max(0, Math.ceil((retryAt - now) / 1000)) : 0;
  return (
    <div className="offline-banner" role="status">
      <Icon name="alert" size={16} />PC와 연결 끊김 · {left > 0 ? `${left}초 후 다시 연결` : '다시 연결 중'}
    </div>
  );
}

/** 새로고침·빠른 실행·폰으로 보기에 필요한 값과 동작 (상단 또는 좌측 바의 버튼이 함께 쓴다) */
interface Actions {
  refresh: { period: number; left: number };
  refreshCooling: boolean;
  local: boolean;
  manualRefresh: () => void;
  openPair: () => void;
  openPalette: () => void;
}

/** 서버 조회 캐시(QueryCacheTtl 3초)와 맞춘 수동 새로고침 대기 시간 */
const REFRESH_COOLDOWN_MS = 3000;
const TIP_REFRESH = (period: number) => `지금 새로고침 — 자동 갱신은 활동이 없으면 15초씩 늘어납니다 (지금 ${period}초 주기)`;
const TIP_PALETTE = '빠른 실행 (Ctrl+K) — 화면 이동·채집·페르소나를 검색해서 바로 실행합니다';
const TIP_PAIR = 'QR코드로 모바일 접속이 가능합니다';

function TopBar({ compact, actions, showActions }: { compact: boolean; actions: Actions; showActions: boolean }) {
  const header = useHeader();
  const status = useStatus();
  const h = header.data?.data;
  const state = status.data?.data.state ?? 'unknown';
  const conn = state === 'connected' ? 'ok' : state === 'unknown' ? 'plain' : 'danger';
  const name = h ? (h.character.nickname ?? h.character.realm) : '…';

  return (
    <header className="top" data-selecting={h?.selecting ? 'true' : undefined}>
      <div className="who">
        <span className={`dot ${conn}`} title={state === 'connected' ? '게임 연결됨' : state === 'cli_missing' ? '게임 CLI를 찾을 수 없음' : '게임과 연결 안 됨'} />
        {h && <span className="who-fig"><JobIcon job={h.character.job} size={compact ? 18 : 30} /></span>}
        <div className="who-txt">
          <div className="who-name">
            <NicknameEditor name={name} current={h?.character.nickname ?? null} />
            {h && <span className="job">{h.character.job} Lv.{h.character.level}</span>}
            {h?.selecting ? <span className="who-title selecting">캐릭터 선택 중</span> : h?.character.title && <span className="who-title">“{h.character.title}”</span>}
            {h && !h.selecting && <MembershipChip />}
          </div>
          {h && (h.location.space || h.inProgress) && (
            <div className="who-now">
              {h.location.space && <span className="now-map" title="현재 맵"><Icon name="pin" size={13} />{h.location.space}</span>}
              {h.inProgress && <span className="now-progress" title={`${h.inProgress.kind} 진행 중: ${h.inProgress.title}`}>{h.inProgress.kind} 진행 중{h.inProgress.title !== h.inProgress.kind && ` · ${h.inProgress.title}`}</span>}
            </div>
          )}
        </div>
      </div>
      {h && (
        <div className="hero-scores" title="이번 접속 누적 변화">
          <div className="hs"><span className="lbl">전투력</span><span className={`v num ${scoreClass('combat', h.scores.combat)}`}>{fmt(h.scores.combat)}</span><Delta value={h.scores.combatDelta} hideZero /></div>
          <div className="hs"><span className="lbl">마도저항</span><span className={`v num ${scoreClass('mdef', h.scores.mdef)}`}>{fmt(h.scores.mdef)}</span><Delta value={h.scores.mdefDelta} hideZero /></div>
        </div>
      )}
      {h && !compact && <LocationScene place={h.location.space ?? h.location.channel} weather={h.location.weather} erinn={h.location.erinn} />}
      {showActions && (
        <div className="top-actions">
          <button type="button" className="refresh" disabled={actions.refreshCooling} onClick={actions.manualRefresh} data-tip={TIP_REFRESH(actions.refresh.period)} aria-label={`지금 새로고침 (다음 자동 갱신 ${actions.refresh.left}초 후)`}>
            <Icon name="refresh" /><span className="refresh-txt num">{actions.refresh.left}초</span>
          </button>
          <button type="button" className="icon-btn" onClick={actions.openPalette} aria-label="빠른 실행" data-tip={TIP_PALETTE}>
            <Icon name="search" />
          </button>
          {actions.local && (
            <button type="button" className="icon-btn" onClick={actions.openPair} aria-label="폰·태블릿으로 보기" data-tip={TIP_PAIR}>
              <Icon name="phone" />
            </button>
          )}
          {!compact && (
            <button type="button" className="stop-btn" onClick={stopAction}>
              <Icon name="stop" />긴급 정지 <kbd>Esc</kbd>
            </button>
          )}
        </div>
      )}
    </header>
  );
}

function NavRail({ onChat, chatOpen, canToggle, actions, showActions }: { onChat: () => void; chatOpen: boolean; canToggle: boolean; actions: Actions; showActions: boolean }) {
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
      {showActions && (
        <>
          <button type="button" className="rail-act" disabled={actions.refreshCooling} onClick={actions.manualRefresh} data-tip={TIP_REFRESH(actions.refresh.period)} aria-label={`지금 새로고침 (다음 자동 갱신 ${actions.refresh.left}초 후)`}>
            <Icon name="refresh" /><span className="num">{actions.refresh.left}초</span>
          </button>
          <button type="button" className="rail-act" onClick={actions.openPalette} aria-label="빠른 실행" data-tip={TIP_PALETTE}>
            <Icon name="search" />실행
          </button>
          {actions.local && (
            <button type="button" className="rail-act" onClick={actions.openPair} aria-label="폰·태블릿으로 보기" data-tip={TIP_PAIR}>
              <Icon name="phone" />폰 연결
            </button>
          )}
        </>
      )}
      {canToggle && <button type="button" className="chat-toggle" onClick={onChat} aria-pressed={chatOpen} title={chatOpen ? '채팅 닫기' : '채팅 열기'}><Icon name="chat" />채팅</button>}
      <button type="button" onClick={() => go('settings')} aria-current={cur === 'settings' ? 'page' : undefined}><Icon name="gear" />설정</button>
      {showActions && (
        <button type="button" className="rail-stop" onClick={stopAction} aria-label="긴급 정지" data-tip="긴급 정지 (Esc) — 채집·이동·자동사냥을 멈춥니다">
          <Icon name="stop" />정지
        </button>
      )}
    </nav>
  );
}

function BottomTabs({ onChat, chatOpen }: { onChat: () => void; chatOpen: boolean }) {
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
      <button type="button" onClick={onChat} aria-pressed={chatOpen}><Icon name="chat" />채팅</button>
    </nav>
  );
}

/**
 * 단축키 (FR-AC-03): Esc = 긴급 정지(단, 대화상자·입력창이 먼저), 1~8 = 화면 전환, / = 채팅 입력.
 * 1~8과 /는 입력 요소에 포커스가 있으면 동작하지 않는다.
 */
function useShortcuts(sheetOpen: boolean) {
  const go = useRouter(s => s.go);
  const setDock = useUi(s => s.setDock);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'k') {   // 빠른 실행 (FR-AC-04)
        e.preventDefault();
        const ui = useUi.getState();
        ui.setPaletteOpen(!ui.paletteOpen);
        return;
      }
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
