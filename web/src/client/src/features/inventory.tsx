import { useDeferredValue, useMemo, useState } from 'react';
import { useCurrencies, useHeader, useInventory, useNearby } from '../api/queries';
import type { Currency, InvItem, NearPlayer } from '../api/types';
import { Delta, ErrorCard, Fresh, Gauge, Icon, JobIcon, LevelState, Pill, Skeleton } from '../components/ui';
import { setFavorite } from '../lib/actions';
import { fmt, plain } from '../lib/format';
import { useRouter } from '../state/router';
import { AutoBadge, autoStyle, jobLabel, MissingLine, REL_TAG, REL_TEXT } from './players';

/**
 * 임무용 아이템(2026-10-24 승운): 마을별 채집·요리 목록. 제작품(장비)은 게임이 개수를 알려 주지 않아(get_items에 장비가 없다) 뺐다. 임무용 보기는 이 목록만, 가진 개수가 0이어도 모두 보여 준다.
 * 이름이 정확히 같은 것만 임무용으로 본다(유니티 색 태그는 떼고 비교). 목록은 승운이 준 것이고, 새로 필요한 아이템은 여기에 더한다.
 * 이름에 '|'가 있으면 앞이 화면에 보이는 이름이고 뒤는 게임이 달리 부를 수 있는 표기다(어느 쪽이든 같은 아이템으로 센다).
 */
export const TOWNS = ['티르코네일', '던바튼', '콜헨', '반호르', '이멘마하'] as const;
export const KINDS = ['채집', '요리'] as const;
export interface MissionInfo { town: string; kind: string; canon: string }
const MISSION_TABLE: Record<string, Record<string, string[]>> = {
  '티르코네일': { '채집': ['거미줄', '양털', '블러디 허브', '밀', '감자'], '요리': ['달걀 프라이|달걀프라이'] },
  '던바튼': { '채집': ['쑥쑥 버섯', '우유', '철 광석', '옥수수', '양파'], '요리': ['사과 주스'] },
  '콜헨': { '채집': ['화살꽃', '달걀', '상급 통나무+', '조개'], '요리': ['조개찜'] },
  '반호르': { '채집': ['콩', '파스닙'], '요리': ['통감자구이'] },
  '이멘마하': { '채집': ['쌀', '호박', '헤이즐넛', '끈적 풀'], '요리': ['마요네즈 고기볶음'] },
};
/** 목록 순서대로의 임무용 아이템(화면에 보이는 이름 기준). */
export const MISSION_SLOTS: readonly { town: string; kind: string; name: string }[] = Object.entries(MISSION_TABLE)
  .flatMap(([town, kinds]) => Object.entries(kinds).flatMap(([kind, names]) => names.map(n => ({ town, kind, name: n.split('|')[0] }))));
const MISSION_INFO: ReadonlyMap<string, MissionInfo> = new Map(
  Object.entries(MISSION_TABLE).flatMap(([town, kinds]) => Object.entries(kinds).flatMap(([kind, names]) =>
    names.flatMap(n => { const [canon, ...alias] = n.split('|'); return [canon, ...alias].map(a => [a, { town, kind, canon }] as const); }))));
export const MISSION_ITEMS: ReadonlySet<string> = new Set(MISSION_INFO.keys());
export const missionInfo = (name: string): MissionInfo | undefined => MISSION_INFO.get(plain(name).trim());
export const isMissionItem = (name: string): boolean => MISSION_INFO.has(plain(name).trim());

const LOC_SHORT: Record<string, string> = { bag: '가방', account: '계정', character: '캐릭터' };
const LOC_KEYS = ['bag', 'account', 'character'] as const;
/** 같은 이름의 아이템을 한 카드로 합친 것. 위치별 개수와 총합을 가진다. */
export interface InvGroup { name: string; category: string; favorite: boolean; locked: boolean; sessionDelta: number; total: number; by: Record<string, number> }
type Mode = 'mission' | 'diet';
const MODES: [Mode, string][] = [['mission', '임무용 보기'], ['diet', '다이어트 보기']];
const SORTS: Record<Mode, [string, string][]> = {
  mission: [['table', '마을 목록 순'], ['count', '개수 많은 순'], ['countAsc', '개수 적은 순'], ['name', '이름순'], ['delta', '이번 접속 늘어난 순'], ['deltaAsc', '이번 접속 줄어든 순']],
  diet: [['count', '개수 많은 순'], ['countAsc', '개수 적은 순'], ['name', '이름순'], ['category', '카테고리순'], ['delta', '이번 접속 늘어난 순'], ['deltaAsc', '이번 접속 줄어든 순']],
};
/** 다이어트 보기의 위치 구분. 무게가 걸리는 곳은 가방이라 가방이 기본이다. */
const DIET_SEGS: [string, string][] = [['bag', '가방'], ['all', '전체'], ['fav', '★ 다이어트 대상'], ['account', '계정 창고'], ['character', '캐릭터 창고']];

/**
 * 가방·창고: 맨 위에서 두 보기를 고른다.
 * - 임무용 보기: 임무용 아이템만, 개수가 0이어도 채집·요리 → 마을 순으로 모두 보여 준다.
 * - 다이어트 보기: 가방·창고 전체 아이템. 별(즐겨찾기)을 누른 것이 "다이어트 대상"이라 맨 위에 모인다.
 *   게임은 아이템별 무게를 주지 않아(가방 전체 무게만 준다) 개수 순이 기본이다.
 */
export function InventoryView() {
  const q = useInventory();
  const params = useRouter(s => s.loc.params);
  const setParam = useRouter(s => s.setParam);
  const mode: Mode = params.get('mode') === 'diet' ? 'diet' : 'mission';
  const rawLoc = params.get('loc');
  const loc = DIET_SEGS.some(([k]) => k === rawLoc) ? rawLoc! : 'bag';
  const [text, setText] = useState(params.get('q') ?? '');
  const search = useDeferredValue(text.trim());   // 입력 중 화면이 버벅이지 않게 (디바운스 대신)
  const rawSort = params.get('sort');
  const sort = SORTS[mode].some(([k]) => k === rawSort) ? rawSort! : SORTS[mode][0][0];

  const rows = useMemo(() => {
    const items = q.data?.data.items ?? [];
    let r: InvGroup[];
    if (mode === 'mission') {
      // 게임이 달리 부르는 표기는 화면 이름으로 모아 센다. 가진 게 없는 임무용 아이템도 0개 카드로 만든다.
      const have = new Map(groupItems(items.filter(x => isMissionItem(x.name)).map(x => ({ ...x, name: missionInfo(x.name)!.canon }))).map(g => [g.name, g]));
      r = MISSION_SLOTS.map(s => have.get(s.name) ?? { name: s.name, category: '', favorite: false, locked: false, sessionDelta: 0, total: 0, by: {} });
      r = r.sort(SORT_FN[sort] ?? SORT_FN.table);
    } else {
      r = groupItems(LOC_KEYS.includes(loc as never) ? items.filter(x => x.location === loc) : items);
      if (loc === 'fav') r = r.filter(x => x.favorite);
      r = r.sort(SORT_FN[sort] ?? SORT_FN.count);
    }
    if (search) r = r.filter(x => {
      const m = missionInfo(x.name);
      return plain(x.name).includes(search) || x.category.includes(search) || (!!m && (m.town.includes(search) || m.kind.includes(search)));
    });
    return r;
  }, [q.data, mode, loc, search, sort]);

  const modeBar = (
    <div className="seg mode-seg" role="group" aria-label="보기 모드">
      {MODES.map(([k, l]) => (
        <button key={k} type="button" aria-pressed={mode === k} onClick={() => { setParam('sort', null); setParam('mode', k === 'mission' ? null : k); }}>{l}</button>
      ))}
    </div>
  );
  if (q.isPending) return <>{modeBar}<Skeleton lines={6} /></>;
  if (q.isError && !q.data) return <>{modeBar}<ErrorCard error={q.error} onRetry={() => q.refetch()} /></>;
  const w = q.data.data.weight;

  const searchBox = (
    <label className="search">
      <Icon name="search" />
      <input type="search" placeholder={mode === 'mission' ? '이름·마을·종류 검색' : '이름·카테고리 검색'} value={text} aria-label="아이템 검색"
        onChange={e => { setText(e.target.value); setParam('q', e.target.value || null); }} />
    </label>
  );
  const sortBox = (
    <label className="inv-sort">정렬
      <select value={sort} aria-label="정렬 순서" onChange={e => setParam('sort', e.target.value === SORTS[mode][0][0] ? null : e.target.value)}>
        {SORTS[mode].map(([k, l]) => <option key={k} value={k}>{l}</option>)}
      </select>
    </label>
  );

  if (mode === 'mission') {
    const haveCount = rows.filter(r => r.total > 0).length;
    return (
      <>
        {modeBar}
        <div className="view-head">
          <h2>임무용 아이템</h2>
          <span className="sub num">보유 {fmt(haveCount)} / {fmt(rows.length)}종 · <Fresh at={q.dataUpdatedAt} /></span>
        </div>
        <div className="toolbar">{searchBox}{sortBox}</div>
        {rows.length === 0 ? <div className="card muted">조건에 맞는 아이템이 없습니다.</div> : KINDS.map(k => {
          const inKind = rows.filter(r => missionInfo(r.name)?.kind === k);
          if (inKind.length === 0) return null;
          return (
            <section key={k} aria-label={k}>
              <h3 className="group-h">{k} <span className="num muted small">(보유 {fmt(inKind.filter(r => r.total > 0).length)} / {fmt(inKind.length)}종)</span></h3>
              {TOWNS.map(t => {
                const list = inKind.filter(r => missionInfo(r.name)?.town === t);
                if (list.length === 0) return null;
                return (
                  <div key={t}>
                    <h4 className="town-h">{t}</h4>
                    <ItemList rows={list} mode={mode} />
                  </div>
                );
              })}
            </section>
          );
        })}
      </>
    );
  }

  // 다이어트 보기: 다이어트 대상(별)을 먼저 모아 보이고, 나머지는 그 아래에 둔다.
  const targets = loc === 'fav' ? [] : rows.filter(r => r.favorite);
  const others = loc === 'fav' ? rows : rows.filter(r => !r.favorite);
  return (
    <>
      {modeBar}
      <div className="view-head">
        <h2>다이어트</h2>
        <span className="sub num">{w && <>{w.current.toFixed(1)} / {fmt(w.max)} · <LevelState level={w.level} /> {w.pct.toFixed(1)}% · </>}<Fresh at={q.dataUpdatedAt} /></span>
      </div>
      {w && <Gauge pct={w.pct} level={w.level} label="가방 무게" />}
      <div className="toolbar">
        <div className="seg" role="group" aria-label="보관 위치">
          {DIET_SEGS.map(([k, l]) => (
            <button key={k} type="button" aria-pressed={loc === k} onClick={() => setParam('loc', k === 'bag' ? null : k)}>{l}</button>
          ))}
        </div>
        {searchBox}{sortBox}
      </div>
      <p className="muted small m0">게임이 아이템별 무게를 알려 주지 않아 <b>개수 순</b>으로 보여 줍니다. 버릴·팔 아이템은 별을 눌러 <b>다이어트 대상</b>으로 표시하면 맨 위에 모입니다.</p>
      {rows.length === 0 ? <div className="card muted">{loc === 'fav' ? '다이어트 대상이 없습니다. 아이템 옆 별을 눌러 추가하세요.' : '조건에 맞는 아이템이 없습니다.'}</div> : (
        <>
          {targets.length > 0 && (
            <>
              <h3 className="group-h">다이어트 대상 <span className="num muted small">({fmt(targets.length)}종)</span></h3>
              <ItemList rows={targets} mode={mode} />
            </>
          )}
          {others.length > 0 && (
            <>
              {targets.length > 0 && <h3 className="group-h">그 외 <span className="num muted small">({fmt(others.length)}종)</span></h3>}
              <ItemList rows={others} mode={mode} />
            </>
          )}
        </>
      )}
      <p className="faint small m0">{fmt(rows.length)}종 표시</p>
    </>
  );
}

/** 아이템 목록(작은 카드 격자). 카드 하나가 이름이 같은 아이템 전체이고, 위치별 개수와 총합을 보인다. 0개 카드는 흐리게. */
function ItemList({ rows, mode }: { rows: InvGroup[]; mode: Mode }) {
  const diet = mode === 'diet';
  return (
    <div className="inv-grid" role="list">
      {rows.map(r => {
        const parts = LOC_KEYS.filter(k => (r.by[k] ?? 0) > 0);
        const info = missionInfo(r.name);
        const detail = parts.map(k => `${LOC_SHORT[k]} ${fmt(r.by[k])}`).join(', ');
        return (
          <div className={`inv-card ${r.favorite && diet ? 'fav' : ''} ${r.total === 0 ? 'zero' : ''}`} role="listitem" key={r.name}
            title={`${plain(r.name)} · ${info ? `${info.town} ${info.kind}` : r.category} · 총 ${fmt(r.total)}개${detail ? ` (${detail})` : ''}`}>
            <div className="t">
              {diet && <button type="button" className="star" aria-pressed={r.favorite} aria-label={`${plain(r.name)} 다이어트 대상`} title={r.favorite ? '다이어트 대상 해제' : '다이어트 대상으로 표시'}
                onClick={() => void setFavorite('items', r.name, !r.favorite)}>{r.favorite ? '★' : '☆'}</button>}
              {r.locked && <Icon name="lock" label="잠금" />}<span>{plain(r.name)}</span>
              {diet && info && <span className="mis-tag" title="임무에 쓰는 아이템">임무</span>}
            </div>
            <div className="tot">
              <span className="num strong">{fmt(r.total)}</span>
              {r.sessionDelta !== 0 && <Pill tone={r.sessionDelta > 0 ? 'warn' : 'danger'} title={`이번 접속에 ${r.sessionDelta > 0 ? '늘어난' : '줄어든'} 개수`}>{r.sessionDelta > 0 ? '+' : '−'}{fmt(Math.abs(r.sessionDelta))}</Pill>}
              <span className="by">{parts.length === 0 ? '없음' : parts.map(k => `${LOC_SHORT[k]} ${fmt(r.by[k])}`).join(' · ')}</span>
            </div>
          </div>
        );
      })}
    </div>
  );
}

const byName = (a: InvGroup, b: InvGroup) => plain(a.name).localeCompare(plain(b.name), 'ko');
const SORT_FN: Record<string, (a: InvGroup, b: InvGroup) => number> = {
  table: () => 0,   // 안정 정렬이라 목록(마을) 순서가 그대로 남는다
  count: (a, b) => b.total - a.total || byName(a, b),
  countAsc: (a, b) => a.total - b.total || byName(a, b),
  name: byName,
  category: (a, b) => a.category.localeCompare(b.category, 'ko') || byName(a, b),
  deltaAsc: (a, b) => a.sessionDelta - b.sessionDelta || a.total - b.total || byName(a, b),
  delta: (a, b) => b.sessionDelta - a.sessionDelta || b.total - a.total || byName(a, b),
};

/** 이름이 같은 아이템(여러 칸·여러 위치)을 한 카드로 합친다. 잠금은 하나라도 잠겼으면 잠금, 즐겨찾기도 하나라도 있으면 즐겨찾기로 본다. */
export function groupItems(items: InvItem[]): InvGroup[] {
  const m = new Map<string, InvGroup>();
  const perLoc = new Map<string, Record<string, number>>();
  for (const i of items) {
    const k = plain(i.name).trim();
    const e = m.get(k);
    if (e) {
      e.total += i.count; e.by[i.location] = (e.by[i.location] ?? 0) + i.count;
      e.locked ||= i.locked; e.favorite ||= i.favorite;
      if (!e.category) e.category = i.category;
    } else m.set(k, { name: i.name, category: i.category, favorite: i.favorite, locked: i.locked, sessionDelta: 0, total: i.count, by: { [i.location]: i.count } });
    // 변화량은 (위치, 이름)마다 한 값이고 같은 이름의 여러 칸이 같은 값을 들고 오므로, 위치마다 한 번만 더한다
    const seen = (perLoc.get(k) ?? perLoc.set(k, {}).get(k)!);
    if (!(i.location in seen)) { seen[i.location] = i.sessionDelta; m.get(k)!.sessionDelta += i.sessionDelta; }
  }
  return [...m.values()];
}


/** 재화 묶음 (FR-DT-17): 골드·데카·M캐시 다음의 재화를 종류별로 묶는다. 분류는 이름 규칙(초안)이다. */
export const CURRENCY_PRIORITY = ['골드', '데카', 'M캐시'];
export function currencyGroup(name: string): string {
  if (name.includes('토큰')) return '토큰';
  if (/환생석|갱신권|강화|원석|결정|재료/.test(name)) return '성장·갱신';
  if (name.includes('정령의 날개')) return '활동력';
  return '기타';
}
const GROUP_ORDER = ['활동력', '토큰', '성장·갱신', '기타'];

/** 재화 (FR-DT-03·17): 골드·데카·M캐시를 크게 맨 위에, 나머지는 종류별로 묶는다. */
export function CurrenciesView() {
  const q = useCurrencies();
  if (q.isPending) return <Skeleton lines={4} />;
  if (q.isError && !q.data) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const { items, session } = q.data.data;
  const deltaOf: Record<string, number> = { '골드': session.gold, '정령의 날개': session.wings, '냥 토큰': session.nyang };
  const top = CURRENCY_PRIORITY.map(n => items.find(c => c.DisplayName === n || (n === 'M캐시' && c.DisplayName === 'M캐쉬'))).filter((c): c is Currency => !!c);
  const rest = items.filter(c => !top.includes(c));
  const groups = GROUP_ORDER.map(g => [g, rest.filter(c => currencyGroup(c.DisplayName) === g)] as const).filter(([, list]) => list.length > 0);
  return (
    <>
      <div className="view-head">
        <h2>재화</h2>
        <span className="sub">이번 접속 · 골드 <Delta value={session.gold} unit=" G" /> 날개 <Delta value={session.wings} /> 냥 <Delta value={session.nyang} /> · <Fresh at={q.dataUpdatedAt} /></span>
      </div>
      <div className="tiles core">
        {top.map(c => {
          const gold = c.DisplayName === '골드';
          const d = deltaOf[c.DisplayName];
          return (
            <div key={c.DisplayName} className={`tile big ${gold ? 'gold-tile' : ''}`}>
              <span className="l">{c.DisplayName}</span>
              <span className={`v num ${gold ? 'gold-text' : ''}`}>{fmt(c.Amount)}</span>
              {d !== undefined ? <Delta value={d} /> : null}
            </div>
          );
        })}
      </div>
      {groups.map(([g, list]) => (
        <section key={g} aria-label={g}>
          <h3 className="group-h">{g}</h3>
          <div className="tiles">
            {list.map(c => {
              const d = deltaOf[c.DisplayName];
              return (
                <div key={c.DisplayName} className="tile">
                  <span className="l">{c.DisplayName}</span>
                  <span className="v num">{fmt(c.Amount)}</span>
                  {d !== undefined ? <Delta value={d} /> : null}
                </div>
              );
            })}
          </div>
        </section>
      ))}
    </>
  );
}

/**
 * 주변 (FR-DT-06·30~34): 파티원 > 친구 > 길드원 > 그 외(서버 정렬)를 표로 보인다. 열: 직업(친구·길드원 표식 포함) · 칭호 · 전투력 · 거리 · 상태 · 레벨 · 생활력 · 매력 · 복장. 던전이면 전투력 순, 아니면 가까운 순.
 * 게임은 캐릭터 이름을 주지 않아 이름 열은 없다. 친구·길드원은 색과 굵은 글씨, 나보다 강하면 전투력 숫자의 색으로만 알린다. 길드원만 보기 필터.
 */
const mmss = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;

function Status({ p }: { p: NearPlayer }) {
  const perf = p.performing;
  return (
    <span className="nb-c nb-st" data-label="상태">
      {p.inCombat && <span className="pl-combat">전투 중</span>}
      {perf && <span className="nb-play" title={perf.title ?? undefined}>연주 중{perf.title ? ` · ${perf.title}` : ''}{perf.remaining != null ? ` (${mmss(perf.remaining)})` : ''}</span>}
      {p.autoSuspect && <AutoBadge reason={p.autoSuspect.reason} seconds={p.autoSuspect.seconds} intensity={p.autoSuspect.intensity} />}
    </span>
  );
}

export function NearbyView() {
  const q = useNearby();
  const watching = useHeader().data?.data.autoWatching === true;   // 레이드·어비스 안: 탭 이름 "파티"
  const [guildOnly, setGuildOnly] = useState(false);
  if (q.isPending) return <Skeleton lines={5} />;
  if (q.isError && !q.data) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const d = q.data.data;
  const rows = d.players.filter(p => !guildOnly || p.relation === 'guild');
  return (
    <>
      <div className="view-head">
        <h2>{watching ? '파티' : '주변'} <span className="num muted">({d.count}명)</span>{watching && <span className="watch-pill" title="레이드·어비스 안이라 오토 의심을 보는 중이에요">오토 의심 감시 중</span>}</h2>
        <span className="sub"><Fresh at={q.dataUpdatedAt} /> <button type="button" className="chip" aria-pressed={guildOnly} onClick={() => setGuildOnly(v => !v)}>길드원만</button></span>
      </div>
      {rows.length === 0 ? <div className="card muted">주변에 표시할 플레이어가 없습니다.</div> : (
        <section className="card nb-table" aria-label="주변 플레이어">
          <div className="nb-grid">
          <div className="nb-row nb-head" aria-hidden="true">
            <span>직업</span><span>칭호</span><span className="nb-n">전투력</span><span className="nb-n">거리</span><span>상태</span><span className="nb-lo nb-n">레벨</span><span className="nb-lo nb-n">생활력</span><span className="nb-lo nb-n">매력</span>
          </div>
          <ul className="nb-body">
            {rows.map((p, i) => (
              <li className={`nb-row pl-row ${p.relation}${p.autoSuspect ? ' auto-suspect' : ''}`} style={p.autoSuspect ? autoStyle(p.autoSuspect.intensity) : undefined} key={`${p.job}|${p.level}|${p.combatScore}|${i}`}>
                <span className={`nb-c nb-job pl ${p.relation}`} data-label="직업">
                  {REL_TAG[p.relation] && <span className="pl-tag" aria-label={REL_TEXT[p.relation]}>{REL_TAG[p.relation]}</span>}
                  <span className="pl-job"><JobIcon job={p.job} size={22} />{jobLabel(p.job.trim(), p.realm?.trim(), d.myRealm)}</span>
                </span>
                <span className={`nb-c nb-title pl ${p.relation}`} data-label="칭호"><span className="pl-title">{p.title?.trim() || <span className="faint">–</span>}</span></span>
                <span className={`nb-c nb-n num pl-cs ${p.isStronger ? 'stronger' : ''}`} data-label="전투력">{fmt(p.combatScore)}{p.isStronger && <span className="sr-only"> (나보다 강함)</span>}</span>
                <span className="nb-c nb-n num nb-dist" data-label="거리">{p.distance.toFixed(1)}m</span>
                <Status p={p} />
                <span className="nb-c nb-n nb-lo num" data-label="레벨">{p.level}</span>
                <span className="nb-c nb-n nb-lo num" data-label="생활력">{p.living > 0 ? fmt(p.living) : '–'}</span>
                <span className="nb-c nb-n nb-lo num" data-label="매력">{p.attract > 0 ? fmt(p.attract) : '–'}</span>
              </li>
            ))}
          </ul>
          </div>
        </section>
      )}
      {(d.missing?.length ?? 0) > 0 && (
        <div className="card">
          <h3 className="m0">한동안 안 보이는 사람</h3>
          <div className="pl-list big" role="list">
            {d.missing!.map((m, i) => (
              <div className="pl-row auto-suspect" style={autoStyle(m.intensity)} role="listitem" key={`${m.job}|${m.level}|${m.combatScore}|${i}`}><MissingLine m={m} myRealm={d.myRealm} /></div>
            ))}
          </div>
        </div>
      )}
    </>
  );
}
