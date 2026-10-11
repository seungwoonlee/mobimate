using System.Collections.Concurrent;

namespace MobiMateHomeWork.Tests;

/// <summary>화면이 받을 알림(AppEvents)을 모아 둔다. 서비스 스레드에서 올라오므로 스레드 안전하게 담는다.</summary>
public sealed class EventLog
{
    private readonly ConcurrentQueue<HeaderView> _headers = new();
    private readonly ConcurrentQueue<HomeworkChange> _homework = new();
    private readonly ConcurrentQueue<AppWarning> _warnings = new();
    private readonly ConcurrentQueue<StatusChange> _status = new();
    private int _characters;

    public EventLog(AppEvents e)
    {
        e.HeaderChanged += _headers.Enqueue;
        e.HomeworkChanged += _homework.Enqueue;
        e.WarningRaised += _warnings.Enqueue;
        e.StatusChanged += _status.Enqueue;
        e.CharactersChanged += () => Interlocked.Increment(ref _characters);
    }

    public IReadOnlyList<HeaderView> Headers => [.. _headers];
    public IReadOnlyList<HomeworkChange> Homework => [.. _homework];
    public IReadOnlyList<AppWarning> Warnings => [.. _warnings];
    public IReadOnlyList<StatusChange> Status => [.. _status];
    public int CharactersChanged => Volatile.Read(ref _characters);
}

/// <summary>메모리에 쌓는 로거: 종료 중 오류가 로그로 남는지 확인한다.</summary>
public sealed class MemoryLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Category, string Message, Exception? Error)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    private sealed class Logger(MemoryLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Entries.Enqueue((logLevel, category, formatter(state, exception), exception));
    }
}

/// <summary>
/// 서비스 직접 호출 하니스: 앱과 같은 서비스 등록(AppServices.Configure)을 임시 저장 폴더 + 가짜 CLI로 구성한다.
/// 주기 서비스(상태 확인·기록·전환 감시·외부 변경 감시)는 기본으로 끈다(주기 0) — 테스트가 메서드를 직접 부른다.
/// WPF 창·Application은 만들지 않는다. 실제 %APPDATA%\MobiMateWeb에는 닿지 않는다(저장 폴더·CLI 경로를 모두 임시 값으로 준다).
/// </summary>
public sealed class AppHarness : IAsyncDisposable
{
    public string StorageDir { get; }
    public FakeGame Game { get; }
    public AppOptions Options { get; }
    public IHost Host { get; }
    public AppEvents Events { get; }
    public EventLog Log { get; }
    public MemoryLoggerProvider Logs { get; } = new();
    private readonly bool _ownsGame;
    private readonly bool _keepStorage;

    public IServiceProvider Services => Host.Services;
    public CharacterSession Session => Services.GetRequiredService<CharacterSession>();
    public HomeworkCommands Commands => Services.GetRequiredService<HomeworkCommands>();
    public HomeworkWatcher Watcher => Services.GetRequiredService<HomeworkWatcher>();
    public HomeworkService Homework => Services.GetRequiredService<HomeworkService>();
    public SnapshotManager Snapshots => Services.GetRequiredService<SnapshotManager>();
    public AccountStore Accounts => Services.GetRequiredService<AccountStore>();
    public AppSettingsStore Settings => Services.GetRequiredService<AppSettingsStore>();
    public GameStateCache State => Services.GetRequiredService<GameStateCache>();
    public GameQueries Queries => Services.GetRequiredService<GameQueries>();
    public StatusMonitor Status => Services.GetRequiredService<StatusMonitor>();
    public HomeworkCatalog Catalog => Services.GetRequiredService<HomeworkCatalog>();
    public ExternalChangeMonitor External => Services.GetServices<IHostedService>().OfType<ExternalChangeMonitor>().Single();

    private AppHarness(string storageDir, FakeGame game, bool ownsGame, bool keepStorage, Action<AppOptions>? configure, Action<IServiceCollection>? services, Action<IServiceCollection>? servicesBefore)
    {
        StorageDir = storageDir;
        Game = game;
        _ownsGame = ownsGame;
        _keepStorage = keepStorage;
        Directory.CreateDirectory(storageDir);

        Options = new AppOptions
        {
            StorageDir = storageDir,
            WpfStorageDir = null,            // WPF판 기록 가져오기 끔
            CliPath = game.ExePath,
            SingleInstance = false,
            LegacyWebCheck = false,
            StatusIntervalSeconds = 0,
            RecordIntervalSeconds = 0,
            WatchIntervalSeconds = 0,
            ExternalWatch = false,
            QueryCacheTtlSeconds = 0,        // 캐시 끔: 호출마다 새로 읽는다
            CombatRecheckDelaySeconds = 0.1,
        };
        configure?.Invoke(Options);
        Options.StorageDir = storageDir;     // 어떤 구성이든 임시 폴더를 벗어나지 않게 못 박는다

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.AddProvider(Logs);
        Events = new AppEvents();
        servicesBefore?.Invoke(builder.Services);   // 앱 서비스보다 먼저 등록: 호스티드 서비스라면 가장 먼저 시작하고 가장 나중에 멈춘다
        AppServices.Configure(builder.Services, Options, Events);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));
        services?.Invoke(builder.Services);
        Host = builder.Build();
        Log = new EventLog(Events);
    }

    /// <param name="storageDir">미리 채워 둔 저장 폴더(없으면 새 임시 폴더). 주면 끝나도 지우지 않는다(테스트가 정리).</param>
    /// <param name="game">여러 하니스가 한 가짜 게임을 공유할 때(다시 시작 시나리오). 주면 이 하니스는 지우지 않는다.</param>
    public static async Task<AppHarness> StartAsync(string? storageDir = null, FakeGame? game = null, Action<AppOptions>? configure = null,
        Action<IServiceCollection>? services = null, Action<IServiceCollection>? servicesBefore = null)
    {
        var h = new AppHarness(storageDir ?? NewStorageDir(), game ?? new FakeGame(), ownsGame: game == null, keepStorage: storageDir != null, configure, services, servicesBefore);
        await h.Host.StartAsync();
        return h;
    }

    public static string NewStorageDir() => Path.Combine(Path.GetTempPath(), "mm-hwapp-" + Guid.NewGuid().ToString("N"));

    /// <summary>접속 중인 캐릭터를 읽어 기록한다(헤더 + 재화). 서비스 주기가 하는 일을 한 번 직접 한다.</summary>
    public Task RecordAsync() => Session.RecordNowAsync(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        try { await Host.StopAsync(); } catch { }
        Host.Dispose();
        if (_ownsGame) Game.Dispose();
        if (!_keepStorage) try { Directory.Delete(StorageDir, recursive: true); } catch { }
    }
}
