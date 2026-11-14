namespace MobiMate.Tests;

/// <summary>
/// 게임 성장 가이드(get_growth_guide, 2026-11-15) 판정: 게임이 직접 주는 완료·남은 횟수는 확정 근거다.
/// 소환의 결계·검은 구멍은 이제 수동이 아니라 게임의 남은 횟수를 그대로 따른다.
/// </summary>
public class HomeworkGrowthGuideTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwgrowth-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = new DateTimeOffset(2026, 11, 12, 20, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 목요일 저녁
    private const string Main = "아이라_화염술사";

    public HomeworkGrowthGuideTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);

    private static GrowthGuideItem G(string target, string day, bool done, int? remaining = null, bool unlocked = true, string? hint = null) =>
        new(target, day, unlocked, done, remaining, hint);

    /// <summary>첫 관찰은 "캐릭터를 바꾼 직후"라 가이드를 쓰지 않는다. 한 번 보게 한 뒤 가이드를 넣어 판정한다.</summary>
    private HomeworkService Observe(HomeworkService? svc, params GrowthGuideItem[] guide)
    {
        svc ??= NewService();
        svc.Evaluate(Main, new HomeworkObservation());
        _now += TimeSpan.FromSeconds(5);
        svc.Evaluate(Main, new HomeworkObservation(GrowthGuide: new GrowthGuideResponse(guide.ToList())));
        return svc;
    }

    private HomeworkCard Card(HomeworkService s, string id) => s.GetBoard(Main).Cards.Single(c => c.Id == id);
    private HomeworkAutoStatus Auto(HomeworkService s, string id) => s.GetAutoStatuses(new[] { Main })[Main].Single(x => x.Id == id);

    [Fact]
    public void DayDungeon_CompletedByTheGame_IsDone_WithTheGameAsEvidence()
    {
        var svc = Observe(null, G("요일 던전", "Daily", true));
        var card = Card(svc, "daily_day_dungeon");
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Contains("성장 가이드", card.Evidence);
    }

    [Fact]
    public void Vanguard_CompletedByTheGame_IsDone()
    {
        var svc = Observe(null, G("뱅가드 브리치", "Weekly", true, 0));
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc, "weekly_vanguard_breach").Status);
    }

    [Fact]
    public void Counter_FollowsTheGamesRemainingCount()
    {
        var svc = Observe(null, G("소환의 결계", "Weekly", false, 4), G("검은 구멍", "Weekly", false, 9));
        Assert.Equal(3, Card(svc, "weekly_barrier_1_7").Count);          // 7 − 4
        Assert.Equal(5, Card(svc, "weekly_black_hole_1_7").Count);       // 14 − 9
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, "weekly_barrier_1_7").Status);
    }

    [Fact]
    public void Counter_ReachingZeroRemaining_Completes()
    {
        var svc = Observe(null, G("소환의 결계", "Weekly", false, 1));
        _now += TimeSpan.FromSeconds(5);
        svc.Evaluate(Main, new HomeworkObservation(GrowthGuide: new GrowthGuideResponse(new() { G("소환의 결계", "Weekly", true, 0) })));
        var card = Card(svc, "weekly_barrier_1_7");
        Assert.Equal(7, card.Count);
        Assert.True(card.Status is HomeworkCardStatus.AutoDone or HomeworkCardStatus.ManualDone);
    }

    [Fact]
    public void Counter_GameValueBeatsAManualCount()
    {
        var svc = NewService();
        svc.Set(Main, "weekly_barrier_1_7", count: 6);   // 손으로 6단계라고 적어 둔 상태
        Observe(svc, G("소환의 결계", "Weekly", false, 5));   // 게임은 2단계까지라고 한다
        Assert.Equal(2, Card(svc, "weekly_barrier_1_7").Count);
    }

    [Fact]
    public void DailyAndWeeklyBlackHole_AreSeparateEntries()
    {
        var svc = Observe(null, G("검은 구멍", "Daily", true, 0), G("검은 구멍", "Weekly", false, 9));
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc, "daily_black_hole").Status);
        Assert.Equal(5, Card(svc, "weekly_black_hole_1_7").Count);
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc, "weekly_black_hole_1_7").Status);
    }

    [Fact]
    public void NotCompletedByTheGame_DoesNotReopenADoneItem()
    {
        var svc = Observe(null, G("요일 던전", "Daily", true));
        _now += TimeSpan.FromSeconds(5);
        svc.Evaluate(Main, new HomeworkObservation(GrowthGuide: new GrowthGuideResponse(new() { G("요일 던전", "Daily", false, 1) })));   // 가이드가 한 박자 늦게 갱신되는 순간
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc, "daily_day_dungeon").Status);
    }

    [Fact]
    public void LockedContent_IsIgnored()
    {
        var svc = Observe(null, G("소환의 결계", "Weekly", false, 7, unlocked: false, hint: "레벨 30부터"));
        Assert.Equal(0, Card(svc, "weekly_barrier_1_7").Count);
    }

    [Fact]
    public void WithoutAGuide_NothingChanges()
    {
        var svc = NewService();
        svc.Set(Main, "weekly_barrier_1_7", count: 3);
        svc.Evaluate(Main, new HomeworkObservation());
        svc.Evaluate(Main, new HomeworkObservation(GrowthGuide: null));   // 가이드를 못 읽었다
        Assert.Equal(3, Card(svc, "weekly_barrier_1_7").Count);
    }

    [Fact]
    public void RightAfterACharacterSwitch_TheGuideIsNotUsed()
    {
        var svc = NewService();
        svc.Evaluate("아이라_다른직업", new HomeworkObservation());
        svc.Evaluate(Main, new HomeworkObservation(GrowthGuide: new GrowthGuideResponse(new() { G("요일 던전", "Daily", true) })));   // 캐릭터를 바꾼 직후의 값은 이전 캐릭터 것일 수 있다
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc, "daily_day_dungeon").Status);
    }

    [Fact]
    public void Catalog_LinksTheGrowthGuideEntries()
    {
        string? Of(string id) => _catalog.Items.Single(i => i.Id == id).GrowthTarget + "|" + _catalog.Items.Single(i => i.Id == id).GrowthDay;
        Assert.Equal("요일 던전|Daily", Of("daily_day_dungeon"));
        Assert.Equal("검은 구멍|Daily", Of("daily_black_hole"));
        Assert.Equal("소환의 결계|Weekly", Of("weekly_barrier_1_7"));
        Assert.Equal("검은 구멍|Weekly", Of("weekly_black_hole_1_7"));
        Assert.Equal("뱅가드 브리치|Weekly", Of("weekly_vanguard_breach"));
    }
}
