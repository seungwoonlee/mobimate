using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MobiMate.Web.Voice;

namespace MobiMate.Web.Tests;

/// <summary>전역 단축키·신호음·마이크 시험 (FR-VC-02·05)</summary>
public class SttHotkeyTests
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
        while (DateTime.UtcNow < until)
        {
            if (cond()) return true;
            await Task.Delay(15);
        }
        return cond();
    }

    private static SttService Stt(TestHost h) => h.Services.GetRequiredService<SttService>();
    private static HotkeyService Hotkeys(TestHost h) => h.Services.GetRequiredService<HotkeyService>();
    private static Task<HttpResponseMessage> Put(HttpClient c, object body) => c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", body));

    // ── 단축키 글 → 키 ──

    [Theory]
    [InlineData("Ctrl+Alt+V", HotkeyParser.Ctrl | HotkeyParser.Alt, 0x56u, "Ctrl+Alt+V")]
    [InlineData("alt+ctrl+v", HotkeyParser.Ctrl | HotkeyParser.Alt, 0x56u, "Ctrl+Alt+V")]    // 순서·대소문자 무관, 표시는 정리된다
    [InlineData("F9", 0u, 0x78u, "F9")]                                                       // 기능키는 단독 허용
    [InlineData("Shift+F12", HotkeyParser.Shift, 0x7Bu, "Shift+F12")]
    [InlineData("Ctrl+Shift+7", HotkeyParser.Ctrl | HotkeyParser.Shift, 0x37u, "Ctrl+Shift+7")]
    [InlineData("Win+Space", HotkeyParser.Win, 0x20u, "Win+Space")]
    [InlineData("Ctrl+PageDown", HotkeyParser.Ctrl, 0x22u, "Ctrl+PageDown")]
    [InlineData(" Ctrl + Alt + F24 ", HotkeyParser.Ctrl | HotkeyParser.Alt, 0x87u, "Ctrl+Alt+F24")]
    public void Parse_Valid(string text, uint mods, uint vk, string display)
    {
        var s = HotkeyParser.Parse(text);
        Assert.NotNull(s);
        Assert.Equal(mods, s!.Modifiers);
        Assert.Equal(vk, s.VirtualKey);
        Assert.Equal(display, s.Display);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("V")]            // 글자 단독: 모든 프로그램에서 그 글자를 칠 수 없게 된다
    [InlineData("7")]
    [InlineData("Space")]
    [InlineData("Ctrl")]         // 주 키가 없다
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+V+B")]     // 주 키가 둘
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Shift+V")]      // Shift만으로는 대문자 입력이 모든 프로그램에서 막힌다
    [InlineData("Shift+7")]
    [InlineData("Shift+Space")]
    public void Parse_Invalid(string? text) => Assert.Null(HotkeyParser.Parse(text));

    // ── 설정 → 등록 ──

    [Fact]
    public async Task SavingAHotkey_RegistersItAtOnce_AndClearingUnregisters()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var r = await TestHost.Data(await Put(c, new { hotkey = "alt+ctrl+v" }));
        Assert.Equal("Ctrl+Alt+V", r.GetProperty("hotkey").GetProperty("key").GetString());
        Assert.True(r.GetProperty("hotkey").GetProperty("registered").GetBoolean());
        Assert.Equal("Ctrl+Alt+V", host.Hotkeys.Current!.Display);
        Assert.Equal("Ctrl+Alt+V", (await TestHost.Data(await c.GetAsync("/api/stt/settings"))).GetProperty("hotkey").GetString());   // 정리된 표기로 저장

        var cleared = await TestHost.Data(await Put(c, new { hotkey = "" }));
        Assert.False(cleared.GetProperty("hotkey").GetProperty("registered").GetBoolean());
        Assert.Null(host.Hotkeys.Current);
    }

    [Fact]
    public async Task AConflictingHotkey_IsKeptButReportedAsNotRegistered()
    {
        using var host = new TestHost();
        host.Hotkeys.FailWith = "다른 프로그램이 이 단축키를 이미 쓰고 있습니다. 다른 조합을 골라 주세요.";
        var c = await host.LocalAsync();
        var r = await TestHost.Data(await Put(c, new { hotkey = "Ctrl+Alt+V" }));
        Assert.False(r.GetProperty("hotkey").GetProperty("registered").GetBoolean());
        Assert.Contains("이미 쓰고", r.GetProperty("hotkey").GetProperty("error").GetString());
        Assert.Equal("Ctrl+Alt+V", r.GetProperty("settings").GetProperty("hotkey").GetString());   // 설정은 남는다(다른 프로그램을 끄면 다음 실행에 등록된다)

        var status = await TestHost.Data(await c.GetAsync("/api/stt/status"));
        Assert.False(status.GetProperty("hotkey").GetProperty("registered").GetBoolean());
        Assert.NotNull(status.GetProperty("hotkey").GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("V")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Ctrl+V+B")]
    public async Task AnInvalidHotkey_Is400_AndNothingIsChanged(string key)
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await Put(c, new { hotkey = "F9" });
        var r = await Put(c, new { hotkey = key });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("F9", host.Hotkeys.Current!.Display);   // 이전 단축키가 그대로
    }

    [Fact]
    public async Task HotkeyMode_MustBeHoldOrToggle()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(c, new { hotkeyMode = "always" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put(c, new { hotkeyMode = "toggle" })).StatusCode);
        Assert.Equal("toggle", (await TestHost.Data(await c.GetAsync("/api/stt/settings"))).GetProperty("hotkeyMode").GetString());
    }

    [Fact]
    public async Task LanDevices_CannotChangeTheHotkey()
    {
        using var host = new TestHost();
        var pc = await host.LocalAsync();
        var phone = await host.LanAsync(pc);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(phone, new { hotkey = "F9" })).StatusCode);
        Assert.Null(host.Hotkeys.Current);
    }

    [Fact]
    public void RegistersTheSavedHotkeyAtStartup()
    {
        using var host = new TestHost();
        var settings = host.Services.GetRequiredService<MobiMate.Web.Hosting.WebSettingsStore>();
        settings.Update(s => s.SttHotkey = "F8");
        var status = Hotkeys(host).Apply(settings.Current.SttHotkey);   // 앱을 켤 때 HotkeyService.StartAsync가 하는 일
        Assert.True(status.Registered);
        Assert.Equal(0x77u, host.Hotkeys.Current!.VirtualKey);
    }

    // ── 누르는 동안 듣기 / 눌러서 켜고 끄기 ──

    [Fact]
    public async Task HoldMode_ListensWhileTheKeyIsDown_AndStopsOnRelease_KeepingSeveralSentences()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { "첫째문장", "둘째문장" };
        var c = await host.LocalAsync();
        await Put(c, new { hotkey = "F9" });

        host.Keys.Down(0x78);
        host.Hotkeys.Fire();
        Assert.True(await Eventually(() => host.Audio.Current?.Started == true));
        Assert.Equal(SttState.Listening, Stt(host).Snapshot().State);

        host.Audio.Current!.PushVoice(4);   // 첫 문장 확정 — 키를 누르고 있으니 세션은 이어진다
        Assert.True(await Eventually(() => Stt(host).Last()?.Text == "첫째문장"));
        Assert.Equal(SttState.Listening, Stt(host).Snapshot().State);
        host.Audio.Current.PushVoice(4);    // 둘째 문장
        Assert.True(await Eventually(() => Stt(host).Last()?.Text == "둘째문장"));

        host.Keys.Up(0x78);                 // 키를 뗀다
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Idle));
        Assert.True(host.Audio.Current.Disposed);
    }

    [Fact]
    public async Task HoldMode_ReleasingRightAway_StopsTheSession()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await Put(c, new { hotkey = "F9" });
        host.Keys.Down(0x78);
        host.Hotkeys.Fire();
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Listening));
        host.Keys.Up(0x78);
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Idle));
    }

    [Fact]
    public async Task ToggleMode_FirstPressStarts_SecondPressStops()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await Put(c, new { hotkey = "Ctrl+Alt+V", hotkeyMode = "toggle" });

        host.Hotkeys.Fire();
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Listening));
        host.Hotkeys.Fire();
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Idle));
    }

    [Fact]
    public async Task APressWhileAlreadyListening_InHoldMode_DoesNotOpenASecondMic()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await Put(c, new { hotkey = "F9" });
        host.Keys.Down(0x78);
        host.Hotkeys.Fire();
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Listening));
        host.Hotkeys.Fire();
        await Task.Delay(100);
        Assert.Equal(1, host.Audio.OpenCount);
        host.Keys.Up(0x78);
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Idle));
    }

    [Fact]
    public async Task APressWhoseKeyIsAlreadyUp_DoesNotStartAnything()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await Put(c, new { hotkey = "F9" });
        host.Hotkeys.Fire();   // 눌렀다가 처리되기 전에 이미 뗐다
        await Task.Delay(150);
        Assert.Equal(0, host.Audio.OpenCount);
        Assert.Equal(SttState.Idle, Stt(host).Snapshot().State);
    }

    [Fact]
    public async Task ReleasingTheKey_OnlyEndsTheSessionTheHotkeyStarted()
    {
        // 키를 누르고 있는 사이 세션이 다른 경로로 끝나고 다른 기기가 새로 시작해도, 키를 뗄 때 그 새 세션을 끊으면 안 된다
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await Put(c, new { hotkey = "F9" });
        host.Keys.Down(0x78);
        host.Hotkeys.Fire();
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Listening));

        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/stop"));
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Idle));
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"))).StatusCode);   // 다른 기기가 새로 시작
        var newSession = Stt(host).Snapshot().SessionId;

        host.Keys.Up(0x78);
        await Task.Delay(250);
        Assert.Equal(SttState.Listening, Stt(host).Snapshot().State);
        Assert.Equal(newSession, Stt(host).Snapshot().SessionId);
        Assert.Equal(2, host.Audio.OpenCount);
    }

    [Fact]
    public async Task Stop_WithASessionId_OnlyStopsThatSession()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        var id = Stt(host).Snapshot().SessionId!;
        Assert.False(Stt(host).Stop("다른-세션"));
        Assert.Equal(SttState.Listening, Stt(host).Snapshot().State);
        Assert.True(Stt(host).Stop(id));
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Idle));
    }

    [Fact]
    public void AHotkeyPressedWhileRegistering_DoesNotDeadlock()
    {
        // 등록은 창 스레드의 응답을 기다린다. 그 사이 창 스레드가 단축키 눌림을 처리하다 같은 락을 기다리면 5초 동안 멈춘다 (리뷰 지적).
        using var host = new TestHost();
#pragma warning disable xUnit1031 // 등록이 창 스레드의 응답을 동기로 기다리는 상황을 그대로 재현하려는 의도된 차단이다
        host.Hotkeys.OnRegister = () => Task.Run(() => host.Hotkeys.Fire()).Wait(3000);   // 등록 중에 눌림이 창 스레드에서 들어온다
#pragma warning restore xUnit1031
        var started = DateTime.UtcNow;
        var status = Hotkeys(host).Apply("F9");
        Assert.True(status.Registered);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1.5), "단축키 등록이 눌림 처리를 기다리며 멈췄다");
    }

    [Fact]
    public async Task ConcurrentSettingsChanges_LeaveTheSavedAndRegisteredHotkeyInAgreement()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var keys = new[] { "F5", "F6", "F7", "F8", "F9", "F10" };
        await Task.WhenAll(keys.Select(k => Put(c, new { hotkey = k })));
        var saved = (await TestHost.Data(await c.GetAsync("/api/stt/settings"))).GetProperty("hotkey").GetString();
        Assert.Equal(saved, host.Hotkeys.Current!.Display);   // 저장된 값과 실제 등록된 값이 같다
        Assert.Equal(saved, Hotkeys(host).Status.Key);
    }

    [Fact]
    public async Task AMicFailureWhileStarting_IsReportedAsAFailure_NotAsACancellation()
    {
        using var host = new TestHost();
        host.Audio.OnCaptureStart = () => host.Audio.Current!.Fail(new AudioFailure(AudioFailureKind.Removed, "마이크 연결이 끊겼습니다."));
        var c = await host.LocalAsync();
        var r = await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);   // 200 {cancelled}로 가려지면 단축키 토스트도 성공처럼 보인다
        Assert.Equal("STT_MIC_REMOVED", await TestHost.ErrorCode(r));
        Assert.Equal(SttState.Idle, Stt(host).Snapshot().State);
    }

    [Fact]
    public async Task APressWithNoHotkeyConfigured_DoesNothing()
    {
        using var host = new TestHost();
        await host.LocalAsync();
        host.Hotkeys.Fire();
        await Task.Delay(100);
        Assert.Equal(0, host.Audio.OpenCount);
    }

    [Fact]
    public async Task AHotkeyStartFailure_TellsEveryScreenWithAToast()
    {
        // 전체 화면 게임 중에는 실패를 볼 수 없으니 모든 기기 화면에 알린다
        using var host = new TestHost();
        host.Engines.Installed = false;
        var c = await host.LocalAsync();
        await Put(c, new { hotkey = "F9" });
        var sse = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await sse.Content.ReadAsStreamAsync());

        host.Keys.Down(0x78);
        host.Hotkeys.Fire();
        var toast = await ReadEvent(reader, "toast", 4000);
        Assert.Contains("모델", toast.GetProperty("message").GetString());
        Assert.Equal(0, host.Audio.OpenCount);
    }

    private static async Task<JsonElement> ReadEvent(StreamReader reader, string name, int ms)
    {
        var cts = new CancellationTokenSource(ms);
        string? ev = null;
        while (await reader.ReadLineAsync(cts.Token) is { } line)
        {
            if (line.StartsWith("event:")) ev = line[6..].Trim();
            else if (line.StartsWith("data:") && ev == name) return JsonDocument.Parse(line[5..].Trim()).RootElement.Clone();
        }
        throw new InvalidOperationException("이벤트를 받지 못했습니다: " + name);
    }

    // ── 신호음 ──

    [Fact]
    public async Task StartCue_PlaysBeforeTheMicOpens_SoItIsNotRecognised()
    {
        using var host = new TestHost();
        List<SttCueKind>? atOpen = null;
        host.Audio.OnOpen = () => atOpen = host.Cue.Played.ToList();
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        Assert.Equal(new[] { SttCueKind.Start }, atOpen);   // 마이크를 열 때 시작음은 이미 끝나 있다
    }

    [Fact]
    public async Task DoneCue_PlaysAfterTheMicIsClosed_SoItCannotLeakIntoTheNextSentence()
    {
        using var host = new TestHost();
        host.Cue.Probe = () => host.Audio.Current?.Disposed ?? false;   // 재생하는 순간 마이크가 닫혀 있었는가
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);
        Assert.True(await Eventually(() => host.Cue.Played.Contains(SttCueKind.Done)));
        var i = host.Cue.Played.ToList().IndexOf(SttCueKind.Done);
        Assert.True(host.Cue.ProbeValues[i], "확정 신호음이 마이크가 열린 채로 재생되었다");
    }

    [Fact]
    public async Task NoDoneCue_WhenNothingWasRecognised()
    {
        using var host = new TestHost();
        host.Engines.Utterances = new[] { "" };
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        host.Audio.Current!.PushVoice(3);
        await Task.Delay(100);
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/stop"));
        Assert.True(await Eventually(() => Stt(host).Snapshot().State == SttState.Idle));
        Assert.DoesNotContain(SttCueKind.Done, host.Cue.Played);
    }

    [Fact]
    public async Task CuesCanBeTurnedOff()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await Put(c, new { cue = false });
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        host.Audio.Current!.PushVoice(4);
        await Task.Delay(300);
        Assert.Empty(host.Cue.Played);
    }

    [Fact]
    public void SoundCue_GeneratesAValidShortWav()
    {
        var wav = SoundCue.Tone(880, 90);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(44 + 44100 * 90 / 1000 * 2, wav.Length);
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));   // RIFF 크기 필드가 맞다
        Assert.Equal(0, BitConverter.ToInt16(wav, 44));               // 시작은 0에서(딱 소리 방지)
    }

    // ── 마이크 시험 ──

    [Fact]
    public async Task MicTest_MeasuresTheLevel_AndClassifiesIt()
    {
        using var host = new TestHost();
        host.Audio.OnCaptureStart = () => host.Audio.Current!.PushVoice(3);   // 0.1 진폭 ≈ -20dBFS
        var c = await host.LocalAsync();
        var d = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Post, "/api/stt/devices/test")));
        Assert.Equal("가짜 마이크", d.GetProperty("device").GetString());
        Assert.InRange(d.GetProperty("peakDb").GetDouble(), -21, -19);
        Assert.Equal("ok", d.GetProperty("level").GetString());
        Assert.True(host.Audio.Current!.Disposed);   // 시험이 끝나면 마이크를 닫는다
    }

    [Fact]
    public async Task MicTest_ExactSilence_IsReportedAsNone()
    {
        using var host = new TestHost();
        host.Audio.OnCaptureStart = () => host.Audio.Current!.PushSilence(3);
        var c = await host.LocalAsync();
        var d = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Post, "/api/stt/devices/test")));
        Assert.Equal("none", d.GetProperty("level").GetString());   // 음소거·권한 막힘·연결 문제
    }

    [Fact]
    public async Task MicTest_MapsMicFailures_AndIsPcOnly()
    {
        using var host = new TestHost();
        host.Audio.OpenFailure = new AudioFailure(AudioFailureKind.Denied, "막힘");
        var pc = await host.LocalAsync();
        var r = await pc.SendAsync(Req(HttpMethod.Post, "/api/stt/devices/test"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("STT_MIC_DENIED", await TestHost.ErrorCode(r));

        host.Audio.OpenFailure = null;
        var phone = await host.LanAsync(pc);
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.SendAsync(Req(HttpMethod.Post, "/api/stt/devices/test"))).StatusCode);   // 폰이 PC 마이크를 켜 볼 수 없다
        Assert.Equal(1, host.Audio.OpenCount);
    }

    [Fact]
    public async Task MicTest_WhileListening_IsRefused()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        var r = await c.SendAsync(Req(HttpMethod.Post, "/api/stt/devices/test"));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(1, host.Audio.OpenCount);
    }

    [Fact]
    public async Task StartingToListen_DuringAMicTest_IsRefused_ThenWorksAfterwards()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var test = c.SendAsync(Req(HttpMethod.Post, "/api/stt/devices/test"));
        await Task.Delay(300);   // 시험(1.5초) 도중
        var start = await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"));
        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
        await test;
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"))).StatusCode);
    }

    [Theory]
    [InlineData(-80, "none")]
    [InlineData(-61, "none")]
    [InlineData(-45, "low")]
    [InlineData(-20, "ok")]
    [InlineData(-6, "ok")]
    [InlineData(-0.5, "loud")]
    public void MicLevelClass_Boundaries(double peakDb, string expected) => Assert.Equal(expected, MobiMate.Web.Endpoints.MicLevelClass.Classify(peakDb));

    [Fact]
    public async Task Status_ShowsHotkeyInstallAndCueDefaults()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var s = await TestHost.Data(await c.GetAsync("/api/stt/status"));
        Assert.Equal(JsonValueKind.Null, s.GetProperty("hotkey").GetProperty("key").ValueKind);
        Assert.Equal("hold", s.GetProperty("hotkey").GetProperty("mode").GetString());   // 기본은 누르는 동안 듣기
        Assert.Equal("idle", s.GetProperty("install").GetProperty("phase").GetString());
        Assert.True((await TestHost.Data(await c.GetAsync("/api/stt/settings"))).GetProperty("cue").GetBoolean());   // 신호음 기본 켬
    }
}
