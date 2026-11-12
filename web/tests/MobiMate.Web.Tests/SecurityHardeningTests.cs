using System.Net;
using System.Text;
using System.Text.Json;
using MobiMate.Web.Endpoints;
using Xunit;

namespace MobiMate.Web.Tests;

/// <summary>보안 감사(2026-11-12) 후속: CLI 경로 변경 가드, 해석기 거부, LAN 모드 PC 전용, 로그 한 줄화</summary>
public class SecurityHardeningTests
{
    private static HttpRequestMessage Put(string url, object body, bool header = true, string? host = null, string? origin = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Put, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        if (header) r.Headers.Add("X-MobiMate-Req", "1");
        if (host != null) r.Headers.Host = host;
        if (origin != null) r.Headers.Add("Origin", origin);
        return r;
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Windows\System32\mshta.exe")]
    public void Validator_RejectsInterpreters(string path) =>
        Assert.Equal("명령을 해석하는 프로그램은 CLI로 지정할 수 없습니다.", CliPathValidator.Validate(path));

    [Fact]
    public async Task CliPath_FromLoopbackButWithoutTheAppHeader_IsRejected()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var r = await local.SendAsync(Put("/api/settings", new { cliPath = @"C:\Windows\System32\cmd.exe" }, header: false));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);   // 다른 웹페이지가 보낼 수 없는 형식이다
    }

    [Fact]
    public async Task CliPath_WithAForeignHostName_IsRejected_EvenFromLoopback()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var r = await local.SendAsync(Put("/api/settings", new { cliPath = @"C:\Windows\System32\cmd.exe" }, host: "attacker.example:17800"));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);   // DNS 리바인딩: 원격 IP는 루프백이지만 Host가 다르다
        Assert.Equal("HOST_NOT_ALLOWED", await TestHost.ErrorCode(r));
    }

    [Fact]
    public async Task CliPath_RealRequestFromTheApp_StillReachesTheValidator()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var r = await local.SendAsync(Put("/api/settings", new { cliPath = @"C:\Windows\System32\cmd.exe" }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);   // 가드는 통과하고 해석기라서 검증에서 거절
    }

    [Fact]
    public async Task LanMode_CannotBeSwitchedFromAPhone()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var phone = await host.LanAsync(local);
        var r = await phone.SendAsync(Put("/api/lan", new { enabled = false }));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("PC_ONLY", await TestHost.ErrorCode(r));
    }

    [Fact]
    public void ClientErrorLog_LineBreaksBecomeSpaces() =>
        Assert.Equal("a  b c", CommsEndpoints.OneLine("a\r\nb\tc"));
}
