namespace MobiMate.Tests;

/// <summary>
/// 오토 의심 표식 (2026-10-22 승운 요청): 1초마다 관찰해 증상별 카운터를 쌓고, 기준을 넘으면 켜져 점점 짙어지며, 해소 요인이 생기면 바로 0으로 풀린다.
/// 증상: 누워 있음(부활 안 누름) · 혼자 전투 안 함 · 혼자 연주 · 혼자 멀리 떨어짐 · 주변에서 사라짐.
/// </summary>
public class AutoSuspicionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>distance: 내 기준 거리. 움직이는 사람은 매 초 거리가 달라진다(wobble).</summary>
    private static WatchedPlayer P(string job, bool combat, double distance = 5, bool playing = false, int level = 100, long score = 100000) =>
        new($"아이라|{job}|{level}|{score}|", "아이라", job, level, score, null, combat, distance, playing);

    /// <summary>1초마다 total초 동안 list(초)를 넣고 마지막 관찰 시각을 돌려준다. list가 초마다 다른 목록을 줄 수 있다.</summary>
    private static DateTimeOffset Run(AutoSuspicionTracker t, DateTimeOffset from, int totalSec, Func<int, IReadOnlyList<WatchedPlayer>> list, bool inInstance = true)
    {
        var now = from;
        for (var s = 0; s <= totalSec; s++) { now = from.AddSeconds(s); t.Observe(now, inInstance, list(s)); }
        return now;
    }

    /// <summary>전투 중인 멤버들: 매 초 거리가 조금씩 바뀐다(싸우며 움직이는 중).</summary>
    private static IReadOnlyList<WatchedPlayer> Fighters(int s) => new[]
    {
        P("전사", true, 4 + (s % 2) * 0.6), P("궁수", true, 6 - (s % 3) * 0.5), P("힐러", true, 7 + (s % 2) * 0.9),
    };

    /// <summary>대부분(75%)만 싸우는 장면: 싸우는 셋 + 가만히 서 있는 한 명(도적). 도적의 입장에서는 전원 전투, 마법사의 입장에서는 4명 중 3명이 전투.</summary>
    private static IReadOnlyList<WatchedPlayer> Mostly(int s) => Fighters(s).Append(P("도적", false, 12 + (s % 4) * 0.7)).ToList();

    private static AutoSuspect? Of(AutoSuspicionTracker t, string job) => t.Suspects.FirstOrDefault(x => x.Player.Job == job);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(29, 0.0)]      // 표시 기준(30초) 전: 아직 아무 표시도 없다
    [InlineData(30, 0.15)]     // 기준을 넘는 순간 약하게 켜진다
    [InlineData(90, 0.575)]    // 중간
    [InlineData(150, 1.0)]     // 가장 짙은 기준
    [InlineData(500, 1.0)]
    public void IntensityOf_GrowsFromShowThresholdToFull(int seconds, double expected) =>
        Assert.Equal(expected, AutoSuspicionTracker.IntensityOf(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(150)), 3);

    [Fact]
    public void Idle_WhenAllOthersFight_ShowsAfterTenSeconds_AndIsFullAtSixty()
    {
        var t = new AutoSuspicionTracker();
        var seen = new Dictionary<int, double>();
        for (var s = 0; s <= 200; s++)
        {
            // 이 사람은 움직인다(거리가 바뀐다) → 누워 있음이 아니라 "혼자 전투 안 함". 다른 사람은 전원 전투 중.
            t.Observe(T0.AddSeconds(s), true, Fighters(s).Append(P("마법사", false, 9 + (s % 4) * 0.7)).ToList());
            seen[s] = Of(t, "마법사")?.Intensity ?? 0;
        }
        Assert.Equal(0, seen[9]);                                     // 10초 전: 속으로만 관찰
        Assert.InRange(seen[10], 0.1, 0.2);                           // 전원 전투 중인데 혼자 안 함: 10초에 옅게 켜진다
        Assert.True(seen[30] > seen[10]);
        Assert.Equal(1.0, seen[60]);
        Assert.Equal(AutoSuspicionReason.Idle, Of(t, "마법사")!.Reason);
    }

    [Fact]
    public void Idle_WhenOnlyMostOthersFight_KeepsTheLongerThirtySecondRule()
    {
        var t = new AutoSuspicionTracker();
        var seen = new Dictionary<int, double>();
        for (var s = 0; s <= 200; s++)
        {
            t.Observe(T0.AddSeconds(s), true, Mostly(s).Append(P("마법사", false, 9 + (s % 4) * 0.7)).ToList());
            seen[s] = Of(t, "마법사")?.Intensity ?? 0;
        }
        Assert.Equal(0, seen[29]);                                    // 29초: 카운터만 쌓인 상태
        Assert.InRange(seen[30], 0.1, 0.2);                           // 30초에 옅게 켜진다
        Assert.True(seen[60] > seen[30] && seen[100] > seen[60]);     // 점점 짙어진다
        Assert.Equal(1.0, seen[150]);
    }

    // ───── 2026-10-24 모델: 처음부터 모두 의심, 사람다운 행동이 보이면 무활동 시계를 0으로 ─────

    private static WatchedPlayer Q(string job, double distance, bool robe = false, bool hood = false, bool weaponHidden = false, int clothes = 5) =>
        new($"아이라|{job}|100|100000|", "아이라", job, 100, 100000, null, false, distance, false, robe, hood, weaponHidden, clothes);

    [Fact]
    public void FirstThirtySeconds_AreObservedSilently_WhenOnlyMostFight_ThenShownOnlyIfNeverCleared()
    {
        var t = new AutoSuspicionTracker();
        var hidden = true;
        for (var s = 0; s <= 40; s++)
        {
            t.Observe(T0.AddSeconds(s), true, Mostly(s).Append(P("마법사", false, 9 + (s % 4) * 0.7)).ToList());
            if (s < 30 && Of(t, "마법사") != null) hidden = false;
        }
        Assert.True(hidden);                                  // 30초 전에는 깜빡이지 않는다
        Assert.NotNull(Of(t, "마법사"));                      // 30초가 지나도 벗겨지지 않으면 표시
    }

    [Fact]
    public void ToggleOfAppearanceOrInstrument_ProvesAHumanAndClearsAtOnce()
    {
        var t = new AutoSuspicionTracker();
        for (var s = 0; s <= 50; s++)
            t.Observe(T0.AddSeconds(s), true, Fighters(s).Append(Q("마법사", 9 + (s % 4) * 0.7)).ToList());
        Assert.NotNull(Of(t, "마법사"));                      // 50초째 의심 중
        t.Observe(T0.AddSeconds(51), true, Fighters(51).Append(Q("마법사", 9, hood: true)).ToList());   // 후드를 썼다 = 사람이 만졌다
        Assert.Null(Of(t, "마법사"));
        Assert.Equal(0, Of(t, "마법사")?.For.TotalSeconds ?? 0);
    }

    [Fact]
    public void MovingAlone_ClearsOnlyWhileAnotherPlayerStaysStill()
    {
        // 다른 사람 하나가 가만히 있으니 내가 움직이는 게 아니다 → 거리가 변하는 사람은 직접 움직인 것
        var t = new AutoSuspicionTracker();
        var still = P("전사", true, 4);
        for (var s = 0; s <= 50; s++)
            t.Observe(T0.AddSeconds(s), true, new[] { still, P("궁수", true, 6), P("힐러", true, 7), Q("마법사", 9 + (s % 4) * 0.7) });
        Assert.Null(Of(t, "마법사"));
        // 모두의 거리가 같이 변하면(내가 움직임) 움직임은 증거가 아니다 → 의심이 유지된다
        var t2 = new AutoSuspicionTracker();
        for (var s = 0; s <= 50; s++)
            t2.Observe(T0.AddSeconds(s), true, Fighters(s).Append(Q("마법사", 9 + (s % 4) * 0.7)).ToList());
        Assert.NotNull(Of(t2, "마법사"));
    }

    [Fact]
    public void WhenEveryoneIsQuiet_TheClockStopsInsteadOfCountingOrClearing()
    {
        var t = new AutoSuspicionTracker();
        // 40초: 다들 싸우는데 마법사만 안 한다 → 의심
        for (var s = 0; s <= 40; s++)
            t.Observe(T0.AddSeconds(s), true, Fighters(s).Append(Q("마법사", 9 + (s % 4) * 0.7)).ToList());
        var before = Of(t, "마법사")!.For.TotalSeconds;
        // 이후 60초: 모두 전투를 멈춘 대기 구간 → 시계는 멈춘다(늘지도 줄지도 않는다)
        for (var s = 41; s <= 100; s++)
            t.Observe(T0.AddSeconds(s), true, new[] { P("전사", false, 4 + (s % 2) * 0.6), P("궁수", false, 6 - (s % 3) * 0.5), P("힐러", false, 7 + (s % 2) * 0.9), Q("마법사", 9 + (s % 4) * 0.7) });
        Assert.Equal(before, Of(t, "마법사")!.For.TotalSeconds, 0);
    }

    [Fact]
    public void ClearedPlayer_BecomesSuspectAgainAfterAnotherQuietStretch()
    {
        var t = new AutoSuspicionTracker();
        for (var s = 0; s <= 40; s++) t.Observe(T0.AddSeconds(s), true, Fighters(s).Append(Q("마법사", 9 + (s % 4) * 0.7)).ToList());
        Assert.NotNull(Of(t, "마법사"));
        t.Observe(T0.AddSeconds(41), true, Fighters(41).Append(new WatchedPlayer("아이라|마법사|100|100000|", "아이라", "마법사", 100, 100000, null, true, 9)).ToList());   // 한 번 싸웠다 → 풀림
        Assert.Null(Of(t, "마법사"));
        for (var s = 42; s <= 100; s++) t.Observe(T0.AddSeconds(s), true, Fighters(s).Append(Q("마법사", 9 + (s % 4) * 0.7)).ToList());
        Assert.NotNull(Of(t, "마법사"));                      // 이후 다시 오래 가만히 있으면 새로 의심
    }

    [Fact]
    public void NeedsFastSampling_OnlyWhileSomeConditionIsCounting()
    {
        var t = new AutoSuspicionTracker();
        Run(t, T0, 20, s => Fighters(s));                              // 모두 평범하게 싸운다
        Assert.False(t.NeedsFastSampling);                             // 평소: 드문 간격으로 충분
        var end = Run(t, T0.AddSeconds(21), 3, s => Fighters(21 + s).Append(P("마법사", false, 9 + (s % 4) * 0.7)).ToList());
        Assert.True(t.NeedsFastSampling);                              // 한 명이 전투를 멈추는 순간부터 1초 간격
        t.Observe(end.AddSeconds(1), true, Fighters(30).Append(P("마법사", true, 9)).ToList());
        Assert.False(t.NeedsFastSampling);                             // 풀리면 다시 드물게
    }

    [Fact]
    public void CountersFollowElapsedTime_EvenWhenSamplingIsSparse()
    {
        var t = new AutoSuspicionTracker();
        // 5초 간격(평소 간격)으로 넣어도 흐른 시간만큼 센다
        for (var s = 0; s <= 100; s += 5)
            t.Observe(T0.AddSeconds(s), true, Fighters(s).Append(P("마법사", false, 9 + (s % 4) * 0.7)).ToList());
        var s0 = Of(t, "마법사")!;
        Assert.Equal(100, s0.For.TotalSeconds, 0);                     // 처음 관찰에서 시작해 100초
        Assert.Equal(AutoSuspicionReason.Idle, s0.Reason);
    }

    [Fact]
    public void LyingStill_WhileOthersFight_IsTheStrongestSign_AndShowsEarlier()
    {
        var t = new AutoSuspicionTracker();
        // 누운 사람: 전투 안 함 + 거리가 전혀 변하지 않는다 (다른 사람들은 계속 움직임)
        var lying = P("마법사", false, 8.0);
        var seen = new Dictionary<int, AutoSuspect?>();
        for (var s = 0; s <= 100; s++)
        {
            t.Observe(T0.AddSeconds(s), true, Mostly(s).Append(lying).ToList());
            seen[s] = Of(t, "마법사");
        }
        Assert.Null(seen[18]);                                        // 정지 확인(8초) 뒤부터 쌓여 20초 기준 전에는 없다
        Assert.NotNull(seen[40]);
        Assert.Equal(AutoSuspicionReason.Lying, seen[40]!.Reason);   // 혼자 전투 안 함보다 확실한 "누워 있음"으로 본다
        Assert.True(seen[60]!.Intensity < seen[100]!.Intensity);      // 점점 짙어진다
        Assert.True(seen[100]!.Intensity >= 0.99);                   // 정지 확인(8초) 뒤부터 90초 쌓이면 만점
    }

    [Fact]
    public void ResolvingFactor_ClearsImmediately_AndTheCounterStartsOver()
    {
        var t = new AutoSuspicionTracker();
        var lying = P("마법사", false, 8.0);
        var end = Run(t, T0, 60, s => Mostly(s).Append(lying).ToList());
        Assert.NotNull(Of(t, "마법사"));

        t.Observe(end.AddSeconds(1), true, Mostly(61).Append(P("마법사", true, 8.0)).ToList());   // 부활해서 전투를 시작했다
        Assert.Null(Of(t, "마법사"));                                                                // 바로 풀린다

        Run(t, end.AddSeconds(2), 15, s => Mostly(62 + s).Append(lying).ToList());                 // 다시 누워도 0부터
        Assert.Null(Of(t, "마법사"));
    }

    [Fact]
    public void LyingNeedsTheSceneToMove_OtherwiseItCannotTellStillFromQuiet()
    {
        var t = new AutoSuspicionTracker();
        // 모두 가만히(거리 변화 없음) 서 있고 한 명만 전투 안 함: 움직임이 없어 "누워 있음"은 모른다 → 혼자 전투 안 함(Idle) 기준을 따른다
        var still = new[] { P("전사", true, 4), P("궁수", true, 6), P("힐러", true, 7), P("마법사", false, 8) };
        Run(t, T0, 8, _ => still);
        var s0 = Of(t, "마법사");
        Assert.Null(s0);                                              // 8초: 전원 전투 기준(10초) 전, Lying은 판정 불가
        Run(t, T0.AddSeconds(9), 10, _ => still);
        Assert.Equal(AutoSuspicionReason.Idle, Of(t, "마법사")!.Reason);   // 움직임이 없으니 누워 있음이 아니라 혼자 전투 안 함
    }

    [Fact]
    public void NotSuspected_WhenAnotherPlayerIsAlsoNotFighting_OrFewerThanTwoOthers()
    {
        var t = new AutoSuspicionTracker();
        Run(t, T0, 300, s => new[] { P("전사", true, 4 + s % 2), P("궁수", true, 6), P("마법사", false, 8), P("도적", false, 9) });   // 다 같이 쉬는 것
        Assert.Empty(t.Suspects);
        var t2 = new AutoSuspicionTracker();
        Run(t2, T0, 300, s => new[] { P("전사", true, 4 + s % 2), P("마법사", false, 8) });    // 상대가 한 명뿐이면 비교가 안 된다
        Assert.Empty(t2.Suspects);
    }

    [Fact]
    public void PlayingMusic_WhileOthersFight_IsSuspected_AndStopsWhenTheMusicStops()
    {
        var t = new AutoSuspicionTracker();
        var bard = P("음유시인", false, 8, playing: true);
        var end = Run(t, T0, 60, s => Fighters(s).Append(bard).ToList());
        Assert.Contains(t.Suspects, x => x.Player.Job == "음유시인" && x.Reason is AutoSuspicionReason.Playing or AutoSuspicionReason.Lying);
        t.Observe(end.AddSeconds(1), true, Fighters(61).Append(P("음유시인", true, 8, playing: false)).ToList());
        Assert.Null(Of(t, "음유시인"));
    }

    [Fact]
    public void FarFromTheGroup_WhileOthersStayClose_IsSuspected_AndClearsWhenTheyCatchUp()
    {
        var t = new AutoSuspicionTracker();
        // 모두 가까이(≤12m)인데 이 사람만 25m 밖. 전투 상태는 같다(전투 중)이라 Far만 걸린다
        var end = Run(t, T0, 90, s => Fighters(s).Append(P("도적", true, 25 + (s % 2) * 0.5)).ToList());
        var s0 = Of(t, "도적")!;
        Assert.Equal(AutoSuspicionReason.Far, s0.Reason);
        Assert.True(s0.Intensity > 0.3);
        t.Observe(end.AddSeconds(1), true, Fighters(91).Append(P("도적", true, 10)).ToList());   // 따라왔다
        Assert.Null(Of(t, "도적"));
    }

    [Fact]
    public void LongTimeSeenThenGone_BuildsAsMissing_AndReturningClearsAtOnce()
    {
        var t = new AutoSuspicionTracker();
        var ghost = P("도적", true, 10);
        var end = Run(t, T0, 20, s => Fighters(s).Append(ghost).ToList());                  // 20초 함께 보였다
        var seen = new Dictionary<int, double>();
        for (var s = 1; s <= 200; s++) { t.Observe(end.AddSeconds(s), true, Fighters(20 + s)); seen[s] = Of(t, "도적")?.Intensity ?? 0; }
        Assert.Equal(0, seen[40]);                                                          // 안 보인 지 얼마 안 됨
        Assert.True(seen[60] > 0);                                                          // 기준(45초)을 넘으면 켜진다
        Assert.True(seen[120] > seen[60]);
        Assert.Equal(1.0, seen[200]);
        Assert.Equal(AutoSuspicionReason.Missing, Of(t, "도적")!.Reason);

        t.Observe(end.AddSeconds(201), true, Fighters(221).Append(ghost).ToList());          // 돌아왔다
        Assert.Null(Of(t, "도적"));
    }

    [Fact]
    public void SomeoneJustPassingBy_IsNeverMissing()
    {
        var t = new AutoSuspicionTracker();
        var passer = P("도적", true, 10);
        var end = Run(t, T0, 5, s => Fighters(s).Append(passer).ToList());                  // 10초가 안 되게 스쳤다
        Run(t, end.AddSeconds(1), 400, s => Fighters(5 + s));
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void Missing_DoesNotBuildUp_WhenFewOthersAreVisible()
    {
        var t = new AutoSuspicionTracker();
        var ghost = P("도적", true, 10);
        var end = Run(t, T0, 20, s => new[] { P("전사", true, 4 + s % 2), ghost });
        Run(t, end.AddSeconds(1), 400, s => new[] { P("전사", true, 4 + s % 2) });         // 이제 주변이 거의 비었다: 내가 혼자 멀어진 것일 수 있다
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void LeavingTheInstance_ResetsEverything()
    {
        var t = new AutoSuspicionTracker();
        var lying = P("마법사", false, 8.0);
        var end = Run(t, T0, 60, s => Mostly(s).Append(lying).ToList());
        Assert.NotNull(Of(t, "마법사"));
        t.Observe(end.AddSeconds(1), false, Mostly(61).Append(lying).ToList());           // 던전 밖
        Assert.Empty(t.Suspects);
        Run(t, end.AddSeconds(2), 10, s => Mostly(62 + s).Append(lying).ToList());        // 처음부터 다시
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void AGapLongerThanTenSeconds_RestartsTheCounters()
    {
        var t = new AutoSuspicionTracker();
        var lying = P("마법사", false, 8.0);
        var end = Run(t, T0, 40, s => Fighters(s).Append(lying).ToList());
        Assert.NotNull(Of(t, "마법사"));
        t.Observe(end.AddSeconds(30), true, Fighters(70).Append(lying).ToList());           // 관찰이 30초 끊겼다: 그 사이는 모른다
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void IdenticalLookingPlayers_AreLeftOut()
    {
        var t = new AutoSuspicionTracker();
        var twin1 = P("마법사", false, 8); var twin2 = P("마법사", false, 9);               // 표식이 같아 누구인지 구분할 수 없다
        Run(t, T0, 200, s => Fighters(s).Append(twin1).Append(twin2).ToList());
        Assert.Empty(t.Suspects);
    }

    [Fact]
    public void KeyOf_UsesRealmJobLevelScoreTitle_AndFromReadsDistanceAndPerformance()
    {
        var pc = new NearPcItem("아이라", "악몽 사냥꾼", 1.5, 100, "듀얼블레이드", 94164, false, false, false, false, Performance: new PerformanceInfo(true));
        Assert.Equal("아이라|듀얼블레이드|100|94164|악몽 사냥꾼", WatchedPlayer.KeyOf(pc));
        var w = WatchedPlayer.From(pc);
        Assert.Equal(1.5, w.Distance);
        Assert.True(w.Playing);
    }

    [Fact]
    public void ObservationsAtTheSameOrAnEarlierTime_AreIgnored_SoTwoFeedersDoNotDoubleCount()
    {
        var t = new AutoSuspicionTracker();
        for (var s = 0; s <= 40; s++)
        {
            var list = Fighters(s).Append(P("마법사", false, 9 + (s % 4) * 0.7)).ToList();
            var now = T0.AddSeconds(s);
            t.Observe(now, true, list);
            t.Observe(now, true, list);                        // 같은 시각에 또 들어온 읽기(화면 조회)
            t.Observe(now.AddSeconds(-1), true, list);         // 늦게 도착한 옛 읽기
        }
        var s0 = Of(t, "마법사")!;
        Assert.InRange(s0.For.TotalSeconds, 39, 40);           // 40초 흘렀으면 40초, 중복·역순이 더해지지 않는다
    }

    [Fact]
    public void CombatScoreDrift_IsTheSamePerson_NotOneMissingAndOneNew()
    {
        // 던전 안에서 버프로 전투력이 조금 바뀌어도 같은 사람이다(2026-10-22: 같은 사람이 2명으로 보였다)
        var t = new AutoSuspicionTracker();
        var now = Run(t, T0, 60, s => Fighters(s).Append(P("마법사", false, 9 + (s % 4) * 0.7, score: 50403)).ToList());
        var idleBefore = Of(t, "마법사")!.For;
        now = Run(t, now.AddSeconds(1), 30, s => Fighters(s).Append(P("마법사", false, 9 + (s % 4) * 0.7, score: 50571)).ToList());
        Assert.DoesNotContain(t.Suspects, x => x.Reason == AutoSuspicionReason.Missing);   // 옛 표식이 "사라짐"으로 남지 않는다
        Assert.Single(t.Suspects, x => x.Player.Job == "마법사");
        Assert.True(Of(t, "마법사")!.For > idleBefore);                                  // 카운터가 이어진다
    }

    [Fact]
    public void ABigScoreDifference_IsADifferentPerson()
    {
        var t = new AutoSuspicionTracker();
        var now = Run(t, T0, 30, s => Fighters(s).Append(P("마법사", true, 9 + (s % 4) * 0.7, score: 50000)).ToList());
        now = Run(t, now.AddSeconds(1), 5, s => Fighters(s).Append(P("마법사", true, 9 + (s % 4) * 0.7, score: 90000)).ToList());
        // 전투력이 크게 다르면 다른 사람: 옛 사람은 사라진 것으로 추적된다(표시 기준 전이라 아직 의심 표시는 없다)
        Assert.DoesNotContain(t.Suspects, x => x.Player.CombatScore == 90000 && x.Reason == AutoSuspicionReason.Missing);
    }

    [Fact]
    public void AWholeNewParty_DoesNotLeaveTheOldPartyBehindAsMissing()
    {
        // 어비스는 나 포함 4명: 방이 바뀌면 이전 파티원 3명이 "안 보임"으로 올라오던 문제(2026-10-23)
        var t = new AutoSuspicionTracker();
        var now = Run(t, T0, 40, s => Fighters(s));
        var next = new[] { P("마법사", true, 5), P("도적", true, 7), P("검술사", true, 9) };
        now = Run(t, now.AddSeconds(1), 120, s => next.Select(p => p with { Distance = p.Distance + (s % 2) * 0.5 }).ToList());
        Assert.DoesNotContain(t.Suspects, x => x.Reason == AutoSuspicionReason.Missing);
    }

    [Fact]
    public void OneMemberLeaving_OfTheSameParty_IsStillMissing()
    {
        var t = new AutoSuspicionTracker();
        var now = Run(t, T0, 30, s => Fighters(s));
        now = Run(t, now.AddSeconds(1), 120, s => Fighters(s).Take(2).ToList());   // 힐러가 사라졌다, 나머지 둘은 그대로
        Assert.Contains(t.Suspects, x => x.Reason == AutoSuspicionReason.Missing && x.Player.Job == "힐러");
    }
}
