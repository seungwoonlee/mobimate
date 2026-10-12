using MobiMateHomeWork.Hosting;

namespace MobiMateHomeWork.Services;

/// <summary>
/// 캐릭터 전환 감시. "캐릭터가 바뀌었다"를 몇 초 안에 스스로 알아챈다.
/// 던전 안(직전 읽기)이거나 요일 던전 지역일 때만 get_activity도 읽어 Cleared로 바뀐 순간 숙제를 즉시 관찰한다(평상시 호출 수는 그대로).
/// 접속 중에는 가벼운 get_my_info 한 번으로 서버·직업·레벨·칭호·전투력의 표식을 비교하고, 달라졌거나 연결이 막 돌아왔으면
/// 곧바로 헤더를 새로 읽는다. 로딩 → 캐릭터 복귀와 맵 이름 변경도 같이 알아채 숙제 관찰을 예약한다(요일 던전 완료 반영을 빠르게). 전환 직후에는 재화·칭호가 아직 이전 캐릭터 것일 수 있어 몇 초 뒤 두 번 더 읽는다.
/// </summary>
public sealed class CharacterWatcher(GameQueries q, CharacterSession session, HomeworkObserver observer, StatusMonitor status, AppOptions options, ILogger<CharacterWatcher> log) : BackgroundService
{
    private static readonly TimeSpan[] Settle = { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8) };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (options.WatchInterval <= TimeSpan.Zero) return;
        using var timer = new PeriodicTimer(options.WatchInterval);
        string? last = null;
        var wasConnected = false;
        var maps = new MapChangeDetector();
        var dungeon = new DungeonClearDetector();
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var connected = status.State == GameConnection.Connected;
                if (!connected) { wasConnected = false; maps.Reset(); dungeon.Reset(); continue; }
                try
                {
                    // 감시는 기본 유지 시간으로 읽는다: 관찰·기록이 재사용하는 5초 캐시에 가려 8초에 한 번만 읽는 일이 없게
                    var tEnv = q.Get<EnvironmentInfo>("get_current_environment", ct, options.QueryCacheTtl);
                    var r = await q.Get<CharacterInfo>("get_my_info", ct, options.QueryCacheTtl);
                    var env = await tEnv;
                    if (!r.Ok || r.Value == null) continue;
                    if (maps.Observe(SnapshotManager.IsRealCharacter(r.Value), env.Value?.GameSpaceDisplayName))
                        observer.RequestAfterMapChange("맵 이동·로딩 복귀");
                    await WatchDungeonAsync(dungeon, r.Value, env.Value?.GameSpaceDisplayName, ct);
                    var sig = Signature(r.Value);
                    var first = last == null;
                    var why = first ? "감시 시작" : !wasConnected ? "연결 복구" : sig != last ? "캐릭터 변경" : null;
                    (last, wasConnected) = (sig, true);
                    if (why == null) continue;
                    log.LogInformation("{Why}: 캐릭터 정보를 바로 새로 읽습니다 ({Sig})", why, sig);
                    await session.RecordNowAsync(ct);
                    if (first) continue;
                    foreach (var wait in Settle)
                    {
                        await Task.Delay(wait, ct);
                        q.Invalidate("get_my_info");
                        await session.RecordNowAsync(ct);
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

    /// <summary>던전 안이었거나 요일 던전 지역일 때만 활동을 읽는다. Cleared로 바뀐 순간 로그를 남기고 숙제 관찰을 예약한다.</summary>
    private async Task WatchDungeonAsync(DungeonClearDetector dungeon, CharacterInfo me, string? map, CancellationToken ct)
    {
        if (!dungeon.ShouldRead(map, observer.Catalog)) return;
        var act = await q.Get<ActivityInfo>("get_activity", ct, options.QueryCacheTtl);
        if (!act.Ok || !dungeon.Observe(act.Value)) return;
        log.LogInformation("던전 클리어 감지: {Character} / 맵 {Map} / {At:HH:mm:ss}", $"{me.RealmName}_{me.JobName}", map ?? "(알 수 없음)", DateTime.Now);
        observer.RequestAfterDungeonCleared();
    }

    /// <summary>캐릭터 선택창의 빈 정보도 표식이 되므로 선택창 → 캐릭터 진입이 전환으로 잡힌다.</summary>
    internal static string Signature(CharacterInfo c) =>
        $"{c.RealmName}|{c.JobName}|{c.Level}|{c.Title}|{c.CombatScore?.Value}";
}
