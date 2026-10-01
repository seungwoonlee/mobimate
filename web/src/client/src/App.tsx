import { useEffect } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { IconSprite } from './components/ui';
import { useServerEvents } from './hooks/useServerEvents';
import { AppShell } from './layout/AppShell';
import { queryClient } from './lib/queryClient';
import { useDevice } from './state/device';
import { useRouter } from './state/router';
import { OverviewView } from './features/overview';
import { CharactersView } from './features/characters';
import { StatsView } from './features/stats';
import { CurrenciesView, InventoryView, NearbyView } from './features/inventory';
import { HomeworkView } from './features/homework';
import { LifeView } from './features/life';
import { SettingsView } from './features/settings';
import { useNameHop } from './features/pairing';

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <IconSprite />
      <Root />
    </QueryClientProvider>
  );
}

function Root() {
  const theme = useDevice(s => s.theme);
  const palette = useDevice(s => s.palette);

  // 테마: 시스템을 따르거나 이 기기에서 고정 (FR-MB-16)
  useEffect(() => {
    const el = document.documentElement;
    if (theme === 'system') el.removeAttribute('data-theme'); else el.setAttribute('data-theme', theme);
  }, [theme]);

  // 다크 모드 색상 패턴 (FR-LY-05)
  useEffect(() => {
    const el = document.documentElement;
    if (palette === 'navy') el.removeAttribute('data-palette'); else el.setAttribute('data-palette', palette);
  }, [palette]);

  // QR(IP 주소)로 열렸고 이름 주소(mobimate.local)가 같은 PC로 풀리면 이름 주소로 옮겨 간다 (FR-MB-13)
  useNameHop();

  return <Connected />;
}

function Connected() {
  useServerEvents();
  const route = useRouter(s => s.loc.name);
  return (
    <AppShell>
      {route === 'characters' && <CharactersView />}
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
