namespace MobiMate.Tests;

/// <summary>FR-HW-17 레이드 클리어 확인 제안 (H-11): 증표가 늘어난 것을 관찰하면 제안만 하고 완료로 세지 않는다.</summary>
public class RaidTokenTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-raid-" + Guid.NewGuid().ToString("N"));
    // 증표가 늘면 "제안"만 하는 일반 경로(TokenAuto 아님)를 검증하는 전용 카탈로그. 실제 카탈로그의 카브락은 TokenAuto(자동 완료)다.
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.Parse("""
    { "version": 1, "sharedPools": {}, "items": [
      { "id": "raid_cavrak", "category": "raid", "period": "weekly", "share": "character", "mode": "manual", "title": "카브락", "subtitle": "", "icon": "raid", "goal": 1, "tokenCurrency": "원정의 증거: 카브락 레이드" },
      { "id": "raid_airel", "category": "raid", "period": "weekly", "share": "character", "mode": "manual", "title": "에이렐", "subtitle": "", "icon": "raid", "goal": 1, "tokenCurrency": "원정의 증거: 에이렐 레이드" },
      { "id": "raid_white_succubus", "category": "raid", "period": "weekly", "share": "character", "mode": "manual", "title": "화이트 서큐버스", "subtitle": "", "icon": "raid", "goal": 1, "tokenCurrency": "원정의 증거: 화이트 서큐버스 레이드" }
    ] }
    """);
    private DateTimeOffset _now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 수요일
    private const string Main = "아이라_격투가";
    private const string Alt = "아이라_석궁사수";
    private const string Cavrak = "원정의 증거: 카브락 레이드";

    public RaidTokenTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);
    private static HomeworkCard Card(HomeworkBoard b, string id) => b.Cards.Single(c => c.Id == id);
    private static HomeworkObservation Obs(params (string Name, long Amount)[] c) =>
        new(Currencies: c.Select(x => new CurrencyItem(x.Name, x.Amount)).Append(new CurrencyItem("골드", 1000)).ToList());

    [Fact]
    public void TokenIncrease_SuggestsOnly_NotCompletion()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs((Cavrak, 3)));
        Assert.Null(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);   // 첫 관찰은 기준값만

        _now += TimeSpan.FromHours(1);
        var change = svc.Evaluate(Main, Obs((Cavrak, 4)));
        Assert.Contains("raid_cavrak", change!.Ids);
        var card = Card(svc.GetBoard(Main), "raid_cavrak");
        Assert.Equal(HomeworkCardStatus.Pending, card.Status);
        Assert.Equal(new HomeworkSuggestion("raidTokenIncreased", Cavrak, 3, 4), card.Suggestion);
        Assert.Equal(0, svc.GetBoard(Main).Weekly.Done);   // 완료로 세지 않음
        Assert.Null(Card(svc.GetBoard(Main), "raid_airel").Suggestion);
    }

    [Fact]
    public void RealCatalog_WeekendBonusOf2_DoesNotCompleteCavrak_But18Does()
    {
        var svc = new HomeworkService(HomeworkCatalog.LoadEmbedded(), new HomeworkStore(_dir), () => _now);
        svc.Evaluate(Main, Obs((Cavrak, 64)));
        _now += TimeSpan.FromHours(1);
        svc.Evaluate(Main, Obs((Cavrak, 66)));   // 에이렐 어려움 클리어 때 들어온 주말 보너스 +2 (실측)
        var card = Card(svc.GetBoard(Main), "raid_cavrak");
        Assert.NotEqual(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Equal(0, svc.GetBoard(Main).Weekly.Done);

        _now += TimeSpan.FromHours(1);
        svc.Evaluate(Main, Obs((Cavrak, 84)));   // 카브락 입문 +18: 기준값은 66으로 갱신돼 있었다
        Assert.Equal(1, svc.GetBoard(Main).Weekly.Done);
    }

    [Fact]
    public void HoldingTokens_WithoutIncrease_NeverSuggests()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs((Cavrak, 5)));
        svc.Evaluate(Main, Obs((Cavrak, 5)));
        svc.Evaluate(Main, Obs((Cavrak, 2)));   // 사용: 기준값만 낮춘다
        Assert.Null(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);
        svc.Evaluate(Main, Obs((Cavrak, 3)));   // 낮아진 기준값에서 늘었다
        Assert.NotNull(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);
    }

    [Fact]
    public void OtherCharacterObservedInBetween_NoComparison()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs((Cavrak, 1)));
        svc.Evaluate(Alt, Obs((Cavrak, 2)));    // 계정 재화일 수 있으므로 다른 캐릭터 관찰이 끼면 비교하지 않는다
        svc.Evaluate(Main, Obs((Cavrak, 2)));
        Assert.Null(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);
        Assert.Null(Card(svc.GetBoard(Alt), "raid_cavrak").Suggestion);
    }

    [Fact]
    public void BaselineFromPreviousWeek_IsNotCompared_AndSuggestionClearsOnReset()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs((Cavrak, 1)));
        svc.Evaluate(Main, Obs((Cavrak, 2)));
        Assert.NotNull(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);

        _now = new DateTimeOffset(2026, 9, 28, 7, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 다음 주 월요일 07:00
        Assert.Null(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);   // 리셋으로 사라짐
        svc.Evaluate(Main, Obs((Cavrak, 3)));   // 지난주 기준값과는 비교하지 않는다
        Assert.Null(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);
    }

    [Fact]
    public void FailedOrEmptyCurrencyQuery_IsIgnored_AndNbspIsNormalized()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(("원정의 증거: 카브락 레이드", 1)));
        svc.Evaluate(Main, new HomeworkObservation(Currencies: null));
        svc.Evaluate(Main, new HomeworkObservation(Currencies: Array.Empty<CurrencyItem>()));
        svc.Evaluate(Main, Obs((Cavrak, 2)));
        Assert.NotNull(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);
    }

    [Fact]
    public void IncompleteList_DoesNotResetBaselineToZero()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs((Cavrak, 3)));
        svc.Evaluate(Main, Obs());               // 골드만 있는 불완전 목록 (증표 없음)
        svc.Evaluate(Main, Obs((Cavrak, 3)));   // 기준값 3 유지 → 늘어나지 않음
        Assert.Null(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);

        var fresh = new HomeworkService(_catalog, new HomeworkStore(Path.Combine(_dir, "b")), () => _now);
        Directory.CreateDirectory(Path.Combine(_dir, "b"));
        fresh.Evaluate(Main, Obs());             // 처음부터 증표가 목록에 없음
        fresh.Evaluate(Main, Obs((Cavrak, 3)));  // 보유량만으로는 제안하지 않는다
        Assert.Null(Card(fresh.GetBoard(Main), "raid_cavrak").Suggestion);
    }

    [Fact]
    public void OtherCharacterBeforeRestart_IsRemembered()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs((Cavrak, 1)));
        svc.Evaluate(Alt, Obs((Cavrak, 2)));
        svc = NewService();                      // 재기동
        svc.Evaluate(Main, Obs((Cavrak, 2)));   // 사이에 Alt 관찰이 있었다 → 비교하지 않음
        Assert.Null(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);
        svc.Evaluate(Main, Obs((Cavrak, 2)));
        svc.Evaluate(Main, Obs((Cavrak, 3)));
        Assert.NotNull(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);
    }

    [Fact]
    public void SeveralTokensIncrease_EachSuggested()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs((Cavrak, 1), ("원정의 증거: 에이렐 레이드", 0)));
        svc.Evaluate(Main, Obs((Cavrak, 2), ("원정의 증거: 에이렐 레이드", 1)));
        var b = svc.GetBoard(Main);
        Assert.NotNull(Card(b, "raid_cavrak").Suggestion);
        Assert.NotNull(Card(b, "raid_airel").Suggestion);
        Assert.Null(Card(b, "raid_white_succubus").Suggestion);
    }

    [Fact]
    public void Suggestion_SurvivesRestart()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs((Cavrak, 1)));
        svc.Evaluate(Main, Obs((Cavrak, 2)));
        var reloaded = NewService();
        Assert.Equal(new HomeworkSuggestion("raidTokenIncreased", Cavrak, 1, 2), Card(reloaded.GetBoard(Main), "raid_cavrak").Suggestion);
    }

    [Fact]
    public void PartialNameMatch_IsNotUsed()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs(("원정의 증거: 카브락 레이드 (구)", 1)));
        svc.Evaluate(Main, Obs(("원정의 증거: 카브락 레이드 (구)", 2)));
        Assert.Null(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);
    }

    [Fact]
    public void ManualCompletion_ClearsSuggestion_AndBaselineSurvivesRestart()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs((Cavrak, 1)));
        svc = NewService();   // 서버 재시작: 기준값은 파일에서 이어진다
        svc.Evaluate(Main, Obs((Cavrak, 2)));
        Assert.NotNull(Card(svc.GetBoard(Main), "raid_cavrak").Suggestion);

        svc.Set(Main, "raid_cavrak", completed: true);
        var card = Card(svc.GetBoard(Main), "raid_cavrak");
        Assert.Equal(HomeworkCardStatus.ManualDone, card.Status);
        Assert.Null(card.Suggestion);
    }
}
