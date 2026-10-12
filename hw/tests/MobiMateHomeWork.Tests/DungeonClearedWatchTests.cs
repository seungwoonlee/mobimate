using MobiMate.Tests;

namespace MobiMateHomeWork.Tests;

/// <summary>
/// 요일 던전 클리어 즉시 반영: Cleared는 약 17초만 유지되므로 감시(4초)가 던전 안일 때만 활동을 읽어 전환 순간을 놓치지 않는다.
/// 던전 밖 평상시에는 get_activity를 추가로 읽지 않는다.
/// </summary>
public class DungeonClearedWatchTests
{
    private static Dictionary<string, int> Calls(FakeGame game) =>
        FakeCliLogReader.ReadLines(game.LogPath)
            .Select(l => l.Split('\t')).Where(f => f.Length > 3 && f[2] == "start")
            .GroupBy(f => f[3]).ToDictionary(g => g.Key, g => g.Count());

    private static Task<AppHarness> Start(FakeGame game) => AppHarness.StartAsync(game: game, configure: o =>
    {
        o.WatchIntervalSeconds = 0.2; o.QueryCacheTtlSeconds = 0.1; o.ReuseCacheTtlSeconds = 0.1;
    });

    private static void SetDungeon(FakeGame game, string state) =>
        game.Edit("get_activity", n => n["Dungeon"] = new System.Text.Json.Nodes.JsonObject { ["State"] = state, ["IsBossBattleInProgress"] = false });

    [Fact]
    public async Task OutsideDungeon_TheWatcherDoesNotReadActivity()
    {
        using var game = new FakeGame();
        game.SetMap("창백한 산");   // 사냥터는 던전이 아니다
        await using var h = await Start(game);
        await h.Status.CheckNowAsync();
        await Wait.UntilAsync(() => Calls(game).GetValueOrDefault("get_my_info") >= 3, "감시가 읽기 시작");
        var activityBefore = Calls(game).GetValueOrDefault("get_activity");   // 감시 시작 때 헤더를 새로 읽을 때의 1회는 원래 있던 호출
        var infoBefore = Calls(game).GetValueOrDefault("get_my_info");
        await Wait.UntilAsync(() => Calls(game).GetValueOrDefault("get_my_info") >= infoBefore + 8, "감시가 더 읽음");
        Assert.Equal(activityBefore, Calls(game).GetValueOrDefault("get_activity"));   // 던전 밖 평상시: 감시가 활동을 더 읽지 않는다
    }

    [Fact]
    public async Task InDayDungeonMap_ClearedIsCaught_CompletesHomeworkRightAway_AndLogs()
    {
        using var game = new FakeGame();
        game.SetMap("찬란한 유적 V");
        SetDungeon(game, "InProgress");
        await using var h = await Start(game);
        await h.Status.CheckNowAsync();
        var observer = h.Services.GetRequiredService<HomeworkObserver>();

        await Wait.UntilAsync(() => Calls(game).GetValueOrDefault("get_activity") >= 2, "던전 안에서는 활동도 읽는다");
        Assert.NotEqual(HomeworkCardStatus.AutoDone, h.Homework.GetBoard(FakeGame.SampleKey).Cards.Single(c => c.Id == "daily_day_dungeon").Status);
        Assert.Equal(0, observer.Runs);

        SetDungeon(game, "Cleared");   // 보스 처치
        await Wait.UntilAsync(() => h.Homework.GetBoard(FakeGame.SampleKey).Cards.Single(c => c.Id == "daily_day_dungeon").Status == HomeworkCardStatus.AutoDone, "클리어 즉시 완료");
        Assert.Equal("던전 클리어(게임 상태)", h.Homework.GetBoard(FakeGame.SampleKey).Cards.Single(c => c.Id == "daily_day_dungeon").Evidence);
        Assert.Contains(h.Logs.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("던전 클리어 감지") && e.Message.Contains("찬란한 유적 V"));
        await Wait.UntilAsync(() => observer.FullRuns >= 2, "+0초 · +3초 관찰", TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task InsideOtherDungeon_ReadsActivity_ButClearedDoesNotCompleteDayDungeon()
    {
        using var game = new FakeGame();
        game.SetMap("페카 고분 심층 1층");
        SetDungeon(game, "InProgress");
        await using var h = await Start(game);
        await h.Status.CheckNowAsync();
        var observer = h.Services.GetRequiredService<HomeworkObserver>();
        // 평상시 맵에서 시작해 던전 안(InProgress)이 되면 읽기 시작한다는 것은 감지기 단위 테스트가 확인한다. 여기서는 오탐이 없는지만 본다.
        SetDungeon(game, "InProgress");
        await Wait.UntilAsync(() => Calls(game).GetValueOrDefault("get_my_info") >= 3, "감시 동작");
        SetDungeon(game, "Cleared");
        await Task.Delay(1500);
        Assert.NotEqual(HomeworkCardStatus.AutoDone, h.Homework.GetBoard(FakeGame.SampleKey).Cards.Single(c => c.Id == "daily_day_dungeon").Status);
    }
}
