import { useEffect, useRef, useState } from 'react';
import { useHeader, useRankings, useRankingSetup, useSession, keys } from '../api/queries';
import { api, ApiError } from '../api/http';
import type { RankEntryView, RankKindKey, RankView } from '../api/types';
import { RankBadge, TIER_RULE } from '../components/RankBadge';
import { CardHead, Dialog, Icon } from '../components/ui';
import { useNow } from '../hooks/layout';
import { queryClient } from '../lib/queryClient';
import { fmt } from '../lib/format';
import { useUi } from '../state/ui';

/** 표시 순서: 종합 → 전투력 → 생활력 → 매력. kind는 서버 RankKind 값(전투력 1, 매력 2, 생활력 3, 종합 4)과 같다. */
const KINDS: { key: RankKindKey; label: string; kind: number }[] = [
  { key: 'total', label: '종합', kind: 4 },
  { key: 'combat', label: '전투력', kind: 1 },
  { key: 'living', label: '생활력', kind: 3 },
  { key: 'attract', label: '매력', kind: 2 },
];

/** "3시간 전" 같은 경과 시간 */
export function agoText(at: string, now: number): string {
  const min = Math.max(0, Math.floor((now - new Date(at).getTime()) / 60_000));
  if (min < 1) return '방금';
  if (min < 60) return `${min}분 전`;
  if (min < 60 * 24) return `${Math.floor(min / 60)}시간 전`;
  return `${Math.floor(min / 1440)}일 전`;
}

/** 현재 캐릭터의 랭킹 보기를 찾는다 */
export function useCurrentRank(): { key: string | undefined; view: RankView | undefined; loading: boolean } {
  const header = useHeader();
  const r = useRankings();
  const key = header.data?.data.characterKey;
  return { key, view: key ? r.data?.data.characters[key] : undefined, loading: r.isPending };
}

/** 서버 랭킹 카드 (개요·스탯 탭): 종합·전투력·생활력·매력 순위와 가져온 시각, 갱신 버튼. 앱은 스스로 조회하지 않는다. */
export function RankPanel() {
  const { key, view } = useCurrentRank();
  const now = useNow(60_000);
  const [open, setOpen] = useState(false);
  const entries = view?.entries ?? {};
  const hasAny = Object.keys(entries).length > 0;

  return (
    <section className="card rank-panel" aria-labelledby="rank-h">
      <CardHead icon="trophy" title={<span id="rank-h">서버 랭킹</span>}
        right={<button type="button" className="btn" disabled={!key} onClick={() => setOpen(true)}>랭킹 갱신</button>} />
      {!view ? <p className="faint small m0">불러오는 중…</p>
        : !view.hasName ? <p className="small m0">캐릭터 이름을 입력하면 서버 랭킹을 볼 수 있어요. 위쪽 이름 옆 연필(<Icon name="edit" size={13} />)로 <b>게임 속 캐릭터 이름</b>을 적어 주세요.</p>
        : !view.supported ? <p className="small m0">이 서버는 랭킹 조회 대상이 아닙니다.</p>
        : (
          <>
            {view.nameChanged && <p className="small warn-text m0">캐릭터 이름이 바뀌어 이전 순위는 쓰지 않아요. 랭킹 갱신으로 다시 가져와 주세요.</p>}
            <ul className="rank-list" aria-label={`${view.name} 서버 랭킹`}>
              {KINDS.map(k => <RankRow key={k.key} label={k.label} e={entries[k.key]} now={now} />)}
            </ul>
            {!hasAny && !view.nameChanged && <p className="faint small m0">아직 가져온 순위가 없어요. 랭킹 갱신을 눌러 가져오세요.</p>}
            <p className="faint small m0">{view.serverName} 서버 · 이름 “{view.name}” 기준 · 종합 = 전투력 + 생활력 + 매력</p>
          </>
        )}
      {open && key && view && <RankUpdateDialog charKey={key} view={view} onClose={() => setOpen(false)} />}
    </section>
  );
}

function RankRow({ label, e, now }: { label: string; e: RankEntryView | undefined; now: number }) {
  return (
    <li className="rank-row">
      <span className="rk-l">{label}</span>
      {e == null ? <span className="rk-v faint">—</span>
        : e.rank == null ? <span className="rk-v faint">순위 정보 없음</span>
        : <span className="rk-v num">서버 {fmt(e.rank)}위 <RankBadge tier={e.tier} rank={e.rank} stale={e.stale} /></span>}
      <span className="rk-s num faint">{e?.score != null ? fmt(e.score) : ''}</span>
      <span className={`rk-t small ${e?.stale ? 'warn-text' : 'faint'}`}>{e ? `${agoText(e.at, now)}${e.stale ? ' · 오래됨' : ''}${e.source === 'manual' ? ' · 직접 입력' : ''}` : ''}</span>
    </li>
  );
}

/** 랭킹 갱신 창: 북마크릿(넥슨 랭킹 페이지에서 직접 누름) 안내와 수동 입력 */
function RankUpdateDialog({ charKey, view, onClose }: { charKey: string; view: RankView; onClose: () => void }) {
  const toast = useUi(s => s.toast);
  const local = useSession().data?.kind === 'local';
  const setup = useRankingSetup(local);
  const link = useRef<HTMLAnchorElement>(null);
  const [vals, setVals] = useState<Record<string, string>>(() => Object.fromEntries(KINDS.map(k => [k.key, view.entries[k.key]?.rank != null ? String(view.entries[k.key]!.rank) : ''])));
  const [busy, setBusy] = useState(false);
  const code = setup.data?.data.bookmarklet;
  const url = setup.data?.data.rankingUrl ?? 'https://mabinogimobile.nexon.com/Ranking/List?t=1';

  // 북마크릿 주소는 DOM에 직접 넣는다 (React가 javascript: 주소를 막는다)
  useEffect(() => { if (link.current && code) link.current.setAttribute('href', code); }, [code]);

  const copy = async () => {
    if (!code) return;
    try { await navigator.clipboard.writeText(code); toast('북마크릿 코드를 복사했어요', 'ok'); } catch { toast('복사하지 못했어요. 버튼을 즐겨찾기 막대로 끌어 놓으세요.', 'warn'); }
  };

  const save = async () => {
    setBusy(true);
    try {
      let saved = 0;
      for (const k of KINDS) {
        const raw = vals[k.key].trim();
        const had = view.entries[k.key]?.rank != null;
        if (raw === '') {
          if (had && view.entries[k.key]?.source === 'manual') { await api.del(`/api/rankings/manual?key=${encodeURIComponent(charKey)}&kind=${k.kind}`); saved++; }
          continue;
        }
        const n = Number(raw.replace(/,/g, ''));
        if (!Number.isInteger(n) || n < 1) { toast(`${k.label} 순위는 1 이상의 숫자로 적어 주세요`, 'warn'); setBusy(false); return; }
        if (had && view.entries[k.key]?.rank === n) continue;
        await api.put('/api/rankings/manual', { key: charKey, kind: k.kind, rank: n });
        saved++;
      }
      await Promise.all([queryClient.invalidateQueries({ queryKey: keys.rankings }), queryClient.invalidateQueries({ queryKey: keys.header }), queryClient.invalidateQueries({ queryKey: keys.characters })]);
      toast(saved > 0 ? '순위를 저장했어요' : '바뀐 순위가 없어요', 'ok');
      if (saved > 0) onClose();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : '저장하지 못했어요', 'warn');
    } finally { setBusy(false); }
  };

  return (
    <Dialog title="서버 랭킹 갱신" onClose={onClose}
      actions={<><button type="button" className="btn" onClick={onClose}>닫기</button><button type="button" className="btn primary" disabled={busy} onClick={() => void save()}>입력한 순위 저장</button></>}>
      <div className="rank-dialog">
        <p className="small m0">넥슨 랭킹 페이지는 프로그램이 직접 가져오지 못하게 막혀 있어서, <b>승운님의 브라우저에서 버튼을 누를 때만</b> 순위를 가져옵니다. 앱이 스스로 조회하지는 않아요.</p>
        {local ? (
          <>
          <h4>1. 북마크릿으로 가져오기 (PC 브라우저)</h4>
          <ol className="rank-steps small">
            <li>아래 <b>MobiMate 순위</b> 버튼을 즐겨찾기 막대로 끌어 놓으세요. (안 되면 <button type="button" className="linklike" onClick={() => void copy()}>코드 복사</button> 뒤 즐겨찾기 주소에 붙여넣기)</li>
            <li><a href={url} target="_blank" rel="noopener noreferrer">넥슨 랭킹 페이지 열기</a></li>
            <li>그 페이지에서 즐겨찾기의 <b>MobiMate 순위</b>를 누르세요. 이름이 있는 캐릭터마다 4종(종합·전투력·생활력·매력)을 1.2초 간격으로 가져와 이 앱에 저장해요.</li>
            <li>처음 누르면 브라우저가 <b>“이 기기의 로컬 네트워크에 연결”</b> 같은 권한을 물어요. 이 앱(내 PC)으로 보내려는 것이니 <b>허용</b>을 눌러 주세요.</li>
          </ol>
          <p><a ref={link} className="btn primary bookmarklet" draggable="true" onClick={e => e.preventDefault()} aria-label="MobiMate 순위 북마크릿. 즐겨찾기 막대로 끌어 놓으세요">MobiMate 순위</a></p>
          </>
        ) : <p className="small m0"><b>북마크릿</b>은 게임을 켠 PC의 브라우저에서만 쓸 수 있어요. 이 기기에서는 아래에 직접 입력해 주세요.</p>}
        <h4>2. 직접 입력 (폰·태블릿 등)</h4>
        <p className="small faint m0">이름 “{view.name}” · {view.serverName} 서버. 비워 두면 입력하지 않은 것으로 봐요.</p>
        <div className="rank-form">
          {KINDS.map(k => (
            <label key={k.key}>
              <span>{k.label}</span>
              <input inputMode="numeric" value={vals[k.key]} placeholder="순위" onChange={e => setVals(v => ({ ...v, [k.key]: e.target.value.replace(/[^\d,]/g, '') }))} />
            </label>
          ))}
        </div>
        <p className="small faint m0">뱃지: {TIER_RULE} (전투력 순위 기준)</p>
      </div>
    </Dialog>
  );
}
