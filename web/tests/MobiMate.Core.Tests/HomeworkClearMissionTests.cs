namespace MobiMate.Tests;

/// <summary>
/// 클리어 미션 카운터로 레이드·어비스·필드 보스를 확정한다 (2026-10-22 승운 승인, 2단계).
/// 주간 미션은 **계정 공유**다(같은 계정의 전사·듀얼블레이드 값이 같다). 그래서 이 캐릭터로 지켜보던 중(직전 읽기가 10분 안)에
/// "레이드 1회 토벌"이 오른 것만 이 캐릭터의 클리어이고, 처음 본 값이나 지켜보지 않은 동안 오른 값은 근거가 아니다.
/// 어느 항목인지는 지역·최근 보스 교전으로 가리고, 못 가리면 "클리어 추정"(필드 보스는 택1이라 묶음 완료)으로 둔다.
/// </summary>
public class HomeworkClearMissionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwclear2-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 목요일
    private const string Main = "에린_댄서";
    private const string Alt = "에린_검술사";
    private const string Town = "던바튼";

    public HomeworkClearMissionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);
    private static HomeworkCard Card(HomeworkService s, string key, string id) => s.GetBoard(key).Cards.Single(c => c.Id == id);

    private static MissionItem Raid(int c) => new("선장님, 출정합니다!", "레이드 1회 토벌", c, 1, c >= 1, c >= 1);
    private static MissionItem Abyss(int c) => new("심연이 나를 부른다", "어비스 1회 토벌", c, 1, c >= 1, c >= 1);
    private static MissionItem Boss1(int c) => new("이 구역은 내가 접수한다", "필드 보스 1회 토벌", c, 1, c >= 1, c >= 1);
    private static MissionItem Boss2(int c) => new("이 구역은 내가 접수한다", "필드 보스 2회 토벌", c, 2, c >= 2, c >= 2);
    private static readonly MissionItem Other = new("두근두근 연금술", "연금술 20회 사용", 1, 20, false, false);

    private static ActivityInfo Fighting(string? target) =>
        new(false, false, true, false, false, false, false, false, false, "Idle", false, AutoPlayTargetDisplayName: target);
    private static ActivityInfo Idle() => new(false, false, false, false, false, false, false, false, false, "Idle", false);

    private static HomeworkObservation Obs(string space, IEnumerable<MissionItem> missions, ActivityInfo? activity = null) => new(
        WeeklyMissions: missions.Append(Other).ToList(),
        Activity: activity ?? Idle(),
        Environment: new EnvironmentInfo("채널 1", null, null, space));

    [Fact]
    public void Catalog_HasTheClearMissionGroups()
    {
        Assert.Empty(_catalog.Validate());
        Assert.Equal(new[] { "레이드 1회 토벌" }, _catalog.ClearMissions["raid"]);
        Assert.Equal(new[] { "어비스 1회 토벌" }, _catalog.ClearMissions["abyss"]);
        Assert.Equal(2, _catalog.ClearMissions["fieldBoss"].Count);
        Assert.All(_catalog.Items.Where(d => d.Id.StartsWith("raid_")), d => Assert.Equal("raid", d.ClearGroup));
        Assert.All(_catalog.Items.Where(d => d.Id.StartsWith("abyss_")), d => Assert.Equal("abyss", d.ClearGroup));
        Assert.All(_catalog.Items.Where(d => d.Id.StartsWith("fieldboss_")), d => Assert.Equal("fieldBoss", d.ClearGroup));
    }

    [Fact]
    public void AbyssCounterRises_WhileInTheAbyssArea_CompletesThatAbyss()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs("광기의 동굴", new[] { Abyss(0) }));                  // 들어왔다: 기준 0
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main, "abyss_madness_cave").Status);
        _now += TimeSpan.FromMinutes(5);
        svc.Evaluate(Main, Obs("광기의 동굴", new[] { Abyss(1) }));                  // 처치: 카운터가 올랐다
        var card = Card(svc, Main, "abyss_madness_cave");
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Contains("어비스 1회 토벌", card.Evidence);
        Assert.Contains("0→1", card.Evidence);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main, "abyss_illusory_anchorage").Status);   // 다른 어비스는 그대로
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main, "abyss_scattered_waterway").Status);
    }

    [Fact]
    public void RaidCounterRises_AfterFightingTheBoss_EvenInAnUnmeasuredArea_CompletesThatRaid()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs("미측정 지역", new[] { Raid(0) }, Fighting("에이렐")));   // 에이렐과 교전 중(지역 이름은 모른다)
        _now += TimeSpan.FromMinutes(3);
        svc.Evaluate(Main, Obs("미측정 지역", new[] { Raid(1) }, Idle()));               // 처치 직후 카운터가 올랐다
        var airel = Card(svc, Main, "raid_airel");
        Assert.Equal(HomeworkCardStatus.AutoDone, airel.Status);
        Assert.Contains("레이드 1회 토벌", airel.Evidence);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main, "raid_cavrak").Status);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main, "raid_white_succubus").Status);
    }

    [Fact]
    public void CounterRisesWithoutAnyClue_OnlySuggests_NeverGuesses()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, new[] { Raid(0) }));
        _now += TimeSpan.FromMinutes(5);
        svc.Evaluate(Main, Obs(Town, new[] { Raid(1) }));                               // 어느 레이드인지 알 길이 없다
        foreach (var id in new[] { "raid_cavrak", "raid_airel", "raid_white_succubus" })
        {
            var c = Card(svc, Main, id);
            Assert.Equal(HomeworkCardStatus.Pending, c.Status);                         // 추측으로 완료하지 않는다
            Assert.Equal("missionCleared", c.Suggestion?.Code);                         // "클리어 추정"으로만 알린다
        }
    }

    [Fact]
    public void AlreadyFinishedAtFirstSight_IsNotThisCharactersEvidence()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, new[] { Abyss(1), Boss1(1) }));                    // 계정 값: 같은 계정의 다른 캐릭터가 깼을 수 있다
        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Main, Obs(Town, new[] { Abyss(1), Boss1(1) }));
        Assert.Null(Card(svc, Main, "abyss_madness_cave").Suggestion);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main, "abyss_madness_cave").Status);
        Assert.Equal("todo", svc.GetFieldBossStatuses(new[] { Main })[Main].State);
    }

    [Fact]
    public void ARiseWhileNotWatching_BelongsToAnotherCharacterOfTheAccount()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs("광기의 동굴", new[] { Abyss(0) }));
        _now += TimeSpan.FromMinutes(40);                                               // 다른 캐릭터로 바꿔 어비스를 깨고 돌아왔다
        svc.Evaluate(Main, Obs("광기의 동굴", new[] { Abyss(1) }));
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc, Main, "abyss_madness_cave").Status);
        Assert.Null(Card(svc, Main, "abyss_madness_cave").Suggestion);
    }

    [Fact]
    public void SwitchingAwayAndBackQuickly_DoesNotAttributeTheOtherCharactersClear()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, new[] { Boss1(0) }));
        _now += TimeSpan.FromMinutes(1);
        svc.Evaluate(Alt, Obs(Town, new[] { Boss1(0) }));    // 같은 계정의 다른 캐릭터 (전환 직후: 건너뜀)
        _now += TimeSpan.FromSeconds(30);
        svc.Evaluate(Alt, Obs(Town, new[] { Boss1(0) }));                                 // 기준 0
        _now += TimeSpan.FromMinutes(3);
        svc.Evaluate(Alt, Obs(Town, new[] { Boss1(1) }));                                 // 그 캐릭터가 필드 보스를 잡았다
        _now += TimeSpan.FromMinutes(2);
        svc.Evaluate(Main, Obs(Town, new[] { Boss1(1) }));   // 6분 만에 돌아왔다
        _now += TimeSpan.FromSeconds(30);
        svc.Evaluate(Main, Obs(Town, new[] { Boss1(1) }));
        Assert.Equal("todo", svc.GetFieldBossStatuses(new[] { Main })[Main].State);
        Assert.Equal("done", svc.GetFieldBossStatuses(new[] { Alt })[Alt].State);
    }

    [Fact]
    public void OldGuessesMadeFromAFirstSightValue_AreCleanedUp()
    {
        var store = new HomeworkStore(_dir);
        var file = new HomeworkFile();
        var ledger = new HomeworkLedger { LastDailyResetUtc = _now.AddHours(-1), LastWeeklyResetUtc = _now.AddDays(-1) };
        ledger.Items["raid_cavrak"] = new HomeworkItemState { Suggestion = new HomeworkSuggestion("missionCleared", "레이드 1회 토벌", 1, 1) };
        file.Characters[Main] = ledger;
        store.Save(file);
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, new[] { Raid(1) }));
        Assert.Null(Card(svc, Main, "raid_cavrak").Suggestion);
    }

    [Fact]
    public void WalkingIntoAnotherArea_LaterOn_DoesNotCompleteThatRaid()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, new[] { Raid(1) }));                               // 이미 1/1(계정 값)
        _now += TimeSpan.FromMinutes(10);
        svc.Evaluate(Main, Obs("미측정 지역", new[] { Raid(1) }, Fighting("카브락")));   // 한참 뒤 카브락과 싸우기 시작(카운터는 그대로)
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc, Main, "raid_cavrak").Status);   // 카운터가 새로 오른 것이 아니므로 가리지 않는다
    }

    [Fact]
    public void WhenOneItemOfTheGroupIsAlreadyDone_TheCounterIsExplainedByIt()
    {
        var svc = NewService();
        svc.Set(Main, "raid_airel", completed: true);
        svc.Evaluate(Main, Obs(Town, new[] { Raid(0) }));
        _now += TimeSpan.FromMinutes(5);
        svc.Evaluate(Main, Obs(Town, new[] { Raid(1) }));
        Assert.Null(Card(svc, Main, "raid_cavrak").Suggestion);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main, "raid_cavrak").Status);
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(svc, Main, "raid_airel").Status);
    }

    [Fact]
    public void FieldBoss_UsesTheLargerOfTheTwoCounters_AndTheBossFoughtRecently_AndTheRestOfThePoolIsDone()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs("케오 섬", new[] { Boss1(0), Boss2(0) }, Fighting("크라마")));
        _now += TimeSpan.FromMinutes(4);
        svc.Evaluate(Main, Obs("케오 섬", new[] { Boss1(1), Boss2(1) }, Idle()));
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc, Main, "fieldboss_krama").Status);
        Assert.Equal(HomeworkCardStatus.PoolDone, Card(svc, Main, "fieldboss_peri").Status);          // 택1: 나머지는 이번 주 끝
        Assert.Contains("필드 보스", Card(svc, Main, "fieldboss_krama").Evidence);
    }

    [Fact]
    public void WithoutTheMissionInTheList_NothingChanges_AndTheFirstObservationAfterSwitchingIsIgnored()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(WeeklyMissions: new List<MissionItem> { Other }, Environment: new EnvironmentInfo("채널 1", null, null, Town)));
        Assert.Null(Card(svc, Main, "raid_airel").Suggestion);

        svc.Evaluate(Alt, Obs(Town, new[] { Raid(1) }));                                // 캐릭터를 바꾼 직후 첫 관찰: 이전 캐릭터 값일 수 있다
        Assert.Null(Card(svc, Alt, "raid_airel").Suggestion);
        _now += TimeSpan.FromSeconds(10);
        svc.Evaluate(Alt, Obs(Town, new[] { Raid(0) }));                                // 검술사의 진짜 값은 0
        Assert.Null(Card(svc, Alt, "raid_airel").Suggestion);
    }

    [Fact]
    public void GuessesResetEveryWeek()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, new[] { Abyss(0) }));
        _now += TimeSpan.FromMinutes(2);
        svc.Evaluate(Main, Obs(Town, new[] { Abyss(1) }));                              // 지켜보던 중 올랐다: 어느 어비스인지 모름
        Assert.Equal("missionCleared", Card(svc, Main, "abyss_madness_cave").Suggestion?.Code);

        _now = new DateTimeOffset(2026, 10, 26, 6, 30, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 다음 주 월요일 새벽
        svc.Evaluate(Main, Obs(Town, new[] { Abyss(0) }));
        svc.Evaluate(Main, Obs(Town, new[] { Abyss(0) }));
        Assert.Null(Card(svc, Main, "abyss_madness_cave").Suggestion);                  // 새 주기: 추정도 기준도 처음부터
    }

    [Fact]
    public void TheGuessIsShownOnTheCharacterCardCells_UntilTheItemIsConfirmed()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, new[] { Raid(0), Boss1(0) }));
        _now += TimeSpan.FromMinutes(5);
        svc.Evaluate(Main, Obs(Town, new[] { Raid(1), Boss1(1) }));                       // 레이드·필드 보스 모두 올랐지만 어느 것인지는 모른다
        var autos = svc.GetAutoStatuses(new[] { Main })[Main];
        Assert.All(autos.Where(a => a.Id is "raid_cavrak" or "raid_airel" or "raid_white_succubus"), a => Assert.True(a.Guess, a.Id));
        Assert.All(autos.Where(a => a.Id.StartsWith("abyss_")), a => Assert.False(a.Guess, a.Id));   // 어비스 미션은 안 올랐다
        var boss = svc.GetFieldBossStatuses(new[] { Main })[Main];
        Assert.Equal("done", boss.State);                                                  // 필드 보스는 택1: 카운터가 오른 것만으로 이번 주 끝
        Assert.Null(boss.Item);
        Assert.All(_catalog.Items.Where(d => d.Id.StartsWith("fieldboss_")), d => Assert.Equal(HomeworkCardStatus.PoolDone, Card(svc, Main, d.Id).Status));

        svc.Set(Main, "raid_airel", completed: true);                                     // 사용자가 확인
        svc.Evaluate(Main, Obs(Town, new[] { Raid(1), Boss1(1) }));
        Assert.DoesNotContain(svc.GetAutoStatuses(new[] { Main })[Main], a => a.Guess && a.Id.StartsWith("raid_"));
        Assert.Contains(svc.GetAutoStatuses(new[] { Main })[Main], a => a.Id == "raid_airel" && a.State == "done" && !a.Guess);
    }

    [Fact]
    public void TheGuessSurvivesObservationsThatDidNotReadTheMissions()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(Town, new[] { Boss1(0) }));
        _now += TimeSpan.FromMinutes(2);
        svc.Evaluate(Main, Obs(Town, new[] { Boss1(1) }));                                 // 지켜보던 중 올랐다: 어느 보스인지 모른다
        Assert.Equal("done", svc.GetFieldBossStatuses(new[] { Main })[Main].State);
        _now += TimeSpan.FromSeconds(20);
        svc.Evaluate(Main, new HomeworkObservation(Environment: new EnvironmentInfo("채널 1", null, null, Town)));   // 빠른 관찰: 미션 목록 없음
        Assert.Equal("done", svc.GetFieldBossStatuses(new[] { Main })[Main].State);        // 깜빡이지 않는다
        Assert.Equal(HomeworkCardStatus.PoolDone, Card(svc, Main, "fieldboss_peri").Status);
    }

    // ── 레이드 지역 안 보상 (2026-10-22 서큐버스 실측: 처치 순간 골드 +101,200, 하트 토큰 +3) ──

    private static HomeworkObservation Gold(string space, long gold, long hearts = 3165) => new(
        Currencies: new List<CurrencyItem> { new("골드", gold), new("하트 토큰", hearts) },
        Environment: new EnvironmentInfo("채널 1", null, null, space));

    [Fact]
    public void GoldRisingInsideTheRaidArea_CompletesThatRaid_EvenWhenTheAccountMissionIsAlreadyFull()
    {
        var svc = NewService();
        svc.Evaluate(Main, Gold("먼 바다의 빛바랜 환영", 37_127_037));
        Assert.Equal(HomeworkCardStatus.InProgress, Card(svc, Main, "raid_white_succubus").Status);   // 들어온 것은 진행 중
        _now += TimeSpan.FromSeconds(40);
        svc.Evaluate(Main, Gold("먼 바다의 빛바랜 환영", 37_228_237, 3168));
        var card = Card(svc, Main, "raid_white_succubus");
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Contains("골드", card.Evidence);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main, "raid_cavrak").Status);
    }

    [Fact]
    public void GoldRisingElsewhere_OrAgainstAnOldBaseline_IsNotAClear()
    {
        var svc = NewService();
        svc.Evaluate(Main, Gold(Town, 1_000_000));
        _now += TimeSpan.FromSeconds(30);
        svc.Evaluate(Main, Gold(Town, 1_200_000));                                       // 마을에서 판매
        Assert.All(new[] { "raid_cavrak", "raid_airel", "raid_white_succubus" }, id => Assert.Equal(HomeworkCardStatus.Pending, Card(svc, Main, id).Status));

        _now += TimeSpan.FromHours(3);                                                   // 오래 안 봤다
        svc.Evaluate(Main, Gold("먼 바다의 바위 협곡", 1_500_000));                       // 그 사이 다른 곳에서 번 골드
        Assert.NotEqual(HomeworkCardStatus.AutoDone, Card(svc, Main, "raid_cavrak").Status);
        Assert.Equal(HomeworkCardStatus.InProgress, Card(svc, Main, "raid_cavrak").Status);   // 들어간 것은 진행 중
    }
}
