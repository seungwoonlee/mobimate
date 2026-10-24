namespace MobiMate.Web.Services;

/// <summary>
/// 오토 의심 표식을 쌓는 서비스(2026-10-22). 던전·레이드 안에서는 <see cref="AutoWatcher"/>가 1초마다 주변 플레이어를 넣고,
/// 주변 화면(GameViews.NearbyAsync)이 의심 중인 사람을 꺼내 표식을 붙인다. 판정은 순수 클래스 <see cref="AutoSuspicionTracker"/>가 한다.
/// 던전·레이드 안인지는 캐릭터 감시(CharacterWatcher, 4초)가 알려 준다.
/// </summary>
public sealed class AutoWatch
{
    /// <summary>"안"이라는 알림이 이 시간보다 오래되면 밖으로 본다(감시가 멈췄을 때 계속 읽지 않게).</summary>
    public static readonly TimeSpan InstanceFreshFor = TimeSpan.FromSeconds(15);

    private readonly object _lock = new();
    private readonly AutoSuspicionTracker _tracker = new();
    private bool _inInstance;
    private DateTimeOffset _instanceAt = DateTimeOffset.MinValue;
    private string? _space;
    private readonly AutoWatchRecorder? _recorder;

    public AutoWatch(AutoWatchRecorder? recorder = null) => _recorder = recorder;

    /// <summary>
    /// 감시가 던전·레이드 안인지 알린다(4초마다). space는 지금 맵 이름.
    /// 어비스에서 어비스로 옮기면 "밖으로 나갔다 들어옴"이 없어 이전 방 파티원이 "안 보임"으로 남았다(2026-10-23): 맵이 바뀌면 처음부터 다시 쌓는다.
    /// 맵 이름이 비는 로딩 중에는 바뀐 것으로 보지 않는다.
    /// </summary>
    public void SetInstance(bool inside, DateTimeOffset? now = null, string? space = null)
    {
        lock (_lock)
        {
            _inInstance = inside; _instanceAt = now ?? DateTimeOffset.UtcNow;
            var s = space?.Trim();
            if (!inside) { _space = null; return; }
            if (string.IsNullOrEmpty(s)) return;
            if (_space != null && _space != s) _tracker.Reset();
            _space = s;
        }
    }

    public bool InInstance(DateTimeOffset? now = null)
    {
        lock (_lock) return _inInstance && (now ?? DateTimeOffset.UtcNow) - _instanceAt < InstanceFreshFor;
    }

    /// <param name="inInstance">던전·레이드 안인가. 밖이면 쌓아 둔 것을 지운다.</param>
    /// <param name="me">거리를 읽은 때의 내 위치(없으면 거리 변화로만 어림한다)</param>
    public void Observe(DateTimeOffset now, bool inInstance, IEnumerable<NearPcItem>? players, WorldPos? me = null)
    {
        var list = (players ?? Enumerable.Empty<NearPcItem>()).ToList();
        lock (_lock)
        {
            _tracker.Observe(now, inInstance, list.Select(WatchedPlayer.From).ToList(), me);
            if (inInstance) _recorder?.Record(now, _space, me, list, _tracker.Suspects);   // 판이 끝난 뒤 기준을 다시 돌려 볼 수 있게 늘 남긴다
        }
    }

    public void Reset() { lock (_lock) _tracker.Reset(); }

    /// <summary>의심 조건이 시작되어 1초 간격으로 읽어야 하는가(아니면 평소의 드문 간격).</summary>
    public bool NeedsFast() { lock (_lock) return _tracker.NeedsFastSampling; }

    public IReadOnlyList<AutoSuspect> Suspects() { lock (_lock) return _tracker.Suspects; }
}
