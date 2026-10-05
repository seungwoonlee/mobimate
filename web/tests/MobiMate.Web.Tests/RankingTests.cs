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
    public void Manual_SetAndClear()
    {
        var s = Service();
        Assert.True(s.SetManual(Key, 1, 523, 91000).Ok);
        var e = s.ViewOf(Key).Entries["combat"];
        Assert.Equal(523, e.Rank);
        Assert.Equal("manual", e.Source);
        Assert.Equal("pink", s.BadgeOf(Key)!.Tier);

        Assert.True(s.Clear(Key, 1));
        Assert.Empty(s.ViewOf(Key).Entries);
    }

    [Theory]
    [InlineData("아이라_격투가", 1, 0)]     // 순위는 1 이상
    [InlineData("아이라_격투가", 1, -3)]
    [InlineData("아이라_격투가", 7, 5)]     // 알 수 없는 종류
    [InlineData("아이라_궁수", 1, 5)]       // 별칭이 없는 캐릭터
    [InlineData("없는_캐릭터", 1, 5)]
    public void Manual_RejectsBadInput(string key, int kind, int rank) => Assert.False(Service().SetManual(key, kind, rank, null).Ok);

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

    [Fact]
    public async Task Manual_PutAndDelete()
    {
        var (host, c, _) = await Ready();
        using var _h = host;
        var put = await c.PutAsJsonAsync("/api/rankings/manual", new { key = "아이라_격투가", kind = 1, rank = 523, score = 91000 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("pink", (await TestHost.Data(put)).GetProperty("entries").GetProperty("combat").GetProperty("tier").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/rankings/manual", new { key = "아이라_격투가", kind = 1, rank = 0 })).StatusCode);

        var del = await c.DeleteAsync("/api/rankings/manual?key=" + Uri.EscapeDataString("아이라_격투가") + "&kind=1");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.Empty((await TestHost.Data(del)).GetProperty("entries").EnumerateObject());
    }
}
