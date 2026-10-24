import { useDeferredValue, useMemo, useState } from 'react';
import { useCurrencies, useHeader, useInventory, useNearby } from '../api/queries';
import type { Currency, InvItem, NearPlayer } from '../api/types';
import { Delta, ErrorCard, Fresh, Gauge, Icon, JobIcon, LevelState, Pill, Skeleton } from '../components/ui';
import { setFavorite } from '../lib/actions';
import { fmt, plain } from '../lib/format';
import { useRouter } from '../state/router';
import { AutoBadge, autoStyle, jobLabel, MissingLine, REL_TAG, REL_TEXT } from './players';

/**
 * 임무용 아이템(2026-10-24 승운, 도시별 채집·요리·제작 목록으로 갱신): 임무에 쓰이는 채집물·요리·제작품. 가방·창고 목록에서 일반 아이템보다 위의 별도 묶음으로 먼저 보인다.
 * 이름이 정확히 같은 것만 묶는다(유니티 색 태그는 떼고 비교). 목록은 승운이 준 것이고, 새로 필요한 아이템은 여기에 더한다.
 */
export const MISSION_ITEMS: ReadonlySet<string> = new Set([
  // 채집물 (티르코네일·던바튼·콜헨·반호르·이멘마하)
  '거미줄', '양털', '블러디 허브', '밀', '감자',
  '쑥쑥 버섯', '우유', '철 광석', '옥수수', '양파',
  '화살꽃', '달걀', '상급 통나무+', '조개',
  '콩', '파스닙',
  '쌀', '호박', '헤이즐넛', '끈적 풀',
  // 요리 ('달걀프라이'·'전문 캠프파이어 키트'는 앞서 받은 표기라 게임 이름이 어느 쪽이든 잡히도록 함께 둔다)
  '달걀 프라이', '달걀프라이', '사과 주스', '조개찜', '통감자구이', '마요네즈 고기볶음',
  // 제작
  '캠프파이어 키트', '전문 캠프파이어 키트', '마법 유탄 부품',
  '크레센트 엣지소드', '그랜드 크로스보우', '로터스 힐링 완드',
  '사슬 갑옷 신발', '두꺼운 가죽 갑옷 신발', '두꺼운 전투복 신발',
  '론 엣지소드S', '라이트 크로스보우S', '마블 힐링 완드S',
  '비늘 갑옷 신발S', '가죽 갑옷 신발S', '전투복 신발S',
]);
export const isMissionItem = (name: string): boolean => MISSION_ITEMS.has(plain(name).trim());

const LOC_SHORT: Record<string, string> = { bag: '가방', account: '계정창고', character: '캐릭터창고' };
const LOC_KEYS = ['bag', 'account', 'character'] as const;
/** 같은 이름의 아이템을 한 카드로 합친 것. 위치별 개수와 총합을 가진다. */
export interface InvGroup { name: string; category: string; favorite: boolean; locked: boolean; sessionDelta: number; total: number; by: Record<string, number> }
const SORTS: [string, string][] = [['count', '개수 많은 순'], ['countAsc', '개수 적은 순'], ['name', '이름순'], ['category', '카테고리순'], ['delta', '이번 접속 늘어난 순']];
const SEGS: [string, string][] = [['all', '전체'], ['fav', '★ 즐겨찾기'], ['bag', '가방'], ['account', '계정 창고'], ['character', '캐릭터 창고'], ['diet', '⚖ 다이어트']];

/**
 * 가방·창고 (FR-DT-02): 위치 세그먼트, 이름·카테고리 검색, 잠금 표시, 무게 다이어트 정렬.
 * 즐겨찾기 (FR-DT-15·16): 별을 누르면 즐겨찾기가 되어 목록 맨 위에 오고, 이번 접속의 개수 변화는 즐겨찾기 아이템만 보여 준다.
 * (다이어트 보기는 늘어난 순서가 목적이라 모든 아이템의 변화를 보여 준다.)
 */
export function InventoryView() {
  const q = useInventory();
  const params = useRouter(s => s.loc.params);
  const setParam = useRouter(s => s.setParam);
  const loc = params.get('loc') ?? 'all';
  const [text, setText] = useState(params.get('q') ?? '');
  const search = useDeferredValue(text.trim());   // 입력 중 화면이 버벅이지 않게 (디바운스 대신)

  const sort = params.get('sort') ?? 'count';

  const rows = useMemo(() => {
    const items = q.data?.data.items ?? [];
    let r: InvGroup[];
    if (loc === 'diet') {
      r = groupItems(items.filter(x => x.location === 'bag' && !x.locked))
        .sort((a, b) => Number(b.sessionDelta > 0) - Number(a.sessionDelta > 0) || b.sessionDelta - a.sessionDelta || b.total - a.total);
    } else {
      r = groupItems(LOC_KEYS.includes(loc as never) ? items.filter(x => x.location === loc) : items);
      if (loc === 'fav') r = r.filter(x => x.favorite);
      r = r.sort(SORT_FN[sort] ?? SORT_FN.count);
      r = r.sort((a, b) => Number(b.favorite) - Number(a.favorite));   // 즐겨찾기가 먼저 (정렬은 안정적이라 나머지 순서는 그대로)
    }
    if (search) r = r.filter(x => plain(x.name).includes(search) || x.category.includes(search));
    return r;
  }, [q.data, loc, search, sort]);

  if (q.isPending) return <Skeleton lines={6} />;
  if (q.isError && !q.data) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const w = q.data.data.weight;
  // 임무용 아이템을 위 묶음으로(다이어트 보기는 늘어난 순서가 목적이라 묶지 않는다). 각 묶음 안의 순서(즐겨찾기 먼저)는 그대로.
  const missionRows = rows.filter(r => isMissionItem(r.name));
  const normalRows = rows.filter(r => !isMissionItem(r.name));

  return (
    <>
      <div className="view-head">
        <h2>가방·창고</h2>
        <span className="sub num">{w && <>{w.current.toFixed(1)} / {fmt(w.max)} · <LevelState level={w.level} /> {w.pct.toFixed(1)}% · </>}<Fresh at={q.dataUpdatedAt} /></span>
      </div>
      {w && <Gauge pct={w.pct} level={w.level} label="가방 무게" />}
      <div className="toolbar">
        <div className="seg" role="group" aria-label="보관 위치">
          {SEGS.map(([k, l]) => (
            <button key={k} type="button" aria-pressed={loc === k} onClick={() => setParam('loc', k === 'all' ? null : k)}>{l}</button>
          ))}
        </div>
        <label className="search">
          <Icon name="search" />
          <input type="search" placeholder="이름·카테고리 검색" value={text} aria-label="아이템 검색"
            onChange={e => { setText(e.target.value); setParam('q', e.target.value || null); }} />
        </label>
        {loc !== 'diet' && (
          <label className="inv-sort">정렬
            <select value={sort} aria-label="정렬 순서" onChange={e => setParam('sort', e.target.value === 'count' ? null : e.target.value)}>
              {SORTS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}
            </select>
          </label>
        )}
      </div>
      {loc === 'diet' && <p className="muted small m0">가방 안 잠금 해제 아이템 중 <b>이번 접속에 늘어난 순</b> → 많이 가진 순입니다.</p>}
      {rows.length === 0 ? <div className="card muted">{loc === 'fav' ? '즐겨찾기한 아이템이 없습니다. 아이템 옆 별을 눌러 추가하세요.' : '조건에 맞는 아이템이 없습니다.'}</div> : (
        <>
          {loc !== 'diet' && missionRows.length > 0 ? (
            <>
              <h3 className="group-h">임무용 아이템 <span className="num muted small">({fmt(missionRows.length)}종)</span></h3>
              <ItemList rows={missionRows} loc={loc} />
              {normalRows.length > 0 && (
                <>
                  <h3 className="group-h">일반 아이템 <span className="num muted small">({fmt(normalRows.length)}종)</span></h3>
                  <ItemList rows={normalRows} loc={loc} />
                </>
              )}
            </>
          ) : <ItemList rows={rows} loc={loc} />}
        </>
      )}
      <p className="faint small m0">{fmt(rows.length)}종 표시</p>
    </>
  );
}

/** 아이템 목록(작은 카드 격자). 카드 하나가 이름이 같은 아이템 전체이고, 위치별 개수와 총합을 보인다. */
function ItemList({ rows, loc }: { rows: InvGroup[]; loc: string }) {
  return (
    <div className="inv-grid" role="list">
      {rows.map(r => {
        const parts = LOC_KEYS.filter(k => (r.by[k] ?? 0) > 0);
        const detail = parts.map(k => `${LOC_SHORT[k]} ${fmt(r.by[k])}`).join(', ');
        return (
          <div className={`inv-card ${r.favorite ? 'fav' : ''}`} role="listitem" key={r.name} title={`${plain(r.name)} · ${r.category} · 총 ${fmt(r.total)}개${detail ? ` (${detail})` : ''}`}>
            <div className="t">
              <button type="button" className="star" aria-pressed={r.favorite} aria-label={`${plain(r.name)} 즐겨찾기`} title={r.favorite ? '즐겨찾기 해제' : '즐겨찾기'}
                onClick={() => void setFavorite('items', r.name, !r.favorite)}>{r.favorite ? '★' : '☆'}</button>
              {r.locked && <Icon name="lock" label="잠금" />}<span>{plain(r.name)}</span>
            </div>
            <div className="tot"><span className="num strong">{fmt(r.total)}</span>
              {r.sessionDelta > 0 && (r.favorite || loc === 'diet') && <Pill tone="warn">+{fmt(r.sessionDelta)}</Pill>}</div>
            {parts.length > 1 || (parts.length === 1 && loc === 'all') ? (
              <ul className="by">{parts.map(k => <li key={k}><span>{LOC_SHORT[k]}</span><span className="num">{fmt(r.by[k])}</span></li>)}</ul>
            ) : <div className="by one">{parts.length === 1 ? LOC_SHORT[parts[0]] : ''}</div>}
          </div>
        );
      })}
    </div>
  );
}

const byName = (a: InvGroup, b: InvGroup) => plain(a.name).localeCompare(plain(b.name), 'ko');
const SORT_FN: Record<string, (a: InvGroup, b: InvGroup) => number> = {
  count: (a, b) => b.total - a.total || byName(a, b),
  countAsc: (a, b) => a.total - b.total || byName(a, b),
  name: byName,
  category: (a, b) => a.category.localeCompare(b.category, 'ko') || byName(a, b),
  delta: (a, b) => b.sessionDelta - a.sessionDelta || b.total - a.total || byName(a, b),
};

/** 이름이 같은 아이템(여러 칸·여러 위치)을 한 카드로 합친다. 잠금은 하나라도 잠겼으면 잠금, 즐겨찾기도 하나라도 있으면 즐겨찾기로 본다. */
export function groupItems(items: InvItem[]): InvGroup[] {
  const m = new Map<string, InvGroup>();
  for (const i of items) {
    const k = plain(i.name).trim();
    const e = m.get(k);
    if (e) {
      e.total += i.count; e.by[i.location] = (e.by[i.location] ?? 0) + i.count;
      e.locked ||= i.locked; e.favorite ||= i.favorite; e.sessionDelta = Math.max(e.sessionDelta, i.sessionDelta);
    } else m.set(k, { name: i.name, category: i.category, favorite: i.favorite, locked: i.locked, sessionDelta: i.sessionDelta, total: i.count, by: { [i.location]: i.count } });
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
