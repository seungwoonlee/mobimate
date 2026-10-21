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

    /// <summary>감시가 던전·레이드 안인지 알린다(4초마다).</summary>
    public void SetInstance(bool inside, DateTimeOffset? now = null)
    {
        lock (_lock) { _inInstance = inside; _instanceAt = now ?? DateTimeOffset.UtcNow; }
    }

    public bool InInstance(DateTimeOffset? now = null)
    {
        lock (_lock) return _inInstance && (now ?? DateTimeOffset.UtcNow) - _instanceAt < InstanceFreshFor;
    }

    /// <param name="inInstance">던전·레이드 안인가. 밖이면 쌓아 둔 것을 지운다.</param>
    public void Observe(DateTimeOffset now, bool inInstance, IEnumerable<NearPcItem>? players)
    {
        lock (_lock) _tracker.Observe(now, inInstance, (players ?? Enumerable.Empty<NearPcItem>()).Select(WatchedPlayer.From).ToList());
    }

    public void Reset() { lock (_lock) _tracker.Reset(); }

    /// <summary>의심 조건이 시작되어 1초 간격으로 읽어야 하는가(아니면 평소의 드문 간격).</summary>
    public bool NeedsFast() { lock (_lock) return _tracker.NeedsFastSampling; }

    public IReadOnlyList<AutoSuspect> Suspects() { lock (_lock) return _tracker.Suspects; }
}
