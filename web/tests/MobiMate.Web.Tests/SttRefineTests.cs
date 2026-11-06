using System.Net;
using System.Text;
using System.Text.Json;
using MobiMate.Web.Voice;

namespace MobiMate.Web.Tests;

/// <summary>AI로 다듬기 (승운 2026-11-07): 잘못 알아들은 낱말·띄어쓰기를 보내기 전에 로컬 Ollama가 고친다. 실패하면 사전 방식으로 보낸다.</summary>
public class SttRefineTests
{
    private static HttpRequestMessage Req(HttpMethod m, string url, object? body = null)
    {
        var r = new HttpRequestMessage(m, url) { Content = new StringContent(JsonSerializer.Serialize(body ?? new { }), Encoding.UTF8, "application/json") };
        r.Headers.Add("X-MobiMate-Req", "1");
        return r;
    }

    private static async Task<JsonElement> Listen(TestHost host, HttpClient c, string heard)
    {
        host.Engines.Utterances = new[] { heard };
        var before = (await TestHost.Data(await c.GetAsync("/api/stt/last"))).ValueKind;
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"))).StatusCode);
        host.Audio.Current!.PushVoice(4);
        var until = DateTime.UtcNow.AddSeconds(5);
        JsonElement last = default;
        while (DateTime.UtcNow < until)
        {
            last = await TestHost.Data(await c.GetAsync("/api/stt/last"));
            if (last.ValueKind == JsonValueKind.Object && last.GetProperty("original").GetString() == heard) break;
            await Task.Delay(20);
        }
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/stop"));
        _ = before;
        return last;
    }

    // ── 믿을지 판정 ──

    [Theory]
    [InlineData("일러한분만더오시면출발해요", "힐러 한 분만 더 오시면 출발해요", "힐러 한 분만 더 오시면 출발해요")]   // 낱말 고침 + 띄어쓰기
    [InlineData("지금레이드들어갑니다", "지금 레이드 들어갑니다.", "지금 레이드 들어갑니다")]                            // 마침표는 지운다
    [InlineData("죽었어요부활부탁드려요", "죽었어요, 부활 부탁드려요?", "죽었어요 부활 부탁드려요")]                       // 쉼표·물음표도
    [InlineData("파티초대부탁드립니다", "\"파티 초대 부탁드립니다\"", "파티 초대 부탁드립니다")]                           // 따옴표도
    [InlineData("지금레이드들어갑니다", "지금 레이드 들어갑니다\n(설명) 레이드에 들어간다는 뜻입니다", "지금 레이드 들어갑니다")] // 첫 줄만
    public void Guard_AcceptsFixesAndSpacing_AndCleansPunctuation(string input, string output, string expected) =>
        Assert.Equal(expected, RefineGuard.Accept(input, output));

    [Theory]
    [InlineData("지금레이드들어갑니다", "")]                                                   // 빈 결과
    [InlineData("지금레이드들어갑니다", "   \n  ")]
    [InlineData("지금레이드들어갑니다", "Sure! 지금 레이드 들어갑니다")]                       // 영문 설명
    [InlineData("지금레이드들어갑니다", "지금 레이드 들어갑니다 😊")]                          // 이모지
    [InlineData("지금레이드들어갑니다", "**지금 레이드 들어갑니다**")]                          // 마크다운
    [InlineData("일러구합니다", "죄송합니다 저는 도와드릴 수 없습니다 그런 요청은 처리하지 않습니다")]   // 딴소리
    [InlineData("딜러한명만더구해요", "딜러 한 명만 더 구해요 그리고 힐러도 두 명 구해요 빨리 오세요")] // 지어낸 내용
    [InlineData("체력이얼마안남았어요조심하세요", "조심")]                                       // 문장을 줄임
    [InlineData("보습앞에서자리잡아주세요", "오늘 날씨가 정말 좋네요 같이 놀러 가요")]                // 완전히 다른 문장
    public void Guard_RejectsInventionExplanationsAndRewrites(string input, string output) =>
        Assert.Null(RefineGuard.Accept(input, output));

    [Fact]
    public void Guard_KeepsAnythingTheInputAlreadyHad_ForeignLettersIncluded() =>
        Assert.Equal("MVP 파티 구해요", RefineGuard.Accept("MVP파티구해요", "MVP 파티 구해요"));   // 입력에 이미 있던 글자는 허용

    [Fact]
    public void EditDistance_CountsSyllables()
    {
        Assert.Equal(0, RefineGuard.Distance("힐러구합니다", "힐러구합니다"));
        Assert.Equal(1, RefineGuard.Distance("일러구합니다", "힐러구합니다"));
        Assert.Equal(3, RefineGuard.Distance("", "가나다"));
    }

    // ── 서비스 흐름 ──

    [Fact]
    public async Task WhenOn_TheFinalIsTheRefinedSentence_TheOriginalStaysRaw_AndTheScreenIsToldWhy()
    {
        using var host = new TestHost();
        host.Refiner.Enabled = true;
        host.Refiner.Reply = t => t == "일러한분만더오시면출발해요" ? "힐러 한 분만 더 오시면 출발해요" : null;
        var c = await host.LocalAsync();
        var fin = await Listen(host, c, "일러한분만더오시면출발해요");
        Assert.Equal("힐러 한 분만 더 오시면 출발해요", fin.GetProperty("text").GetString());
        Assert.Equal("일러한분만더오시면출발해요", fin.GetProperty("original").GetString());   // 학습 비교용 원문은 그대로
        Assert.Contains("AI 다듬기", fin.GetProperty("applied").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task WhenTheRefinerFails_TheDictionaryResultIsSentInstead_NothingIsLost()
    {
        using var host = new TestHost();
        host.Refiner.Enabled = true;   // Reply 기본은 null = 실패·시간 초과
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { spacing = true, spacingLevel = 0 }));
        var fin = await Listen(host, c, "포션이부족해서마을에다녀올게요");
        Assert.Equal("포션이 부족해서 마을에 다녀올게요", fin.GetProperty("text").GetString());
        Assert.DoesNotContain("AI 다듬기", fin.GetProperty("applied").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task AnInventedReply_IsDiscarded_AndTheDictionaryResultIsUsed()
    {
        using var host = new TestHost();
        host.Refiner.Enabled = true;
        host.Refiner.Reply = _ => "오늘 날씨가 정말 좋네요 같이 놀러 가요 지금 당장";
        var c = await host.LocalAsync();
        var fin = await Listen(host, c, "일러한분만더오시면출발해요");
        Assert.Equal("일러한분만더오시면출발해요", fin.GetProperty("text").GetString());   // 띄어쓰기 끔(기준선) + 치환 없음 = 그대로
    }

    [Fact]
    public async Task WhenOff_TheRefinerIsNeverCalled()
    {
        using var host = new TestHost();
        host.Refiner.Enabled = false;
        host.Refiner.Reply = _ => "무조건 바뀜";
        var c = await host.LocalAsync();
        var fin = await Listen(host, c, "지금레이드들어갑니다");
        Assert.Equal("지금레이드들어갑니다", fin.GetProperty("text").GetString());
        Assert.Empty(host.Refiner.Inputs);
    }

    [Fact]
    public async Task TheRefinerGetsTheUnspacedText_AfterLearnedSubstitutions_NotTheSpacedFallback()
    {
        using var host = new TestHost();
        host.Refiner.Enabled = true;
        host.Refiner.Reply = _ => null;
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { spacing = true }));
        await Listen(host, c, "지금레이드들어갑니다");
        Assert.Contains("지금레이드들어갑니다", host.Refiner.Inputs);   // 측정과 같은 조건(띄어쓰기 없는 글자)
    }

    [Fact]
    public async Task ASlowRefiner_IsAbandonedByItsOwnTimeoutLogic_NotByTheService()
    {
        // 서비스는 refiner가 시간 제한을 지킨다고 믿는다. 가짜가 Reply 없이 끝나면 곧바로 사전 방식으로 간다.
        using var host = new TestHost();
        host.Refiner.Enabled = true;
        host.Refiner.DelayMs = 50;
        var c = await host.LocalAsync();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var fin = await Listen(host, c, "지금레이드들어갑니다");
        Assert.Equal("지금레이드들어갑니다", fin.GetProperty("text").GetString());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4));
    }

    // ── 설정·상태 ──

    [Fact]
    public async Task Settings_DefaultToOnWithTheMeasuredBestModel_AndAreValidated()
    {
        using var host = new TestHost { LegacyVoiceBaseline = false };
        var c = await host.LocalAsync();
        var d = await TestHost.Data(await c.GetAsync("/api/stt/settings"));
        Assert.True(d.GetProperty("refine").GetBoolean());
        Assert.Equal("gemma4:e4b", d.GetProperty("refineModel").GetString());
        Assert.Equal(2500, d.GetProperty("refineMs").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { refineMs = 100 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { refineMs = 60000 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { refineModel = "나쁜 모델\n이름" }))).StatusCode);

        var ok = await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { refine = false, refineModel = "gemma4:e2b", refineMs = 1500 }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var after = await TestHost.Data(await c.GetAsync("/api/stt/settings"));
        Assert.False(after.GetProperty("refine").GetBoolean());
        Assert.Equal("gemma4:e2b", after.GetProperty("refineModel").GetString());
        Assert.Equal(1500, after.GetProperty("refineMs").GetInt32());

        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { refineModel = "" }));   // 비우면 기본 모델로
        Assert.Equal("gemma4:e4b", (await TestHost.Data(await c.GetAsync("/api/stt/settings"))).GetProperty("refineModel").GetString());
    }

    [Fact]
    public async Task ChoosingAModel_WarmsItUp_AndTheStatusShowsTheRefiner()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var before = host.Refiner.WarmUps;
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { refineModel = "gemma4:e2b" }));
        var until = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < until && host.Refiner.WarmUps == before) await Task.Delay(10);
        Assert.True(host.Refiner.WarmUps > before);

        var s = await TestHost.Data(await c.GetAsync("/api/stt/status"));
        Assert.True(s.TryGetProperty("refine", out var r));
        Assert.True(r.TryGetProperty("enabled", out _));
        Assert.True(r.TryGetProperty("cloud", out _));
    }

    [Fact]
    public async Task TheModelList_IsPcOnly_AndLanDevicesAreRefused()
    {
        using var host = new TestHost();
        var lan = host.LanClient("192.168.0.50");
        Assert.NotEqual(HttpStatusCode.OK, (await lan.GetAsync("/api/stt/refine/models")).StatusCode);
    }

    [Theory]
    [InlineData("gemma4:31b-cloud", true)]
    [InlineData("glm-5.3:cloud", true)]
    [InlineData("gemma4:e4b", false)]
    [InlineData("exaone3.5:7.8b", false)]
    public void CloudModels_AreRecognisedByName(string model, bool cloud) => Assert.Equal(cloud, OllamaVoiceRefiner.IsCloud(model));
}

/// <summary>실제 Ollama로 확인한다. MOBIMATE_OLLAMA_TEST=1 일 때만 돈다 (일반 시험은 Ollama 없이 통과해야 한다).</summary>
public class SttRefineOllamaTests
{
    [Fact]
    public async Task RealOllama_FixesTheHardCases_FastEnough()
    {
        if (Environment.GetEnvironmentVariable("MOBIMATE_OLLAMA_TEST") != "1") return;
        var dir = Path.Combine(Path.GetTempPath(), "mm-refine-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new MobiMate.Web.Hosting.WebSettingsStore(dir);
            var r = new OllamaVoiceRefiner(settings, new VoiceVocabulary(dir), Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaVoiceRefiner>.Instance);
            await r.WarmUpAsync();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var a = await r.RefineAsync("일러한분만더오시면출발해요", CancellationToken.None);
            var ms = sw.ElapsedMilliseconds;
            var b = await r.RefineAsync("동판밟지마세요", CancellationToken.None);
            var c = await r.RefineAsync("지금레이드들어갑니다", CancellationToken.None);
            Console.WriteLine($"[실제 Ollama] {a} | {b} | {c} | 첫 문장 {ms}ms | 상태 {r.Status.LastError}");
            Assert.Equal("지금 레이드 들어갑니다", c);
            Assert.Equal("힐러 한 분만 더 오시면 출발해요", a);
            Assert.True(ms < 2500, $"{ms}ms");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
