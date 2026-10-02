import { useState } from 'react';
import { keys, useCharacters } from '../api/queries';
import { api, ApiError } from '../api/http';
import type { AccountGroup, CharacterCard, CoinView, HomeworkAuto } from '../api/types';
import { CardHead, Dialog, ErrorCard, Fresh, Icon, JobIcon, Pill, Skeleton } from '../components/ui';
import { useNow } from '../hooks/layout';
import { fmt } from '../lib/format';
import { queryClient } from '../lib/queryClient';
import { scoreClass } from '../lib/score';
import { useDevice } from '../state/device';
import { useRouter } from '../state/router';
import { useUi } from '../state/ui';

/** "방금 / 3시간 전 / 2일 전": 마지막으로 접속했을 때 본 값이 얼마나 오래됐는지 */
export function lastSeenText(iso: string, now = Date.now()): string {
  const m = Math.max(0, Math.floor((now - new Date(iso).getTime()) / 60000));
  if (m < 1) return '방금';
  if (m < 60) return `${m}분 전`;
  const h = Math.floor(m / 60);
  return h < 48 ? `${h}시간 전` : `${Math.floor(h / 24)}일 전`;
}

/** 가득 찰 때까지 남은 시간: "3시간 20분" / "2일 4시간" */
export function waitText(minutes: number): string {
  const m = Math.max(0, Math.round(minutes));
  if (m < 60) return `${m}분`;
  const h = Math.floor(m / 60);
  if (h < 48) return m % 60 ? `${h}시간 ${m % 60}분` : `${h}시간`;
  return `${Math.floor(h / 24)}일 ${h % 24}시간`;
}

/** 카드 상태: 충전 재화가 가득이면 빨강, 80% 이상이면 노랑, 데카·M캐시가 어긋나면 진회색 (우선순위 순) */
export type CardTone = 'red' | 'yellow' | 'gray' | 'none';
export function cardTone(c: Pick<CharacterCard, 'silver' | 'tribute' | 'stale'>): CardTone {
  if (c.silver.level === 'full' || c.tribute.level === 'full') return 'red';
  if (c.silver.level === 'near' || c.tribute.level === 'near') return 'yellow';
  return c.stale ? 'gray' : 'none';
}

/** 멤버십 남은 시간: "27일 3시간" / "5시간 20분". 이미 끝났으면 null */
export function membershipLeft(expiresAt: string | null, now = Date.now()): { text: string; urgent: boolean } | null {
  if (!expiresAt) return null;
  const ms = new Date(expiresAt).getTime() - now;
  if (ms <= 0) return null;
  const min = Math.floor(ms / 60000), h = Math.floor(min / 60), d = Math.floor(h / 24);
  const text = d > 0 ? `${d}일 ${h % 24}시간` : h > 0 ? `${h}시간 ${min % 60}분` : `${Math.max(1, min)}분`;
  return { text, urgent: ms <= 3 * 86_400_000 };   // 3일 이내면 붉은색으로 경고
}

type SortMode = 'combat' | 'urgency' | 'manual';
const sortMembers = (list: CharacterCard[], mode: SortMode) =>
  [...list].sort((a, b) => (mode === 'urgency' ? b.urgency - a.urgency || b.combat - a.combat : b.combat - a.combat));
const sortAccounts = (list: AccountGroup[], mode: SortMode) =>
  mode === 'manual'
    ? [...list].sort((a, b) => (a.manualRank < 0 ? 1e9 : a.manualRank) - (b.manualRank < 0 ? 1e9 : b.manualRank))   // 정하지 않은 계정은 뒤에(서버 순서 유지)
    : mode === 'urgency'
    ? [...list].sort((a, b) => Math.max(...b.members.map(m => m.urgency)) - Math.max(...a.members.map(m => m.urgency)) || b.topCombat - a.topCombat)
    : list;   // 서버 순서: 지금 접속한 계정 → 계정 안 최고 전투력 순

/**
 * 내 캐릭터 전체 현황 (FR-AL, 메인 화면): 같은 계정(데카·M캐시가 같은 캐릭터)끼리 묶어 보여 준다.
 * 게임은 지금 접속한 캐릭터만 알려 주므로 다른 캐릭터는 마지막으로 접속했을 때 본 값이다.
 * 은동전·마족 공물은 마지막 값에서 시간이 지난 만큼 충전된 예상 보유량이다 — 가득 차면 충전이 멈추므로 먼저 접속할 캐릭터를 고르는 근거가 된다.
 */
export function CharactersView() {
  const q = useCharacters();
  const now = useNow(30_000);
  const sort = useDevice(s => s.charSort);
  const setDevice = useDevice(s => s.set);
  const [editing, setEditing] = useState(false);
  const toast = useUi(s => s.toast);
  if (q.isPending) return <><Skeleton /><Skeleton /></>;
  if (q.isError) return <ErrorCard error={q.error} onRetry={() => q.refetch()} />;
  const data = q.data.data;
  const total = data.accounts.reduce((n, a) => n + a.members.length, 0);
  const ordered = sortAccounts(data.accounts, sort);
  const moveAccount = async (list: AccountGroup[], i: number, dir: -1 | 1) => {
    const ids = list.map(a => a.id);
    [ids[i], ids[i + dir]] = [ids[i + dir], ids[i]];
    try {
      await api.put('/api/accounts/order', { order: ids });
      await queryClient.invalidateQueries({ queryKey: keys.characters });
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '순서를 저장하지 못했습니다', 'warn');
    }
  };

  return (
    <>
      <div className="view-head">
        <h2>내 캐릭터 <span className="num muted">({total}명)</span></h2>
        <span className="sub">
          <span className="seg" role="group" aria-label="정렬">
            <button type="button" aria-pressed={sort === 'combat'} onClick={() => setDevice({ charSort: 'combat' })}>전투력순</button>
            <button type="button" aria-pressed={sort === 'urgency'} onClick={() => setDevice({ charSort: 'urgency' })}>접속 시급 순</button>
            <button type="button" aria-pressed={sort === 'manual'} onClick={() => setDevice({ charSort: 'manual' })}>내 순서</button>
          </span>
          <button type="button" className="btn" onClick={() => setEditing(true)}>계정 편집</button>
          <Fresh at={q.dataUpdatedAt} />
        </span>
      </div>
      {total === 0 ? (
        <div className="card muted">아직 확인한 캐릭터가 없습니다. 게임에 접속하면 여기에 모입니다.</div>
      ) : (
        <>
          {ordered.map((a, i) => (
            <AccountSection key={a.id} a={a} sort={sort} now={now}
              move={sort === 'manual' && ordered.length > 1 ? { up: i > 0 ? () => void moveAccount(ordered, i, -1) : null, down: i < ordered.length - 1 ? () => void moveAccount(ordered, i, 1) : null } : undefined} />
          ))}
          {sort === 'manual' && ordered.length > 1 && <p className="faint small m0">▲▼로 계정 순서를 정하세요. 이 순서는 모든 기기에 같이 적용됩니다.</p>}
          <p className="faint small m0">
            지금 접속한 캐릭터만 실시간입니다. 다른 캐릭터는 마지막으로 접속했을 때 본 값이고, 은동전·마족 공물은 그 뒤 충전된 예상 개수입니다.
            데카·M캐시가 같은 캐릭터는 같은 계정으로 묶입니다.
          </p>
        </>
      )}
      {editing && <AssignDialog data={data.accounts} onClose={() => setEditing(false)} />}
    </>
  );
}

function AccountSection({ a, sort, now, move }: { a: AccountGroup; sort: SortMode; now: number; move?: { up: (() => void) | null; down: (() => void) | null } }) {
  const left = membershipLeft(a.membership.expiresAt, now);
  const servers = [...new Set(a.members.map(m => m.realm))];   // 서버별로 캐릭터를 만든 계정은 서버를 같이 보여 준다
  return (
    <section className="acct" aria-label={`${a.name} 계정`}>
      <div className="acct-h">
        <h3>{a.solo ? a.name : `${a.name} 계정`} <span className="faint small num">{a.members.length}명</span>
          {move && (
            <span className="acct-move">
              <button type="button" className="icon-btn" aria-label={`${a.name} 계정 위로`} disabled={!move.up} onClick={move.up ?? undefined}>▲</button>
              <button type="button" className="icon-btn" aria-label={`${a.name} 계정 아래로`} disabled={!move.down} onClick={move.down ?? undefined}>▼</button>
            </span>
          )}
        </h3>
        <span className="acct-meta small muted">
          {servers.length > 1 && <>서버 {servers.join('·')} · </>}데카 <b className="num">{fmt(a.deca)}</b> · M캐시 <b className="num">{fmt(a.mcash)}</b>
          {left ? <> · <span className={left.urgent ? 'danger-text' : ''}>멤버십 {left.text} 남음</span></> : <> · 멤버십 미등록</>}
        </span>
      </div>
      <div className="chars">
        {sortMembers(a.members, sort).map(c => <CharacterCardView key={c.key} c={c} now={now} />)}
      </div>
    </section>
  );
}

function CoinChip({ label, c }: { label: string; c: CoinView }) {
  return (
    <span className={`coin ${c.level}`} title={c.level === 'full' ? '충전이 멈춰 있습니다' : c.level === 'near' ? '곧 가득 찹니다' : undefined}>
      {label} <b className="num">{fmt(c.expected)}</b><span className="num faint">/{fmt(c.cap)}</span>
    </span>
  );
}

/** 자동 판정되는 숙제 칩 (전체 탭 카드): 짧은 이름 + 아이콘만. 완료 ✔ 초록 / 확인된 미완료 ○ 주황 테두리 / 아직 확인 못 함 ○ 점선 */
function HwChip({ h }: { h: HomeworkAuto }) {
  const text = h.state === 'done' ? '완료' : h.state === 'todo' ? '미완료 (확인됨)' : '미완료 (아직 확인 전)';
  return (
    <span className={`hw-chip ${h.state}`} title={`${h.title}: ${h.evidence ?? text}`} aria-label={`${h.title} ${text}`}>
      <Icon name={h.state === 'done' ? 'check' : 'clock'} size={11} />{h.title}
    </span>
  );
}

function CharacterCardView({ c, now }: { c: CharacterCard; now: number }) {
  const go = useRouter(s => s.go);
  const toast = useUi(s => s.toast);
  const [asking, setAsking] = useState(false);
  const [busy, setBusy] = useState(false);
  const name = charName(c);   // 게임이 캐릭터 이름을 주지 않아 서버·직업으로 구분한다
  const tone = cardTone(c);
  const seen = c.isCurrent ? '접속 중' : lastSeenText(c.lastSeen, now);
  const days = Math.floor((now - new Date(c.lastSeen).getTime()) / 86_400_000);
  const notes: string[] = [];
  if (c.silver.level === 'full') notes.push(`은동전이 가득 차 충전이 멈췄어요 (${fmt(c.silver.expected)}/${fmt(c.silver.cap)})`);
  else if (c.silver.level === 'near') notes.push(`은동전이 ${waitText(c.silver.minutesToFull)} 뒤 가득 차요`);
  if (c.tribute.level === 'full') notes.push(`마족 공물이 가득 차 충전이 멈췄어요 (${fmt(c.tribute.expected)}/${fmt(c.tribute.cap)})`);
  else if (c.tribute.level === 'near') notes.push(`마족 공물이 ${waitText(c.tribute.minutesToFull)} 뒤 가득 차요`);
  if (tone === 'red') notes.push('지금 접속해서 사용하세요.');
  if (c.stale) notes.push(`데카·M캐시가 같은 계정의 다른 캐릭터와 달라요. ${days >= 1 ? `${days}일째 접속하지 않아` : '마지막 접속 이후 값이 바뀌어'} 아직 동기화되지 않은 것입니다.`);

  const body = (
    <>
      <CardHead
        title={<span className="cc-name">{name}
          {!c.isCurrent && <button type="button" className="cc-del" aria-label={`${name} 삭제`} title="이 캐릭터 삭제" onClick={() => setAsking(true)}><Icon name="x" size={12} /></button>}
        </span>}
        right={
          <span className="cc-right">
            {c.isCurrent ? <Pill tone="ok">접속 중</Pill> : <span className={`seen small ${days >= 7 ? 'danger-text' : 'faint'}`}>{seen} 접속</span>}
            <span className="coins"><CoinChip label="은동전" c={c.silver} /><CoinChip label="마족공물" c={c.tribute} /></span>
          </span>
        }
      />
      <div className="cc-job"><JobIcon job={c.job} size={30} /><span className="cc-jt"><span>{c.nickname && <span className="realm-chip">{c.realm}</span>}{c.job} Lv.{c.level}</span>{c.title && <span className="cc-title">“{c.title}”</span>}</span></div>
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
        <span className="muted">M캐시 <b className="num">{fmt(c.mcash)}</b></span>
      </div>
      {c.homework.length > 0 && <div className="cc-hw" aria-label="자동 판정 숙제">{c.homework.map(h => <HwChip key={h.id} h={h} />)}</div>}
      {notes.length > 0 && <ul className="cc-notes small">{notes.map(n => <li key={n}>{n}</li>)}</ul>}
      {asking && (
        <Dialog title="캐릭터 삭제" onClose={() => setAsking(false)}
          actions={<>
            <button type="button" className="btn" disabled={busy} onClick={() => setAsking(false)}>아니오</button>
            <button type="button" className="btn danger" disabled={busy} onClick={() => void remove()}>예</button>
          </>}>
          <p>“{name}”({c.job} Lv.{c.level}) 캐릭터를 목록에서 삭제할까요?</p>
          <p className="small muted m0">저장된 기록이 지워지며, 이 캐릭터로 다시 접속하면 새로 기록됩니다.</p>
        </Dialog>
      )}
    </>
  );
  const remove = async () => {
    setBusy(true);
    try {
      await api.del(`/api/characters?key=${encodeURIComponent(c.key)}`);
      setAsking(false);
      await queryClient.invalidateQueries({ queryKey: keys.characters });
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '캐릭터를 지우지 못했습니다', 'warn');
      setBusy(false);
    }
  };
  const cls = `card char tone-${tone} ${c.isCurrent ? 'cur' : ''}`;
  // 지금 접속한 캐릭터는 누르면 자세한 개요로 간다. 나머지는 게임이 정보를 주지 않아 이동할 곳이 없다.
  return c.isCurrent
    ? <a className={`${cls} link`} href="/overview" aria-label={`${name} 개요 보기`} onClick={e => { if (e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return; e.preventDefault(); go('overview'); }}>{body}</a>
    : <article className={cls}>{body}</article>;
}

/** 카드·목록에서 쓰는 이름: 캐릭터명이 없으면 서버 · 직업(같은 서버·직업의 다른 캐릭터는 #2) */
const charName = (c: Pick<CharacterCard, 'nickname' | 'realm' | 'job' | 'variant'>) =>
  c.nickname ?? `${c.realm} · ${c.job}${c.variant > 1 ? ` #${c.variant}` : ''}`;

/** 계정 편집: 자동으로 묶인 계정이 틀렸을 때 캐릭터를 직접 다른 계정에 묶거나 따로 뺀다. 직접 정한 건 자동으로 바뀌지 않는다. */
function AssignDialog({ data, onClose }: { data: AccountGroup[]; onClose: () => void }) {
  const toast = useUi(s => s.toast);
  const [busy, setBusy] = useState(false);
  const [splitAsk, setSplitAsk] = useState(false);
  const chars = data.flatMap(a => a.members.map(m => ({ ...m, account: a.id })));
  const options = data.map(a => ({ id: a.id, label: a.solo ? `${a.name} (혼자)` : `${a.name} 계정` }));

  const change = async (character: string, account: string) => {
    setBusy(true);
    try {
      await api.put('/api/accounts/assign', { character, account });
      await queryClient.invalidateQueries({ queryKey: keys.characters });
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '계정을 바꾸지 못했습니다', 'warn');
    } finally { setBusy(false); }
  };

  // 같은 서버·직업의 다른 계정 캐릭터가 한 카드로 합쳐졌을 때: 지금 접속한 캐릭터를 따로 뗀다
  const split = async (key: string) => {
    setBusy(true);
    try {
      await api.post('/api/characters/split', { key });
      setSplitAsk(false);
      await queryClient.invalidateQueries({ queryKey: keys.characters });
      toast('다른 캐릭터로 나눴습니다', 'ok');
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '나누지 못했습니다', 'warn');
    } finally { setBusy(false); }
  };

  return (
    <Dialog title="계정 편집" onClose={onClose}>
      <p className="small muted">
        데카·M캐시가 같았던 캐릭터는 자동으로 같은 계정이 됩니다. 잘못 묶였거나 묶이지 않은 캐릭터는 여기서 직접 정할 수 있고, 직접 정한 캐릭터는 자동으로 바뀌지 않습니다.
      </p>
      <div className="assign-list">
        {chars.map(c => (
          <label key={c.key} className="assign-row">
            <span><JobIcon job={c.job} size={15} /> {c.nickname ?? `${c.realm}${c.variant > 1 ? ` #${c.variant}` : ''}`} <span className="faint small">{c.nickname ? `${c.realm} · ` : ''}{c.job} Lv.{c.level}</span></span>
            <select value={c.account} disabled={busy} aria-label={`${c.nickname ?? c.realm}${c.variant > 1 ? ` #${c.variant}` : ''} 소속 계정`} onChange={e => void change(c.key, e.target.value)}>
              {options.map(o => <option key={o.id} value={o.id}>{o.label}</option>)}
              <option value="new">따로 빼기 (새 계정)</option>
            </select>
            {c.isCurrent && (splitAsk
              ? <span className="del-confirm"><button type="button" className="btn" disabled={busy} onClick={() => setSplitAsk(false)}>아니오</button><button type="button" className="btn danger" disabled={busy} onClick={() => void split(c.key)}>예, 나눕니다</button></span>
              : <button type="button" className="btn" disabled={busy} title="다른 계정의 같은 서버·직업 캐릭터가 이 카드에 합쳐져 있을 때" onClick={() => setSplitAsk(true)}>다른 캐릭터로 나누기</button>)}
          </label>
        ))}
      </div>
    </Dialog>
  );
}
