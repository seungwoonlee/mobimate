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

    [GeneratedRegex(@"(?:(\d+)-)?(\d{1,2})-(\d{1,2})\s+(\d{1,2}):(\d{2})")]
    private static partial Regex ErinnRegex();

    /// <summary>
    /// 에린 시간: "2959-4-23 15:51" → "에린 시간 2959년 4월 23일 15:51 ☀️ (낮)".
    /// v1.1.0에서 연도 표시를 되살렸다. 연도가 없으면 연도 없이 표시한다.
    /// </summary>
    public static string ErinnTime(string? erinnNow)
    {
        if (string.IsNullOrWhiteSpace(erinnNow)) return "";

        var m = ErinnRegex().Match(erinnNow);
        if (!m.Success) return $"에린 시간 {erinnNow}";

        var year = m.Groups[1].Success ? $"{m.Groups[1].Value}년 " : "";
        var month = int.Parse(m.Groups[2].Value);
        var day = int.Parse(m.Groups[3].Value);
        var hour = int.Parse(m.Groups[4].Value);
        var minute = m.Groups[5].Value;
        var isDay = hour >= 6 && hour < 18;
        return $"에린 시간 {year}{month}월 {day}일 {hour:D2}:{minute} {(isDay ? "☀️" : "🌙")} ({(isDay ? "낮" : "밤")})";
    }
}
