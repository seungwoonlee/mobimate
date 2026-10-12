using System.Diagnostics;
using MobiMateHomeWork.Hosting;

namespace MobiMateHomeWork.Services;

/// <summary>
/// 숙제 관찰 서비스. (1) 접속 중 주기 관찰 — 두 갈래: 빠른 관찰(기본 20초, 퀘스트 + 캐릭터 + 현재 맵 3개)과 전체 관찰(기본 60초, 9개 전부).
/// 전체 관찰 시점에는 전체가 빠른 관찰을 대신한다. 접속이 새로 시작되면 첫 관찰은 전체다(기준값을 잡는다).
/// (2) 맵 이동·로딩 복귀 직후 +2초·+8초 예약 관찰은 항상 전체다(레이드·어비스 보상이 맵 이동 직후 들어오므로).
/// 관찰은 한 번에 하나만 돈다: 주기 관찰은 이미 도는 중이면 건너뛰고, 예약 관찰은 끝나기를 기다렸다가 돈다(맵이 바뀐 뒤의 값을 보려고).
/// (3) 요일 던전 클리어(Cleared)를 감시가 알아챈 순간 즉시(+0초)와 +3초에 전체 관찰한다(Cleared는 약 17초만 유지되고, 그 사이의 활동·맵으로 완료를 확정한다).
/// 예약 관찰은 퀘스트·미션 등의 캐시를 비우고 읽는다. 전체 관찰 1회 = CLI 최대 9회(캐릭터·맵·활동은 감시가 방금 읽었으면 캐시 재사용), 빠른 관찰 1회 = 최대 3회(보통 퀘스트 1회).
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
    private readonly DelayedBurst _clearBurst;
    private readonly CancellationTokenSource _stop = new();
    /// <summary>던전 클리어를 알아챈 뒤 즉시 관찰에 더해 한 번 더 관찰하는 시점.</summary>
    internal static readonly TimeSpan ClearedSecondDelay = TimeSpan.FromSeconds(3);
    private int _runs;
    private int _fullRuns;
    private readonly long _origin = Stopwatch.GetTimestamp();   // 전체 관찰 격자의 기준(서비스 시작). 기록 주기(60초)와 격자가 맞아 캐릭터 기록이 방금 읽은 활동·재화를 재사용한다
    private TimeSpan? _lastFullAt;   // 마지막으로 전체 관찰을 마친 시각(기준으로부터). 없으면 다음 주기 관찰은 전체

    public HomeworkObserver(HomeworkWatcher homework, GameQueries q, StatusMonitor status, AppOptions options, ILogger<HomeworkObserver> log)
        : this(homework, q, status, options, log, null) { }

    internal HomeworkObserver(HomeworkWatcher homework, GameQueries q, StatusMonitor status, AppOptions options, ILogger<HomeworkObserver> log,
        Func<TimeSpan, CancellationToken, Task>? delay)
    {
        (_homework, _q, _status, _options, _log) = (homework, q, status, options, log);
        _burst = new DelayedBurst([options.HomeworkTriggerFirstDelay, options.HomeworkTriggerSecondDelay], ct => RunAsync(true, ct), delay);
        _clearBurst = new DelayedBurst([ClearedSecondDelay], ct => RunAsync(true, ct), delay);
    }

    /// <summary>던전 클리어 상태 전환 감지에 쓰는 숙제 카탈로그.</summary>
    public HomeworkCatalog Catalog => _homework.Service.Catalog;

    /// <summary>
    /// 던전 클리어(Cleared)를 알렸다: 즉시(+0초) 전체 관찰하고 +3초에 한 번 더 한다. 관찰은 활동·맵 캐시를 비우고 읽는다.
    /// 즉시 관찰은 감시 루프를 막지 않게 따로 돌리고, 이미 도는 관찰이 있으면 끝나기를 기다렸다가 돈다.
    /// </summary>
    public void RequestAfterDungeonCleared()
    {
        if (_stop.IsCancellationRequested) return;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(true, _stop.Token); }
            catch (OperationCanceledException) { }
        });
        _clearBurst.Request();
    }

    /// <summary>관찰을 실제로 돈 횟수(테스트용).</summary>
    public int Runs => Volatile.Read(ref _runs);

    /// <summary>그중 전체(9개) 관찰 횟수(테스트용).</summary>
    public int FullRuns => Volatile.Read(ref _fullRuns);

    /// <summary>합쳐진 것을 포함한 예약 요청 횟수(테스트용).</summary>
    public int BurstRequests => _burst.Requests;

    /// <summary>맵 이동·로딩 복귀를 알렸다: +2초, +8초에 관찰한다(겹치면 하나로 합친다).</summary>
    public void RequestAfterMapChange(string reason)
    {
        _log.LogInformation("{Reason}: 숙제를 곧 관찰합니다", reason);
        _burst.Request();
    }

    /// <summary>
    /// 이번 주기 관찰이 전체 관찰이어야 하는가. 빠른 관찰만 켜졌으면(전체 0) 항상 빠른 관찰, 전체만 켜졌으면(빠른 0) 항상 전체.
    /// 둘 다 켜졌으면 아직 전체를 한 적이 없을 때(접속 직후)와, 마지막 전체 관찰 뒤 전체 주기의 격자(기준 시각의 배수, 타이머 흔들림은 빠른 주기의 절반까지 봐 줌)를 넘었을 때 전체.
    /// </summary>
    /// <param name="elapsed">기준 시각(서비스 시작)으로부터 지금까지</param>
    /// <param name="lastFullAt">마지막 전체 관찰 시각(같은 기준). 없으면 null</param>
    internal static bool NextIsFull(TimeSpan fast, TimeSpan full, TimeSpan elapsed, TimeSpan? lastFullAt)
    {
        if (full <= TimeSpan.Zero) return false;
        if (fast <= TimeSpan.Zero || full <= fast || lastFullAt is null) return true;
        var slack = fast / 2;
        return Math.Floor((elapsed + slack) / full) > Math.Floor((lastFullAt.Value + slack) / full);
    }

    internal Task<bool> RunAsync(bool triggered, CancellationToken ct) => RunAsync(triggered, full: triggered || NextIsFull(
        _options.HomeworkObserveInterval, _options.HomeworkFullObserveInterval, Stopwatch.GetElapsedTime(_origin), _lastFullAt), ct);

    internal async Task<bool> RunAsync(bool triggered, bool full, CancellationToken ct)
    {
        if (_status.State != GameConnection.Connected) { _lastFullAt = null; return false; }
        if (triggered) await _gate.WaitAsync(ct);
        else if (!await _gate.WaitAsync(0, ct)) return false;
        try
        {
            if (triggered) _q.Invalidate("get_quests", "get_daily_missions", "get_weekly_missions", "get_current_environment", "get_activity");
            Interlocked.Increment(ref _runs);
            if (full) Interlocked.Increment(ref _fullRuns);
            await _homework.ObserveAsync(ct, full);
            if (full) _lastFullAt = Stopwatch.GetElapsedTime(_origin);
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
            var period = _options.HomeworkObserveInterval > TimeSpan.Zero ? _options.HomeworkObserveInterval : _options.HomeworkFullObserveInterval;
            if (period <= TimeSpan.Zero) return;   // 빠른 관찰·전체 관찰 모두 꺼짐
            using var timer = new PeriodicTimer(period);
            while (await timer.WaitForNextTickAsync(ct))
                await RunAsync(false, ct);
        }
        catch (OperationCanceledException) { }
    }

    public override void Dispose()
    {
        _stop.Cancel();
        _burst.Dispose();
        _clearBurst.Dispose();
        base.Dispose();
    }
}
