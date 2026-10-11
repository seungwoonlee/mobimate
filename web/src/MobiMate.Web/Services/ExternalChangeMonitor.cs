using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

/// <summary>
/// 같은 저장 폴더를 쓰는 다른 프로그램(숙제 앱)이 공유 파일을 바꿨는지 감지해 화면에 알린다 (공유 저장 패치).
/// 폴더 감시(공유 파일 4개만, 300ms 묶음) + 10초 폴링. 신호가 오면 각 저장소가 자기가 마지막으로 읽거나 쓴 상태와 비교해
/// 남이 바꿨을 때만 다시 읽고(자기 쓰기는 거른다), 화면이 이미 듣는 이벤트로 알린다:
/// 숙제는 homework.changed(reason = external), 캐릭터 기록은 state.changed(profile), 계정은 state.changed(accounts).
/// </summary>
public sealed class ExternalChangeMonitor(MobiMateOptions options, HomeworkService homework, SnapshotManager snapshots, AccountStore accounts,
    SseHub hub, ILogger<ExternalChangeMonitor> log) : IHostedService
{
    private SharedStorageWatcher? _watcher;

    public Task StartAsync(CancellationToken ct)
    {
        if (options.ExternalWatch)
            _watcher = new SharedStorageWatcher(options.StorageDir, Check, options.ExternalWatchDebounce, options.ExternalWatchPoll);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _watcher?.Dispose();
        _watcher = null;
        return Task.CompletedTask;
    }

    /// <summary>외부 변경을 확인하고 바뀐 것을 알린다 (감시 신호가 부른다. 테스트는 직접 부를 수 있다).</summary>
    public void Check()
    {
        try
        {
            if (homework.RefreshExternal())
                hub.Broadcast("homework.changed", new { characterKey = (string?)null, ids = Array.Empty<string>(), reason = "external" });
            if (snapshots.RefreshExternal())
                hub.Broadcast("state.changed", new { keys = new[] { "profile" } });
            if (accounts.RefreshExternal())
                hub.Broadcast("state.changed", new { keys = new[] { "accounts" } });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogDebug(ex, "외부 변경 확인 실패(다음 신호에 다시 시도)");
        }
    }
}
