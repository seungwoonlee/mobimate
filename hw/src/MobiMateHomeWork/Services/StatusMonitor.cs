using MobiMateHomeWork.Hosting;

namespace MobiMateHomeWork.Services;

public enum GameConnection { Unknown, Connected, Disconnected, CliMissing }

/// <summary>
/// 게임 연결 상태: status를 고정 주기로 확인하고 바뀌면 <see cref="AppEvents.StatusChanged"/>로 알린다.
/// 주기가 0이면 주기 확인을 하지 않는다(<see cref="CheckNowAsync"/>는 그대로 쓸 수 있다).
/// </summary>
public sealed class StatusMonitor(IGameCli cli, AppEvents events, AppOptions options, ILogger<StatusMonitor> log) : BackgroundService
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _check = new(1, 1);

    public GameConnection State { get; private set; } = GameConnection.Unknown;
    public DateTimeOffset Since { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>주기 확인과 설정 변경 확인이 겹치지 않게 직렬화한다. 늦게 끝난 옛 경로 결과가 새 상태를 덮지 않도록.</summary>
    /// <summary>`status` 결과로 연결 상태를 판정한다: 응답이 왔고 `pipe`가 끊김이 아닐 때만 연결됨(연결이 끊겨도 CLI는 정상 종료한다).</summary>
    internal static GameConnection Judge(CliResult r) => r.Ok && CliEnvelope.PipeConnected(r.Stdout) != false ? GameConnection.Connected : GameConnection.Disconnected;

    public async Task<GameConnection> CheckNowAsync(CancellationToken ct = default)
    {
        await _check.WaitAsync(ct);
        try { return await CheckCoreAsync(ct); }
        finally { _check.Release(); }
    }

    private async Task<GameConnection> CheckCoreAsync(CancellationToken ct)
    {
        GameConnection next;
        if (!cli.IsAvailable) next = GameConnection.CliMissing;
        else
        {
            // 연결이 끊겨 있으면 CLI가 5초 기다린 뒤 {"pipe":"disconnected"}로 응답한다: 타임아웃은 그보다 길게, 판정은 응답 내용으로
            var r = await cli.RunAsync(new CliCommand("status", Timeout: TimeSpan.FromSeconds(8)), ct);
            next = Judge(r);
        }

        bool changed;
        DateTimeOffset since;
        lock (_lock)
        {
            changed = next != State;
            if (changed)
            {
                State = next;
                Since = DateTimeOffset.UtcNow;
            }
            since = Since;
        }
        if (changed)
        {
            log.LogInformation("게임 연결 상태: {State}", next);
            events.RaiseStatusChanged(new StatusChange(next, since));
        }
        return next;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.StatusInterval <= TimeSpan.Zero) return;
        using var timer = new PeriodicTimer(options.StatusInterval);
        try
        {
            do
            {
                try
                {
                    await CheckNowAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "상태 확인 실패");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
    }
}
