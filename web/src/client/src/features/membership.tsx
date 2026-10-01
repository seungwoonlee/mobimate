import { useState } from 'react';
import { api, ApiError } from '../api/http';
import { keys, useCharacters } from '../api/queries';
import { Dialog } from '../components/ui';
import { useNow } from '../hooks/layout';
import { queryClient } from '../lib/queryClient';
import { useUi } from '../state/ui';
import { membershipLeft } from './characters';

/**
 * 멤버십 표시 (최상단, 항상 보인다): 지금 접속한 캐릭터의 계정에 등록된 멤버십 남은 시간을 자동으로 센다.
 * 3일 이내면 붉은색으로 경고하고, 등록이 없거나 끝났으면 "멤버십 미등록"으로 보여 눌러서 등록하게 한다.
 * 멤버십 여부는 게임이 알려 주지 않아 사용자가 게임 캐시샵 메뉴의 남은 기간을 입력해 등록한다. 계정 단위로 적용된다.
 */
export function MembershipChip() {
  const q = useCharacters();
  const now = useNow(60_000);
  const [open, setOpen] = useState(false);
  const data = q.data?.data;
  const acct = data?.accounts.find(a => a.id === data.currentAccountId);
  const current = acct?.members.find(m => m.isCurrent);
  if (!acct || !current) return null;
  const left = membershipLeft(acct.membership.expiresAt, now);
  return (
    <>
      <button type="button" className={`mem-chip ${left ? (left.urgent ? 'urgent' : 'ok') : 'none'}`} onClick={() => setOpen(true)}
        aria-label={left ? `멤버십 ${left.text} 남음. 눌러서 남은 기간 고치기` : '멤버십 미등록. 눌러서 등록하기'}>
        {left ? <>멤버십 <b>{left.text}</b> 남음{left.urgent && ' · 곧 끝나요'}</> : <>멤버십 미등록</>}
      </button>
      {open && <MembershipDialog character={current.key} onClose={() => setOpen(false)} />}
    </>
  );
}

function MembershipDialog({ character, onClose }: { character: string; onClose: () => void }) {
  const toast = useUi(s => s.toast);
  const [days, setDays] = useState('');
  const [busy, setBusy] = useState(false);

  const save = async (d: number) => {
    setBusy(true);
    try {
      await api.put('/api/membership', { character, days: d });
      await queryClient.invalidateQueries({ queryKey: keys.characters });
      toast(d === 0 ? '멤버십 등록을 해제했습니다' : `멤버십 ${d}일 남음으로 등록했습니다`, 'ok');
      onClose();
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '멤버십을 저장하지 못했습니다', 'warn');
    } finally { setBusy(false); }
  };

  const d = Number(days || 0);
  const valid = Number.isInteger(d) && d >= 1 && d <= 400;
  return (
    <Dialog title="멤버십 남은 일수 등록" onClose={onClose}
      actions={<>
        <button type="button" className="btn" onClick={() => void save(0)} disabled={busy}>등록 해제</button>
        <button type="button" className="btn" onClick={onClose}>취소</button>
        <button type="submit" form="mem-form" className="btn primary" disabled={busy || !valid}>저장</button>
      </>}>
      <form id="mem-form" className="form" onSubmit={e => { e.preventDefault(); if (valid) void save(d); }}>
        <p className="small muted">
          <b>게임 안 캐시샵 메뉴</b>에서 멤버십 <b>남은 일수</b>를 확인해 그 숫자만 입력해 주세요. 멤버십은 계정 단위라 같은 계정의 모든 캐릭터에 적용됩니다.
          남은 시간은 <b>오늘 + 남은 일수 − 1일</b>이 지난 뒤 첫 <b>새벽 6시</b>까지로 계산해 자동으로 줄어듭니다.
          멤버십이면 은동전은 150개, 마족 공물은 15개까지, 미가입이면 100개 / 10개까지 충전됩니다.
        </p>
        <label>남은 일수 <input inputMode="numeric" value={days} onChange={e => setDays(e.target.value.replace(/\D/g, '').slice(0, 3))} placeholder="예: 27" autoFocus /></label>
      </form>
    </Dialog>
  );
}
