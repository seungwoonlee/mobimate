namespace MobiMate.Tests;

/// <summary>
/// 미션 카운터로 뱅가드 브리치를 판정한다 (2026-10-22 승운 승인). 주간 미션은 캐릭터마다 값이 다르다(실측: 궁수 3/3 → 화염술사 0/3).
/// "우리 집에 왜 왔니?"(뱅가드 브리치 3회 토벌) N/3이 그 캐릭터의 진행이라 퀘스트 목록의 사라짐보다 확실하다.
/// </summary>
public class HomeworkMissionCounterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwcounter-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 목요일
    private const string Main = "에린_댄서";
    private const string Alt = "에린_검술사";
    private const string Id = "weekly_vanguard_breach";
    private const string Title = "우리 집에 왜 왔니?";
    private const string QuestTitle = "[긴급 의뢰] 뱅가드 브리치";

    public HomeworkMissionCounterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);
    private HomeworkCard Card(HomeworkService s, string key) => s.GetBoard(key).Cards.Single(c => c.Id == Id);
    private HomeworkAutoStatus Auto(HomeworkService s, string key) => s.GetAutoStatuses(new[] { key })[key].Single(x => x.Id == Id);

    private static MissionItem Counter(int c, int g = 3) => new(Title, "뱅가드 브리치 3회 토벌", c, g, c >= g, c >= g);
    private static readonly MissionItem OtherMission = new("두근두근 연금술", "연금술 20회 사용", 1, 20, false, false);

    private static HomeworkObservation Obs(MissionItem? counter, params QuestItem[] quests) => new(
        WeeklyMissions: counter == null ? new List<MissionItem> { OtherMission } : new List<MissionItem> { OtherMission, counter },
        Quests: quests.ToList());

    private static QuestItem Quest(string title) => new(title, "pinned_sub", "사이드", new List<QuestObjective> { new("<color=orange>목표</color>", false) });

    [Fact]
    public void Catalog_VanguardHasTheCounterMission_AndWeeklyMissionsAreNowPerCharacter()
    {
        Assert.Empty(_catalog.Validate());
        Assert.Equal(new[] { Title }, _catalog.Find(Id)!.CounterMissionTitles);
        Assert.Equal(HomeworkShare.Character, _catalog.Find("weekly_missions_all")!.Share);   // 주간 미션은 캐릭터마다 다르다
        Assert.Equal(HomeworkShare.Account, _catalog.Find("daily_missions_all")!.Share);      // 일일 미션은 실측 근거가 없어 그대로
    }

    [Fact]
    public void Counter_InProgress_IsConfirmedTodo_WithTheRemainingRuns()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Counter(1), Quest(QuestTitle)));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main).Status);
        var a = Auto(svc, Main);
        Assert.Equal("todo", a.State);
        Assert.Equal(2, a.Remaining);
        Assert.Contains("1/3", Card(svc, Main).Evidence);
    }

    [Fact]
    public void Counter_FollowsEachRun_ThenCompletes_WithoutNeedingTheQuestToVanish()
    {
        var svc = NewService();
        foreach (var (count, remaining) in new[] { (0, 3), (1, 2), (2, 1) })
        {
            _now += TimeSpan.FromMinutes(10);
            svc.Evaluate(Main, Obs(Counter(count), Quest(QuestTitle)));
            Assert.Equal(remaining, Auto(svc, Main).Remaining);
            Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main).Status);
        }
        _now += TimeSpan.FromMinutes(10);
        svc.Evaluate(Main, Obs(Counter(3), Quest(QuestTitle)));          // 퀘스트가 아직 목록에 남아 있어도 카운터가 3/3이면 완료
        var card = Card(svc, Main);
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Contains("3/3", card.Evidence);
        Assert.Equal("done", Auto(svc, Main).State);
        Assert.Null(card.Remaining);
    }

    [Fact]
    public void ACounterThatSaysNotDone_CancelsAnEarlierVanishCompletion()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(Quests: new List<QuestItem> { Quest(QuestTitle) }));   // 미션 목록 없이 퀘스트만 본다
        _now += TimeSpan.FromMinutes(1);
        var other = new HomeworkObservation(Quests: new List<QuestItem> { Quest("우울한 화가") });
        svc.Evaluate(Main, other);
        _now += HomeworkEvaluator.VanishConfirmWindow + TimeSpan.FromSeconds(1);
        svc.Evaluate(Main, other);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc, Main).Status);                           // 예전 방식으로는 완료로 보였다

        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Main, Obs(Counter(1)));                                                         // 미션은 1/3이다: 퀘스트 사라짐이 틀렸다
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main).Status);
        Assert.Equal(2, Auto(svc, Main).Remaining);
    }

    [Fact]
    public void ManualComplete_IsRespected_ButAFinishedCounterOverridesManualIncomplete()
    {
        var svc = NewService();
        svc.Set(Main, Id, completed: true);
        svc.Evaluate(Main, Obs(Counter(0)));
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(svc, Main).Status);                         // 수동 완료는 존중한다

        var svc2 = NewService();
        svc2.Set(Alt, Id, completed: false);
        _now += TimeSpan.FromMinutes(1);
        svc2.Evaluate(Alt, Obs(Counter(0)));
        svc2.Evaluate(Alt, Obs(Counter(3)));
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc2, Alt).Status);                           // 3/3은 확실한 근거라 수동 미완료를 이긴다
    }

    [Fact]
    public void EachCharacterHasItsOwnCounter_AndTheFirstObservationAfterSwitchingIsNotTrusted()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Counter(3)));                                                         // 댄서는 3/3
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc, Main).Status);

        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Alt, Obs(Counter(3)));                                                          // 전환 직후 값은 이전 캐릭터 것일 수 있다
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Alt).Status);
        _now += TimeSpan.FromSeconds(5);
        svc.Evaluate(Alt, Obs(Counter(0)));                                                          // 검술사의 진짜 값은 0/3
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Alt).Status);
        Assert.Equal(3, Auto(svc, Alt).Remaining);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc, Main).Status);                           // 댄서는 그대로
    }

    [Fact]
    public void WithoutTheMissionInTheList_TheOldQuestRuleStillWorks()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(WeeklyMissions: new List<MissionItem> { OtherMission }, Quests: new List<QuestItem> { Quest(QuestTitle) }));
        Assert.Equal("todo", Auto(svc, Main).State);                                                 // 퀘스트가 보여 미완료 확인
        _now += TimeSpan.FromMinutes(1);
        var gone = new HomeworkObservation(WeeklyMissions: new List<MissionItem> { OtherMission }, Quests: new List<QuestItem> { Quest("우울한 화가") });
        svc.Evaluate(Main, gone);
        _now += HomeworkEvaluator.VanishConfirmWindow + TimeSpan.FromSeconds(1);
        svc.Evaluate(Main, gone);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc, Main).Status);                           // 예비 규칙(사라짐 확인 대기 뒤 완료)
    }
}
