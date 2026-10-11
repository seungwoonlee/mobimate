using System.Globalization;
using MobiMateHomeWork.Services;

namespace MobiMateHomeWork.ViewModels;

public enum MeterKind { Silver, Tribute, Weight }

/// <summary>막대의 색 단계: 평소 / 주의(가득 차기 전·95%↑) / 위험(가득·100%↑).</summary>
public enum MeterLevel { Normal, Warn, Danger }

/// <summary>
/// 막대 하나(은동전·마족 공물·가방 무게)의 표시 값. HasBar가 거짓이면 막대 없이 숫자만 보인다(무게의 최대를 모를 때, 기록이 없을 때).
/// Estimated = 접속 안 한 캐릭터의 예상치(마지막 값 + 지난 시간만큼 충전).
/// </summary>
public sealed record MeterModel(MeterKind Kind, string Label, string ValueText, double Fraction, MeterLevel Level, bool HasBar, bool Estimated, string Tooltip)
{
    public static MeterModel Empty(MeterKind kind, string label) => new(kind, label, "—", 0, MeterLevel.Normal, false, false, $"{label} 기록이 아직 없습니다.");
}

/// <summary>카드·표에 쓰는 값 서식 (천 단위 구분, 만 단위 줄임, 지난 시간, 막대 값).</summary>
public static class CardFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>1,234,567 (천 단위 구분).</summary>
    public static string Thousands(long n) => n.ToString("N0", Inv);

    /// <summary>표의 좁은 칸용 전투력: 1만 이상은 "8.9만", 아니면 천 단위 구분.</summary>
    public static string CompactCombat(long n) => n >= 10_000 ? (n / 10_000.0).ToString("0.#", Inv) + "만" : Thousands(n);

    /// <summary>"방금" / "N분 전" / "N시간 전" / "N일 전". 접속 중이면 "접속 중".</summary>
    public static string Ago(DateTimeOffset last, DateTimeOffset now, bool live = false)
    {
        if (live) return "접속 중";
        var d = now - last;
        if (d < TimeSpan.FromMinutes(1)) return "방금";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes}분 전";
        if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours}시간 전";
        return $"{(int)d.TotalDays}일 전";
    }

    /// <summary>"3시간 20분" / "45분" / "1분 미만".</summary>
    public static string Duration(double minutes)
    {
        if (minutes < 1) return "1분 미만";
        var m = (int)Math.Ceiling(minutes);
        var h = m / 60;
        var r = m % 60;
        if (h >= 24) return r == 0 && h % 24 == 0 ? $"{h / 24}일" : $"{h / 24}일 {h % 24}시간";
        return h == 0 ? $"{r}분" : r == 0 ? $"{h}시간" : $"{h}시간 {r}분";
    }

    /// <summary>
    /// 은동전·마족 공물 막대. 접속 중이면 보유량 그대로, 아니면 마지막으로 본 값에서 지난 시간만큼 충전된 예상 보유량(CoinForecast).
    /// 가득 차기까지 24시간 안이면 주의색, 이미 가득이면 위험색.
    /// </summary>
    public static MeterModel Coin(MeterKind kind, CoinView c, bool live, DateTimeOffset lastSeen, DateTimeOffset now)
    {
        var label = kind == MeterKind.Silver ? "은동전" : "마족 공물";
        var step = kind == MeterKind.Silver ? CoinForecast.SilverStep : CoinForecast.TributeStep;
        var cap = Math.Max(1, c.Cap);
        CoinState s;
        if (live) s = CoinForecast.Forecast(c.Held, now.UtcDateTime, now.UtcDateTime, step, cap);   // 실시간: 예상 없이 보유량 그대로
        else s = CoinForecast.Forecast(c.Held, lastSeen.UtcDateTime, now.UtcDateTime, step, cap);
        var estimated = !live && s.Expected > s.Held;
        var level = s.Level switch { CoinLevel.Full => MeterLevel.Danger, CoinLevel.Near => MeterLevel.Warn, _ => MeterLevel.Normal };
        var text = $"{s.Expected}/{cap}" + (estimated ? " 예상" : "");
        var lines = new List<string>();
        if (live) lines.Add($"{label} {s.Expected} / 상한 {cap}");
        else lines.Add($"마지막으로 본 값 {s.Held} → 지금 예상 {s.Expected} (상한 {cap})");
        lines.Add(s.Expected >= cap ? "이미 가득 찼습니다 — 충전이 멈췄습니다" : $"가득까지 약 {Duration(s.MinutesToFull)}");
        return new MeterModel(kind, label, text, s.Percent, level, true, estimated, string.Join('\n', lines));
    }

    /// <summary>가방 무게 막대. 최대를 모르면 현재 무게만 숫자로 보인다. 95%↑ 주의, 100%↑ 위험. 기록이 없으면 빈 값.</summary>
    public static MeterModel Weight(WeightView? w)
    {
        if (w == null) return MeterModel.Empty(MeterKind.Weight, "가방");
        var cur = w.Current.ToString("#,0.#", Inv);
        if (w.Max is not { } max || w.Percent is not { } pct || w.Level is not { } lv)
            return new MeterModel(MeterKind.Weight, "가방", $"{cur} kg", 0, MeterLevel.Normal, false, false, $"가방 {cur} kg\n최대 무게를 아직 읽지 못했습니다 — 접속하면 기록됩니다.");
        var level = lv switch { WeightLevel.Danger => MeterLevel.Danger, WeightLevel.Warn => MeterLevel.Warn, _ => MeterLevel.Normal };
        var maxText = max.ToString("#,0.#", Inv);
        return new MeterModel(MeterKind.Weight, "가방", $"{cur}/{maxText} kg", Math.Clamp(pct / 100, 0, 1), level, true, false,
            $"가방 {cur} / {maxText} kg ({pct:0.#}%)" + (level == MeterLevel.Danger ? "\n무게 한도를 넘었습니다" : level == MeterLevel.Warn ? "\n곧 가득 찹니다" : ""));
    }
}
