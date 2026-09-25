import { defineConfig, devices } from '@playwright/test';

/**
 * E2E (TST-05·05a·06). 가짜 CLI로 로컬 서버를 띄우고(e2e/global-setup.ts) 설치된 Edge로 돌린다(브라우저를 따로 받지 않음).
 * 서버 상태(숙제 체크·채팅 로그)를 공유하므로 한 번에 하나씩 실행한다.
 * 실행: npm run e2e  (화면 빌드 → 서버 빌드(화면을 dll에 넣음) → 테스트 순서로 돈다. 이미 빌드했으면 npm run e2e:only)
 * Safari 엔진(WebKit)은 Playwright 브라우저를 따로 받아야 한다: npx playwright install webkit 후 E2E_WEBKIT=1
 */
const port = Number(process.env.E2E_PORT ?? 17890);

// 요구사양 TST-05의 뷰포트: PC, 탭 S10 울트라 가로(잠정), 아이패드 에어 4 가로·세로, 트라이폴드 접힘(잠정), 아이폰 12
const viewports: [string, number, number, boolean][] = [
  ['pc-1440', 1440, 900, false],
  ['tab-s10-1480', 1480, 924, true],
  ['ipad-1180', 1180, 820, true],
  ['ipad-820', 820, 1180, true],
  ['trifold-384', 384, 850, true],
  ['trifold-open-1080', 1080, 790, true],
  ['iphone-390', 390, 844, true],
  ['iphone-land-844', 844, 390, true],
];

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 30_000,
  reporter: [['list']],
  globalSetup: './e2e/global-setup.ts',
  globalTeardown: './e2e/global-teardown.ts',
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    channel: 'msedge',
    storageState: './e2e/.auth/state.json',
    locale: 'ko-KR',
    trace: 'retain-on-failure',
  },
  projects: [
    ...viewports.map(([name, width, height, touch]) => ({
      name,
      use: { ...devices['Desktop Edge'], channel: 'msedge', viewport: { width, height }, hasTouch: touch, isMobile: false },
    })),
    // iOS Safari와 같은 엔진(WebKit)으로 아이폰·아이패드 크기를 돌린다 (TST-05)
    ...(process.env.E2E_WEBKIT ? [
      { name: 'webkit-iphone-390', use: { ...devices['iPhone 12'], channel: undefined } },
      { name: 'webkit-ipad-820', use: { ...devices['iPad (gen 7)'], viewport: { width: 820, height: 1180 }, channel: undefined } },
    ] : []),
  ],
});
