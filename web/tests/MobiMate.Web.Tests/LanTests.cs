using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using MobiMate.Web.Hosting;
using MobiMate.Web.Lan;

namespace MobiMate.Web.Tests;

/// <summary>LAN 모드 (NFR-03·04, 상세설계 §3.6). 바인딩 자체는 실행 스모크로 확인한다.</summary>
public class LanTests
{
    [Fact]
    public async Task LanOff_ShareIs409_AndLanHostIs421()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var r = await local.GetAsync("/api/lan/share");
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("LAN_OFF", await TestHost.ErrorCode(r));
        Assert.Equal((HttpStatusCode)421, (await host.LanClient().GetAsync("/api/ping")).StatusCode);
    }

    [Fact]
    public async Task EnableLan_ExposesHosts_AndShareUrls()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        await host.EnableLanAsync(local);

        var meta = await TestHost.Data(await local.GetAsync("/api/meta"));
        var lan = meta.GetProperty("lan");
        Assert.True(lan.GetProperty("active").GetBoolean());
        Assert.Equal(TestHost.LanIp, lan.GetProperty("hosts")[0].GetString());
        Assert.Equal("mobimate.local", lan.GetProperty("mdnsName").GetString());

        var p = await TestHost.Data(await local.GetAsync("/api/lan/share"));
        Assert.Equal($"http://{TestHost.LanIp}:{host.Port}/?n=mobimate.local", p.GetProperty("urlIp").GetString());
        Assert.Equal($"http://mobimate.local:{host.Port}/", p.GetProperty("urlName").GetString());

        Assert.Equal(HttpStatusCode.OK, (await host.LanClient().GetAsync("/api/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.LanClient(host: "mobimate.local").GetAsync("/api/ping")).StatusCode);
    }

    [Fact]
    public async Task Ping_AllowsCrossOriginForNameCheck()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        await host.EnableLanAsync(local);

        // IP 주소로 연 페이지가 이름 주소의 /api/ping을 확인하는 경우 (FR-MB-13): 어느 출처든 읽을 수 있다
        var ping = await host.LanClient(host: "mobimate.local").GetAsync("/api/ping");
        Assert.Equal("*", ping.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task PhoneSession_IsLan_AndPcSession_IsLocal()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var phone = await host.LanAsync(local);
        Assert.Equal("local", (await TestHost.Data(await local.GetAsync("/api/session"))).GetProperty("kind").GetString());
        Assert.Equal("lan", (await TestHost.Data(await phone.GetAsync("/api/session"))).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task CliPath_CanOnlyBeChangedFromThePc()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var phone = await host.LanAsync(local);
        var r = await phone.PutAsJsonAsync("/api/settings", new { cliPath = @"C:\x\MabinogiMobile_CLI.exe" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("LOOPBACK_ONLY", await TestHost.ErrorCode(r));
    }

    [Fact]
    public async Task NetworkTurnsPublic_LanHostIs421_AndLanSseClosesImmediately()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var phone = await host.LanAsync(local);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/session")).StatusCode);

        var sse = await phone.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await sse.Content.ReadAsStreamAsync());
        Assert.Equal("event: hello", await reader.ReadLineAsync());

        host.Network.Addresses = Array.Empty<LanAddress>();   // 카페 와이파이(공용)로 바뀜
        var s = await host.Services.GetRequiredService<LanService>().ReconcileAsync();
        Assert.False(s.Active);
        Assert.Contains(s.Error, new[] { LanError.NotPrivate, LanError.NoAddress });

        var readToEnd = reader.ReadToEndAsync();
        Assert.Same(readToEnd, await Task.WhenAny(readToEnd, Task.Delay(5000)));
        Assert.Equal((HttpStatusCode)421, (await phone.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await local.GetAsync("/api/session")).StatusCode);   // PC 브라우저는 그대로
    }

    [Fact]
    public async Task NewAddress_NeedsTwoConsecutivePrivateVerdicts()
    {
        using var host = new TestHost();
        var lan = host.Services.GetRequiredService<LanService>();
        Assert.True(host.Services.GetRequiredService<WebSettingsStore>().Update(s => s.LanEnabled = true));

        var first = await lan.ReconcileAsync();
        Assert.False(first.Active);
        Assert.Equal(LanError.Pending, first.Error);
        var second = await lan.ReconcileAsync();
        Assert.True(second.Active);

        // 네트워크 변경 이벤트: 즉시 내리고 판정하지 않는다. 그 뒤 두 번 연속 판정해야 올린다
        var down = await lan.ReconcileAsync(networkChanged: true);
        Assert.False(down.Active);
        Assert.Equal(LanError.Pending, (await lan.ReconcileAsync()).Error);
        Assert.True((await lan.ReconcileAsync()).Active);
    }

    [Fact]
    public async Task OneOfTwoAddressesLeaves_OtherKeepsSse_AndMdnsNameIsRepublished()
    {
        using var host = new TestHost();
        var second = new LanAddress(IPAddress.Parse("192.168.0.24"), 8, "Ethernet 2");
        host.Network.Addresses = new[] { host.Network.Addresses![0], second };
        var local = await host.LocalAsync();
        var phone = await host.LanAsync(local);   // 192.168.0.23으로 접속
        var sse = await phone.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await sse.Content.ReadAsStreamAsync());
        Assert.Equal("event: hello", await reader.ReadLineAsync());

        host.Mdns.NextName = "mobimate-2.local";   // 재광고 때 이름이 바뀌는 경우
        host.Network.Addresses = new[] { host.Network.Addresses[0] };   // .24만 빠짐
        var s = await host.Services.GetRequiredService<LanService>().ReconcileAsync();
        Assert.True(s.Active);
        Assert.Equal(new[] { TestHost.LanIp }, s.Addresses);
        Assert.Equal("mobimate-2.local", s.MdnsName);

        var lanHosts = host.Services.GetRequiredService<ILanHosts>().Current;
        Assert.Contains("mobimate-2.local", lanHosts);
        Assert.DoesNotContain("mobimate.local", lanHosts);
        // .23의 SSE는 그대로: 이벤트(state.changed·ping)가 계속 오고, 스트림 끝(null)이 아니다
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(line);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task ExceptionWhileBringingUp_RollsBackKestrelEndpoints()
    {
        using var host = new TestHost();
        host.Probe.Throw = true;
        var local = await host.LocalAsync();
        var s = await TestHost.Data(await local.PutAsJsonAsync("/api/lan", new { enabled = true }));
        Assert.Equal("BindFailed", s.GetProperty("error").GetString());
        Assert.Empty(host.Services.GetRequiredService<KestrelEndpointSource>().Provider.Lan);   // 설정이 되돌려짐

        host.Probe.Throw = false;   // 다음 판정에서 정상적으로 올라온다
        var lan = host.Services.GetRequiredService<LanService>();
        await lan.ReconcileAsync();
        Assert.True((await lan.ReconcileAsync()).Active);
        Assert.Single(host.Services.GetRequiredService<KestrelEndpointSource>().Provider.Lan);
    }

    [Fact]
    public async Task ProfileUnknown_DoesNotOpenLan()
    {
        using var host = new TestHost();
        host.Network.Addresses = null;   // NLM COM 실패
        var local = await host.LocalAsync();
        var s = await TestHost.Data(await local.PutAsJsonAsync("/api/lan", new { enabled = true }));
        Assert.False(s.GetProperty("active").GetBoolean());
        Assert.Equal("ProfileUnknown", s.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(ProbeResult.InUse, true, "PortInUse")]
    [InlineData(ProbeResult.Reserved, true, "PortReserved")]
    [InlineData(ProbeResult.Free, false, "BindFailed")]
    public async Task PortProblems_KeepLanClosed_WithReason(ProbeResult probe, bool listening, string error)
    {
        using var host = new TestHost();
        host.Probe.Result = probe;
        host.Probe.Listening = listening;
        var local = await host.LocalAsync();
        var s = await TestHost.Data(await local.PutAsJsonAsync("/api/lan", new { enabled = true }));
        Assert.False(s.GetProperty("active").GetBoolean());
        Assert.Equal(error, s.GetProperty("error").GetString());
        Assert.Equal((HttpStatusCode)421, (await host.LanClient().GetAsync("/api/ping")).StatusCode);
    }

    [Fact]
    public async Task FallbackPort_RefusesLan()
    {
        using var host = new TestHost();
        var id = host.Services.GetRequiredService<ServerIdentity>();
        id.PreferredPort = id.Port + 1;   // 기동 때 설정 포트가 사용 중이라 다른 포트로 뜬 상태
        var local = await host.LocalAsync();
        var s = await TestHost.Data(await local.PutAsJsonAsync("/api/lan", new { enabled = true }));
        Assert.Equal("PortChanged", s.GetProperty("error").GetString());
    }

    [Fact]
    public void KestrelEndpoints_ReloadOnlyWhenChanged()
    {
        var src = new KestrelEndpointSource();
        var root = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Add(src).Build();
        var reloads = 0;
        ChangeToken.OnChange(root.GetReloadToken, () => reloads++);

        src.Provider.SetEndpoints(17800, Array.Empty<IPAddress>());
        Assert.Equal("http://127.0.0.1:17800", root["Kestrel:Endpoints:Loopback:Url"]);
        Assert.Null(root["Kestrel:Endpoints:Lan_192_168_0_23:Url"]);
        src.Provider.SetEndpoints(17800, new[] { IPAddress.Parse("192.168.0.23") });
        Assert.Equal("http://192.168.0.23:17800", root["Kestrel:Endpoints:Lan_192_168_0_23:Url"]);
        src.Provider.SetEndpoints(17800, new[] { IPAddress.Parse("192.168.0.23") });   // 같은 값: 리로드 없음
        src.Provider.SetEndpoints(17800, Array.Empty<IPAddress>());
        Assert.Null(root["Kestrel:Endpoints:Lan_192_168_0_23:Url"]);
        Assert.Equal(3, reloads);
    }

    /// <summary>실제 PC의 NLM COM 호출 (vtable 선언 검증). 결과 목록은 환경마다 다르므로 null이 아닌지만 본다.</summary>
    [Fact]
    public void Nlm_RealComCall_Succeeds()
    {
        var src = new NlmNetworkProfileSource(Microsoft.Extensions.Logging.Abstractions.NullLogger<NlmNetworkProfileSource>.Instance);
        Assert.NotNull(src.PrivateAddresses());
    }
}
