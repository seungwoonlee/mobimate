import { useEffect } from 'react';
import { connectSse } from '../api/sse';
import { keys, keys5 } from '../api/queries';
import { queryClient } from '../lib/queryClient';
import { useUi, type ToastLevel } from '../state/ui';
import { api } from '../api/http';
import type { GatherJob, Header } from '../api/types';

/**
 * SSE 이벤트 → 쿼리 무효화·토스트 (상세설계 §4.3).
 * - header: 다른 기기가 새로 받은 헤더를 그대로 캐시에 넣는다(추가 조회 없음)
 * - status·gather·homework.changed·state.changed: 해당 키를 무효화
 * - toast: 서버가 보낸 알림(채집 완료, 기기 연결 등)
 */
export function useServerEvents() {
  const setSse = useUi(s => s.setSse);
  const toast = useUi(s => s.toast);
  const setGatherJob = useUi(s => s.setGatherJob);

  useEffect(() => connectSse({
    onState: (s, retry) => {
      setSse(s, retry);
      if (s === 'open') {
        void queryClient.invalidateQueries({ type: 'active' });   // 다시 붙으면 현재 화면을 바로 갱신 (FR-MB-12)
        // 자리를 비운 사이 시작·진행된 채집을 이 화면도 보여 준다 (끝난 작업은 다시 띄우지 않는다)
        void api.get<{ job: GatherJob | null }>('/api/actions/gather/current')
          .then(r => {
            // 끊긴 동안 끝난 작업의 완료 이벤트를 놓쳤을 수 있다: 서버가 진행 중이 아니라고 하면 남아 있던 "채집 중"을 서버 기준으로 바로잡는다.
            const cur = useUi.getState().gatherJob;
            if (r.data.job?.state === 'running') setGatherJob(r.data.job);
            else if (cur?.state === 'running') setGatherJob(r.data.job ?? { ...cur, state: 'stopped', message: '연결이 끊긴 사이 채집이 끝났습니다. 결과는 가방에서 확인해 주세요.' });
          })
          .catch(() => { /* 조회 실패는 다음 SSE 이벤트가 채운다 */ });
      }
    },
    onEvent: (type, data) => {
      switch (type) {
        case 'header':
          queryClient.setQueryData(keys.header, { data: data as Header, fetchedAt: new Date().toISOString() });
          break;
        case 'status':
          void queryClient.invalidateQueries({ queryKey: keys.status });
          break;
        case 'toast': {
          const t = data as { level?: string; message?: string };
          if (t?.message) toast(t.message, (t.level as ToastLevel) ?? 'info');
          break;
        }
        case 'gather':
          setGatherJob(data as GatherJob);
          void queryClient.invalidateQueries({ queryKey: keys.life });
          void queryClient.invalidateQueries({ queryKey: keys.inventory });
          break;
        case 'chat.logged':
          void queryClient.invalidateQueries({ queryKey: keys5.chatLog });
          break;
        case 'homework.changed':
          void queryClient.invalidateQueries({ queryKey: keys.homework });
          void queryClient.invalidateQueries({ queryKey: keys.overview });
          break;
        case 'state.changed': {
          const k = (data as { keys?: string[] })?.keys ?? [];
          if (k.includes('lan') || k.includes('settings')) void queryClient.invalidateQueries({ queryKey: keys.meta });
          if (k.includes('settings')) void queryClient.invalidateQueries({ queryKey: keys.settings });
          if (k.includes('profile')) { void queryClient.invalidateQueries({ queryKey: keys.header }); void queryClient.invalidateQueries({ queryKey: keys.characters }); }
          if (k.includes('accounts')) void queryClient.invalidateQueries({ queryKey: keys.characters });
          if (k.includes('rankings')) { void queryClient.invalidateQueries({ queryKey: keys.rankings }); void queryClient.invalidateQueries({ queryKey: keys.header }); void queryClient.invalidateQueries({ queryKey: keys.characters }); }
          if (k.includes('favorites')) { void queryClient.invalidateQueries({ queryKey: keys.inventory }); void queryClient.invalidateQueries({ queryKey: keys.life }); }
          if (k.includes('personas')) void queryClient.invalidateQueries({ queryKey: keys5.personas });
          if (k.includes('engine')) void queryClient.invalidateQueries({ queryKey: keys5.engines });
          break;
        }
      }
    },
  }), [setSse, toast, setGatherJob]);
}
