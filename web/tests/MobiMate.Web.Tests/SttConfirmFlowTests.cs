using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MobiMate.Web.Voice;

namespace MobiMate.Web.Tests;

/// <summary>
/// 게임은 채팅마다 확인창을 띄운다. 음성으로 보낸 문장은 확인창이 한 번만 뜨게 하고(행동 명령은 두 번째 write_chat이라 두 번째 확인창을 만든다),
/// 게임이 관리자 권한이고 앱이 일반 권한이면 설정 화면이 알린다.
/// </summary>
public class SttConfirmFlowTests
{
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

    private const string EmotionSentence = "안녕하세요 여러분";   // 자동 이모티콘 😊 + 행동 /손인사1 이 붙는 문장 (ApiTests와 같다)

    private static int ChatCalls(TestHost host) => host.CliCalls().Count(x => x.StartsWith("write_chat"));

    [Fact]
    public async Task ATypedSentence_StillSendsTheTextAndTheSocialAction_TwoPopups()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.PostAsJsonAsync("/api/chat/game", new { text = EmotionSentence, autoEmote = true });
        Assert.True(await Eventually(() => ChatCalls(host) >= 2));   // 기존 동작 유지: 직접 입력은 행동까지
        Assert.Contains("write_chat /손인사1", host.CliCalls());
    }

    [Fact]
    public async Task AVoiceSentence_SendsOnlyTheText_SoTheGameAsksOnlyOnce()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var r = await TestHost.Data(await c.PostAsJsonAsync("/api/chat/game", new { text = EmotionSentence, autoEmote = true, source = "음성" }));
        Assert.Equal("안녕하세요 여러분 😊", r.GetProperty("message").GetString());   // 이모지는 문장에 그대로 붙는다
        Assert.Equal(JsonValueKind.Null, r.GetProperty("behaviour").ValueKind);        // 행동 명령은 보내지 않는다
        await Task.Delay(500);                                                          // 두 번째 호출이 있다면 150ms 뒤에 나갔을 것이다
        Assert.Equal(1, ChatCalls(host));
        Assert.DoesNotContain("write_chat /손인사1", host.CliCalls());
    }

    [Fact]
    public async Task AnAutoSentVoiceSentence_AlsoAsksOnlyOnce()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { EmotionSentence };
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { autoSend = true }));
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);

        Assert.True(await Eventually(() => ChatCalls(host) >= 1));
        await Task.Delay(500);
        Assert.Equal(1, ChatCalls(host));
        var log = await TestHost.Data(await c.GetAsync("/api/chat/game/log"));
        Assert.Equal(JsonValueKind.Null, log[0].GetProperty("behaviour").ValueKind);
        Assert.Equal("음성", log[0].GetProperty("source").GetString());
    }

    // ── 권한 불일치 안내 ──

    [Theory]
    [InlineData(true, false, true)]     // 게임은 관리자, 앱은 일반 → 게임 화면에서 단축키가 안 먹는다
    [InlineData(true, true, false)]     // 둘 다 관리자
    [InlineData(false, false, false)]   // 둘 다 일반
    [InlineData(false, true, false)]    // 앱만 관리자(문제 없음)
    public void Mismatch_OnlyWhenTheGameIsElevatedAndTheAppIsNot(bool game, bool app, bool expected) =>
        Assert.Equal(expected, new ElevationInfo(app, game).Mismatch);

    [Fact]
    public void Mismatch_IsFalse_WhenTheGameIsNotRunningOrUnknown() => Assert.False(new ElevationInfo(false, null).Mismatch);

    [Fact]
    public void Detect_NeverThrows_AndKnowsThisProcess()
    {
        var info = ElevationProbe.Detect();
        Assert.Equal(ElevationProbe.IsAppElevated(), info.App);
        _ = ElevationProbe.ForegroundProcessName();   // 앞에 있는 창이 없어도 예외 없이
    }

    [Fact]
    public async Task TheStatus_ReportsTheElevationOfTheAppAndTheGame()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var e = (await TestHost.Data(await c.GetAsync("/api/stt/status"))).GetProperty("elevation");
        Assert.True(e.TryGetProperty("app", out var app) && app.ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.True(e.TryGetProperty("game", out _));
        Assert.True(e.TryGetProperty("mismatch", out var mm) && mm.ValueKind is JsonValueKind.True or JsonValueKind.False);
    }
}
