namespace MobiMate.Tests;

/// <summary>
/// 자동 판정 숙제 (승운 확인 2026-10-02): 요일 던전(퀘스트가 보였다가 사라지면 완료) · 카브락(증표가 늘면 완료) ·
/// 주간 목표 정기 의뢰(이름 뒤 (N)). 그리고 캐릭터 카드용 현황(GetAutoStatuses).
/// </summary>
public class HomeworkAutoCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwauto-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = Kst(2026, 10, 6, 12, 0);   // 화요일 정오 KST: 눈부신 보석을 찾아서
    private const string Main = "에린_댄서";
    private const string Alt = "에린_검술사";

    public HomeworkAutoCheckTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static DateTimeOffset Kst(int y, int mo, int d, int h, int mi) => new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromHours(9)).ToUniversalTime();
    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);
    private static HomeworkCard Card(HomeworkBoard b, string id) => b.Cards.Single(c => c.Id == id);
    private static HomeworkAutoStatus Auto(HomeworkService s, string key, string id) => s.GetAutoStatuses(new[] { key })[key].Single(x => x.Id == id);
    private static QuestItem Quest(string title, params bool[] objectives) =>
        new(title, "pinned_sub", "사이드", objectives.Select((o, i) => new QuestObjective($"<color=orange>목표</color> {i + 1}", o)).ToList());
    private static HomeworkObservation Quests(params QuestItem[] q) => new(Quests: q.ToList());
    private static readonly QuestItem Other = Quest("우울한 화가", false);
    private const string Gem = "눈부신 보석을 찾아서";
    private const string Guild = "[주간 목표] 모험가 길드의 정기 의뢰";

    // ── 요일 던전 ──

    [Fact]
    public void DayDungeon_NeverSeen_StaysPendingAndUnknown()
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Other));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_day_dungeon").Status);
        Assert.Equal("unknown", Auto(svc, Main, "daily_day_dungeon").State);
    }

    [Fact]
    public void DayDungeon_QuestVisible_IsConfirmedTodo_ThenVanished_IsAutoDone()
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(Gem, false), Other));
        Assert.Equal("todo", Auto(svc, Main, "daily_day_dungeon").State);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_day_dungeon").Status);

        _now += TimeSpan.FromMinutes(30);
        svc.Evaluate(Main, Quests(Other));   // 퀘스트가 목록에서 사라짐
        var card = Card(svc.GetBoard(Main), "daily_day_dungeon");
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Contains("사라짐", card.Evidence);
        Assert.Equal("done", Auto(svc, Main, "daily_day_dungeon").State);
    }

    [Fact]
    public void DayDungeon_OnlyTodaysQuestCounts()
    {
        var svc = NewService();   // 화요일: 보석. 황금(월·목) 퀘스트는 오늘 것이 아니다
        svc.Evaluate(Main, Quests(Quest("빛나는 황금을 찾아서", false), Other));
        _now += TimeSpan.FromMinutes(5);
        svc.Evaluate(Main, Quests(Other));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_day_dungeon").Status);
    }

    [Theory]
    [InlineData(5, "빛나는 황금을 찾아서")]   // 월
    [InlineData(6, "눈부신 보석을 찾아서")]   // 화
    [InlineData(7, "찬란한 촉매를 찾아서")]   // 수
    [InlineData(8, "빛나는 황금을 찾아서")]   // 목
    [InlineData(9, "눈부신 보석을 찾아서")]   // 금
    [InlineData(10, "찬란한 촉매를 찾아서")]  // 토
    [InlineData(11, "찬란한 촉매를 찾아서")]  // 일: 셋 중 아무거나
    public void DayDungeon_UsesTheQuestOfTheWeekday(int day, string title)
    {
        _now = Kst(2026, 10, day, 12, 0);
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(title, false)));
        _now += TimeSpan.FromMinutes(5);
        svc.Evaluate(Main, Quests(Other));
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main), "daily_day_dungeon").Status);
    }

    [Fact]
    public void DayDungeon_UsesGameDay_SoBefore6amIsStillYesterday()
    {
        _now = Kst(2026, 10, 7, 5, 30);   // 수요일 05:30 = 아직 화요일(보석)
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(Gem, false)));
        _now += TimeSpan.FromMinutes(10);
        svc.Evaluate(Main, Quests(Other));
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main), "daily_day_dungeon").Status);
    }

    [Fact]
    public void DayDungeon_EmptyQuestList_IsNotEvidenceOfVanishing()
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(Gem, false)));
        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Main, Quests());   // 아직 덜 읽힌 빈 목록
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_day_dungeon").Status);
    }

    [Fact]
    public void DayDungeon_ReappearingQuest_CancelsTheAutoCompletion()
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(Gem, false)));
        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Main, Quests(Other));
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main), "daily_day_dungeon").Status);
        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Main, Quests(Quest(Gem, false)));   // 다시 보임: 트래커 슬롯이 바뀌었을 뿐
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_day_dungeon").Status);
    }

    [Fact]
    public void DayDungeon_SightingOfYesterday_DoesNotCompleteToday()
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(Gem, false)));
        _now = Kst(2026, 10, 9, 7, 0);   // 금요일(보석) 아침: 어제의 목격은 오늘 근거가 아니다
        svc.Evaluate(Main, Quests(Other));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_day_dungeon").Status);
        Assert.Equal("unknown", Auto(svc, Main, "daily_day_dungeon").State);
    }

    [Fact]
    public void DayDungeon_ManualCheck_IsNeverOverwritten_AndSurvivesRestart()
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(Gem, false)));
        svc.Set(Main, "daily_day_dungeon", completed: true);
        svc.Evaluate(Main, Quests(Quest(Gem, false)));
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(svc.GetBoard(Main), "daily_day_dungeon").Status);

        var fresh = NewService();
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(fresh.GetBoard(Main), "daily_day_dungeon").Status);
    }

    [Fact]
    public void DayDungeon_SightingPersists_SoVanishAfterRestartStillCompletes()
    {
        NewService().Evaluate(Main, Quests(Quest(Gem, false)));
        _now += TimeSpan.FromMinutes(20);
        var restarted = NewService();
        restarted.Evaluate(Main, Quests(Other));
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(restarted.GetBoard(Main), "daily_day_dungeon").Status);
    }

    [Fact]
    public void QuestsAfterCharacterSwitch_AreNotUsed()
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(Gem, false)));
        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Alt, Quests(Quest(Gem, false)));          // 전환 직후: 이전 캐릭터 목록일 수 있어 건너뜀
        Assert.Equal("unknown", Auto(svc, Alt, "daily_day_dungeon").State);
        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Alt, Quests(Quest(Gem, false)));          // 이어진 관찰부터 쓴다
        Assert.Equal("todo", Auto(svc, Alt, "daily_day_dungeon").State);
    }

    // ── 카브락 ──

    [Fact]
    public void Cavrak_TokenIncrease_IsAutoDone()
    {
        const string token = "원정의 증거: 카브락 레이드";
        HomeworkObservation Cur(long n) => new(Currencies: new List<CurrencyItem> { new(token, n), new("골드", 1000) });
        var svc = NewService();
        svc.Evaluate(Main, Cur(84));
        Assert.Equal("todo", Auto(svc, Main, "raid_cavrak").State);
        _now += TimeSpan.FromHours(2);
        svc.Evaluate(Main, Cur(85));
        var card = Card(svc.GetBoard(Main), "raid_cavrak");
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Null(card.Suggestion);
        Assert.Equal("done", Auto(svc, Main, "raid_cavrak").State);
    }

    [Fact]
    public void InactiveRaids_AreHiddenAndNotCounted()
    {
        var svc = NewService();
        var b = svc.GetBoard(Main);
        Assert.DoesNotContain(b.Cards, c => c.Id is "raid_glasgivnen" or "raid_tabartas");
        Assert.Single(b.Cards, c => c.Category == "raid");
    }

    // ── 주간 목표: 모험가 길드 정기 의뢰 ──

    [Fact]
    public void GuildRegular_NameWithoutSuffix_IsNotDone_AndShowsObjectiveProgress()
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(Guild, true, true, false)));
        var card = Card(svc.GetBoard(Main), "weekly_guild_regular");
        Assert.Equal(HomeworkCardStatus.Pending, card.Status);
        Assert.Contains("아직 클리어 전", card.Evidence);
        Assert.Contains("2/3", card.Evidence);
        Assert.Equal("todo", Auto(svc, Main, "weekly_guild_regular").State);
    }

    [Theory]
    [InlineData("[주간 목표] 모험가 길드의 정기 의뢰 (1)")]
    [InlineData("[주간 목표] 모험가 길드의 정기 의뢰 (2)")]
    [InlineData("[주간 목표]  모험가 길드의 정기 의뢰(3)")]
    public void GuildRegular_NameWithSuffix_IsAutoDone(string title)
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(title, false)));
        var card = Card(svc.GetBoard(Main), "weekly_guild_regular");
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Contains("표시", card.Evidence);
    }

    [Fact]
    public void GuildRegular_OtherQuestsWithSimilarNames_DoNotMatch()
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest("모험가 길드의 고난도 심층 공략", false), Quest(Guild + " (0)", false), Quest(Guild + " 특별", false)));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "weekly_guild_regular").Status);
        Assert.Equal("unknown", Auto(svc, Main, "weekly_guild_regular").State);
    }

    [Fact]
    public void GuildRegular_ResetsOnMonday6am()
    {
        var svc = NewService();
        svc.Evaluate(Main, Quests(Quest(Guild + " (1)", true)));
        Assert.Equal("done", Auto(svc, Main, "weekly_guild_regular").State);
        _now = Kst(2026, 10, 12, 6, 1);   // 다음 주 월요일 06:01
        Assert.Equal("unknown", Auto(svc, Main, "weekly_guild_regular").State);   // 지난 주 완료는 새 주기의 근거가 아니다
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "weekly_guild_regular").Status);
    }

    // ── 캐릭터 카드용 현황 ──

    [Fact]
    public void AutoStatuses_UnknownCharacter_IsAllUnknown_AndOnlyAutoCheckItemsAppear()
    {
        var svc = NewService();
        var list = svc.GetAutoStatuses(new[] { "에린_없는캐릭터" })["에린_없는캐릭터"];
        Assert.Equal(new[] { "daily_day_dungeon", "weekly_guild_regular", "raid_cavrak" }.OrderBy(x => x), list.Select(x => x.Id).OrderBy(x => x));
        Assert.All(list, x => Assert.Equal("unknown", x.State));
    }

    [Fact]
    public void AutoStatuses_DailyStatusFromYesterday_IsNotShownAsDoneToday()
    {
        var svc = NewService();
        svc.Set(Main, "daily_day_dungeon", completed: true);
        Assert.Equal("done", Auto(svc, Main, "daily_day_dungeon").State);
        _now = Kst(2026, 10, 7, 6, 5);   // 다음 날 06:05: 기록은 그대로지만 새 주기
        Assert.Equal("unknown", Auto(svc, Main, "daily_day_dungeon").State);
    }

    [Fact]
    public void Catalog_NewModesPassValidation() => Assert.Empty(_catalog.Validate());
}
