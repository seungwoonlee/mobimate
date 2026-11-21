namespace MobiMate.Tests;

/// <summary>은동전·마족 공물 +1 위상 학습 (2026-11-21): 충전은 캐릭터마다 고정된 위상에서 일어나고 접속하지 않아도 이어진다</summary>
public class CoinPhaseTests
{
    private static readonly DateTime T0 = new(2026, 11, 21, 3, 0, 0, DateTimeKind.Utc);
    private const int Phase = 10 * 60 + 5;   // 매 30분 중 10분 5초

    /// <summary>위상 Phase에서 +1 되는 은동전을 매 <paramref name="poll"/>초마다 읽은 것처럼 먹인다</summary>
    private static CoinPhase Feed(int minutes, int poll = 60, long start = 20)
    {
        var c = CoinPhase.Silver();
        for (var s = 0; s <= minutes * 60; s += poll)
        {
            var t = T0.AddSeconds(s);
            var ticks = CoinPhase.TicksBetween(Phase, 1800, T0, t);
            c.Observe(t, start + ticks);
        }
        return c;
    }

    [Fact]
    public void FrequentReads_NarrowThePhaseToTheReadingInterval()
    {
        var est = Feed(180).Estimate();
        Assert.NotNull(est);
        Assert.InRange(est!.Value.Width, 1, 61);                        // 60초마다 읽었으니 폭은 60초 안쪽
        Assert.InRange(Math.Abs(est.Value.Phase - Phase), 0, 31);       // 중심은 실제 위상 근처
    }

    [Fact]
    public void FinerReads_NarrowItFurther()
    {
        var est = Feed(180, poll: 2).Estimate();
        Assert.InRange(est!.Value.Width, 1, 3);
        Assert.InRange(Math.Abs(est.Value.Phase - Phase), 0, 2);
    }

    [Fact]
    public void NoInformationYet_ReturnsNull()
    {
        var c = CoinPhase.Silver();
        c.Observe(T0, 20);
        c.Observe(T0.AddSeconds(60), 20);   // +1이 없었다는 것만으로는 위상을 못 정한다
        Assert.Null(c.Estimate());
    }

    [Fact]
    public void TicksBetween_CountsThePhaseCrossings_NotElapsedOverStep()
    {
        // 마지막 본 때가 위상 직전이면 4분만 지나도 +1, 위상 직후라면 29분이 지나도 +0
        Assert.Equal(1, CoinPhase.TicksBetween(Phase, 1800, T0.AddSeconds(Phase - 120), T0.AddSeconds(Phase + 120)));
        Assert.Equal(0, CoinPhase.TicksBetween(Phase, 1800, T0.AddSeconds(Phase + 10), T0.AddSeconds(Phase + 1700)));
        Assert.Equal(2, CoinPhase.TicksBetween(Phase, 1800, T0.AddSeconds(Phase + 10), T0.AddSeconds(Phase + 1810 + 1800)));
    }

    [Fact]
    public void Forecast_UsesThePhase_WhenKnown()
    {
        var seen = T0.AddSeconds(Phase + 60);                // 위상을 막 지난 때 본 값 30
        var now = seen.AddMinutes(60);                       // 한 시간 뒤: 위상을 2번 지났다 (경과 ÷ 주기와도 같다)
        Assert.Equal(32, CoinForecast.Silver(30, seen, now, member: false, Phase).Expected);
        var seen2 = T0.AddSeconds(Phase - 60);               // 위상 직전에 본 값
        var now2 = seen2.AddMinutes(35);                     // 경과 ÷ 주기는 1, 실제는 위상을 2번(−1분, +29분) 지났다
        Assert.Equal(1, CoinForecast.Silver(30, seen2, now2, false).Expected - 30);
        Assert.Equal(2, CoinForecast.Silver(30, seen2, now2, false, Phase).Expected - 30);
    }

    [Fact]
    public void MinutesToFull_UsesTheNextTickTime()
    {
        var now = T0.AddSeconds(Phase - 30 * 60 + 600);     // 다음 +1이 20분 뒤
        var s = CoinForecast.Silver(98, now, now, false, Phase);
        Assert.Equal(20 + 30, s.MinutesToFull, 0.1);           // 20분 뒤 99, 다시 30분 뒤 100
    }

    [Fact]
    public void SpendingFromTheCap_ResetsWhatWeKnew()
    {
        var c = CoinPhase.Silver();
        c.Observe(T0, 100);
        c.Observe(T0.AddSeconds(60), 90);   // 상한에서 소비: 시계가 새로 시작
        Assert.Null(c.Estimate());
    }

    [Fact]
    public void ASingleBadRead_DoesNotBreakAWellKnownPhase()
    {
        var c = Feed(180, poll: 5);
        var before = c.Estimate()!.Value;
        var t = T0.AddMinutes(181);
        c.Observe(t, c.LastHeld!.Value);
        c.Observe(t.AddSeconds(5), c.LastHeld!.Value + 1);   // 위상과 맞지 않는 곳에서 +1이 나왔다 (읽기 오류)
        c.Observe(t.AddSeconds(10), c.LastHeld!.Value);
        var after = c.Estimate()!.Value;
        Assert.InRange(Math.Abs(after.Phase - before.Phase), 0, 3);
    }

    [Fact]
    public void Bootstrap_FromPastDetections_FindsTheCommonPhase()
    {
        var times = new List<DateTime>();
        for (var i = 0; i < 40; i++) times.Add(T0.AddSeconds(Phase + 1800L * i + (i % 5) * 12));   // 읽는 주기 때문에 감지가 조금씩 늦다
        times.Add(T0.AddSeconds(Phase + 300));   // 이상한 값 하나
        var c = CoinPhase.Silver();
        Assert.True(c.Bootstrap(times));
        var est = c.Estimate()!.Value;
        Assert.InRange(est.Phase, Phase - 80, Phase + 10);
    }

    [Fact]
    public void Bootstrap_RefusesWhenThePhaseKeepsMoving()
    {
        var rnd = new Random(1);
        var times = Enumerable.Range(0, 30).Select(i => T0.AddSeconds(rnd.Next(0, 1800) + 1800L * i)).ToList();
        Assert.False(CoinPhase.Silver().Bootstrap(times));
    }

    [Fact]
    public void SaveAndLoad_KeepsTheIntervals()
    {
        var c = Feed(120);
        var est = c.Estimate();
        var d = CoinPhase.Silver();
        d.LoadIntervals(c.ToIntervals(), c.LastHeld, c.LastObsUtc);
        Assert.Equal(est!.Value.Width, d.Estimate()!.Value.Width);
    }
}
