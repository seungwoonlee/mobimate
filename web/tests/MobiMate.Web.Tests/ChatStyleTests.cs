using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MobiMate.Web.Services;

namespace MobiMate.Web.Tests;

/// <summary>맥락 이모티콘 · 페르소나 말투 (승운 2026-11-07): 보내기 전에 AI가 이모지를 고르고 말투를 바꾼다. 뜻이 바뀌거나 실패하면 원문을 보낸다.</summary>
public class ChatStyleTests
{
    // ── 말투 검증 ──

    [Theory]
    [InlineData("힐러 한 분만 더 구해요", "오호호, 힐러 한 분만 더 구해야겠사와요.")]               // 악덕영애의 평서 어미
    [InlineData("도와주셔서 감사합니다", "오호호, 도와주셔서 감사하답니다 사와요")]               // 감사는 감사로
    [InlineData("축하드려요 드디어 클리어했네요", "오호호! 드디어 클리어하셨다니, 축하드려요.")]
    [InlineData("포션이 부족해서 마을에 다녀올게요", "에헴, 포션이 모자라서 마을에 다녀오겠구먼.")]    // 구두쇠: 어휘를 바꿔도 핵심이 남으면 허용
    [InlineData("장판 밟지 마세요", "마, 장판 밟지 마이소!")]
    public void Tone_Accepts_ThePersonaFlavourThatKeepsTheMeaning(string input, string output) =>
        Assert.NotNull(StyleGuard.AcceptTone(input, output, 48));

    [Theory]
    [InlineData("도와주셔서 감사합니다", "오호호, 별것 아닌 일에 감사하다니, 참으로 유치한 것 같사와요.")]     // 비꼼(모욕어)
    [InlineData("도와주셔서 감사합니다", "오호호, 도와주셨다니 감사하다고 하시나요?")]                       // 감사를 질문으로
    [InlineData("도와주셔서 감사합니다", "오호호, 도와주셨군요.")]                                          // 감사가 사라짐
    [InlineData("죄송합니다 잠시 자리 비웠어요", "오호호, 잠시 자리를 비우셨군요.")]                         // 사과가 사라짐
    [InlineData("장판 밟지 마세요", "오호호, 저기 바닥을 밟지 말아 주시겠어요?")]                          // 게임 용어가 사라짐
    [InlineData("3명만 더 구해요", "오호호, 두 명만 더 구해요")]                                           // 숫자가 바뀜
    [InlineData("지금 레이드 들어갑니다", "오호호 😊 지금 레이드 들어갑니다")]                             // 이모지는 따로 붙인다
    [InlineData("지금 레이드 들어갑니다", "**지금 레이드 들어갑니다**")]                                   // 마크다운
    [InlineData("포션이 부족해서 마을에 다녀올게요", "오호호, 포션이 부족하다니, 마을에 다녀오시나요?")]         // "다녀올게요"가 상대에게 묻는 말로 바뀜
    [InlineData("지금 레이드 들어갑니다", "오호호, 이제 레이드에 들어가시나요?")]                       // 알리는 말이 질문으로
    [InlineData("지금 레이드 들어갑니다", "")]
    [InlineData("지금 레이드 들어갑니다", "오늘 날씨가 정말 좋네요 같이 놀러 가요")]                        // 완전히 다른 문장
    [InlineData("힐러 한 분 구해요", "오호호, 힐러 한 분 구하는 것이군요 그러니 저에게도 기회가 있다면 기꺼이 합류하고 싶지만 지금은 바빠서 어렵겠사와요")]   // 지나치게 길어짐
    public void Tone_Rejects_FlippedMeaningInventionAndNoise(string input, string output) =>
        Assert.Null(StyleGuard.AcceptTone(input, output, 48));

    [Fact]
    public void Tone_LabelsAndExtraLines_AreCleanedUp() =>
        Assert.Equal("오호호, 힐러 한 분만 더 구해야겠사와요.", StyleGuard.AcceptTone("힐러 한 분만 더 구해요", "문장: 오호호, 힐러 한 분만 더 구해야겠사와요.\n(설명) 영애 말투입니다", 48));

    [Fact]
    public void Tone_RespectsTheLengthLeftForTheEmoji() =>
        Assert.Null(StyleGuard.AcceptTone("포션이 부족해서 마을에 다녀올게요", "에헴, 포션이 모자라서 마을에 다녀오겠구먼 정말로 그러하구먼 에헴 에헴", 20));

    [Theory]
    [InlineData("😍", "😍", false)]
    [InlineData("이모지: 🥳", "🥳", false)]
    [InlineData("없음", null, true)]
    [InlineData("이모지: 없음", null, true)]
    [InlineData("😡", "😡", false)]
    [InlineData("🤔", "🤔", false)]
    [InlineData("👍", null, false)]          // 게임이 표시하지 않는 이모지
    [InlineData("😤", null, false)]          // 허용 목록 밖은 모른다(키워드 규칙으로)
    [InlineData("", null, false)]
    public void Emoji_IsOneOfTheAllowedEightOrNone(string output, string? emoji, bool none) => Assert.Equal((emoji, none), StyleGuard.AcceptEmoji(output));

    [Fact]
    public void ChatPlan_WithAChosenEmoji_PairsTheSocialAction_AndNoneMeansPlainText()
    {
        Assert.Equal("/사과1", ChatPlanService.BehaviourForEmoji("😓"));
        Assert.Equal("/하트", ChatPlanService.BehaviourForEmoji("😍"));
        Assert.Null(ChatPlanService.BehaviourForEmoji("🤣x"));
        var p = ChatPlanService.BuildChatPlanWithEmoji("도와주셔서 감사합니다", "😍");
        Assert.Equal("도와주셔서 감사합니다 😍", p.FinalMessage);
        Assert.Equal("/하트", p.BehaviourCommand);
        var none = ChatPlanService.BuildChatPlanWithEmoji("힐러 구합니다", null);
        Assert.Equal("힐러 구합니다", none.FinalMessage);
        Assert.Null(none.BehaviourCommand);
    }

    // ── 보내기 흐름 ──

    private static async Task<JsonElement> Send(HttpClient c, string text, bool autoEmote)
    {
        var res = await c.PostAsJsonAsync("/api/chat/game", new { text, autoEmote });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await TestHost.Data(res);
    }

    private static Task Set(HttpClient c, object body) => c.PutAsJsonAsync("/api/settings", body);

    [Fact]
    public async Task ByDefault_NothingIsAskedOfTheAi()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var s = await TestHost.Data(await c.GetAsync("/api/settings"));
        Assert.False(s.GetProperty("smartEmote").GetBoolean());
        Assert.False(s.GetProperty("personaTone").GetBoolean());
        var e = await Send(c, "안녕하세요 여러분", autoEmote: true);
        Assert.Equal("안녕하세요 여러분 😊", e.GetProperty("message").GetString());   // 기존 키워드 규칙 그대로
        Assert.Empty(host.Stylist.Calls);
    }

    [Fact]
    public async Task SmartEmote_UsesTheEmojiTheAiChose_AndItsSocialAction()
    {
        using var host = new TestHost();
        host.Stylist.Reply = (_, _) => new ChatStyleResult(null, "😍", false);
        var c = await host.LocalAsync();
        await Set(c, new { smartEmote = true });
        var e = await Send(c, "도와주셔서 감사합니다", autoEmote: true);
        Assert.Equal("도와주셔서 감사합니다 😍", e.GetProperty("message").GetString());
        Assert.Equal("/하트", e.GetProperty("behaviour").GetString());
        Assert.True(host.Stylist.Calls.Single().Req.Emoji);
        Assert.False(host.Stylist.Calls.Single().Req.Tone);
    }

    [Fact]
    public async Task SmartEmote_NoneMeansNoEmojiAtAll_NotEvenTheDefaultSmile()
    {
        using var host = new TestHost();
        host.Stylist.Reply = (_, _) => new ChatStyleResult(null, null, true);
        var c = await host.LocalAsync();
        await Set(c, new { smartEmote = true });
        var e = await Send(c, "힐러 한 분만 더 구해요", autoEmote: true);
        Assert.Equal("힐러 한 분만 더 구해요", e.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, e.GetProperty("behaviour").ValueKind);
    }

    [Fact]
    public async Task SmartEmote_WhenTheAiCannotDecide_FallsBackToTheKeywordRule()
    {
        using var host = new TestHost();   // 가짜는 기본으로 아무것도 정하지 못한다
        var c = await host.LocalAsync();
        await Set(c, new { smartEmote = true });
        var e = await Send(c, "안녕하세요 여러분", autoEmote: true);
        Assert.Equal("안녕하세요 여러분 😊", e.GetProperty("message").GetString());
        Assert.Equal("/손인사1", e.GetProperty("behaviour").GetString());
    }

    [Fact]
    public async Task SmartEmote_NeedsTheAutoEmoteCheckbox_OtherwiseNothingIsAdded_AndTheAiIsNotAsked()
    {
        using var host = new TestHost();
        host.Stylist.Reply = (_, _) => new ChatStyleResult(null, "😍", false);
        var c = await host.LocalAsync();
        await Set(c, new { smartEmote = true });
        var e = await Send(c, "도와주셔서 감사합니다", autoEmote: false);
        Assert.Equal("도와주셔서 감사합니다", e.GetProperty("message").GetString());
        Assert.Empty(host.Stylist.Calls);
    }

    [Fact]
    public async Task PersonaTone_SendsTheRewrittenSentence_WithTheCurrentPersonasDescription()
    {
        using var host = new TestHost();
        host.Stylist.Reply = (t, _) => new ChatStyleResult("마, 힐러 한 분만 더 구한다 아이가!", null, false);
        var c = await host.LocalAsync();
        await Set(c, new { personaTone = true, chatterPersona = "GyeongsangAhjussi" });
        var e = await Send(c, "힐러 한 분만 더 구해요", autoEmote: false);
        Assert.Equal("마, 힐러 한 분만 더 구한다 아이가!", e.GetProperty("message").GetString());
        var call = host.Stylist.Calls.Single();
        Assert.True(call.Req.Tone);
        Assert.Contains("경상도", call.Req.PersonaDesc);
    }

    [Fact]
    public async Task PersonaTone_TheDefaultPersonaIsTheVillainess()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await Set(c, new { personaTone = true });
        await Send(c, "힐러 한 분만 더 구해요", autoEmote: false);
        Assert.Contains("영애", host.Stylist.Calls.Single().Req.PersonaDesc);
    }

    [Fact]
    public async Task PersonaTone_AFivePersonaChoice_EachReachesTheAiWithItsOwnDescription()
    {
        var expect = new Dictionary<string, string>
        {
            ["Villainess"] = "영애", ["Scrooge"] = "구두쇠", ["MorningSpirit"] = "모닝이", ["GyeongsangAhjussi"] = "경상도", ["IdolDancer"] = "아이돌",
        };
        foreach (var (name, word) in expect)
        {
            using var host = new TestHost();
            var c = await host.LocalAsync();
            await Set(c, new { personaTone = true, chatterPersona = name });
            await Send(c, "힐러 한 분만 더 구해요", autoEmote: false);
            Assert.Contains(word, host.Stylist.Calls.Single().Req.PersonaDesc);
        }
    }

    [Fact]
    public async Task PersonaTone_ACustomPersona_UsesTheUsersOwnNameAndInstructions_AndFollowsWhenItChanges()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var created = await TestHost.Data(await c.PostAsJsonAsync("/api/personas", new { name = "해적 선장", emoji = "🏴‍☠️", prompt = "거친 바다 사나이. 어미로 '~다 이놈아', '~이노라'를 쓰고 럼주를 좋아함." }));
        var id = created.GetProperty("id").GetString();
        await Set(c, new { personaTone = true, chatterPersona = "custom:" + id });
        await Send(c, "힐러 한 분만 더 구해요", autoEmote: false);
        var desc = host.Stylist.Calls.Last().Req.PersonaDesc;
        Assert.Contains("해적 선장", desc);
        Assert.Contains("럼주", desc);

        // 수정한 내용이 바로 반영된다
        await c.PutAsJsonAsync("/api/personas/" + id, new { name = "해적 선장", emoji = "🏴‍☠️", prompt = "점잖은 해군 제독. 어미로 '~하오', '~이오'를 씀." });
        await Send(c, "포션이 부족해요", autoEmote: false);
        Assert.Contains("제독", host.Stylist.Calls.Last().Req.PersonaDesc);
    }

    [Fact]
    public async Task PersonaTone_ADeletedCustomPersona_FallsBackToAPlainDescription_NotACrash()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await Set(c, new { personaTone = true, chatterPersona = "custom:없는아이디" });
        var e = await Send(c, "힐러 한 분만 더 구해요", autoEmote: false);
        Assert.Equal("힐러 한 분만 더 구해요", e.GetProperty("message").GetString());   // 문장은 그대로 나간다
        Assert.True(host.Stylist.Calls.Count <= 1);
    }

    [Fact]
    public async Task ToneAndEmoji_Together_TheEmojiGoesAfterTheStyledSentence_WithinFiftyCharacters()
    {
        using var host = new TestHost();
        host.Stylist.Reply = (_, _) => new ChatStyleResult("오호호, 힐러 한 분만 더 구해야겠사와요.", "😓", false);
        var c = await host.LocalAsync();
        await Set(c, new { personaTone = true, smartEmote = true });
        var e = await Send(c, "힐러 한 분만 더 구해요", autoEmote: true);
        Assert.Equal("오호호, 힐러 한 분만 더 구해야겠사와요. 😓", e.GetProperty("message").GetString());
        var call = host.Stylist.Calls.Single();
        Assert.True(call.Req.Tone && call.Req.Emoji);
        Assert.True(ChatText.Count(e.GetProperty("message").GetString()!) <= ChatText.MaxLength);
    }

    [Fact]
    public async Task WhenTheAiFailsOrThrows_TheOriginalSentenceIsSentAsIs()
    {
        using var host = new TestHost();
        host.Stylist.Throw = true;
        var c = await host.LocalAsync();
        await Set(c, new { personaTone = true, smartEmote = true });
        var e = await Send(c, "안녕하세요 여러분", autoEmote: true);
        Assert.Equal("안녕하세요 여러분 😊", e.GetProperty("message").GetString());   // 말투는 원문, 이모지는 키워드 규칙
    }

    [Fact]
    public async Task ASpokenSentence_AlsoGetsTheStyle_AndStillAsksTheGameOnlyOnce()
    {
        using var host = new TestHost();
        host.Stylist.Reply = (_, _) => new ChatStyleResult("마, 힐러 구한다 아이가!", "😊", false);
        host.Engines.Utterances = new[] { "힐러구합니다" };
        var c = await host.LocalAsync();
        await Set(c, new { personaTone = true, smartEmote = true, autoEmoteDefault = true });
        var put = new HttpRequestMessage(HttpMethod.Put, "/api/stt/settings") { Content = JsonContent.Create(new { autoSend = true }) };
        put.Headers.Add("X-MobiMate-Req", "1");
        await c.SendAsync(put);
        var start = new HttpRequestMessage(HttpMethod.Post, "/api/stt/start") { Content = JsonContent.Create(new { }) };
        start.Headers.Add("X-MobiMate-Req", "1");
        await c.SendAsync(start);
        host.Audio.Current!.PushVoice(4);

        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until && host.CliCalls().Count(x => x.StartsWith("write_chat")) == 0) await Task.Delay(30);
        await Task.Delay(400);
        var chats = host.CliCalls().Where(x => x.StartsWith("write_chat")).ToList();
        Assert.Single(chats);                                  // 확인창은 한 번
        Assert.Contains("마, 힐러 구한다 아이가! 😊", chats[0]);
    }

    [Fact]
    public async Task TheSettingsApi_RoundTripsBothCheckboxes()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await Set(c, new { smartEmote = true, personaTone = true });
        var s = await TestHost.Data(await c.GetAsync("/api/settings"));
        Assert.True(s.GetProperty("smartEmote").GetBoolean());
        Assert.True(s.GetProperty("personaTone").GetBoolean());
        await Set(c, new { personaTone = false });
        s = await TestHost.Data(await c.GetAsync("/api/settings"));
        Assert.True(s.GetProperty("smartEmote").GetBoolean());
        Assert.False(s.GetProperty("personaTone").GetBoolean());
    }
}

/// <summary>실제 Ollama로 악덕영애 말투를 확인한다. MOBIMATE_OLLAMA_TEST=1 일 때만 돈다.</summary>
public class ChatStyleOllamaTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task RealOllama_Villainess_MostSentencesPassTheGuard_AndNeverFlipTheMeaning()
    {
        if (Environment.GetEnvironmentVariable("MOBIMATE_OLLAMA_TEST") != "1") return;
        var dir = Path.Combine(Path.GetTempPath(), "mm-style-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new MobiMate.Web.Hosting.WebSettingsStore(dir);
            var stylist = new OllamaChatStylist(settings, Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaChatStylist>.Instance);
            var desc = ChatterLineService.DescribePersona(ChatterPersona.Villainess, null);
            var sentences = new[]
            {
                "지금 레이드 들어갑니다", "힐러 한 분만 더 구해요", "죄송합니다 잠시 자리 비웠어요", "축하드려요 드디어 클리어했네요", "포션이 부족해서 마을에 다녀올게요",
                "수고하셨습니다 다음에 또 봬요", "도와주셔서 감사합니다", "방금 보스 패턴 너무 어려웠어요", "장판 밟지 마세요", "아이템 드롭됐어요 확인해 주세요",
                "안녕하세요 처음 왔는데 잘 부탁드립니다", "아 아까워요 템이 안 나오네요",
            };
            var styled = 0;
            foreach (var s in sentences)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = await stylist.StyleAsync(s, new ChatStyleRequest(true, true, desc), CancellationToken.None);
                output.WriteLine($"{s} => {r.Text ?? "(원문)"} [{(r.EmojiNone ? "없음" : r.Emoji ?? "?")}] {sw.ElapsedMilliseconds}ms");
                if (r.Text != null) styled++;
            }
            output.WriteLine($"말투 적용 {styled}/{sentences.Length}");
            Assert.True(styled >= sentences.Length / 2, $"{styled}/{sentences.Length}");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
