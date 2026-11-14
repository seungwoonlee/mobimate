using System.Diagnostics;

namespace MobiMateHomeWork.Tests;

/// <summary>
/// 시작·종료 주변 (WPF Application 자체는 제외): 단일 인스턴스, 옛 웹앱 감지·경고 해제, Host 종료 순서(H2a 위험 지점 g·i).
/// </summary>
public class HostingTests
{
    // ── SingleInstance ──
    // 명명 뮤텍스는 스레드 소유라서 동기 테스트로 한 스레드에서 다룬다. 이름은 테스트마다 고유하다(실제 앱 이름과 겹치지 않는다).

    private static (string Mutex, string Show) UniqueNames()
    {
        var id = Guid.NewGuid().ToString("N");
        return ($@"Local\MobiMateHomeWork.Test.Single.{id}", $@"Local\MobiMateHomeWork.Test.Show.{id}");
    }

    [Fact]
    public void SingleInstance_TheFirstWins_TheSecondIsNotFirst_AndAReleasedNameCanBeTakenAgain()
    {
        var (m, e) = UniqueNames();
        var first = new SingleInstance(m, e);
        Assert.True(first.IsFirst);

        var second = new SingleInstance(m, e);
        Assert.False(second.IsFirst);                       // 같은 이름은 두 번째가 얻지 못한다
        second.Dispose();
        using (var again = new SingleInstance(m, e)) Assert.False(again.IsFirst);   // 두 번째가 닫혀도 첫째가 쥔 동안은 그대로

        first.Dispose();
        using var third = new SingleInstance(m, e);
        Assert.True(third.IsFirst);                         // 첫째가 끝나면 다시 얻을 수 있다
    }

    [Fact]
    public void SingleInstance_TheSecondRunsSignalReachesTheFirst()
    {
        var (m, e) = UniqueNames();
        using var first = new SingleInstance(m, e);
        using var got = new ManualResetEventSlim();
        first.Listen(() => got.Set());

        using var second = new SingleInstance(m, e);
        Assert.False(second.IsFirst);
        Assert.True(SingleInstance.SignalExisting(e));       // 두 번째 실행이 "창 앞으로" 신호를 보낸다
        Assert.True(got.Wait(TimeSpan.FromSeconds(10)), "첫 인스턴스가 신호를 받지 못했습니다.");

        got.Reset();
        Assert.True(SingleInstance.SignalExisting(e));       // 여러 번 보내도 매번 전달된다
        Assert.True(got.Wait(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void SingleInstance_ASubscriberThatThrows_DoesNotBreakLaterSignals()
    {
        var (m, e) = UniqueNames();
        using var first = new SingleInstance(m, e);
        var calls = 0;
        using var twice = new ManualResetEventSlim();
        first.Listen(() =>
        {
            if (Interlocked.Increment(ref calls) == 2) twice.Set();
            throw new InvalidOperationException("구독자 오류");
        });
        Assert.True(SingleInstance.SignalExisting(e));
        SpinWait.SpinUntil(() => Volatile.Read(ref calls) >= 1, TimeSpan.FromSeconds(10));
        Assert.True(SingleInstance.SignalExisting(e));
        Assert.True(twice.Wait(TimeSpan.FromSeconds(10)), "예외 뒤에는 신호를 받지 못합니다.");
    }

    [Fact]
    public void SingleInstance_SignalWithoutAFirstInstance_ReportsFailure()
    {
        var (_, e) = UniqueNames();
        Assert.False(SingleInstance.SignalExisting(e));     // 받을 쪽이 없다 (몇 번 재시도한 뒤 false)
    }

    [Fact]
    public void SingleInstance_DefaultNames_BelongToThisUserAndThisApp()
    {
        Assert.StartsWith(@"Local\MobiMateHomeWork.Single.", SingleInstance.MutexName);
        Assert.StartsWith(@"Local\MobiMateHomeWork.Show.", SingleInstance.ShowEventName);
        Assert.NotEqual(@"Local\MobiMate.Single", SingleInstance.MutexName);   // 웹앱의 단일 인스턴스와 이름이 겹치지 않는다
    }

    // ── LegacyWebGuard ──

    private static readonly LegacyWebGuard.WebProc Old = new(4242, DateTime.Now.AddHours(-3));

    [Fact]
    public async Task Legacy_NoWebApp_NoWarning()
    {
        var pids = await LegacyWebGuard.FindLegacyWebAsync(@"C:\any", mutexExists: _ => throw new Exception("프로세스가 없으면 뮤텍스는 보지 않는다"), runningWeb: () => []);
        Assert.Empty(pids);
    }

    [Fact]
    public async Task Legacy_WebAppThatCreatedTheSharedMutex_IsPatched_NoWarning()
    {
        string? asked = null;
        var dir = Path.Combine(Path.GetTempPath(), "mm-legacy");
        var pids = await LegacyWebGuard.FindLegacyWebAsync(dir, mutexExists: n => { asked = n; return true; }, runningWeb: () => [Old]);
        Assert.Empty(pids);
        Assert.Equal(SharedStorageLock.MutexNameFor(dir), asked);   // 이 저장 폴더의 공유 뮤텍스 이름으로 확인한다
    }

    [Fact]
    public async Task Legacy_OldWebAppWithoutTheSharedMutex_IsLegacy_AndReportedRightAway()
    {
        var sw = Stopwatch.StartNew();
        var pids = await LegacyWebGuard.FindLegacyWebAsync(@"C:\any", mutexExists: _ => false, runningWeb: () => [Old, new(5151, DateTime.Now.AddHours(-1))]);
        Assert.Equal(new[] { 4242, 5151 }, pids);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), "오래 떠 있던 웹앱은 기다릴 이유가 없다");
    }

    [Fact]
    public async Task Legacy_AFreshlyStartedWebApp_GetsTimeToCreateItsMutex()
    {
        var polls = 0;
        var fresh = new LegacyWebGuard.WebProc(777, DateTime.Now);   // 방금 시작
        var pids = await LegacyWebGuard.FindLegacyWebAsync(@"C:\any", mutexExists: _ => Interlocked.Increment(ref polls) >= 3, runningWeb: () => [fresh]);
        Assert.Empty(pids);                                          // 잠시 뒤 뮤텍스가 생겼다 → 패치된 웹앱
        Assert.True(polls >= 3);
    }

    [Fact]
    public async Task Legacy_ACancelledCheck_StopsWaiting()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var fresh = new LegacyWebGuard.WebProc(777, DateTime.Now);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LegacyWebGuard.FindLegacyWebAsync(@"C:\any", cts.Token, mutexExists: _ => false, runningWeb: () => [fresh]));
    }

    // ── LegacyWebWatcher ──

    [Fact]
    public void LegacyWatcher_RaisesGoneOnce_WhenAllLegacyProcessesEnded()
    {
        var running = new List<LegacyWebGuard.WebProc> { new(1, DateTime.Now.AddHours(-1)), new(2, DateTime.Now.AddHours(-1)) };
        var gone = 0;
        using var w = new LegacyWebWatcher([1, 2], () => Interlocked.Increment(ref gone), TimeSpan.FromHours(1), () => [.. running]);

        w.Check();
        Assert.Equal(0, gone);                              // 둘 다 살아 있다
        running.RemoveAll(p => p.Pid == 1);
        w.Check();
        Assert.Equal(0, gone);                              // 하나가 남았다
        running.Clear();
        w.Check();
        Assert.Equal(1, gone);
        w.Check();
        w.Check();
        Assert.Equal(1, gone);                              // 한 번만
    }

    [Fact]
    public void LegacyWatcher_IgnoresANewWebAppThatStartedLater()
    {
        var running = new List<LegacyWebGuard.WebProc> { new(1, DateTime.Now.AddHours(-1)) };
        var gone = 0;
        using var w = new LegacyWebWatcher([1], () => Interlocked.Increment(ref gone), TimeSpan.FromHours(1), () => [.. running]);

        running.Clear();
        running.Add(new LegacyWebGuard.WebProc(999, DateTime.Now));   // 옛 웹앱을 끄고 새로 켠 웹앱: 판정 대상이 아니다
        w.Check();
        Assert.Equal(1, gone);                                        // 옛 프로세스가 끝났으니 경고를 거둔다
    }

    [Fact]
    public async Task LegacyWatcher_ChecksOnItsOwnTimer()
    {
        var gone = new TaskCompletionSource();
        var alive = true;
        using var w = new LegacyWebWatcher([1], () => gone.TrySetResult(), TimeSpan.FromMilliseconds(30),
            () => alive ? [new LegacyWebGuard.WebProc(1, DateTime.Now.AddHours(-1))] : []);
        await Task.Delay(100);
        Assert.False(gone.Task.IsCompleted);
        alive = false;
        await gone.Task.WaitAsync(Wait.Default);
    }

    [Fact]
    public async Task LegacyWarning_IsRaisedOnceAndClearedThroughAppEvents()
    {
        // App.StartCoreAsync가 하는 연결과 같은 모양: 판정 → 경고 → 감시 → 해제
        var events = new AppEvents();
        var log = new EventLog(events);
        var cleared = new List<AppWarningKind>();
        events.WarningCleared += cleared.Add;
        var running = new List<LegacyWebGuard.WebProc> { Old };

        var legacy = await LegacyWebGuard.FindLegacyWebAsync(@"C:\any", mutexExists: _ => false, runningWeb: () => [.. running]);
        events.RaiseWarning(new AppWarning(AppWarningKind.LegacyWebApp, "옛 웹앱"));
        events.RaiseWarning(new AppWarning(AppWarningKind.LegacyWebApp, "옛 웹앱"));   // 같은 종류는 한 번만
        using var w = new LegacyWebWatcher(legacy, () => events.ClearWarning(AppWarningKind.LegacyWebApp), TimeSpan.FromHours(1), () => [.. running]);
        Assert.Single(events.Warnings);
        Assert.Single(log.Warnings);

        running.Clear();
        w.Check();
        Assert.Empty(events.Warnings);
        Assert.Equal(new[] { AppWarningKind.LegacyWebApp }, cleared);
    }

    // ── 종료 순서 (Host.StopAsync) ──

    private sealed class Probe(string name, List<string> order) : IHostedService
    {
        public bool Stopped;
        public Task StartAsync(CancellationToken ct) { order.Add("start:" + name); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) { Stopped = true; order.Add("stop:" + name); return Task.CompletedTask; }
    }

    private sealed class ThrowsOnStop(List<string> order) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) { order.Add("stop:thrower"); throw new InvalidOperationException("종료 중 서비스 오류"); }
    }

    /// <summary>ExecuteAsync가 멈춤 신호를 무시하고 매달려 있는 서비스.</summary>
    private sealed class Stuck : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Delay(Timeout.Infinite);
    }

    private sealed class Tracked : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private static IHost PlainHost(MemoryLoggerProvider logs, Action<IServiceCollection> configure)
    {
        var b = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        b.Logging.AddProvider(logs);
        b.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromMilliseconds(500));
        configure(b.Services);
        return b.Build();
    }

    [Fact]
    public async Task Shutdown_AServiceThatThrows_DoesNotStopTheOthersFromStopping_AndTheHostIsDisposed()
    {
        var order = new List<string>();
        var logs = new MemoryLoggerProvider();
        var a = new Probe("first", order);
        var c = new Probe("last", order);
        var host = PlainHost(logs, s =>
        {
            s.AddSingleton<Tracked>();
            s.AddSingleton<IHostedService>(a);
            s.AddSingleton<IHostedService>(new ThrowsOnStop(order));
            s.AddSingleton<IHostedService>(c);
        });
        await host.StartAsync();
        var tracked = host.Services.GetRequiredService<Tracked>();   // 컨테이너가 만든 서비스(직접 넣은 인스턴스는 컨테이너가 정리하지 않는다)

        await HostShutdown.StopAsync(host, logs.CreateLogger("test"), TimeSpan.FromSeconds(10));   // 예외가 밖으로 나오지 않는다

        Assert.Equal(new[] { "start:first", "start:last", "stop:last", "stop:thrower", "stop:first" }, order);   // 등록의 역순, 예외 서비스 뒤에도 계속
        Assert.True(a.Stopped && c.Stopped);
        Assert.True(tracked.Disposed);                                  // 호스트 정리
        Assert.Contains(logs.Entries, x => x.Level == LogLevel.Warning && x.Message.Contains("종료 중 오류"));   // 로그로 남는다
    }

    [Fact]
    public async Task Shutdown_OfTheRealAppServices_SurvivesAThrowingServiceAndStopsEverythingBehindIt()
    {
        var order = new List<string>();
        var first = new Probe("first", order);        // 앱 서비스보다 먼저 등록 = 가장 나중에 멈춘다
        var h = await AppHarness.StartAsync(
            servicesBefore: s => s.AddSingleton<IHostedService>(first),
            services: s =>
            {
                s.AddSingleton<IHostedService>(new ThrowsOnStop(order));   // 앱 서비스 뒤에 등록 = 가장 먼저 멈춘다
                s.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(2));
            });
        await h.Status.CheckNowAsync();
        var logger = h.Logs.CreateLogger("test");

        await HostShutdown.StopAsync(h.Host, logger, TimeSpan.FromSeconds(10));

        Assert.True(first.Stopped, "예외를 던진 서비스 뒤에 있는 서비스(앱 서비스 포함)까지 멈춰야 한다");
        Assert.Contains(h.Logs.Entries, x => x.Level == LogLevel.Warning && x.Message.Contains("종료 중 오류"));
        Assert.Throws<ObjectDisposedException>(() => h.Services.GetRequiredService<CharacterSession>());   // 서비스 컨테이너가 정리되었다
        await h.DisposeAsync();                                         // 이미 정리된 호스트를 다시 정리해도 안전하다
    }

    [Fact]
    public async Task Shutdown_AServiceThatIgnoresTheStopSignal_DoesNotHangTheShutdown()
    {
        var order = new List<string>();
        var logs = new MemoryLoggerProvider();
        var last = new Probe("last", order);
        var host = PlainHost(logs, s =>
        {
            s.AddSingleton<IHostedService>(last);
            s.AddHostedService<Stuck>();
        });
        await host.StartAsync();

        var sw = Stopwatch.StartNew();
        await HostShutdown.StopAsync(host, logs.CreateLogger("test"), TimeSpan.FromSeconds(10));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"종료가 매달린 서비스를 기다리느라 멈췄다 ({sw.Elapsed})");   // 호스트 제한(0.5초)에서 끊긴다
        Assert.True(last.Stopped);
    }

    [Fact]
    public async Task Shutdown_StoppingTwice_IsHarmless()
    {
        var logs = new MemoryLoggerProvider();
        var host = PlainHost(logs, _ => { });
        await host.StartAsync();
        await HostShutdown.StopAsync(host, logs.CreateLogger("test"), TimeSpan.FromSeconds(5));
        await HostShutdown.StopAsync(host, logs.CreateLogger("test"), TimeSpan.FromSeconds(5));   // 이미 정리된 호스트: 예외 없이 끝난다
    }
}
