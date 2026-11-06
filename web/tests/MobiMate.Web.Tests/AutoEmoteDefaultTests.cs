using System.Net;
using System.Net.Http.Json;

namespace MobiMate.Web.Tests;

/// <summary>자동 이모티콘은 기본 끔 (승운 2026-11-06): 키워드에 따라 같은 이모지를 붙이는 것은 맥락과 무관해 의미가 없다.</summary>
public class AutoEmoteDefaultTests
{
    private const string EmotionSentence = "안녕하세요 여러분";   // 켜져 있으면 😊 와 /손인사1 이 붙는 문장

    [Fact]
    public async Task ByDefault_NoEmojiAndNoSocialAction_ArePlainTextOnly()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.False((await TestHost.Data(await c.GetAsync("/api/settings"))).GetProperty("autoEmoteDefault").GetBoolean());

        var entry = await TestHost.Data(await c.PostAsJsonAsync("/api/chat/game", new { text = EmotionSentence }));
        Assert.Equal(EmotionSentence, entry.GetProperty("message").GetString());                           // 이모지 없음
        Assert.Equal(System.Text.Json.JsonValueKind.Null, entry.GetProperty("behaviour").ValueKind);        // 행동 명령 없음
        await Task.Delay(400);
        Assert.Equal(new[] { "write_chat " + EmotionSentence }, host.CliCalls().Where(x => x.StartsWith("write_chat")).ToArray());   // 게임 확인창도 한 번뿐
    }

    [Fact]
    public async Task ThePreview_ShowsNoEmojiByDefault_AndTheFullTextCount()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var pv = await TestHost.Data(await c.PostAsJsonAsync("/api/chat/game/preview", new { text = EmotionSentence }));
        Assert.Equal(EmotionSentence, pv.GetProperty("final").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, pv.GetProperty("emoji").ValueKind);
        Assert.Equal(EmotionSentence.Length, pv.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task TheUserCanStillTurnItOn_AndARequestCanAskForItExplicitly()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var asked = await TestHost.Data(await c.PostAsJsonAsync("/api/chat/game", new { text = EmotionSentence, autoEmote = true }));
        Assert.Equal(EmotionSentence + " 😊", asked.GetProperty("message").GetString());   // 요청이 직접 켜면 된다 (기존 기능은 그대로)

        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/settings", new { autoEmoteDefault = true })).StatusCode);
        await Task.Delay(1100);   // 같은 문장 중복 방지 창을 넘긴다
        var onByDefault = await TestHost.Data(await c.PostAsJsonAsync("/api/chat/game", new { text = EmotionSentence }));
        Assert.Equal(EmotionSentence + " 😊", onByDefault.GetProperty("message").GetString());   // 설정으로 켜면 기본이 켜진다
    }

    [Fact]
    public async Task AVoiceSentence_AutoSent_HasNoEmojiByDefault_TooLongSentencesStillFitWithoutTheEmojiReserve()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { new string('가', 50) };   // 50자: 이모지를 붙이면 한도를 넘지만 끄면 딱 맞는다
        host.Engines.SamplesPerUtterance = 1600;
        var c = await host.LocalAsync();
        var put = new HttpRequestMessage(HttpMethod.Put, "/api/stt/settings") { Content = JsonContent.Create(new { autoSend = true }) };
        put.Headers.Add("X-MobiMate-Req", "1");
        await c.SendAsync(put);
        var start = new HttpRequestMessage(HttpMethod.Post, "/api/stt/start") { Content = JsonContent.Create(new { }) };
        start.Headers.Add("X-MobiMate-Req", "1");
        await c.SendAsync(start);
        host.Audio.Current!.PushVoice(3);

        var until = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < until && (await TestHost.Data(await c.GetAsync("/api/chat/game/log"))).GetArrayLength() == 0) await Task.Delay(30);
        var log = await TestHost.Data(await c.GetAsync("/api/chat/game/log"));
        Assert.Equal(1, log.GetArrayLength());                                    // 이모지가 없으니 50자가 그대로 자동 전송된다
        Assert.Equal(new string('가', 50), log[0].GetProperty("message").GetString());
    }
}
