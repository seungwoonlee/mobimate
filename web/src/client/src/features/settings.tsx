import { useEffect, useState } from 'react';
import { useLayout } from '../hooks/layout';
import { useQuery } from '@tanstack/react-query';
import { api, ApiError, session } from '../api/http';
import { keys, useMeta, useSettings } from '../api/queries';
import { DeviceList } from './pairing';
import type { LanView } from '../api/types';
import { CardHead, Pill } from '../components/ui';
import { queryClient } from '../lib/queryClient';
import { useDevice, type Theme } from '../state/device';
import { useUi } from '../state/ui';

const LAN_ERROR: Record<string, string> = {
  Pending: '네트워크 확인 중',
  NotPrivate: '공용 네트워크라 열지 않았습니다',
  ProfileUnknown: '네트워크 종류를 확인하지 못했습니다',
  NoAddress: '연결된 네트워크가 없습니다',
  PortInUse: '포트를 다른 프로그램이 쓰고 있습니다',
  PortReserved: '포트가 예약 범위라 쓸 수 없습니다',
  PortChanged: '다른 포트로 떠 있습니다 · 앱을 다시 시작하면 켤 수 있습니다',
  BindFailed: '연결을 열지 못했습니다',
};

/** 설정 (FR-ST, FR-MB-16). 이 기기 설정은 localStorage, PC 서버 설정은 서버에 둔다. 기기 관리·페어링은 S5. */
export function SettingsView() {
  const dev = useDevice();
  const meta = useMeta();
  const me = useQuery({ queryKey: ['session'], queryFn: () => session() });
  const local = me.data?.kind === 'local';

  return (
    <>
      <div className="view-head"><h2>설정</h2></div>
      <div className="two">
        <div className="card">
          <CardHead title="이 기기" />
          <Row label="테마">
            <Seg value={dev.theme} options={[['system', '시스템'], ['dark', '다크'], ['light', '라이트']]} onChange={v => dev.set({ theme: v as Theme })} />
          </Row>
          <Row label="글자 크기">
            <Seg value={String(dev.scale)} options={[['1', '보통'], ['1.15', '크게'], ['1.3', '아주 크게']]} onChange={v => dev.set({ scale: Number(v) as 1 | 1.15 | 1.3 })} />
          </Row>
          <Row label="좁은 화면 글랜스 모드">
            <Seg value={dev.glance ? 'on' : 'off'} options={[['on', '켜기'], ['off', '끄기']]} onChange={v => dev.set({ glance: v === 'on' })} />
          </Row>
          <p className="faint small">이 기기에만 저장됩니다. 기기: {me.data?.deviceName ?? '…'}</p>
          <ScreenInfo />
        </div>
        <div className="card">
          <CardHead title="PC 서버" />
          <div className="kv"><span className="muted">버전</span><span className="num">{meta.data?.data.version ?? '…'}</span></div>
          {meta.data && <LanRow lan={meta.data.data.lan} canChange={local} />}
          {meta.data?.data.wpfRunning && <p className="warn-text small">WPF판 MobiMate가 함께 실행 중입니다. 게임 CLI를 같이 쓰므로 조회가 느려질 수 있습니다.</p>}
          <ServerSettings canChangeCli={local} />
        </div>
      </div>
      {local && <div className="card"><DeviceList /></div>}
    </>
  );
}

function LanRow({ lan, canChange }: { lan: LanView; canChange: boolean }) {
  const [busy, setBusy] = useState(false);
  const toast = useUi(s => s.toast);
  const toggle = async () => {
    setBusy(true);
    try {
      await api.put('/api/lan', { enabled: !lan.enabled });
      await queryClient.invalidateQueries({ queryKey: keys.meta });
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'LAN 모드를 바꾸지 못했습니다', 'warn');
    } finally {
      setBusy(false);
    }
  };
  const addr = lan.active ? `${lan.mdnsName ?? lan.hosts[0]}:${lan.port}` : null;
  return (
    <>
      <div className="kv">
        <span className="muted">폰·태블릿 접속 (LAN)</span>
        <span>{lan.active ? <Pill tone="ok">켜짐 · 개인 네트워크</Pill> : lan.enabled ? <Pill tone="warn">대기</Pill> : <Pill>꺼짐</Pill>}</span>
      </div>
      {addr && <div className="kv"><span className="muted">주소</span><span className="num">{addr}</span></div>}
      {lan.enabled && !lan.active && lan.error && <p className="warn-text small">{LAN_ERROR[lan.error] ?? lan.error}</p>}
      {canChange
        ? <button type="button" className="btn mt-8" disabled={busy} onClick={toggle}>{lan.enabled ? 'LAN 모드 끄기' : 'LAN 모드 켜기'}</button>
        : <p className="faint small">LAN 모드는 게임 PC에서만 바꿀 수 있습니다.</p>}
      <p className="faint small">공용 와이파이에서는 켜지 마세요. 개인 네트워크에서만 실제로 열립니다.</p>
    </>
  );
}

/** PC 서버 설정 (FR-ST): 자동 갱신 최대 주기, 자동 이모티콘 기본값, 게임 CLI 경로(게임 PC 전용, SEC-08·09) */
function ServerSettings({ canChangeCli }: { canChangeCli: boolean }) {
  const q = useSettings();
  const toast = useUi(s => s.toast);
  const [cli, setCli] = useState<string | null>(null);
  const s = q.data?.data;
  if (!s) return null;
  const save = async (body: Record<string, unknown>) => {
    try {
      await api.put('/api/settings', body);
      await queryClient.invalidateQueries({ queryKey: keys.settings });
      toast('설정을 저장했습니다', 'ok');
      return true;
    } catch (e) {
      toast(e instanceof ApiError ? e.message : '설정을 저장하지 못했습니다', 'warn');
      return false;
    }
  };
  return (
    <>
      <Row label="자동 갱신 최대 주기">
        <Seg value={String(s.maxRefreshSec)} options={[['60', '1분'], ['300', '5분'], ['900', '15분']]} onChange={v => void save({ maxRefreshSec: Number(v) })} />
      </Row>
      <Row label="채팅 자동 이모티콘 기본값">
        <Seg value={s.autoEmoteDefault ? 'on' : 'off'} options={[['on', '켜기'], ['off', '끄기']]} onChange={v => void save({ autoEmoteDefault: v === 'on' })} />
      </Row>
      <div className="kv wrap">
        <span className="muted">게임 CLI {s.cliAvailable ? <Pill tone="ok">찾음</Pill> : <Pill tone="danger">없음</Pill>}</span>
        <span className="faint small path">{s.cliPath ?? '—'}</span>
      </div>
      {canChangeCli && (cli === null
        ? <button type="button" className="btn" onClick={() => setCli(s.cliPath ?? '')}>CLI 경로 바꾸기</button>
        : <form className="form" onSubmit={async e => { e.preventDefault(); if (await save({ cliPath: cli.trim() })) setCli(null); }}>
            <label>MabinogiMobile_CLI.exe 전체 경로 <input value={cli} onChange={e => setCli(e.target.value)} placeholder="C:\\...\\MabinogiMobile_CLI.exe" /></label>
            <div className="acts"><button type="button" className="btn" onClick={() => setCli(null)}>취소</button><button type="submit" className="btn primary" disabled={!cli.trim()}>저장</button></div>
          </form>)}
    </>
  );
}

/** 실기 점검 M8: 이 기기의 CSS 화면 크기·배율·크기 클래스·입력 방식 */
function ScreenInfo() {
  const l = useLayout();
  const [, force] = useState(0);
  useEffect(() => {
    const on = () => force(n => n + 1);
    window.addEventListener('resize', on);
    return () => window.removeEventListener('resize', on);
  }, []);
  return (
    <p className="faint small num" aria-label="화면 정보">
      화면 {window.innerWidth} × {window.innerHeight} · 배율 {window.devicePixelRatio} · {l.size}{l.posture !== 'flat' ? ` · ${l.posture}` : ''} · {l.coarse ? '터치' : '마우스'}
    </p>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="kv wrap"><span className="muted">{label}</span>{children}</div>;
}

function Seg({ value, options, onChange }: { value: string; options: [string, string][]; onChange: (v: string) => void }) {
  return (
    <span className="seg" role="group">
      {options.map(([v, l]) => <button key={v} type="button" aria-pressed={value === v} onClick={() => onChange(v)}>{l}</button>)}
    </span>
  );
}
