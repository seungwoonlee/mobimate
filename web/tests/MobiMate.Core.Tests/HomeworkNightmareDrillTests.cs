namespace MobiMate.Tests;

/// <summary>
/// 나이트메어 드릴 (2026-11-02 승운): 1~30구역은 손으로 올리는 단계 카운터, 랭킹 한 칸은 손으로 입력하는 수치(위)다.
/// 랭킹은 1 이상이면 입력된 것(완료 표시)이고 숙제 카드·진행 통계에는 넣지 않는다.
/// </summary>
public class HomeworkNightmareDrillTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwdrill-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 23, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();
    private const string Main = "에린_힐러";
    private const string Drill = "weekly_nightmare_drill";
    private const string Rank = "weekly_nightmare_rank";

    public HomeworkNightmareDrillTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);
    private HomeworkAutoStatus Auto(HomeworkService s, string id) => s.GetAutoStatuses(new[] { Main })[Main].Single(x => x.Id == id);

    [Theory]
    [InlineData(0, "unknown", null)]
    [InlineData(1, "todo", 29)]
    [InlineData(12, "todo", 18)]
    [InlineData(29, "todo", 1)]
    [InlineData(30, "done", null)]
    public void TheZoneIsSavedAsStepsOutOfThirty(int zone, string state, int? remaining)
    {
        NewService().Set(Main, Drill, count: zone);
        var a = Auto(NewService(), Drill);
        Assert.Equal(state, a.State);
        Assert.Equal(remaining, a.Remaining);
    }

    [Fact]
    public void TheZoneIsClampedToThirty()
    {
        var svc = NewService();
        svc.Set(Main, Drill, count: 99);
        Assert.Equal("done", Auto(svc, Drill).State);
    }

    [Fact]
    public void RankIsUnsetUntilEntered_ThenShownAsDoneWithItsValue()
    {
        var svc = NewService();
        Assert.Equal("unknown", Auto(svc, Rank).State);
        Assert.Null(Auto(svc, Rank).Value);
        svc.Set(Main, Rank, count: 1234);
        var a = Auto(NewService(), Rank);   // 다시 읽어도 같다
        Assert.Equal("done", a.State);
        Assert.Equal(1234, a.Value);
    }

    [Fact]
    public void RankZeroClearsIt()
    {
        var svc = NewService();
        svc.Set(Main, Rank, count: 7);
        svc.Set(Main, Rank, count: 0);
        var a = Auto(svc, Rank);
        Assert.Equal("unknown", a.State);
        Assert.Null(a.Value);
    }

    [Fact]
    public void TheRankAndTheDrillAreNotInTheBoardCardsOrProgress()
    {
        var svc = NewService();
        svc.Set(Main, Rank, count: 5);
        var board = svc.GetBoard(Main);
        Assert.DoesNotContain(board.Cards, c => c.Id == Rank);
        Assert.DoesNotContain(board.Cards, c => c.Id == Drill);   // 시즌제라 끝나지 않는 주간 숙제가 되지 않게 카드에서 뺀다
        Assert.Equal(0, board.Weekly.Done);   // 랭킹을 넣어도 진행 통계는 오르지 않는다
    }

    [Fact]
    public void BothAreSeasonal_SoTheWeeklyResetDoesNotClearThem()
    {
        var svc = NewService();
        svc.Set(Main, Rank, count: 9);
        svc.Set(Main, Drill, count: 12);
        _now += TimeSpan.FromDays(21);   // 주간 리셋을 여러 번 지나도
        svc.GetBoard(Main);              // 리셋을 적용하는 읽기
        Assert.Equal(9, Auto(NewService(), Rank).Value);
        Assert.Equal(18, Auto(NewService(), Drill).Remaining);
    }

    [Fact]
    public void TheFullResetKeepsTheSeasonalOnes()
    {
        var svc = NewService();
        svc.Set(Main, Rank, count: 9);
        svc.Set(Main, Drill, count: 12);
        svc.Set(Main, "weekly_barrier_1_7", count: 3);
        svc.ResetAll(Main, includeAccount: false);
        Assert.Equal(9, Auto(svc, Rank).Value);
        Assert.Equal(18, Auto(svc, Drill).Remaining);
        Assert.Equal("unknown", Auto(svc, "weekly_barrier_1_7").State);   // 주기 체크는 지워진다
    }

    [Fact]
    public void TheSecondRankIsGone()
    {
        Assert.Null(_catalog.Find("weekly_nightmare_rank_2"));
        Assert.Null(_catalog.Find("weekly_nightmare_rank_1"));
    }

    // ── 시즌 초기화 버튼과 메모리얼 사가 (2026-11-06 승운) ──

    private const string Saga = "weekly_memorial_saga";
    private const string Alt = "에린_댄서";

    [Fact]
    public void ResetDrill_ClearsTheZoneAndTheRank_OfEveryCharacter_ButNothingElse()
    {
        var svc = NewService();
        foreach (var who in new[] { Main, Alt })
        {
            svc.Set(who, Drill, count: 12);
            svc.Set(who, Rank, count: 345);
            svc.Set(who, Saga, count: 40);
            svc.Set(who, "weekly_guild_regular", completed: true);
        }

        var (cleared, change) = svc.ResetDrillAllCharacters(Main);
        Assert.Equal(2, cleared);
        Assert.NotNull(change);
        Assert.Equal(HomeworkChangeReason.Reset, change!.Reason);

        var fresh = NewService();   // 다시 읽어도
        foreach (var who in new[] { Main, Alt })
        {
            var all = fresh.GetAutoStatuses(new[] { who })[who];
            Assert.Equal("unknown", all.Single(x => x.Id == Drill).State);
            Assert.Null(all.Single(x => x.Id == Rank).Value);
            Assert.Equal(40, all.Single(x => x.Id == Saga).Value);                        // 메모리얼 사가는 그대로
            Assert.Equal("done", all.Single(x => x.Id == "weekly_guild_regular").State);   // 다른 숙제도 그대로
        }
    }

    [Fact]
    public void ResetSaga_ClearsOnlyTheFloors_OfEveryCharacter_AndLeavesTheDrill()
    {
        var svc = NewService();
        foreach (var who in new[] { Main, Alt }) { svc.Set(who, Saga, count: 40); svc.Set(who, Drill, count: 12); svc.Set(who, Rank, count: 9); }
        var (cleared, change) = svc.ResetSagaAllCharacters(Main);
        Assert.Equal(2, cleared);
        Assert.NotNull(change);
        var fresh = NewService();
        foreach (var who in new[] { Main, Alt })
        {
            var all = fresh.GetAutoStatuses(new[] { who })[who];
            Assert.Null(all.Single(x => x.Id == Saga).Value);
            Assert.Equal(18, all.Single(x => x.Id == Drill).Remaining);   // 12구역 = 남은 18, 드릴은 그대로
            Assert.Equal(9, all.Single(x => x.Id == Rank).Value);
        }
        Assert.Equal(0, svc.ResetSagaAllCharacters(Main).Characters);
    }

    [Fact]
    public void TheSagaCountsUpWithoutALimit_AndDrillResetLeavesIt()
    {
        var svc = NewService();
        svc.Set(Main, Saga, count: 250);   // 끝 층을 모른다
        Assert.Equal(250, Auto(svc, Saga).Value);
        svc.ResetDrillAllCharacters(Main);
        Assert.Equal(250, Auto(svc, Saga).Value);
    }

    [Fact]
    public void ResetDrill_WithNothingToClear_ChangesNothing()
    {
        var svc = NewService();
        svc.Set(Main, Saga, count: 5);
        var (cleared, change) = svc.ResetDrillAllCharacters(Main);
        Assert.Equal(0, cleared);
        Assert.Null(change);
        Assert.Equal(5, Auto(svc, Saga).Value);
    }

    [Fact]
    public void ResetDrill_CountsOnlyCharactersThatHadDrillRecords()
    {
        var svc = NewService();
        svc.Set(Main, Drill, count: 3);
        svc.Set(Alt, "weekly_guild_regular", completed: true);   // 드릴 기록 없음
        Assert.Equal(1, svc.ResetDrillAllCharacters(Main).Characters);
    }

    [Fact]
    public void WeeklyResetAndFullReset_DoNotClearTheSeasonItemsEither()
    {
        var svc = NewService();
        svc.Set(Main, Saga, count: 33);
        svc.ResetAll(Main, includeAccount: false);
        Assert.Equal(33, Auto(svc, Saga).Value);
        _now = _now.AddDays(8);   // 주간 리셋을 넘겨도
        Assert.Equal(33, Auto(NewService(), Saga).Value);
    }

    [Fact]
    public void MemorialSaga_IsAManualFloorNumber_NotACardAndNotInProgress()
    {
        var svc = NewService();
        Assert.Equal("unknown", Auto(svc, Saga).State);
        Assert.Null(Auto(svc, Saga).Value);

        svc.Set(Main, Saga, count: 57);
        var a = Auto(NewService(), Saga);
        Assert.Equal("done", a.State);
        Assert.Equal(57, a.Value);

        var board = svc.GetBoard(Main);
        Assert.DoesNotContain(board.Cards, c => c.Id == Saga);   // 숙제 카드·진행 통계에 넣지 않는다 (랭킹과 같은 방식)
        svc.Set(Main, Saga, count: 0);                           // 0이면 지운다(-)
        Assert.Equal("unknown", Auto(NewService(), Saga).State);
    }

    [Fact]
    public void MemorialSaga_IsASeasonItem_WithNoRank()
    {
        var def = _catalog.Find(Saga)!;
        Assert.True(def.SeasonReset);
        Assert.Equal("메모리얼 사가", def.Title);
        Assert.DoesNotContain(_catalog.Items, d => d.Id.Contains("saga", StringComparison.OrdinalIgnoreCase) && d.Id.Contains("rank", StringComparison.OrdinalIgnoreCase));
    }
}
