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
  expect(status.data.allowLanStart).toBeUndefined();   // 폰·태블릿 시작 설정은 없다(음성 입력은 PC 전용)
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

// ───────── 설정 화면 > 음성 입력 (FR-VC-14) ─────────
// 주의: 이 E2E 서버는 실제 Windows 구현(전역 단축키·마이크·모델 내려받기)을 쓴다. 그래서 저장·시험·설치 버튼은 누르지 않고
// 화면 구성과 키 캡처의 안전장치만 확인한다 (실제 단축키가 등록되거나 마이크가 켜지거나 160MB를 받으면 안 된다).

async function openVoiceSettings(page: Page) {
  await page.goto('/settings');
  const card = page.locator('.card').filter({ has: page.getByRole('heading', { name: '음성 입력' }) });
  await expect(card).toBeVisible();
  return card;
}

test('설정: 음성 입력 구역에 모델 설치·마이크·단축키·전송 옵션이 보이고 위험한 옵션은 기본 꺼짐이다', async ({ page }) => {
  const card = await openVoiceSettings(page);
  await expect(card.getByText('음성 인식 모델')).toBeVisible();
  await expect(card.getByText('설치 필요')).toBeVisible();
  await expect(card.getByRole('button', { name: /모델 설치/ })).toBeVisible();   // 누르지 않는다
  await expect(card.getByRole('combobox', { name: '마이크 선택' })).toBeVisible();
  await expect(card.getByRole('button', { name: '마이크 테스트' })).toBeVisible();   // 누르지 않는다
  await expect(card.getByRole('button', { name: '단축키 지정' })).toBeVisible();

  // 자동 전송은 기본 꺼짐 (켜져 있으면 안 된다). 폰·태블릿 시작 설정은 없다(PC 전용)
  const autoSend = card.locator('.kv', { hasText: '말이 끝나면 바로 전송' });
  await expect(autoSend.getByRole('button', { name: '끄기' })).toHaveAttribute('aria-pressed', 'true');
  await expect(card.getByText('폰·태블릿에서 시작')).toHaveCount(0);
  const hotwords = card.locator('.kv', { hasText: '우선 인식' });
  await expect(hotwords.getByRole('button', { name: '끄기' })).toHaveAttribute('aria-pressed', 'true');
  // 누적 학습은 항상 켜져 있고(끄는 항목 없음) 문장을 저장하지 않는다고 알린다
  await expect(card.getByText(/문장 전체는 저장하지 않고/)).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});

test('설정: 단축키 지정은 위험한 조합을 저장하지 않고, Esc·바깥 클릭으로 끝낸다', async ({ page, request }) => {
  const card = await openVoiceSettings(page);
  const start = card.getByRole('button', { name: '단축키 지정' });

  // 글자 하나만 / Shift만으로는 안내만 하고 캡처를 계속한다 (그 키가 모든 프로그램에서 막히므로)
  await start.click();
  await expect(card.getByText('키를 누르세요…')).toBeVisible();
  await page.keyboard.press('v');
  await expect(card.getByRole('status').filter({ hasText: 'Ctrl·Alt 중 하나' })).toBeVisible();
  await page.keyboard.press('Shift+V');
  await expect(card.getByRole('status').filter({ hasText: 'Ctrl·Alt 중 하나' })).toBeVisible();
  await expect(card.getByText('키를 누르세요…')).toBeVisible();

  // Esc로 끝낸다
  await page.keyboard.press('Escape');
  await expect(card.getByText('키를 누르세요…')).toBeHidden();
  await expect(card.locator('kbd')).toHaveText('없음');

  // 바깥 클릭으로도 끝난다 (끝나지 않으면 나중에 누른 키가 단축키로 저장된다)
  await start.click();
  await expect(card.getByText('키를 누르세요…')).toBeVisible();
  await page.locator('h2').first().click();
  await expect(card.getByText('키를 누르세요…')).toBeHidden();

  // 아무것도 저장되지 않았고 실제로 등록된 단축키도 없다
  const status = await (await request.get('/api/stt/status')).json();
  expect(status.data.hotkey.key).toBeNull();
  expect(status.data.hotkey.registered).toBe(false);
});

test('설정: 단축키 캡처 중 Tab은 막지 않고 캡처를 끝낸다 (키보드로 나갈 수 있다)', async ({ page }) => {
  const card = await openVoiceSettings(page);
  await card.getByRole('button', { name: '단축키 지정' }).click();
  await expect(card.getByText('키를 누르세요…')).toBeVisible();
  await page.keyboard.press('Tab');
  await expect(card.getByText('키를 누르세요…')).toBeHidden();
});
