using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MobiMate.Web.Voice;

namespace MobiMate.Web.Tests;

/// <summary>음성 입력 서비스·API (요구사양 §11.13). 가짜 마이크·가짜 엔진으로 흐름과 보호 규칙을 시험한다.</summary>
public class SttTests
{
    private static HttpRequestMessage Req(HttpMethod m, string url, object? body = null, bool header = true, string? contentType = "application/json", string? origin = null)
    {
        var r = new HttpRequestMessage(m, url);
        if (contentType != null) r.Content = new StringContent(JsonSerializer.Serialize(body ?? new { }), Encoding.UTF8, contentType);
        if (header) r.Headers.Add(SttGuardHeader, "1");
        if (origin != null) r.Headers.Add("Origin", origin);
        return r;
    }

    private const string SttGuardHeader = "X-MobiMate-Req";

    private static Task<HttpResponseMessage> Post(HttpClient c, string url, object? body = null) => c.SendAsync(Req(HttpMethod.Post, url, body));

    private static async Task<bool> Eventually(Func<Task<bool>> cond, int ms = 4000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            if (await cond()) return true;
            await Task.Delay(20);
        }
        return await cond();
    }

    private static async Task<JsonElement> Status(HttpClient c) => await TestHost.Data(await c.GetAsync("/api/stt/status"));
    private static async Task<JsonElement> Last(HttpClient c) => await TestHost.Data(await c.GetAsync("/api/stt/last"));

    private static async Task<bool> IdleAsync(HttpClient c) => (await Status(c)).GetProperty("state").GetString() == "idle";

    [Fact]
    public async Task Start_Utterance_BecomesFinal_ThenIdle()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();

        var started = await TestHost.Data(await Post(c, "/api/stt/start"));
        Assert.False(string.IsNullOrEmpty(started.GetProperty("sessionId").GetString()));
        Assert.True(host.Audio.Current!.Started);
        Assert.Equal("listening", (await Status(c)).GetProperty("state").GetString());

        host.Audio.Current.PushVoice(4);   // 문장 + 뒤따르는 소리 → 끝점
        Assert.True(await Eventually(async () => (await Last(c)).ValueKind == JsonValueKind.Object));
        var last = await Last(c);
        Assert.Equal("힐러한분만더구합니다", last.GetProperty("text").GetString());
        Assert.Equal("힐러한분만더구합니다", last.GetProperty("original").GetString());

        Assert.True(await Eventually(() => IdleAsync(c)));   // 첫 문장 확정으로 세션 종료
        Assert.True(host.Audio.Current.Disposed);            // 마이크를 닫았다
    }

    [Fact]
    public async Task Start_WhileListening_Is409Busy_AndOpensMicOnlyOnce()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.OK, (await Post(c, "/api/stt/start")).StatusCode);

        var second = await Post(c, "/api/stt/start");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("STT_BUSY", await TestHost.ErrorCode(second));
        Assert.Equal(1, host.Audio.OpenCount);
    }

    [Fact]
    public async Task Stop_FinishesPartialSentence()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { "보스패턴조심하세요" };
        host.Engines.SamplesPerUtterance = 16000;   // 한 블록(1600)이면 앞부분만 들린 상태
        var c = await host.LocalAsync();
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(3);   // 16000샘플 문장 중 30% ≈ 앞 3글자
        await Task.Delay(150);

        var stop = await TestHost.Data(await Post(c, "/api/stt/stop"));
        Assert.True(stop.GetProperty("stopped").GetBoolean());
        Assert.True(await Eventually(() => IdleAsync(c)));
        var text = (await Last(c)).GetProperty("text").GetString()!;
        Assert.StartsWith("보스", text);          // 지금까지 들은 글자가 확정된다
        Assert.True(text.Length < "보스패턴조심하세요".Length);
    }

    [Fact]
    public async Task Stop_WhenIdle_IsNotAnError()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var d = await TestHost.Data(await Post(c, "/api/stt/stop"));
        Assert.False(d.GetProperty("stopped").GetBoolean());
    }

    [Fact]
    public async Task EmptyResult_DoesNotProduceFinal()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { "" };
        var c = await host.LocalAsync();
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(3);
        await Task.Delay(200);
        await Post(c, "/api/stt/stop");
        Assert.True(await Eventually(() => IdleAsync(c)));
        Assert.Equal(JsonValueKind.Null, (await Last(c)).ValueKind);
    }

    [Fact]
    public async Task MaxSeconds_EndsTheSession()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var put = await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { maxSeconds = 1 }));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        await TestHost.Data(await Post(c, "/api/stt/start"));
        Assert.True(await Eventually(() => IdleAsync(c), 4000));   // 소리가 없어도 1초 뒤 스스로 끝난다
        Assert.True(host.Audio.Current!.Disposed);
    }

    [Fact]
    public async Task DeviceRemoved_EndsSessionAndKeepsWhatWasHeard()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { "저는잠깐자리비우겠습니다" };
        host.Engines.SamplesPerUtterance = 16000;
        var c = await host.LocalAsync();
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(1);
        await Task.Delay(150);

        host.Audio.Current.Fail(new AudioFailure(AudioFailureKind.Removed, "마이크 연결이 끊겼습니다."));
        Assert.True(await Eventually(() => IdleAsync(c)));
        Assert.StartsWith("저는", (await Last(c)).GetProperty("text").GetString());
        // 세션이 끝났으니 다시 시작할 수 있다
        Assert.Equal(HttpStatusCode.OK, (await Post(c, "/api/stt/start")).StatusCode);
    }

    [Fact]
    public async Task ModelMissing_Is409_AndMicStaysClosed()
    {
        using var host = new TestHost();
        host.Engines.Installed = false;
        var c = await host.LocalAsync();
        var r = await Post(c, "/api/stt/start");
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("STT_MODEL_MISSING", await TestHost.ErrorCode(r));
        Assert.Equal(0, host.Audio.OpenCount);
    }

    [Theory]
    [InlineData(AudioFailureKind.Denied, "STT_MIC_DENIED")]
    [InlineData(AudioFailureKind.InUse, "STT_MIC_BUSY")]
    [InlineData(AudioFailureKind.NoDevice, "STT_MIC_NONE")]
    public async Task MicOpenFailure_IsReportedWithItsOwnCode_AndNothingIsStuck(AudioFailureKind kind, string code)
    {
        using var host = new TestHost();
        host.Audio.OpenFailure = new AudioFailure(kind, "실패");
        var c = await host.LocalAsync();
        var r = await Post(c, "/api/stt/start");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal(code, await TestHost.ErrorCode(r));
        Assert.True(await IdleAsync(c));

        host.Audio.OpenFailure = null;   // 고치면 바로 다시 된다
        Assert.Equal(HttpStatusCode.OK, (await Post(c, "/api/stt/start")).StatusCode);
    }

    // ── 보호 규칙 (FR-VC-10) ──

    [Fact]
    public async Task Start_WithoutCustomHeader_IsRejected_AndMicNeverOpens()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var r = await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start", header: false));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(0, host.Audio.OpenCount);
    }

    [Fact]
    public async Task Start_WithFormContentType_IsRejected()
    {
        // 다른 웹페이지의 단순 폼 전송(text/plain 등)으로는 마이크를 켤 수 없다
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var r = await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start", contentType: "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
        Assert.Equal(0, host.Audio.OpenCount);
    }

    [Fact]
    public async Task Start_FromForeignOrigin_IsRejected_ButOwnOriginIsAllowed()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var bad = await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start", origin: "http://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        Assert.Equal(0, host.Audio.OpenCount);

        var good = await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start", origin: host.Origin));
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
    }

    [Fact]
    public async Task LanDevice_CannotStart_UntilPcTurnsItOn()
    {
        using var host = new TestHost();
        var pc = await host.LocalAsync();
        var phone = await host.LanAsync(pc);

        var denied = await Post(phone, "/api/stt/start");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("STT_LAN_DISABLED", await TestHost.ErrorCode(denied));
        Assert.Equal(0, host.Audio.OpenCount);

        Assert.Equal(HttpStatusCode.OK, (await pc.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { allowLanStart = true }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(phone, "/api/stt/start")).StatusCode);
    }

    [Fact]
    public async Task Settings_AreChangeableOnlyFromThePc()
    {
        using var host = new TestHost();
        var pc = await host.LocalAsync();
        var phone = await host.LanAsync(pc);

        var put = await phone.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { allowLanStart = true }));
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal("PC_ONLY", await TestHost.ErrorCode(put));
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.GetAsync("/api/stt/settings")).StatusCode);
        Assert.False((await TestHost.Data(await pc.GetAsync("/api/stt/settings"))).GetProperty("allowLanStart").GetBoolean());
    }

    [Fact]
    public async Task Settings_RejectOutOfRangeValues_AndDefaultsAreSafe()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var d = await TestHost.Data(await c.GetAsync("/api/stt/settings"));
        Assert.False(d.GetProperty("autoSend").GetBoolean());
        Assert.False(d.GetProperty("allowLanStart").GetBoolean());
        Assert.Equal(15, d.GetProperty("maxSeconds").GetInt32());
        Assert.Equal(1, d.GetProperty("threads").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { maxSeconds = 0 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { threads = 9 }))).StatusCode);
    }

    [Fact]
    public async Task SelectedDevice_IsPassedToTheMicOpen()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { deviceId = "fake-1" }));
        await TestHost.Data(await Post(c, "/api/stt/start"));
        Assert.Equal("fake-1", host.Audio.LastRequestedId);

        var dev = await TestHost.Data(await c.GetAsync("/api/stt/devices"));
        Assert.Equal("가짜 마이크", dev.GetProperty("devices")[0].GetProperty("name").GetString());
    }

    // ── 자동 전송 (FR-VC-13) ──

    [Fact]
    public async Task AutoSend_SendsFinalToGameChat_WithVoiceSource()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { autoSend = true }));
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);

        Assert.True(await Eventually(async () => (await TestHost.Data(await c.GetAsync("/api/chat/game/log"))).GetArrayLength() == 1));
        var e = (await TestHost.Data(await c.GetAsync("/api/chat/game/log")))[0];
        Assert.Equal("음성", e.GetProperty("source").GetString());
        Assert.Contains("힐러한분만더구합니다", e.GetProperty("message").GetString());
        Assert.True(e.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task AutoSendOff_NeverSends()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);
        Assert.True(await Eventually(async () => (await Last(c)).ValueKind == JsonValueKind.Object));
        await Task.Delay(200);
        Assert.Equal(0, (await TestHost.Data(await c.GetAsync("/api/chat/game/log"))).GetArrayLength());
    }

    [Fact]
    public async Task AutoSend_TooLongSentence_IsNotSent()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { new string('가', 60) };
        host.Engines.SamplesPerUtterance = 1600;
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { autoSend = true }));
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(3);
        Assert.True(await Eventually(async () => (await Last(c)).ValueKind == JsonValueKind.Object));
        Assert.Equal(60, (await Last(c)).GetProperty("text").GetString()!.Length);   // 잘리지 않는다
        await Task.Delay(300);
        Assert.Equal(0, (await TestHost.Data(await c.GetAsync("/api/chat/game/log"))).GetArrayLength());
    }

    // ── 채팅 출처 (FR-GC-05) ──

    [Fact]
    public async Task ChatEndpoint_AcceptsVoiceSource_ButNotArbitrary()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/chat/game", new { text = "안녕하세요", autoEmote = false, source = "음성" })).StatusCode);
        await Task.Delay(1100);   // 같은 문장 중복 방지 창을 넘긴다
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/chat/game", new { text = "반갑습니다", autoEmote = false, source = "해킹" })).StatusCode);
        var log = await TestHost.Data(await c.GetAsync("/api/chat/game/log"));
        Assert.Equal("음성", log[0].GetProperty("source").GetString());
        Assert.Equal("직접", log[1].GetProperty("source").GetString());
    }

    [Fact]
    public async Task Last_IsNullWhenNothingWasHeard()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(JsonValueKind.Null, (await Last(c)).ValueKind);
        Assert.False((await Status(c)).GetProperty("autoSend").GetBoolean());
        Assert.True((await Status(c)).GetProperty("modelInstalled").GetBoolean());
        Assert.NotNull(host.Services.GetRequiredService<SttService>());
    }
}
