using System.Windows;
using System.Windows.Threading;
using MobiMateHomeWork.Hosting;
using MobiMateHomeWork.Services;

namespace MobiMateHomeWork;

/// <summary>
/// 앱 시작·종료. 순서(웹앱 Program.cs와 같은 사정):
/// 구성 읽기(환경변수·실행 인자) → 로그 → 단일 인스턴스 → 옛 웹앱 확인(<b>저장소를 만들기 전에</b>) → Generic Host 시작 → 창.
/// 서비스는 Host가 소유한다. 종료는 <see cref="Quit"/> 한 길로 모은다(트레이 "종료"도 이것을 부른다):
/// Host.StopAsync가 끝난 뒤에 Application.Shutdown을 부르므로 백그라운드 서비스가 확실히 멈춘다.
/// </summary>
public partial class App : Application
{
    /// <summary>환경변수 MobiMate__SingleInstance=false / --single-instance false 로 끈다(검증용).</summary>
    private SingleInstance? _single;
    private IHost? _host;
    private ILogger _log = null!;
    private FileLoggerProvider? _logProvider;
    private MainWindow? _window;
    private int _quitting;
    private int _exitCode;

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("서비스가 아직 시작되지 않았습니다.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;

        try
        {
            await StartCoreAsync(e.Args);
        }
        catch (Exception ex)
        {
            // 콘솔이 없는 WinExe라 조용히 끝나지 않게 로그와 메시지 상자로 알린다
            _log?.LogCritical(ex, "숙제 앱을 시작하지 못했습니다.");
            try { MessageBox.Show($"MobiMate 숙제를 시작하지 못했습니다.\n\n{ex.Message}\n\n로그: {_logProvider?.LogDirectory}", "MobiMate 숙제", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch { }
            _exitCode = 1;
            await StopHostAsync();
            Shutdown(1);
        }
    }

    private async Task StartCoreAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.AddCommandLine(args, new Dictionary<string, string>(AppOptions.SwitchMappings));
        var options = builder.Configuration.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();
        options.StorageDir = string.IsNullOrWhiteSpace(options.StorageDir) ? SnapshotManager.DefaultStorageDirectory : Path.GetFullPath(options.StorageDir);
        Directory.CreateDirectory(options.StorageDir);

        // 로그: 웹앱과 같은 logs 폴더, 파일 이름은 homework-*.log
        _logProvider = new FileLoggerProvider(Path.Combine(options.StorageDir, "logs"));
        _log = _logProvider.CreateLogger("MobiMateHomeWork");
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);
        builder.Logging.AddProvider(_logProvider);

        // 단일 인스턴스: 이미 실행 중이면 기존 창을 앞으로 가져오게 신호만 보내고 끝낸다
        if (options.SingleInstance)
        {
            _single = new SingleInstance();
            if (!_single.IsFirst)
            {
                _log.LogInformation("이미 실행 중인 숙제 앱이 있어 기존 창을 앞으로 가져오고 종료합니다.");
                SingleInstance.SignalExisting();
                _single.Dispose();
                _single = null;
                Shutdown(0);
                return;
            }
            _single.Listen(() =>
            {
                _log.LogInformation("다른 실행의 요청으로 창을 앞으로 가져옵니다.");
                Dispatcher.BeginInvoke(new Action(ShowMainWindow));
            });
        }

        var events = new AppEvents();

        // 옛 웹앱 확인: 이 앱이 저장소(=공유 뮤텍스)를 만들기 전에 해야 한다. 공유 저장 패치 이전의 웹앱은 뮤텍스를 만들지 않는다.
        if (options.LegacyWebCheck && await LegacyWebGuard.IsLegacyWebRunningAsync(options.StorageDir))
        {
            const string msg = "공유 저장 패치 이전의 웹앱(MobiMateWeb)이 실행 중입니다. 같이 쓰면 숙제·캐릭터 기록이 서로 덮어써질 수 있습니다. 웹앱을 최신 버전으로 바꿔 주세요.";
            _log.LogWarning("{Message}", msg);
            events.RaiseWarning(new AppWarning(AppWarningKind.LegacyWebApp, msg));
        }

        ConfigureServices(builder.Services, options, events);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));   // 종료가 오래 걸리면 기다리지 않는다

        _host = builder.Build();
        // 호스트는 UI 스레드의 동기화 컨텍스트 밖에서 시작한다: 백그라운드 서비스의 await 이어가기가 UI 스레드에서 돌면 화면이 멈춘다
        await Task.Run(() => _host.StartAsync());

        _window = new MainWindow(_host.Services);
        MainWindow = _window;
        _window.Show();
    }

    private static void ConfigureServices(IServiceCollection s, AppOptions options, AppEvents events)
    {
        s.AddSingleton(options);
        s.AddSingleton(events);
        s.AddSingleton(sp => new AppSettingsStore(options.StorageDir, sp.GetRequiredService<ILogger<AppSettingsStore>>()));

        s.AddSingleton(sp => new GameCli(AppSettingsStore.ResolveCliPath(options, sp.GetRequiredService<AppSettingsStore>())));
        s.AddSingleton<IGameCli>(sp => sp.GetRequiredService<GameCli>());
        s.AddSingleton<QueryCache>();
        s.AddSingleton<GameQueries>();
        s.AddSingleton<GameStateCache>();
        s.AddSingleton(_ => new SnapshotManager(options.StorageDir));
        s.AddSingleton(_ => new AccountStore(options.StorageDir));
        s.AddSingleton(_ => HomeworkCatalog.LoadEmbedded());
        s.AddSingleton(_ => new HomeworkStore(options.StorageDir));
        s.AddSingleton(sp => new HomeworkService(sp.GetRequiredService<HomeworkCatalog>(), sp.GetRequiredService<HomeworkStore>()));
        s.AddSingleton<CombatScoreGuard>();
        s.AddSingleton<HomeworkWatcher>();
        s.AddSingleton<CharacterSession>();
        s.AddSingleton<HomeworkCommands>();

        // 등록 순서 = 시작 순서: 가져오기를 먼저 한다
        s.AddHostedService<StartupTasks>();
        s.AddSingleton<StatusMonitor>();
        s.AddHostedService(sp => sp.GetRequiredService<StatusMonitor>());
        s.AddHostedService<ExternalChangeMonitor>();
        s.AddHostedService<CharacterRecorder>();
        s.AddHostedService<CharacterWatcher>();
    }

    /// <summary>기존 창을 앞으로 가져온다(두 번째 실행의 신호, 트레이 "열기"). UI 스레드에서 부른다.</summary>
    public void ShowMainWindow()
    {
        if (_window == null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        // 전경 권한이 없어도 확실히 올리려고 잠깐 Topmost를 켰다 끈다
        var top = _window.Topmost;
        _window.Topmost = true;
        _window.Topmost = top;
        _window.Focus();
    }

    /// <summary>
    /// 앱 종료의 유일한 길(창 닫기·트레이 "종료"). Host.StopAsync가 끝난 뒤에 Shutdown한다.
    /// 어느 스레드에서 불러도 된다. 두 번 불러도 한 번만 한다.
    /// </summary>
    public void Quit()
    {
        if (Interlocked.Exchange(ref _quitting, 1) == 1) return;
        Dispatcher.InvokeAsync(async () =>
        {
            try { await StopHostAsync(); }
            catch (Exception ex) { _log?.LogWarning(ex, "종료 중 오류"); }
            Shutdown(_exitCode);
        });
    }

    private int _stopped;

    private async Task StopHostAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1) return;
        var host = _host;
        if (host == null) return;
        _log?.LogInformation("숙제 앱을 종료합니다.");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Task.Run(() => host.StopAsync(cts.Token));
        }
        catch (OperationCanceledException) { _log?.LogWarning("호스트 종료가 시간 안에 끝나지 않았습니다."); }
        host.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Quit을 거치지 않고 끝나는 경우(로그오프·시스템 종료, 외부에서 Shutdown 호출)에도 호스트를 멈춘다.
        // UI 스레드를 막은 채 기다리지 않도록 풀 스레드에서 멈추고 잠깐만 기다린다.
        try
        {
            if (_host != null && Volatile.Read(ref _stopped) == 0)
                Task.Run(StopHostAsync).Wait(TimeSpan.FromSeconds(8));
        }
        catch { }
        _single?.Dispose();
        _single = null;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.LogError(e.Exception, "처리되지 않은 UI 예외");
        e.Handled = true;   // 화면 예외 하나로 관찰을 멈추지 않는다
    }

    private void OnDomainUnhandled(object? sender, UnhandledExceptionEventArgs e)
    {
        _log?.LogCritical(e.ExceptionObject as Exception, "처리되지 않은 예외 (종료 중={Terminating})", e.IsTerminating);
    }

    private void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.LogError(e.Exception, "관찰되지 않은 작업 예외");
        e.SetObserved();
    }
}
