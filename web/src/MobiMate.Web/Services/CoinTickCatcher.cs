using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

/// <summary>
/// 은동전·마족 공물이 +1 되는 순간 잡기 (2026-11-21). 위상(<see cref="CoinPhase"/>)을 대충 알면, 그 시각 근처에서만 재화를 1초마다 읽어
/// 변하는 순간을 초 단위로 좁힌다. 평소에는 읽지 않아 호출이 늘지 않는다(30분에 위상 폭 + 몇 초 동안만). 위상이 좁아질수록 읽는 시간도 줄어든다.
/// 위상을 아직 모르는 캐릭터는 60초 주기 기록(HomeworkWatcher)이 먼저 좁혀 준다.
/// </summary>
public sealed class CoinTickCatcher(GameQueries q, StatusMonitor status, GameStateCache state, CoinPhaseStore phases, ILogger<CoinTickCatcher> log) : BackgroundService
{
    /// <summary>위상 폭이 이보다 넓으면(정보가 부족) 잡으러 읽지 않는다: 호출이 너무 많아진다.</summary>
    internal const int MaxWidthSeconds = 150;
    /// <summary>위상 앞뒤로 더 읽는 여유(초)</summary>
    internal const int MarginSeconds = 4;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (status.State != GameConnection.Connected) continue;
                var key = state.CurrentKey;
                if (key == null || !SnapshotManager.IsRealCharacter(state.Character)) continue;
                var now = DateTime.UtcNow;
                if (!InWindow(key, now)) continue;
                try
                {
                    q.Invalidate("get_currencies");
                    var r = await q.Get<List<CurrencyItem>>("get_currencies", ct);
                    if (!r.Ok || r.Value == null || state.CurrentKey != key) continue;
                    phases.Observe(key, DateTime.UtcNow,
                        r.Value.FirstOrDefault(c => c.DisplayName == "은동전")?.Amount,
                        r.Value.FirstOrDefault(c => c.DisplayName is "마족 공물" or "마족공물")?.Amount);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { log.LogDebug(ex, "충전 순간 읽기 실패"); }
            }
        }
        catch (OperationCanceledException) { }
        finally { phases.Flush(); }
    }

    /// <summary>지금이 이 캐릭터의 은동전·마족 공물 중 하나가 +1 될 만한 시각(위상 ± 폭/2 ± 여유)인가</summary>
    private bool InWindow(string key, DateTime nowUtc)
    {
        foreach (var kind in new[] { CoinKind.Silver, CoinKind.Tribute })
        {
            if (phases.Estimate(key, kind) is not { } e || e.Width > MaxWidthSeconds) continue;
            var step = kind == CoinKind.Silver ? 1800 : 43200;
            var epoch = (long)(nowUtc - DateTime.UnixEpoch).TotalSeconds;
            var d = ((epoch - e.Phase) % step + step) % step;   // 위상을 지난 지 몇 초인가 [0, 주기)
            var half = e.Width / 2.0 + MarginSeconds;
            if (d <= half || d >= step - half) return true;
        }
        return false;
    }
}
