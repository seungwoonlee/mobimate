using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MobiMate.Web.Tests;

/// <summary>조회·조작·숙제·채팅·AI API (가짜 CLI 기준).</summary>
public class ApiTests
{
    [Fact]
    public async Task Header_ReturnsCharacterWeightAndErinnTime()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var d = await TestHost.Data(await c.GetAsync("/api/header"));
        Assert.Equal("아이라_격투가", d.GetProperty("characterKey").GetString());
        Assert.Equal(88737, d.GetProperty("character").GetProperty("combatScore").GetInt64());
        Assert.Equal("ok", d.GetProperty("weight").GetProperty("level").GetString());
        Assert.StartsWith("에린 시간 2959-4-23 15:51", d.GetProperty("location").GetProperty("erinn").GetString());
    }

    [Fact]
    public async Task Header_HasFourScores_AndCutoffsFollowFr_Co()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var h = await TestHost.Data(await c.GetAsync("/api/header"));
        var scores = h.GetProperty("scores");
        Assert.Equal(88737, scores.GetProperty("combat").GetInt64());
        Assert.Equal(4316, scores.GetProperty("mdef").GetInt64());
        Assert.Equal(23011, scores.GetProperty("living").GetInt64());
        Assert.Equal(19745, scores.GetProperty("attract").GetInt64());

        // 전투력 88,737 / 마도저항 4,316: 어비스 지옥 1 입장 가능, 지옥 1 압도 저항(4,400) 미달 → 매우 어려움 추천(압도 근접)
        var cut = await TestHost.Data(await c.GetAsync("/api/cutoffs"));
        var abyss = cut.GetProperty("contents").EnumerateArray().Single(x => x.GetProperty("id").GetString() == "abyss");
        Assert.Equal("지옥 1", abyss.GetProperty("maxEntryTier").GetString());
        Assert.Equal("매우 어려움", abyss.GetProperty("recommendedTier").GetString());
        Assert.Equal("near", abyss.GetProperty("status").GetString());
        Assert.Equal("지옥 1", abyss.GetProperty("next").GetProperty("tier").GetString());
        Assert.Equal(84, abyss.GetProperty("next").GetProperty("mdefShort").GetInt64());
        Assert.False(cut.GetProperty("stale").GetBoolean());
        var succubus = cut.GetProperty("contents").EnumerateArray().Single(x => x.GetProperty("id").GetString() == "white_succubus");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, succubus.GetProperty("next").ValueKind);   // 최고 난이도
        var cavrak = cut.GetProperty("contents").EnumerateArray().Single(x => x.GetProperty("id").GetString() == "cavrak");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cavrak.GetProperty("entryShort").ValueKind);

        var ov = await TestHost.Data(await c.GetAsync("/api/overview"));
        Assert.Equal(4, ov.GetProperty("cutoffs").GetProperty("contents").GetArrayLength());
    }

    [Fact]
    public async Task ChatterLine_ReturnsRawWithoutEmoji_AndSourceIsLogged()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var line = await TestHost.Data(await c.PostAsJsonAsync("/api/chatter/line", new { persona = "Scrooge" }));
        var raw = line.GetProperty("text").GetString()!;
        var final = line.GetProperty("final").GetString()!;
        Assert.True(ChatText.Count(raw) <= ChatText.MaxLength - 2);
        Assert.NotEqual(raw, final);                  // 최종 문장에는 이모지가 붙어 있다
        Assert.StartsWith(raw.TrimEnd(), final);

        var sent = await TestHost.Data(await c.PostAsJsonAsync("/api/chat/game", new { text = raw, autoEmote = true, source = "아무말 · 구두쇠 영감" }));
        Assert.Equal("아무말 · 구두쇠 영감", sent.GetProperty("source").GetString());
        Assert.Equal(final, sent.GetProperty("message").GetString());   // 이모지는 한 번만

        var spoof = await TestHost.Data(await c.PostAsJsonAsync("/api/chat/game", new { text = "다른 말", source = "관리자" }));
        Assert.Equal("직접", spoof.GetProperty("source").GetString());
    }

    [Fact]
    public async Task ChatterPersona_IsSharedServerSetting()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal("Villainess", (await TestHost.Data(await c.GetAsync("/api/settings"))).GetProperty("chatterPersona").GetString());
        Assert.Equal("IdolDancer", (await TestHost.Data(await c.PutAsJsonAsync("/api/settings", new { chatterPersona = "IdolDancer" }))).GetProperty("chatterPersona").GetString());
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/settings", new { chatterPersona = "Custom" })).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/settings", new { chatterPersona = "관리자" })).StatusCode);
    }

    [Fact]
    public async Task Meta_Version_IsBuildTimestamp()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var v = (await TestHost.Data(await c.GetAsync("/api/meta"))).GetProperty("version").GetString();
        Assert.Matches(@"^1\.0\.\d{4}\.\d{4}$", v);
    }

    [Fact]
    public async Task ConcurrentQueries_ShareOneCliCall()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var before = host.CliCalls().Count(x => x == "get_currencies");
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => c.GetAsync("/api/currencies")));
        Assert.Equal(1, host.CliCalls().Count(x => x == "get_currencies") - before);   // NFR-07
    }

    [Fact]
    public async Task Overview_CombinesSections_IncludingHomeworkProgress()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var d = await TestHost.Data(await c.GetAsync("/api/overview"));
        Assert.Equal(JsonValueKind.Object, d.GetProperty("header").ValueKind);
        Assert.True(d.GetProperty("homework").GetProperty("daily").GetProperty("total").GetInt32() > 0);
        Assert.Equal(6, d.GetProperty("nearby").GetProperty("count").GetInt32());
        Assert.Equal("파티원", d.GetProperty("nearby").GetProperty("players")[0].GetProperty("relationLabel").GetString());
    }

    [Fact]
    public async Task Gather_Returns202_BroadcastsResult_RejectsSecondJob_AndStopIsNotBlocked()
    {
        using var host = new TestHost();
        Environment.SetEnvironmentVariable("FAKECLI_DELAY_EXECUTE_GATHERING", "4000");
        try
        {
            var c = await host.LocalAsync();
            var sse = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
            using var reader = new StreamReader(await sse.Content.ReadAsStreamAsync());

            var r = await c.PostAsJsonAsync("/api/actions/gather", new { displayName = "사과", count = 12 });
            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            var second = await c.PostAsJsonAsync("/api/actions/gather", new { displayName = "철광석" });
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

            await WaitFor(() => host.CliCalls().Any(x => x.StartsWith("execute_gathering")));
            var sw = Stopwatch.StartNew();
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/actions/stop", null)).StatusCode);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"정지가 늦었다: {sw.Elapsed}");

            var gatherEvent = await ReadEvent(reader, "gather", TimeSpan.FromSeconds(8));
            Assert.Equal("running", gatherEvent.GetProperty("state").GetString());
            gatherEvent = await ReadEvent(reader, "gather", TimeSpan.FromSeconds(8));
            Assert.Equal("stopped", gatherEvent.GetProperty("state").GetString());
            Assert.Contains(host.CliCalls(), x => x.StartsWith("execute_gathering") && x.Contains("\"count\":12"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_DELAY_EXECUTE_GATHERING", null);
        }
    }

    [Theory]
    [InlineData("{\"result\":1,\"gained\":\"x\"}")]
    [InlineData("{\"result\":{\"a\":1}}")]
    [InlineData("[]")]
    public void ParseGather_OddShapes_DoNotThrow(string stdout)
    {
        var (result, gained) = MobiMate.Web.Services.GameActions.ParseGather(stdout);
        Assert.Equal("completed", result);
        Assert.Equal(0, gained);
    }

    [Fact]
    public async Task QueryCache_InvalidateDuringFlight_DoesNotCacheStaleResult()
    {
        var cache = new MobiMate.Web.Infrastructure.QueryCache();
        var gate = new TaskCompletionSource<CliResult>();
        var calls = 0;
        Task<CliResult> Factory() { Interlocked.Increment(ref calls); return calls == 1 ? gate.Task : Task.FromResult(new CliResult(true, "new", null, TimeSpan.Zero, TimeSpan.Zero)); }

        var first = cache.GetAsync("k", Factory, TimeSpan.FromSeconds(30), CancellationToken.None);
        await Task.Delay(50);
        cache.Invalidate("k");                                   // 조작(수거 등)이 끝남
        gate.SetResult(new CliResult(true, "old", null, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Equal("old", (await first).Stdout);               // 이미 기다리던 호출자는 받는다
        var next = await cache.GetAsync("k", Factory, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Equal("new", next.Stdout);                        // 새 요청은 옛 결과를 캐시에서 받지 않는다
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Gather_UnknownItem_Returns400()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/actions/gather", new { displayName = "사과 파이" })).StatusCode);
    }

    [Fact]
    public async Task Collect_CompletesAlteringHomework()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.GetAsync("/api/header");
        var d = await TestHost.Data(await c.PostAsJsonAsync("/api/actions/collect", new { }));
        Assert.Equal("질긴 가죽", d.GetProperty("collected").GetString());
        var hw = await TestHost.Data(await c.GetAsync("/api/homework?category=life"));
        Assert.Equal("autoDone", hw.GetProperty("cards")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Chat_SendsWithEmoteAndBehaviour_LogsIt_AndRejectsDuplicate()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var entry = await TestHost.Data(await c.PostAsJsonAsync("/api/chat/game", new { text = "안녕하세요 여러분" }));
        Assert.Equal("안녕하세요 여러분 😊", entry.GetProperty("message").GetString());
        Assert.Equal("/손인사1", entry.GetProperty("behaviour").GetString());

        await WaitFor(() => host.CliCalls().Count(x => x.StartsWith("write_chat")) >= 2);
        Assert.Contains("write_chat 안녕하세요 여러분 😊", host.CliCalls());
        Assert.Contains("write_chat /손인사1", host.CliCalls());

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/chat/game", new { text = "안녕하세요 여러분" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/chat/game", new { text = "   " })).StatusCode);
        Assert.Equal(1, (await TestHost.Data(await c.GetAsync("/api/chat/game/log"))).GetArrayLength());

        var pv = await TestHost.Data(await c.PostAsJsonAsync("/api/chat/game/preview", new { text = new string('가', 60) }));
        Assert.Equal(50, pv.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Homework_ListSetAndReset()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var board = await TestHost.Data(await c.GetAsync("/api/homework"));
        Assert.Equal(31, board.GetProperty("cards").GetArrayLength());
        Assert.Equal("pending", Card(board, "raid_cavrak").GetProperty("status").GetString());   // 계정 도전과제로 오탐 안 됨

        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/homework/raid_cavrak", new { completed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/homework/raid_cavrak", new { completed = true })).StatusCode);   // 멱등
        board = await TestHost.Data(await c.GetAsync("/api/homework"));
        Assert.Equal("manualDone", Card(board, "raid_cavrak").GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/homework/reset", new { scope = "all" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/homework/reset", new { scope = "character" })).StatusCode);
        board = await TestHost.Data(await c.GetAsync("/api/homework"));
        Assert.Equal("pending", Card(board, "raid_cavrak").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/homework/nope", new { completed = true })).StatusCode);
    }

    [Fact]
    public async Task AiAsk_StopCommandActs_QuestionAboutStopGoesToEngine()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();

        var stop = await Ndjson(await c.PostAsJsonAsync("/api/ai/ask", new { text = "멈춰!" }));
        Assert.Equal("action", stop[0].GetProperty("type").GetString());
        await WaitFor(() => host.CliCalls().Contains("stop_action"));

        var before = host.CliCalls().Count(x => x == "stop_action");
        var q = await Ndjson(await c.PostAsJsonAsync("/api/ai/ask", new { text = "정지 기능 알려줘" }));
        Assert.Contains(q, l => l.GetProperty("type").GetString() == "token");
        Assert.Equal("done", q[^1].GetProperty("type").GetString());
        Assert.Equal(before, host.CliCalls().Count(x => x == "stop_action"));

        var gather = await Ndjson(await c.PostAsJsonAsync("/api/ai/ask", new { text = "사과 20개 캐줘" }));
        Assert.Equal("intent", gather[0].GetProperty("type").GetString());
        Assert.Equal(20, gather[0].GetProperty("count").GetInt32());
        Assert.DoesNotContain(host.CliCalls(), x => x.StartsWith("execute_gathering"));   // 확인 카드만, 실행 안 함
    }

    [Fact]
    public async Task Personas_CrudAndGenerateDraft()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var p = await TestHost.Data(await c.PostAsJsonAsync("/api/personas", new { name = "츤데레 메이드", emoji = "🎀", prompt = "흥! 이라고 말한다" }));
        var id = p.GetProperty("id").GetString();
        Assert.Equal(1, (await TestHost.Data(await c.GetAsync("/api/personas"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/personas/{id}", new { name = "메이드", prompt = "공손하게" })).StatusCode);

        var line = await TestHost.Data(await c.PostAsJsonAsync("/api/chatter/line", new { persona = "custom", customId = id }));
        Assert.True(line.GetProperty("count").GetInt32() <= 50);

        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync($"/api/personas/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/personas", new { name = "", prompt = "x" })).StatusCode);

        await c.PutAsJsonAsync("/api/ai/engines/current", new { id = "builtin:guide" });
        var draft = await TestHost.Data(await c.PostAsJsonAsync("/api/personas/generate", new { request = "공주기사" }));
        Assert.Equal("🛡️", draft.GetProperty("emoji").GetString());
        Assert.Equal(0, (await TestHost.Data(await c.GetAsync("/api/personas"))).GetArrayLength());   // 초안은 저장하지 않음
    }

    [Fact]
    public async Task Engines_UnknownIdIs404()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/ai/engines/current", new { id = "nope" })).StatusCode);
        var d = await TestHost.Data(await c.GetAsync("/api/ai/engines"));
        Assert.Equal("builtin:guide", d.GetProperty("current").GetString());
    }

    [Fact]
    public async Task CliFailure_MapsToErrorCode()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Environment.SetEnvironmentVariable("FAKECLI_FAIL_GET_MY_INFO", "3");
        try
        {
            var r = await c.GetAsync("/api/character");
            Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
            Assert.Equal("CLI_FAILED", await TestHost.ErrorCode(r));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_FAIL_GET_MY_INFO", null);
        }
    }

    private static JsonElement Card(JsonElement board, string id) =>
        board.GetProperty("cards").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);

    private static async Task<List<JsonElement>> Ndjson(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.StartsWith("application/x-ndjson", r.Content.Headers.ContentType?.ToString());
        var text = await r.Content.ReadAsStringAsync();
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
    }

    private static async Task<JsonElement> ReadEvent(StreamReader reader, string name, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        string? evt = null;
        while (!cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line == null) break;
            if (line.StartsWith("event: ")) evt = line[7..];
            else if (line.StartsWith("data: ") && evt == name) return JsonDocument.Parse(line[6..]).RootElement.Clone();
        }
        throw new TimeoutException($"SSE 이벤트 '{name}'를 받지 못했습니다.");
    }

    private static async Task WaitFor(Func<bool> cond, int ms = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds > ms) throw new TimeoutException();
            await Task.Delay(25);
        }
    }
}
