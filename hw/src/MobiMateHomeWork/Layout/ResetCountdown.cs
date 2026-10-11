using System.Globalization;

namespace MobiMateHomeWork.Layout;

/// <summary>제목줄의 리셋 카운트다운 글: "일 5:12 · 주 2일 5h". 일일 = 다음 06:00까지 시:분, 주간 = 다음 월요일 06:00까지 일·시간(내림).</summary>
public static class ResetCountdown
{
    public static string Format(DateTimeOffset nowUtc)
    {
        var daily = KstClock.NextDailyReset(nowUtc) - nowUtc;
        var weekly = KstClock.NextWeeklyReset(nowUtc) - nowUtc;
        var dailyText = $"{(int)daily.TotalHours}:{daily.Minutes.ToString("00", CultureInfo.InvariantCulture)}";
        var weeklyText = weekly.Days > 0 ? $"{weekly.Days}일 {weekly.Hours}h" : $"{weekly.Hours}h";
        return $"일 {dailyText} · 주 {weeklyText}";
    }
}
