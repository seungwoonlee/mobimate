namespace MobiMate;

/// <summary>
/// 던전 클리어 순간 알아채기(순수). Cleared는 보스 처치 순간부터 약 17초만 유지되므로(2026-10-12 실측) 4초 감시가 놓치지 않게 한다.
/// 활동(get_activity)은 던전 안이었거나 요일 던전 지역일 때만 읽는다: 평상시 호출 수를 늘리지 않기 위해서다.
/// 감시가 활동을 읽을 때마다 <see cref="Observe"/>에 넣고, true(막 Cleared로 바뀜)가 오면 숙제 관찰을 예약한다.
/// </summary>
public sealed class DungeonClearDetector
{
    private bool _inside;
    private bool _cleared;

    /// <summary>직전 읽기에서 던전 안(입장·진행·클리어)이었다.</summary>
    public bool Inside => _inside;

    /// <summary>이번 틱에 활동을 읽어야 하는가: 직전 읽기에서 던전 안이었거나, 현재 맵이 클리어 판정 대상 지역일 때.</summary>
    public bool ShouldRead(string? currentMap, HomeworkCatalog catalog) => _inside || catalog.IsDungeonClearArea(currentMap);

    /// <summary>활동을 읽은 결과를 넣는다. State가 Cleared가 아니던 상태에서 Cleared로 바뀐 순간에만 true. 활동을 못 읽었으면(null) 아무것도 바꾸지 않는다.</summary>
    public bool Observe(ActivityInfo? activity)
    {
        if (activity == null) return false;
        var d = activity.Dungeon;
        var cleared = d?.IsCleared == true;
        _inside = cleared || d?.IsInside == true;
        var trigger = cleared && !_cleared;
        _cleared = cleared;
        return trigger;
    }

    /// <summary>연결이 끊기면 처음부터.</summary>
    public void Reset() { _inside = false; _cleared = false; }
}
