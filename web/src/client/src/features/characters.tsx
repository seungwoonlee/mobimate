import { useCharacters } from '../api/queries';
import type { CharacterCard } from '../api/types';
import { CardHead, ErrorCard, Fresh, Icon, Pill, Skeleton } from '../components/ui';
import { useNow } from '../hooks/layout';
import { fmt } from '../lib/format';
import { scoreClass } from '../lib/score';
import { useRouter } from '../state/router';

/** "방금 / 3시간 전 / 2일 전": 마지막으로 접속했을 때 본 값이 얼마나 오래됐는지 */
export function lastSeenText(iso: string, now = Date.now()): string {
  const m = Math.max(0, Math.floor((now - new Date(iso).getTime()) / 60000));
  if (m < 1) return '방금';
  if (m < 60) return `${m}분 전`;
  const h = Math.floor(m / 60);
  return h < 48 ? `${h}시간 전` : `${Math.floor(h / 24)}일 전`;
}

/**
 * 내 캐릭터 전체 현황 (FR-AL, 메인 화면): 캐릭터마다 카드 하나. 게임은 지금 접속한 캐릭터만 알려 주므로
 * 다른 캐릭터는 마지막으로 접속했을 때 본 값이다. 같은 서버·같은 직업 캐릭터 둘은 구분되지 않는다(캐릭터 키가 서버_직업).
 */
export function CharactersView() {
  const q = useCharacters();
  const now = useNow(30_000);
  if (q.isPending) return <><Skeleton /><Skeleton /></>;
  if (q.isError) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const list = q.data.data;

  return (
    <>
      <div className="view-head">
        <h2>내 캐릭터 <span className="num muted">({list.length}명)</span></h2>
        <span className="sub"><Fresh at={q.dataUpdatedAt} /></span>
      </div>
      {list.length === 0 ? (
        <div className="card muted">아직 확인한 캐릭터가 없습니다. 게임에 접속하면 여기에 모입니다.</div>
      ) : (
        <>
          <section className="chars" aria-label="내 캐릭터 목록">
            {list.map(c => <CharacterCardView key={c.key} c={c} now={now} />)}
          </section>
          <p className="faint small m0">지금 접속한 캐릭터만 실시간입니다. 다른 캐릭터는 마지막으로 접속했을 때 본 값입니다.</p>
        </>
      )}
    </>
  );
}

function CharacterCardView({ c, now }: { c: CharacterCard; now: number }) {
  const go = useRouter(s => s.go);
  const name = c.nickname ?? c.realm;
  const body = (
    <>
      <CardHead
        title={<span className="cc-name">{name}</span>}
        right={c.isCurrent ? <Pill tone="ok">접속 중</Pill> : <span className="faint small">{lastSeenText(c.lastSeen, now)}</span>}
      />
      <div className="cc-job">{c.job} Lv.{c.level}{c.title && <span className="cc-title">“{c.title}”</span>}</div>
      <div className="cc-main">
        <div><span className="lbl">전투력</span><b className={`v num ${scoreClass('combat', c.combat)}`}>{fmt(c.combat)}</b></div>
        <div><span className="lbl">마도저항</span><b className={`v num ${scoreClass('mdef', c.mdef)}`}>{fmt(c.mdef)}</b></div>
      </div>
      <div className="cc-sub">
        <span><Icon name="hammer" size={14} />생활력 <b className="num">{fmt(c.living)}</b></span>
        <span><Icon name="heart" size={14} />매력 <b className={`num ${scoreClass('attract', c.attract)}`}>{fmt(c.attract)}</b></span>
      </div>
      <div className="cc-wallet">
        <span><Icon name="coins" size={14} /><b className="num gold-text">{fmt(c.gold)}</b> G</span>
        <span className="muted">데카 <b className="num">{fmt(c.deca)}</b></span>
      </div>
    </>
  );
  // 지금 접속한 캐릭터는 누르면 자세한 개요로 간다. 나머지는 게임이 정보를 주지 않아 이동할 곳이 없다.
  return c.isCurrent
    ? <a className="card char cur link" href="/overview" aria-label={`${name} 개요 보기`} onClick={e => { if (e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return; e.preventDefault(); go('overview'); }}>{body}</a>
    : <article className="card char">{body}</article>;
}
