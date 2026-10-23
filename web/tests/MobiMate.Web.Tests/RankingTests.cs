using System.Net;
using System.Net.Http.Json;
using System.Text;
using MobiMate.Web.Endpoints;
using MobiMate.Web.Services;

namespace MobiMate.Web.Tests;

/// <summary>서버 랭킹 저장·조회와 북마크릿 API (v0.3)</summary>
public sealed class RankingServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-rank-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly SnapshotManager _snap;

    public RankingServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _snap = new SnapshotManager(_dir);
        Seed("아이라", "격투가", 90000, "헤니컵A");
        Seed("아이라", "궁수", 80000, "");            // 별칭 없음: 조회 대상이 아니다
        Seed("에린", "마법사", 70000, "이름있음");     // 서버를 모른다
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private RankingService Service() => new(_dir, _snap, () => _now);

    private void Seed(string realm, string job, long combat, string nick)
    {
        var info = new CharacterInfo(null, realm, 100, job, new ScoreVal("전투력", combat), null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        _snap.UpdateSnapshot(info, new List<CurrencyItem> { new("골드", 1), new("데카", 5), new("M캐시", 9) }, null);
        if (nick != "") _snap.SetCustomName(realm, job, nick);
    }

    internal static string Item(int rank, string name, string cls, string type, params string[] scores) =>
        $"<li class=\"item rank01 on\"> <div> <dl> <dt>{rank}위</dt> </dl> </div> <div> <dl> <dt>서버명</dt> <dd>아이라</dd> </dl> </div> " +
        $"<div> <dl> <dt>캐릭터명</dt> <dd data-charactername=\"{name}\">{name}</dd> </dl> </div> <div> <dl> <dt>클래스</dt> <dd class=\"warrior_4\"> {cls} </dd> </dl> </div> " +
        string.Concat(scores.Select((v, i) => $"<div> <dl> <dt>{type}</dt> <dd class=\"type_{i + 1}\"> {v} </dd> </dl> </div> ")) + "</li>";

    private const string Key = "아이라_격투가";

    [Fact]
    public void Targets_AreCharactersWithANameAndAKnownServer()
    {
        var t = Service().Targets();
        Assert.Single(t);
        Assert.Equal(new RankTarget(Key, "헤니컵A", 2, "아이라", "격투가", 90000), t[0]);
    }

    [Fact]
    public void Import_StoresTheFourKinds_AndTheBadgeFollowsTheCombatRank()
    {
        var s = Service();
        var r = s.Import(new[]
        {
            new RankImportItem(Key, "헤니컵A", 1, Item(8, "헤니컵A", "격투가", "전투력", "91,000")),
            new RankImportItem(Key, "헤니컵A", 4, Item(1, "헤니컵A", "격투가", "종합 점수", "245,172", "141,099", "45,237", "58,836")),
            new RankImportItem(Key, "헤니컵A", 3, Item(9, "헤니컵A", "격투가", "생활력", "45,237")),
            new RankImportItem(Key, "헤니컵A", 2, Item(66, "헤니컵A", "격투가", "매력", "58,836")),
        });
        Assert.Equal(4, r.Accepted);
        Assert.Equal(0, r.Rejected);
        var v = s.ViewOf(Key);
        Assert.Equal(8, v.Entries["combat"].Rank);
        Assert.Equal("gold", v.Entries["combat"].Tier);
        Assert.Equal(1, v.Entries["total"].Rank);
        Assert.Equal(245172, v.Entries["total"].Score);
        Assert.Equal(66, v.Entries["attract"].Rank);
        var badge = s.BadgeOf(Key)!;
        Assert.Equal(8, badge.Rank);
        Assert.Equal("gold", badge.Tier);
        Assert.False(badge.Stale);
    }

    /// <summary>2026-10-23 넥슨 종합 랭킹 화면의 실제 형식(검색한 캐릭터는 클래스에 on이 붙는다)</summary>
    private const string TotalRealFormat =
        "<li class=\"item rank05 on\"> <div> <dl> <dt>3,190위</dt> </dl> </div> <div> <dl> <dt>서버명</dt> <dd>아이라</dd> </dl> </div> <div> <dl> <dt>길드명</dt> <dd>천사S2</dd> </dl> </div> " +
        "<div> <dl> <dt>캐릭터명</dt> <dd data-charactername=\"헤니컵A\">헤니컵A</dd> </dl> </div> <div> <dl> <dt>클래스</dt> <dd class=\"warrior_4\"> 격투가 </dd> </dl> </div> " +
        "<div> <dl> <dt> <span>종합 점수</span> <span>194,656</span> </dt> <dd> <span class=\"type_1\"><em>전투력</em>91,000</span> <span class=\"type_3\"><em>생활력</em>28,100</span> <span class=\"type_2\"><em>매력</em>51,308</span> </dd> </dl> </div> </li>";

    [Fact]
    public void TotalRanking_InTheCurrentNexonFormat_IsRead()
    {
        var s = Service();
        var r = s.Import(new[] { new RankImportItem(Key, "헤니컵A", 4, TotalRealFormat) });
        Assert.Equal(1, r.Accepted);
        var e = s.ViewOf(Key).Entries["total"];
        Assert.Equal(3190, e.Rank);
        Assert.Equal(194656, e.Score);
        var reading = RankingParser.Parse(TotalRealFormat, RankKind.Total)!;
        Assert.Equal(91000, reading.CombatScore);
        Assert.Equal(28100, reading.LivingScore);
        Assert.Equal(51308, reading.AttractScore);
    }

    [Fact]
    public void TotalRanking_WithMissingSubScores_IsNotAccepted()
    {
        var broken = TotalRealFormat.Replace("<span class=\"type_2\"><em>매력</em>51,308</span>", "");
        Assert.Null(RankingParser.Parse(broken, RankKind.Total));
    }

    [Theory]
    [InlineData(10, "gold")]
    [InlineData(11, "orange")]
    [InlineData(101, "pink")]
    [InlineData(1001, "purple")]
    public void Badge_TiersFollowTheRank(int rank, string tier)
    {
        var s = Service();
        s.Import(new[] { new RankImportItem(Key, "헤니컵A", 1, Item(rank, "헤니컵A", "격투가", "전투력", "90,000")) });
        Assert.Equal(tier, s.BadgeOf(Key)!.Tier);
    }

    [Fact]
    public void LivingAttractAndTotal_GetTheSameBadgeRule_AsCombat()
    {
        var s = Service();
        s.Import(new[]
        {
            new RankImportItem(Key, "헤니컵A", 3, Item(9, "헤니컵A", "격투가", "생활력", "45,237")),                              // 10위 이내: 골드
            new RankImportItem(Key, "헤니컵A", 2, Item(66, "헤니컵A", "격투가", "매력", "58,836")),                               // 100위 이내: 주황
            new RankImportItem(Key, "헤니컵A", 4, Item(523, "헤니컵A", "격투가", "종합 점수", "245,172", "141,099", "45,237", "58,836")),   // 1000위 이내: 핑크
        });
        Assert.Equal("gold", s.BadgeOf(Key, "living")!.Tier);
        Assert.Equal("orange", s.BadgeOf(Key, "attract")!.Tier);
        Assert.Equal("pink", s.BadgeOf(Key, "total")!.Tier);
        Assert.Null(s.BadgeOf(Key));                                      // 전투력은 아직 없다
        Assert.Null(s.BadgeOf(Key, "combat"));
    }

    [Fact]
    public void BeyondTenThousand_OrNotFound_HasNoBadge_ButIsKept()
    {
        var s = Service();
        s.Import(new[] { new RankImportItem(Key, "헤니컵A", 1, Item(10001, "헤니컵A", "격투가", "전투력", "90,000")) });
        Assert.Null(s.BadgeOf(Key));
        Assert.Equal(10001, s.ViewOf(Key).Entries["combat"].Rank);

        s.Import(new[] { new RankImportItem(Key, "헤니컵A", 1, "<div>결과가 없습니다.</div>") });
        Assert.Null(s.ViewOf(Key).Entries["combat"].Rank);   // 순위 없음을 확인한 값
        Assert.Null(s.BadgeOf(Key));
    }

    [Fact]
    public void Import_RejectsWhatDoesNotBelong()
    {
        var s = Service();
        var good = Item(5, "헤니컵A", "격투가", "전투력", "90,000");
        var r = s.Import(new[]
        {
            new RankImportItem("없는_캐릭터", "x", 1, good),                                 // 조회 대상이 아님
            new RankImportItem(Key, "다른이름", 1, good),                                    // 이름이 지금 별칭과 다름
            new RankImportItem(Key, "헤니컵A", 9, good),                                     // 알 수 없는 종류
            new RankImportItem(Key, "헤니컵A", 1, "<html>보안 검사를 진행해 주세요</html>"),  // 형식이 다름 (순위 없음과 다르다)
            new RankImportItem(Key, "헤니컵A", 1, Item(5, "헤니컵A", "마법사", "전투력", "150,000")),   // 직업이 다르고 전투력이 20% 넘게 다름: 동명이인
        });
        Assert.Equal(0, r.Accepted);
        Assert.Equal(5, r.Rejected);
        Assert.Empty(s.ViewOf(Key).Entries);
    }

    [Fact]
    public void SameNameWithADifferentClass_ButSimilarScore_IsAccepted()
    {
        var s = Service();
        var r = s.Import(new[] { new RankImportItem(Key, "헤니컵A", 1, Item(5, "헤니컵A", "대검전사", "전투력", "95,000")) });   // 주↔부직업 전환 등
        Assert.Equal(1, r.Accepted);
    }

    [Theory]
    [InlineData("108,000", 1)]   // 전투력 9만의 정확히 +20%: 같은 캐릭터로 본다
    [InlineData("72,000", 1)]    // 정확히 -20%
    [InlineData("108,001", 0)]   // 20%를 넘으면 동명이인
    [InlineData("71,999", 0)]
    public void HomonymGuard_BoundaryIsExactlyTwentyPercent(string score, int accepted)
    {
        var r = Service().Import(new[] { new RankImportItem(Key, "헤니컵A", 1, Item(5, "헤니컵A", "마법사", "전투력", score)) });   // 직업이 다르다
        Assert.Equal(accepted, r.Accepted);
    }

    [Fact]
    public void ChangingTheNickname_MakesOldRanksUnusable()
    {
        var s = Service();
        s.Import(new[] { new RankImportItem(Key, "헤니컵A", 1, Item(5, "헤니컵A", "격투가", "전투력", "90,000")) });
        Assert.NotNull(s.BadgeOf(Key));

        _snap.SetCustomName("아이라", "격투가", "새이름");
        var v = s.ViewOf(Key);
        Assert.True(v.NameChanged);
        Assert.Empty(v.Entries);
        Assert.Null(s.BadgeOf(Key));

        s.Import(new[] { new RankImportItem(Key, "새이름", 1, Item(50, "새이름", "격투가", "전투력", "90,000")) });
        Assert.False(s.ViewOf(Key).NameChanged);
        Assert.Equal("orange", s.BadgeOf(Key)!.Tier);
    }

    [Fact]
    public void Ranks_AreStaleAfterSixHours_ButStillShown()
    {
        var s = Service();
        s.Import(new[] { new RankImportItem(Key, "헤니컵A", 1, Item(5, "헤니컵A", "격투가", "전투력", "90,000")) });
        _now += TimeSpan.FromHours(5) + TimeSpan.FromMinutes(59);
        Assert.False(s.ViewOf(Key).Entries["combat"].Stale);
        _now += TimeSpan.FromMinutes(2);
        Assert.True(s.ViewOf(Key).Entries["combat"].Stale);
        Assert.True(s.BadgeOf(Key)!.Stale);   // 오래돼도 뱃지는 남기고 "오래됨"으로 표시한다
    }

    [Fact]
    public void Ranks_AndTheTokenSurviveARestart()
    {
        var s = Service();
        s.Import(new[] { new RankImportItem(Key, "헤니컵A", 1, Item(5, "헤니컵A", "격투가", "전투력", "90,000")) });
        var again = Service();
        Assert.Equal(5, again.ViewOf(Key).Entries["combat"].Rank);
        Assert.Equal(s.Token, again.Token);
        Assert.True(again.TokenOk(s.Token));
        Assert.False(again.TokenOk("틀린 토큰"));
        Assert.False(again.TokenOk(null));
    }

    [Fact]
    public void Bookmarklet_IsOneLine_AndCarriesThePortAndToken_AndNeverTheCharacterNames()
    {
        var js = RankingBookmarklet.Build(17800, "abc123");
        Assert.StartsWith("javascript:", js);
        Assert.DoesNotContain('\n', js);
        Assert.Contains("http://127.0.0.1:17800", js);
        Assert.Contains("'abc123'", js);
        Assert.Contains("mabinogimobile.nexon.com", js);
        Assert.DoesNotContain("헤니컵", js);
        Assert.DoesNotContain("__PORT__", js);
        Assert.DoesNotContain("__TOKEN__", js);
    }

    [Fact]
    public void Bookmarklet_SavesPerCharacter_StopsWhenNothingIsReadable_AndShowsWhySomeWereSkipped()
    {
        var js = RankingBookmarklet.Build(17800, "abc123");
        // 캐릭터마다 바로 저장해 도중에 끊겨도 앞부분이 남는다(2026-10-23: 한꺼번에 저장하던 때 뒤쪽 5명이 비었다)
        Assert.Equal(1, js.Split("/api/rankings/import").Length - 1);   // 호출 위치는 하나(반복문 안)
        Assert.Contains("blind>=2", js);                                 // 두 캐릭터 연속으로 하나도 못 읽으면 멈춘다
        Assert.Contains("probs.join", js);                               // 제외된 이유를 화면에 보여 준다
        Assert.Contains("t.kinds", js);                                  // 최신인 종류는 건너뛴다(넥슨에 한꺼번에 너무 많이 부르지 않게)
        Assert.Contains("nap(2000)", js);                                // 요청 사이 2초
        Assert.Contains("nap(6000)", js);                                // 읽지 못하면 한 번 쉬었다 다시
        Assert.Equal(3, js.Split("로그인 후 이용해").Length - 1);   // 시작 전 확인·안내 문구, 읽지 못했을 때 확인: 로그아웃을 두 곳에서 확인한다
        Assert.Contains("로그아웃된 것 같아요", js);
    }

    [Fact]
    public void KindsNeeded_OnlyWhatIsMissingOrOlderThanFiveMinutes()
    {
        var s = Service();
        var t = s.Targets().Single();
        Assert.Equal(new[] { 4, 1, 3, 2 }, s.KindsNeeded(t));                                   // 아무것도 없다
        s.Import(new[]
        {
            new RankImportItem(Key, "헤니컵A", 1, Item(8, "헤니컵A", "격투가", "전투력", "91,000")),
            new RankImportItem(Key, "헤니컵A", 3, Item(9, "헤니컵A", "격투가", "생활력", "45,237")),
        });
        Assert.Equal(new[] { 4, 2 }, s.KindsNeeded(t));                                         // 종합·매력만 남았다
        _now += TimeSpan.FromMinutes(4);
        Assert.Equal(new[] { 4, 2 }, s.KindsNeeded(t));                                         // 방금 가져온 것은 건너뛴다
        _now += TimeSpan.FromMinutes(2);
        Assert.Equal(new[] { 4, 1, 3, 2 }, s.KindsNeeded(t));                                   // 5분이 지나면 다시 가져온다
    }

    [Fact]
    public void TargetsByNeed_PutsMissingAndOldestFirst_SoAnInterruptedRunFillsTheGaps()
    {
        var s = Service();
        s.Import(new[] { new RankImportItem(Key, "헤니컵A", 1, Item(8, "헤니컵A", "격투가", "전투력", "91,000")) });   // 한 종류만 있다
        Assert.Single(s.TargetsByNeed());

        // 두 번째 대상: 순위가 하나도 없는 캐릭터가 한 종류만 있는 캐릭터보다 먼저 와야 한다
        Seed("아이라", "전사", 70000, "순위없음");
        var order = s.TargetsByNeed().Select(t => t.Name).ToList();
        Assert.Equal("순위없음", order[0]);
        Assert.Equal("헤니컵A", order[1]);
    }
}

/// <summary>북마크릿이 부르는 API: 토큰과 CORS</summary>
public class RankingApiTests
{
    private static async Task<(TestHost host, HttpClient c, string token)> Ready()
    {
        var host = new TestHost();
        var c = await host.LocalAsync();
        await c.GetAsync("/api/header");   // 가짜 게임의 캐릭터를 기록에 올린다
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/profile/nickname", new { nickname = "헤니컵A" })).StatusCode);
        var setup = await TestHost.Data(await c.GetAsync("/api/rankings/setup"));
        var js = setup.GetProperty("bookmarklet").GetString()!;
        var token = js.Split('\'').First(s => s.Length == 24 && s.All(Uri.IsHexDigit));
        return (host, c, token);
    }

    private static HttpRequestMessage FromNexon(string url, object body, string method = "POST")
    {
        var m = new HttpRequestMessage(new HttpMethod(method), url) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "text/plain") };
        m.Headers.Add("Origin", RankingEndpoints.NexonOrigin);
        return m;
    }

    [Fact]
    public async Task Targets_NeedTheToken_AndAnswerTheNexonOriginOnly()
    {
        var (host, c, token) = await Ready();
        using var _ = host;

        var bad = await c.SendAsync(FromNexon("/api/rankings/targets", new { token = "틀림" }));
        Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);

        var ok = await c.SendAsync(FromNexon("/api/rankings/targets", new { token }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(RankingEndpoints.NexonOrigin, ok.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var targets = (await TestHost.Data(ok)).GetProperty("targets");
        Assert.Equal(1, targets.GetArrayLength());
        Assert.Equal("헤니컵A", targets[0].GetProperty("name").GetString());
        Assert.Equal(2, targets[0].GetProperty("serverId").GetInt32());

        var other = new HttpRequestMessage(HttpMethod.Post, "/api/rankings/targets") { Content = new StringContent($"{{\"token\":\"{token}\"}}", Encoding.UTF8, "text/plain") };
        other.Headers.Add("Origin", "https://evil.example");
        Assert.False((await c.SendAsync(other)).Headers.Contains("Access-Control-Allow-Origin"));   // 다른 페이지에는 CORS를 열지 않는다
    }

    [Fact]
    public async Task Setup_IsGivenOnlyToTheGamePc_NotToLanDevices()
    {
        var (host, c, _) = await Ready();
        using var _h = host;
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/rankings/setup")).StatusCode);
        var lan = await host.LanAsync(c);
        var r = await lan.GetAsync("/api/rankings/setup");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("LOCAL_ONLY", await TestHost.ErrorCode(r));
    }

    [Fact]
    public async Task Preflight_AllowsThePrivateNetworkRequestFromNexon()
    {
        var (host, c, _) = await Ready();
        using var _h = host;
        var r = await c.SendAsync(FromNexon("/api/rankings/import", new { }, "OPTIONS"));
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal("true", r.Headers.GetValues("Access-Control-Allow-Private-Network").Single());
        Assert.Equal(RankingEndpoints.NexonOrigin, r.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Import_StoresRanks_AndTheCharactersListShowsTheBadge()
    {
        var (host, c, token) = await Ready();
        using var _h = host;
        var item = RankingServiceTests.Item(8, "헤니컵A", "격투가", "전투력", "91,000");
        var r = await c.SendAsync(FromNexon("/api/rankings/import", new { token, items = new[] { new { key = "아이라_격투가", name = "헤니컵A", kind = 1, html = item } } }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1, (await TestHost.Data(r)).GetProperty("accepted").GetInt32());

        var all = await TestHost.Data(await c.GetAsync("/api/rankings"));
        Assert.Equal(8, all.GetProperty("characters").GetProperty("아이라_격투가").GetProperty("entries").GetProperty("combat").GetProperty("rank").GetInt32());

        var chars = await TestHost.Data(await c.GetAsync("/api/characters"));
        var me = ViewsTests.Cards(chars).First(m => m.GetProperty("key").GetString() == "아이라_격투가");
        Assert.Equal("gold", me.GetProperty("combatRank").GetProperty("tier").GetString());

        var header = await TestHost.Data(await c.GetAsync("/api/header"));
        Assert.Equal(8, header.GetProperty("scores").GetProperty("combatRank").GetProperty("rank").GetInt32());
    }

    [Fact]
    public async Task Import_WithABadToken_IsRefused_AndNothingIsStored()
    {
        var (host, c, _) = await Ready();
        using var _h = host;
        var item = RankingServiceTests.Item(8, "헤니컵A", "격투가", "전투력", "91,000");
        var r = await c.SendAsync(FromNexon("/api/rankings/import", new { token = "틀림", items = new[] { new { key = "아이라_격투가", name = "헤니컵A", kind = 1, html = item } } }));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var all = await TestHost.Data(await c.GetAsync("/api/rankings"));
        Assert.Empty(all.GetProperty("characters").GetProperty("아이라_격투가").GetProperty("entries").EnumerateObject());
    }

}
