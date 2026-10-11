using System.Text.RegularExpressions;

namespace MobiMate;

/// <summary>화면 표시용 문구 (WPF판 v1.1.0 형식과 같게 맞춘다).</summary>
public static partial class DisplayFormat
{
    /// <summary>가공 남은 시간: 10995 → "3시간 3분 15초", 0 이하 → "수거 대기" (FR-DT-05).</summary>
    public static string RemainingTime(int totalSeconds)
    {
        if (totalSeconds <= 0) return "수거 대기";
        var t = TimeSpan.FromSeconds(totalSeconds);
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}시간 {t.Minutes}분 {t.Seconds}초";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}분 {t.Seconds}초";
        return $"{t.Seconds}초";
    }

    [GeneratedRegex(@"(\d{1,2}):(\d{2})")]
    private static partial Regex ErinnClockRegex();

    /// <summary>
    /// 에린 시간 (REQUIREMENTS K-11, WPF판 v1.2.0): 게임이 준 문자열을 그대로 두고 낮·밤 배지만 붙인다.
    /// "2960-0-20 13:47" → "에린 시간 2960-0-20 13:47 ☀️ (낮)". 엔진의 월은 0부터 시작하므로 "N월"로 풀어 쓰지 않는다.
    /// </summary>
    public static string ErinnTime(string? erinnNow)
    {
        if (string.IsNullOrWhiteSpace(erinnNow)) return "";

        var raw = erinnNow.Trim();
        var m = ErinnClockRegex().Match(raw);
        if (!m.Success) return $"에린 시간 {raw}";

        var hour = int.Parse(m.Groups[1].Value);
        var isDay = hour >= 6 && hour < 18;
        return $"에린 시간 {raw} {(isDay ? "☀️" : "🌙")} ({(isDay ? "낮" : "밤")})";
    }
}
