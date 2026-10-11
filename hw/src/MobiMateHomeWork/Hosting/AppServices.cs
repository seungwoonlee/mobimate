using MobiMateHomeWork.Services;

namespace MobiMateHomeWork.Hosting;

/// <summary>
/// 서비스 등록 한 곳. 앱(App.xaml.cs)과 서비스 테스트(MobiMateHomeWork.Tests)가 같은 등록을 쓴다 — 테스트가 앱과 다른 구성을 검증하지 않게.
/// 등록 순서 = 호스티드 서비스 시작 순서다.
/// </summary>
public static class AppServices
{
    public static void Configure(IServiceCollection s, AppOptions options, AppEvents events)
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
}

/// <summary>
/// Host 종료 한 길. 서비스 하나가 예외를 던지거나 제한 시간을 넘겨도 종료를 막지 않고, 끝나면 호스트를 정리(Dispose)한다.
/// 어느 쪽이든 예외는 밖으로 나가지 않는다(로그만 남긴다).
/// </summary>
public static class HostShutdown
{
    /// <param name="timeout">이 시간이 지나면 종료를 기다리지 않고 취소 신호를 보낸다(호스트 자체 ShutdownTimeout보다 조금 길게 잡는 안전망).</param>
    public static async Task StopAsync(IHost host, ILogger? log, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await Task.Run(() => host.StopAsync(cts.Token));
        }
        catch (Exception ex)
        {
            // OperationCanceled·AggregateException 등 무엇이든 종료를 막지 않는다
            log?.LogWarning(ex, "호스트 종료 중 오류(무시하고 계속 종료)");
        }
        try { host.Dispose(); }
        catch (Exception ex) { log?.LogWarning(ex, "호스트 정리 중 오류"); }
    }
}
