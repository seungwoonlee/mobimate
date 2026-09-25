import { useDeferredValue, useMemo, useState } from 'react';
import { useCurrencies, useInventory, useNearby } from '../api/queries';
import type { InvItem } from '../api/types';
import { Delta, ErrorCard, Fresh, Gauge, Icon, LevelState, Pill, Skeleton } from '../components/ui';
import { fmt } from '../lib/format';
import { useRouter } from '../state/router';

const LOC: Record<string, string> = { bag: '가방', account: '계정 창고', character: '캐릭터 창고' };
const SEGS: [string, string][] = [['all', '전체'], ['bag', '가방'], ['account', '계정 창고'], ['character', '캐릭터 창고'], ['diet', '⚖ 다이어트']];

/** 가방·창고 (FR-DT-02): 위치 세그먼트, 이름·카테고리 검색, 잠금 표시, 이번 접속 증가량, 무게 다이어트 정렬. */
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
    } else if (loc !== 'all') {
      r = r.filter(x => x.location === loc);
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
      {rows.length === 0 ? <div className="card muted">조건에 맞는 아이템이 없습니다.</div> : (
        <div className="list" role="list">
          {rows.map(r => (
            <div className="row" role="listitem" key={`${r.location}|${r.name}`}>
              <div className="t">{r.locked && <Icon name="lock" label="잠금" />}<span>{r.name}</span></div>
              <div className="r">
                <span className="num strong">{fmt(r.count)}</span>
                {r.sessionDelta > 0 && <Pill tone="warn">+{fmt(r.sessionDelta)} 이번 접속</Pill>}
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

/** 재화 (FR-DT-03): 핵심 3종 먼저, 이번 접속 증감. */
export function CurrenciesView() {
  const q = useCurrencies();
  if (q.isPending) return <Skeleton lines={4} />;
  if (q.isError) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const { items, session } = q.data.data;
  const core = ['골드', '정령의 날개', '냥 토큰'];
  const deltaOf: Record<string, number> = { '골드': session.gold, '정령의 날개': session.wings, '냥 토큰': session.nyang };
  const sorted = [...items].sort((a, b) => {
    const ia = core.indexOf(a.DisplayName), ib = core.indexOf(b.DisplayName);
    return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib);
  });
  return (
    <>
      <div className="view-head">
        <h2>재화</h2>
        <span className="sub">이번 접속 · 골드 <Delta value={session.gold} unit=" G" /> 날개 <Delta value={session.wings} /> 냥 <Delta value={session.nyang} /> · <Fresh at={q.dataUpdatedAt} /></span>
      </div>
      <div className="tiles">
        {sorted.map(c => {
          const gold = c.DisplayName === '골드';
          const d = deltaOf[c.DisplayName];
          return (
            <div key={c.DisplayName} className={`tile ${gold ? 'gold-tile' : ''}`}>
              <span className="l">{c.DisplayName}</span>
              <span className={`v num ${gold ? 'gold-text' : ''}`}>{fmt(c.Amount)}</span>
              {d !== undefined ? <Delta value={d} /> : null}
            </div>
          );
        })}
      </div>
    </>
  );
}

/** 주변 레이더 (FR-DT-06): 관계 → 전투력 → 거리 순(서버 정렬), 인원수, 강한 유저 배지, 길드원만 보기. */
export function NearbyView() {
  const q = useNearby();
  const [guildOnly, setGuildOnly] = useState(false);
  if (q.isPending) return <Skeleton lines={5} />;
  if (q.isError) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const d = q.data.data;
  const rows = d.players.filter(p => !guildOnly || p.relation === 'guild');
  const tone = { party: 'info', friend: 'cyan', guild: 'ok', other: 'plain' } as const;
  return (
    <>
      <div className="view-head">
        <h2>주변 플레이어 <span className="num muted">({d.count}명)</span></h2>
        <span className="sub"><Fresh at={q.dataUpdatedAt} /> <button type="button" className="chip" aria-pressed={guildOnly} onClick={() => setGuildOnly(v => !v)}>길드원만</button></span>
      </div>
      {rows.length === 0 ? <div className="card muted">주변에 표시할 플레이어가 없습니다.</div> : (
        <div className="list" role="list">
          {rows.map(p => (
            <div className="row" role="listitem" key={`${p.name}|${p.realm}`}>
              <div className="t">
                <span>{p.name}</span>
                {p.relation !== 'other' ? <Pill tone={tone[p.relation]}>{p.relationLabel}</Pill> : <span className="faint small">({p.relationLabel})</span>}
                {p.isStronger && <Pill tone="warn">강함</Pill>}
              </div>
              <div className="r"><span className="num strong">{p.distance.toFixed(1)}m</span></div>
              <div className="m">
                <span>{p.job}</span><span>Lv.{p.level}</span><span className="faint">·</span>
                <span>전투력 <span className="num">{fmt(p.combatScore)}</span>{p.inCombat && <span className="danger-text"> (전투 중)</span>}</span>
              </div>
            </div>
          ))}
        </div>
      )}
      <p className="faint small m0">내 전투력 <span className="num">{fmt(d.myCombatScore)}</span> 기준 · {rows.length}명 표시</p>
    </>
  );
}
