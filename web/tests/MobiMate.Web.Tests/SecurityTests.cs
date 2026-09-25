using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MobiMate.Web.Security;

namespace MobiMate.Web.Tests;

/// <summary>TST-03 보안 행렬 (SEC-01~10).</summary>
public class SecurityTests
{
    [Fact]
    public async Task NoSession_Returns401_ButPingAndAppShellAreOpen()
    {
        using var host = new TestHost();
        var c = host.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/header")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task AppShellRoutes_FallBackToIndex_ButUnknownApiIs404()
    {
        using var host = new TestHost();
        var c = host.Anonymous();
        var page = await c.GetAsync("/homework?tab=daily");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.True(page.Headers.CacheControl?.NoCache, "앱 셸은 no-cache");
        var local = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await local.GetAsync("/api/does-not-exist")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await local.GetAsync("/API/Does-Not-Exist")).StatusCode);   // 대소문자 무관
        Assert.NotEqual("text/html", (await local.PostAsync("/homework", null)).Content.Headers.ContentType?.MediaType);   // 폴백은 GET·HEAD만
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/does-not-exist")).StatusCode);   // 세션 검사가 먼저
    }

    [Fact]
    public async Task ForeignHost_Returns421()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        req.Headers.Host = "attacker.example";   // DNS 리바인딩
        var r = await c.SendAsync(req);
        Assert.Equal((HttpStatusCode)421, r.StatusCode);
    }

    [Fact]
    public async Task HostWithOtherPort_Returns421()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        req.Headers.Host = $"localhost:{host.Port + 1}";
        Assert.Equal((HttpStatusCode)421, (await c.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task OriginFromOtherLocalPort_Returns403_EvenOnCsrfExemptPairing()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        c.DefaultRequestHeaders.Remove("Origin");
        c.DefaultRequestHeaders.Add("Origin", "http://localhost:3000");   // 같은 PC의 다른 로컬 서버
        var stop = await c.PostAsync("/api/actions/stop", null);
        Assert.Equal(HttpStatusCode.Forbidden, stop.StatusCode);
        Assert.Equal("ORIGIN_NOT_ALLOWED", await TestHost.ErrorCode(stop));

        var anon = host.Anonymous();
        anon.DefaultRequestHeaders.Remove("Origin");
        anon.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:3000");
        var confirm = await anon.PostAsJsonAsync("/api/pairing/confirm", new { code = "000000" });
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
    }

    [Theory]
    [InlineData("/API/PING")]
    [InlineData("/api/ping/")]
    public async Task ExemptPath_CaseAndTrailingSlashVariants_StayOpen(string path)
    {
        using var host = new TestHost();
        Assert.Equal(HttpStatusCode.OK, (await host.Anonymous().GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("http://attacker.example")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task StateChange_WithForeignOrMissingOrigin_Returns403(string? origin)
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/actions/stop");
        req.Headers.Remove("Origin");
        if (origin != null) req.Headers.TryAddWithoutValidation("Origin", origin);
        c.DefaultRequestHeaders.Remove("Origin");
        var r = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("ORIGIN_NOT_ALLOWED", await TestHost.ErrorCode(r));
    }

    [Fact]
    public async Task StateChange_WithoutCsrf_Returns403()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        c.DefaultRequestHeaders.Remove(SecurityMiddleware.CsrfHeader);
        var r = await c.PostAsync("/api/actions/stop", null);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("CSRF", await TestHost.ErrorCode(r));
        Assert.DoesNotContain(host.CliCalls(), x => x.StartsWith("stop_action"));
    }

    [Fact]
    public async Task BootCode_IsOneTime_AndRequiresLoopback()
    {
        using var host = new TestHost();
        var code = host.Services.GetRequiredService<BootCodes>().Issue(DateTimeOffset.UtcNow);
        var lan = host.Anonymous("192.168.0.77");
        Assert.Equal(HttpStatusCode.Unauthorized, (await lan.GetAsync($"/auth/boot?code={code}")).StatusCode);

        var code2 = host.Services.GetRequiredService<BootCodes>().Issue(DateTimeOffset.UtcNow);
        var a = host.Anonymous();
        var first = await a.GetAsync($"/auth/boot?code={code2}");
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal("/", first.Headers.Location?.ToString());   // 코드는 URL에서 지워진다
        var b = host.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync($"/auth/boot?code={code2}")).StatusCode);
    }

    [Fact]
    public async Task LanDevice_CannotUseLoopbackOnlyEndpoints()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var phone = await host.LanAsync(local);

        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.PostAsync("/api/pairing/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.GetAsync("/api/pairing/devices")).StatusCode);
        var r = await phone.PutAsJsonAsync("/api/settings", new { cliPath = FakeCli.ExePath });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("LOOPBACK_ONLY", await TestHost.ErrorCode(r));
    }

    [Fact]
    public async Task PairingCode_IsOneTime_AndLocksAfterFiveFailures()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        await host.EnableLanAsync(local);
        var code = (await TestHost.Data(await local.PostAsync("/api/pairing/start", null))).GetProperty("code").GetString();

        var ok = await host.Anonymous("192.168.0.51").PostAsJsonAsync("/api/pairing/confirm", new { code });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var again = await host.Anonymous("192.168.0.52").PostAsJsonAsync("/api/pairing/confirm", new { code });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);   // 1회용

        await local.PostAsync("/api/pairing/start", null);
        var attacker = host.Anonymous("192.168.0.66");
        HttpResponseMessage last = null!;
        for (var i = 0; i < 5; i++) last = await attacker.PostAsJsonAsync("/api/pairing/confirm", new { code = $"00000{i}" });
        Assert.Equal((HttpStatusCode)429, last.StatusCode);
        Assert.Equal("PAIRING_LOCKED", await TestHost.ErrorCode(last));
    }

    [Fact]
    public async Task RevokedDevice_Is401_AndItsSseStreamCloses()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var phone = await host.LanAsync(local);
        var phoneId = (await TestHost.Data(await phone.GetAsync("/api/session"))).GetProperty("deviceId").GetString();

        var sse = await phone.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, sse.StatusCode);
        var stream = await sse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        Assert.Equal("event: hello", await reader.ReadLineAsync());

        Assert.Equal(HttpStatusCode.OK, (await local.DeleteAsync($"/api/pairing/devices/{phoneId}")).StatusCode);

        var readToEnd = reader.ReadToEndAsync();
        Assert.Same(readToEnd, await Task.WhenAny(readToEnd, Task.Delay(5000)));   // 스트림이 즉시 닫힌다
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task DeviceToken_SurvivesServerRestart_AndIsStoredOnlyAsHash()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-web-restart-" + Guid.NewGuid().ToString("N"));
        string cookie;
        using (var first = new TestHost(dir) { KeepStorage = true })
        {
            var c = first.Anonymous();
            var code = first.Services.GetRequiredService<BootCodes>().Issue(DateTimeOffset.UtcNow);
            var boot = await c.GetAsync($"/auth/boot?code={code}");
            cookie = boot.Headers.GetValues("Set-Cookie").First().Split(';')[0];
        }
        var token = cookie.Split('=', 2)[1];
        Assert.DoesNotContain(token, File.ReadAllText(Path.Combine(dir, DeviceStore.FileName)));

        using var second = new TestHost(dir);
        var again = second.Anonymous();
        again.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/session")).StatusCode);
    }

    [Theory]
    [InlineData(@"\\attacker\share\evil.exe")]
    [InlineData(@"relative\cli.exe")]
    [InlineData(@"C:\Windows\notepad.txt")]
    [InlineData(@"C:\없는\경로\MabinogiMobile_CLI.exe")]
    public async Task CliPath_RejectsUncRelativeNonExeAndMissing(string path)
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var r = await c.PutAsJsonAsync("/api/settings", new { cliPath = path });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task CliPath_AcceptsExistingLocalExe()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var d = await TestHost.Data(await c.PutAsJsonAsync("/api/settings", new { cliPath = FakeCli.ExePath }));
        Assert.True(d.GetProperty("cliAvailable").GetBoolean());
    }

    [Fact]
    public async Task SecurityHeaders_ArePresent()
    {
        using var host = new TestHost();
        var r = await host.Anonymous().GetAsync("/api/ping");
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
    }
}
