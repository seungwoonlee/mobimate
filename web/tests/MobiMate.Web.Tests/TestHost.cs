using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MobiMate.Web.Hosting;
using MobiMate.Web.Lan;
using MobiMate.Tests;

[assembly: CollectionBehavior(DisableTestParallelization = true)]   // 가짜 CLI 환경변수가 프로세스 전역이다

namespace MobiMate.Web.Tests;

/// <summary>
/// 테스트용 서버: 임시 저장 폴더 + 가짜 CLI, WPF 가져오기·브라우저 열기 끔.
/// 인증이 없으므로(요구사양 Q8) 클라이언트는 그냥 만들어 쓴다. 요청 헤더 X-Test-Remote-IP로 원격 주소를 흉내 낸다(기본 127.0.0.1 = 게임 PC).
/// </summary>
public sealed class TestHost : WebApplicationFactory<Program>
{
    public const string RemoteIpHeader = "X-Test-Remote-IP";
    public const string LocalIpHeader = "X-Test-Local-IP";
    public string StorageDir { get; }
    public string FakeCliLog { get; }
    public string StopFile { get; }
    public bool KeepStorage { get; init; }

    /// <summary>가짜 네트워크: 기본은 개인 네트워크 192.168.0.23 하나.</summary>
    public FakeNetwork Network { get; } = new();
    public FakePortProbe Probe { get; } = new();
    public FakeMdns Mdns { get; } = new();
    public const string LanIp = "192.168.0.23";

    public TestHost(string? storageDir = null)
    {
        StorageDir = storageDir ?? Path.Combine(Path.GetTempPath(), "mm-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StorageDir);
        FakeCliLog = Path.Combine(StorageDir, "fakecli.log");
        StopFile = Path.Combine(StorageDir, "stop.flag");
        foreach (var k in new[] { "FAKECLI_DELAY_EXECUTE_GATHERING", "FAKECLI_STATE", "FAKECLI_FAIL_GET_MY_INFO" })
            Environment.SetEnvironmentVariable(k, null);
        Environment.SetEnvironmentVariable("FAKECLI_LOG", FakeCliLog);
        // 호스트 빌드 전에 읽는 값은 환경변수로만 바꿀 수 있다: 단일 인스턴스 끄기, 실제 포트와 겹치지 않는 포트
        Environment.SetEnvironmentVariable("MobiMate__SingleInstance", "false");
        Environment.SetEnvironmentVariable("MobiMate__Port", FreeLoopbackPort().ToString());
        Environment.SetEnvironmentVariable("FAKECLI_STOPFILE", StopFile);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("MobiMate:StorageDir", StorageDir);
        builder.UseSetting("MobiMate:WpfStorageDir", "");
        builder.UseSetting("MobiMate:CliPath", FakeCli.ExePath);
        builder.UseSetting("MobiMate:OpenBrowser", "false");
        builder.UseSetting("MobiMate:SsePingInterval", "00:00:01");
        builder.UseSetting("MobiMate:Tray", "false");
        builder.UseSetting("MobiMate:RecordInterval", "00:00:00");   // 자동 기록은 끈다 (테스트가 직접 부른다)
        builder.UseSetting("MobiMate:LanConfirmDelay", "00:00:00");
        builder.ConfigureServices(s => s.AddSingleton<IStartupFilter, RemoteIpStartupFilter>());
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<INetworkProfileSource>(Network);
            s.AddSingleton<IPortProbe>(Probe);
            s.AddSingleton<IMdnsAdvertiser>(Mdns);
        });
    }

    /// <summary>서버가 고른 포트.</summary>
    public int Port => Services.GetRequiredService<ServerIdentity>().Port;
    public string Origin => $"http://localhost:{Port}";

    // 무작위 포트가 이 PC에서 이미 쓰이면 서버가 다음 포트로 뜨고(PreferredPort ≠ Port) LAN이 PortChanged로 막힌다.
    // 서버와 같은 방식(루프백 바인드)으로 비어 있는 포트만 고른다.
    private static int FreeLoopbackPort()
    {
        for (var i = 0; i < 50; i++)
        {
            var p = Random.Shared.Next(20000, 60000);
            try
            {
                var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, p);
                l.Start();
                l.Stop();
                return p;
            }
            catch (System.Net.Sockets.SocketException) { }
        }
        throw new InvalidOperationException("비어 있는 루프백 포트를 찾지 못했습니다.");
    }

    /// <summary>게임 PC 브라우저 (루프백으로 접속, 자동 리디렉트 안 함).</summary>
    public Task<HttpClient> LocalAsync() =>
        Task.FromResult(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri(Origin) }));

    /// <summary>게임 PC에서 LAN 모드를 켠다 (가짜 네트워크).</summary>
    public async Task EnableLanAsync(HttpClient local)
    {
        var s = await Data(await local.PutAsJsonAsync("/api/lan", new { enabled = true }));
        Assert.True(s.GetProperty("active").GetBoolean(), s.ToString());
    }

    /// <summary>LAN 주소로 접속하는 기기 (Host = LAN IP:포트, 받은 로컬 주소 = LAN IP).</summary>
    public HttpClient LanClient(string remoteIp = "192.168.0.50", string host = LanIp)
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"http://{host}:{Port}") });
        c.DefaultRequestHeaders.Add(RemoteIpHeader, remoteIp);
        return c;
    }

    /// <summary>LAN 기기(폰): 게임 PC가 LAN을 켜 두면 기기가 LAN 주소로 바로 접속한다.</summary>
    public async Task<HttpClient> LanAsync(HttpClient local, string ip = "192.168.0.50")
    {
        if (!Services.GetRequiredService<LanService>().Status.Active) await EnableLanAsync(local);
        return LanClient(ip);
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
        FakeCliLogReader.ReadLines(FakeCliLog).Select(l => l.Split('\t')).Where(p => p.Length > 3 && p[2] == phase).Select(p => p[3] + (p.Length > 4 && p[4].Length > 0 ? " " + p[4] : "")).ToList();

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
                // 받은 로컬 주소: Host가 IP면 그 주소로 들어온 연결로 본다 (Kestrel이면 실제 바인딩 주소)
                ctx.Connection.LocalIpAddress = ctx.Request.Headers.TryGetValue(LocalIpHeader, out var l) ? IPAddress.Parse(l!)
                    : IPAddress.TryParse(ctx.Request.Host.Host, out var local) ? local : IPAddress.Loopback;
                await n();
            });
            next(app);
        };
    }
}

public sealed class FakeNetwork : INetworkProfileSource
{
    public volatile IReadOnlyList<LanAddress>? Addresses = new[] { new LanAddress(IPAddress.Parse(TestHost.LanIp), 7, "Wi-Fi") };
    public IReadOnlyList<LanAddress>? PrivateAddresses() => Addresses;
}

public sealed class FakePortProbe : IPortProbe
{
    public ProbeResult Result = ProbeResult.Free;
    public bool Listening = true;
    public bool Throw;
    public ProbeResult CanBind(IPAddress address, int port) => Result;
    public Task<bool> IsListeningAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken ct) =>
        Throw ? throw new OperationCanceledException("테스트: 확인 도중 취소") : Task.FromResult(Listening);
}

public sealed class FakeMdns : IMdnsAdvertiser
{
    public string? CurrentName { get; private set; }
    public string NextName = "mobimate.local";
    public int Starts;
    public Task<string?> StartAsync(IReadOnlyList<LanAddress> addresses, CancellationToken ct)
    {
        Starts++;
        CurrentName = NextName;
        return Task.FromResult<string?>(CurrentName);
    }
    public Task StopAsync()
    {
        CurrentName = null;
        return Task.CompletedTask;
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
