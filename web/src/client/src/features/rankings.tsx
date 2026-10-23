import { useEffect, useRef, useState } from 'react';
import { useHeader, useRankings, useRankingSetup, useSession } from '../api/queries';
import type { RankEntryView, RankKindKey, RankView } from '../api/types';
import { NameBadge, TIER_RULE, TierDot } from '../components/RankBadge';
import { CardHead, Dialog, Icon } from '../components/ui';
import { useNow } from '../hooks/layout';
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
  const badge = useHeader().data?.data.rankBadge;   // 지금 캐릭터의 인게임 딱지 예측
  const now = useNow(60_000);
  const [open, setOpen] = useState(false);
  const entries = view?.entries ?? {};
  const hasAny = Object.keys(entries).length > 0;
  const newest = Object.values(entries).map(e => e.at).sort().at(-1);   // 네 종류를 한꺼번에 가져오므로 시각은 하나로 충분하다

  return (
    <section className="card rank-panel" aria-labelledby="rank-h">
      <CardHead icon="trophy" title={<><span id="rank-h">서버 랭킹</span>{newest && <span className="rk-ago faint small"> {agoText(newest, now)}</span>}<NameBadge badge={badge} big /></>}
        right={<button type="button" className="btn" disabled={!key} onClick={() => setOpen(true)}>랭킹 갱신</button>} />
      {!view ? <p className="faint small m0">불러오는 중…</p>
        : !view.hasName ? <p className="small m0">캐릭터 이름을 입력하면 서버 랭킹을 볼 수 있어요. 위쪽 이름 옆 연필(<Icon name="edit" size={13} />)로 <b>게임 속 캐릭터 이름</b>을 적어 주세요.</p>
        : !view.supported ? <p className="small m0">이 서버는 랭킹 조회 대상이 아닙니다.</p>
        : (
          <>
            {view.nameChanged && <p className="small warn-text m0">캐릭터 이름이 바뀌어 이전 순위는 쓰지 않아요. 랭킹 갱신으로 다시 가져와 주세요.</p>}
            <ul className="rank-list" aria-label={`${view.name} 서버 랭킹`}>
              {KINDS.map(k => <RankRow key={k.key} label={k.label} e={entries[k.key]} />)}
            </ul>
            {!hasAny && !view.nameChanged && <p className="faint small m0">아직 가져온 순위가 없어요. 랭킹 갱신을 눌러 가져오세요.</p>}
            <p className="faint small m0">{view.serverName} 서버 · 이름 “{view.name}” 기준 · 종합 = 전투력 + 생활력 + 매력</p>
          </>
        )}
      {open && <RankUpdateDialog onClose={() => setOpen(false)} />}
    </section>
  );
}

function RankRow({ label, e }: { label: string; e: RankEntryView | undefined }) {
  return (
    <li className="rank-row">
      <span className="rk-l">{label}</span>
      {e == null ? <span className="rk-v faint">—</span>
        : e.rank == null ? <span className="rk-v faint">순위 정보 없음</span>
        : <span className="rk-v num"><TierDot tier={e.tier} />{fmt(e.rank)}위</span>}
      <span className="rk-s num faint">{e?.score != null ? fmt(e.score) : ''}</span>
    </li>
  );
}

/** 랭킹 갱신 버튼과 창(북마크릿 안내). 스탯 탭 머리글에서 쓴다. */
export function RankRefreshButton() {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button type="button" className="btn" onClick={() => setOpen(true)}>랭킹 갱신</button>
      {open && <RankUpdateDialog onClose={() => setOpen(false)} />}
    </>
  );
}

/** 랭킹 갱신 창: 북마크릿(넥슨 랭킹 페이지에서 직접 누름) 안내와 수동 입력 */
function RankUpdateDialog({ onClose }: { onClose: () => void }) {
  const toast = useUi(s => s.toast);
  const local = useSession().data?.kind === 'local';
  const setup = useRankingSetup(local);
  const link = useRef<HTMLAnchorElement>(null);
  const code = setup.data?.data.bookmarklet;
  const url = setup.data?.data.rankingUrl ?? 'https://mabinogimobile.nexon.com/Ranking/List?t=1';

  // 북마크릿 주소는 DOM에 직접 넣는다 (React가 javascript: 주소를 막는다)
  useEffect(() => { if (link.current && code) link.current.setAttribute('href', code); }, [code]);

  const copy = async () => {
    if (!code) return;
    try { await navigator.clipboard.writeText(code); toast('북마크릿 코드를 복사했어요', 'ok'); } catch { toast('복사하지 못했어요. 버튼을 즐겨찾기 막대로 끌어 놓으세요.', 'warn'); }
  };

  return (
    <Dialog title="서버 랭킹 갱신" onClose={onClose}
      actions={<button type="button" className="btn" onClick={onClose}>닫기</button>}>
      <div className="rank-dialog">
        <p className="small m0">넥슨 랭킹 페이지는 프로그램이 직접 가져오지 못하게 막혀 있어서, <b>승운님의 브라우저에서 버튼을 누를 때만</b> 순위를 가져옵니다. 앱이 스스로 조회하지는 않아요.</p>
        {local ? (
          <>
          <h4>1. 북마크릿으로 가져오기 (PC 브라우저)</h4>
          <ol className="rank-steps small">
            <li>아래 <b>MobiMate 순위</b> 버튼을 즐겨찾기 막대로 끌어 놓으세요. (안 되면 <button type="button" className="linklike" onClick={() => void copy()}>코드 복사</button> 뒤 즐겨찾기 주소에 붙여넣기)</li>
            <li><a href={url} target="_blank" rel="noopener noreferrer">넥슨 랭킹 페이지 열기</a></li>
            <li>그 페이지에서 즐겨찾기의 <b>MobiMate 순위</b>를 누르세요. <b>지금 로그인한 계정</b>(접속 중인 캐릭터의 계정, 접속이 없으면 마지막으로 본 계정)의 이름 있는 캐릭터만, 5분 안에 가져온 순위만 건너뛰고 2초 간격으로 가져와 이 앱에 저장해요. 다른 계정은 넥슨에서 그 계정으로 로그인한 뒤 같은 방법으로 가져오세요.</li>
            <li>처음 누르면 브라우저가 <b>“이 기기의 로컬 네트워크에 연결”</b> 같은 권한을 물어요. 이 앱(내 PC)으로 보내려는 것이니 <b>허용</b>을 눌러 주세요.</li>
          </ol>
          <p><a ref={link} className="btn primary bookmarklet" draggable="true" onClick={e => e.preventDefault()} aria-label="MobiMate 순위 북마크릿. 즐겨찾기 막대로 끌어 놓으세요">MobiMate 순위</a></p>
          </>
        ) : <p className="small m0"><b>북마크릿</b>은 게임을 켠 PC의 브라우저에서만 쓸 수 있어요.</p>}
        <p className="small faint m0">딱지: {TIER_RULE} — 색은 네 분류 중 가장 높은 등급, 숫자는 그 등급에 든 분류 수</p>
      </div>
    </Dialog>
  );
}
