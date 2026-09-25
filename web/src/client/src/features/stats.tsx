import { useCharacter, useCutoffs, useHeader } from '../api/queries';
import type { Score } from '../api/types';
import { CardHead, Delta, ErrorCard, Fresh, Gauge, Pill, Skeleton } from '../components/ui';
import { fmt } from '../lib/format';
import { CutoffCard } from './cutoffs';

/** 스탯 (FR-DT-01) + 콘텐츠 추천 (FR-CO-07). 4대 점수를 크게, 기본·성기사 스탯은 접힘 영역에 둔다(WPF v1.2.0). */
export function StatsView() {
  const header = useHeader();
  const ch = useCharacter();
  const cut = useCutoffs();
  const h = header.data?.data;
  const c = ch.data?.data.character;

  return (
    <>
      <div className="view-head">
        <h2>스탯</h2>
        {h && <span className="sub">행동 상태 <Pill>{h.activity.text}</Pill>{c?.Vitals ? ` · 버프 ${c.Vitals.ActiveBuffCount}개` : ''}</span>}
      </div>
      {h ? (
        <div className="tiles four">
          <Tile label="전투력" value={h.scores.combat} delta={h.scores.combatDelta} />
          <Tile label="마도저항" value={h.scores.mdef} delta={h.scores.mdefDelta} />
          <Tile label="생활력" value={h.scores.living} />
          <Tile label="매력" value={h.scores.attract} />
        </div>
      ) : header.isError ? <ErrorCard error={header.error} onRetry={() => header.refetch()} /> : <Skeleton lines={2} />}

      <section aria-labelledby="cut-h">
        <div className="view-head sub-head">
          <h2 id="cut-h">콘텐츠 추천</h2>
          {cut.data && <span className="sub">{cut.data.data.stale ? '마지막으로 받은 값 기준 · ' : ''}<Fresh at={cut.dataUpdatedAt} /></span>}
        </div>
        {cut.isPending ? <Skeleton /> : cut.isError ? <ErrorCard error={cut.error} onRetry={() => cut.refetch()} /> : (
          <div className="two">{cut.data.data.contents.map(x => <CutoffCard key={x.id} c={x} />)}</div>
        )}
      </section>

      {c && (
        <div className="two">
          <div className="card">
            <CardHead title="생명·만복도" />
            {c.Vitals && <>
              <div className="kv"><span className="muted">체력</span><span className="v num">{fmt(c.Vitals.HealthCurrent)} / {fmt(c.Vitals.HealthMax)}</span></div>
              <Gauge pct={c.Vitals.HealthMax ? (c.Vitals.HealthCurrent / c.Vitals.HealthMax) * 100 : 0} level="ok" label="체력" />
              <div className="kv"><span className="muted">만복도</span><span className="v num">{fmt(c.Vitals.SatietyValue)} / {fmt(c.Vitals.SatietyMax)}</span></div>
              <Gauge pct={c.Vitals.SatietyMax ? (c.Vitals.SatietyValue / c.Vitals.SatietyMax) * 100 : 0} level={c.Vitals.SatietyValue / (c.Vitals.SatietyMax || 1) < 0.2 ? 'danger' : 'ok'} label="만복도" />
            </>}
            <div className="kv"><span className="muted">데코 점수</span><span className="v num">{fmt(c.DecorScore?.Value)}</span></div>
          </div>
          <details className="card fold">
            <summary><CardHead title="상세 스탯 (전투 능력치·성기사)" /></summary>
            {[c.AttackPower, c.DefencePower, c.HealthMax, c.STR, c.DEX, c.INT, c.LUCK, c.WILL].filter((x): x is Score => !!x).map(s => (
              <div className="kv" key={s.DisplayName}><span className="muted">{s.DisplayName}</span><span className="v num">{fmt(s.Value)}</span></div>
            ))}
            {c.PaladinStats && <>
              <div className="card-h mt-10"><h3>성기사 스탯</h3></div>
              {Object.values(c.PaladinStats).map(s => (
                <div className="kv" key={s.DisplayName}><span className="muted">{s.DisplayName}</span><span className="v num">{fmt(s.Value)}</span></div>
              ))}
            </>}
          </details>
        </div>
      )}
    </>
  );
}

function Tile({ label, value, delta }: { label: string; value: number; delta?: number }) {
  return (
    <div className="tile">
      <span className="l">{label}</span>
      <span className="v num">{fmt(value)}</span>
      {delta !== undefined && <Delta value={delta} hideZero />}
    </div>
  );
}
