namespace MobiMate.Tests;

/// <summary>
/// 소환의 결계·검은 구멍(주간) 1~7단계 (2026-10-24 승운): 자동으로 알 길이 없어(주간 미션 "소환결계 1회 파괴"·"검은 구멍 1회 입장"은 계정 공유의 1회짜리라
/// 몇 단계인지, 어느 캐릭터인지 모른다) 숙제 화면에서 손으로 0~7을 맞춘다. 서버는 단계를 저장하고, 화면 칸이 읽을 수 있게 캐릭터별 현황에 단계를 준다.
/// </summary>
public class HomeworkStepCounterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwstep-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 23, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 목요일
    private const string Main = "에린_힐러";
    private static readonly string[] Ids = { "weekly_barrier_1_7", "weekly_black_hole_1_7" };

    public HomeworkStepCounterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);
    private HomeworkAutoStatus Auto(HomeworkService s, string id) => s.GetAutoStatuses(new[] { Main })[Main].Single(x => x.Id == id);

    [Theory]
    [InlineData("weekly_barrier_1_7")]
    [InlineData("weekly_black_hole_1_7")]
    public void BothCountersAreInTheCharacterStatuses_EvenBeforeAnythingIsSet(string id)
    {
        var svc = NewService();
        Assert.Equal("unknown", Auto(svc, id).State);
        Assert.Null(Auto(svc, id).Remaining);
    }

    [Theory]
    [InlineData(0, "unknown", null)]
    [InlineData(1, "todo", 6)]
    [InlineData(4, "todo", 3)]
    [InlineData(6, "todo", 1)]
    [InlineData(7, "done", null)]
    public void TheStepIsSavedAndShownAsRemainingSteps(int step, string state, int? remaining)
    {
        foreach (var id in Ids)
        {
            var svc = NewService();
            svc.Set(Main, id, count: step);
            var a = Auto(NewService(), id);   // 다시 읽어도 같다
            Assert.Equal(state, a.State);
            Assert.Equal(remaining, a.Remaining);
        }
    }

    [Fact]
    public void TheStepIsClampedToZeroThroughSeven_AndSevenIsDone()
    {
        var svc = NewService();
        svc.Set(Main, "weekly_barrier_1_7", count: 99);
        Assert.Equal(HomeworkCardStatus.ManualDone, svc.GetBoard(Main).Cards.Single(c => c.Id == "weekly_barrier_1_7").Status);
        svc.Set(Main, "weekly_barrier_1_7", count: -3);
        Assert.Equal("unknown", Auto(svc, "weekly_barrier_1_7").State);
    }

    [Fact]
    public void TheTwoCountersAreIndependent()
    {
        var svc = NewService();
        svc.Set(Main, "weekly_barrier_1_7", count: 3);
        Assert.Equal(4, Auto(svc, "weekly_barrier_1_7").Remaining);
        Assert.Equal("unknown", Auto(svc, "weekly_black_hole_1_7").State);
    }

    [Fact]
    public void TheStepResetsWithTheWeeklyReset()
    {
        var svc = NewService();
        svc.Set(Main, "weekly_black_hole_1_7", count: 5);
        _now += TimeSpan.FromDays(7);   // 다음 주 목요일 06:00 이후
        Assert.Equal("unknown", Auto(NewService(), "weekly_black_hole_1_7").State);
    }
}
