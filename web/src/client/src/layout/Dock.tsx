import { useRef, useState } from 'react';
import { Icon } from '../components/ui';
import { useDevice } from '../state/device';
import { GameChatPanel } from '../features/dock/GameChat';
import { AiPanel } from '../features/dock/AiPanel';
import { useUi } from '../state/ui';
import { stopAction } from '../lib/actions';

/**
 * 채팅·AI 도크 (상세설계 §4.5). 옆 패널·시트·책 자세 분할을 모두 같은 DOM으로 보여 준다(FR-MB-03).
 * 두 패널은 늘 마운트해 두고 CSS로만 바꿔 보인다: 탭을 오가도 입력·스트리밍이 이어진다.
 */
export const DOCK_MIN = 300;
const DOCK_MAX = 760;
/** 채팅 폭 상한: 본문이 너무 좁아지지 않게 화면 폭에서 레일과 본문 최소 폭을 뺀다 */
export function clampDock(px: number, win = window.innerWidth) {
  return Math.round(Math.max(DOCK_MIN, Math.min(DOCK_MAX, win - 72 - 360, px)));
}

/** 채팅 폭 조절 손잡이: 끌거나 방향키(←→)로 바꾸고, 두 번 누르면 기본 폭으로 돌아간다 */
function ResizeHandle() {
  const dockPx = useDevice(s => s.dockPx);
  const set = useDevice(s => s.set);
  const [drag, setDrag] = useState(false);
  const start = useRef(0);
  const current = () => dockPx || (document.querySelector('.dock') as HTMLElement | null)?.getBoundingClientRect().width || 400;
  return (
    <div
      className={`dock-resize${drag ? ' drag' : ''}`}
      role="separator" aria-orientation="vertical" aria-label="채팅 폭 조절 (끌거나 ← → 키, 두 번 누르면 기본값)"
      aria-valuemin={DOCK_MIN} aria-valuemax={DOCK_MAX} aria-valuenow={Math.round(current())} tabIndex={0}
      onPointerDown={e => { e.currentTarget.setPointerCapture(e.pointerId); start.current = e.clientX; setDrag(true); }}
      onPointerMove={e => { if (drag) set({ dockPx: clampDock(window.innerWidth - e.clientX) }); }}
      onPointerUp={() => setDrag(false)}
      onPointerCancel={() => setDrag(false)}
      onDoubleClick={() => set({ dockPx: 0 })}
      onKeyDown={e => {
        if (e.key === 'ArrowLeft') { e.preventDefault(); set({ dockPx: clampDock(current() + 20) }); }
        else if (e.key === 'ArrowRight') { e.preventDefault(); set({ dockPx: clampDock(current() - 20) }); }
        else if (e.key === 'Home' || e.key === 'Escape') set({ dockPx: 0 });
      }}
    />
  );
}

export function Dock({ mode, onClose }: { mode: 'side' | 'sheet' | 'split'; onClose: () => void }) {
  const tab = useUi(s => s.dockTab);
  const setDock = useUi(s => s.setDock);
  return (
    <aside className="dock" aria-label="채팅과 AI">
      {mode !== 'split' && <ResizeHandle />}
      <div className="grab" />
      <div className="dock-h">
        <div className="seg" role="tablist" aria-label="도크 탭">
          <button type="button" role="tab" aria-selected={tab === 'game'} onClick={() => setDock(true, 'game')}><Icon name="chat" />게임 채팅</button>
          <button type="button" role="tab" aria-selected={tab === 'ai'} onClick={() => setDock(true, 'ai')}><Icon name="spark" />AI 도우미</button>
        </div>
        {mode === 'sheet' && <button type="button" className="stop-btn dock-stop" onClick={stopAction} aria-label="긴급 정지"><Icon name="stop" />정지</button>}
        {mode !== 'split' && <button type="button" className="icon-btn" onClick={onClose} aria-label="닫기"><Icon name="x" /></button>}
      </div>
      <section className="panel game" role="tabpanel" aria-label="게임 채팅">
        <GameChatPanel />
      </section>
      <section className="panel ai" role="tabpanel" aria-label="AI 도우미">
        <AiPanel />
      </section>
    </aside>
  );
}
