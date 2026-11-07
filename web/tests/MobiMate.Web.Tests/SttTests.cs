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
    private const string GuardHeader = "X-MobiMate-Req";

    private static HttpRequestMessage Req(HttpMethod m, string url, object? body = null, bool header = true, string? contentType = "application/json", string? origin = null, string? host = null)
    {
        var r = new HttpRequestMessage(m, url);
        if (contentType != null) r.Content = new StringContent(JsonSerializer.Serialize(body ?? new { }), Encoding.UTF8, contentType);
        if (header) r.Headers.Add(GuardHeader, "1");
        if (origin != null) r.Headers.Add("Origin", origin);
        if (host != null) r.Headers.Host = host;
        return r;
    }

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
    private static SttService Stt(TestHost host) => host.Services.GetRequiredService<SttService>();

    /// <summary>SSE 이벤트를 모아 두는 수집기. 이벤트 이름·내용을 직접 검사한다.</summary>
    private sealed class Sse : IDisposable
    {
        private readonly List<(string Name, JsonElement Data)> _events = new();
        private readonly CancellationTokenSource _cts = new();

        private Sse(Stream body)
        {
            _ = Task.Run(async () =>
            {
                using var reader = new StreamReader(body);
                string? name = null;
                try
                {
                    while (!_cts.IsCancellationRequested && await reader.ReadLineAsync(_cts.Token) is { } line)
                    {
                        if (line.StartsWith("event:")) name = line[6..].Trim();
                        else if (line.StartsWith("data:") && name != null)
                        {
                            try { lock (_events) _events.Add((name, JsonDocument.Parse(line[5..].Trim()).RootElement.Clone())); } catch (JsonException) { }
                            name = null;
                        }
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            });
        }

        public static async Task<Sse> Open(HttpClient c)
        {
            var res = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
            return new Sse(await res.Content.ReadAsStreamAsync());
        }

        public List<JsonElement> Of(string name)
        {
            lock (_events) return _events.Where(e => e.Name == name).Select(e => e.Data).ToList();
        }

        public async Task<JsonElement?> WaitFor(string name, Func<JsonElement, bool>? pred = null, int ms = 4000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            do
            {
                foreach (var d in Of(name)) if (pred == null || pred(d)) return d;
                await Task.Delay(15);
            } while (DateTime.UtcNow < until);
            return null;
        }

        public void Dispose() => _cts.Cancel();
    }

    private static bool IdleWithError(JsonElement e, string code) =>
        e.GetProperty("state").GetString() == "idle" && e.GetProperty("error").ValueKind == JsonValueKind.String && e.GetProperty("error").GetString() == code;

    // ── 기본 흐름 ──

    [Fact]
    public async Task Start_Utterance_BecomesFinal_ThenIdle()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        using var sse = await Sse.Open(c);

        var started = await TestHost.Data(await Post(c, "/api/stt/start"));
        Assert.False(string.IsNullOrEmpty(started.GetProperty("sessionId").GetString()));
        Assert.True(host.Audio.Current!.Started);
        Assert.Equal("listening", (await Status(c)).GetProperty("state").GetString());

        host.Audio.Current.PushVoice(4);   // 문장 + 뒤따르는 소리 → 끝점
        var fin = await sse.WaitFor("stt.final");
        Assert.NotNull(fin);
        Assert.Equal("힐러한분만더구합니다", fin!.Value.GetProperty("text").GetString());
        Assert.Equal("힐러한분만더구합니다", fin.Value.GetProperty("original").GetString());
        Assert.False(fin.Value.GetProperty("autoSent").GetBoolean());   // 자동 전송이 꺼져 있으니 화면 초안에 붙는다
        Assert.NotNull(await sse.WaitFor("stt.partial"));               // 인식 중 글자도 흘렀다

        Assert.True(await Eventually(() => IdleAsync(c)));   // 첫 문장 확정으로 세션 종료
        Assert.True(host.Audio.Current.Disposed);            // 마이크를 닫았다
        Assert.Equal("힐러한분만더구합니다", (await Last(c)).GetProperty("text").GetString());

        // 상태 이벤트 순서: starting → listening → (finishing →) idle, 오류 없음
        var states = sse.Of("stt.state").Select(e => e.GetProperty("state").GetString()).ToList();
        Assert.Equal("starting", states.First());
        Assert.Contains("listening", states);
        Assert.Equal("idle", states.Last());
        Assert.All(sse.Of("stt.state"), e => Assert.Equal(JsonValueKind.Null, e.GetProperty("error").ValueKind));
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
    public async Task EmptyResult_DoesNotProduceFinal_AndEndsWithoutError()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { "" };
        var c = await host.LocalAsync();
        using var sse = await Sse.Open(c);
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(3);
        await Task.Delay(200);
        await Post(c, "/api/stt/stop");
        Assert.True(await Eventually(() => IdleAsync(c)));
        await Task.Delay(100);

        Assert.Empty(sse.Of("stt.final"));
        Assert.Equal(JsonValueKind.Null, (await Last(c)).ValueKind);
        Assert.All(sse.Of("stt.state"), e => Assert.Equal(JsonValueKind.Null, e.GetProperty("error").ValueKind));   // 실패가 아니라 "들린 게 없음"
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
    public async Task DeviceRemoved_EndsSessionKeepsWhatWasHeard_AndReportsTheCause()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { "저는잠깐자리비우겠습니다" };
        host.Engines.SamplesPerUtterance = 16000;
        var c = await host.LocalAsync();
        using var sse = await Sse.Open(c);
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(1);
        await Task.Delay(150);

        host.Audio.Current.Fail(new AudioFailure(AudioFailureKind.Removed, "마이크 연결이 끊겼습니다."));
        Assert.NotNull(await sse.WaitFor("stt.state", e => IdleWithError(e, "STT_MIC_REMOVED")));   // 원인이 화면에 전달된다
        Assert.StartsWith("저는", (await Last(c)).GetProperty("text").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Post(c, "/api/stt/start")).StatusCode);   // 다시 시작할 수 있다
    }

    [Fact]
    public async Task NoSignal_WarnsAfterThreeSecondsOfExactSilence()
    {
        // 마이크 접근이 막히면 소리가 정확히 0으로 온다 (FR-VC-05 ⑥). 시간을 기다리지 않고 샘플 수로 시험한다.
        using var host = new TestHost();
        host.Engines.Utterances = new[] { "" };   // 가짜 엔진이 샘플 수만 세어 글자를 내면 세션이 먼저 끝난다: 아무 글자도 내지 않게
        var c = await host.LocalAsync();
        using var sse = await Sse.Open(c);
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushSilence(29);   // 2.9초: 아직 아니다
        await Task.Delay(150);
        Assert.Empty(sse.Of("stt.state").Where(e => e.GetProperty("warn").ValueKind == JsonValueKind.String));
        host.Audio.Current.PushSilence(2);     // 3.1초
        Assert.NotNull(await sse.WaitFor("stt.state", e => e.GetProperty("warn").ValueKind == JsonValueKind.String && e.GetProperty("warn").GetString() == "NO_SIGNAL"));
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

    [Fact]
    public async Task EngineCreateFailure_Is500_AndNothingIsStuck()
    {
        using var host = new TestHost();
        host.Engines.ThrowOnGetEngine = true;   // 예: 모델 파일이 손상됨
        var c = await host.LocalAsync();
        var r = await Post(c, "/api/stt/start");
        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        Assert.Equal("STT_ENGINE_FAILED", await TestHost.ErrorCode(r));
        Assert.Equal(0, host.Audio.OpenCount);   // 엔진이 안 되면 마이크도 열지 않는다
        Assert.True(await IdleAsync(c));

        host.Engines.ThrowOnGetEngine = false;
        Assert.Equal(HttpStatusCode.OK, (await Post(c, "/api/stt/start")).StatusCode);
    }

    [Fact]
    public async Task EngineFailureWhileListening_EndsTheSession_WithAnError()
    {
        using var host = new TestHost();
        host.Engines.ThrowOnAccept = true;
        var c = await host.LocalAsync();
        using var sse = await Sse.Open(c);
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(1);

        Assert.NotNull(await sse.WaitFor("stt.state", e => IdleWithError(e, "STT_MIC_FAILED")));
        Assert.True(host.Audio.Current.Disposed);   // 마이크는 반드시 닫힌다
        Assert.True(await IdleAsync(c));
    }

    // ── 시작하는 도중 중지 (경합) ──

    [Fact]
    public async Task StopWhileEngineLoads_CancelsWithoutOpeningTheMic()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        host.Engines.OnGetEngine = () => Stt(host).Stop();   // 첫 사용 때 모델을 올리는 동안 사용자가 중지를 누른다
        var r = await TestHost.Data(await Post(c, "/api/stt/start"));
        Assert.True(r.GetProperty("cancelled").GetBoolean());
        Assert.Equal(0, host.Audio.OpenCount);
        Assert.True(await IdleAsync(c));
        await AssertCanStartAgain(host, c);
    }

    [Fact]
    public async Task StopWhileMicOpens_CancelsAndClosesTheMic_WithoutErrorToast()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        using var sse = await Sse.Open(c);
        host.Audio.OnOpen = () => Stt(host).Stop();
        var r = await TestHost.Data(await Post(c, "/api/stt/start"));
        Assert.True(r.GetProperty("cancelled").GetBoolean());
        Assert.True(host.Audio.Current!.Disposed);
        Assert.True(await IdleAsync(c));
        await Task.Delay(100);
        Assert.All(sse.Of("stt.state"), e => Assert.Equal(JsonValueKind.Null, e.GetProperty("error").ValueKind));   // 잘못된 오류 알림이 없다
        await AssertCanStartAgain(host, c);
    }

    [Fact]
    public async Task StopRightAfterCaptureStarts_NeverLeavesItListening()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        using var sse = await Sse.Open(c);
        host.Audio.OnCaptureStart = () => Stt(host).Stop();
        var r = await TestHost.Data(await Post(c, "/api/stt/start"));
        Assert.True(r.GetProperty("cancelled").GetBoolean());
        Assert.True(host.Audio.Current!.Disposed);
        Assert.True(await IdleAsync(c));
        await Task.Delay(100);
        Assert.Equal("idle", sse.Of("stt.state").Last().GetProperty("state").GetString());   // 마지막 알림이 listening이면 화면이 멈춘다
        await AssertCanStartAgain(host, c);
    }

    private static async Task AssertCanStartAgain(TestHost host, HttpClient c)
    {
        host.Engines.OnGetEngine = null;
        host.Audio.OnOpen = null;
        host.Audio.OnCaptureStart = null;
        var r = await Post(c, "/api/stt/start");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False((await TestHost.Data(r)).TryGetProperty("cancelled", out _));
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

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:17800")]
    [InlineData("rebind.attacker.test")]
    public async Task ForeignHost_IsRejected_OnEveryEndpoint_EvenWithMatchingOrigin(string hostHeader)
    {
        // DNS 리바인딩: 공격 페이지의 Origin과 Host가 같고 원격 IP는 루프백이다. Host가 이 서버 주소가 아니면 모두 거절한다.
        using var host = new TestHost();
        var c = await host.LocalAsync();

        var start = await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start", origin: "http://" + hostHeader, host: hostHeader));
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        Assert.Equal("STT_FORBIDDEN", await TestHost.ErrorCode(start));
        Assert.Equal(0, host.Audio.OpenCount);

        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req(HttpMethod.Get, "/api/stt/last", contentType: null, host: hostHeader))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req(HttpMethod.Get, "/api/stt/status", contentType: null, host: hostHeader))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { autoSend = true }, origin: "http://" + hostHeader, host: hostHeader))).StatusCode);
        Assert.False((await TestHost.Data(await c.GetAsync("/api/stt/settings"))).GetProperty("autoSend").GetBoolean());   // 설정도 안 바뀌었다
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:17800")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:17800")]
    [InlineData("[::1]:17800")]
    public async Task OwnHostNames_AreAllowed(string hostHeader)
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var r = await c.SendAsync(Req(HttpMethod.Get, "/api/stt/status", contentType: null, host: hostHeader));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    private sealed class FakeLanHosts(params string[] hosts) : MobiMate.Web.Lan.ILanHosts
    {
        public IReadOnlyCollection<string> Current { get; } = hosts;
        public bool Serves(System.Net.IPAddress local) => true;
    }

    private static bool HostOk(string hostHeader, params string[] lanHosts)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.Host = new Microsoft.AspNetCore.Http.HostString(hostHeader.Contains(':') && !hostHeader.StartsWith('[') ? hostHeader : hostHeader);
        return MobiMate.Web.Endpoints.SttGuard.HostAllowed(ctx, new FakeLanHosts(lanHosts));
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST:17800", true)]
    [InlineData("127.0.0.1:17800", true)]
    [InlineData("[::1]:17800", true)]
    [InlineData("192.168.0.23:17800", true)]        // 지금 열어 둔 LAN IP
    [InlineData("mobimate.local:17800", true)]      // 광고 중인 mDNS 이름
    [InlineData("MobiMate.LOCAL", true)]            // 대소문자 무시
    [InlineData("192.168.0.99:17800", false)]       // 열어 두지 않은 주소
    [InlineData("evil.example", false)]
    [InlineData("localhost.evil.example", false)]   // localhost로 시작해도 다른 도메인
    [InlineData("127.0.0.1.evil.example", false)]
    [InlineData("", false)]
    public void HostAllowed_OnlyOwnAddresses(string hostHeader, bool expected) =>
        Assert.Equal(expected, HostOk(hostHeader, "192.168.0.23", "mobimate.local"));

    [Fact]
    public async Task LanDevices_CannotUseAnyVoiceEndpoint_ReadOrWrite()
    {
        // 음성 입력은 게임 PC에서만 쓴다: 폰·태블릿(LAN)은 시작·중지뿐 아니라 상태·인식 결과·마이크 목록도 볼 수 없다
        using var host = new TestHost();
        var pc = await host.LocalAsync();
        var phone = await host.LanAsync(pc);

        foreach (var url in new[] { "/api/stt/status", "/api/stt/last", "/api/stt/devices", "/api/stt/model", "/api/stt/settings", "/api/stt/vocab" })
        {
            var r = await phone.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("PC_ONLY", await TestHost.ErrorCode(r));
        }
        foreach (var url in new[] { "/api/stt/start", "/api/stt/stop", "/api/stt/devices/test", "/api/stt/model/install", "/api/stt/vocab/clear" })
        {
            var r = await phone.SendAsync(Req(HttpMethod.Post, url));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("PC_ONLY", await TestHost.ErrorCode(r));
        }
        Assert.Equal(0, host.Audio.OpenCount);   // 마이크는 한 번도 열리지 않았다
        Assert.Equal(HttpStatusCode.OK, (await Post(pc, "/api/stt/start")).StatusCode);   // PC에서는 된다
    }

    [Fact]
    public async Task VoiceEvents_ReachOnlyThisPc_NotOtherDevicesOnTheNetwork()
    {
        // PC 마이크로 말한 내용(인식 중인 글자·확정 글자)을 같은 와이파이의 다른 기기에 퍼뜨리지 않는다
        using var host = new TestHost();
        var pc = await host.LocalAsync();
        var phone = await host.LanAsync(pc);
        using var pcSse = await Sse.Open(pc);
        using var phoneSse = await Sse.Open(phone);

        await Post(pc, "/api/stt/start");
        host.Audio.Current!.PushVoice(4);
        Assert.NotNull(await pcSse.WaitFor("stt.final"));
        Assert.NotNull(await pcSse.WaitFor("stt.state"));
        await Task.Delay(300);

        Assert.Empty(phoneSse.Of("stt.final"));
        Assert.Empty(phoneSse.Of("stt.partial"));
        Assert.Empty(phoneSse.Of("stt.state"));
        Assert.NotEmpty(phoneSse.Of("hello"));   // 연결 자체는 되어 있다 (다른 이벤트는 받는다)
    }

    [Fact]
    public async Task Settings_AreChangeableOnlyFromThePc()
    {
        using var host = new TestHost();
        var pc = await host.LocalAsync();
        var phone = await host.LanAsync(pc);

        var put = await phone.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { autoSend = true }));
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal("PC_ONLY", await TestHost.ErrorCode(put));
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.GetAsync("/api/stt/settings")).StatusCode);
        Assert.False((await TestHost.Data(await pc.GetAsync("/api/stt/settings"))).GetProperty("autoSend").GetBoolean());
    }

    [Fact]
    public async Task Settings_RejectOutOfRangeValues_AndTheDefaultsAreTheChosenFlow()
    {
        using var host = new TestHost { LegacyVoiceBaseline = false };
        var c = await host.LocalAsync();
        var d = await TestHost.Data(await c.GetAsync("/api/stt/settings"));
        // 승운 2026-11-06 기본 흐름: Ctrl+Space → 삑 → 녹음 → 게임 확인창에 출력 → 사용자가 보고 Space로 보냄
        Assert.True(d.GetProperty("autoSend").GetBoolean());
        Assert.Equal("LCtrl+Space", d.GetProperty("hotkey").GetString());
        Assert.Equal("toggle", d.GetProperty("hotkeyMode").GetString());
        Assert.True(d.GetProperty("cue").GetBoolean());
        Assert.True(d.GetProperty("hotwords").GetBoolean());
        Assert.True(d.GetProperty("spacing").GetBoolean());
        Assert.Equal(2, d.GetProperty("spacingLevel").GetInt32());
        Assert.False(d.TryGetProperty("allowLanStart", out _));   // 폰·태블릿 시작 설정은 없다(PC 전용)
        Assert.Equal(15, d.GetProperty("maxSeconds").GetInt32());
        Assert.Equal(1, d.GetProperty("threads").GetInt32());
        Assert.Equal(500, d.GetProperty("endSilenceMs").GetInt32());   // 기본 0.5초

        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { maxSeconds = 0 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { threads = 9 }))).StatusCode);
    }

    [Fact]
    public async Task Settings_EndSilence_ValidatesAndReportsRestart()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { endSilenceMs = 100 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { endSilenceMs = 5000 }))).StatusCode);
        var same = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { endSilenceMs = 500 })));
        Assert.False(same.GetProperty("restartRequired").GetBoolean());
        var changed = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { endSilenceMs = 800 })));
        Assert.True(changed.GetProperty("restartRequired").GetBoolean());
        Assert.Equal(800, changed.GetProperty("settings").GetProperty("endSilenceMs").GetInt32());
    }

    [Fact]
    public async Task Settings_ThreadsChange_ReportsThatARestartIsNeeded()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var first = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { threads = 2 })));
        Assert.True(first.GetProperty("restartRequired").GetBoolean());
        Assert.Equal(2, first.GetProperty("settings").GetProperty("threads").GetInt32());
        var same = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { threads = 2, maxSeconds = 20 })));
        Assert.False(same.GetProperty("restartRequired").GetBoolean());
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
    public async Task AutoSend_SendsFinalToGameChat_WithVoiceSource_AndTellsTheScreenNotToAddItToTheDraft()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        using var sse = await Sse.Open(c);
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { autoSend = true }));
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);

        Assert.True(await Eventually(async () => (await TestHost.Data(await c.GetAsync("/api/chat/game/log"))).GetArrayLength() == 1));
        var e = (await TestHost.Data(await c.GetAsync("/api/chat/game/log")))[0];
        Assert.Equal("음성", e.GetProperty("source").GetString());
        Assert.Contains("힐러한분만더구합니다", e.GetProperty("message").GetString());
        Assert.True(e.GetProperty("ok").GetBoolean());

        var fin = await sse.WaitFor("stt.final");
        Assert.True(fin!.Value.GetProperty("autoSent").GetBoolean());   // 화면이 초안에 또 붙이지 않는다 (다시 보내는 사고 방지)
        Assert.True((await Last(c)).GetProperty("autoSent").GetBoolean());
    }

    [Fact]
    public async Task Recognized_Sentence_IsKeptInTheHistory_ForManualCorrection()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);
        Assert.True(await Eventually(async () => (await TestHost.Data(await c.GetAsync("/api/stt/history"))).GetArrayLength() == 1));
        var e = (await TestHost.Data(await c.GetAsync("/api/stt/history")))[0];
        Assert.Equal("힐러한분만더구합니다", e.GetProperty("original").GetString());   // 인식기가 낸 글자 그대로
        Assert.False(string.IsNullOrEmpty(e.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task HistoryOff_KeepsNothing()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { history = false }));
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);
        Assert.True(await Eventually(async () => (await Last(c)).ValueKind == JsonValueKind.Object));
        Assert.Equal(0, (await TestHost.Data(await c.GetAsync("/api/stt/history"))).GetArrayLength());
    }

    [Fact]
    public async Task AutoSendOff_NeverSends()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);
        Assert.True(await Eventually(async () => (await Last(c)).ValueKind == JsonValueKind.Object));
        Assert.True(await Eventually(() => IdleAsync(c)));
        await Task.Delay(200);   // 보냈다면 이쯤 로그에 있다
        Assert.Equal(0, (await TestHost.Data(await c.GetAsync("/api/chat/game/log"))).GetArrayLength());
    }

    [Fact]
    public async Task AutoSend_TooLongSentence_IsKeptForTheDraft_NotSent()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { new string('가', 60) };
        host.Engines.SamplesPerUtterance = 1600;
        var c = await host.LocalAsync();
        using var sse = await Sse.Open(c);
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { autoSend = true }));
        await TestHost.Data(await Post(c, "/api/stt/start"));
        host.Audio.Current!.PushVoice(3);

        var fin = await sse.WaitFor("stt.final");
        Assert.Equal(60, fin!.Value.GetProperty("text").GetString()!.Length);   // 잘리지 않는다
        Assert.False(fin.Value.GetProperty("autoSent").GetBoolean());           // 보내지 않았으니 화면 초안에 남긴다
        Assert.NotNull(await sse.WaitFor("toast", t => t.GetProperty("message").GetString()!.Contains("길어서")));
        Assert.True(await Eventually(() => IdleAsync(c)));
        await Task.Delay(200);
        Assert.Equal(0, (await TestHost.Data(await c.GetAsync("/api/chat/game/log"))).GetArrayLength());
    }

    [Theory]
    [InlineData("가나다라마", false, true)]
    [InlineData("가나다라마", true, true)]
    [InlineData("안녕하세요 여러분", true, true)]
    public void FitsInChat_ShortSentences(string text, bool autoEmote, bool expected) =>
        Assert.Equal(expected, SttService.FitsInChat(text, autoEmote));

    [Fact]
    public void FitsInChat_UsesTheRealEmojiLength_NotAFixedReserve()
    {
        // 49자: 이모티콘이 꺼져 있으면 한 번에 들어가지만, 자동 이모티콘(" 😊")이 붙으면 본문이 잘린다 → 자동 전송하면 안 된다
        var text = "안녕하세요" + new string('가', 44);
        Assert.Equal(49, text.Length);
        Assert.True(SttService.FitsInChat(text, autoEmote: false));
        Assert.False(SttService.FitsInChat(text, autoEmote: true));
        // 51자는 이모티콘이 없어도 넘친다
        Assert.False(SttService.FitsInChat(new string('가', 51), autoEmote: false));
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
    public async Task Status_ReportsDefaults_WhenNothingWasHeard()
    {
        using var host = new TestHost();   // 예전 기준선(자동 전송 끔)
        var c = await host.LocalAsync();
        Assert.Equal(JsonValueKind.Null, (await Last(c)).ValueKind);
        var s = await Status(c);
        Assert.False(s.GetProperty("autoSend").GetBoolean());
        Assert.False(s.TryGetProperty("allowLanStart", out _));
        Assert.True(s.GetProperty("modelInstalled").GetBoolean());
        Assert.Equal("idle", s.GetProperty("state").GetString());
    }
}
