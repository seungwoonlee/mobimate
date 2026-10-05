import type { APIRequestContext } from '@playwright/test';

/** 서버 랭킹 시험용: 현재 캐릭터(아이라_격투가)에 별칭을 달고 순위를 직접 입력한 상태로 만든다 (전투력 523위 = 핑크 뱃지). */
export async function seedRanking(request: APIRequestContext) {
  await request.get('/api/header');   // 가짜 게임의 캐릭터를 기록에 올린다
  await request.put('/api/profile/nickname', { data: { nickname: '시험캐릭터' } });
  for (const [kind, rank, score] of [[1, 523, 88737], [4, 612, 150000], [3, 2400, 23011], [2, 8800, 19745]])
    await request.put('/api/rankings/manual', { data: { key: '아이라_격투가', kind, rank, score } });
}

/** 다른 시험(별칭 없음을 가정)에 영향이 없게 되돌린다 */
export async function clearRanking(request: APIRequestContext) {
  for (const kind of [1, 2, 3, 4]) await request.delete(`/api/rankings/manual?key=${encodeURIComponent('아이라_격투가')}&kind=${kind}`);
  await request.put('/api/profile/nickname', { data: { nickname: '' } });
}
