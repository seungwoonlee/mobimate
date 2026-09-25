import { Icon } from '../components/ui';
import { useUi } from '../state/ui';
import { stopAction } from '../lib/actions';

/**
 * 채팅·AI 도크 틀 (상세설계 §4.5). 옆 패널·시트·책 자세 분할을 모두 같은 DOM으로 보여 준다(FR-MB-03).
 * 게임 채팅·아무말·AI 대화 내용은 S5에서 채운다.
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
      <section className="panel game" role="tabpanel">
        <div className="dock-empty"><Icon name="chat" size={28} /><p>게임 채팅·아무말 대잔치는 다음 단계(S5)에서 연결합니다.</p></div>
      </section>
      <section className="panel ai" role="tabpanel">
        <div className="dock-empty"><Icon name="spark" size={28} /><p>AI 도우미는 다음 단계(S5)에서 연결합니다.</p></div>
      </section>
    </aside>
  );
}
