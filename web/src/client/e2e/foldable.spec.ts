import { expect, test } from '@playwright/test';

/**
 * TST-05a 폴더블 전환: 같은 페이지에서 폭을 바꿔도(접힘 344px ↔ 펼침 720px) 상태가 이어진다 (FR-MB-03).
 * 뷰포트를 직접 바꾸므로 한 프로젝트에서만 돈다.
 */
test('접었다 펴도 입력 초안·도크·화면이 유지된다', async ({ page }, info) => {
  test.skip(info.project.name !== 'pc-1440', '뷰포트를 직접 바꾸는 테스트');
  await page.setViewportSize({ width: 344, height: 882 });
  await page.goto('/homework');
  await page.getByRole('button', { name: '채팅', exact: true }).filter({ visible: true }).first().click();
  await page.locator('#chatIn').fill('펼쳐도 남아 있어야 하는 초안');

  await page.setViewportSize({ width: 720, height: 800 });
  await expect(page).toHaveURL(/\/homework/);
  await expect(page.locator('#chatIn')).toHaveValue('펼쳐도 남아 있어야 하는 초안');

  await page.setViewportSize({ width: 1180, height: 820 });   // 옆 패널 도크로 이어진다
  await expect(page.locator('#chatIn')).toHaveValue('펼쳐도 남아 있어야 하는 초안');
  await expect(page.locator('.app')).toHaveAttribute('data-dock-mode', 'side');
});

test('좁은 커버 화면(344px)은 글랜스 모드로 핵심 4개만 보인다 (FR-MB-06)', async ({ page }, info) => {
  test.skip(info.project.name !== 'pc-1440', '뷰포트를 직접 바꾸는 테스트');
  await page.setViewportSize({ width: 344, height: 882 });
  await page.goto('/');
  await expect(page.locator('.glance')).toBeVisible();
  await expect(page.getByText('89K')).toBeVisible();   // 88,737 → 축약
  await page.getByRole('button', { name: '전체 개요 보기' }).click();
  await expect(page.locator('.glance')).toHaveCount(0);
});
