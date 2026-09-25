import { expect, test, type Page } from '@playwright/test';

/** 도크(채팅·AI)를 연다: 옆 패널이 닫혀 있거나 시트면 레일·하단 탭의 "채팅"을 누른다 */
async function openDock(page: Page, tab: '게임 채팅' | 'AI 도우미') {
  // 닫힌 시트는 화면 밖으로 밀려나 있을 뿐이라 "보이는지"로는 판단할 수 없다. 앱의 도크 상태로 본다
  if ((await page.locator('.app').getAttribute('data-dock-open')) !== 'true') {
    await page.getByRole('button', { name: '채팅', exact: true }).filter({ visible: true }).first().click();
    await expect(page.locator('.app')).toHaveAttribute('data-dock-open', 'true');
  }
  await page.getByRole('tab', { name: tab }).click();
}

const toast = (page: Page, text: string | RegExp) => page.locator('.toasts').getByText(text).first();

test.beforeEach(async ({ page }) => {
  await page.goto('/');
  await expect(page.getByText(/88,737|89K/).filter({ visible: true }).first()).toBeVisible();   // 가짜 CLI 전투력 (좁은 화면은 축약)
});

test('개요: 4대 점수·가방·숙제·콘텐츠 추천이 보이고 카드로 이동한다 (FR-OV-01·02)', async ({ page }) => {
  await expect(page.getByText('마도저항').filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText(/83\.0/).filter({ visible: true }).first()).toBeVisible();
  if (!(await page.locator('.glance').count())) {
    await expect(page.getByText('콘텐츠 추천')).toBeVisible();
    await page.getByRole('link', { name: /가방 무게/ }).click();
    await expect(page).toHaveURL(/\/inventory/);
    await expect(page.getByRole('heading', { name: '가방·창고' })).toBeVisible();
  }
});

test('숙제: 카드를 누르면 수동 완료, 다시 누르면 되돌아간다 (FR-HW-08·12)', async ({ page }) => {
  await page.goto('/homework?tab=raid');
  const card = page.getByRole('button', { name: /카브락/ });
  await expect(card).toHaveAttribute('aria-pressed', 'false');
  await card.click();
  await expect(card).toHaveAttribute('aria-pressed', 'true');
  await expect(card.getByText('수동 완료')).toBeVisible();
  await card.click();
  await expect(card).toHaveAttribute('aria-pressed', 'false');
});

test('게임 채팅: 미리보기 후 전송하면 로그에 이모지·행동과 함께 남는다 (FR-GC-03·04·05)', async ({ page }, info) => {
  await openDock(page, '게임 채팅');
  const input = page.locator('#chatIn');
  const text = `안녕하세요 ${info.project.name}`;
  await input.fill(text);
  await expect(page.locator('.comp-meta')).toContainText('/손인사1');
  await input.press('Enter');
  await expect(page.locator('.panel.game .msg').filter({ hasText: text }).first()).toContainText('✓ 전송');
  await expect(input).toHaveValue('');
});

test('긴급 정지: Esc 한 번으로 바로 요청된다 (FR-AC-01·03)', async ({ page }) => {
  await page.locator('body').click({ position: { x: 5, y: 5 } }).catch(() => {});
  await page.keyboard.press('Escape');
  await expect(toast(page, '행동 정지를 요청했습니다')).toBeVisible();
});

test('AI 도우미: 채집 명령은 확인 카드를 거쳐야 시작된다 (FR-AI-05, D-07)', async ({ page }) => {
  await openDock(page, 'AI 도우미');
  const input = page.locator('#aiIn');
  await input.fill('사과 20개 채집해줘');
  await input.press('Enter');
  const card = page.locator('.intent').filter({ hasText: '사과 채집' }).last();
  await expect(card).toContainText('정령의 날개 5개');
  await card.getByRole('button', { name: '채집 시작' }).click();
  await expect(card).toContainText('채집을 시작했습니다');
  await expect(toast(page, /사과 채집/)).toBeVisible();
});

// 연결 끊김 배너·조작 차단(FR-MB-12)은 브라우저 오프라인 전환이 이미 열린 SSE를 끊지 않아 E2E로 재현되지 않는다.
// 같은 규칙을 src/state/offline.test.ts(단위 테스트)에서 검증한다.
