using MobiMateHomeWork.Hosting;

namespace MobiMateHomeWork.Services;

/// <summary>
/// 캐릭터 전환 감시. "캐릭터가 바뀌었다"를 몇 초 안에 스스로 알아챈다.
/// 접속 중에는 가벼운 get_my_info 한 번으로 서버·직업·레벨·칭호·전투력의 표식을 비교하고, 달라졌거나 연결이 막 돌아왔으면
/// 곧바로 헤더를 새로 읽는다. 전환 직후에는 재화·칭호가 아직 이전 캐릭터 것일 수 있어 몇 초 뒤 두 번 더 읽는다.
/// </summary>
public sealed class CharacterWatcher(GameQueries q, CharacterSession session, StatusMonitor status, AppOptions options, ILogger<CharacterWatcher> log) : BackgroundService
{
    private static readonly TimeSpan[] Settle = { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8) };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (options.WatchInterval <= TimeSpan.Zero) return;
        using var timer = new PeriodicTimer(options.WatchInterval);
        string? last = null;
        var wasConnected = false;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var connected = status.State == GameConnection.Connected;
                if (!connected) { wasConnected = false; continue; }
                try
                {
                    var r = await q.Get<CharacterInfo>("get_my_info", ct);
                    if (!r.Ok || r.Value == null) continue;
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

    /// <summary>캐릭터 선택창의 빈 정보도 표식이 되므로 선택창 → 캐릭터 진입이 전환으로 잡힌다.</summary>
    internal static string Signature(CharacterInfo c) =>
        $"{c.RealmName}|{c.JobName}|{c.Level}|{c.Title}|{c.CombatScore?.Value}";
}
