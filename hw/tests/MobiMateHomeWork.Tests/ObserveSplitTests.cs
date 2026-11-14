using MobiMate.Tests;
using Xunit.Abstractions;

namespace MobiMateHomeWork.Tests;

/// <summary>
/// CLI 호출 줄이기(hw/fix-cli-load): 숙제 관찰의 빠른/전체 두 갈래, 부분 관찰이 만드는 null이 오판을 일으키지 않음,
/// 감시가 읽은 캐릭터·맵의 캐시 재사용, 접속 1분 호출 수 측정.
/// </summary>
public class ObserveSplitTests(ITestOutputHelper output)
{
    private static HomeworkCard Card(AppHarness h, string key, string id) => h.Homework.GetBoard(key).Cards.Single(c => c.Id == id);

    /// <summary>가짜 CLI 호출 시작 기록에서 명령별 횟수 (skip = 앞에서 건너뛸 로그 줄 수).</summary>
    private static Dictionary<string, int> Calls(FakeGame game, int skip = 0) =>
        FakeCliLogReader.ReadLines(game.LogPath).Skip(skip)
            .Select(l => l.Split('\t')).Where(f => f.Length > 3 && f[2] == "start")
            .GroupBy(f => f[3]).ToDictionary(g => g.Key, g => g.Count());

    private static int LogLines(FakeGame game) => FakeCliLogReader.ReadLines(game.LogPath).Count;

    // ── 주기 결정 ──

    [Theory]
    [InlineData(20, 60, 20.0, null, true)]    // 접속 직후 첫 관찰은 전체
    [InlineData(20, 60, 40.0, 20.0, false)]
    [InlineData(20, 60, 59.9, 20.0, true)]    // 60초 격자(타이머가 빠른 주기의 절반까지 일찍 와도)에 닿으면 전체
    [InlineData(20, 60, 60.0, 20.0, true)]
    [InlineData(20, 60, 80.0, 60.0, false)]
    [InlineData(20, 60, 120.0, 60.0, true)]
    [InlineData(20, 60, 80.0, 65.0, false)]   // 맵 이동 전체 관찰(65초)이 격자 안이라 다음 격자(120)까지 빠른 관찰
    [InlineData(20, 60, 120.0, 65.0, true)]
    [InlineData(20, 60, 100.0, 20.0, true)]   // 접속이 늦어 격자를 건너뛰었으면 다음 틱에 바로
    [InlineData(0, 60, 5.0, 1.0, true)]       // 빠른 관찰 끔: 전체만
    [InlineData(20, 0, 20.0, null, false)]    // 전체 관찰 끔: 빠른 것만
    [InlineData(60, 20, 61.0, 60.0, true)]    // 전체 주기가 더 짧으면 항상 전체
    [InlineData(20, 20, 41.0, 20.0, true)]
    public void NextIsFull_FollowsTheTwoIntervals(double fast, double full, double elapsed, double? lastFull, bool expected) =>
        Assert.Equal(expected, HomeworkObserver.NextIsFull(TimeSpan.FromSeconds(fast), TimeSpan.FromSeconds(full), TimeSpan.FromSeconds(elapsed), lastFull is { } s ? TimeSpan.FromSeconds(s) : null));

    [Fact]
    public void Options_DefaultsAndCacheTtlPerCommand()
    {
        var o = new AppOptions();
        Assert.Equal(20, o.HomeworkObserveIntervalSeconds);
        Assert.Equal(60, o.HomeworkFullObserveIntervalSeconds);
        Assert.Equal(TimeSpan.FromSeconds(5), o.CacheTtlFor("get_my_info"));
        Assert.Equal(TimeSpan.FromSeconds(5), o.CacheTtlFor("get_current_environment"));
        Assert.Equal(TimeSpan.FromSeconds(3), o.CacheTtlFor("get_quests"));
        Assert.Equal(TimeSpan.FromSeconds(3), o.CacheTtlFor("get_currencies"));   // 전환 직후 재화는 재사용 대상이 아니다
        o.QueryCacheTtlSeconds = 0;                                              // 캐시를 끄면 재사용 유지 시간도 쓰지 않는다
        Assert.Equal(TimeSpan.Zero, o.CacheTtlFor("get_my_info"));
    }

    // ── 빠른 관찰은 3개만 읽는다 ──

    [Fact]
    public async Task FastObservation_ReadsOnlyQuestsCharacterAndMap_FullReadsAllTen()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game);
        // 감시가 캐릭터를 아직 모르면(첫 관찰) 재화로 키를 정해야 하니 전체로 승격한다
        Assert.Equal("아이라_격투가", await h.Watcher.ObserveAsync(CancellationToken.None, full: false));
        Assert.Contains("get_currencies", Calls(game).Keys);

        await h.RecordAsync();                          // 감시가 캐릭터를 아는 상태가 된다
        var from = LogLines(game);
        Assert.Equal("아이라_격투가", await h.Watcher.ObserveAsync(CancellationToken.None, full: false));
        var fast = Calls(game, from);
        Assert.Equal(["get_current_environment", "get_my_info", "get_quests"], fast.Keys.OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(fast.Values, n => Assert.Equal(1, n));

        from = LogLines(game);
        await h.Watcher.ObserveAsync(CancellationToken.None, full: true);
        Assert.Equal(10, Calls(game, from).Count);   // 성장 가이드(get_growth_guide)가 늘었다
    }

    // ── 부분 관찰(null)이 판정을 해치지 않는다 ──

    [Fact]
    public async Task FastObservation_StillJudgesQuestVanish_ButDoesNotJudgeMissions()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game);
        var key = FakeGame.SampleKey;
        await h.RecordAsync();

        // 전체 관찰: 뱅가드 브리치(questVanish)를 목격한다
        await h.Watcher.ObserveAsync(CancellationToken.None, full: true);
        Assert.Equal(HomeworkCardStatus.Pending, Card(h, key, "weekly_vanguard_breach").Status);

        // 퀘스트가 목록에서 사라지고 일일 미션도 모두 달성: 빠른 관찰은 퀘스트 소멸만 반영한다
        game.Edit("get_quests", n => { var a = n.AsArray(); for (var i = a.Count - 1; i >= 0; i--) if (a[i]!["QuestTitle"]!.GetValue<string>().Contains("브리치")) a.RemoveAt(i); });
        game.Edit("get_daily_missions", n => { foreach (var m in n.AsArray()) { m!["CurrentCount"] = m["GoalCount"]!.GetValue<int>(); m["IsCompleted"] = true; } });
        await h.Watcher.ObserveAsync(CancellationToken.None, full: false);
        // 뱅가드(끊김 없는 관찰 항목)는 사라짐을 확인 대기 시간(30초) 뒤에도 계속 없을 때 완료로 본다: 첫 관찰은 대기만 시작한다.
        // 완료 확정은 Core 테스트(HomeworkAutoCheckTests)가 시계를 움직여 확인한다.
        Assert.Equal(HomeworkCardStatus.Pending, Card(h, key, "weekly_vanguard_breach").Status);
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(h, key, "daily_gather_3").Status);      // 미션은 전체 관찰에서
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(h, key, "daily_missions_all").Status);

        await h.Watcher.ObserveAsync(CancellationToken.None, full: true);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(h, key, "daily_gather_3").Status);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(h, key, "daily_missions_all").Status);
    }

    [Fact]
    public async Task FastObservation_DoesNotTouchRaidTokenBaseline_FullObservationStillCatchesTheIncrease()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game);
        var key = FakeGame.SampleKey;
        await h.RecordAsync();
        await h.Watcher.ObserveAsync(CancellationToken.None, full: true);   // 하프 조각 0개 기준값

        game.Edit("get_items", n => n.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(
            "{\"Location\":\"inventory\",\"DisplayName\":\"선율을 품은 하프 조각\",\"Category\":\"Material\",\"CategoryDisplayName\":\"재료\",\"Count\":1,\"IsLocked\":false}")));
        await h.Watcher.ObserveAsync(CancellationToken.None, full: false);
        await h.Watcher.ObserveAsync(CancellationToken.None, full: false);
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(h, key, "raid_airel").Status);   // 아이템을 읽지 않았다: 판정도 기준값 갱신도 없음

        await h.Watcher.ObserveAsync(CancellationToken.None, full: true);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(h, key, "raid_airel").Status);      // 기준값이 그대로라 늘어난 것을 알아챈다
    }

    [Fact]
    public async Task FastObservation_UsesTheMap_RaidAreaEntryShowsInProgressWithoutWaitingForTheFullRead()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game);
        await h.RecordAsync();
        await h.Watcher.ObserveAsync(CancellationToken.None, full: true);
        game.SetMap("먼 바다의 춤추는 바람");   // 에이렐 전용 지역
        await h.Watcher.ObserveAsync(CancellationToken.None, full: false);
        Assert.Equal(HomeworkCardStatus.InProgress, Card(h, FakeGame.SampleKey, "raid_airel").Status);   // 진입은 진행 중일 뿐, 완료는 미션 카운터로 (2026-10-22)
    }

    [Fact]
    public async Task NullsFromAPartialObservation_NeverReadAsDisappearance()
    {
        // 판정기에 직접: 퀘스트가 null(읽지 않음·실패)이면 사라짐 판정을 건너뛰고, 빈 목록도 사라짐의 근거가 아니다
        await using var h = await AppHarness.StartAsync();
        var key = FakeGame.SampleKey;
        var env = new EnvironmentInfo(null, null, null, "콜헨");
        var withQuest = new List<QuestItem> { new("<color=orange>[긴급 의뢰]</color> 뱅가드  브리치", "Weekly", "주간", [new("a", false)]) };
        h.Homework.Evaluate(key, new HomeworkObservation(Quests: withQuest, Environment: env));                  // 목격
        h.Homework.Evaluate(key, new HomeworkObservation(Environment: env));                                      // 퀘스트 null
        h.Homework.Evaluate(key, new HomeworkObservation(Quests: new List<QuestItem>(), Environment: env));      // 빈 목록
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(h, key, "weekly_vanguard_breach").Status);
        // 미션·가공·재화·아이템·활동이 모두 null이어도 아무 숙제도 자동 완료되지 않는다
        Assert.DoesNotContain(h.Homework.GetBoard(key).Cards, c => c.Status == HomeworkCardStatus.AutoDone);
    }

    [Fact]
    public async Task FastObservation_AfterACharacterSwitch_UpgradesToFull_SoTheKeyIsDecidedByTheWallet()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game);
        await h.RecordAsync();
        game.SetCharacter(Chars.Rogue);                       // 감시가 아직 모르는 사이에 바뀜
        var from = LogLines(game);
        Assert.Equal("바람_도적", await h.Watcher.ObserveAsync(CancellationToken.None, full: false));
        Assert.Contains("get_currencies", Calls(game, from).Keys);   // 재화를 새로 읽어 캐릭터를 확정했다
    }

    // ── 캐시 재사용 ──

    [Fact]
    public async Task ObservationReusesWhatTheWatcherJustRead_ButTheWatcherItselfReadsFresh()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game, configure: o => { o.QueryCacheTtlSeconds = 3; o.ReuseCacheTtlSeconds = 5; });
        await h.RecordAsync();                                // 헤더가 get_my_info·환경을 읽는다
        var from = LogLines(game);
        await h.Watcher.ObserveAsync(CancellationToken.None, full: false);
        Assert.Equal(["get_quests"], Calls(game, from).Keys);   // 캐릭터·맵은 캐시 재사용: 퀘스트 한 번만

        // 3초가 지나면 감시(기본 유지 시간 3초)는 다시 읽지만, 재사용하는 쪽(5초)은 아직 캐시를 쓴다
        await Task.Delay(3300);
        from = LogLines(game);
        await h.Queries.Get<CharacterInfo>("get_my_info", default, h.Options.QueryCacheTtl);
        Assert.Equal(1, Calls(game, from).GetValueOrDefault("get_my_info"));
        await h.Queries.Get<CharacterInfo>("get_my_info");
        Assert.Equal(1, Calls(game, from).GetValueOrDefault("get_my_info"));
    }

    [Fact]
    public async Task Watcher_NoticesACharacterSwitchPromptly_EvenThoughOthersReuseTheCharacterFor5Seconds()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game, configure: o =>
        {
            o.QueryCacheTtlSeconds = 0.5; o.ReuseCacheTtlSeconds = 30; o.WatchIntervalSeconds = 0.7;   // 재사용 30초여도 감시는 0.5초 기준으로 읽는다
            o.CombatRecheckDelaySeconds = 0.1;
        });
        await h.Status.CheckNowAsync();
        await Wait.UntilAsync(() => h.Log.Headers.Any(x => !x.Selecting), "감시 시작");
        game.SetCharacter(Chars.Rogue);
        await Wait.UntilAsync(() => h.Log.Headers.Any(x => x.Job == "도적"), "전환 감지", TimeSpan.FromSeconds(5));   // 30초 캐시에 가렸다면 안 잡힌다
    }

    // ── 접속 1분 호출 수 측정 ──

    /// <summary>
    /// 접속 상태 1분의 명령별 호출 수. 1분을 그대로 기다리는 대신 모든 주기와 캐시 유지 시간·관찰 창을 같은 배율로 줄여 돌려 "1분"으로 본다
    /// (가짜 CLI 프로세스 시작 시간은 줄지 않으므로 배율이 높을수록 호출이 겹쳐 캐시 적중이 달라진다). 환경변수 MM_MEASURE_SCALE=1이면 실제 1분.
    /// </summary>
    private async Task<Dictionary<string, int>> MeasureAsync(string label, double scale, Action<AppOptions, double> configure)
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game, configure: o =>
        {
            o.StatusIntervalSeconds = 10 / scale; o.WatchIntervalSeconds = 4 / scale; o.RecordIntervalSeconds = 60 / scale;
            o.QueryCacheTtlSeconds = 3 / scale; o.ReuseCacheTtlSeconds = 5 / scale;
            o.CombatRecheckDelaySeconds = 5 / scale;
            configure(o, scale);
        });
        await h.Status.CheckNowAsync();
        await Wait.UntilAsync(() => h.Log.Headers.Any(x => !x.Selecting), "감시 시작");
        var observer = h.Services.GetRequiredService<HomeworkObserver>();
        await Wait.UntilAsync(() => observer.FullRuns >= 1, "접속 직후 첫 전체 관찰", TimeSpan.FromSeconds(60));   // 시작 때 한 번 있는 읽기는 정상 상태가 아니니 창에서 뺀다
        await Task.Delay(TimeSpan.FromSeconds(1.0 / scale));
        var from = LogLines(game);
        await Task.Delay(TimeSpan.FromSeconds(60 / scale));
        var calls = Calls(game, from);
        var per = calls;   // 주기·유지 시간·창을 모두 같은 배율로 줄였으므로 창 안의 횟수가 곧 1분의 횟수다
        output.WriteLine($"[{label}] 배율 {scale}: 창 {60 / scale:0.#}초(=1분) {per.Values.Sum()}회  " + string.Join(", ", per.OrderByDescending(x => x.Value).Select(x => $"{x.Key}={x.Value}")));
        return per;
    }

    private static double Scale => double.TryParse(Environment.GetEnvironmentVariable("MM_MEASURE_SCALE"), out var s) && s >= 1 ? s : 4;

    [Fact]
    public async Task OneMinuteConnected_CliCalls_DropComparedToTheSingleFullObservationDesign()
    {
        var scale = Scale;
        // 이전 설계: 20초마다 전체 관찰, 캐릭터·맵은 3초 캐시로만 재사용
        var before = await MeasureAsync("이전", scale, (o, k) =>
        {
            o.HomeworkObserveIntervalSeconds = 0; o.HomeworkFullObserveIntervalSeconds = 20 / k; o.ReuseCacheTtlSeconds = 0;
        });
        var after = await MeasureAsync("이후", scale, (o, k) =>
        {
            o.HomeworkObserveIntervalSeconds = 20 / k; o.HomeworkFullObserveIntervalSeconds = 60 / k;
        });
        var b = before.Values.Sum();
        var a = after.Values.Sum();
        Assert.True(a <= b, $"줄지 않았다: 이전 {b} → 이후 {a}");
        if (scale <= 1.5)   // 실제 시간(배율 1)에서만 수치를 못 박는다. 배율을 올리면 가짜 CLI 시작 지연이 주기에 비해 커져 호출이 겹치는 정도가 달라진다.
        {
            Assert.True(a < b * 0.9, $"호출 수가 충분히 줄지 않았다: 이전 {b} → 이후 {a}");
            Assert.True(a <= 50, $"1분 {a}회 (목표 45 안팎)");
        }
        Assert.True(after.GetValueOrDefault("get_daily_missions") <= 2);   // 미션은 전체 관찰에서만(1분에 1번, 경계 여유 1)
        Assert.True(after.GetValueOrDefault("get_quests") >= 2);                   // 퀘스트는 빠른 관찰로도 읽는다
    }
}
