using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

/// <summary>
/// 오토 의심 초 단위 관찰(2026-10-22): 평소의 읽기는 기존 주기가 맡는다 — 캐릭터 감시(CharacterWatcher) 틱이 던전·레이드 안에서 주변을 읽고,
/// 화면이 주변을 조회할 때도 같은 값을 넣는다. 그러다 누군가의 의심 조건이 시작되면(<see cref="AutoWatch.NeedsFast"/>)
/// 이 서비스가 그때부터 <b>1초마다</b> get_near_pcs를 읽어 초 단위로 센다. 조건이 풀리면 멈추고 다시 평소 주기로 돌아간다.
/// 게임 CLI를 계속 두드리지 않으려는 목적이라 의심 조건이 없을 때는 아무것도 읽지 않는다.
/// </summary>
public sealed class AutoWatcher(GameQueries q, StatusMonitor status, MobiMateOptions options, AutoWatch auto, ILogger<AutoWatcher> log) : BackgroundService
{
    public static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(1);

    /// <summary>이 틱에 1초 읽기를 할 것인가: 게임에 연결돼 있고, 던전·레이드 안이며, 의심 조건이 도는 동안만.</summary>
    public static bool ShouldRead(bool connected, bool inInstance, bool needsFast) => connected && inInstance && needsFast;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (options.WatchInterval <= TimeSpan.Zero) return;   // 감시를 끈 설정(시험)에서는 돌지 않는다
        using var timer = new PeriodicTimer(FastInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { await TickAsync(DateTimeOffset.UtcNow, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { log.LogDebug(ex, "오토 의심 관찰 실패"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    internal async Task TickAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!ShouldRead(status.State == GameConnection.Connected, auto.InInstance(now), auto.NeedsFast())) return;
        q.Invalidate("get_near_pcs");   // 캐시를 쓰지 않고 새로 읽는다
        var near = await q.Get<List<NearPcItem>>("get_near_pcs", ct);
        if (near.Ok && near.Value != null) auto.Observe(now, true, near.Value);
    }
}
