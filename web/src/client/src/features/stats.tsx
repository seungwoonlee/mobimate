import { useCharacter, useCutoffs, useHeader } from '../api/queries';
import { CardHead, Delta, ErrorCard, Fresh, Gauge, Pill, Skeleton } from '../components/ui';
import { fmt } from '../lib/format';
import { scoreClass, vitalLevel, type ScoreKind } from '../lib/score';
import { CutoffCard } from './cutoffs';

/** 스탯 (FR-DT-01) + 콘텐츠 추천 (FR-CO-07). 5대 점수(전투력·마도저항·생활력·매력·데코)를 같은 크기로, 점수 색은 게임과 같다(FR-DT-11). */
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
        <div className="tiles five">
          <Tile label="전투력" value={h.scores.combat} kind="combat" delta={h.scores.combatDelta} />
          <Tile label="마도저항" value={h.scores.mdef} kind="mdef" delta={h.scores.mdefDelta} />
          <Tile label="생활력" value={h.scores.living} />
          <Tile label="매력" value={h.scores.attract} kind="attract" />
          <Tile label="데코 점수" value={c?.DecorScore?.Value} />
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

      {c?.Vitals && (
        <div className="card">
          <CardHead title="생명·만복도" />
          <div className="vitals">
            <div>
              <div className="kv"><span className="muted">체력</span><span className="v num">{fmt(c.Vitals.HealthCurrent)} / {fmt(c.Vitals.HealthMax)}</span></div>
              <Gauge pct={pctOf(c.Vitals.HealthCurrent, c.Vitals.HealthMax)} level={vitalLevel(pctOf(c.Vitals.HealthCurrent, c.Vitals.HealthMax))} label="체력" />
            </div>
            <div>
              <div className="kv"><span className="muted">만복도</span><span className="v num">{fmt(c.Vitals.SatietyValue)} / {fmt(c.Vitals.SatietyMax)}</span></div>
              <Gauge pct={pctOf(c.Vitals.SatietyValue, c.Vitals.SatietyMax)} level={vitalLevel(pctOf(c.Vitals.SatietyValue, c.Vitals.SatietyMax))} label="만복도" />
            </div>
          </div>
        </div>
      )}
    </>
  );
}

const pctOf = (cur: number, max: number) => (max ? (cur / max) * 100 : 0);

function Tile({ label, value, kind, delta }: { label: string; value: number | undefined; kind?: ScoreKind; delta?: number }) {
  return (
    <div className="tile">
      <span className="l">{label}</span>
      <span className={`v num ${kind && value != null ? scoreClass(kind, value) : ''}`}>{fmt(value)}</span>
      {delta !== undefined && <Delta value={delta} hideZero />}
    </div>
  );
}
