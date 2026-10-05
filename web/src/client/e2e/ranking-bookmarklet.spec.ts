import { expect, test } from '@playwright/test';

/**
 * 북마크릿(앱이 만들어 주는 javascript: 코드)을 실제 브라우저(Edge)에서 돌려 본다.
 * 넥슨에는 접속하지 않는다: 넥슨 랭킹 주소(https://mabinogimobile.nexon.com)를 가로채 가짜 페이지와 가짜 검색 응답을 주고,
 * 코드가 거기서 로컬 앱(http://127.0.0.1:포트)으로 보내는 요청의 CORS·토큰·저장까지 확인한다.
 */
/** 최신 Edge·Chrome은 공개 사이트가 내 PC의 로컬 주소로 요청하려면 "로컬 네트워크 접근" 권한을 묻는다. 사용자는 처음 한 번 허용을 누르고, 시험은 미리 준다. */
const allowLocalNetwork = (page: import('@playwright/test').Page) =>
  page.context().grantPermissions(['local-network-access' as never], { origin: 'https://mabinogimobile.nexon.com' });

const item = (rank: number, name: string, type: string, scores: string[]) =>
  `<li class="item rank01 on"><div><dl><dt>${rank}위</dt></dl></div><div><dl><dt>캐릭터명</dt><dd data-charactername="${name}">${name}</dd></dl></div>` +
  `<div><dl><dt>클래스</dt><dd class="x"> 격투가 </dd></dl></div>` +
  scores.map((v, i) => `<div><dl><dt>${type}</dt><dd class="type_${i + 1}"> ${v} </dd></dl></div>`).join('') + '</li>';

test('북마크릿: 넥슨 랭킹 페이지에서 누르면 순위를 가져와 앱에 저장한다', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.name !== 'pc-1440', '한 환경에서만 확인한다 (브라우저 동작은 화면 크기와 무관)');
  test.setTimeout(60_000);

  const setup = await (await request.get('/api/rankings/setup')).json();
  const code: string = setup.data.bookmarklet;
  expect(code.startsWith('javascript:')).toBe(true);

  const asked: string[] = [];
  await page.route('https://mabinogimobile.nexon.com/**', async route => {
    const req = route.request();
    if (new URL(req.url()).pathname === '/Ranking/List/rankdata') {
      const body = req.postData() ?? '';
      const field = (n: string) => new RegExp(`name="${n}"\\r?\\n\\r?\\n([^\\r\\n]*)`).exec(body)?.[1] ?? '';
      const t = Number(field('t')), name = field('search');
      asked.push(`${t}:${field('s')}:${name}`);
      const html = t === 4 ? `<ul class="list">${item(3, name, '종합 점수', ['150,000', '88,737', '23,011', '19,745'])}</ul>`
        : t === 1 ? `<ul class="list">${item(9, name, '전투력', ['88,737'])}</ul>`
        : t === 3 ? `<ul class="list">${item(40, name, '생활력', ['23,011'])}</ul>`
        : `<ul class="list">${item(150, name, '매력', ['19,745'])}</ul>`;
      await route.fulfill({ status: 200, contentType: 'text/html; charset=utf-8', body: html });
    } else {
      await route.fulfill({ status: 200, contentType: 'text/html; charset=utf-8', body: '<!doctype html><html><body><h1>가짜 랭킹 페이지</h1></body></html>' });
    }
  });

  await allowLocalNetwork(page);
  await page.goto('https://mabinogimobile.nexon.com/Ranking/List?t=4');
  await page.evaluate(code.slice('javascript:'.length));
  await expect(page.locator('#mm-rank-msg')).toContainText('완료: 4건 저장', { timeout: 30_000 });

  // 순서: 종합 → 전투력 → 생활력 → 매력, 서버 번호 2(아이라), 앱에 적어 둔 이름으로 검색한다
  expect(asked).toEqual(['4:2:시험캐릭터', '1:2:시험캐릭터', '3:2:시험캐릭터', '2:2:시험캐릭터']);

  const all = await (await request.get('/api/rankings')).json();
  const e = all.data.characters['아이라_격투가'].entries;
  expect(e.combat).toMatchObject({ rank: 9, tier: 'gold', source: 'bookmarklet' });
  expect(e.total.rank).toBe(3);
  expect(e.living.rank).toBe(40);
  expect(e.attract.rank).toBe(150);

  // 다른 시험에 영향이 없게 처음 값으로 되돌린다
  for (const [kind, rank, score] of [[1, 523, 88737], [4, 612, 150000], [3, 2400, 23011], [2, 8800, 19745]])
    await request.put('/api/rankings/manual', { data: { key: '아이라_격투가', kind, rank, score } });
});

test('북마크릿: 토큰이 틀리면 앱이 거절한다', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.name !== 'pc-1440', '한 환경에서만 확인한다');
  const setup = await (await request.get('/api/rankings/setup')).json();
  const wrong = (setup.data.bookmarklet as string).replace(/'[0-9a-f]{24}'/, "'000000000000000000000000'");
  await page.route('https://mabinogimobile.nexon.com/**', route => route.fulfill({ status: 200, contentType: 'text/html', body: '<!doctype html><html><body>가짜</body></html>' }));
  await allowLocalNetwork(page);
  await page.goto('https://mabinogimobile.nexon.com/Ranking/List?t=4');
  await page.evaluate(wrong.slice('javascript:'.length));
  await expect(page.locator('#mm-rank-msg')).toContainText('실패', { timeout: 15_000 });
  await expect(page.locator('#mm-rank-msg')).toContainText('토큰');
});

test('북마크릿: 넥슨 페이지가 아니면 안내만 하고 아무것도 보내지 않는다', async ({ page, request, baseURL }, testInfo) => {
  test.skip(testInfo.project.name !== 'pc-1440', '한 환경에서만 확인한다');
  const setup = await (await request.get('/api/rankings/setup')).json();
  const code = (setup.data.bookmarklet as string).slice('javascript:'.length);
  const sent: string[] = [];
  page.on('request', r => { if (r.url().includes('/api/rankings/')) sent.push(r.url()); });
  page.on('dialog', d => void d.accept());
  await page.goto(`${baseURL}/overview`);
  sent.length = 0;
  await page.evaluate(code);
  await page.waitForTimeout(500);
  expect(sent).toEqual([]);
});
