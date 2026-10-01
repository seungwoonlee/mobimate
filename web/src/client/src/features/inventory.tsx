import { useDeferredValue, useMemo, useState } from 'react';
import { useCurrencies, useInventory, useNearby } from '../api/queries';
import type { Currency, InvItem } from '../api/types';
import { Delta, ErrorCard, Fresh, Gauge, Icon, LevelState, Pill, Skeleton } from '../components/ui';
import { setFavorite } from '../lib/actions';
import { fmt } from '../lib/format';
import { useRouter } from '../state/router';
import { PlayerLine } from './players';

const LOC: Record<string, string> = { bag: '가방', account: '계정 창고', character: '캐릭터 창고' };
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

  const rows = useMemo(() => {
    let r: InvItem[] = merge(q.data?.data.items ?? []);
    if (loc === 'diet') {
      r = r.filter(x => x.location === 'bag' && !x.locked)
        .sort((a, b) => Number(b.sessionDelta > 0) - Number(a.sessionDelta > 0) || b.sessionDelta - a.sessionDelta || b.count - a.count);
    } else {
      if (loc === 'fav') r = r.filter(x => x.favorite);
      else if (loc !== 'all') r = r.filter(x => x.location === loc);
      r = r.sort((a, b) => Number(b.favorite) - Number(a.favorite));   // 즐겨찾기가 먼저 (정렬은 안정적이라 나머지 순서는 그대로)
    }
    if (search) r = r.filter(x => x.name.includes(search) || x.category.includes(search));
    return r;
  }, [q.data, loc, search]);

  if (q.isPending) return <Skeleton lines={6} />;
  if (q.isError) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const w = q.data.data.weight;

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
      </div>
      {loc === 'diet' && <p className="muted small m0">가방 안 잠금 해제 아이템 중 <b>이번 접속에 늘어난 순</b> → 많이 가진 순입니다.</p>}
      {rows.length === 0 ? <div className="card muted">{loc === 'fav' ? '즐겨찾기한 아이템이 없습니다. 아이템 옆 별을 눌러 추가하세요.' : '조건에 맞는 아이템이 없습니다.'}</div> : (
        <div className="list" role="list">
          {rows.map(r => (
            <div className={`row ${r.favorite ? 'fav' : ''}`} role="listitem" key={`${r.location}|${r.name}`}>
              <div className="t">
                <button type="button" className="star" aria-pressed={r.favorite} aria-label={`${r.name} 즐겨찾기`} title={r.favorite ? '즐겨찾기 해제' : '즐겨찾기'}
                  onClick={() => void setFavorite('items', r.name, !r.favorite)}>{r.favorite ? '★' : '☆'}</button>
                {r.locked && <Icon name="lock" label="잠금" />}<span>{r.name}</span>
              </div>
              <div className="r">
                <span className="num strong">{fmt(r.count)}</span>
                {r.sessionDelta > 0 && (r.favorite || loc === 'diet') && <Pill tone="warn">+{fmt(r.sessionDelta)} 이번 접속</Pill>}
              </div>
              <div className="m"><span>{r.category}</span><span className="faint">·</span><span>{LOC[r.location] ?? r.location}</span></div>
            </div>
          ))}
        </div>
      )}
      <p className="faint small m0">{fmt(rows.length)}종 표시</p>
    </>
  );
}

/** 같은 위치의 같은 아이템(여러 칸)을 합친다. 잠금은 하나라도 잠겼으면 잠금으로 본다. */
function merge(items: InvItem[]): InvItem[] {
  const m = new Map<string, InvItem>();
  for (const i of items) {
    const k = `${i.location}|${i.name}`;
    const e = m.get(k);
    if (e) m.set(k, { ...e, count: e.count + i.count, locked: e.locked || i.locked, sessionDelta: Math.max(e.sessionDelta, i.sessionDelta) });
    else m.set(k, { ...i });
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
  if (q.isError) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
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
 * 주변 레이더 (FR-DT-06·30~34): 파티원 > 친구 > 길드원 > 그 외(서버 정렬), 한 줄에 클래스 - 레벨 - 전투력 - 칭호.
 * 친구·길드원은 색과 굵은 글씨, 나보다 강하면 전투력 숫자의 색으로만 알린다. 길드원만 보기 필터.
 */
export function NearbyView() {
  const q = useNearby();
  const [guildOnly, setGuildOnly] = useState(false);
  if (q.isPending) return <Skeleton lines={5} />;
  if (q.isError) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const d = q.data.data;
  const rows = d.players.filter(p => !guildOnly || p.relation === 'guild');
  return (
    <>
      <div className="view-head">
        <h2>주변 플레이어 <span className="num muted">({d.count}명)</span></h2>
        <span className="sub"><Fresh at={q.dataUpdatedAt} /> <button type="button" className="chip" aria-pressed={guildOnly} onClick={() => setGuildOnly(v => !v)}>길드원만</button></span>
      </div>
      {rows.length === 0 ? <div className="card muted">주변에 표시할 플레이어가 없습니다.</div> : (
        <div className="card">
          <div className="pl-list big" role="list">
            {rows.map((p, i) => (
              <div className="pl-row" role="listitem" key={`${p.job}|${p.level}|${p.combatScore}|${i}`}>
                <PlayerLine p={p} />
                <span className="num faint small">{p.distance.toFixed(1)}m</span>
              </div>
            ))}
          </div>
        </div>
      )}
    </>
  );
}
