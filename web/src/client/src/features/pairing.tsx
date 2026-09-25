import { useEffect, useRef, useState } from 'react';
import QRCode from 'qrcode';
import { api, ApiError } from '../api/http';
import { keys, keys5, useDevices, useMeta, useSession } from '../api/queries';
import type { Device, PairingStart } from '../api/types';
import { Dialog, Icon, Pill } from '../components/ui';
import { useNow } from '../hooks/layout';
import { queryClient } from '../lib/queryClient';
import { useUi } from '../state/ui';

const LAN_ERROR: Record<string, string> = {
  Pending: '네트워크를 확인하는 중입니다. 잠시 후 다시 눌러 주세요.',
  NotPrivate: '지금 네트워크가 공용으로 설정돼 있어 열지 않았습니다. Windows 설정에서 이 네트워크를 개인 네트워크로 바꿔 주세요.',
  ProfileUnknown: '네트워크 종류를 확인하지 못했습니다.',
  NoAddress: '연결된 네트워크가 없습니다.',
  PortInUse: '포트를 다른 프로그램이 쓰고 있습니다.',
  PortReserved: '포트가 Windows 예약 범위라 쓸 수 없습니다.',
  PortChanged: '다른 포트로 떠 있습니다. 앱을 다시 시작하면 켤 수 있습니다.',
  BindFailed: '연결을 열지 못했습니다.',
};

/**
 * 📱 폰으로 보기 (FR-MB-10): LAN 모드를 켜고 QR(IP 주소) + 6자리 코드 + 5분 카운트다운을 보여 준다.
 * 폰 카메라로 찍으면 입력 없이 연결된다. 아래에 연결된 기기 목록과 폐기 버튼(SEC-07)을 둔다.
 */
export function PairDialog({ onClose }: { onClose: () => void }) {
  const meta = useMeta();
  const lan = meta.data?.data.lan;
  const [start, setStart] = useState<PairingStart | null>(null);
  const [qr, setQr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const now = useNow();
  const seq = useRef(0);

  // 서버는 코드를 하나만 유지한다(새 코드가 이전 코드를 무효로 만든다). 가장 최근 요청의 결과만 보여 준다.
  const newCode = async () => {
    const my = ++seq.current;
    setErr(null);
    setBusy(true);
    try {
      const r = await api.post<PairingStart>('/api/pairing/start');
      const img = await QRCode.toDataURL(r.data.urlIp, { margin: 1, width: 240, errorCorrectionLevel: 'M' });
      if (my !== seq.current) return;
      setStart(r.data);
      setQr(img);
    } catch (e) {
      if (my === seq.current) setErr(e instanceof ApiError ? e.message : '페어링 코드를 만들지 못했습니다.');
    } finally {
      if (my === seq.current) setBusy(false);
    }
  };

  const enableLan = async () => {
    setBusy(true);
    setErr(null);
    try {
      await api.put('/api/lan', { enabled: true });
      await queryClient.invalidateQueries({ queryKey: keys.meta });
    } catch (e) {
      setErr(e instanceof ApiError ? e.message : 'LAN 모드를 켜지 못했습니다.');
    } finally { setBusy(false); }
  };

  const started = useRef(false);
  useEffect(() => {
    if (lan?.active && !started.current) { started.current = true; void newCode(); }
  }, [lan?.active]); // eslint-disable-line react-hooks/exhaustive-deps

  const left = start ? Math.max(0, Math.floor((Date.parse(start.expiresAt) - now) / 1000)) : 0;
  const expired = !!start && left === 0;

  return (
    <Dialog title="폰·태블릿으로 보기" onClose={onClose}>
      {!lan ? <p className="muted">불러오는 중…</p> : !lan.active ? (
        <>
          <p>폰·태블릿이 같은 와이파이에서 이 PC에 접속하려면 <b>LAN 모드</b>를 켜야 합니다. 처음 켤 때 Windows 방화벽 허용 창이 한 번 뜰 수 있습니다(개인 네트워크만 허용).</p>
          {lan.enabled && lan.error && <p className="warn-text small">{LAN_ERROR[lan.error] ?? lan.error}</p>}
          <p className="faint small">공용 와이파이(카페 등)에서는 켜지 마세요. 개인 네트워크에서만 실제로 열립니다.</p>
          <button type="button" className="btn primary" disabled={busy} onClick={enableLan}><Icon name="phone" />{busy ? '확인 중…' : 'LAN 모드 켜기'}</button>
        </>
      ) : (
        <>
          <div className="qr-wrap">
            {qr && !expired ? <img className="qr" src={qr} alt="폰 카메라로 찍으면 연결되는 QR 코드" width={148} height={148} /> : <div className="qr qr-empty" aria-hidden="true" />}
            <div>
              <p className="small muted">폰 카메라로 QR을 찍거나, 폰에서 주소를 열고 코드를 넣으세요.</p>
              <div className="code num" aria-label="페어링 코드">{start?.code ?? '······'}</div>
              <p className={`small ${expired ? 'warn-text' : 'faint'}`}>{expired ? '코드가 만료됐습니다' : `${Math.floor(left / 60)}:${String(left % 60).padStart(2, '0')} 후 만료 · 1회용`}</p>
              {start && <div className="url">{lan.mdnsName ? `http://${lan.mdnsName}:${lan.port}` : start.urlIp.split('/pair')[0]}</div>}
            </div>
          </div>
          <div className="acts left"><button type="button" className="btn" onClick={newCode} disabled={busy}><Icon name="refresh" />새 코드</button></div>
          <DeviceList poll />
        </>
      )}
      {err && <p className="warn-text small" role="alert">{err}</p>}
    </Dialog>
  );
}

/** 연결된 기기 (SEC-07): 폐기하면 그 기기의 SSE가 즉시 닫힌다. 게임 PC에서만 보인다(SEC-08). */
export function DeviceList({ poll = false }: { poll?: boolean }) {
  const q = useDevices();
  const me = useSession().data?.deviceId;
  const toast = useUi(s => s.toast);
  // 페어링 대화상자가 열린 동안에만 새로 연결된 기기가 바로 보이게 짧게 다시 읽는다
  useEffect(() => {
    if (!poll) return;
    const t = window.setInterval(() => void queryClient.invalidateQueries({ queryKey: keys5.devices }), 3000);
    return () => window.clearInterval(t);
  }, [poll]);
  const revoke = async (d: Device) => {
    if (!window.confirm(`${d.name} 연결을 해제할까요? 그 기기는 QR을 다시 찍어야 합니다.`)) return;
    try {
      await api.del(`/api/pairing/devices/${d.id}`);
      await queryClient.invalidateQueries({ queryKey: keys5.devices });
      toast(`${d.name} 연결을 해제했습니다`, 'ok');
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '해제하지 못했습니다', 'warn');
    }
  };
  const list = q.data?.data ?? [];
  return (
    <div>
      <h4 className="sub-title">연결된 기기 {list.length ? `(${list.length})` : ''}</h4>
      {list.map(d => (
        <div className="dev" key={d.id}>
          <span>
            {d.name} <Pill tone={d.kind === 'lan' ? 'info' : 'plain'}>{d.kind === 'lan' ? '폰·태블릿' : 'PC 브라우저'}</Pill>{d.id === me && <Pill tone="ok">지금 이 기기</Pill>}
            <span className="faint small"> · 최근 {new Date(d.lastSeenAt).toLocaleString('ko-KR', { dateStyle: 'short', timeStyle: 'short' })}{d.expiresAt ? ` · ${new Date(d.expiresAt).toLocaleDateString('ko-KR')} 만료` : ''}</span>
          </span>
          {/* 지금 쓰는 기기를 해제하면 바로 연결이 끊긴다. 실수를 막으려고 버튼을 두지 않는다 */}
          {d.id !== me && <button type="button" className="btn" onClick={() => revoke(d)}>해제</button>}
        </div>
      ))}
    </div>
  );
}

/**
 * 폰 쪽 페어링 착륙 (FR-MB-10·11·13). 세션 없이 열린다.
 * ① QR은 IP 주소로 열린다 ② 이름 주소(n)의 /api/ping이 1.5초 안에 같은 서버로 답하면 그 주소로 옮겨 가서 페어링한다
 * (쿠키는 주소마다 따로라, 이름으로 페어링하면 PC의 IP가 바뀌어도 이어진다) ③ 아니면 지금 주소에서 페어링한다.
 */
export function PairLanding() {
  const params = new URLSearchParams(window.location.search);
  const [code, setCode] = useState(params.get('code') ?? '');
  const name = params.get('n');
  const [state, setState] = useState<'checking' | 'form' | 'sending' | 'done' | 'error'>(code ? 'checking' : 'form');
  const [msg, setMsg] = useState<string | null>(null);
  const ran = useRef(false);

  const confirm = async (c: string) => {
    setState('sending');
    try {
      const res = await fetch('/api/pairing/confirm', {
        method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code: c }),
      });
      const body = await res.json().catch(() => null) as { error?: { message?: string } } | null;
      if (!res.ok) throw new Error(body?.error?.message ?? `연결하지 못했습니다 (${res.status})`);
      window.history.replaceState(null, '', '/');   // 코드를 주소에서 지운다
      setState('done');
    } catch (e) {
      window.history.replaceState(null, '', '/pair');   // 실패해도 코드를 주소에 남기지 않는다
      setMsg((e as Error).message);
      setState('error');
    }
  };

  useEffect(() => {
    // 1회용 코드라 한 번만 확인한다 (개발 모드 StrictMode의 두 번 실행 포함)
    if (state !== 'checking' || ran.current) return;
    ran.current = true;
    (async () => {
      if (name && window.location.hostname !== name) {
        try {
          const ctl = new AbortController();
          const t = window.setTimeout(() => ctl.abort(), 1500);
          const [mine, there] = await Promise.all([
            fetch('/api/ping').then(r => r.json()),
            fetch(`http://${name}:${window.location.port}/api/ping`, { signal: ctl.signal }).then(r => r.json()),
          ]);
          window.clearTimeout(t);
          if (mine?.serverId && mine.serverId === there?.serverId) {
            window.location.replace(`http://${name}:${window.location.port}/pair?code=${encodeURIComponent(code)}`);
            return;
          }
        } catch { /* 이 기기는 .local 이름을 못 푼다: IP 주소로 페어링 */ }
      }
      await confirm(code);
    })();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <main className="auth">
      <div className="card auth-card">
        <Icon name="phone" size={28} />
        <h1>MobiMate 연결</h1>
        {state === 'checking' || state === 'sending' ? <p>PC와 연결하는 중…</p>
          : state === 'done' ? <>
            <p>연결됐습니다. 이 기기는 30일 동안 다시 연결하지 않아도 됩니다.</p>
            <p className="small muted"><b>홈 화면에 추가</b>하면 앱처럼 바로 열 수 있습니다. iPhone·iPad: 공유 버튼 → 홈 화면에 추가 / 안드로이드: 메뉴 → 홈 화면에 추가.</p>
            <button type="button" className="btn primary" onClick={() => window.location.replace('/')}>시작하기</button>
          </> : <>
            {state === 'error' && <p className="warn-text" role="alert">{msg}</p>}
            <p>PC 화면의 <b>📱 폰으로 보기</b>에 나온 6자리 코드를 넣어 주세요.</p>
            <form className="pair-form" onSubmit={e => { e.preventDefault(); if (/^\d{6}$/.test(code)) void confirm(code); }}>
              <input inputMode="numeric" autoComplete="one-time-code" pattern="\d{6}" maxLength={6} value={code} aria-label="페어링 코드 6자리"
                onChange={e => setCode(e.target.value.replace(/\D/g, ''))} />
              <button type="submit" className="btn primary" disabled={!/^\d{6}$/.test(code)}>연결</button>
            </form>
          </>}
      </div>
    </main>
  );
}
