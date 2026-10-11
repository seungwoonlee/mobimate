namespace MobiMate;

public enum NearbyRelation { Friend = 0, Guild = 1, Other = 2 }

/// <param name="IsStronger">내 전투력보다 높으면 true (내 전투력을 모르면 false)</param>
public sealed record NearbyEntry(NearPcItem Pc, NearbyRelation Relation, bool IsStronger)
{
    /// <summary>"Lv.100" 뒤에 붙는 관계 표기. 길드가 없으면 "(길드 없음)" (FR-DT-06).</summary>
    public string RelationLabel => Relation switch
    {
        NearbyRelation.Friend => "친구",
        NearbyRelation.Guild => "길드원",
        _ => Pc.HasGuild ? "" : "길드 없음"
    };
}

/// <summary>
/// 주변 플레이어 정렬 (v1.5, 承雲 확정): 관계(친구 > 길드원 > 기타) → 전투력 높은 순 → 가까운 순.
/// 게임 정보의 IsInParty는 파티에 가입하지 않았는데도 true로 오는 것이 확인되어(2026-10-02) "내 파티원" 판정에 쓰지 않는다.
/// 내가 파티에 있는지 알려 주는 정보가 따로 없어, 그 사람이 어떤 파티에 속해 있다는 뜻으로 보이는 이 값은 무시한다.
/// </summary>
public static class NearbyRanker
{
    public static NearbyRelation RelationOf(NearPcItem p) =>
        p.IsFriend ? NearbyRelation.Friend
        : p.IsSameGuild ? NearbyRelation.Guild
        : NearbyRelation.Other;

    public static IReadOnlyList<NearbyEntry> Rank(IEnumerable<NearPcItem>? pcs, long myCombatScore)
    {
        if (pcs == null) return Array.Empty<NearbyEntry>();
        return pcs
            .Select(p => new NearbyEntry(p, RelationOf(p), myCombatScore > 0 && p.CombatScore > myCombatScore))
            .OrderBy(e => e.Relation)
            .ThenByDescending(e => e.Pc.CombatScore)
            .ThenBy(e => e.Pc.Distance)
            .ToList();
    }
}
