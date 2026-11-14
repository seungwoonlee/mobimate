using MobiMateHomeWork.Hosting;

namespace MobiMateHomeWork.Services;

/// <summary>
/// 같은 저장 폴더를 쓰는 다른 프로그램(웹앱)이 공유 파일을 바꿨는지 감지해 화면에 알린다.
/// 폴더 감시(공유 파일 4개만, 300ms 묶음) + 10초 폴링. 신호가 오면 각 저장소가 자기가 마지막으로 읽거나 쓴 상태와 비교해
/// 남이 바꿨을 때만 다시 읽고(자기 쓰기는 거른다), 알린다:
/// 숙제는 <see cref="AppEvents.HomeworkChanged"/>(reason External), 캐릭터 기록·계정은 <see cref="AppEvents.CharactersChanged"/>.
/// 공유 뮤텍스를 만들지 못한 상태(<see cref="SharedStorageLock.Degraded"/>)는 로그 경고와 <see cref="AppWarningKind.StorageLockDegraded"/>로 알린다.
/// </summary>
public sealed class ExternalChangeMonitor(AppOptions options, HomeworkService homework, SnapshotManager snapshots, AccountStore accounts,
    AppEvents events, ILogger<ExternalChangeMonitor> log) : IHostedService
{
    private SharedStorageWatcher? _watcher;

    public Task StartAsync(CancellationToken ct)
    {
        // 공유 뮤텍스를 만들 수 없으면 웹앱과 동시에 쓸 때 보호되지 않는다: 로그와 경고로 드러낸다
        if (SharedStorageLock.DegradedReason is { } reason) OnDegraded(reason);
        SharedStorageLock.Degraded += OnDegraded;
        if (options.ExternalWatch)
        {
            var poll = options.ExternalWatchPoll;
            _watcher = new SharedStorageWatcher(options.StorageDir, Check, options.ExternalWatchDebounce,
                poll > TimeSpan.Zero ? poll : Timeout.InfiniteTimeSpan);   // 0이면 폴링을 끈다(감시만)
        }
        return Task.CompletedTask;
    }

    private void OnDegraded(string reason)
    {
        log.LogWarning("{Reason}", reason);
        events.RaiseWarning(new AppWarning(AppWarningKind.StorageLockDegraded, reason));
    }

    public Task StopAsync(CancellationToken ct)
    {
        SharedStorageLock.Degraded -= OnDegraded;
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
            {
                log.LogInformation("다른 프로그램이 숙제 기록을 바꿔 다시 읽었습니다.");
                events.RaiseHomeworkChanged(new HomeworkChange("", Array.Empty<string>(), HomeworkChangeReason.External));
            }
            var snap = snapshots.RefreshExternal();
            var acc = accounts.RefreshExternal();
            if (snap || acc)
            {
                log.LogInformation("다른 프로그램이 캐릭터·계정 기록을 바꿔 다시 읽었습니다(캐릭터 {Snap}, 계정 {Acc}).", snap, acc);
                events.RaiseCharactersChanged();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogDebug(ex, "외부 변경 확인 실패(다음 신호에 다시 시도)");
        }
    }
}
