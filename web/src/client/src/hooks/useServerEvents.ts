import { useEffect } from 'react';
import { connectSse } from '../api/sse';
import { keys, keys5 } from '../api/queries';
import { queryClient } from '../lib/queryClient';
import { useUi, type ToastLevel } from '../state/ui';
import { api } from '../api/http';
import type { GatherJob, Header, SttFinalEvent, SttLast, SttStateEvent } from '../api/types';
import { sttErrorMessage } from '../lib/voice';

/**
 * 음성 입력 SSE 이벤트 → 상태 저장소 (FR-VC). 훅에서 떼어 내 직접 시험한다.
 * - stt.state: 상태 표시. 오류는 세션이 끝날 때(idle) 한 번만 알린다(중간 상태마다 알리면 같은 오류가 겹쳐 뜬다)
 * - stt.partial: 인식 중인 글자
 * - stt.final: 확정 글자. 서버가 이미 게임 채팅으로 보냈다면(autoSent) 초안에 붙이지 않는다(붙이면 다시 보내게 된다)
 */
export function applyVoiceEvent(type: string, data: unknown): void {
  const ui = useUi.getState();
  switch (type) {
    case 'stt.state': {
      const e = data as SttStateEvent;
      ui.setVoice({
        state: e.state, sessionId: e.sessionId, byDevice: e.byDevice ?? null, device: e.device ?? null,
        partial: e.state === 'idle' ? '' : ui.voice.partial,
        warn: e.warn ?? (e.state === 'idle' ? null : ui.voice.warn),
      });
      if (e.state === 'idle' && e.error) ui.toast(sttErrorMessage(e.error, e.message), 'warn');
      break;
    }
    case 'stt.partial':
      ui.setVoice({ partial: (data as { text?: string })?.text ?? '' });
      break;
    case 'stt.final': {
      const e = data as SttFinalEvent;
      ui.setVoice({ partial: '' });
      if (!e.autoSent) ui.receiveVoiceFinal(e);
      break;
    }
  }
}

/** 끊겼다 돌아온 뒤 서버가 보관한 마지막 확정 결과를 반영한다. 이미 붙였거나 자동 전송한 문장은 건너뛴다 (FR-VC-12) */
export function recoverVoiceFinal(last: SttLast | null | undefined): void {
  if (last && !last.autoSent) useUi.getState().receiveVoiceFinal(last);
}

/** 캐릭터 선택창(selecting)은 마지막 캐릭터를 흐리게 보여 줄 뿐이라 전환으로 치지 않는다 */
export const characterSwitched = (prev: Header | undefined, next: Header) => !!prev && !next.selecting && prev.characterKey !== next.characterKey;

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
      const reconnected = s === 'open' && useUi.getState().everOpen;   // 처음 열린 것이 아니라 끊겼다 돌아온 것
      setSse(s, retry);
      if (s === 'open') {
        void queryClient.invalidateQueries({ queryKey: keys5.stt });
        // 끊긴 사이 확정된 음성 문장(서버가 60초 보관)을 받아 초안에 붙인다. 이미 붙인 문장은 건너뛴다 (FR-VC-12)
        if (reconnected) {
          void api.get<SttLast | null>('/api/stt/last')
            .then(r => recoverVoiceFinal(r.data))
            .catch(() => { /* 다음 이벤트가 채운다 */ });
        }
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
        case 'header': {
          const next = data as Header;
          const prev = queryClient.getQueryData<{ data: Header }>(keys.header)?.data;
          queryClient.setQueryData(keys.header, { data: next, fetchedAt: new Date().toISOString() });
          // 캐릭터가 바뀌었다: 상단 바만이 아니라 열려 있는 화면(스탯·가방·재화·숙제·생활·전체)도 다음 자동 갱신(최대 30초)을 기다리지 않고 바로 새로 읽는다
          if (characterSwitched(prev, next)) {
            void queryClient.invalidateQueries({ predicate: q => q.queryKey[0] !== keys.header[0] });
          }
          break;
        }
        case 'status':
          void queryClient.invalidateQueries({ queryKey: keys.status });
          // 게임 연결이 돌아왔다(캐릭터를 바꾸면 선택창을 거치며 끊긴다): 다음 자동 갱신을 기다리지 않고 지금 화면을 바로 새로 읽는다
          if ((data as { state?: string })?.state === 'connected') void queryClient.invalidateQueries({ type: 'active' });
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
        case 'stt.install':   // 모델 내려받기 진행: 상태를 다시 읽어 진행 막대를 갱신한다 (서버가 0.25초마다만 보낸다)
          void queryClient.invalidateQueries({ queryKey: keys5.stt });
          break;
        case 'stt.state':
        case 'stt.partial':
        case 'stt.final':
          applyVoiceEvent(type, data);
          if (type === 'stt.state') void queryClient.invalidateQueries({ queryKey: keys5.stt });
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
