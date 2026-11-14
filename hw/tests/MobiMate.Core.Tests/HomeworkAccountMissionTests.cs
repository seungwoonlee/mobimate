namespace MobiMate.Tests;

/// <summary>
/// 주간 미션은 계정 공유다 (2026-10-22 재실측: 같은 계정 acc-4의 전사·듀얼블레이드가 모든 주간 미션 값이 같다.
/// 처음의 "캐릭터별" 결론은 다른 계정의 궁수·화염술사를 비교한 착오였다). 그래서 뱅가드 같은 캐릭터별 숙제를 계정 카운터로 판정하지 않는다.
/// </summary>
public class HomeworkAccountMissionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwacct-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 목요일
    private const string Main = "에린_댄서";
    private const string Alt = "에린_검술사";
    private const string Id = "weekly_vanguard_breach";
    private const string Title = "우리 집에 왜 왔니?";
    private const string QuestTitle = "[긴급 의뢰] 뱅가드 브리치";

    public HomeworkAccountMissionTests() => Directory.CreateDirectory(_dir);
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
    public void Catalog_EveryHomeworkItemIsPerCharacter()
    {
        Assert.Empty(_catalog.Validate());
        Assert.All(_catalog.Items, d => Assert.Equal(HomeworkShare.Character, d.Share));   // 승운 확인(2026-10-22): 숙제는 모두 캐릭터별
    }

    [Fact]
    public void TheAccountsVanguardMission_DoesNotCompleteACharacterWhoseQuestIsStillOpen()
    {
        var svc = NewService();
        svc.Evaluate(Alt, Obs(Counter(3), Quest(QuestTitle)));   // 같은 계정의 다른 캐릭터가 3회를 채웠다
        _now += TimeSpan.FromSeconds(30);
        svc.Evaluate(Alt, Obs(Counter(3), Quest(QuestTitle)));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Alt).Status);
        Assert.Equal("todo", Auto(svc, Alt).State);
    }

    [Fact]
    public void TheVanguardQuestDisappearing_IsNotACompletion()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(WeeklyMissions: new List<MissionItem> { OtherMission }, Quests: new List<QuestItem> { Quest(QuestTitle) }));
        Assert.Equal("todo", Auto(svc, Main).State);                                                 // 퀘스트가 보여 미완료 확인
        _now += TimeSpan.FromMinutes(1);
        var gone = new HomeworkObservation(WeeklyMissions: new List<MissionItem> { OtherMission }, Quests: new List<QuestItem> { Quest("우울한 화가") });
        svc.Evaluate(Main, gone);
        _now += HomeworkEvaluator.VanishConfirmWindow + TimeSpan.FromSeconds(1);
        svc.Evaluate(Main, gone);
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc, Main).Status);                        // 사라진 것은 근거가 아니다(HomeworkVanguardRunTests)
    }
}
