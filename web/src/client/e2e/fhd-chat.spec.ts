import { test, expect } from '@playwright/test';

const routes = ['/', '/homework', '/overview', '/stats', '/inventory', '/mission', '/currencies', '/life', '/nearby', '/settings'];
for (const dockPx of [460, 624, 760]) {
  test(`FHD 채팅 폭 ${dockPx}에서 모든 화면이 본문 안에 표시된다`, async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.addInitScript(value => localStorage.setItem('mobimate.device.v1', JSON.stringify({dockPx: value})), dockPx);
    for (const path of routes) {
      await page.goto(path);
      if (await page.locator('.app').getAttribute('data-dock-open') !== 'true')
        await page.getByRole('button', {name:'채팅', exact:true}).filter({visible:true}).first().click();
      await expect(page.locator('.app')).toHaveAttribute('data-dock-mode', 'side');
      await expect(page.getByText(/88,737|89K/).filter({visible:true}).first()).toBeVisible();
      const main = page.locator('.main');
      await expect.poll(() => main.evaluate(el => el.scrollWidth - el.clientWidth), {message: `${path}: 본문 가로 넘침`}).toBeLessThanOrEqual(2);
    }
  });
}
test('넓은 채팅 폭을 유지한 채 FHD 반쪽 창으로 줄여도 본문을 사용할 수 있다', async ({ page }) => {
  await page.setViewportSize({width: 1920, height: 1080});
  await page.addInitScript(() => localStorage.setItem('mobimate.device.v1', JSON.stringify({dockPx:760})));
  await page.goto('/');
  const toggle = page.getByRole('button', {name:'채팅', exact:true}).filter({visible:true}).first();
  await toggle.click(); await toggle.click(); // 사용자가 연 선택을 기억한다
  await page.setViewportSize({width:960, height:1040});
  await expect(page.locator('.app')).toHaveAttribute('data-dock-open', 'true');
  for (const path of routes) {
    await page.goto(path);
    if (await page.locator('.app').getAttribute('data-dock-open') !== 'true') await toggle.click();
    await expect(page.getByText(/88,737|89K/).filter({visible:true}).first()).toBeVisible();
    const bounds = await page.locator('.main').evaluate(el => ({width:el.clientWidth, overflow:el.scrollWidth - el.clientWidth}));
    expect.soft(bounds.width, path).toBeGreaterThanOrEqual(480);
    if (bounds.overflow > 2) console.log(path, await page.locator('.main').evaluate(el => [...el.querySelectorAll('*')].filter(e => e.getBoundingClientRect().right > el.getBoundingClientRect().right + 2).slice(0, 12).map(e => ({tag:e.tagName, cls:e.className, width:Math.round(e.getBoundingClientRect().width)}))));
    expect.soft(bounds.overflow, path).toBeLessThanOrEqual(2);
  }
});
