namespace MobiMate;

/// <summary>화면에 보이는 계정 공통 재화 한 칸. <see cref="Sub"/>는 이름 뒤쪽(패키지 포인트의 시즌 이름 등).</summary>
public sealed record SharedCurrencyView(string Name, string Label, string? Sub, long Amount);

/// <summary>
/// 계정 공통 재화 (승운 확인 2026-11-13, 게임 "보유한 재화" 화면의 [서버] 표시와 같다):
/// M캐시·데카·길드 토큰·패키지 포인트·캐시샵 포인트·환생석·웨카. 캐릭터마다 다른 재화(골드·은동전·정령의 날개 …)는 넣지 않는다.
/// 클래스 마스터 메달처럼 공유 창고에 모아 둔 탓에 같아 보이는 것도 넣지 않는다.
/// <see cref="Order"/>가 곧 화면에 보이는 우선순위다(좁으면 뒤쪽이 잘린다).
/// </summary>
public static class SharedCurrencies
{
    /// <summary>우선순위(앞이 먼저 보인다): 승운이 정한 순서(2026-11-13)</summary>
    public static readonly string[] Order = { "M캐시", "데카", "길드 토큰", "패키지 포인트", "캐시샵 포인트", "환생석", "웨카" };

    private const string Package = "패키지 포인트";

    /// <summary>이 이름이 계정 공통 재화의 몇 번째인지, 아니면 -1. "패키지 포인트: 그랜드 앙상블"처럼 시즌마다 뒤가 바뀌므로 앞부분으로 찾는다.</summary>
    public static int RankOf(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return -1;
        var n = displayName.Trim();
        if (n == "M캐쉬") n = "M캐시";   // 옛 표기
        if (n.StartsWith(Package, StringComparison.Ordinal)) return Array.IndexOf(Order, Package);
        return Array.IndexOf(Order, n);
    }

    /// <summary>재화 목록에서 계정 공통인 것만 이름→보유량으로 뽑는다</summary>
    public static Dictionary<string, long> Pick(IEnumerable<CurrencyItem>? currencies)
    {
        var d = new Dictionary<string, long>(StringComparer.Ordinal);
        if (currencies == null) return d;
        foreach (var c in currencies)
            if (RankOf(c.DisplayName) >= 0) d[c.DisplayName.Trim() == "M캐쉬" ? "M캐시" : c.DisplayName.Trim()] = c.Amount;
        return d;
    }

    /// <summary>
    /// 저장된 값에 새로 읽은 값을 얹는다. 새로 읽은 것에 패키지 포인트가 하나라도 있으면 옛 시즌 것은 지운다
    /// (시즌이 바뀌면 이름이 바뀌어 옛 것이 영원히 남기 때문).
    /// </summary>
    public static Dictionary<string, long> Merge(IReadOnlyDictionary<string, long> stored, IReadOnlyDictionary<string, long> read)
    {
        var d = new Dictionary<string, long>(stored, StringComparer.Ordinal);
        if (read.Keys.Any(k => k.StartsWith(Package, StringComparison.Ordinal)))
            foreach (var k in d.Keys.Where(k => k.StartsWith(Package, StringComparison.Ordinal)).ToList()) d.Remove(k);
        foreach (var (k, v) in read) d[k] = v;
        return d;
    }

    /// <summary>우선순위 순으로 늘어놓는다. 같은 순위(시즌이 여럿인 패키지 포인트)는 이름 순.</summary>
    public static IReadOnlyList<SharedCurrencyView> Ordered(IReadOnlyDictionary<string, long> items) =>
        items.Where(kv => RankOf(kv.Key) >= 0)
            .OrderBy(kv => RankOf(kv.Key)).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv =>
            {
                if (!kv.Key.StartsWith(Package, StringComparison.Ordinal)) return new SharedCurrencyView(kv.Key, kv.Key, null, kv.Value);
                var sub = kv.Key[Package.Length..].TrimStart(':', ' ').Trim();
                return new SharedCurrencyView(kv.Key, "패키지 포인트", sub.Length == 0 ? null : sub, kv.Value);
            })
            .ToList();
}
