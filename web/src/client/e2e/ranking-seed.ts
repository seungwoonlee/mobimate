import type { APIRequestContext } from '@playwright/test';

/** 넥슨 랭킹 목록의 한 줄(북마크릿이 보내는 것과 같은 모양). 종합(4)은 합계·전투력·생활력·매력 순서의 점수 네 개를 쓴다. */
function item(rank: number, name: string, cls: string, type: string, ...scores: string[]) {
  return `<li class="item rank01 on"> <div> <dl> <dt>${rank}위</dt> </dl> </div> <div> <dl> <dt>서버명</dt> <dd>아이라</dd> </dl> </div> `
    + `<div> <dl> <dt>캐릭터명</dt> <dd data-charactername="${name}">${name}</dd> </dl> </div> <div> <dl> <dt>클래스</dt> <dd class="warrior_4"> ${cls} </dd> </dl> </div> `
    + scores.map((v, i) => `<div> <dl> <dt>${type}</dt> <dd class="type_${i + 1}"> ${v} </dd> </dl> </div> `).join('') + '</li>';
}

/** 서버 랭킹 시험용: 현재 캐릭터(아이라_격투가)에 별칭을 달고 북마크릿과 같은 경로로 순위를 넣는다 (전투력 523위 = 핑크 뱃지). */
export async function seedRanking(request: APIRequestContext) {
  await request.get('/api/header');   // 가짜 게임의 캐릭터를 기록에 올린다
  await request.put('/api/profile/nickname', { data: { nickname: '시험캐릭터' } });
  const setup = await (await request.get('/api/rankings/setup')).json();
  const token = String(setup.data.bookmarklet).split("'").find(s => s.length === 24 && /^[0-9a-f]+$/i.test(s));
  const n = '시험캐릭터', cls = '격투가', key = '아이라_격투가';
  const items = [
    { key, name: n, kind: 1, html: item(523, n, cls, '전투력', '88,737') },
    { key, name: n, kind: 4, html: item(612, n, cls, '종합 점수', '150,000', '88,737', '23,011', '19,745') },
    { key, name: n, kind: 3, html: item(2400, n, cls, '생활력', '23,011') },
    { key, name: n, kind: 2, html: item(8800, n, cls, '매력', '19,745') },
  ];
  await request.post('/api/rankings/import', { headers: { 'Content-Type': 'text/plain' }, data: JSON.stringify({ token, items }) });
}

/** 다른 시험(별칭 없음을 가정)에 영향이 없게 되돌린다: 별칭을 비우면 그 이름으로 받은 순위는 쓰이지 않는다 */
export async function clearRanking(request: APIRequestContext) {
  await request.put('/api/profile/nickname', { data: { nickname: '' } });
}
