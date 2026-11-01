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
}
