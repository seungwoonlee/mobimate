using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using MobiMate;
using MobiMate.Web.Endpoints;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Lan;
using MobiMate.Web.Services;

// 콘텐츠 루트를 실행 파일 폴더로 고정한다. 다른 폴더에서 실행해도 wwwroot를 찾게 하기 위해서다.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

// 단일 인스턴스 (상세설계 §3.1). 이미 실행 중이면 그쪽에 브라우저 열기를 부탁하고 끝낸다.
// 호스트를 만들기 전이라 테스트 설정은 아직 보이지 않는다. 테스트는 환경변수 MobiMate__SingleInstance=false로 끈다.
using var single = builder.Configuration.GetValue($"{MobiMateOptions.Section}:SingleInstance", true) ? new SingleInstance() : null;
if (single is { IsFirst: false })
{
    SingleInstance.SignalExisting();
    return;
}

// 설정은 DI에서 늦게 읽는다. 테스트(WebApplicationFactory)가 구성 값을 바꿀 수 있게 하기 위해서다.
builder.Services.Configure<MobiMateOptions>(builder.Configuration.GetSection(MobiMateOptions.Section));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MobiMateOptions>>().Value);

var configuredPort = builder.Configuration.GetValue<int?>($"{MobiMateOptions.Section}:Port") ?? 17800;
var port = PortPicker.FirstFree(configuredPort);

// Kestrel 엔드포인트는 전용 설정 공급자에서 읽는다. 루프백은 고정, LAN 엔드포인트는 LanService가 넣고 뺀다 (§3.6).
var kestrelEndpoints = new KestrelEndpointSource();
var kestrelConfig = new ConfigurationBuilder().Add(kestrelEndpoints).Build();
kestrelEndpoints.Provider.SetEndpoints(port, Array.Empty<IPAddress>());
builder.WebHost.ConfigureKestrel(o => o.Configure(kestrelConfig.GetSection("Kestrel"), reloadOnChange: true));
builder.Services.AddSingleton(kestrelEndpoints);

builder.Services.AddSingleton<ILoggerProvider>(sp => new FileLoggerProvider(Path.Combine(sp.GetRequiredService<MobiMateOptions>().StorageDir, "logs")));
// 프레임워크 요청 로그는 URL 전체(쿼리 포함)를 남긴다. 기동 코드·페어링 코드가 파일에 남지 않도록 경고 이상만 쓴다 (NFR-15).
builder.Logging.AddFilter<FileLoggerProvider>("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter<FileLoggerProvider>("System", LogLevel.Warning);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

builder.Services.AddSingleton(new ServerIdentity { Port = port, PreferredPort = configuredPort });
builder.Services.AddSingleton(sp => new WebSettingsStore(Dir(sp)));
builder.Services.AddSingleton<INetworkProfileSource, NlmNetworkProfileSource>();
builder.Services.AddSingleton<IPortProbe, TcpPortProbe>();
builder.Services.AddSingleton<IMdnsAdvertiser, MdnsResponder>();
builder.Services.AddSingleton<LanService>();
builder.Services.AddSingleton<ILanHosts>(sp => sp.GetRequiredService<LanService>());
builder.Services.AddSingleton<BrowserLauncher>();

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
builder.Services.AddSingleton(sp =>
{
    var catalog = CutoffCatalog.Load(Dir(sp));
    foreach (var w in catalog.Warnings) sp.GetRequiredService<ILogger<CutoffCatalog>>().LogWarning("컷오프 기준표: {Warning}", w);
    return catalog;
});
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
builder.Services.AddHostedService(sp => sp.GetRequiredService<LanService>());
builder.Services.AddHostedService<InstancePipeListener>();
builder.Services.AddHostedService<TrayHost>();

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    if (ctx.Request.Path.StartsWithSegments("/api")) h.CacheControl = "no-store";
    else if (ctx.Request.Path.StartsWithSegments("/assets")) h.CacheControl = "public, max-age=31536000, immutable";   // 파일 이름에 해시가 있다
    else h.CacheControl = "no-cache";   // 앱 셸(index.html)은 매번 확인한다: 업그레이드 뒤 옛 화면이 남지 않게
    await next();
});
app.UseMiddleware<LanGate>();
// 앱 셸은 exe에 넣은 파일에서 읽는다 (단일 exe, NFR-02). 폴백 라우트(MapFallbackToFile)도 같은 제공자를 쓴다.
app.Environment.WebRootFileProvider = new EmbeddedWebRoot(typeof(Program).Assembly);
app.UseDefaultFiles();
app.UseStaticFiles();
// 라우팅은 정적 파일 뒤에 둔다. 앞에 있으면(최소 호스팅의 기본 위치) 폴백 라우트가 먼저 선택되어 정적 파일 미들웨어가 건너뛴다.
app.UseRouting();

AuthEndpoints.Map(app);
GameEndpoints.Map(app);
CommsEndpoints.Map(app);

// 앱 셸 라우팅: /stats 같은 화면 경로는 index.html로 돌려준다. /api는 제외(없는 API는 404 그대로).
app.MapFallbackToFile("{*path:regex(^(?!(?i:api/|api$)).*$)}", "index.html")
    .WithMetadata(new Microsoft.AspNetCore.Routing.HttpMethodMetadata(new[] { "GET", "HEAD" }));

try
{
    app.Run();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // 콘솔이 없는 WinExe라 조용히 끝나지 않게 로그와 메시지 상자로 알린다 (상세설계 §3.6)
    app.Logger.LogCritical(ex, "서버를 시작하지 못했습니다.");
    try
    {
        System.Windows.Forms.MessageBox.Show($"MobiMate Web을 시작하지 못했습니다.\n\n{ex.Message}\n\n로그: {Path.Combine(app.Services.GetRequiredService<MobiMateOptions>().StorageDir, "logs")}",
            "MobiMate Web", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
    }
    catch { }
    Environment.ExitCode = 1;
}

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

    /// <summary>기동 작업: WPF판 기록 가져오기(NFR-13), AI 엔진 감지, 브라우저 열기(NFR-01).</summary>
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
                // 기동 작업은 서버가 포트를 열기 전에 돈다. 먼저 열면 첫 접속이 거부되므로 서버가 준비된 뒤에 연다
                sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => sp.GetRequiredService<BrowserLauncher>().Open());
            }
            var id = sp.GetRequiredService<ServerIdentity>();
            log.LogInformation("MobiMate 서버 시작 {Version}: http://127.0.0.1:{Port}", id.Version, id.Port);
            if (id.Port != id.PreferredPort) log.LogWarning("포트 {Preferred}이 사용 중이라 {Port}로 떴습니다. 이 상태에서는 LAN 모드를 켜지 않습니다.", id.PreferredPort, id.Port);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
