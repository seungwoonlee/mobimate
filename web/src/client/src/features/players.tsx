import type { NearPlayer } from '../api/types';
import { JobIcon } from '../components/ui';
import { fmt } from '../lib/format';

const REL_TEXT: Record<NearPlayer['relation'], string> = { party: '파티원', friend: '친구', guild: '길드원', other: '' };
/** 줄 맨 앞 표식 (v1.5): 친구·파티원·길드원은 (친구) 같은 표식을 달아 색만으로 구분하지 않게 한다. */
export const REL_TAG: Record<NearPlayer['relation'], string> = { party: '(파티원)', friend: '(친구)', guild: '(길드원)', other: '' };

/**
 * 주변 플레이어 한 줄 (FR-DT-32·33, FR-OV-13): 클래스 · 레벨 · 전투력 · 칭호 순서. 캐릭터 이름은 게임이 주지 않아 표시할 수 없다.
 * 파티원·친구·길드원은 색과 굵은 글씨로 구분하고(스크린 리더에는 관계를 글자로 읽어 준다),
 * 나보다 전투력이 높으면 전투력 숫자의 색으로만 알린다.
 */
export function PlayerLine({ p }: { p: NearPlayer }) {
  return (
    <span className={`pl ${p.relation}`}>
      <span className="pl-tag" aria-label={REL_TEXT[p.relation] || undefined}>{REL_TAG[p.relation]}</span>
      <span className="pl-job"><JobIcon job={p.job} size={15} />{p.job}</span>
      <span className="pl-sep">-</span>
      <span className="pl-lv num">Lv.{p.level}</span>
      <span className="pl-sep">-</span>
      <span className={`pl-cs num ${p.isStronger ? 'stronger' : ''}`}>
        {fmt(p.combatScore)}
        {p.isStronger && <span className="sr-only"> (나보다 강함)</span>}
      </span>
      {p.title && <><span className="pl-sep">-</span><span className="pl-title">{p.title}</span></>}
      {p.inCombat && <span className="pl-combat"> · 전투 중</span>}
    </span>
  );
}
