import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

/** TST-06 접근성 자동 점검: 주요 화면을 다크·라이트에서 검사해 serious 이상 위반 0건. PC와 폰 폭에서 돈다. */
const screens = ['/', '/stats', '/homework', '/inventory', '/life', '/nearby', '/settings'];

for (const scheme of ['dark', 'light'] as const) {
  for (const path of screens) {
    test(`axe ${scheme} ${path}`, async ({ page }, info) => {
      test.skip(!['pc-1440', 'iphone-390'].includes(info.project.name), 'PC·폰 폭에서만');
      await page.emulateMedia({ colorScheme: scheme, reducedMotion: 'reduce' });
      await page.goto(path);
      await page.locator('.main-inner > *').first().waitFor();
      await page.waitForTimeout(400);   // 불러오기 끝
      const r = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
      const bad = r.violations.filter(v => v.impact === 'serious' || v.impact === 'critical');
      expect(bad.map(v => `${v.id}: ${v.nodes.slice(0, 3).map(n => n.target.join(' ')).join(' | ')}`)).toEqual([]);
    });
  }
}
