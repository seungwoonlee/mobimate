import { useEffect, useRef } from 'react';
import { useHeader, useHomework, useLife } from '../api/queries';
import type { Life } from '../api/types';
import { bagJustFull, newlyCompletedWorks, vibrate, weeklyResetDue } from '../lib/notices';
import { useUi } from '../state/ui';

/**
 * 화면이 켜져 있는 동안의 알림 (FR-MB-14, FR-HW-15): 가공 완료, 가방 가득(100%), 주간 리셋 1시간 전.
 * 토스트로 알리고 가능한 기기는 진동을 더한다. 조회는 적응형 갱신(활성 쿼리 갱신)이 돌려 준다.
 * 같은 알림이 반복되지 않게 이전 값과 비교하고, 주간 리셋 알림은 리셋 주기마다 한 번만 보낸다.
 */
export function useLiveNotices() {
  const toast = useUi(s => s.toast);
  const life = useLife();
  const header = useHeader();
  const homework = useHomework();

  const prevWorks = useRef<Life['works']>();
  const prevPct = useRef<number>();

  // 가공 완료
  useEffect(() => {
    const next = life.data?.data.works;
    if (!next) return;
    const done = newlyCompletedWorks(prevWorks.current, next);
    prevWorks.current = next;
    if (done.length) {
      toast(`가공 완료: ${done.join(', ')}`, 'ok');
      vibrate();
    }
  }, [life.data, toast]);

  // 가방 가득
  const pct = header.data?.data.weight?.pct;
  useEffect(() => {
    if (bagJustFull(prevPct.current, pct)) {
      toast('가방이 가득 찼습니다 (100%) · 다이어트가 필요합니다', 'warn');
      vibrate([200, 100, 200]);
    }
    if (pct !== undefined) prevPct.current = pct;
  }, [pct, toast]);

  // 주간 리셋 1시간 전
  const board = homework.data?.data;
  useEffect(() => {
    if (!board) return;
    const left = board.weekly.total - board.weekly.done;
    const key = `mm.weeklyRemind.${board.nextWeeklyResetUtc}`;
    if (!weeklyResetDue(board.nextWeeklyResetUtc, left)) return;
    try {
      if (localStorage.getItem(key)) return;
      localStorage.setItem(key, '1');
    } catch { /* 저장 불가: 이번 세션 안에서만 중복을 막지 못한다 */ }
    toast(`주간 리셋 1시간 전 · 주간 숙제 ${left}개가 남았습니다`, 'warn');
    vibrate();
  }, [board, toast]);
}
