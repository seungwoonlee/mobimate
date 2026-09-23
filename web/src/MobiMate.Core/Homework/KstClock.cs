namespace MobiMate;

/// <summary>
/// 인게임 초기화 시각 계산 (K-06, H-6): 일일 매일 06:00, 주간 매주 월요일 06:00, 한국 표준시 기준.
/// 한국은 일광절약시간이 없으므로 OS 시간대 데이터 대신 +9시간 고정 오프셋을 쓴다. 입출력은 UTC.
/// </summary>
public static class KstClock
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(9);
    public const int ResetHour = 6;

    public static DateTimeOffset ToKst(DateTimeOffset t) => t.ToOffset(Offset);

    /// <summary>now 이전(같은 시각 포함) 가장 최근 일일 리셋 시각 (UTC).</summary>
    public static DateTimeOffset LastDailyReset(DateTimeOffset nowUtc)
    {
        var k = ToKst(nowUtc);
        var today = new DateTimeOffset(k.Year, k.Month, k.Day, ResetHour, 0, 0, Offset);
        return (k >= today ? today : today.AddDays(-1)).ToUniversalTime();
    }

    public static DateTimeOffset NextDailyReset(DateTimeOffset nowUtc) => LastDailyReset(nowUtc).AddDays(1);

    /// <summary>now 이전(같은 시각 포함) 가장 최근 월요일 06:00 (UTC).</summary>
    public static DateTimeOffset LastWeeklyReset(DateTimeOffset nowUtc)
    {
        var k = ToKst(nowUtc);
        var today = new DateTimeOffset(k.Year, k.Month, k.Day, ResetHour, 0, 0, Offset);
        var daysSinceMonday = ((int)k.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var monday = today.AddDays(-daysSinceMonday);
        return (k >= monday ? monday : monday.AddDays(-7)).ToUniversalTime();
    }

    public static DateTimeOffset NextWeeklyReset(DateTimeOffset nowUtc) => LastWeeklyReset(nowUtc).AddDays(7);

    public static DateTimeOffset LastReset(HomeworkPeriod period, DateTimeOffset nowUtc) =>
        period == HomeworkPeriod.Daily ? LastDailyReset(nowUtc) : LastWeeklyReset(nowUtc);
}
