namespace MobiMate.Web.Services;

/// <summary>
/// 오토 의심 표식을 쌓는 서비스(2026-10-22). 감시(CharacterWatcher)가 던전·레이드 안일 때만 주변 플레이어를 8초마다 넣고,
/// 주변 화면(GameViews.NearbyAsync)이 의심 중인 사람을 꺼내 표식을 붙인다. 판정은 순수 클래스 <see cref="AutoSuspicionTracker"/>가 한다.
/// </summary>
public sealed class AutoWatch
{
    private readonly object _lock = new();
    private readonly AutoSuspicionTracker _tracker = new();

    /// <param name="inInstance">던전·레이드 안인가. 밖이면 쌓아 둔 것을 지운다.</param>
    public void Observe(DateTimeOffset now, bool inInstance, IEnumerable<NearPcItem>? players)
    {
        lock (_lock) _tracker.Observe(now, inInstance, (players ?? Enumerable.Empty<NearPcItem>()).Select(WatchedPlayer.From).ToList());
    }

    public void Reset() { lock (_lock) _tracker.Reset(); }

    public IReadOnlyList<AutoSuspect> Suspects() { lock (_lock) return _tracker.Suspects; }
}
