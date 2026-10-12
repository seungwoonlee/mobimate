using MobiMateHomeWork.Hosting;

namespace MobiMateHomeWork.Services;

/// <summary>
/// 숙제 관찰 서비스. (1) 접속 중 주기 관찰(기본 20초, 캐릭터 기록 60초와 별개) (2) 맵 이동·로딩 복귀 직후 +2초·+8초 예약 관찰.
/// 관찰은 한 번에 하나만 돈다: 주기 관찰은 이미 도는 중이면 건너뛰고, 예약 관찰은 끝나기를 기다렸다가 돈다(맵이 바뀐 뒤의 값을 보려고).
/// 예약 관찰은 퀘스트·미션 등의 3초 캐시를 비우고 읽는다. 관찰 1회 = CLI 9회.
/// </summary>
public sealed class HomeworkObserver : BackgroundService
{
    private readonly HomeworkWatcher _homework;
    private readonly GameQueries _q;
    private readonly StatusMonitor _status;
    private readonly AppOptions _options;
    private readonly ILogger<HomeworkObserver> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DelayedBurst _burst;
    private int _runs;

    public HomeworkObserver(HomeworkWatcher homework, GameQueries q, StatusMonitor status, AppOptions options, ILogger<HomeworkObserver> log)
        : this(homework, q, status, options, log, null) { }

    internal HomeworkObserver(HomeworkWatcher homework, GameQueries q, StatusMonitor status, AppOptions options, ILogger<HomeworkObserver> log,
        Func<TimeSpan, CancellationToken, Task>? delay)
    {
        (_homework, _q, _status, _options, _log) = (homework, q, status, options, log);
        _burst = new DelayedBurst([options.HomeworkTriggerFirstDelay, options.HomeworkTriggerSecondDelay], ct => RunAsync(true, ct), delay);
    }

    /// <summary>관찰을 실제로 돈 횟수(테스트용).</summary>
    public int Runs => Volatile.Read(ref _runs);

    /// <summary>합쳐진 것을 포함한 예약 요청 횟수(테스트용).</summary>
    public int BurstRequests => _burst.Requests;

    /// <summary>맵 이동·로딩 복귀를 알렸다: +2초, +8초에 관찰한다(겹치면 하나로 합친다).</summary>
    public void RequestAfterMapChange(string reason)
    {
        _log.LogInformation("{Reason}: 숙제를 곧 관찰합니다", reason);
        _burst.Request();
    }

    internal async Task<bool> RunAsync(bool triggered, CancellationToken ct)
    {
        if (_status.State != GameConnection.Connected) return false;
        if (triggered) await _gate.WaitAsync(ct);
        else if (!await _gate.WaitAsync(0, ct)) return false;
        try
        {
            if (triggered) _q.Invalidate("get_quests", "get_daily_missions", "get_weekly_missions", "get_current_environment", "get_activity");
            Interlocked.Increment(ref _runs);
            await _homework.ObserveAsync(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "숙제 관찰 실패");
            return false;
        }
        finally { _gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            if (_options.HomeworkObserveInterval <= TimeSpan.Zero) return;
            using var timer = new PeriodicTimer(_options.HomeworkObserveInterval);
            while (await timer.WaitForNextTickAsync(ct))
                await RunAsync(false, ct);
        }
        catch (OperationCanceledException) { }
    }

    public override void Dispose()
    {
        _burst.Dispose();
        base.Dispose();
    }
}
