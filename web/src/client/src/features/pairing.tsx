import { useEffect, useRef, useState } from 'react';
import QRCode from 'qrcode';
import { api, ApiError } from '../api/http';
import { keys, useMeta, useSession } from '../api/queries';
import type { LanShare } from '../api/types';
import { Dialog, Icon } from '../components/ui';
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
 * 📱 폰으로 보기 (FR-MB-10): LAN 모드를 켜고 접속 주소 QR을 보여 준다. 폰 카메라로 찍으면 바로 열린다.
 * 인증은 없다(요구사양 Q8): QR은 주소를 편하게 넘기는 용도다.
 */
export function PairDialog({ onClose }: { onClose: () => void }) {
  const meta = useMeta();
  const lan = meta.data?.data.lan;
  const [share, setShare] = useState<LanShare | null>(null);
  const [qr, setQr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const local = useSession().data?.kind === 'local';
  const toast = useUi(s => s.toast);

  const openFirewall = async () => {
    try {
      await api.post('/api/lan/firewall/open', {});
      toast('관리자 승인 창에서 "예"를 누르면 방화벽이 열립니다. 끝나면 폰에서 QR을 다시 찍어 보세요.', 'info');
      window.setTimeout(() => void loadShare(), 8000);
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '방화벽 스크립트를 실행하지 못했습니다', 'warn');
    }
  };

  const loadShare = async () => {
    try {
      const r = await api.get<LanShare>('/api/lan/share');
      const img = await QRCode.toDataURL(r.data.urlIp, { margin: 1, width: 240, errorCorrectionLevel: 'M' });
      setShare(r.data);
      setQr(img);
    } catch (e) {
      setErr(e instanceof ApiError ? e.message : '접속 주소를 만들지 못했습니다.');
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

  // LAN 주소가 바뀌면(와이파이를 옮김 등) QR도 새로 만든다
  const addrKey = lan?.active ? `${lan.hosts[0]}|${lan.mdnsName}|${lan.port}` : '';
  useEffect(() => {
    if (addrKey) void loadShare(); else { setShare(null); setQr(null); }
  }, [addrKey]); // eslint-disable-line react-hooks/exhaustive-deps

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
        <div className="qr-wrap">
          {qr ? <img className="qr" src={qr} alt="폰 카메라로 찍으면 열리는 접속 주소 QR 코드" width={148} height={148} /> : <div className="qr qr-empty" aria-hidden="true" />}
          <div>
            <p className="small muted">폰 카메라로 QR을 찍거나, 폰 브라우저에서 아래 주소를 여세요. 홈 화면에 추가하면 앱처럼 쓸 수 있습니다.</p>
            {share && <div className="url">{share.urlName ?? share.urlIp.split('?')[0]}</div>}
          </div>
        </div>
      )}
      {lan?.active && share?.firewall && (share.firewall.state === 'Missing' || share.firewall.state === 'Blocked') && (
        <div className="fw-note" role="note">
          <p className="warn-text small m0">
            {share.firewall.state === 'Blocked'
              ? 'Windows 방화벽 규칙이 폰의 접속을 막고 있습니다.'
              : 'Windows 방화벽에 폰 접속을 허용하는 규칙이 없습니다.'}
            {' '}폰에서 "응답하는 데 시간이 너무 오래 걸립니다"가 뜨면 방화벽 때문입니다.
          </p>
          {share.firewall.script
            ? (local
              ? <button type="button" className="btn" onClick={openFirewall}>방화벽 열기 (관리자 승인)</button>
              : <p className="faint small m0">이 PC에서 앱 옆의 allow-lan-firewall.bat를 실행해 주세요.</p>)
            : <p className="faint small m0">앱 배포 파일(zip)에 들어 있는 allow-lan-firewall.bat를 이 PC에서 실행해 주세요.</p>}
          <p className="faint small m0">개인 네트워크에서만 TCP {lan.port}, UDP 5353을 허용합니다. 이미 잘 열린다면 이 안내는 무시해도 됩니다.</p>
        </div>
      )}
      {err && <p className="warn-text small" role="alert">{err}</p>}
    </Dialog>
  );
}

/**
 * QR은 IP 주소로 열린다. 주소에 이름(n)이 있고 그 이름의 /api/ping이 1.5초 안에 같은 서버로 답하면 이름 주소로 옮겨 간다
 * (PC의 IP가 바뀌어도 같은 주소로 열리게) (FR-MB-13). 이 기기가 .local 이름을 못 풀면 그대로 IP 주소에 머문다.
 */
export function useNameHop() {
  const ran = useRef(false);
  useEffect(() => {
    if (ran.current) return;
    ran.current = true;
    const params = new URLSearchParams(window.location.search);
    const name = params.get('n');
    if (!name) return;
    params.delete('n');
    const rest = params.toString();
    const clean = window.location.pathname + (rest ? `?${rest}` : '') + window.location.hash;
    window.history.replaceState(null, '', clean);   // 주소의 n을 지운다
    if (window.location.hostname === name) return;
    void (async () => {
      const ctl = new AbortController();
      const t = window.setTimeout(() => ctl.abort(), 1500);
      try {
        const [mine, there] = await Promise.all([
          fetch('/api/ping').then(r => r.json()),
          fetch(`http://${name}:${window.location.port}/api/ping`, { signal: ctl.signal }).then(r => r.json()),
        ]);
        if (mine?.serverId && mine.serverId === there?.serverId) window.location.replace(`http://${name}:${window.location.port}${clean}`);
      } catch { /* 이 기기는 .local 이름을 못 푼다: IP 주소 그대로 */ }
      finally { window.clearTimeout(t); }
    })();
  }, []);
}
