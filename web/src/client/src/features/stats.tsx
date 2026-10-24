import { useCharacters, useCutoffs, useRankings } from '../api/queries';
import { useState } from 'react';
import type { AccountGroup, CharStats, CharacterCard, RankEntryView, RankKindKey, RankView } from '../api/types';
import { ErrorCard, JobIcon, Pill, Skeleton } from '../components/ui';
import { NameBadge, RankText } from '../components/RankBadge';
import { fmt } from '../lib/format';
import { scoreClass, type ScoreKind } from '../lib/score';
import { CutoffCard } from './cutoffs';
import { charName } from './characters';
import { RankRefreshButton, agoText } from './rankings';
import { useNow } from '../hooks/layout';

/**
 * 스탯 탭: 모든 계정의 캐릭터를 **한 표**에 모아 비교한다(2026-10-24 승운). 계정은 이름 줄에 작게 붙이고, 머리글을 눌러 그 열 기준으로 정렬한다. 기본·"캐릭터" 머리글은 계정(최근 접속 순) → 계정 안에서 전투력 순.
 * 열: 점수(전투력·마도저항·생활력·매력·종합, 서버 순위 포함)·기본 스탯·공격력·방어력·체력·데코·어비스·레이드 추천. 접속하지 않은 캐릭터는 마지막으로 관찰한 값이다.
 * 칸 폭은 내용에 맞고 줄바꿈하지 않으며, 좁은 화면에서는 줄이 카드로 접혀 옆으로 스크롤하지 않는다.
 */
type SortKey = 'character' | 'combat' | 'mdef' | 'living' | 'attract' | 'total' | keyof CharStats;

export function StatsView() {
  const chars = useCharacters();
  const rank = useRankings();
  const now = useNow(60_000);
  const [sort, setSort] = useState<SortKey>('character');

  const accounts: AccountGroup[] = chars.data?.data.accounts ?? [];
  const current = chars.data?.data.currentAccountId;
  // 지금 접속한 계정을 맨 위에 둔다
  const ordered = [...accounts].sort((x, y) => Number(y.id === current) - Number(x.id === current));
  const views = rank.data?.data.characters ?? {};
  const total = accounts.reduce((n, a) => n + a.members.length, 0);
  const value = (c: CharacterCard, k: Exclude<SortKey, 'character'>): number => {
    if (k === 'total') { const t = views[c.key] && !views[c.key].nameChanged ? views[c.key].entries.total?.score : undefined; return t ?? c.combat + c.living + c.attract; }
    if (k === 'combat' || k === 'mdef' || k === 'living' || k === 'attract') return c[k];
    return c.stats?.[k] ?? 0;
  };
  // 기본(캐릭터): 계정은 가장 최근에 접속한 순, 계정 안에서는 전투력 순. 다른 열을 누르면 계정 구분 없이 그 값 순.
  const latest = (a: AccountGroup) => Math.max(...a.members.map(m => (m.isCurrent ? Number.MAX_SAFE_INTEGER : Date.parse(m.lastSeen) || 0)));
  const rows = sort === 'character'
    ? [...accounts].sort((x, y) => latest(y) - latest(x)).flatMap(a => [...a.members].sort((p, q) => q.combat - p.combat).map(c => ({ c, account: a.solo ? a.name : `${a.name} 계정` })))
    : ordered.flatMap(a => a.members.map(c => ({ c, account: a.solo ? a.name : `${a.name} 계정` }))).sort((x, y) => value(y.c, sort) - value(x.c, sort));
  const oldest = accounts.flatMap(a => a.members).flatMap(m => Object.values(views[m.key]?.entries ?? {})).map(e => e.at).sort()[0];

  return (
    <>
      <div className="view-head">
        <h2>스탯 {total > 0 && <span className="num muted">({total}명)</span>}</h2>
        <span className="sub st-actions"><RankRefreshButton /></span>
      </div>

      {chars.isPending ? <Skeleton lines={5} />
        : chars.isError && !chars.data ? <ErrorCard error={chars.error} onRetry={() => chars.refetch()} />
        : accounts.length === 0 ? <div className="card muted">아직 확인한 캐릭터가 없습니다. 게임에 접속하면 여기에 모입니다.</div>
        : (
          <>
            <section className="card st-table" aria-label="캐릭터 스탯">
              <div className="st-grid">
                <div className="st-row st-head">
                  <button type="button" className={`st-hb${sort === 'character' ? ' on' : ''}`} aria-pressed={sort === 'character'} aria-label="계정(최근 접속 순)·전투력 순으로 정렬" onClick={() => setSort('character')}>캐릭터{sort === 'character' ? ' ▼' : ''}</button>
                  {SORTABLE.map(([k, label]) => <button key={k} type="button" className={`st-hb${k in STAT_KEYS ? ' st-hx' : ''}${sort === k ? ' on' : ''}`} aria-pressed={sort === k} aria-label={`${label} 순으로 정렬`} onClick={() => setSort(k)}>{label}{sort === k ? ' ▼' : ''}</button>)}
                  <span>어비스·레이드 추천</span>
                </div>
                <ul className="st-body">
                  {rows.map(({ c, account }) => <StatRow key={c.key} c={c} account={account} view={views[c.key]} now={now} />)}
                </ul>
              </div>
            </section>
            <p className="faint small m0 st-note">
              접속 중이 아닌 캐릭터는 마지막으로 본 값이고, 스탯은 그 캐릭터로 한 번 접속해야 채워져요. 서버 순위는 {oldest ? `${agoText(oldest, now)} 값` : '아직 가져오지 않았어요'}
              {oldest ? '' : ' (랭킹 갱신으로 가져오세요)'} · 종합 = 전투력 + 생활력 + 매력
            </p>
          </>
        )}

      <CurrentCutoffs />
    </>
  );
}

const KIND_KEY: Record<'combat' | 'living' | 'attract' | 'total', RankKindKey> = { combat: 'combat', living: 'living', attract: 'attract', total: 'total' };

function StatRow({ c, account, view, now }: { c: CharacterCard; account: string; view: RankView | undefined; now: number }) {
  const name = charName(c);
  const e = (k: keyof typeof KIND_KEY): RankEntryView | undefined => (view && !view.nameChanged ? view.entries[KIND_KEY[k]] : undefined);
  return (
    <li className={`st-row${c.isCurrent ? ' current' : ''}`}>
      <span className="st-who">
        <JobIcon job={c.job} size={22} />
        <span className="st-name">
          <span className="st-n">{name}<NameBadge badge={c.rankBadge} />{c.isCurrent && <Pill tone="ok">접속 중</Pill>}</span>
          <span className="small muted">{account} · {c.job} Lv.{c.level}{c.isCurrent ? '' : ` · ${agoText(c.lastSeen, now)}`}</span>
        </span>
      </span>
      <Score label="전투력" value={c.combat} kind="combat" rank={e('combat')} />
      <Score label="마도저항" value={c.mdef} kind="mdef" />
      <Score label="생활력" value={c.living} rank={e('living')} />
      <Score label="매력" value={c.attract} rank={e('attract')} />
      <Score label="종합" value={e('total')?.score ?? c.combat + c.living + c.attract} rank={e('total')} />
      <Detail s={c.stats} />
      <Recommend items={c.recommend} />
    </li>
  );
}

const STAT_KEYS: Record<string, true> = { str: true, dex: true, int: true, will: true, luck: true, attack: true, defence: true, health: true, decor: true };
const SORTABLE: [Exclude<SortKey, 'character'>, string][] = [
  ['combat', '전투력'], ['mdef', '마도저항'], ['living', '생활력'], ['attract', '매력'], ['total', '종합'],
  ['str', '힘'], ['dex', '솜씨'], ['int', '지력'], ['will', '의지'], ['luck', '행운'], ['attack', '공격력'], ['defence', '방어력'], ['health', '체력'], ['decor', '데코'],
];
const DETAIL: [keyof CharStats, string][] = [
  ['str', '힘'], ['dex', '솜씨'], ['int', '지력'], ['will', '의지'], ['luck', '행운'],
  ['attack', '공격력'], ['defence', '방어력'], ['health', '최대 체력'], ['decor', '데코'],
];

/**
 * get_my_info 스탯 칸들: 전투력·마도저항과 같은 표 열이다(넓은 화면). 중간 폭에서는 캐릭터 줄 아래 한 줄로 내려가고, 좁으면 카드 안 칸이 된다.
 * 접속하지 않은 캐릭터는 마지막으로 본 값, 아직 못 받았으면 "–".
 */
function Detail({ s }: { s: CharStats | undefined }) {
  return (
    <span className="st-det">
      {DETAIL.map(([k, label]) => (
        <span key={k} className="st-s st-x" data-label={label}>
          {s?.[k] != null ? <b className="num">{fmt(s[k]!)}</b> : <b className="faint" aria-label="아직 못 받음">–</b>}
        </span>
      ))}
    </span>
  );
}

const SHORT: Record<string, string> = { white_succubus: '서큐' };

/**
 * 어비스·레이드 추천을 두 줄로 줄인다: 어비스 한 줄, 레이드는 추천 난이도가 같은 것끼리 묶어 한 줄(예: 에이렐·서큐 매우 어려움 · 카브락 어려움).
 * 입장 못 하는 것은 "불가"로 묶는다. 자세한 입장 가능 난이도는 마우스를 올리면(title) 나온다.
 */
function Recommend({ items }: { items: CharacterCard['recommend'] }) {
  const tierOf = (r: CharacterCard['recommend'][number]) => (r.status === 'locked' ? null : r.tier);
  const title = (r: CharacterCard['recommend'][number]) => `${r.name}: ${r.status === 'locked' ? '입장 불가' : `입장 ${r.maxEntry} · 추천 ${r.tier}`}`;
  const abyss = items.find(r => r.id === 'abyss');
  const groups = new Map<string, CharacterCard['recommend']>();
  for (const r of items.filter(x => x.id !== 'abyss')) groups.set(tierOf(r) ?? '', [...(groups.get(tierOf(r) ?? '') ?? []), r]);
  return (
    <span className="st-rec" data-label="추천">
      {abyss && <span className="st-rec-i" title={title(abyss)}><span className="faint">어비스</span> {tierOf(abyss) ? <b>{tierOf(abyss)}</b> : <span className="danger-text">불가</span>}</span>}
      {groups.size > 0 && (
        <span className="st-rec-i">
          <span className="faint">레이드</span>{' '}
          {[...groups.entries()].map(([tier, rs], i) => (
            <span key={tier || 'x'} title={rs.map(title).join(' / ')}>{i > 0 && ' · '}{rs.map(r => SHORT[r.id] ?? r.name).join('·')} {tier ? <b>{tier}</b> : <span className="danger-text">불가</span>}</span>
          ))}
        </span>
      )}
    </span>
  );
}

/** 점수 한 칸: 값(게임과 같은 점수 색) + 서버 순위. 좁은 화면에서는 앞에 이름표가 붙는다. */
function Score({ label, value, kind, rank }: { label: string; value: number; kind?: ScoreKind; rank?: RankEntryView }) {
  return (
    <span className="st-s" data-label={label}>
      <b className={`num ${kind ? scoreClass(kind, value) : ''}`}>{fmt(value)}</b>
      {rank?.rank != null && <RankText rank={rank.rank} tier={rank.tier} className="st-rk" />}
    </span>
  );
}

/** 지금 접속한 캐릭터의 어비스·레이드 추천(다음 단계 가이드). 카드 수만큼 폭을 나눠 빈 칸이 남지 않게 한다. */
function CurrentCutoffs() {
  const cut = useCutoffs();
  if (!cut.data) return null;
  return (
    <section aria-label="지금 캐릭터 어비스·레이드 추천">
      <h3 className="st-acct st-cut-h">지금 캐릭터 · 어비스·레이드 추천</h3>
      <div className="st-cut">{cut.data.data.contents.map(x => <CutoffCard key={x.id} c={x} />)}</div>
    </section>
  );
}
