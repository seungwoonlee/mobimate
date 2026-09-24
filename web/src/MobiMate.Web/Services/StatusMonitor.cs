using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Security;

namespace MobiMate.Web.Services;

public enum GameConnection { Unknown, Connected, Disconnected, CliMissing }

/// <summary>
/// 게임 연결 상태 (FR-CN-01): status를 고정 주기로 확인하고 바뀌면 SSE "status"로 알린다. 서버의 유일한 자체 폴링.
/// 같은 루프에서 만료된 기기를 정리하고 그 기기의 SSE를 닫는다 (SEC-07).
/// </summary>
public sealed class StatusMonitor(IGameCli cli, SseHub hub, DeviceStore devices, MobiMateOptions options, ILogger<StatusMonitor> log) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);
    private readonly object _lock = new();
    private readonly SemaphoreSlim _check = new(1, 1);
    private DateTimeOffset _lastSweep = DateTimeOffset.MinValue;

    public GameConnection State { get; private set; } = GameConnection.Unknown;
    public DateTimeOffset Since { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>API·SSE에 쓰는 상태 값 (상세설계 §3.5: connected|disconnected|cli_missing).</summary>
    public static string Wire(GameConnection s) => s switch
    {
        GameConnection.Connected => "connected",
        GameConnection.Disconnected => "disconnected",
        GameConnection.CliMissing => "cli_missing",
        _ => "unknown",
    };

    /// <summary>주기 확인과 설정 변경 확인이 겹치지 않게 직렬화한다. 늦게 끝난 옛 경로 결과가 새 상태를 덮지 않도록.</summary>
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
            var r = await cli.RunAsync(new CliCommand("status", Timeout: TimeSpan.FromSeconds(3)), ct);
            next = r.Ok ? GameConnection.Connected : GameConnection.Disconnected;
        }

        bool changed;
        lock (_lock)
        {
            changed = next != State;
            if (changed)
            {
                State = next;
                Since = DateTimeOffset.UtcNow;
            }
        }
        if (changed)
        {
            log.LogInformation("게임 연결 상태: {State}", next);
            hub.Broadcast("status", new { state = Wire(next), since = Since });
        }
        return next;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.StatusInterval);
        do
        {
            try
            {
                await CheckNowAsync(stoppingToken);
                SweepDevices();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "상태 확인 실패");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void SweepDevices()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSweep < SweepInterval) return;
        _lastSweep = now;
        foreach (var id in devices.SweepExpired(now))
        {
            hub.Disconnect(id);
            log.LogInformation("만료된 기기 정리: {Id}", id);
        }
    }
}
