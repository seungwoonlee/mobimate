namespace MobiMate.Tests;

/// <summary>
/// 나이트메어 드릴 (2026-11-02 승운): 1~30구역은 손으로 올리는 단계 카운터, 랭킹 두 칸은 손으로 입력하는 수치(위)다.
/// 랭킹은 1 이상이면 입력된 것(완료 표시)이고 숙제 카드·진행 통계에는 넣지 않는다.
/// </summary>
public class HomeworkNightmareDrillTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwdrill-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 23, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();
    private const string Main = "에린_힐러";
    private const string Drill = "weekly_nightmare_drill";
    private static readonly string[] Ranks = { "weekly_nightmare_rank_1", "weekly_nightmare_rank_2" };

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
        foreach (var id in Ranks) { Assert.Equal("unknown", Auto(svc, id).State); Assert.Null(Auto(svc, id).Value); }
        svc.Set(Main, Ranks[0], count: 1234);
        var a = Auto(NewService(), Ranks[0]);   // 다시 읽어도 같다
        Assert.Equal("done", a.State);
        Assert.Equal(1234, a.Value);
        Assert.Equal("unknown", Auto(NewService(), Ranks[1]).State);   // 두 칸은 따로다
    }

    [Fact]
    public void RankZeroClearsIt()
    {
        var svc = NewService();
        svc.Set(Main, Ranks[1], count: 7);
        svc.Set(Main, Ranks[1], count: 0);
        var a = Auto(svc, Ranks[1]);
        Assert.Equal("unknown", a.State);
        Assert.Null(a.Value);
    }

    [Fact]
    public void RanksAreNotInTheBoardCardsOrProgress_ButTheDrillIs()
    {
        var svc = NewService();
        svc.Set(Main, Ranks[0], count: 5);
        var board = svc.GetBoard(Main);
        Assert.DoesNotContain(board.Cards, c => Ranks.Contains(c.Id));
        Assert.Contains(board.Cards, c => c.Id == Drill);
        Assert.Equal(0, board.Weekly.Done);   // 랭킹을 넣어도 진행 통계는 오르지 않는다
    }

    [Fact]
    public void RanksResetWithTheWeeklyReset()
    {
        NewService().Set(Main, Ranks[0], count: 9);
        _now += TimeSpan.FromDays(7);
        Assert.Equal("unknown", Auto(NewService(), Ranks[0]).State);
    }
}
