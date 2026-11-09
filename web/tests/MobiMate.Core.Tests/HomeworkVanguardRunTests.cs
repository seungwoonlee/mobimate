namespace MobiMate.Tests;

/// <summary>
/// 뱅가드 브리치 (2026-10-22 승운 주 3회 → 2026-11-10부터 주 1회): 퀘스트 `[긴급 의뢰] 뱅가드 브리치`는 다른 퀘스트에 따라 보였다 안 보였다 해서, 안 보이는 것으로는 완료하지 않는다.
/// 이번 주 횟수 = 퀘스트가 보일 때 목표 "작전 장소로 이동 N/1"의 N(게임 값), 그 사이엔 뱅가드 지역(`뱅가드 브리치 • …`) 안에서 골드가 크게 들어올 때마다 +1
/// (실측: 1회 클리어에 골드 +100,000). 목표(1)에 닿으면 완료.
/// </summary>
public class HomeworkVanguardRunTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwvanguard-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 23, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 목요일
    private const string Main = "에린_힐러";
    private const string Id = "weekly_vanguard_breach";
    private const string Area = "뱅가드 브리치 • 늑대의 숲";
    private const string Town = "티르코네일";
    private long _gold = 60_000_000;

    public HomeworkVanguardRunTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);
    private HomeworkCard Card(HomeworkService s) => s.GetBoard(Main).Cards.Single(c => c.Id == Id);
    private HomeworkAutoStatus Auto(HomeworkService s) => s.GetAutoStatuses(new[] { Main })[Main].Single(x => x.Id == Id);

    private static QuestItem Weekly(int done) =>
        new("[긴급 의뢰] 뱅가드 브리치", "auto_register_sub", "",
            new List<QuestObjective> { new($"<color=orange>작전 장소</color>로 이동 {done}/1", false, done, 1) });
    private static readonly QuestItem Other = new("[주간 목표] 모험가 길드의 정기 의뢰 (2)", "main", "메인", new List<QuestObjective> { new("사냥터 클리어 0/5", false, 0, 5) });

    private HomeworkObservation Obs(string space, params QuestItem[] quests) => new(
        Quests: quests.ToList(),
        Currencies: new List<CurrencyItem> { new("골드", _gold) },
        Environment: new EnvironmentInfo("채널 1", null, null, space));

    /// <summary>뱅가드 한 판: 들어가서 기준값을 잡고, 처치 순간 골드 +100,000, 나온다.</summary>
    private void Run(HomeworkService svc)
    {
        _now += TimeSpan.FromSeconds(30);
        svc.Evaluate(Main, Obs(Area, Other));
        _now += TimeSpan.FromMinutes(2);
        _gold += 100_000;
        svc.Evaluate(Main, Obs(Area, Other));
        _now += TimeSpan.FromSeconds(25);
        svc.Evaluate(Main, Obs(Town, Other));
    }

    // 2026-11-10부터 주 1회: 칸을 누르면 0 → 1(완료) → 0으로 돈다.

    [Fact]
    public void ManualCount_Zero_IsTodo_WithoutARemainingCount()
    {
        var svc = NewService();
        svc.Set(Main, Id, count: 0);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc).Status);
        Assert.Equal("todo", Auto(svc).State);
        Assert.Null(Auto(svc).Remaining);
        Assert.Null(Card(svc).Remaining);
    }

    [Fact]
    public void ManualCount_AtTheGoal_Completes_AndZeroReopens()
    {
        var svc = NewService();
        svc.Set(Main, Id, count: 1);
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(svc).Status);
        Assert.Equal("done", Auto(svc).State);
        svc.Set(Main, Id, count: 0);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc).Status);
        Assert.Null(Auto(svc).Remaining);
    }

    [Fact]
    public void ManualCount_IsClamped_AndSurvivesARestart()
    {
        var svc = NewService();
        svc.Set(Main, Id, count: 99);
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(NewService()).Status);
        svc.Set(Main, Id, count: -5);
        Assert.Null(Auto(NewService()).Remaining);
    }

    [Fact]
    public void CompletedFlag_StillWorksForTheOldCallers()
    {
        var svc = NewService();
        svc.Set(Main, Id, completed: true);
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(svc).Status);
        svc.Set(Main, Id, completed: false);
        Assert.Null(Auto(svc).Remaining);
    }

    [Fact]
    public void QuestDisappearing_IsNeverACompletion_TheLastKnownCountStays()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(0), Other));
        Assert.Null(Auto(svc).Remaining);
        for (var i = 0; i < 5; i++)
        {
            _now += TimeSpan.FromMinutes(5);
            svc.Evaluate(Main, Obs(Town, Other));   // 다른 퀘스트에 밀려 안 보인다
        }
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc).Status);
        Assert.Equal("todo", Auto(svc).State);
        Assert.Null(Auto(svc).Remaining);
    }

    [Fact]
    public void AGoldRewardInsideTheVanguardArea_CompletesTheWeek()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(0), Other));
        Run(svc);
        var card = Card(svc);
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Contains("1/1", card.Evidence);
    }

    [Fact]
    public void GoldEarnedOutsideTheArea_IsNotARun()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(0), Other));
        _now += TimeSpan.FromMinutes(1);
        _gold += 500_000;   // 마을에서 판매
        svc.Evaluate(Main, Obs(Town, Other));
        Assert.Null(Auto(svc).Remaining);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc).Status);
    }

    [Fact]
    public void AfterTheRun_AStaleQuestListDoesNotReopenTheWeek()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(0), Other));
        Run(svc);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc).Status);
        _now += TimeSpan.FromMinutes(10);
        svc.Evaluate(Main, Obs(Town, Weekly(0), Other));   // 주 1회라 한 번 끝나면 이번 주는 끝이다 (다시 열려면 손으로 미완료)
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc).Status);
    }

    [Fact]
    public void CountsResetEveryWeek()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(0), Other));
        Run(svc);
        _now = new DateTimeOffset(2026, 10, 26, 6, 30, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 다음 주 월요일
        svc.Evaluate(Main, Obs(Town, Other));
        Assert.Equal("unknown", Auto(svc).State);
        Assert.Null(Auto(svc).Remaining);
    }

    [Theory]
    [InlineData("먼 바다의 춤추는 바람", true)]      // 레이드
    [InlineData("광기의 동굴", true)]                // 어비스
    [InlineData("빛나는 동굴 V ", false)]            // 요일 던전
    [InlineData("북쪽 폐허 심층 던전 2층 1구역", false)]
    [InlineData(Area, false)]                        // 뱅가드
    [InlineData("콜헨", false)]
    public void AutoSuspicion_IsWatchedOnlyInRaidAndAbyssAreas(string space, bool expected) =>
        Assert.Equal(expected, _catalog.IsAutoWatchArea(space));   // 2026-10-22 승운: 일반 던전에서는 의미가 없다
}
