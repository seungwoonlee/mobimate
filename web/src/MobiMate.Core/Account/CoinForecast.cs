namespace MobiMate;

/// <summary>충전 재화의 상태: 여유(경고 없음) / 곧 가득(81% 이상) / 가득(충전 멈춤)</summary>
public enum CoinLevel { Ok, Near, Full }

/// <param name="Held">마지막으로 본 보유량</param>
/// <param name="Expected">지금 예상 보유량 (상한까지)</param>
/// <param name="Percent">예상 보유량 / 상한 (0~1)</param>
/// <param name="MinutesToFull">가득 찰 때까지 남은 분. 이미 가득이면 0</param>
public sealed record CoinState(long Held, long Expected, long Cap, CoinLevel Level, double Percent, double MinutesToFull);

/// <summary>
/// 은동전·마족 공물 예상 보유량 (v1.5, 承雲 규칙). 최종 로그오프 시점의 보유량을 기억해 두고 시간이 지난 만큼 더한다.
/// 은동전은 30분에 1개, 마족 공물은 12시간에 1개가 충전된다. 상한에 닿으면 충전이 멈춘다:
/// 멤버십 계정은 150개 / 15개, 미가입은 100개 / 10개. 이미 상한 이상이면 더 늘지 않는다(그래서 서둘러 접속해야 한다).
/// </summary>
public static class CoinForecast
{
    public static readonly TimeSpan SilverStep = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan TributeStep = TimeSpan.FromHours(12);

    /// <summary>"곧 가득" 경고 기준: 상한의 81% 이상 (承雲 확정 2026-10-02). 그 아래는 아무 경고도 없다.</summary>
    public const long NearPercent = 81;

    public const long SilverCapMember = 150, SilverCapFree = 100;
    public const long TributeCapMember = 15, TributeCapFree = 10;

    public static long SilverCap(bool member) => member ? SilverCapMember : SilverCapFree;
    public static long TributeCap(bool member) => member ? TributeCapMember : TributeCapFree;

    /// <param name="lastSeenUtc">마지막으로 이 캐릭터를 본 시각. 지금 접속 중이면 now와 같다(경과 0).</param>
    public static CoinState Forecast(long held, DateTime lastSeenUtc, DateTime nowUtc, TimeSpan step, long cap)
    {
        var elapsed = nowUtc > lastSeenUtc ? nowUtc - lastSeenUtc : TimeSpan.Zero;
        long expected;
        double minutesToFull;
        if (held >= cap)
        {
            expected = held;   // 이미 가득: 충전이 멈춰 있다
            minutesToFull = 0;
        }
        else
        {
            var gained = (long)Math.Floor(elapsed / step);
            expected = Math.Min(cap, held + gained);
            var intoStep = TimeSpan.FromTicks(elapsed.Ticks % step.Ticks);
            minutesToFull = expected >= cap ? 0 : Math.Max(0, ((cap - expected) * step - intoStep).TotalMinutes);
        }
        var level = expected >= cap ? CoinLevel.Full : expected * 100 >= cap * NearPercent ? CoinLevel.Near : CoinLevel.Ok;
        return new CoinState(held, expected, cap, level, cap > 0 ? Math.Min(1.0, (double)expected / cap) : 0, minutesToFull);
    }

    public static CoinState Silver(long held, DateTime lastSeenUtc, DateTime nowUtc, bool member) =>
        Forecast(held, lastSeenUtc, nowUtc, SilverStep, SilverCap(member));

    public static CoinState Tribute(long held, DateTime lastSeenUtc, DateTime nowUtc, bool member) =>
        Forecast(held, lastSeenUtc, nowUtc, TributeStep, TributeCap(member));
}
