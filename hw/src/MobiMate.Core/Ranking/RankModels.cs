namespace MobiMate;

/// <summary>랭킹 종류. 값은 넥슨 랭킹 페이지의 종류 번호(t)다: 1 전투력, 2 매력, 3 생활력, 4 종합.</summary>
public enum RankKind { Combat = 1, Attract = 2, Living = 3, Total = 4 }

/// <summary>전투력 서버 순위 등급: 10위 이내 골드, 100위 이내 주황, 1000위 이내 핑크, 10000위 이내 보라.</summary>
public enum RankTier { None, Purple, Pink, Orange, Gold }

public static class RankTiers
{
    public const int GoldMax = 10, OrangeMax = 100, PinkMax = 1000, PurpleMax = 10000;

    public static RankTier Of(int? rank) => rank switch
    {
        null or <= 0 => RankTier.None,
        <= GoldMax => RankTier.Gold,
        <= OrangeMax => RankTier.Orange,
        <= PinkMax => RankTier.Pink,
        <= PurpleMax => RankTier.Purple,
        _ => RankTier.None,
    };

    public static string Name(RankTier t) => t switch
    {
        RankTier.Gold => "gold", RankTier.Orange => "orange", RankTier.Pink => "pink", RankTier.Purple => "purple", _ => "none",
    };

    /// <summary>화면 문구용 등급 이름</summary>
    public static string Label(RankTier t) => t switch
    {
        RankTier.Gold => "골드", RankTier.Orange => "주황", RankTier.Pink => "핑크", RankTier.Purple => "보라", _ => "",
    };
}

/// <summary>넥슨 랭킹 페이지의 서버 번호 (s)와 게임의 서버 이름.</summary>
public static class RankingServers
{
    private static readonly Dictionary<string, int> Ids = new(StringComparer.Ordinal)
    {
        ["데이안"] = 1, ["아이라"] = 2, ["던컨"] = 3, ["알리사"] = 4, ["메이븐"] = 5, ["라사"] = 6, ["칼릭스"] = 7, ["몰리"] = 8,
    };

    /// <summary>서버 이름으로 번호를 찾는다. 모르는 이름·빈 이름이면 null.</summary>
    public static int? IdOf(string? realm) => !string.IsNullOrWhiteSpace(realm) && Ids.TryGetValue(realm.Trim(), out var id) ? id : null;

    public static string? NameOf(int id) => Ids.FirstOrDefault(kv => kv.Value == id).Key;
}
