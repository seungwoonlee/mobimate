namespace MobiMate.Tests;

/// <summary>
/// 요일 던전 확정 근거: 같은 관찰에서 던전 상태가 Cleared이고 현재 맵이 요일 던전 지역이면 즉시 완료
/// (실측 2026-10-12: Cleared는 처치 순간부터 약 17초 유지, 퀘스트는 나간 뒤에 사라진다).
/// </summary>
public class HomeworkDungeonClearedTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwclear-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 화요일: 눈부신 보석을 찾아서
    private const string Main = "에린_댄서";
    private const string Id = "daily_day_dungeon";
    private const string Gem = "눈부신 보석을 찾아서";

    public HomeworkDungeonClearedTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);
    private static HomeworkCard Card(HomeworkBoard b) => b.Cards.Single(c => c.Id == Id);

    private static ActivityInfo Act(string? state) =>
        new(false, false, false, false, false, false, false, false, false, "Idle", false, Dungeon: state == null ? null : new DungeonState(state));

    private static QuestItem GemQuest() => new(Gem, "pinned_sub", "사이드", new List<QuestObjective> { new("<color=orange>목표</color> 1", false) });

    private static HomeworkObservation Obs(string? state, string? map, bool withQuest = true, bool activity = true) => new(
        Quests: withQuest ? new List<QuestItem> { GemQuest() } : new List<QuestItem>(),
        Activity: activity ? Act(state) : null,
        Environment: new EnvironmentInfo("채널 1", null, null, map));

    [Fact]
    public void Catalog_OnlyDayDungeon_UsesClearedRule_AndValidates()
    {
        Assert.Empty(_catalog.Validate());
        Assert.Equal(new[] { Id }, _catalog.Items.Where(d => d.ClearOnDungeonCleared).Select(d => d.Id));
    }

    [Fact]
    public void Cleared_InDayDungeonMap_CompletesImmediately_WithEvidence()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs("InProgress", "찬란한 유적 V"));
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main)).Status);

        var change = svc.Evaluate(Main, Obs("Cleared", "찬란한 유적 V"));   // 퀘스트는 아직 목록에 있다
        Assert.NotNull(change);
        var card = Card(svc.GetBoard(Main));
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Equal("던전 클리어(게임 상태)", card.Evidence);
    }

    [Fact]
    public void Cleared_StaysDone_WhileQuestStillListedAfterExit_ButQuestStillVisibleLongAfter_CancelsIt()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs("Cleared", "빛나는 동굴 III"));
        _now += TimeSpan.FromSeconds(20);
        svc.Evaluate(Main, Obs("NotInDungeon", "빛나는 동굴 III"));   // 나갔지만 퀘스트가 아직 보인다
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main)).Status);

        _now += TimeSpan.FromMinutes(3);
        svc.Evaluate(Main, Obs("NotInDungeon", "카룬"));   // 한참 뒤에도 퀘스트가 보이면 클리어가 아니었다
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main)).Status);
    }

    [Fact]
    public void Cleared_InOtherDungeonMap_IsIgnored()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs("Cleared", "페카 고분 심층 1층"));
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main)).Status);
    }

    [Theory]
    [InlineData("InProgress")]
    [InlineData("Entering")]
    [InlineData("NotInDungeon")]
    [InlineData(null)]
    public void NotCleared_IsIgnored(string? state)
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(state, "찬란한 유적 V"));
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main)).Status);
    }

    [Fact]
    public void Cleared_WithoutActivityOrMap_IsIgnored()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs("Cleared", "찬란한 유적 V", activity: false));
        svc.Evaluate(Main, Obs("Cleared", null));
        svc.Evaluate(Main, Obs("Cleared", ""));
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main)).Status);
    }

    [Fact]
    public void Cleared_RightAfterCharacterSwitch_IsIgnored()
    {
        var svc = NewService();
        svc.Evaluate("에린_검술사", Obs("InProgress", "찬란한 유적 V"));
        svc.Evaluate(Main, Obs("Cleared", "찬란한 유적 V"));   // 직전 관찰과 캐릭터가 다르다: 이전 캐릭터의 활동일 수 있다
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main)).Status);

        _now += TimeSpan.FromSeconds(3);
        svc.Evaluate(Main, Obs("Cleared", "찬란한 유적 V"));   // 같은 캐릭터로 다시 관찰하면 인정
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main)).Status);
    }

    [Fact]
    public void ManualIncomplete_IsOverridden_ByClearedAfterManual()
    {
        var svc = NewService();
        svc.Set(Main, Id, completed: false);   // 수동 미완료
        _now += TimeSpan.FromMinutes(5);
        svc.Evaluate(Main, Obs("Cleared", "찬란한 유적 V"));
        var card = Card(svc.GetBoard(Main));
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
    }

    [Fact]
    public void ManualIncomplete_JustSet_IsRespected_BecauseClearedMayPredateIt()
    {
        var svc = NewService();
        svc.Set(Main, Id, completed: false);
        _now += TimeSpan.FromSeconds(5);
        svc.Evaluate(Main, Obs("Cleared", "찬란한 유적 V"));
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main)).Status);
    }

    [Fact]
    public void AlreadyDone_StaysUnchanged()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs("Cleared", "찬란한 유적 V"));
        _now += TimeSpan.FromSeconds(3);
        Assert.Null(svc.Evaluate(Main, Obs("Cleared", "찬란한 유적 V")));
    }

    [Fact]
    public void ActivityInfo_ParsesRealDungeonShape()
    {
        const string json = """{"IsInCombat":false,"Dungeon":{"State":"Cleared","IsBossBattleInProgress":false},"Battlefield":{"IsInBattleField":false}}""";
        var a = System.Text.Json.JsonSerializer.Deserialize<ActivityInfo>(json)!;
        Assert.True(a.Dungeon!.IsCleared);
        Assert.False(a.Dungeon.IsInside);
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<ActivityInfo>("""{"IsInCombat":false}""")!.Dungeon);
    }
}
