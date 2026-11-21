import { useDeferredValue, useId, useMemo, useState } from 'react';
import { useStock } from '../api/queries';
import type { StockAccount, StockRow } from '../api/types';
import { ErrorCard, Fresh, Icon, Skeleton } from '../components/ui';
import { setFavorite, setStockTarget, setStockTimed } from '../lib/actions';
import { fmt, plain } from '../lib/format';
import { useRouter } from '../state/router';
import { baseGroupOf, GROUPS, MANAGED, type GroupId } from './stockData';

const STALE_HOURS = 72;

/** "3시간 전" 같은 짧은 경과 표현 */
function ago(iso: string, now = Date.now()): string {
  const m = Math.max(0, Math.round((now - new Date(iso).getTime()) / 60_000));
  if (m < 1) return '방금';
  if (m < 60) return `${m}분 전`;
  const h = Math.round(m / 60);
  return h < 48 ? `${h}시간 전` : `${Math.round(h / 24)}일 전`;
}
const isStale = (iso: string) => Date.now() - new Date(iso).getTime() > STALE_HOURS * 3_600_000;

interface Line { name: string; row: StockRow | null; groups: GroupId[]; target: number; timed: boolean; fav: boolean }

/**
 * 계정 재료 현황: 한 계정의 모든 캐릭터의 가방·캐릭터 창고와 계정 창고를 합쳐 "이미 가진 양"과 "모자란 양"을 본다.
 * 캐릭터마다 마지막에 읽은 값이라 오래 접속하지 않은 캐릭터는 지금과 다를 수 있다(읽은 때를 위에 보인다).
 */
export function StockView() {
  const q = useStock();
  const params = useRouter(s => s.loc.params);
  const setParam = useRouter(s => s.setParam);
  const [text, setText] = useState('');
  const search = useDeferredValue(text.trim());
  const rawGroup = params.get('g');
  const group: GroupId | 'all' | 'short' = rawGroup === 'short' || GROUPS.some(g => g.id === rawGroup) ? (rawGroup as GroupId | 'short') : 'all';

  const data = q.data?.data;
  const accountId = params.get('acct') ?? data?.currentAccount ?? data?.accounts[0]?.id ?? null;
  const account: StockAccount | undefined = data?.accounts.find(a => a.id === accountId) ?? data?.accounts[0];

  const lines = useMemo<Line[]>(() => {
    if (!data || !account) return [];
    const byName = new Map(account.rows.map(r => [plain(r.name).trim(), r]));
    const favs = new Set(data.favorites.map(f => plain(f).trim()));
    const timed = new Set(data.prefs.timed);
    const names = new Map<string, GroupId[]>();
    const add = (n: string, g: GroupId | null) => { const l = names.get(n) ?? names.set(n, []).get(n)!; if (g && !l.includes(g)) l.push(g); };
    for (const m of MANAGED) add(m.name, m.group);
    for (const n of byName.keys()) add(n, baseGroupOf(n));
    for (const f of favs) { add(f, null); names.get(f)!.push('fav'); }
    for (const n of timed) { add(n, null); names.get(n)!.push('timed'); }
    for (const n of Object.keys(data.prefs.targets)) add(n, null);
    return [...names].map(([name, groups]) => ({
      name, row: byName.get(name) ?? null, groups, target: data.prefs.targets[name] ?? 0, timed: timed.has(name), fav: favs.has(name),
    }));
  }, [data, account]);

  const shown = useMemo(() => {
    const order = GROUPS.map(g => g.id);
    let r = lines;
    if (group === 'short') r = r.filter(l => l.target > 0 && (l.row?.total ?? 0) < l.target);
    else if (group !== 'all') r = r.filter(l => l.groups.includes(group));
    else r = r.filter(l => l.groups.length > 0 || (l.row?.total ?? 0) > 0 || l.target > 0);
    if (search) r = r.filter(l => plain(l.name).includes(search) || (l.row?.category ?? '').includes(search));
    return r.sort((a, b) => {
      const ga = Math.min(...a.groups.map(g => order.indexOf(g)), 99), gb = Math.min(...b.groups.map(g => order.indexOf(g)), 99);
      const sa = a.target > 0 ? (a.row?.total ?? 0) - a.target : Infinity, sb = b.target > 0 ? (b.row?.total ?? 0) - b.target : Infinity;
      return (group === 'all' ? ga - gb : 0) || sa - sb || (b.row?.total ?? 0) - (a.row?.total ?? 0) || a.name.localeCompare(b.name, 'ko');
    });
  }, [lines, group, search]);

  const captionId = useId();
  if (q.isPending) return <Skeleton lines={6} />;
  if (q.isError && !q.data) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  if (!data || !account) {
    return <div className="card muted">아직 모은 보유량이 없습니다. 게임에 접속한 채로 캐릭터를 한 번씩 읽으면(앱이 자동으로 읽습니다) 여기에 쌓입니다.</div>;
  }
  const shortCount = lines.filter(l => l.target > 0 && (l.row?.total ?? 0) < l.target).length;
  const staleChars = account.characters.filter(c => isStale(c.atUtc));

  return (
    <>
      <div className="toolbar">
        <div className="seg" role="group" aria-label="재료 묶음">
          <button type="button" aria-pressed={group === 'all'} onClick={() => setParam('g', null)}>전체</button>
          <button type="button" aria-pressed={group === 'short'} onClick={() => setParam('g', 'short')}>모자란 것{shortCount > 0 && <> · {shortCount}</>}</button>
          {GROUPS.map(g => <button key={g.id} type="button" aria-pressed={group === g.id} title={g.hint} onClick={() => setParam('g', g.id)}>{g.label}</button>)}
        </div>
        <label className="search">
          <Icon name="search" />
          <input type="search" placeholder="재료 이름 검색" value={text} aria-label="재료 검색" onChange={e => setText(e.target.value)} />
        </label>
        {data.accounts.length > 1 && (
          <label className="inv-sort">계정
            <select value={account.id} aria-label="계정" onChange={e => setParam('acct', e.target.value === data.currentAccount ? null : e.target.value)}>
              {data.accounts.map((a, i) => <option key={a.id} value={a.id}>{a.id === data.currentAccount ? '지금 계정' : `계정 ${i + 1}`} · {a.characters.length}캐릭</option>)}
            </select>
          </label>
        )}
      </div>

      <details className="stock-chars">
        <summary>
          {account.characters.length}개 캐릭터 합산
          {staleChars.length > 0 ? <span className="stock-warn"> · {staleChars.length}명은 {STALE_HOURS / 24}일 넘게 못 읽음</span> : <span className="muted"> · 모두 최근에 읽음</span>}
        </summary>
        <ul className="stock-char-list">
          {account.characters.map(c => (
            <li key={c.key} className={isStale(c.atUtc) ? 'stale' : ''}><span>{c.key}</span> <span className="muted">{ago(c.atUtc)}</span></li>
          ))}
        </ul>
        <p className="muted small m0">캐릭터를 읽은 시점의 값을 더한 것입니다. 오래 접속하지 않은 캐릭터는 그 뒤에 쓰거나 모은 만큼 다를 수 있습니다. 계정 창고는 한 번만 셉니다.</p>
      </details>

      {shown.length === 0 ? <div className="card muted">{group === 'short' ? '목표를 정한 재료 중 모자란 것이 없습니다. 개수 칸에 목표를 입력하면 여기에 모입니다.' : '조건에 맞는 재료가 없습니다.'}</div> : (
        <div className="card table-wrap" role="region" aria-labelledby={captionId} tabIndex={0}>
          <table className="stock-table">
            <caption id={captionId} className="sr-only">계정 전체 재료 보유량</caption>
            <thead>
              <tr><th scope="col">재료</th><th scope="col" className="n">합계</th><th scope="col" className="n">목표</th><th scope="col">부족</th><th scope="col" className="n">가방</th><th scope="col" className="n">캐릭 창고</th><th scope="col" className="n">계정 창고</th><th scope="col">보관 캐릭터</th></tr>
            </thead>
            <tbody>{shown.map(l => <StockLine key={l.name} l={l} />)}</tbody>
          </table>
        </div>
      )}
      <p className="faint small m0">
        {fmt(shown.length)}종 표시 · <Fresh at={q.dataUpdatedAt} /> · 사용기간 제한은 게임이 알려 주지 않아 ⏳로 직접 표시합니다.
      </p>
    </>
  );
}

function StockLine({ l }: { l: Line }) {
  const r = l.row;
  const total = r?.total ?? 0;
  const short = l.target > 0 ? Math.max(0, l.target - total) : 0;
  const holders = r?.holders ?? [];
  const staleHolders = holders.filter(h => isStale(h.atUtc)).length;
  return (
    <tr className={`${total === 0 ? 'zero' : ''} ${short > 0 ? 'short' : ''}`}>
      <th scope="row" data-label="재료">
        <button type="button" className="star" aria-pressed={l.fav} aria-label={`${plain(l.name)} 찜`} title={l.fav ? '찜 해제' : '찜하기 (이 목록에 항상 보입니다)'}
          onClick={() => void setFavorite('items', l.name, !l.fav)}>{l.fav ? '★' : '☆'}</button>
        <span className="stock-name">{plain(l.name)}</span>
        <button type="button" className="timed" aria-pressed={l.timed} aria-label={`${plain(l.name)} 기간제 표시`} title={l.timed ? '기간제 표시 해제' : '사용기간 제한이 있는 아이템으로 표시'}
          onClick={() => void setStockTimed(l.name, !l.timed)}>⏳</button>
      </th>
      <td className="n num strong" data-label="합계">{fmt(total)}</td>
      <td className="n" data-label="목표"><TargetInput name={l.name} value={l.target} /></td>
      <td data-label="부족">
        {l.target > 0
          ? <span className="stock-short"><progress max={l.target} value={Math.min(total, l.target)} aria-label={`${plain(l.name)} 목표 달성도`} /><span className={`num ${short > 0 ? 'danger' : 'ok'}`}>{short > 0 ? `−${fmt(short)}` : '충분'}</span></span>
          : <span className="faint">–</span>}
      </td>
      <td className="n num" data-label="가방">{r?.bag ? fmt(r.bag) : <span className="faint">–</span>}</td>
      <td className="n num" data-label="캐릭 창고">{r?.storage ? fmt(r.storage) : <span className="faint">–</span>}</td>
      <td className="n num" data-label="계정 창고">{r?.account ? fmt(r.account) : <span className="faint">–</span>}</td>
      <td data-label="보관 캐릭터">
        {holders.length === 0 ? <span className="faint">–</span> : (
          <details className="stock-holders">
            <summary>{holders.length}명{staleHolders > 0 && <span className="stock-warn" title="3일 넘게 읽지 못한 캐릭터가 있습니다"> ⚠</span>}</summary>
            <ul>
              {holders.map(h => (
                <li key={h.character} className={isStale(h.atUtc) ? 'stale' : ''}>
                  <span>{h.character}</span>
                  <span className="num">{[h.bag > 0 && `가방 ${fmt(h.bag)}`, h.storage > 0 && `창고 ${fmt(h.storage)}`].filter(Boolean).join(' · ')}</span>
                  <span className="muted">{ago(h.atUtc)}</span>
                </li>
              ))}
            </ul>
          </details>
        )}
      </td>
    </tr>
  );
}

/** 목표 개수 입력: 칸을 벗어나거나 Enter를 누르면 저장한다. 비우거나 0이면 목표를 지운다. */
function TargetInput({ name, value }: { name: string; value: number }) {
  const [draft, setDraft] = useState<string | null>(null);
  const commit = () => {
    if (draft === null) return;
    const n = draft.trim() === '' ? 0 : Math.floor(Number(draft));
    setDraft(null);
    if (Number.isFinite(n) && n >= 0 && n !== value) void setStockTarget(name, n);
  };
  return (
    <input className="stock-target num" type="number" inputMode="numeric" min={0} max={99999999} step={1} placeholder="목표" aria-label={`${plain(name)} 목표 개수`}
      value={draft ?? (value > 0 ? String(value) : '')}
      onChange={e => setDraft(e.target.value)} onBlur={commit}
      onKeyDown={e => { if (e.key === 'Enter') e.currentTarget.blur(); if (e.key === 'Escape') { setDraft(null); e.currentTarget.blur(); } }} />
  );
}
