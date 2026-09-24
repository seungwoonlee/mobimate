using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using MobiMate;
using MobiMate.Web.Endpoints;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Security;
using MobiMate.Web.Services;

// 콘텐츠 루트를 실행 파일 폴더로 고정한다. 다른 폴더에서 실행해도 wwwroot를 찾게 하기 위해서다.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

// 설정은 DI에서 늦게 읽는다. 테스트(WebApplicationFactory)가 구성 값을 바꿀 수 있게 하기 위해서다.
builder.Services.Configure<MobiMateOptions>(builder.Configuration.GetSection(MobiMateOptions.Section));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MobiMateOptions>>().Value);

var configuredPort = builder.Configuration.GetValue<int?>($"{MobiMateOptions.Section}:Port") ?? 17800;
var port = PortPicker.FirstFree(configuredPort);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");   // 루프백만 (NFR-03). LAN 엔드포인트는 S3b.

builder.Services.AddSingleton<ILoggerProvider>(sp => new FileLoggerProvider(Path.Combine(sp.GetRequiredService<MobiMateOptions>().StorageDir, "logs")));
// 프레임워크 요청 로그는 URL 전체(쿼리 포함)를 남긴다. 기동 코드·페어링 코드가 파일에 남지 않도록 경고 이상만 쓴다 (NFR-15).
builder.Logging.AddFilter<FileLoggerProvider>("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter<FileLoggerProvider>("System", LogLevel.Warning);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

builder.Services.AddSingleton(new ServerIdentity { Port = port });
builder.Services.AddSingleton(sp => new WebSettingsStore(Dir(sp)));
builder.Services.AddSingleton(sp => new DeviceStore(Dir(sp)));
builder.Services.AddSingleton<BootCodes>();
builder.Services.AddSingleton<PairingService>();
builder.Services.AddSingleton<ILanHosts, NoLanHosts>();

builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<MobiMateOptions>();
    var saved = sp.GetRequiredService<WebSettingsStore>().Current.CliPath;
    return new GameCli(!string.IsNullOrWhiteSpace(o.CliPath) ? o.CliPath : saved);
});
builder.Services.AddSingleton<IGameCli>(sp => sp.GetRequiredService<GameCli>());
builder.Services.AddSingleton<QueryCache>();
builder.Services.AddSingleton<GameQueries>();
builder.Services.AddSingleton<GameStateCache>();
builder.Services.AddSingleton(sp => new SnapshotManager(Dir(sp)));
builder.Services.AddSingleton(_ => HomeworkCatalog.LoadEmbedded());
builder.Services.AddSingleton(sp => new HomeworkStore(Dir(sp)));
builder.Services.AddSingleton(sp => new HomeworkService(sp.GetRequiredService<HomeworkCatalog>(), sp.GetRequiredService<HomeworkStore>()));
builder.Services.AddSingleton<HomeworkWatcher>();
builder.Services.AddSingleton<SseHub>();
builder.Services.AddSingleton<GameViews>();
builder.Services.AddSingleton<GameActions>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton(_ => new AiEngineManager());
builder.Services.AddSingleton(sp => new ChatterLineService(sp.GetRequiredService<AiEngineManager>()));
builder.Services.AddSingleton<AiService>();
builder.Services.AddSingleton<StatusMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StatusMonitor>());
builder.Services.AddHostedService<StartupTasks>();

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    // 보안 헤더 (상세설계 §3.2)
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    h.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; media-src 'self' blob:; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'";
    if (ctx.Request.Path.StartsWithSegments("/api")) h.CacheControl = "no-store";
    await next();
});
app.UseMiddleware<SecurityMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

AuthEndpoints.Map(app);
GameEndpoints.Map(app);
CommsEndpoints.Map(app);

app.Run();

static string Dir(IServiceProvider sp)
{
    var dir = sp.GetRequiredService<MobiMateOptions>().StorageDir;
    Directory.CreateDirectory(dir);
    return dir;
}

public partial class Program { }

namespace MobiMate.Web.Hosting
{
    /// <summary>포트가 사용 중이면 다음 빈 포트를 찾는다 (NFR-04, 로컬 전용 모드).</summary>
    public static class PortPicker
    {
        public static int FirstFree(int start, int tries = 20)
        {
            for (var p = start; p < start + tries; p++)
            {
                try
                {
                    var l = new TcpListener(IPAddress.Loopback, p);
                    l.Start();
                    l.Stop();
                    return p;
                }
                catch (SocketException) { }
            }
            return start;
        }
    }

    /// <summary>기동 작업: WPF판 기록 가져오기(NFR-13), AI 엔진 감지, 브라우저 열기(NFR-01·SEC-06).</summary>
    public sealed class StartupTasks(IServiceProvider sp, MobiMateOptions options, ILogger<StartupTasks> log) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(options.WpfStorageDir) && Directory.Exists(options.WpfStorageDir))
            {
                var copied = sp.GetRequiredService<SnapshotManager>().ImportMissingFrom(options.WpfStorageDir);
                var hw = sp.GetRequiredService<HomeworkStore>().ImportFromWpfIfMissing(options.WpfStorageDir, sp.GetRequiredService<HomeworkCatalog>(), DateTimeOffset.UtcNow);
                if (copied > 0 || hw > 0) log.LogInformation("WPF판 기록 가져옴: 파일 {Files}개, 숙제 캐릭터 {Chars}개", copied, hw);
            }

            _ = Task.Run(() => sp.GetRequiredService<AiService>().EnsureDiscoveredAsync(), CancellationToken.None);

            if (options.OpenBrowser)
            {
                var id = sp.GetRequiredService<ServerIdentity>();
                var code = sp.GetRequiredService<BootCodes>().Issue(DateTimeOffset.UtcNow);
                var url = $"http://127.0.0.1:{id.Port}/auth/boot?code={code}";
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { log.LogWarning(ex, "브라우저를 열지 못했습니다. 직접 여세요: http://127.0.0.1:{Port}", id.Port); }
            }
            log.LogInformation("MobiMate 서버 시작: http://127.0.0.1:{Port}", sp.GetRequiredService<ServerIdentity>().Port);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
