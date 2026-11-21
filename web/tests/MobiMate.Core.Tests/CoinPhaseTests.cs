namespace MobiMate.Tests;

/// <summary>은동전·마족 공물 +1 위상 학습 (2026-11-21): 충전은 캐릭터마다 고정된 위상에서 일어나고 접속하지 않아도 이어진다</summary>
public class CoinPhaseTests
{
    private static readonly DateTime T0 = new(2026, 11, 21, 3, 0, 0, DateTimeKind.Utc);
    private const int Phase = 10 * 60 + 5;   // 매 30분 중 10분 5초

    /// <summary>위상 Phase에서 +1 되는 은동전을 매 <paramref name="poll"/>초마다 읽은 것처럼 먹인다</summary>
    private static CoinPhase Feed(int minutes, int poll = 60, long start = 20, long cap = 100)
    {
        var c = CoinPhase.Silver();
        for (var s = 0; s <= minutes * 60; s += poll)
        {
            var t = T0.AddSeconds(s);
            var ticks = CoinPhase.TicksBetween(Phase, 1800, T0, t);
            c.Observe(t, start + ticks, cap);
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
        c.Observe(T0, 20, 100);
        c.Observe(T0.AddSeconds(60), 20, 100);   // +1이 없었다는 것만으로는 위상을 못 정한다
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
    public void SpendingFromTheCap_RestartsTheClock_SoTheSpendMomentIsTheNewPhase()
    {
        // 상한(100)에서 소비하면 충전 시계가 그 순간 새로 시작한다 (실측: 상한에서 소비한 뒤 첫 +1은 한 주기 뒤)
        var c = Feed(60);   // 위상을 이미 알고 있다
        var full = CoinPhase.Silver();
        var spendAt = T0.AddSeconds(7 * 60 + 23);   // 이 순간 소비
        full.Observe(spendAt.AddSeconds(-30), 100, 100);
        full.Observe(spendAt.AddSeconds(30), 99, 100);        // 60초 사이에 소비가 있었다
        var est = full.Estimate();
        Assert.NotNull(est);
        Assert.InRange(est!.Value.Width, 1, 61);
        var expected = (7 * 60 + 23);                          // 위상 = 소비한 순간(주기 안의 초)
        Assert.InRange(Math.Abs(est.Value.Phase - expected - ((T0 - DateTime.UnixEpoch).TotalSeconds % 1800)), 0, 31);
        _ = c;
    }

    [Fact]
    public void SpendingBelowTheCap_KeepsTheKnownPhase()
    {
        var c = Feed(120, poll: 5);
        var before = c.Estimate()!.Value;
        var t = T0.AddMinutes(121);
        c.Observe(t, 22, 100);
        c.Observe(t.AddSeconds(5), 15, 100);      // 상한 아래에서 소비: 시계는 그대로
        var after = c.Estimate()!.Value;
        Assert.Equal(before.Phase, after.Phase, 3);
        Assert.Equal(before.Width, after.Width);
    }

    [Fact]
    public void MemberCap150_IsNotTheCapAt100()
    {
        // 멤버십(상한 150)은 100에서 소비해도 시계가 그대로다
        var c = Feed(120, poll: 5, start: 100, cap: 150);
        var before = c.Estimate()!.Value;
        var t = T0.AddMinutes(121);
        c.Observe(t, 105, 150);
        c.Observe(t.AddSeconds(5), 90, 150);
        Assert.Equal(before.Phase, c.Estimate()!.Value.Phase, 3);
    }

    [Fact]
    public void SpendingFromTheCap_AfterALongGap_OnlyForgetsTheOldPhase()
    {
        var c = Feed(120, poll: 5);
        c.Observe(T0.AddMinutes(130), 100, 100);
        c.Observe(T0.AddHours(5), 98, 100);   // 한참 뒤 읽었더니 줄어 있다: 소비한 때를 모른다
        Assert.Null(c.Estimate());
    }

    [Fact]
    public void ASingleBadRead_DoesNotBreakAWellKnownPhase()
    {
        var c = Feed(180, poll: 5);
        var before = c.Estimate()!.Value;
        var t = T0.AddMinutes(181);
        c.Observe(t, c.LastHeld!.Value, 100);
        c.Observe(t.AddSeconds(5), c.LastHeld!.Value + 1, 100);   // 위상과 맞지 않는 곳에서 +1이 나왔다 (읽기 오류)
        c.Observe(t.AddSeconds(10), c.LastHeld!.Value, 100);
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
