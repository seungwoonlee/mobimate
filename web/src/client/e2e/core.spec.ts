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
  await page.goto('/overview');
  await expect(page.getByText(/88,737|89K/).filter({ visible: true }).first()).toBeVisible();   // 가짜 CLI 전투력 (좁은 화면은 축약)
});

test('개요: 4대 점수·가방·숙제·콘텐츠 추천이 보이고 카드로 이동한다 (FR-OV-01·02)', async ({ page }) => {
  await expect(page.getByText('마도저항').filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText(/83\.0/).filter({ visible: true }).first()).toBeVisible();
  if (!(await page.locator('.glance').count())) {
    await expect(page.locator('.main').getByText('콘텐츠 추천')).toBeVisible();
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

// ── v1.5 화면 (요구사양 §11) ──

test('첫 화면은 내 캐릭터 전체 현황이다 (FR-AL-01·02)', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: /내 캐릭터/ })).toBeVisible();
  const card = page.locator('.card.char').first();
  await expect(card).toContainText('아이라');
  await expect(card).toContainText('접속 중');
  await expect(card).toContainText('88,737');
});

test('주변 레이더: 클래스 - 레벨 - 전투력 - 칭호 순서, 요약 줄은 없다 (FR-DT-32~34)', async ({ page }) => {
  await page.goto('/nearby');
  await expect(page.getByText('대검전사').filter({ visible: true }).first()).toBeVisible();
  const row = page.locator('.pl-row').first();
  await expect(row).toContainText(/대검전사\s*-\s*Lv\.100\s*-\s*92,450(\s*\(나보다 강함\))?\s*-\s*어둠을 가르는/);   // 스크린 리더용 숨김 문구를 포함해서 본다
  await expect(page.locator('.pill', { hasText: '강함' })).toHaveCount(0);   // 눈에 보이는 배지는 없다
  await expect(page.getByText(/기준 ·/)).toHaveCount(0);
});

test('생활: 채집 목표 수량 기본값은 100개 (FR-DT-22)', async ({ page }) => {
  await page.goto('/life');
  await expect(page.locator('.stepper .v')).toHaveText('100');
  await expect(page.getByRole('heading', { name: /금속 가공|가죽 가공|옷감 가공/ }).first()).toBeVisible();   // 종류별 묶음 (FR-DT-20)
});

test('가방 즐겨찾기: 별을 누르면 맨 위로 오고 즐겨찾기 보기에 모인다 (FR-DT-15)', async ({ page }) => {
  await page.goto('/inventory');
  const rows = page.locator('.list .row');
  await expect(rows.first()).toBeVisible();
  const last = rows.last();
  const name = (await last.locator('.t span').last().textContent())!.trim();
  const star = last.getByRole('button', { name: `${name} 즐겨찾기` });
  await star.click();
  await expect(rows.first()).toContainText(name);                      // 즐겨찾기가 맨 위
  await page.getByRole('button', { name: '★ 즐겨찾기' }).click();
  await expect(rows).toHaveCount(1);
  await rows.first().getByRole('button', { name: `${name} 즐겨찾기` }).click();   // 되돌려 두기 (서버 상태를 공유한다)
  await expect(page.getByText('즐겨찾기한 아이템이 없습니다')).toBeVisible();
});

test('AI 도우미 바로 가기: 한 줄에 하나, 가로 스크롤 없이 화면으로 바로 이동한다 (FR-AI-20·21)', async ({ page }) => {
  await openDock(page, 'AI 도우미');
  const nav = page.locator('.guide-nav').first();
  await expect(nav).toBeVisible();
  const box = await nav.evaluate(el => ({ sw: el.scrollWidth, cw: el.clientWidth }));
  expect(box.sw).toBeLessThanOrEqual(box.cw);                           // 가로 스크롤 없음
  const tops = await nav.locator('button').evaluateAll(bs => bs.map(b => Math.round(b.getBoundingClientRect().left)));
  expect(new Set(tops).size).toBe(1);                                   // 모두 같은 열 = 한 줄에 하나
  await nav.getByRole('button', { name: /^재화/ }).click();
  await expect(page).toHaveURL(/\/currencies/);
});

test('다크 모드 색상 패턴을 고를 수 있다 (FR-LY-05)', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('/settings');
  await page.getByRole('button', { name: '보라' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-palette', 'violet');
  await page.getByRole('button', { name: '바다' }).click();
  await expect(page.locator('html')).not.toHaveAttribute('data-palette', /.+/);
});

test('별칭: 이름 옆 연필로 그 자리에서 고치고 지울 수 있다 (P1)', async ({ page }) => {
  await page.goto('/overview');
  await page.getByRole('button', { name: '캐릭터 별칭 바꾸기' }).click();
  const input = page.getByLabel('캐릭터 별칭');
  await input.fill('모험가 승운');
  await input.press('Enter');
  await expect(page.locator('.who-name .nm')).toHaveText('모험가 승운');
  await page.getByRole('button', { name: '캐릭터 별칭 바꾸기' }).click();   // 되돌려 두기 (서버 상태를 공유한다)
  await page.getByLabel('캐릭터 별칭').fill('');
  await page.getByLabel('캐릭터 별칭').press('Enter');
  await expect(page.locator('.who-name .nm')).toHaveText('아이라');
});

test('빠른 실행: Ctrl+K로 열고 검색해서 화면으로 이동한다 (FR-AC-04)', async ({ page }) => {
  await page.keyboard.press('Control+k');
  const dlg = page.getByRole('dialog', { name: '빠른 실행' });
  await expect(dlg).toBeVisible();
  await page.getByLabel('빠른 실행 검색').fill('재화');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/currencies/);
  await expect(dlg).toHaveCount(0);
});

test('빠른 실행: 채집 재료를 고르면 채집 확인 창이 열린다', async ({ page }) => {
  await page.keyboard.press('Control+k');
  await page.getByLabel('빠른 실행 검색').fill('사과');
  await expect(page.getByRole('option', { name: /채집: 사과/ })).toBeVisible();   // 채집 목록을 받은 뒤에 고른다
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/life/);
  await expect(page.getByRole('dialog', { name: '사과 채집' })).toBeVisible();
  await page.getByRole('button', { name: '취소' }).click();
});

test('직업별 아이콘이 레이더 줄에 붙는다', async ({ page }) => {
  await page.goto('/nearby');
  await expect(page.locator('.pl-row').first().locator('.pl-job svg')).toHaveCount(1);
});
