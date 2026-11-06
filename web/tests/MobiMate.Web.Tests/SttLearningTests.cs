using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MobiMate.Web.Voice;

namespace MobiMate.Web.Tests;

/// <summary>누적 학습을 실제 흐름에 연결한 시험: 고쳐 보내기 → 다음 인식부터 자동 치환 (FR-VC-08)</summary>
public class SttLearningTests
{
    private const string Raw = "일러한번만더구합니다";          // 모델이 알아들은 글자 (P0 실측)
    private const string Fixed = "힐러 한 분만 더 구합니다";      // 사용자가 고쳐 보낸 글

    private static HttpRequestMessage Req(HttpMethod m, string url, object? body = null)
    {
        var r = new HttpRequestMessage(m, url) { Content = new StringContent(JsonSerializer.Serialize(body ?? new { }), Encoding.UTF8, "application/json") };
        r.Headers.Add("X-MobiMate-Req", "1");
        return r;
    }

    private static async Task<bool> Eventually(Func<bool> cond, int ms = 4000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { if (cond()) return true; await Task.Delay(15); }
        return cond();
    }

    /// <summary>이 PC 화면이 보내는 것과 같은 채팅 요청 (학습은 헤더가 있는 요청만 받는다)</summary>
    private static Task<HttpResponseMessage> PostChat(HttpClient c, object body, bool header = true, string? host = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/chat/game") { Content = JsonContent.Create(body) };
        if (header) r.Headers.Add("X-MobiMate-Req", "1");
        if (host != null) r.Headers.Host = host;
        return c.SendAsync(r);
    }

    private static SttService Stt(TestHost h) => h.Services.GetRequiredService<SttService>();

    /// <summary>음성으로 받아 고쳐 보냈다고 서버에 알린다 (중복 방지 1초를 넘겨서)</summary>
    private static async Task SendCorrected(HttpClient c, string text = Fixed, string original = Raw, string source = "음성")
    {
        var r = await PostChat(c, new { text, autoEmote = false, source, sttOriginal = original });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await Task.Delay(1100);
    }

    /// <summary>가짜 엔진이 <see cref="Raw"/>를 알아들은 것으로 한 번 듣고, 확정된 글을 돌려준다</summary>
    private static async Task<(string Text, string Original, string[] Applied)> ListenOnce(TestHost host, HttpClient c, string utterance = Raw)
    {
        host.Engines.Utterances = new[] { utterance };
        var previous = Stt(host).Last()?.SessionId;   // Last는 60초 보관된다: 이전 세션의 결과를 새 결과로 착각하지 않는다
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"))).StatusCode);
        host.Audio.Current!.PushVoice(4);
        Assert.True(await Eventually(() => Stt(host).Last() is { } l && l.SessionId != previous));
        var last = Stt(host).Last()!;
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Idle));
        return (last.Text, last.Original, last.Applied.ToArray());
    }

    [Fact]
    public async Task CorrectingTwice_TeachesTheServer_AndTheNextRecognitionIsFixedAutomatically()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();

        var before = await ListenOnce(host, c);
        Assert.Equal(Raw, before.Text);                       // 아직 모른다

        await SendCorrected(c);
        var once = await ListenOnce(host, c);
        Assert.Equal(Raw, once.Text);                         // 1회: 아직 적용하지 않는다

        await SendCorrected(c);
        var learned = await ListenOnce(host, c);
        Assert.Equal("힐러한분만더구합니다", learned.Text);          // 2회 확인: 자동으로 고쳐 준다
        Assert.Equal(Raw, learned.Original);                  // 고치기 전 글자는 따로 남는다 (다음 학습 비교용)
        Assert.Contains("일러→힐러", learned.Applied);
    }

    [Fact]
    public async Task TheAppliedSubstitutions_AreShownToTheScreen()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await SendCorrected(c);
        await SendCorrected(c);

        var res = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await res.Content.ReadAsStreamAsync());
        host.Engines.Utterances = new[] { Raw };
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);
        var cts = new CancellationTokenSource(4000);
        string? ev = null;
        JsonElement? fin = null;
        while (fin == null && await reader.ReadLineAsync(cts.Token) is { } line)
        {
            if (line.StartsWith("event:")) ev = line[6..].Trim();
            else if (line.StartsWith("data:") && ev == "stt.final") fin = JsonDocument.Parse(line[5..].Trim()).RootElement.Clone();
        }
        Assert.NotNull(fin);
        Assert.Equal("힐러한분만더구합니다", fin!.Value.GetProperty("text").GetString());
        Assert.Contains("일러→힐러", fin.Value.GetProperty("applied").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task OnlyHumanWrittenChat_IsLearned_NotGeneratedPersonaLines()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await PostChat(c, new { text = "생성된 대사입니다 호호호", autoEmote = false, source = "아무말 · 악덕영애" });
        await PostChat(c, new { text = "직접 쓴 글입니다", autoEmote = false });

        var words = (await TestHost.Data(await c.GetAsync("/api/stt/vocab"))).GetProperty("words").EnumerateArray().Select(w => w.GetProperty("word").GetString()).ToList();
        Assert.Contains("직접", words);
        Assert.Contains("글입니다", words);
        Assert.DoesNotContain("생성된", words);   // 한마디(아무말) 대사는 사용자의 말이 아니다
    }

    [Fact]
    public async Task ARecognitionOriginal_OnlyCountsForVoiceDrafts()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        // 직접 입력인데 sttOriginal을 실어 보내도(조작) 쌍은 배우지 않는다
        for (var i = 0; i < 2; i++) await SendCorrected(c, source: "직접");
        Assert.Empty((await TestHost.Data(await c.GetAsync("/api/stt/vocab"))).GetProperty("pairs").EnumerateArray());
    }

    [Fact]
    public async Task TheOriginalRecognition_IsNeverWrittenToTheChatLog()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await SendCorrected(c);
        var log = await (await c.GetAsync("/api/chat/game/log")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(Raw, log);
        Assert.DoesNotContain("sttOriginal", log, StringComparison.OrdinalIgnoreCase);
        // 서버 로그 파일에도 본문을 남기지 않는다 (NFR-15): 저장 폴더 어디에도 인식 원문이 없다.
        // (fakecli.log는 가짜 CLI가 "게임으로 나간 명령"을 적는 시험 도구의 파일이라 제외한다)
        var appFiles = Directory.EnumerateFiles(host.StorageDir, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p) != Path.GetFileName(host.FakeCliLog) && Path.GetFileName(p) != "stop.flag").ToList();
        var vocabFile = appFiles.SingleOrDefault(p => p.EndsWith("vocab.json"));
        Assert.NotNull(vocabFile);   // 검사가 헛돌지 않는다: 학습 파일은 실제로 있다
        foreach (var f in appFiles.Where(p => p != vocabFile))
        {
            string text;
            try { text = File.ReadAllText(f); } catch (IOException) { continue; }
            Assert.DoesNotContain(Raw, text);
            Assert.DoesNotContain(Fixed, text);
            Assert.DoesNotContain("sttOriginal", text, StringComparison.OrdinalIgnoreCase);
        }

        // vocab.json은 한글이 \\uXXXX로 저장되어 원문 문자열 비교로는 아무것도 잡지 못한다: JSON으로 읽어 낱말·쌍 값을 비교한다
        var root = JsonDocument.Parse(File.ReadAllText(vocabFile!)).RootElement;
        var stored = root.GetProperty("Words").EnumerateObject().Select(w => w.Name)
            .Concat(root.GetProperty("Pairs").EnumerateArray().SelectMany(p => new[] { p.GetProperty("From").GetString()!, p.GetProperty("To").GetString()! })).ToList();
        Assert.NotEmpty(stored);
        Assert.DoesNotContain(Raw, stored);
        Assert.DoesNotContain(Raw.Replace(" ", ""), stored);
        Assert.DoesNotContain(Fixed, stored);
        Assert.DoesNotContain(Fixed.Replace(" ", ""), stored);
        Assert.All(stored, s => Assert.True(s.Length <= 8 && !s.Contains(' '), $"문장 조각이 저장됨: {s}"));
    }

    [Fact]
    public async Task LearningCannotBeInjected_FromAnotherDevice_WithoutTheHeader_OrThroughAForeignHost()
    {
        // 인증이 없으므로 LAN의 다른 기기나 다른 웹페이지가 sttOriginal을 실어 보내 쌍을 주입할 수 있었다 (같은 쌍을 두 번 보내면 활성화)
        using var host = new TestHost();
        var pc = await host.LocalAsync();
        var phone = await host.LanAsync(pc);
        var body = (string tail) => new { text = Fixed + tail, autoEmote = false, source = "음성", sttOriginal = Raw };   // 이모지만 달라 중복 방지에 걸리지 않는다

        foreach (var tail in new[] { " 😊", " 🌹", " 🔥" })
        {
            Assert.Equal(HttpStatusCode.OK, (await PostChat(phone, body(tail))).StatusCode);                              // 폰: 보내기는 된다
            Assert.Equal(HttpStatusCode.OK, (await PostChat(pc, body(tail + "a"), header: false)).StatusCode);           // PC지만 요청 형식이 다르다(다른 웹페이지)
            Assert.Equal(HttpStatusCode.OK, (await PostChat(pc, body(tail + "b"), host: "evil.example")).StatusCode);     // 도메인을 돌려 붙인 요청
        }
        var v = await TestHost.Data(await pc.GetAsync("/api/stt/vocab"));
        Assert.Empty(v.GetProperty("pairs").EnumerateArray());   // 아무것도 배우지 않았다
        Assert.Empty(v.GetProperty("words").EnumerateArray());

        // 대조: 이 PC 화면의 정상 요청은 배운다 (위 시험이 헛돌지 않는다는 증거)
        await SendCorrected(pc);
        await SendCorrected(pc);
        Assert.NotEmpty((await TestHost.Data(await pc.GetAsync("/api/stt/vocab"))).GetProperty("pairs").EnumerateArray());
    }

    [Fact]
    public async Task AutoSentVoiceSentences_TeachWordsToo()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { autoSend = true }));
        host.Engines.Utterances = new[] { "힐러 구합니다" };
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);
        Assert.True(await Eventually(() => host.Services.GetRequiredService<VoiceVocabulary>().View().Words.Any(w => w.Word == "구합니다")));
    }

    // ── 학습 내용 API (PC 전용) ──

    [Fact]
    public async Task TheVocabulary_IsViewableAndChangeableOnlyFromThePc()
    {
        using var host = new TestHost();
        var pc = await host.LocalAsync();
        var phone = await host.LanAsync(pc);
        await SendCorrected(pc);

        Assert.Equal(HttpStatusCode.Forbidden, (await phone.GetAsync("/api/stt/vocab")).StatusCode);   // 낱말에 이름이 섞일 수 있다
        foreach (var (m, url) in new[] { (HttpMethod.Put, "/api/stt/vocab"), (HttpMethod.Post, "/api/stt/vocab/pair"), (HttpMethod.Post, "/api/stt/vocab/word"), (HttpMethod.Post, "/api/stt/vocab/clear") })
        {
            var r = await phone.SendAsync(Req(m, url, new { learningOn = false, from = "일러", to = "힐러", action = "delete", word = "힐러" }));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        Assert.NotEmpty((await TestHost.Data(await pc.GetAsync("/api/stt/vocab"))).GetProperty("words").EnumerateArray());   // 지워지지 않았다
    }

    [Fact]
    public async Task ViewShowsWordsAndPairsWithTheirState()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await SendCorrected(c);
        await SendCorrected(c);
        var v = await TestHost.Data(await c.GetAsync("/api/stt/vocab"));
        Assert.True(v.GetProperty("learningOn").GetBoolean());
        var pair = v.GetProperty("pairs").EnumerateArray().Single(p => p.GetProperty("from").GetString() == "일러");
        Assert.Equal("힐러", pair.GetProperty("to").GetString());
        Assert.Equal(2, pair.GetProperty("count").GetInt32());
        Assert.True(pair.GetProperty("active").GetBoolean());
        Assert.False(pair.GetProperty("off").GetBoolean());
        var word = v.GetProperty("words").EnumerateArray().Single(w => w.GetProperty("word").GetString() == "힐러");
        Assert.Equal(2, word.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task SwitchingAPairOff_StopsTheSubstitution_AndOnRestoresIt_DeleteRemovesIt()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await SendCorrected(c);
        await SendCorrected(c);

        // 한 번 고친 문장에서 쌍이 둘 생겼다: 일러→힐러, 번만→분만
        async Task<HttpResponseMessage> Cmd(string from, string to, string action) => await c.SendAsync(Req(HttpMethod.Post, "/api/stt/vocab/pair", new { from, to, action }));

        Assert.Equal(HttpStatusCode.OK, (await Cmd("일러", "힐러", "off")).StatusCode);
        Assert.Equal("일러한분만더구합니다", (await ListenOnce(host, c)).Text);        // 하나만 껐으니 나머지는 계속 적용된다

        Assert.Equal(HttpStatusCode.OK, (await Cmd("번만", "분만", "off")).StatusCode);
        Assert.Equal(Raw, (await ListenOnce(host, c)).Text);                          // 둘 다 끄면 그대로

        Assert.Equal(HttpStatusCode.OK, (await Cmd("일러", "힐러", "on")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Cmd("번만", "분만", "on")).StatusCode);
        Assert.Equal("힐러한분만더구합니다", (await ListenOnce(host, c)).Text);         // 다시 켜면 복구

        Assert.Equal(HttpStatusCode.OK, (await Cmd("일러", "힐러", "delete")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Cmd("번만", "분만", "delete")).StatusCode);
        Assert.Equal(Raw, (await ListenOnce(host, c)).Text);
        Assert.Empty((await TestHost.Data(await c.GetAsync("/api/stt/vocab"))).GetProperty("pairs").EnumerateArray());
    }

    [Fact]
    public async Task PairAndWordCommands_ValidateTheirInput()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/vocab/pair", new { from = "가나", to = "다라", action = "bogus" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/vocab/pair", new { from = "", to = "다라", action = "off" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/vocab/pair", new { from = "없는", to = "쌍", action = "off" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/vocab/word", new { word = "" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/vocab/word", new { word = "없는낱말" }))).StatusCode);
    }

    [Fact]
    public async Task TurningLearningOff_StopsLearningAndSubstituting_AndClearWipesIt()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await SendCorrected(c);
        await SendCorrected(c);
        Assert.Equal("힐러한분만더구합니다", (await ListenOnce(host, c)).Text);

        var off = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Put, "/api/stt/vocab", new { learningOn = false })));
        Assert.False(off.GetProperty("learningOn").GetBoolean());
        Assert.Equal(Raw, (await ListenOnce(host, c)).Text);                  // 꺼지면 이미 배운 치환도 하지 않는다
        await PostChat(c, new { text = "꺼진 뒤에 쓴 말입니다", autoEmote = false });
        Assert.DoesNotContain((await TestHost.Data(await c.GetAsync("/api/stt/vocab"))).GetProperty("words").EnumerateArray(), w => w.GetProperty("word").GetString() == "꺼진");

        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/vocab", new { learningOn = true }));
        var cleared = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Post, "/api/stt/vocab/clear")));
        Assert.Empty(cleared.GetProperty("words").EnumerateArray());
        Assert.Empty(cleared.GetProperty("pairs").EnumerateArray());
        Assert.True(cleared.GetProperty("learningOn").GetBoolean());
        Assert.Equal(Raw, (await ListenOnce(host, c)).Text);
    }

    [Fact]
    public async Task RemovingAWord_RemovesItFromTheList()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await PostChat(c, new { text = "파티장님 고맙습니다", autoEmote = false });
        Assert.Contains((await TestHost.Data(await c.GetAsync("/api/stt/vocab"))).GetProperty("words").EnumerateArray(), w => w.GetProperty("word").GetString() == "파티장님");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/vocab/word", new { word = "파티장님" }))).StatusCode);
        Assert.DoesNotContain((await TestHost.Data(await c.GetAsync("/api/stt/vocab"))).GetProperty("words").EnumerateArray(), w => w.GetProperty("word").GetString() == "파티장님");
    }

    // ── 핫워드(실험, 기본 끔) ──

    [Fact]
    public async Task Hotwords_AreOffByDefault_AndChangingThemNeedsARestart()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.False((await TestHost.Data(await c.GetAsync("/api/stt/settings"))).GetProperty("hotwords").GetBoolean());
        var on = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { hotwords = true })));
        Assert.True(on.GetProperty("restartRequired").GetBoolean());
        var again = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { hotwords = true })));
        Assert.False(again.GetProperty("restartRequired").GetBoolean());
    }

    [Fact]
    public void HotwordLines_AreSyllableSpaced_AndSkipWordsTheModelCannotRead()
    {
        var tokens = new HashSet<string> { "힐", "러", "장", "판", "구", "합", "니", "다" };
        var lines = HotwordFile.BuildLines(new[] { "힐러", "장판", "구합니다", "보스", "" }, tokens);
        Assert.Equal(new[] { "힐 러", "장 판", "구 합 니 다" }, lines);   // "보스"는 글자표에 없는 음절이라 뺀다
    }

    [Fact]
    public void HotwordLines_AreCapped()
    {
        var tokens = Enumerable.Range(0, 200).Select(i => ((char)('가' + i)).ToString()).ToHashSet();
        var words = Enumerable.Range(0, 100).Select(i => $"{(char)('가' + i)}{(char)('가' + i + 1)}");
        Assert.Equal(HotwordFile.MaxWords, HotwordFile.BuildLines(words, tokens).Count);
        Assert.Equal(5, HotwordFile.BuildLines(words, tokens, 5).Count);
    }

    [Fact]
    public void ThePostProcessorInTheApp_IsTheLearningOne()
    {
        using var host = new TestHost();
        Assert.IsType<LearnedPostProcessor>(host.Services.GetRequiredService<IVoicePostProcessor>());
    }
}
