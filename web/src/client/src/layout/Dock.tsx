import { Icon } from '../components/ui';
import { GameChatPanel } from '../features/dock/GameChat';
import { AiPanel } from '../features/dock/AiPanel';
import { useUi } from '../state/ui';
import { stopAction } from '../lib/actions';

/**
 * 채팅·AI 도크 (상세설계 §4.5). 옆 패널·시트·책 자세 분할을 모두 같은 DOM으로 보여 준다(FR-MB-03).
 * 두 패널은 늘 마운트해 두고 CSS로만 바꿔 보인다: 탭을 오가도 입력·스트리밍이 이어진다.
 */
export function Dock({ mode, onClose }: { mode: 'side' | 'sheet' | 'split'; onClose: () => void }) {
  const tab = useUi(s => s.dockTab);
  const setDock = useUi(s => s.setDock);
  return (
    <aside className="dock" aria-label="채팅과 AI">
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
