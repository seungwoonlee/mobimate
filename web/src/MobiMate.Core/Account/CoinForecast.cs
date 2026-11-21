namespace MobiMate;

/// <summary>충전 재화의 상태: 여유(경고 없음) / 곧 가득(24시간 안에 가득 참) / 가득(충전 멈춤)</summary>
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

    /// <summary>
    /// "곧 가득" 경고 기준: 지금부터 24시간 안에 가득 차는 경우 (承雲 확정 2026-10-04). 오늘 접속해야 내일 접속할 때까지 가득 차지 않게 유지할 수 있다.
    /// 그보다 여유가 있으면 아무 경고도 없다.
    /// </summary>
    public static readonly TimeSpan NearWindow = TimeSpan.FromHours(24);

    public const long SilverCapMember = 150, SilverCapFree = 100;
    public const long TributeCapMember = 15, TributeCapFree = 10;

    public static long SilverCap(bool member) => member ? SilverCapMember : SilverCapFree;
    public static long TributeCap(bool member) => member ? TributeCapMember : TributeCapFree;

    /// <param name="lastSeenUtc">마지막으로 이 캐릭터를 본 시각. 지금 접속 중이면 now와 같다(경과 0).</param>
    /// <param name="phaseSeconds">+1이 일어나는 위상(주기 안의 초). 알면 마지막으로 본 때부터 그 시각을 지난 횟수로 세고, 모르면 경과 시간 ÷ 주기로 센다 (<see cref="CoinPhase"/>).</param>
    public static CoinState Forecast(long held, DateTime lastSeenUtc, DateTime nowUtc, TimeSpan step, long cap, double? phaseSeconds = null)
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
            var stepSec = (int)step.TotalSeconds;
            var gained = phaseSeconds is { } p ? CoinPhase.TicksBetween(p, stepSec, lastSeenUtc, nowUtc) : (long)Math.Floor(elapsed / step);
            expected = Math.Min(cap, held + gained);
            if (expected >= cap) minutesToFull = 0;
            else if (phaseSeconds is { } ph)
            {
                var next = CoinPhase.NextTick(ph, stepSec, nowUtc);   // 다음 +1까지 남은 시간 + 그 뒤로 (남은 개수 − 1) × 주기
                minutesToFull = Math.Max(0, (next - nowUtc).TotalMinutes + (cap - expected - 1) * step.TotalMinutes);
            }
            else
            {
                var intoStep = TimeSpan.FromTicks(elapsed.Ticks % step.Ticks);
                minutesToFull = Math.Max(0, ((cap - expected) * step - intoStep).TotalMinutes);
            }
        }
        var level = expected >= cap ? CoinLevel.Full : minutesToFull <= NearWindow.TotalMinutes ? CoinLevel.Near : CoinLevel.Ok;
        return new CoinState(held, expected, cap, level, cap > 0 ? Math.Min(1.0, (double)expected / cap) : 0, minutesToFull);
    }

    public static CoinState Silver(long held, DateTime lastSeenUtc, DateTime nowUtc, bool member, double? phaseSeconds = null) =>
        Forecast(held, lastSeenUtc, nowUtc, SilverStep, SilverCap(member), phaseSeconds);

    public static CoinState Tribute(long held, DateTime lastSeenUtc, DateTime nowUtc, bool member, double? phaseSeconds = null) =>
        Forecast(held, lastSeenUtc, nowUtc, TributeStep, TributeCap(member), phaseSeconds);
}
