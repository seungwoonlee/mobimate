import { useState } from 'react';
import { api, ApiError } from '../../api/http';
import { keys5, usePersonas } from '../../api/queries';
import type { CustomPersona, PersonaDraft } from '../../api/types';
import { Dialog, Icon } from '../../components/ui';
import { setPersona } from './GameChat';
import { queryClient } from '../../lib/queryClient';
import { useUi } from '../../state/ui';

/**
 * 커스텀 페르소나 관리 (FR-CH-04·07): 목록·추가·수정·삭제, 자연어로 초안 만들기(저장은 사용자가).
 * 커스텀 말투는 AI 엔진이 연결됐을 때만 제대로 나온다는 안내를 함께 둔다 (D-03).
 */
export function PersonaManager({ onClose }: { onClose: () => void }) {
  const q = usePersonas();
  const toast = useUi(s => s.toast);
  const [edit, setEdit] = useState<{ id?: string; name: string; emoji: string; prompt: string } | null>(null);
  const [request, setRequest] = useState('');
  const [busy, setBusy] = useState(false);

  const refresh = () => queryClient.invalidateQueries({ queryKey: keys5.personas });
  const fail = (e: unknown) => toast(e instanceof ApiError ? e.message : '요청이 실패했습니다', 'warn');

  const generate = async () => {
    if (!request.trim()) return;
    setBusy(true);
    try {
      const r = await api.post<PersonaDraft>('/api/personas/generate', { request: request.trim() });
      setEdit({ name: r.data.name, emoji: r.data.emoji, prompt: r.data.prompt });
      if (!r.data.fromAi) toast('AI 응답을 쓸 수 없어 기본 틀로 초안을 만들었습니다', 'info');
    } catch (e) { fail(e); } finally { setBusy(false); }
  };

  const save = async () => {
    if (!edit) return;
    setBusy(true);
    try {
      const body = { name: edit.name, emoji: edit.emoji, prompt: edit.prompt };
      const r = edit.id ? await api.put<CustomPersona>(`/api/personas/${edit.id}`, body) : await api.post<CustomPersona>('/api/personas', body);
      await refresh();
      void setPersona(`custom:${r.data.id}`);
      setEdit(null);
      toast(`${r.data.displayName} 저장했습니다`, 'ok');
    } catch (e) { fail(e); } finally { setBusy(false); }
  };

  const remove = async (p: CustomPersona) => {
    if (!window.confirm(`${p.displayName} 페르소나를 지울까요?`)) return;
    try {
      await api.del(`/api/personas/${p.id}`);
      await refresh();
      void setPersona('Villainess');
    } catch (e) { fail(e); }
  };

  return (
    <Dialog title="커스텀 페르소나" onClose={onClose}>
      {edit ? (
        <div className="form">
          <label>이름 <input value={edit.name} maxLength={20} onChange={e => setEdit({ ...edit, name: e.target.value })} /></label>
          <label>이모지 <input value={edit.emoji} maxLength={4} onChange={e => setEdit({ ...edit, emoji: e.target.value })} /></label>
          <label>말투 지침 <textarea value={edit.prompt} maxLength={300} rows={4} onChange={e => setEdit({ ...edit, prompt: e.target.value })} /></label>
          <div className="acts">
            <button type="button" className="btn" onClick={() => setEdit(null)}>취소</button>
            <button type="button" className="btn primary" disabled={busy || !edit.name.trim() || !edit.prompt.trim()} onClick={save}>저장</button>
          </div>
        </div>
      ) : (
        <>
          <p className="small muted">커스텀 말투는 AI 엔진(Ollama 등)이 연결돼 있을 때 나옵니다.</p>
          <div className="list">
            {(q.data?.data ?? []).map(p => (
              <div className="row" key={p.id}>
                <div className="t"><span>{p.displayName}</span></div>
                <div className="r pills">
                  <button type="button" className="btn" onClick={() => setEdit({ id: p.id, name: p.name, emoji: p.tagEmoji, prompt: p.systemPrompt })}>수정</button>
                  <button type="button" className="btn" onClick={() => remove(p)} aria-label={`${p.name} 삭제`}><Icon name="x" /></button>
                </div>
                <div className="m">{p.systemPrompt}</div>
              </div>
            ))}
            {q.data?.data.length === 0 && <div className="row"><div className="t faint">아직 만든 페르소나가 없습니다.</div></div>}
          </div>
          <div className="form">
            <label>AI로 초안 만들기
              <input value={request} maxLength={200} placeholder="예: 공주기사 느낌으로 만들어줘" onChange={e => setRequest(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter' && !e.nativeEvent.isComposing) void generate(); }} />
            </label>
            <div className="acts">
              <button type="button" className="btn" onClick={() => setEdit({ name: '', emoji: '🎭', prompt: '' })}>직접 만들기</button>
              <button type="button" className="btn primary" disabled={busy || !request.trim()} onClick={generate}><Icon name="spark" />{busy ? '만드는 중…' : '초안 만들기'}</button>
            </div>
          </div>
        </>
      )}
    </Dialog>
  );
}
