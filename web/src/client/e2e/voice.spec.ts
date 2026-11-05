import { expect, test, type Page } from '@playwright/test';

/** 도크(채팅·AI)를 연다: 옆 패널이 닫혀 있거나 시트면 레일·하단 탭의 "채팅"을 누른다 */
async function openChat(page: Page) {
  await page.goto('/');
  const panel = page.getByRole('tabpanel', { name: '게임 채팅' });
  if (!(await panel.isVisible().catch(() => false))) {
    await page.getByRole('button', { name: '채팅', exact: true }).filter({ visible: true }).first().click();
  }
  await expect(page.locator('#chatIn')).toBeVisible();
}

// 요구사양 FR-VC-02·05·07: 채팅 입력창의 마이크 버튼. E2E 서버에는 음성 인식 모델이 없다(첫 사용 때 내려받는 파일).
test('음성 입력: 모델이 없으면 마이크 버튼은 보이되 비활성이고 이유를 알려 준다', async ({ page }) => {
  await openChat(page);
  const mic = page.getByRole('button', { name: '음성으로 입력' });
  await expect(mic).toBeVisible();
  await expect(mic).toBeDisabled();   // 상태를 받으면 모델 없음으로 막힌다
  await expect(mic).toHaveAttribute('title', /모델/);
  await expect(mic).toHaveAttribute('aria-pressed', 'false');
});

test('음성 입력: 입력창·마이크·전송이 한 줄에 겹치지 않고 터치로 누를 크기이며 가로 스크롤이 없다', async ({ page }) => {
  await openChat(page);
  const input = await page.locator('#chatIn').boundingBox();
  const mic = await page.getByRole('button', { name: '음성으로 입력' }).boundingBox();
  const send = await page.getByRole('button', { name: '게임으로 전송' }).boundingBox();
  expect(input && mic && send).toBeTruthy();

  // 왼쪽부터 입력창 → 마이크 → 전송, 서로 겹치지 않는다
  expect(input!.x + input!.width).toBeLessThanOrEqual(mic!.x + 0.5);
  expect(mic!.x + mic!.width).toBeLessThanOrEqual(send!.x + 0.5);
  // 같은 줄(세로 가운데가 비슷)
  expect(Math.abs((mic!.y + mic!.height / 2) - (send!.y + send!.height / 2))).toBeLessThan(2);
  // 터치 대상 크기 (WCAG 2.2: 24px 이상, 이 앱은 44px 기준)
  expect(mic!.width).toBeGreaterThanOrEqual(40);
  expect(mic!.height).toBeGreaterThanOrEqual(40);
  // 입력창이 아주 좁아지지 않는다 (폭이 좁은 트라이폴드 접힘에서도 글을 쓸 수 있어야 한다)
  expect(input!.width).toBeGreaterThanOrEqual(120);

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});

test('음성 입력: 눌러도 서버의 마이크가 켜지지 않고(비활성) 상태 API는 모델 없음을 알린다', async ({ page, request }) => {
  await openChat(page);
  await page.getByRole('button', { name: '음성으로 입력' }).click({ force: true, trial: false }).catch(() => { /* 비활성이라 눌리지 않는다 */ });
  const status = await (await request.get('/api/stt/status')).json();
  expect(status.data.state).toBe('idle');
  expect(status.data.modelInstalled).toBe(false);
  expect(status.data.allowLanStart).toBe(false);   // 폰·태블릿 시작은 기본 꺼짐
  expect(status.data.autoSend).toBe(false);        // 자동 전송도 기본 꺼짐
});

test('음성 입력: 마이크를 켜는 요청은 사용자 지정 헤더 없이는 거절된다 (다른 웹페이지 차단)', async ({ request }) => {
  const noHeader = await request.post('/api/stt/start', { data: {} });
  expect(noHeader.status()).toBe(403);
  const wrongType = await request.post('/api/stt/start', { headers: { 'X-MobiMate-Req': '1', 'Content-Type': 'text/plain' }, data: '{}' });
  expect(wrongType.status()).toBe(415);
  // 헤더와 JSON이 맞아도 모델이 없으면 마이크는 열리지 않는다
  const ok = await request.post('/api/stt/start', { headers: { 'X-MobiMate-Req': '1' }, data: {} });
  expect(ok.status()).toBe(409);
  expect((await ok.json()).error.code).toBe('STT_MODEL_MISSING');
});
