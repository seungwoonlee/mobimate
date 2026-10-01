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
  const card = page.locator('.card.char.cur');
  await expect(card).toContainText('아이라');
  await expect(card).toContainText('접속 중');
  await expect(card).toContainText('88,737');
});

test('주변 레이더: 친구 > 길드원 > 그 외(파티원 표시 없음), 맨 앞 표식, 클래스 - 레벨 - 전투력 - 칭호 (FR-DT-30~34)', async ({ page }) => {
  await page.goto('/nearby');
  await expect(page.getByText('마법사').filter({ visible: true }).first()).toBeVisible();
  const rows = page.locator('.pl-row');
  await expect(rows.first()).toContainText('(친구)');
  await expect(rows.first()).toContainText(/마법사\s*-\s*Lv\.72\s*-\s*32,100\s*-\s*던바튼 요리사/);
  const tags = await rows.locator('.pl-tag').allTextContents();
  expect(tags.filter(t => t).join(',')).toBe('(친구),(길드원),(길드원),(길드원)');   // 표식이 있는 줄의 순서
  expect(tags.join('')).not.toContain('파티원');          // 게임 정보로는 내 파티원을 알 수 없다 (파티 없이도 파티원으로 오던 오류)
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
  await page.getByRole('button', { name: '캐릭터명 바꾸기' }).click();
  const input = page.getByLabel('캐릭터명', { exact: true });
  await input.fill('모험가 승운');
  await input.press('Enter');
  await expect(page.locator('.who-name .nm')).toHaveText('모험가 승운');
  await page.getByRole('button', { name: '캐릭터명 바꾸기' }).click();   // 되돌려 두기 (서버 상태를 공유한다)
  await page.getByLabel('캐릭터명', { exact: true }).fill('');
  await page.getByLabel('캐릭터명', { exact: true }).press('Enter');
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

test('상단 에린 날짜에 0월이 나오지 않고 "N월 D일"로 보인다', async ({ page }) => {
  test.skip(page.viewportSize()!.width < 600, '폰 폭에서는 그림을 접는다');
  await expect(page.locator('.scene .date')).toHaveText(/에린 \d{1,2}월 \d{1,2}일 · (낮|밤)/);
  await expect(page.locator('.scene .date')).not.toContainText('0월');
});

test('호버하면 QR·빠른 실행 말풍선이 뜬다', async ({ page }, info) => {
  test.skip(info.project.name !== 'pc-1440', '마우스 호버는 PC에서만');
  const qr = page.getByRole('button', { name: '폰·태블릿으로 보기' });
  await expect(qr).toHaveAttribute('data-tip', /QR코드로 모바일 접속이 가능합니다/);
  await qr.hover();
  await page.waitForTimeout(450);
  const shown = await qr.evaluate(el => getComputedStyle(el, '::after').opacity);
  expect(Number(shown)).toBeGreaterThan(0.9);
  await expect(page.getByRole('button', { name: '빠른 실행' })).toHaveAttribute('data-tip', /Ctrl\+K/);
});

test('채팅 기본값: 가로 화면은 열려 있고 세로 화면·폰은 닫혀 있다, 채팅 아이콘으로 열고 닫는다', async ({ page }, info) => {
  await page.evaluate(() => localStorage.removeItem('mobimate.dock.v1'));
  await page.reload();
  const w = page.viewportSize()!;
  const phone = w.width < 600;
  const portrait = w.width <= w.height;
  const app = page.locator('.app');
  await expect(app).toHaveAttribute('data-dock-mode', /side|sheet|split/);
  const mode = await app.getAttribute('data-dock-mode');
  const wantOpen = mode === 'side' && !portrait && !phone;
  await expect(app).toHaveAttribute('data-dock-open', String(wantOpen));
  if (mode !== 'side') return;   // 시트 모드(폰·낮은 가로 화면)는 아래 토글 확인을 건너뛴다
  const toggle = page.getByRole('button', { name: '채팅', exact: true }).filter({ visible: true }).first();
  await expect(toggle).toBeVisible();                                   // 열려 있어도 아이콘이 보인다
  await toggle.click();
  await expect(app).toHaveAttribute('data-dock-open', String(!wantOpen));
  await toggle.click();
  await expect(app).toHaveAttribute('data-dock-open', String(wantOpen));
});

// ── 전체 현황: 계정 묶음·충전 재화·멤버십 (v1.5 요청 3·5) ──

test('전체 현황: 같은 계정끼리 묶고 계정 머리글에 데카·M캐시를 보인다', async ({ page }) => {
  await page.goto('/');
  const sections = page.locator('section.acct');
  await expect(sections.first()).toContainText('데카');
  await expect(sections.first()).toContainText('10,285');
  await expect(sections.first()).toContainText('5,619');
  // 지금 접속한 캐릭터의 계정(아이라·도적·마법사)이 맨 위, 계정 안에서는 전투력 순 (도적 99,000 > 마법사 80,000 > 아이라 88,737 → 도적, 아이라, 마법사)
  const names = await sections.first().locator('.cc-name').allTextContents();
  expect(names).toEqual(['바람 · 도적', '아이라 · 격투가', '바람 · 마법사']);
  const jobs = await sections.first().locator('.cc-job').allTextContents();
  expect(jobs.map(j => j.trim().split(' ')[0])).toEqual(['도적', '격투가', '마법사']);
  await expect(sections.nth(1)).toContainText('사제');   // 다른 계정
});

test('전체 현황: 가득 = 빨강 + 설명, 80% 이상 = 노랑, 동기화 안 됨 = 진회색', async ({ page }) => {
  await page.goto('/');
  const rogue = page.locator('.card.char', { hasText: '도적' });
  await expect(rogue).toHaveClass(/tone-red/);
  await expect(rogue).toContainText('충전이 멈췄어요');
  await expect(rogue).toContainText('지금 접속해서 사용하세요');
  await expect(rogue).toContainText('동기화되지 않은');     // 값이 어긋난 계정원이기도 하다 (빨강이 우선)
  const mage = page.locator('.card.char', { hasText: '마법사' });
  await expect(mage).toHaveClass(/tone-gray/);
  await expect(mage).toContainText('같은 계정의 다른 캐릭터와 달라요');
  const priest = page.locator('.card.char', { hasText: '사제' });
  await expect(priest).toHaveClass(/tone-yellow/);
  await expect(priest).toContainText(/은동전이 .* 뒤 가득 차요/);
  await expect(priest.locator('.coins')).toContainText(/은동전 8\d\/100/);   // 82 + 2시간분 = 약 84
});

test('전체 현황: 접속 시급 순으로 바꾸면 가득 찬 캐릭터가 먼저 온다', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: '접속 시급 순' }).click();
  await expect(page.getByRole('button', { name: '접속 시급 순' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('section.acct').first().locator('.card.char').first()).toHaveClass(/tone-red/);
  await page.getByRole('button', { name: '전투력순' }).click();   // 되돌려 둔다
});

test('멤버십: 상단에 남은 시간이 보이고, 3일 이내면 붉게, 등록·해제할 수 있다', async ({ page }) => {
  await page.goto('/overview');
  const chip = page.locator('.mem-chip');
  await expect(chip).toContainText('멤버십 미등록');
  await chip.click();
  const dlg = page.getByRole('dialog', { name: '멤버십 남은 일수 등록' });
  await expect(dlg).toContainText('캐시샵');                    // 입력할 곳을 안내한다
  await expect(dlg.getByLabel('남은 시간')).toHaveCount(0);   // 남은 일수만 입력한다
  await expect(dlg).toContainText('새벽 6시');
  await dlg.getByLabel('남은 일수').fill('27');
  await dlg.getByRole('button', { name: '저장' }).click();
  await expect(chip).toContainText(/멤버십 2[67]일 \d+시간/);       // (오늘 + 27일 − 1일) 다음 새벽 6시까지: 시각에 따라 26~27일
  await expect(chip).not.toHaveClass(/urgent/);
  await chip.click();
  await page.getByRole('dialog').getByLabel('남은 일수').fill('2');
  await page.getByRole('dialog').getByRole('button', { name: '저장' }).click();
  await expect(chip).toHaveClass(/urgent/);                     // 3일 이내 = 붉은 경고
  await chip.click();
  await page.getByRole('dialog').getByRole('button', { name: '등록 해제' }).click();
  await expect(chip).toContainText('멤버십 미등록');
});

test('계정 편집: 캐릭터를 따로 빼면 계정이 나뉜다', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: '계정 편집' }).click();
  const count = await page.locator('section.acct').count();
  expect(count).toBe(2);
  // 아이라(격투가) 소속을 직접 새 계정으로 뺐다가 되돌린다
  const me = page.getByLabel('아이라 소속 계정');
  const original = await me.inputValue();
  await me.selectOption('new');
  await page.getByRole('dialog').getByRole('button', { name: '닫기', exact: true }).click();
  await expect(page.locator('section.acct')).toHaveCount(3);
  await page.getByRole('button', { name: '계정 편집' }).click();
  await page.getByLabel('아이라 소속 계정').selectOption(original.startsWith('solo:') ? 'new' : original);
  await page.getByRole('dialog').getByRole('button', { name: '닫기', exact: true }).click();
  await expect(page.locator('section.acct')).toHaveCount(2);
});

test('직업 이미지: 내려받아 둔 직업은 전신 이미지로, 없는 직업은 SVG 아이콘으로 보인다 (v1.5)', async ({ page }) => {
  await page.goto('/nearby');
  await expect(page.locator('.pl-row').first()).toBeVisible();
  // 격투가는 이미지가 있다 (자르지 않은 전신)
  const rogue = page.locator('.pl-row', { hasText: '격투가' }).locator('.pl-job');
  await expect(rogue.locator('.jobimg')).toHaveCount(1);
  await expect(rogue.locator('.jobimg')).toHaveCSS('background-image', /\/api\/class-image\/thief_3/);
  await expect(rogue.locator('.jobimg')).toHaveCSS('background-size', 'contain');
  // 대검전사는 이미지를 아직 못 받아 SVG 아이콘이다
  const warrior = page.locator('.pl-row', { hasText: '대검전사' }).locator('.pl-job');
  await expect(warrior.locator('svg.ic')).toHaveCount(1);
  await expect(warrior.locator('.jobimg')).toHaveCount(0);
});

test('상단 버튼은 좌측 바 아이콘으로 옮겨 가고 긴급 정지가 가장 아래에 있다 (폰 제외)', async ({ page }) => {
  const w = page.viewportSize()!.width;
  test.skip(w < 600, '폰은 좌측 바가 없어 상단에 둔다');
  const rail = page.locator('nav.rail');
  await expect(rail.getByRole('button', { name: /지금 새로고침/ })).toBeVisible();
  await expect(rail.getByRole('button', { name: '빠른 실행' })).toBeVisible();
  await expect(rail.getByRole('button', { name: '폰·태블릿으로 보기' })).toBeVisible();
  await expect(page.locator('header.top .top-actions')).toHaveCount(0);     // 상단 우측에는 더 이상 없다
  const labels = await rail.locator('button').evaluateAll(bs => bs.map(b => b.getAttribute('aria-label') ?? b.textContent?.trim() ?? ''));
  expect(labels.at(-1)).toBe('긴급 정지');                                    // 가장 아래
  const ys = await rail.locator('button').evaluateAll(bs => bs.map(b => Math.round(b.getBoundingClientRect().bottom)));
  expect(ys.at(-1)).toBe(Math.max(...ys));
});

test('상단 멤버십은 칭호 다음에 오고, 글자는 기본 크기다', async ({ page }) => {
  test.skip(page.viewportSize()!.width < 840, '넓은 화면에서만 확인');
  const name = Number((await page.locator('.who-name .nm').evaluate(e => parseFloat(getComputedStyle(e).fontSize))));
  expect(name).toBeLessThan(50);                                              // 1.5배 확대를 되돌렸다
  const order = await page.locator('.who-name').evaluate(el => [...el.children].map(c => c.className.split(' ')[0]));
  if (order.includes('mem-chip')) expect(order.indexOf('who-title')).toBeLessThan(order.indexOf('mem-chip'));   // 칭호 → 멤버십
  expect(order.indexOf('job')).toBeLessThan(order.indexOf('who-title'));
});

test('이름 편집 입력칸은 "캐릭터명"이라고 부른다', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: '캐릭터명 바꾸기' }).click();
  await expect(page.getByPlaceholder('캐릭터명')).toBeVisible();
  await expect(page.locator('body')).not.toContainText('별칭');
});

test('전체 탭: 내 순서로 계정 순서를 직접 정한다', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('group', { name: '정렬' }).getByRole('button', { name: '내 순서' }).click();
  const heads = page.locator('section.acct .acct-h h3');
  const before = await heads.allTextContents();
  expect(before.length).toBeGreaterThanOrEqual(2);
  await page.locator('section.acct').nth(1).getByRole('button', { name: /위로/ }).click();
  await expect.poll(async () => (await heads.allTextContents())[0]).toBe(before[1]);
  await page.reload();                                                         // 서버에 저장되어 유지된다
  await page.getByRole('group', { name: '정렬' }).getByRole('button', { name: '내 순서' }).click();
  await expect.poll(async () => (await heads.allTextContents())[0]).toBe(before[1]);
  await page.locator('section.acct').nth(1).getByRole('button', { name: /위로/ }).click();   // 되돌려 두기
  await expect.poll(async () => (await heads.allTextContents())[0]).toBe(before[0]);
});

test('전체 탭: 카드의 x 아이콘으로 캐릭터를 삭제한다 (예/아니오, 기본 아니오, 접속 중은 x 없음)', async ({ page }, info) => {
  test.skip(info.project.name !== 'pc-1440', '한 번만 지울 수 있어 한 화면에서만 확인');
  await page.goto('/');
  const bard = page.locator('.card.char', { hasText: '음유시인' });
  await expect(bard).toHaveCount(1);
  await expect(page.locator('.card.char.cur .cc-del')).toHaveCount(0);          // 접속 중인 캐릭터는 x가 없다
  await bard.locator('.cc-del').click();
  const dlg = page.getByRole('dialog', { name: '캐릭터 삭제' });
  await expect(dlg.getByRole('button', { name: '아니오' })).toBeFocused();       // 기본값은 아니오
  await page.keyboard.press('Enter');
  await expect(dlg).toHaveCount(0);
  await expect(bard).toHaveCount(1);                                            // 아니오 = 그대로
  await bard.locator('.cc-del').click();
  await dlg.getByRole('button', { name: '예' }).click();
  await expect(page.locator('.card.char', { hasText: '음유시인' })).toHaveCount(0);
});

test('전체 탭: 서버가 여러 곳인 계정은 헤더에 서버를 함께 보여 준다', async ({ page }) => {
  await page.goto('/');
  const first = page.locator('section.acct').first();                    // 아이라(격투가) + 바람(도적·마법사)
  await expect(first.locator('.acct-meta')).toContainText(/서버 .*아이라.*바람|서버 .*바람.*아이라/);
  await expect(page.locator('section.acct').nth(1).locator('.acct-meta')).not.toContainText('서버');   // 한 서버만
});
