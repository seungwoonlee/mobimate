import type React from 'react';
import type { MissingPlayer, NearPlayer } from '../api/types';
import { JobIcon } from '../components/ui';
import { fmt } from '../lib/format';

export const REL_TEXT: Record<NearPlayer['relation'], string> = { friend: '친구', guild: '길드원', other: '' };
/** 줄 맨 앞 표식 (v1.5): 친구·길드원은 (친구) 같은 표식을 달아 색만으로 구분하지 않게 한다. 파티원은 게임 정보로 알 수 없어 표시하지 않는다. */
export const REL_TAG: Record<NearPlayer['relation'], string> = { friend: '(친구)', guild: '(길드원)', other: '' };

/**
 * 주변 플레이어 한 줄 (FR-DT-32·33, FR-OV-13): 클래스 · 레벨 · 전투력 · 칭호 순서. 캐릭터 이름은 게임이 주지 않아 표시할 수 없다.
 * 친구·길드원은 색과 굵은 글씨로 구분하고(스크린 리더에는 관계를 글자로 읽어 준다),
 * 나보다 전투력이 높으면 전투력 숫자의 색으로만 알린다.
 */
/** 오토 의심 짙기를 CSS 변수로: 테두리·깜빡임 속도가 짙기에 따라 달라진다 (--auto-mix = 테두리 색 비율 %, --auto-i = 0..1). */
export function autoStyle(intensity: number): React.CSSProperties {
  const i = Math.min(1, Math.max(0, intensity));
  return { '--auto-i': i, '--auto-mix': `${Math.round(30 + 70 * i)}%` } as React.CSSProperties;
}

/** 짙기 단계 이름 (색만으로 알리지 않는다): 의심 → 유력 → 매우 유력 */
export function autoLevel(intensity: number): { label: string; dots: string } {
  return intensity >= 0.67 ? { label: '매우 유력', dots: '●●●' } : intensity >= 0.34 ? { label: '유력', dots: '●●○' } : { label: '의심', dots: '●○○' };
}

/** 오토 의심 배지(2026-10-22): 글자와 단계 점으로 알리고, 이유·시간은 설명으로 둔다. 짙을수록 점이 채워진다. */
export type AutoReason = 'lying' | 'idle' | 'playing' | 'far' | 'missing' | 'frozen' | 'relative';

/** 이유별 설명(시간은 "N분째"). 모르는 이유는 일반 문구로 보인다. */
function autoWhy(reason: string, t: string): string {
  switch (reason) {
    case 'lying': return `다른 사람은 싸우는데 ${t} 제자리에서 전투하지 않아요 (쓰러진 채 부활을 안 눌렀을 수 있어요)`;
    case 'idle': return `던전·레이드 안에서 다른 사람 대부분이 싸우는데 ${t} 이 사람만 전투·조작·이동이 보이지 않아요`;
    case 'playing': return `다른 사람이 싸우는데 ${t} 혼자 연주하고 있어요`;
    case 'far': return `다른 사람들은 모여 있는데 ${t} 혼자 멀리 떨어져 있어요`;
    case 'relative': return `전투가 시작될 때 가장 늦게 합류하고 한 자리에 오래 머무는 모습이에요 (파티 다른 사람과 비교한 의심, 확정 아님, 시범 기준)`;
    case 'frozen': return `${t} 한 자리에서 움직이지 않았어요 (죽은 채 부활을 안 눌렀거나 자동전투가 멈춘 모습. 전투 중으로 읽혀도 위치로 판단, 시범 기준)`;
    default: return `함께 보이다가 던전·레이드 안에서 ${t} 주변에 보이지 않아요`;
  }
}

export function AutoBadge({ reason, seconds, intensity }: { reason: AutoReason | string; seconds: number; intensity: number }) {
  const t = seconds >= 60 ? `${Math.floor(seconds / 60)}분째` : `${seconds}초째`;
  const why = autoWhy(reason, t);
  const lv = autoLevel(intensity);
  return <span className="auto-badge" title={`${why} (${lv.label})`} aria-label={`오토 의심 ${lv.label}: ${why}`}>오토 의심 <span className="auto-dots" aria-hidden="true">{lv.dots}</span></span>;
}

/** 한동안 함께 보이다가 사라진 사람 한 줄 (목록에 없어 직업·레벨·전투력만 안다) */
/** 직업 이름. 내 서버와 다른 사람이면 "직업 (서버)" (2026-10-22 승운: 다른 서버 파티원 구분) */
export function jobLabel(job: string, realm: string | null | undefined, myRealm: string | null | undefined): string {
  return realm && myRealm && realm !== myRealm ? `${job} (${realm})` : job;
}

export function MissingLine({ m, myRealm }: { m: MissingPlayer; myRealm?: string | null }) {
  return (
    <span className="pl pl-missing">
      <JobIcon job={m.job} size={17} />
      <span className="pl-who">
        <span className="pl-job">{jobLabel(m.job, m.realm, myRealm)}<AutoBadge reason="missing" seconds={m.seconds} intensity={m.intensity} /></span>
        <span className="pl-sub small"><span className="num">Lv.{m.level}</span> · <span className="num">{fmt(m.combatScore)}</span><span className="pl-combat"> · 안 보임</span></span>
      </span>
    </span>
  );
}

export function PlayerLine({ p, myRealm }: { p: NearPlayer; myRealm?: string | null }) {
  return (
    <span className={`pl ${p.relation}`}>
      <span className="pl-tag" aria-label={REL_TEXT[p.relation] || undefined}>{REL_TAG[p.relation]}</span>
      <span className="pl-job"><JobIcon job={p.job} size={17} />{jobLabel(p.job, p.realm, myRealm)}</span>
      <span className="pl-sep">-</span>
      <span className="pl-lv num">Lv.{p.level}</span>
      <span className="pl-sep">-</span>
      <span className={`pl-cs num ${p.isStronger ? 'stronger' : ''}`}>
        {fmt(p.combatScore)}
        {p.isStronger && <span className="sr-only"> (나보다 강함)</span>}
      </span>
      {p.title && <><span className="pl-sep">-</span><span className="pl-title">{p.title}</span></>}
      {p.inCombat && <span className="pl-combat"> · 전투 중</span>}
      {p.autoSuspect && <AutoBadge reason={p.autoSuspect.reason} seconds={p.autoSuspect.seconds} intensity={p.autoSuspect.intensity} />}
    </span>
  );
}
