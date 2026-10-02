import { api, ApiError } from '../api/http';
import { keys } from '../api/queries';
import { queryClient } from './queryClient';
import { isOffline, useUi } from '../state/ui';
import { runExclusive } from './busy';
import type { Envelope, HomeworkBoard, Inventory, Life } from '../api/types';

const toast = (m: string, l?: 'ok' | 'warn' | 'danger' | 'info') => useUi.getState().toast(m, l);

/** 끊긴 동안에는 조작을 보내지 않는다. 보냈는지 헷갈리지 않게 알린다 (FR-MB-12). */
function blockedOffline(): boolean {
  if (!isOffline(useUi.getState())) return false;
  toast('PC와 연결이 끊겨 보내지 않았습니다. 다시 연결되면 눌러 주세요.', 'warn');
  return true;
}
const msg = (e: unknown) => (e instanceof ApiError ? e.message : '요청이 실패했습니다.');

/** 긴급 정지 (FR-AC-01): 확인 없이 바로 보낸다. 결과 토스트는 서버가 SSE로도 보낸다. */
export async function stopAction() {
  // 안전 기능이라 끊김 표시 중에도 막지 않고 보낸다. SSE만 끊기고 HTTP는 살아 있을 수 있다 (FR-AC-01, FR-MB-12 예외).
  const offline = isOffline(useUi.getState());
  try {
    await api.post('/api/actions/stop');
    toast('행동 정지를 요청했습니다', 'danger');
  } catch (e) {
    toast(offline ? '행동 정지를 보내지 못했습니다 · PC와 연결이 끊겨 있습니다. 게임에서 직접 멈춰 주세요.' : `행동 정지 실패: ${msg(e)}`, 'warn');
  }
}

/** 가공물 수거 (FR-DT-05). 이름이 없으면 완료된 첫 작업. 수거 중에는 다시 보내지 않는다(busy 'collect'). */
export async function collect(name?: string): Promise<boolean> {
  return (await runExclusive('collect', () => collectOne(name))) ?? false;
}

async function collectOne(name?: string): Promise<boolean> {
  if (blockedOffline()) return false;
  try {
    const r = await api.post<{ collected: string }>('/api/actions/collect', name ? { displayName: name } : {});
    toast(`${r.data.collected} 수거했습니다`, 'ok');
    return true;
  } catch (e) {
    toast(e instanceof ApiError && e.code === 'NOTHING_TO_COLLECT' ? '완료된 가공물이 없습니다' : `수거 실패: ${msg(e)}`, 'warn');
    return false;
  } finally {
    void queryClient.invalidateQueries({ queryKey: keys.life });
    void queryClient.invalidateQueries({ queryKey: keys.overview });
    void queryClient.invalidateQueries({ queryKey: keys.homework });
  }
}

/** 완료된 가공물을 모두 수거한다 (퀵 액션 📥). 하나씩 차례로 보낸다. */
export async function collectAll(names: string[]): Promise<boolean> {
  if (!names.length) { toast('완료된 가공물이 없습니다', 'info'); return false; }
  return (await runExclusive('collect', async () => {
    let any = false;
    for (const n of names) any = (await collectOne(n)) || any;
    return any;
  })) ?? false;
}

/** 채집 시작 (FR-DT-07·08): 202로 바로 돌아오고 결과는 SSE "gather"로 온다. */
export async function startGather(name: string, count: number | null): Promise<boolean> {
  return (await runExclusive('gather', () => startGatherOnce(name, count))) ?? false;
}

async function startGatherOnce(name: string, count: number | null): Promise<boolean> {
  if (blockedOffline()) return false;
  try {
    await api.post('/api/actions/gather', { displayName: name, count: count ?? undefined });
    toast(`${name} 채집을 시작했습니다${count ? ` · 목표 ${count}개` : ''}`, 'info');
    return true;
  } catch (e) {
    toast(`채집을 시작하지 못했습니다: ${msg(e)}`, 'warn');
    return false;
  }
}

/**
 * 즐겨찾기 켜기·끄기 (FR-DT-15·21): 가방 아이템 / 채집물. 이름 기준이라 같은 이름은 모두 바뀐다.
 * 화면은 먼저 바꾸고(낙관적 갱신) 서버 저장이 실패하면 되돌린다.
 */
export async function setFavorite(kind: 'items' | 'gather', name: string, favorite: boolean): Promise<void> {
  if (blockedOffline()) return;
  const apply = (value: boolean) => {
    if (kind === 'items') {
      queryClient.setQueryData<Envelope<Inventory>>(keys.inventory, old => old && { ...old, data: { ...old.data, items: old.data.items.map(i => (i.name === name ? { ...i, favorite: value } : i)) } });
    } else {
      queryClient.setQueryData<Envelope<Life>>(keys.life, old => old && { ...old, data: { ...old.data, gatherables: old.data.gatherables?.map(g => (g.name === name ? { ...g, favorite: value } : g)) ?? null } });
    }
  };
  apply(favorite);
  try {
    await api.put(`/api/favorites/${kind}`, { name, favorite });
  } catch (e) {
    apply(!favorite);
    toast(`즐겨찾기를 저장하지 못했습니다: ${msg(e)}`, 'warn');
  }
}

/** 숙제 수동 설정 (FR-HW-08): 뒤집기가 아니라 목표 상태를 보낸다. */
let homeworkInFlight = 0;

export async function setHomework(id: string, body: { completed?: boolean; count?: number }) {
  if (blockedOffline()) return;
  // 낙관적 갱신: 연달아 눌러도 다음 목표 상태를 최신 값에서 계산하게 한다.
  // 진행 중인 재조회는 취소한다(늦게 온 옛 값이 낙관적 값을 덮지 않게). 신선도 시각은 서버 확인 전이므로 그대로 둔다.
  await queryClient.cancelQueries({ queryKey: keys.homework });
  const updatedAt = queryClient.getQueryState(keys.homework)?.dataUpdatedAt;
  homeworkInFlight++;
  queryClient.setQueryData<Envelope<HomeworkBoard>>(keys.homework, old => old && {
    ...old,
    data: {
      ...old.data,
      cards: old.data.cards.map(c => {
        if (c.id !== id) return c;
        const count = body.count ?? (body.completed ? c.goal : body.completed === false ? 0 : c.count);
        const done = body.count !== undefined ? count >= c.goal : !!body.completed;
        return { ...c, count, isDone: done, status: done ? 'manualDone' as const : 'pending' as const, suggestion: null };
      }),
    },
  }, { updatedAt });
  try {
    await api.put(`/api/homework/${encodeURIComponent(id)}`, body);
  } catch (e) {
    toast(`숙제를 바꾸지 못했습니다: ${msg(e)}`, 'warn');
  } finally {
    // 마지막 체크가 끝났을 때만 서버 값으로 맞춘다(중간 재조회가 연속 입력을 되돌리지 않게)
    if (--homeworkInFlight === 0) {
      void queryClient.invalidateQueries({ queryKey: keys.homework });
      void queryClient.invalidateQueries({ queryKey: keys.overview });
    }
  }
}

export async function resetHomework(scope: 'character' | 'characterAndAccount') {
  await runExclusive('hw-reset', () => resetHomeworkOnce(scope));
}

async function resetHomeworkOnce(scope: 'character' | 'characterAndAccount') {
  try {
    await api.post('/api/homework/reset', { scope });
    toast('이번 주기 체크를 모두 지웠습니다', 'ok');
  } catch (e) {
    toast(`초기화하지 못했습니다: ${msg(e)}`, 'warn');
  } finally {
    void queryClient.invalidateQueries({ queryKey: keys.homework });
    void queryClient.invalidateQueries({ queryKey: keys.overview });
  }
}
