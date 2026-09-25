import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { AdaptiveRefresh } from '../lib/refresh';
import { useUi } from '../state/ui';
import { useRouter } from '../state/router';

/**
 * 적응형 자동 갱신 (FR-RF-01~04, 상세설계 §4.4).
 * - 이 기기에서 활동(누르기·키·터치·화면 전환)이 있으면 15초로 리셋, 없으면 틱마다 +15초(최대 maxSec).
 * - 탭이 숨겨지면 멈추고, 다시 보이면 바로 1회 갱신한 뒤 기본 주기로 돌아간다.
 * - 갱신 대상은 지금 화면에 걸린(active) 쿼리뿐이다: 상단 바 + 현재 화면 (FR-RF-03).
 * 돌려주는 값: 다음 갱신까지 남은 초, 현재 주기.
 */
export function useAdaptiveRefresh(maxSec: number) {
  const qc = useQueryClient();
  const ctl = useRef(new AdaptiveRefresh(maxSec));
  const [left, setLeft] = useState(ctl.current.current);
  const [period, setPeriod] = useState(ctl.current.current);
  const deadline = useRef(Date.now() + ctl.current.current * 1000);
  const route = useRouter(s => s.loc.name);
  const manual = useUi(s => s.refreshTick);

  useEffect(() => { ctl.current.setMax(maxSec); }, [maxSec]);

  useEffect(() => {
    const restart = () => {
      deadline.current = Date.now() + ctl.current.current * 1000;
      setPeriod(ctl.current.current);
      setLeft(ctl.current.current);
    };
    const activity = () => {
      if (ctl.current.current !== AdaptiveRefresh.Base) {
        ctl.current.recordActivity();
        restart();
      } else {
        ctl.current.recordActivity();
      }
    };
    const refresh = () => { void qc.invalidateQueries({ type: 'active' }); };

    const timer = window.setInterval(() => {
      if (document.hidden) return;
      const remainMs = deadline.current - Date.now();
      if (remainMs <= 0) {
        refresh();
        ctl.current.onTick();
        restart();
      } else {
        setLeft(Math.ceil(remainMs / 1000));
      }
    }, 1000);

    const onVisible = () => {
      if (!document.hidden) {
        refresh();
        ctl.current.reset();
        restart();
      }
    };

    const events = ['pointerdown', 'keydown', 'touchstart'] as const;
    events.forEach(e => window.addEventListener(e, activity, { passive: true }));
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      window.clearInterval(timer);
      events.forEach(e => window.removeEventListener(e, activity));
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [qc]);

  // 화면 전환·수동 새로고침: 주기를 리셋한다 (수동 새로고침은 호출한 쪽이 즉시 갱신한다)
  useEffect(() => {
    ctl.current.reset();
    deadline.current = Date.now() + ctl.current.current * 1000;
    setPeriod(ctl.current.current);
    setLeft(ctl.current.current);
  }, [route, manual]);

  return { left, period };
}
