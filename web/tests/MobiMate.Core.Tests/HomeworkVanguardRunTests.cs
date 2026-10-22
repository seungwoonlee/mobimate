namespace MobiMate.Tests;

/// <summary>
/// 뱅가드 브리치 주 3회 (2026-10-22 승운): 퀘스트 `[긴급 의뢰] 뱅가드 브리치`는 다른 퀘스트에 따라 보였다 안 보였다 해서, 안 보이는 것으로는 완료하지 않는다.
/// 이번 주 횟수 = 퀘스트가 보일 때 목표 "작전 장소로 이동 N/3"의 N(게임 값), 그 사이엔 뱅가드 지역(`뱅가드 브리치 • …`) 안에서 골드가 크게 들어올 때마다 +1
/// (실측: 1회 클리어에 골드 +100,000). 3에 닿으면 완료.
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
            new List<QuestObjective> { new($"<color=orange>작전 장소</color>로 이동 {done}/3", false, done, 3) });
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

    [Fact]
    public void QuestDisappearing_IsNeverACompletion_TheLastKnownCountStays()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(1), Other));
        Assert.Equal(2, Auto(svc).Remaining);
        for (var i = 0; i < 5; i++)
        {
            _now += TimeSpan.FromMinutes(5);
            svc.Evaluate(Main, Obs(Town, Other));   // 다른 퀘스트에 밀려 안 보인다
        }
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc).Status);
        Assert.Equal("todo", Auto(svc).State);
        Assert.Equal(2, Auto(svc).Remaining);
    }

    [Fact]
    public void EachGoldRewardInsideTheVanguardArea_CountsOneRun_AndTheThirdCompletes()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(1), Other));
        Run(svc);
        Assert.Equal(1, Auto(svc).Remaining);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc).Status);
        Run(svc);
        var card = Card(svc);
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Contains("3/3", card.Evidence);
    }

    [Fact]
    public void GoldEarnedOutsideTheArea_IsNotARun()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(1), Other));
        _now += TimeSpan.FromMinutes(1);
        _gold += 500_000;   // 마을에서 판매
        svc.Evaluate(Main, Obs(Town, Other));
        Assert.Equal(2, Auto(svc).Remaining);
    }

    [Fact]
    public void TheQuestsCount_IsTheTruth_OverCountedRuns()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(0), Other));
        Run(svc);
        Assert.Equal(2, Auto(svc).Remaining);
        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Main, Obs(Town, Weekly(1), Other));   // 게임도 1/3
        Assert.Equal(2, Auto(svc).Remaining);
        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Main, Obs(Town, Weekly(2), Other));   // 앱이 못 본 한 판(예: 앱이 꺼져 있었다)도 게임 값으로 맞춘다
        Assert.Equal(1, Auto(svc).Remaining);
    }

    [Fact]
    public void RightAfterTheThirdRun_AStaleQuestDoesNotReopen_ButALaterOneDoes()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(2), Other));
        Run(svc);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc).Status);
        _now += TimeSpan.FromSeconds(30);
        svc.Evaluate(Main, Obs(Town, Weekly(2), Other));   // 목록이 아직 갱신 전
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc).Status);
        _now += TimeSpan.FromMinutes(10);
        svc.Evaluate(Main, Obs(Town, Weekly(2), Other));   // 한참 뒤에도 2/3이면 게임 값이 맞다
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc).Status);
        Assert.Equal(1, Auto(svc).Remaining);
    }

    [Fact]
    public void CountsResetEveryWeek()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, Weekly(1), Other));
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
