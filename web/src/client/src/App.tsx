import { useEffect } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { onApiError } from './api/http';
import { IconSprite, Icon } from './components/ui';
import { useServerEvents } from './hooks/useServerEvents';
import { AppShell } from './layout/AppShell';
import { queryClient } from './lib/queryClient';
import { useDevice } from './state/device';
import { useRouter } from './state/router';
import { useUi } from './state/ui';
import { OverviewView } from './features/overview';
import { StatsView } from './features/stats';
import { CurrenciesView, InventoryView, NearbyView } from './features/inventory';
import { HomeworkView } from './features/homework';
import { LifeView } from './features/life';
import { SettingsView } from './features/settings';

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <IconSprite />
      <Root />
    </QueryClientProvider>
  );
}

function Root() {
  const unauthorized = useUi(s => s.unauthorized);
  const setUnauthorized = useUi(s => s.setUnauthorized);
  const theme = useDevice(s => s.theme);

  // 테마: 시스템을 따르거나 이 기기에서 고정 (FR-MB-16)
  useEffect(() => {
    const el = document.documentElement;
    if (theme === 'system') el.removeAttribute('data-theme'); else el.setAttribute('data-theme', theme);
  }, [theme]);

  // 401 → 인증 안내 화면 (SEC-06·07)
  useEffect(() => onApiError(e => { if (e.unauthorized) setUnauthorized(true); }), [setUnauthorized]);

  if (unauthorized) return <AuthNeeded />;
  return <Connected />;
}

function Connected() {
  useServerEvents();
  const route = useRouter(s => s.loc.name);
  return (
    <AppShell>
      {route === 'overview' && <OverviewView />}
      {route === 'stats' && <StatsView />}
      {route === 'inventory' && <InventoryView />}
      {route === 'currencies' && <CurrenciesView />}
      {route === 'homework' && <HomeworkView />}
      {route === 'life' && <LifeView />}
      {route === 'nearby' && <NearbyView />}
      {route === 'settings' && <SettingsView />}
    </AppShell>
  );
}

/** 세션이 없거나 폐기된 기기 (상세설계 §5 오류 표: 로컬 = 트레이에서 브라우저 열기 / LAN = 다시 페어링). */
function AuthNeeded() {
  const local = ['localhost', '127.0.0.1', '[::1]'].includes(window.location.hostname);
  return (
    <main className="auth">
      <div className="card auth-card">
        <Icon name="lock" size={28} />
        <h1>연결 인증이 필요합니다</h1>
        {local ? (
          <p>작업 표시줄 트레이의 <b>MobiMate → 브라우저 열기</b>를 누르면 이 PC가 다시 연결됩니다.</p>
        ) : (
          <p>이 기기의 연결이 만료되었거나 해제되었습니다. PC 화면의 <b>📱 폰으로 보기</b>에서 QR을 다시 찍어 주세요.</p>
        )}
        <button type="button" className="btn primary" onClick={() => window.location.reload()}>다시 확인</button>
      </div>
    </main>
  );
}
