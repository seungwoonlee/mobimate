using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MobiMate;

/// <summary>
/// 저장 폴더 하나에 대한 공유 잠금 (공유 저장 패치). 같은 폴더를 쓰는 모든 프로세스(웹앱·숙제 앱)와 모든 저장소가
/// 이름 있는 뮤텍스 하나를 같이 쓴다: Local\MobiMate.Data.{폴더 전체 경로 소문자 해시}.
/// - 프로세스 안에서는 먼저 폴더별 모니터를 잡고, 그 안에서 뮤텍스를 잡는다 (순서 고정: 서비스 잠금 → 모니터 → 뮤텍스 → 파일별 잠금).
/// - 뮤텍스는 스레드 소유다. 그래서 <see cref="Run{T}"/>는 동기 구간만 받는다: await를 걸쳐 쥐지 않는다.
/// - 같은 스레드에서 중첩 호출해도 된다(모니터·뮤텍스 모두 재진입 가능).
/// - 이름은 Local\ 접두사(현재 로그인 세션 범위)를 유지한다: 웹앱과 숙제 앱은 같은 사용자의 같은 세션에서 실행되므로 서로 보이고,
///   다른 사용자 세션의 프로세스와 불필요하게 잠금을 다투지 않는다(Global\은 권한 문제로 만들기 실패할 수 있다).
/// - 대기는 10초까지다. 넘으면 IOException으로 작업을 미룬다(영원히 멈추지 않게).
/// - 다른 프로세스가 뮤텍스를 쥔 채 죽으면 AbandonedMutexException이 나는데, 이때도 뮤텍스는 우리가 소유한 것이므로 획득으로 처리한다.
/// </summary>
public sealed class SharedStorageLock
{
    private static readonly ConcurrentDictionary<string, SharedStorageLock> All = new();
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly Mutex? _mutex;

    public string Name { get; }

    /// <summary>뮤텍스를 만들지 못해 프로세스 안 잠금만 쓰는 상태가 되면 그 이유. 호스트가 로그로 드러낸다(없으면 null).</summary>
    public static string? DegradedReason { get; private set; }

    /// <summary>뮤텍스를 만들지 못했을 때 발생한다(두 프로세스 동시 쓰기 보호가 꺼진 상태).</summary>
    public static event Action<string>? Degraded;

    private SharedStorageLock(string dir)
    {
        Name = MutexNameFor(dir);
        try { _mutex = new Mutex(false, Name); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            // 뮤텍스를 만들 수 없으면 프로세스 안 잠금만으로 계속한다(웹앱 단독 동작은 그대로)
            var reason = $"공유 저장 뮤텍스를 만들지 못해 프로세스 안 잠금만 씁니다({Name}): {ex.Message}";
            System.Diagnostics.Trace.TraceWarning(reason);
            DegradedReason = reason;
            Degraded?.Invoke(reason);
        }
    }

    public static SharedStorageLock For(string dir) => All.GetOrAdd(Normalize(dir), d => new SharedStorageLock(d));

    /// <summary>폴더 경로(전체 경로·소문자)의 해시로 만든 뮤텍스 이름. 같은 폴더면 두 앱이 같은 이름을 얻는다.</summary>
    public static string MutexNameFor(string dir)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(dir)));
        return "Local\\MobiMate.Data." + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static string Normalize(string dir) =>
        Path.GetFullPath(dir).TrimEnd('\\', '/').ToLowerInvariant();

    /// <summary>잠금 안에서 동기 구간을 실행한다. 잠금을 얻지 못하면(제한 시간 초과) IOException.</summary>
    public T Run<T>(Func<T> action)
    {
        if (!Monitor.TryEnter(_gate, WaitLimit)) throw new IOException("공유 저장소 잠금을 얻지 못했습니다(프로세스 안).");
        try
        {
            var owned = false;
            if (_mutex != null)
            {
                try { owned = _mutex.WaitOne(WaitLimit); }
                catch (AbandonedMutexException) { owned = true; }   // 이전 소유자가 놓지 않고 죽음: 지금은 우리가 소유
                if (!owned) throw new IOException("공유 저장소 잠금을 얻지 못했습니다(다른 프로그램이 오래 쥐고 있음).");
            }
            try { return action(); }
            finally { if (owned) _mutex!.ReleaseMutex(); }
        }
        finally { Monitor.Exit(_gate); }
    }

    public void Run(Action action) => Run<object?>(() => { action(); return null; });
}

/// <summary>
/// 파일의 마지막 쓰기 시각(100ns 단위)·크기·파일 고유 번호(볼륨 일련번호 + 파일 인덱스). 파일이 바뀌었는지 판정하는 근거다.
/// 모든 쓰기가 임시 파일 → File.Replace/File.Move라서 쓰기마다 새 파일이 대상 이름을 차지하고 고유 번호가 바뀐다:
/// 시각 해상도가 거칠어 같은 틱·같은 길이로 연달아 쓴 경우도 구분된다. 고유 번호를 읽지 못하면 <see cref="Unknown"/>이고
/// 같은 상태로 치지 않는다(안전한 쪽 = 바뀐 것으로 본다).
/// </summary>
public readonly record struct FileStamp(bool Exists, long Ticks, long Length, uint Volume = 0, ulong FileId = 0, bool Unknown = false)
{
    public static FileStamp Of(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return default;
            var stamp = new FileStamp(true, fi.LastWriteTimeUtc.Ticks, fi.Length);
            if (!OperatingSystem.IsWindows()) return stamp;
            return NativeFileId.TryGet(path, out var volume, out var id) ? stamp with { Volume = volume, FileId = id } : stamp with { Unknown = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    /// <summary>마지막으로 기억한 상태와 같은가. 고유 번호를 모르면 같다고 하지 않는다.</summary>
    public bool SameAs(FileStamp other) => !Unknown && !other.Unknown && this == other;
}

/// <summary>
/// GetFileInformationByHandle로 파일 고유 번호를 읽는다. 접근 권한 0(속성 조회만)·FileShare 전부로 열어 바로 닫으므로
/// 다른 프로그램이 쓰기 중이거나 배타적으로 열어 둔 파일에도 방해하지 않고 열린다(열기 비용은 수십 µs).
/// </summary>
internal static class NativeFileId
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out ByHandleFileInformation info);

    private const uint ShareAll = 0x1 | 0x2 | 0x4;   // READ | WRITE | DELETE
    private const uint OpenExisting = 3;

    public static bool TryGet(string path, out uint volume, out ulong id)
    {
        volume = 0; id = 0;
        using var h = CreateFileW(Path.GetFullPath(path), 0, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid || !GetFileInformationByHandle(h, out var info)) return false;
        volume = info.VolumeSerialNumber;
        id = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
        return true;
    }
}

public enum RefreshResult
{
    /// <summary>파일이 마지막으로 읽거나 쓴 뒤 바뀌지 않았다.</summary>
    Unchanged,
    /// <summary>새로 읽었다. value가 null이면 빈 기록(첫 실행·격리).</summary>
    Reloaded,
    /// <summary>읽지 못했다(잠김, 교체 중). 메모리 값을 그대로 두고 이번 작업을 미룬다.</summary>
    Unavailable,
    /// <summary>파일이 사라졌거나 격리됐다. 메모리 값이 기준이다(다음 저장이 파일을 다시 만든다).</summary>
    KeepMemory,
}

/// <summary>
/// 공유 파일 하나의 "내가 마지막으로 읽거나 쓴 상태"를 기억해, 다른 프로세스가 바꿨는지 알아낸다.
/// 자기 쓰기는 <see cref="MarkWritten"/>으로 기억하므로 바뀐 것으로 치지 않는다.
/// 모든 메서드는 <see cref="SharedStorageLock"/> 안에서 부른다(<see cref="IsStale"/> 제외: 읽기만 하는 빠른 확인).
/// </summary>
public sealed class SharedFileTracker(string path)
{
    /// <summary>직전까지 있던 파일이 이 시간 넘게 계속 없으면 정말 지워진 것으로 본다.</summary>
    private static readonly long GoneAfterMs = 10_000;
    private const int MissingRetries = 3;
    private const int MissingRetryDelayMs = 50;

    /// <summary>테스트용 훅: 파일이 없어 재시도할 때마다(0부터) 대기 직전에 불린다. 시간 경쟁 없이 "교체 중" 순서를 재현한다. 스레드별.</summary>
    [ThreadStatic] internal static Action<int>? OnMissingRetry;

    private readonly JsonFileStore _store = new();
    private FileStamp _stamp;
    private bool _loadedOnce;
    private long _missingSinceMs = -1;

    public string Path { get; } = path;

    /// <summary>마지막으로 읽거나 쓴 뒤 파일이 바뀌었는가(또는 아직 읽은 적이 없는가). 잠금 없이 부를 수 있다.</summary>
    public bool IsStale => !_loadedOnce || !FileStamp.Of(Path).SameAs(_stamp);

    /// <summary>
    /// 필요하면 다시 읽는다. <paramref name="hasMemory"/>가 false면(메모리에 쓸 만한 값이 없음) 무조건 읽는다.
    /// 읽기는 <see cref="JsonFileStore.TryLoad{T}"/>만 쓴다: 잠김(Failed)은 Unavailable, 직전까지 있던 파일이 없으면 교체 중으로 보고
    /// 50ms×3 다시 시도한 뒤에도 없으면 Unavailable(10초 넘게 없으면 KeepMemory).
    /// </summary>
    public RefreshResult Refresh<T>(bool hasMemory, out T? value) where T : class
    {
        value = null;
        var before = FileStamp.Of(Path);
        var force = !hasMemory || !_loadedOnce;
        if (!force && before.SameAs(_stamp)) return RefreshResult.Unchanged;

        var hadFile = _loadedOnce && _stamp.Exists;
        var status = _store.TryLoad(Path, out value);
        if (status == LoadStatus.Missing && hadFile && _missingSinceMs < 0)   // 이미 없는 채로 기다리는 중이면 재시도(대기)를 반복하지 않는다
        {
            for (var i = 0; i < MissingRetries && status == LoadStatus.Missing; i++)
            {
                OnMissingRetry?.Invoke(i);
                Thread.Sleep(MissingRetryDelayMs);
                before = FileStamp.Of(Path);
                status = _store.TryLoad(Path, out value);
            }
        }

        switch (status)
        {
            case LoadStatus.Loaded:
                _stamp = before;
                _loadedOnce = true;
                _missingSinceMs = -1;
                return RefreshResult.Reloaded;

            case LoadStatus.Failed:
                return RefreshResult.Unavailable;

            case LoadStatus.Quarantined:
                // 손상 파일은 .bak으로 격리됐다. 이미 읽은 값이 있으면 그것을 지키고, 없으면 빈 기록으로 시작한다(원래 동작)
                _stamp = FileStamp.Of(Path);
                _loadedOnce = true;
                _missingSinceMs = -1;
                value = null;
                return hasMemory ? RefreshResult.KeepMemory : RefreshResult.Reloaded;

            default:   // Missing
                if (!hadFile)
                {
                    // 원래 없던 파일(첫 실행): 빈 기록으로 시작
                    _stamp = default;
                    _loadedOnce = true;
                    return RefreshResult.Reloaded;
                }
                var now = Environment.TickCount64;
                if (_missingSinceMs < 0) _missingSinceMs = now;
                if (now - _missingSinceMs < GoneAfterMs) return RefreshResult.Unavailable;
                _stamp = default;   // 정말 지워졌다: 메모리(없으면 빈 기록)가 기준
                return hasMemory ? RefreshResult.KeepMemory : RefreshResult.Reloaded;
        }
    }

    /// <summary>방금 내가 파일을 썼다: 이 상태를 기억해 자기 쓰기를 바뀐 것으로 치지 않는다.</summary>
    public void MarkWritten()
    {
        _stamp = FileStamp.Of(Path);
        _loadedOnce = true;
        _missingSinceMs = -1;
    }
}

/// <summary>
/// 공유 파일 4개의 변경 감지 신호기. FileSystemWatcher(공유 파일 이름만, 300ms 묶음) + 10초 폴링 폴백.
/// 이 클래스는 "바뀌었을 수 있다"는 신호만 낸다. 실제로 남이 바꿨는지는 각 저장소가 자기 기억(<see cref="SharedFileTracker"/>)과 비교해 가린다.
/// 신호 처리기는 한 번에 하나만 돈다(겹치면 한 번 더 도는 것으로 합친다).
/// </summary>
public sealed class SharedStorageWatcher : IDisposable
{
    public static readonly string[] SharedFileNames =
        ["character_snapshots.json", "character_history_db.json", "homework_records.json", "accounts.json"];

    private readonly Action _onSignal;
    private readonly TimeSpan _debounce;
    private readonly FileSystemWatcher? _fsw;
    private readonly Timer _debounceTimer;
    private readonly Timer _pollTimer;
    private readonly object _gate = new();
    private bool _running, _again, _disposed;

    public SharedStorageWatcher(string dir, Action onSignal, TimeSpan? debounce = null, TimeSpan? poll = null)
    {
        _onSignal = onSignal;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        _debounceTimer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        _pollTimer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            Directory.CreateDirectory(dir);
            _fsw = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, EnableRaisingEvents = false };
            _fsw.Changed += (_, e) => OnEvent(e.Name);
            _fsw.Created += (_, e) => OnEvent(e.Name);
            _fsw.Deleted += (_, e) => OnEvent(e.Name);
            _fsw.Renamed += (_, e) => { OnEvent(e.Name); OnEvent(e.OldName); };
            _fsw.Error += (_, e) => System.Diagnostics.Trace.TraceWarning($"폴더 감시 오류(폴링으로 계속): {e.GetException().Message}");
            _fsw.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            System.Diagnostics.Trace.TraceWarning($"폴더 감시를 시작하지 못했습니다(폴링으로 계속): {ex.Message}");
            _fsw?.Dispose();
            _fsw = null;
        }
        var pollEvery = poll ?? TimeSpan.FromSeconds(10);
        _pollTimer.Change(pollEvery, pollEvery);
    }

    /// <summary>공유 파일 이름인가 (.tmp.*·.bak·.corrupted.* 같은 부산물은 아니다).</summary>
    public static bool IsSharedFile(string? name) =>
        name != null && SharedFileNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    private void OnEvent(string? name)
    {
        if (!IsSharedFile(name)) return;
        lock (_gate) { if (!_disposed) _debounceTimer.Change(_debounce, Timeout.InfiniteTimeSpan); }   // 마지막 이벤트 뒤 300ms에 한 번
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_running) { _again = true; return; }
            _running = true;
        }
        while (true)
        {
            try { _onSignal(); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"외부 변경 처리 실패: {ex.Message}"); }
            lock (_gate)
            {
                if (_again && !_disposed) { _again = false; continue; }
                _running = false;
                return;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; }
        _fsw?.Dispose();
        _debounceTimer.Dispose();
        _pollTimer.Dispose();
    }
}
