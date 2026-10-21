namespace MobiMate.Tests;

/// <summary>
/// 오토 의심 표식 (2026-10-22 승운 요청): 조건이 시작되면 초 단위 카운터를 쌓고, 표시 기준을 넘으면 켜져 점점 짙어지며,
/// 해소 요인이 생기면 바로 0으로 풀린다. 대상은 던전·레이드 안에서 혼자 오래 전투하지 않는 사람 / 함께 보이다가 오래 안 보이는 사람.
/// </summary>
public class AutoSuspicionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static WatchedPlayer P(string job, bool combat, int level = 100, long score = 100000) =>
        new($"아이라|{job}|{level}|{score}|", "아이라", job, level, score, null, combat);

    private static readonly WatchedPlayer A = P("전사", true), B = P("궁수", true), C = P("힐러", true);

    /// <summary>step초마다 total초 동안 같은 목록을 넣고 마지막 관찰 시각을 돌려준다.</summary>
    private static DateTimeOffset Run(AutoSuspicionTracker t, DateTimeOffset from, int totalSec, Func<IReadOnlyList<WatchedPlayer>> list, bool inInstance = true, int step = 10)
    {
        var now = from;
        for (var s = 0; s <= totalSec; s += step) { now = from.AddSeconds(s); t.Observe(now, inInstance, list()); }
        return now;
    }

    private static double IntensityOf(AutoSuspicionTracker t, string job) => t.Suspects.FirstOrDefault(s => s.Player.Job == job)?.Intensity ?? 0;

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(29, 0.0)]      // 표시 기준(30초) 전: 아직 아무 표시도 없다
    [InlineData(30, 0.15)]     // 기준을 넘는 순간 약하게 켜진다
    [InlineData(90, 0.575)]    // 중간
    [InlineData(150, 1.0)]     // 가장 짙은 기준
    [InlineData(500, 1.0)]
    public void IntensityOf_GrowsFromShowThresholdToFull(int seconds, double expected) =>
        Assert.Equal(expected, AutoSuspicionTracker.IntensityOf(TimeSpan.FromSeconds(seconds), AutoSuspicionTracker.IdleShowFrom, AutoSuspicionTracker.IdleFull), 3);

    [Fact]
    public void Idle_CounterBuildsUp_ThenShowsFaintly_AndGetsDarkerOverTime()
    {
        var t = new AutoSuspicionTracker();
        var idle = P("마법사", false);
        Run(t, T0, 20, () => new[] { A, B, C, idle });
        Assert.Empty(t.Suspects);                                    // 20초: 카운터만 쌓였고 표시는 아직

        var seen = new Dictionary<int, double>();
        for (var s = 0; s <= 200; s += 10) { t.Observe(T0.AddSeconds(s), true, new[] { A, B, C, idle }); seen[s] = IntensityOf(t, "마법사"); }
        Assert.Equal(0, seen[20]);
        Assert.InRange(seen[30], 0.1, 0.2);                          // 켜진 직후는 옅다
        Assert.True(seen[60] > seen[30] && seen[90] > seen[60] && seen[120] > seen[90]);   // 점점 짙어진다
        Assert.Equal(1.0, seen[150]);                                // 만점 기준
        Assert.Equal(1.0, seen[200]);
        var s0 = Assert.Single(t.Suspects);
        Assert.Equal(AutoSuspicionReason.Idle, s0.Reason);
        Assert.True(s0.For >= TimeSpan.FromSeconds(150));
    }

    [Fact]
    public void ResolvingFactor_ClearsImmediately_AndTheCounterStartsOver()
    {
        var t = new AutoSuspicionTracker();
        var idle = P("마법사", false);
        var end = Run(t, T0, 100, () => new[] { A, B, C, idle });
        Assert.NotEmpty(t.Suspects);

        t.Observe(end.AddSeconds(10), true, new[] { A, B, C, P("마법사", true) });   // 전투를 시작했다
        Assert.Empty(t.Suspects);                                                      // 바로 풀린다

        Run(t, end.AddSeconds(20), 20, () => new[] { A, B, C, idle });                // 다시 쉬어도 0부터
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void Idle_ClearsImmediately_WhenAnotherPlayerStopsFighting()
    {
        var t = new AutoSuspicionTracker();
        var idle = P("마법사", false);
        var end = Run(t, T0, 100, () => new[] { A, B, C, idle });
        Assert.NotEmpty(t.Suspects);
        t.Observe(end.AddSeconds(10), true, new[] { A, B, P("힐러", false), idle });    // 다른 사람도 쉰다: 비교할 수 없다
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void NotSuspected_WhenAnotherPlayerIsAlsoNotFighting_OrFewerThanTwoOthers()
    {
        var t = new AutoSuspicionTracker();
        Run(t, T0, 300, () => new[] { A, B, P("마법사", false), P("도적", false) });   // 다 같이 쉬는 것
        Assert.Empty(t.Suspects);
        var t2 = new AutoSuspicionTracker();
        Run(t2, T0, 300, () => new[] { A, P("마법사", false) });                      // 상대가 한 명뿐이면 비교가 안 된다
        Assert.Empty(t2.Suspects);
    }

    [Fact]
    public void LeavingTheInstance_ResetsEverything()
    {
        var t = new AutoSuspicionTracker();
        var idle = P("마법사", false);
        var end = Run(t, T0, 100, () => new[] { A, B, C, idle });
        Assert.NotEmpty(t.Suspects);
        t.Observe(end.AddSeconds(10), false, new[] { A, B, C, idle });                 // 던전 밖
        Assert.Empty(t.Suspects);
        Run(t, end.AddSeconds(20), 20, () => new[] { A, B, C, idle });                 // 처음부터 다시
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void ALongGapBetweenObservations_RestartsTheCounter()
    {
        var t = new AutoSuspicionTracker();
        var idle = P("마법사", false);
        var end = Run(t, T0, 40, () => new[] { A, B, C, idle });
        Assert.NotEmpty(t.Suspects);
        t.Observe(end.AddSeconds(120), true, new[] { A, B, C, idle });                 // 감시가 2분 멈췄다: 그 사이는 모른다
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void Missing_BuildsUpWhileGone_AndDeepens_ThenReturningClearsAtOnce()
    {
        var t = new AutoSuspicionTracker();
        var ghost = P("도적", true);
        var end = Run(t, T0, 40, () => new[] { A, B, C, ghost });                      // 40초 함께 보였다
        var seen = new Dictionary<int, double>();
        for (var s = 10; s <= 300; s += 10) { t.Observe(end.AddSeconds(s), true, new[] { A, B, C }); seen[s] = IntensityOf(t, "도적"); }
        Assert.Equal(0, seen[50]);                                                     // 안 보인 지 얼마 안 됨: 카운터만
        Assert.True(seen[130] > 0);                                                    // 기준(60초)을 넘으면 켜진다
        Assert.True(seen[200] > seen[130]);                                            // 점점 짙어진다
        Assert.Equal(1.0, seen[300]);
        var s0 = Assert.Single(t.Suspects);
        Assert.Equal(AutoSuspicionReason.Missing, s0.Reason);
        Assert.Equal("도적", s0.Player.Job);

        t.Observe(end.AddSeconds(310), true, new[] { A, B, C, ghost });                // 돌아왔다
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void SomeoneJustPassingBy_IsNeverMissing()
    {
        var t = new AutoSuspicionTracker();
        var passer = P("도적", true);
        var end = Run(t, T0, 20, () => new[] { A, B, C, passer });                     // 30초가 안 되게 스쳤다
        Run(t, end.AddSeconds(10), 400, () => new[] { A, B, C });
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void Missing_DoesNotBuildUp_WhenFewOthersAreVisible()
    {
        var t = new AutoSuspicionTracker();
        var ghost = P("도적", true);
        var end = Run(t, T0, 40, () => new[] { A, B, ghost });
        Run(t, end.AddSeconds(10), 400, () => new[] { A });                            // 주변이 거의 비었다: 내가 혼자 멀어진 것일 수 있다
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void IdenticalLookingPlayers_AreLeftOut()
    {
        var t = new AutoSuspicionTracker();
        var twin1 = P("마법사", false); var twin2 = P("마법사", false);                // 표식이 같아 누구인지 구분할 수 없다
        Run(t, T0, 300, () => new[] { A, B, C, twin1, twin2 });
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void KeyOf_UsesRealmJobLevelScoreTitle()
    {
        var pc = new NearPcItem("아이라", "악몽 사냥꾼", 1.5, 100, "듀얼블레이드", 94164, false, false, false, false);
        Assert.Equal("아이라|듀얼블레이드|100|94164|악몽 사냥꾼", WatchedPlayer.KeyOf(pc));
    }
}
