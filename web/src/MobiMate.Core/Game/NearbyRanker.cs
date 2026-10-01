namespace MobiMate;

public enum NearbyRelation { Friend = 0, Party = 1, Guild = 2, Other = 3 }

/// <param name="IsStronger">내 전투력보다 높으면 true (내 전투력을 모르면 false)</param>
public sealed record NearbyEntry(NearPcItem Pc, NearbyRelation Relation, bool IsStronger)
{
    /// <summary>"Lv.100" 뒤에 붙는 관계 표기. 길드가 없으면 "(길드 없음)" (FR-DT-06).</summary>
    public string RelationLabel => Relation switch
    {
        NearbyRelation.Party => "파티원",
        NearbyRelation.Friend => "친구",
        NearbyRelation.Guild => "길드원",
        _ => Pc.HasGuild ? "" : "길드 없음"
    };
}

/// <summary>주변 플레이어 정렬 (v1.5, 承雲 확정): 관계(친구 > 파티원 > 길드원 > 기타) → 전투력 높은 순 → 가까운 순. 친구이면서 파티원이면 친구로 본다.</summary>
public static class NearbyRanker
{
    public static NearbyRelation RelationOf(NearPcItem p) =>
        p.IsFriend ? NearbyRelation.Friend
        : p.IsInParty ? NearbyRelation.Party
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
