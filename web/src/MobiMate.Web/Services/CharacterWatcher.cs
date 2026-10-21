using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

/// <summary>
/// 캐릭터 전환 감시 (v1.6). 서버가 "캐릭터가 바뀌었다"를 스스로, 몇 초 안에 알아챈다.
/// 예전에는 화면의 자동 갱신(최대 30초)이나 60초 기록 주기가 읽을 때까지 몰랐고, 화면이 백그라운드이거나 게임 연결이
/// 끊겼다 돌아오는 순간(캐릭터 선택창을 거치면 연결이 1분 가까이 끊긴다)에는 더 늦었다.
/// 접속 중에는 가벼운 get_my_info 한 번으로 서버·직업·레벨·칭호·전투력의 표식을 비교하고, 달라졌거나 연결이 막 돌아왔으면
/// 던전 안이었거나 요일 던전 지역일 때만 get_activity도 읽어 Cleared로 바뀐 순간 숙제를 곧바로 관찰한다(평상시 호출 수는 그대로).
/// 곧바로 헤더를 새로 읽어 SSE로 밀어 준다. 전환 직후에는 재화·칭호가 아직 이전 캐릭터 것일 수 있어 몇 초 뒤 한 번 더 읽는다.
/// </summary>
public sealed class CharacterWatcher(GameQueries q, GameViews views, StatusMonitor status, MobiMateOptions options, GameStateCache state, HomeworkWatcher homework, AutoWatch auto, ILogger<CharacterWatcher> log) : BackgroundService
{
    private static readonly TimeSpan[] Settle = { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8) };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (options.WatchInterval <= TimeSpan.Zero) return;
        using var timer = new PeriodicTimer(options.WatchInterval);
        string? last = null;
        var wasConnected = false;
        var dungeon = new DungeonClearDetector();
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var connected = status.State == GameConnection.Connected;
                if (!connected) { wasConnected = false; dungeon.Reset(); auto.Reset(); continue; }
                try
                {
                    var r = await q.Get<CharacterInfo>("get_my_info", ct);
                    if (!r.Ok || r.Value == null) continue;
                    await WatchDungeonAsync(dungeon, r.Value, ct);
                    await WatchNearbyAsync(dungeon, ct);
                    var sig = Signature(r.Value);
                    var first = last == null;
                    var why = first ? "감시 시작" : !wasConnected ? "연결 복구" : sig != last ? "캐릭터 변경" : null;
                    (last, wasConnected) = (sig, true);
                    if (why == null) continue;
                    log.LogInformation("{Why}: 캐릭터 정보를 바로 새로 읽습니다 ({Sig})", why, sig);
                    await views.RecordNowAsync(ct);
                    if (first) continue;
                    foreach (var wait in Settle)
                    {
                        await Task.Delay(wait, ct);
                        q.Invalidate("get_my_info");
                        await views.RecordNowAsync(ct);
                    }
                    last = Signature((await q.Get<CharacterInfo>("get_my_info", ct)).Value ?? r.Value);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    log.LogDebug(ex, "캐릭터 감시 읽기 실패");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 던전 안이었거나(직전 읽기, 또는 마지막 헤더가 던전 안) 요일 던전 지역일 때만 활동을 읽는다. 맵은 마지막 헤더 값을 쓴다(추가 호출 없음).
    /// Cleared로 바뀐 순간 로그를 남기고 숙제를 바로(+0초) 관찰하며, 3초 뒤 한 번 더 관찰한다(Cleared는 약 17초만 유지된다).
    /// </summary>
    private async Task WatchDungeonAsync(DungeonClearDetector dungeon, CharacterInfo me, CancellationToken ct)
    {
        var map = state.Environment?.GameSpaceDisplayName;
        if (!dungeon.ShouldRead(map, homework.Service.Catalog) && state.Activity?.Dungeon is not { IsInside: true }) return;
        var act = await q.Get<ActivityInfo>("get_activity", ct);
        if (!act.Ok || !dungeon.Observe(act.Value)) return;
        q.Invalidate("get_quests", "get_current_environment");
        var env = await q.Get<EnvironmentInfo>("get_current_environment", ct);
        log.LogInformation("던전 클리어 감지: {Character} / 맵 {Map} / {At:HH:mm:ss}", $"{me.RealmName}_{me.JobName}", env.Value?.GameSpaceDisplayName ?? "(알 수 없음)", DateTime.Now);
        await homework.ObserveAsync(ct);
        await Task.Delay(ClearedSecondDelay, ct);
        q.Invalidate("get_quests", "get_current_environment", "get_activity");
        await homework.ObserveAsync(ct);
    }

    internal static readonly TimeSpan ClearedSecondDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 오토 의심용(2026-10-22): 던전·레이드 안(던전 상태가 안이거나, 어비스·레이드·요일 던전 지역)인지 AutoWatch에 알리고,
    /// 안이면 평소의 읽기 주기인 이 감시 틱마다 주변을 읽어 넣는다. 의심 조건이 시작되어 카운터가 도는 동안은 AutoWatcher가 1초마다 읽으므로 여기서는 건너뛴다.
    /// 안이 아니면 쌓은 것을 지운다(평상시에는 호출을 늘리지 않는다).
    /// </summary>
    private async Task WatchNearbyAsync(DungeonClearDetector dungeon, CancellationToken ct)
    {
        var map = state.Environment?.GameSpaceDisplayName;
        var inside = dungeon.Inside || state.Activity?.Dungeon is { IsInside: true } || homework.Service.Catalog.FindByArea(map) != null;
        var now = DateTimeOffset.UtcNow;
        auto.SetInstance(inside, now);
        if (!inside) { auto.Observe(now, false, null); return; }
        if (auto.NeedsFast()) return;
        q.Invalidate("get_near_pcs");
        var near = await q.Get<List<NearPcItem>>("get_near_pcs", ct);
        if (near.Ok && near.Value != null) auto.Observe(now, true, near.Value);
    }

    /// <summary>캐릭터 선택창의 빈 정보도 표식이 되므로 선택창 → 캐릭터 진입이 전환으로 잡힌다.</summary>
    internal static string Signature(CharacterInfo c) =>
        $"{c.RealmName}|{c.JobName}|{c.Level}|{c.Title}|{c.CombatScore?.Value}";
}
