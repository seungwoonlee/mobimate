import { useEffect, useRef, useState } from 'react';
import { api, ApiError } from '../api/http';
import { keys } from '../api/queries';
import { Icon } from '../components/ui';
import { queryClient } from '../lib/queryClient';
import { useDevice } from '../state/device';
import { useUi } from '../state/ui';

export const NICKNAME_MAX = 20;

/**
 * 캐릭터 별칭 (P1): 이름 옆 연필을 눌러 그 자리에서 고친다. Enter 저장 · Esc 취소 · 비워서 저장하면 별칭 해제.
 * 별칭이 없으면 안내 말풍선을 한 번 띄운다(닫으면 이 기기에서는 다시 보이지 않는다).
 */
export function NicknameEditor({ name, current }: { name: string; current: string | null }) {
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);
  const seen = useDevice(s => s.nickHintSeen);
  const setDevice = useDevice(s => s.set);
  const toast = useUi(s => s.toast);
  const input = useRef<HTMLInputElement>(null);
  const showHint = !current && !seen && !editing && name !== '…';

  useEffect(() => { if (editing) input.current?.select(); }, [editing]);

  const start = () => {
    setText(current ?? '');
    setEditing(true);
    if (!seen) setDevice({ nickHintSeen: true });
  };

  const save = async () => {
    const v = text.trim();
    if (v.length > NICKNAME_MAX) { toast(`별칭은 ${NICKNAME_MAX}자까지입니다`, 'warn'); return; }
    if (v === (current ?? '')) { setEditing(false); return; }
    setBusy(true);
    try {
      await api.put('/api/profile/nickname', { nickname: v });
      await Promise.all([queryClient.invalidateQueries({ queryKey: keys.header }), queryClient.invalidateQueries({ queryKey: keys.characters })]);
      toast(v ? `별칭을 “${v}”로 정했습니다` : '별칭을 지웠습니다', 'ok');
      setEditing(false);
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '별칭을 저장하지 못했습니다', 'warn');
    } finally {
      setBusy(false);
    }
  };

  if (editing) {
    return (
      <form className="nm-form" onSubmit={e => { e.preventDefault(); void save(); }}>
        <input ref={input} value={text} maxLength={NICKNAME_MAX} aria-label="캐릭터 별칭" placeholder="별칭" disabled={busy}
          onChange={e => setText(e.target.value)}
          onKeyDown={e => { if (e.key === 'Escape' && !e.nativeEvent.isComposing) { e.stopPropagation(); setEditing(false); } }} />
        <button type="submit" className="icon-btn" aria-label="별칭 저장" disabled={busy}><Icon name="check" /></button>
        <button type="button" className="icon-btn" aria-label="별칭 편집 취소" onClick={() => setEditing(false)}><Icon name="x" /></button>
      </form>
    );
  }
  return (
    <span className="nm-wrap">
      <span className="nm">{name}</span>
      <button type="button" className="nm-edit" onClick={start} aria-label="캐릭터 별칭 바꾸기" title="별칭 바꾸기"><Icon name="edit" /></button>
      {showHint && (
        <span className="nm-hint" role="note">
          연필을 눌러 별칭을 정해 두면 캐릭터를 알아보기 쉬워요
          <button type="button" aria-label="안내 닫기" onClick={() => setDevice({ nickHintSeen: true })}>×</button>
        </span>
      )}
    </span>
  );
}
