using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MobiMateHomeWork.Hosting;

/// <summary>
/// 단일 인스턴스: 명명 뮤텍스 <c>Local\MobiMateHomeWork.Single.&lt;사용자 SID&gt;</c>로 첫 인스턴스를 가린다(웹앱과 같은 방식, 이름은 다르다).
/// 이미 실행 중이면 두 번째 실행은 명명 이벤트로 "창을 앞으로" 신호만 보내고 끝난다. 호스트를 만들기 전에 판단한다.
/// 이벤트는 첫 인스턴스가 뮤텍스를 쥔 직후에 만든다 — 뮤텍스가 보이면 이벤트도 곧 보인다(두 번째 실행은 몇 번 재시도).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string Suffix = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    public static string MutexName => $@"Local\MobiMateHomeWork.Single.{Suffix}";
    public static string ShowEventName => $@"Local\MobiMateHomeWork.Show.{Suffix}";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _show;
    private readonly string _showEventName;
    private RegisteredWaitHandle? _registration;
    public bool IsFirst { get; }

    /// <param name="mutexName">테스트용 이름 바꾸기(기본은 <see cref="MutexName"/>).</param>
    /// <param name="showEventName">테스트용 이름 바꾸기(기본은 <see cref="ShowEventName"/>).</param>
    public SingleInstance(string? mutexName = null, string? showEventName = null)
    {
        _showEventName = showEventName ?? ShowEventName;
        _mutex = new Mutex(initiallyOwned: true, mutexName ?? MutexName, out var created);
        IsFirst = created;
        if (created) _show = new EventWaitHandle(false, EventResetMode.AutoReset, _showEventName);
    }

    /// <summary>첫 인스턴스에서 "창 앞으로" 신호를 받으면 <paramref name="onShow"/>를 부른다(스레드 풀 스레드. UI 스레드로 넘기는 것은 호출자 몫).</summary>
    public void Listen(Action onShow)
    {
        if (_show == null) return;
        _registration = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => { try { onShow(); } catch { } }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>이미 실행 중인 인스턴스에 창을 앞으로 가져오라고 알린다. 실패하면 false.</summary>
    public static bool SignalExisting(string? showEventName = null)
    {
        showEventName ??= ShowEventName;
        AllowSetForegroundWindow(-1);   // 첫 인스턴스가 이 실행(사용자가 방금 연 것)의 전경 권한을 이어받아 창을 앞으로 올릴 수 있게
        for (var i = 0; i < 5; i++)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(showEventName, out var ev))
                {
                    using (ev) { ev.Set(); }
                    return true;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            Thread.Sleep(300);
        }
        return false;
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _show?.Dispose();
        if (IsFirst) try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}

/// <summary>
/// 옛 웹앱 감지. 공유 저장 패치 이전의 웹앱은 기록을 메모리에 들고 통째로 저장하므로, 같은 폴더를 같이 쓰면 서로 덮어쓴다.
/// 판단: <c>MobiMateWeb</c> 프로세스가 떠 있는데 그 저장 폴더의 공유 뮤텍스(<see cref="SharedStorageLock.MutexNameFor"/>)가 없으면 옛 웹앱이다.
/// 패치된 웹앱은 시작할 때 저장소(SnapshotManager·AccountStore·HomeworkStore)를 만들며 뮤텍스를 함께 만든다.
/// 버전 번호는 MMdd 형식이라 해가 바뀌면 비교가 틀리므로 쓰지 않는다.
/// <b>반드시 이 앱이 저장소를 만들기 전에 확인한다</b> — 이 앱이 먼저 뮤텍스를 만들면 항상 "있음"으로 보인다.
/// </summary>
public static class LegacyWebGuard
{
    public const string WebProcessName = "MobiMateWeb";

    /// <summary>방금 뜬 웹앱은 저장소를 만들기 전일 수 있다: 이 시간 안에 시작했으면 뮤텍스가 생길 때까지 잠시 기다린다.</summary>
    private static readonly TimeSpan YoungProcess = TimeSpan.FromSeconds(30);

    public readonly record struct WebProc(int Pid, DateTime Start);

    /// <summary>
    /// 옛 웹앱 프로세스의 PID 목록(없으면 빈 목록). <paramref name="mutexExists"/>·<paramref name="runningWeb"/>는 테스트에서 바꿔 끼운다.
    /// 뮤텍스는 이 앱이 저장소를 만들면 항상 보이게 되므로, 시작 때 한 번만 이 판정을 하고 이후에는 <see cref="LegacyWebWatcher"/>가 그 PID가 살아 있는지만 본다.
    /// </summary>
    public static async Task<IReadOnlyList<int>> FindLegacyWebAsync(string storageDir, CancellationToken ct = default,
        Func<string, bool>? mutexExists = null, Func<IReadOnlyList<WebProc>>? runningWeb = null)
    {
        mutexExists ??= MutexExists;
        runningWeb ??= WebProcesses;
        var procs = runningWeb();
        if (procs.Count == 0) return [];

        var name = SharedStorageLock.MutexNameFor(storageDir);
        if (mutexExists(name)) return [];

        // 방금 시작한 웹앱이 있으면 몇 초 기다리며 뮤텍스가 생기는지 본다
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && runningWeb().Any(s => DateTime.Now - s.Start < YoungProcess))
        {
            await Task.Delay(500, ct);
            if (mutexExists(name)) return [];
        }
        return [.. runningWeb().Select(p => p.Pid)];
    }

    private static bool MutexExists(string name)
    {
        try
        {
            if (!Mutex.TryOpenExisting(name, out var m)) return false;
            m.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException) { return true; }   // 있는데 열 권한이 없다: 있는 것으로 본다
    }

    /// <summary>같은 로그인 세션에서 실행 중인 웹앱 프로세스. 뮤텍스(Local\)는 세션별이라 다른 세션 프로세스는 제외한다.</summary>
    public static IReadOnlyList<WebProc> WebProcesses()
    {
        var result = new List<WebProc>();
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName(WebProcessName))
        {
            using (p)
            {
                try
                {
                    if (p.SessionId != session || p.HasExited) continue;
                    result.Add(new WebProc(p.Id, p.StartTime));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return result;
    }
}

/// <summary>
/// 시작 때 옛 웹앱으로 판정된 프로세스들이 모두 끝나면(웹앱을 껐거나 패치판으로 바꿔 다시 켬) 경고를 거둔다. 주기적으로(기본 30초) 본다.
/// 새로 뜬 웹앱은 판정하지 않는다 — 이 앱이 이미 공유 뮤텍스를 만들었기 때문에 뮤텍스로는 패치 여부를 가릴 수 없다.
/// </summary>
public sealed class LegacyWebWatcher : IDisposable
{
    private readonly Timer _timer;
    private readonly HashSet<int> _pids;
    private readonly Func<IReadOnlyList<LegacyWebGuard.WebProc>> _running;
    private readonly Action _onGone;
    private int _done;

    public LegacyWebWatcher(IEnumerable<int> legacyPids, Action onGone, TimeSpan? every = null, Func<IReadOnlyList<LegacyWebGuard.WebProc>>? running = null)
    {
        _pids = [.. legacyPids];
        _onGone = onGone;
        _running = running ?? LegacyWebGuard.WebProcesses;
        var t = every ?? TimeSpan.FromSeconds(30);
        _timer = new Timer(_ => Check(), null, t, t);
    }

    /// <summary>살아 있는 옛 웹앱이 없으면 한 번만 onGone을 부르고 멈춘다.</summary>
    public void Check()
    {
        if (Volatile.Read(ref _done) == 1) return;
        var alive = _running();
        if (_pids.Any(pid => alive.Any(a => a.Pid == pid))) return;
        if (Interlocked.Exchange(ref _done, 1) == 1) return;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _onGone();
    }

    public void Dispose() => _timer.Dispose();
}
