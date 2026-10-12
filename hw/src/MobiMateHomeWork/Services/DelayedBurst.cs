namespace MobiMateHomeWork.Services;

/// <summary>
/// "트리거 뒤 정해진 시점마다 한 번씩 실행"을 겹치지 않게 합친다. <see cref="Request"/>를 부르면 시점 목록(예: +2초, +8초)을 새로 시작하고,
/// 아직 끝나지 않은 앞선 요청의 남은 시점은 버린다(트리거가 겹치면 하나로 합침). 대기는 주입한 delay로 하므로 테스트는 가짜 시계로 진행한다.
/// 이미 시작한 action은 끊지 않는다.
/// </summary>
public sealed class DelayedBurst : IDisposable
{
    private readonly IReadOnlyList<TimeSpan> _offsets;
    private readonly Func<CancellationToken, Task> _action;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _burst;
    private int _requests;

    public DelayedBurst(IEnumerable<TimeSpan> offsets, Func<CancellationToken, Task> action, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _offsets = offsets.Where(o => o > TimeSpan.Zero).OrderBy(o => o).ToList();
        _action = action;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>요청 횟수(합쳐진 것 포함).</summary>
    public int Requests => Volatile.Read(ref _requests);

    public void Request()
    {
        if (_offsets.Count == 0 || _stop.IsCancellationRequested) return;
        CancellationTokenSource mine;
        lock (_lock)
        {
            _burst?.Cancel();
            mine = _burst = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            Interlocked.Increment(ref _requests);
        }
        _ = Task.Run(() => RunAsync(mine));
    }

    private async Task RunAsync(CancellationTokenSource burst)
    {
        try
        {
            var done = TimeSpan.Zero;
            foreach (var at in _offsets)
            {
                await _delay(at - done, burst.Token);
                done = at;
                if (burst.IsCancellationRequested) return;
                await _action(_stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* action이 스스로 기록한다 */ }
        finally
        {
            lock (_lock) { if (ReferenceEquals(_burst, burst)) _burst = null; }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        lock (_lock) _burst?.Cancel();
    }
}

/// <summary>
/// 맵 이동 알아채기(순수). 4초 감시가 읽을 때마다 (실제 캐릭터인가, 현재 맵)을 넣는다.
/// 로딩(캐릭터 정보가 빔) → 실제 캐릭터로 돌아온 순간, 또는 같은 캐릭터의 맵 이름이 바뀐 순간에 true.
/// 맵이 비어 읽히면 마지막 맵을 유지한다.
/// </summary>
public sealed class MapChangeDetector
{
    private bool? _wasReal;
    private string? _map;

    public bool Observe(bool isReal, string? map)
    {
        var m = string.IsNullOrWhiteSpace(map) ? null : map.Trim();
        if (!isReal) { _wasReal = false; return false; }
        var trigger = _wasReal == false || (_wasReal == true && m != null && _map != null && !string.Equals(m, _map, StringComparison.Ordinal));
        _wasReal = true;
        if (m != null) _map = m;
        return trigger;
    }

    /// <summary>연결이 끊기면 처음부터.</summary>
    public void Reset() { _wasReal = null; _map = null; }
}
