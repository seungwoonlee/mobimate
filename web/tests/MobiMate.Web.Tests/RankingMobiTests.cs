using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MobiMate.Web.Services;

namespace MobiMate.Web.Tests;

/// <summary>모비라이프 순위 조회(2026-10-22): 버튼 한 번, 캐릭터당 한 번, 쿨다운, 막히면 멈춤, 엉뚱한 줄은 저장하지 않음</summary>
public sealed class RankingMobiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-mobi-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly SnapshotManager _snap;
    private readonly List<HttpRequestMessage> _calls = new();
    private Func<HttpRequestMessage, HttpResponseMessage> _reply = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };

    public RankingMobiTests()
    {
        Directory.CreateDirectory(_dir);
        _snap = new SnapshotManager(_dir);
        Seed("아이라", "힐러", 115000, "GALAXYZ");
        Seed("아이라", "궁수", 100000, "활잡이");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void Seed(string realm, string job, long combat, string nick)
    {
        var info = new CharacterInfo(null, realm, 100, job, new ScoreVal("전투력", combat), null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        _snap.UpdateSnapshot(info, new List<CurrencyItem> { new("골드", 1), new("데카", 5), new("M캐시", 9) }, null);
        _snap.SetCustomName(realm, job, nick);
    }

    private sealed class Handler(RankingMobiTests t) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            t._calls.Add(request);
            return Task.FromResult(t._reply(request));
        }
    }

    private RankingService Ranking() => new(_dir, _snap, () => _now);

    private RankingMobiRefresher Refresher(RankingService r) =>
        new(r, new MobiRankClient(() => new HttpClient(new Handler(this))), NullLogger<RankingMobiRefresher>.Instance, TimeSpan.Zero, (_, _) => Task.CompletedTask);

    private static string Row(string name, string server, long combat, int combatRank = 2273) =>
        $"{{\"character_name\":\"{name}\",\"server\":\"{server}\",\"klass\":\"10\",\"combat\":{combat},\"charm\":51308,\"life_skill\":28100,\"total\":194656," +
        $"\"server_combat_rank\":{combatRank},\"server_charm_rank\":2851,\"server_life_skill_rank\":23584,\"server_total_rank\":3190}}";

    private void RespondByName(Func<string, string> body) =>
        _reply = req => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[" + body(Uri.UnescapeDataString(req.RequestUri!.Query.Split("character_name=")[1])) + "]", Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Refresh_StoresTheFourServerRanks_AndAsksOncePerCharacter_WithAnHonestUserAgent()
    {
        var r = Ranking();
        RespondByName(n => n == "GALAXYZ" ? Row("GALAXYZ", "02", 115248) : Row("활잡이", "02", 99000, 6846));
        var res = await Refresher(r).RunAsync(CancellationToken.None);

        Assert.True(res.Started);
        Assert.Equal(2, res.Updated);
        Assert.Equal(2, _calls.Count);                                        // 캐릭터당 한 번, 재시도 없음
        Assert.All(_calls, c => Assert.Contains("MobiMate", c.Headers.UserAgent.ToString()));
        Assert.Contains(_calls, c => c.RequestUri!.Query == "?character_name=GALAXYZ");
        Assert.Contains(_calls, c => c.RequestUri!.Query == "?character_name=" + Uri.EscapeDataString("활잡이"));   // 이름은 주소에 맞게 인코딩
        var v = r.ViewOf("아이라_힐러");
        Assert.Equal(2273, v.Entries["combat"].Rank);
        Assert.Equal(2851, v.Entries["attract"].Rank);
        Assert.Equal(23584, v.Entries["living"].Rank);
        Assert.Equal(3190, v.Entries["total"].Rank);
        Assert.Equal("mabimobi", v.Entries["combat"].Source);
        Assert.Equal(115248, v.Entries["combat"].Score);
    }

    [Fact]
    public async Task NameIsEscaped_AndOnlyTheSameServerAndNameIsTaken()
    {
        var r = Ranking();
        RespondByName(n => string.Join(",", Row("GALAXYZ", "05", 115000, 1), Row("GALAXYZ2", "02", 115000, 2), Row("galaxyz", "02", 115248, 2273)));
        var res = await Refresher(r).RunAsync(CancellationToken.None);
        Assert.True(res.Updated >= 1);
        Assert.Equal(2273, r.ViewOf("아이라_힐러").Entries["combat"].Rank);   // 다른 서버·다른 이름의 줄은 쓰지 않고 이름·서버가 맞는 줄만
    }

    [Fact]
    public async Task NotFound_AndAMismatchedCharacter_AreReportedAndDoNotOverwriteWhatWeHave()
    {
        var r = Ranking();
        r.Import(new[] { new RankImportItem("아이라_힐러", "GALAXYZ", 1, RankingServiceTests.Item(4000, "GALAXYZ", "힐러", "전투력", "115,000")) });   // 이미 있던 값(북마크릿)
        RespondByName(n => n == "GALAXYZ" ? "" : Row("활잡이", "02", 30000));   // 힐러는 사이트에 없고, 궁수는 전투력이 너무 다르다
        var res = await Refresher(r).RunAsync(CancellationToken.None);
        Assert.Equal(0, res.Updated);
        Assert.Equal(new[] { "GALAXYZ" }, res.NotFound);
        Assert.Equal(new[] { "활잡이" }, res.Mismatch);
        Assert.Equal(4000, r.ViewOf("아이라_힐러").Entries["combat"].Rank);    // "사이트에 없음"을 "순위 없음"으로 덮어쓰지 않는다
        Assert.Empty(r.ViewOf("아이라_궁수").Entries);
    }

    [Fact]
    public async Task CooldownBlocksASecondRun_EvenAfterARestart_UntilItExpires()
    {
        var first = Ranking();
        await Refresher(first).RunAsync(CancellationToken.None);
        var calls = _calls.Count;

        _now += TimeSpan.FromMinutes(3);
        var again = await Refresher(Ranking()).RunAsync(CancellationToken.None);   // 서버를 다시 켠 것과 같다(저장된 시각을 읽는다)
        Assert.False(again.Started);
        Assert.InRange(again.WaitSeconds, 400, 420);
        Assert.Equal(calls, _calls.Count);                                         // 사이트를 부르지 않았다

        _now += TimeSpan.FromMinutes(8);
        Assert.True((await Refresher(Ranking()).RunAsync(CancellationToken.None)).Started);
    }

    [Fact]
    public async Task ABlockedAnswer_StopsImmediately()
    {
        _reply = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var res = await Refresher(Ranking()).RunAsync(CancellationToken.None);
        Assert.Single(_calls);
        Assert.Contains("거절", res.Stopped);
        Assert.Equal(0, res.Updated);
    }

    [Fact]
    public async Task TwoFailuresInARow_Stop_AndTheyAreNotRetried()
    {
        Seed("아이라", "전사", 90000, "전사님");
        _reply = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var res = await Refresher(Ranking()).RunAsync(CancellationToken.None);
        Assert.Equal(2, _calls.Count);
        Assert.NotNull(res.Stopped);
    }

    [Fact]
    public async Task OnlyOneRunAtATime()
    {
        var gate = new TaskCompletionSource();
        var r = Ranking();
        var refresher = new RankingMobiRefresher(r, new MobiRankClient(() => new HttpClient(new Handler(this))), NullLogger<RankingMobiRefresher>.Instance, TimeSpan.Zero, (_, _) => gate.Task);
        var first = refresher.RunAsync(CancellationToken.None);
        var second = await refresher.RunAsync(CancellationToken.None);
        Assert.False(second.Started);
        gate.SetResult();
        await first;
    }

    [Fact]
    public void Parse_IgnoresLinesWithoutNameOrServer_AndNonArrays()
    {
        Assert.Null(MobiRankClient.Parse("{\"error\":1}"));
        var rows = MobiRankClient.Parse("[{\"character_name\":\"A\"},{\"server\":\"02\"},{\"character_name\":\"B\",\"server\":\"02\",\"server_combat_rank\":7}]")!;
        Assert.Single(rows);
        Assert.Equal(7, rows[0].ServerCombatRank);
    }
}
