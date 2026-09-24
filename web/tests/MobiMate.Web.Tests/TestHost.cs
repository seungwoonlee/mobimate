using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MobiMate.Web.Hosting;
using MobiMate.Web.Security;

[assembly: CollectionBehavior(DisableTestParallelization = true)]   // 가짜 CLI 환경변수가 프로세스 전역이다

namespace MobiMate.Web.Tests;

/// <summary>
/// 테스트용 서버: 임시 저장 폴더 + 가짜 CLI, WPF 가져오기·브라우저 열기 끔.
/// 요청 헤더 X-Test-Remote-IP로 원격 주소를 흉내 낸다(기본 127.0.0.1 = 게임 PC).
/// </summary>
public sealed class TestHost : WebApplicationFactory<Program>
{
    public const string RemoteIpHeader = "X-Test-Remote-IP";
    public string StorageDir { get; }
    public string FakeCliLog { get; }
    public string StopFile { get; }
    public bool KeepStorage { get; init; }

    public TestHost(string? storageDir = null)
    {
        StorageDir = storageDir ?? Path.Combine(Path.GetTempPath(), "mm-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StorageDir);
        FakeCliLog = Path.Combine(StorageDir, "fakecli.log");
        StopFile = Path.Combine(StorageDir, "stop.flag");
        foreach (var k in new[] { "FAKECLI_DELAY_EXECUTE_GATHERING", "FAKECLI_STATE", "FAKECLI_FAIL_GET_MY_INFO" })
            Environment.SetEnvironmentVariable(k, null);
        Environment.SetEnvironmentVariable("FAKECLI_LOG", FakeCliLog);
        Environment.SetEnvironmentVariable("FAKECLI_STOPFILE", StopFile);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("MobiMate:StorageDir", StorageDir);
        builder.UseSetting("MobiMate:WpfStorageDir", "");
        builder.UseSetting("MobiMate:CliPath", FakeCli.ExePath);
        builder.UseSetting("MobiMate:OpenBrowser", "false");
        builder.UseSetting("MobiMate:SsePingInterval", "00:00:01");
        builder.ConfigureServices(s => s.AddSingleton<IStartupFilter, RemoteIpStartupFilter>());
    }

    /// <summary>서버가 고른 포트. Host·Origin 검사가 포트까지 비교하므로 테스트 요청도 이 포트를 쓴다.</summary>
    public int Port => Services.GetRequiredService<ServerIdentity>().Port;
    public string Origin => $"http://localhost:{Port}";

    /// <summary>세션 없는 클라이언트 (쿠키 보관, 자동 리디렉트 안 함).</summary>
    public HttpClient Anonymous(string? remoteIp = null)
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri(Origin) });
        c.DefaultRequestHeaders.Add("Origin", Origin);
        if (remoteIp != null) c.DefaultRequestHeaders.Add(RemoteIpHeader, remoteIp);
        return c;
    }

    /// <summary>게임 PC 브라우저: 기동 코드로 세션을 받고 CSRF 헤더를 붙인다.</summary>
    public async Task<HttpClient> LocalAsync()
    {
        var c = Anonymous();
        var code = Services.GetRequiredService<BootCodes>().Issue(DateTimeOffset.UtcNow);
        var boot = await c.GetAsync($"/auth/boot?code={code}");
        Assert.Equal(HttpStatusCode.Redirect, boot.StatusCode);
        await AttachCsrfAsync(c);
        return c;
    }

    /// <summary>LAN 기기(폰): 게임 PC가 페어링을 시작하고, 기기가 코드로 확인한다.</summary>
    public async Task<HttpClient> LanAsync(HttpClient local, string ip = "192.168.0.50")
    {
        var code = (await Data(await local.PostAsync("/api/pairing/start", null))).GetProperty("code").GetString();
        var c = Anonymous(ip);
        var r = await c.PostAsJsonAsync("/api/pairing/confirm", new { code, deviceName = "테스트 폰" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await AttachCsrfAsync(c);
        return c;
    }

    public static async Task AttachCsrfAsync(HttpClient c)
    {
        var s = await Data(await c.GetAsync("/api/session"));
        c.DefaultRequestHeaders.Remove(SecurityMiddleware.CsrfHeader);
        c.DefaultRequestHeaders.Add(SecurityMiddleware.CsrfHeader, s.GetProperty("csrf").GetString());
    }

    public static async Task<JsonElement> Data(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    public static async Task<string> ErrorCode(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    public IReadOnlyList<string> CliCalls(string phase = "start") =>
        File.Exists(FakeCliLog)
            ? File.ReadAllLines(FakeCliLog).Select(l => l.Split('\t')).Where(p => p.Length > 3 && p[2] == phase).Select(p => p[3] + (p.Length > 4 && p[4].Length > 0 ? " " + p[4] : "")).ToList()
            : Array.Empty<string>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!KeepStorage) try { Directory.Delete(StorageDir, true); } catch { }
    }

    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use(async (HttpContext ctx, Func<Task> n) =>
            {
                ctx.Connection.RemoteIpAddress = ctx.Request.Headers.TryGetValue(RemoteIpHeader, out var v) ? IPAddress.Parse(v!) : IPAddress.Loopback;
                await n();
            });
            next(app);
        };
    }
}

/// <summary>빌드된 가짜 CLI 위치: tests/MobiMate.Web.Tests/bin/{Config}/{tfm} → tools/FakeCli/bin/{Config}/net8.0.</summary>
public static class FakeCli
{
    public static string ExePath { get; } = Locate();

    private static string Locate()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var config = Path.GetFileName(Path.GetDirectoryName(baseDir)!);
        var dir = new DirectoryInfo(baseDir);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "FakeCli"))) dir = dir.Parent;
        var exe = Path.Combine(dir!.FullName, "tools", "FakeCli", "bin", config, "net8.0", "MabinogiMobile_CLI.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("가짜 CLI가 빌드되지 않았습니다.", exe);
        return exe;
    }
}
