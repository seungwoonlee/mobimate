import { expect, test } from '@playwright/test';

/**
 * 같은 브라우저에서 MobiMate 탭을 여러 개 열어 둬도 모두 정상 동작해야 한다.
 * 브라우저는 호스트당 동시 연결을 6개로 제한한다(HTTP/1.1). 탭마다 서버 이벤트(SSE) 연결을 하나씩 잡으면
 * 일곱 번째 탭부터 요청이 대기열에 갇혀 "무한 로딩"이 된다 → 탭이 아무리 많아도 SSE는 한 탭만 연결한다.
 */
test('탭을 8개 열어도 모두 불러온다 (SSE는 한 탭만 연결)', async ({ browser, baseURL }) => {
  test.setTimeout(90_000);
  const ctx = await browser.newContext();
  const pages = [];
  for (let i = 0; i < 8; i++) {
    const p = await ctx.newPage();
    await p.goto(`${baseURL}/overview`);
    pages.push(p);
  }
  for (const [i, p] of pages.entries()) {
    await expect(p.locator('.main-inner > *').first(), `탭 ${i + 1}`).toBeVisible({ timeout: 15_000 });
    // 새 요청도 막히지 않는다
    const ms = await p.evaluate(async () => { const t = performance.now(); await fetch('/api/ping'); return performance.now() - t; });
    expect(ms, `탭 ${i + 1} 요청 지연`).toBeLessThan(3000);
  }
  await ctx.close();
});

test('대표 탭을 닫아도 남은 탭이 서버 이벤트를 이어서 받는다', async ({ browser, baseURL }) => {
  test.setTimeout(60_000);
  const ctx = await browser.newContext();
  const first = await ctx.newPage();
  await first.goto(`${baseURL}/overview`);
  await expect(first.locator('.main-inner > *').first()).toBeVisible();
  const second = await ctx.newPage();
  await second.goto(`${baseURL}/overview`);
  await expect(second.locator('.main-inner > *').first()).toBeVisible();

  // 먼저 연 탭이 대표다. 닫으면 두 번째 탭이 락을 넘겨받아 서버에 직접 연결한다.
  await first.close();
  await second.waitForTimeout(2500);

  // 서버가 SSE로 보내는 알림(toast)이 두 번째 탭에 도착한다
  await second.evaluate(() => fetch('/api/actions/stop', { method: 'POST' }));
  await expect(second.getByText('행동 정지를 요청했습니다').first()).toBeVisible({ timeout: 10_000 });
  await ctx.close();
});

test('대표가 아닌 탭도 서버 이벤트를 받는다', async ({ browser, baseURL }) => {
  test.setTimeout(60_000);
  const ctx = await browser.newContext();
  const leader = await ctx.newPage();
  await leader.goto(`${baseURL}/overview`);
  await expect(leader.locator('.main-inner > *').first()).toBeVisible();
  const follower = await ctx.newPage();
  await follower.goto(`${baseURL}/overview`);
  await expect(follower.locator('.main-inner > *').first()).toBeVisible();
  await follower.waitForTimeout(1500);

  await follower.evaluate(() => fetch('/api/actions/stop', { method: 'POST' }));
  await expect(follower.getByText('행동 정지를 요청했습니다').first()).toBeVisible({ timeout: 10_000 });
  await expect(leader.getByText('행동 정지를 요청했습니다').first()).toBeVisible({ timeout: 10_000 });
  await ctx.close();
});
