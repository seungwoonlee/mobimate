namespace MobiMate;

/// <summary>
/// 캐릭터 지문 (캐릭터 식별 개선 1단계, 2026-11-20). CLI가 이름·고유번호를 주지 않아 "서버·직업·레벨·재화"로 추정하던 것을
/// 느리게 변하고 줄지 않는 값(생활 스킬, 캐릭터 창고, 계정 창고)으로 보강한다.
/// 1단계에서는 모으기만 한다: 판정(<see cref="CharacterIdentity"/>)은 바꾸지 않고, 지문 기준으로 어떻게 판정했을지를 로그로 비교한다.
/// </summary>
public sealed class IdentityFingerprint
{
    public string Key { get; set; } = "";
    public string Realm { get; set; } = "";
    public string Job { get; set; } = "";
    public int Level { get; set; }
    public long Combat { get; set; }
    public long Living { get; set; }
    public long? Deca { get; set; }
    public long? MCash { get; set; }
    public DateTime AtUtc { get; set; }

    /// <summary>생활 스킬 이름 → [레벨, 경험치]. 레벨·경험치는 줄지 않는다(레벨이 오르면 경험치가 처음부터).</summary>
    public Dictionary<string, long[]> Skills { get; set; } = new();

    /// <summary>캐릭터 창고에 있는 아이템 이름(중복 없이 정렬). 가방은 계속 변해서 넣지 않는다.</summary>
    public List<string> CharStorage { get; set; } = new();

    /// <summary>계정 창고 아이템 이름. 같은 계정의 캐릭터는 모두 같은 창고를 본다.</summary>
    public List<string> AccountStorage { get; set; } = new();

    public const int MaxStorageNames = 600;

    /// <summary>지금 읽은 값으로 지문을 만든다. 생활 스킬을 못 읽었으면(빈 목록) Skills가 비어 있다.</summary>
    public static IdentityFingerprint From(string key, CharacterInfo ch, IEnumerable<ItemData>? items, IEnumerable<CurrencyItem>? currencies, DateTime nowUtc)
    {
        var fp = new IdentityFingerprint
        {
            Key = key, Realm = ch.RealmName ?? "", Job = ch.JobName ?? "", Level = ch.Level,
            Combat = ch.CombatScore?.Value ?? 0, Living = ch.LivingScore?.Value ?? 0, AtUtc = nowUtc,
        };
        foreach (var s in ch.LivingSkills ?? [])
            if (!string.IsNullOrWhiteSpace(s.DisplayName)) fp.Skills[s.DisplayName!] = [s.Level, s.Exp];
        if (items != null)
        {
            static string Loc(string? l) => (l ?? "").ToLowerInvariant().Replace("_", "");
            fp.CharStorage = Names(items.Where(i => Loc(i.Location) == "characterstorage"));
            fp.AccountStorage = Names(items.Where(i => Loc(i.Location) == "accountstorage"));
        }
        if (currencies != null)
        {
            long? Pick(params string[] names) => currencies.FirstOrDefault(c => names.Contains(c.DisplayName))?.Amount;
            fp.Deca = Pick("데카");
            fp.MCash = Pick("M캐시", "M 캐시");
        }
        return fp;
    }

    private static List<string> Names(IEnumerable<ItemData> items) =>
        items.Select(i => i.DisplayName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Order(StringComparer.Ordinal).Take(MaxStorageNames).ToList();

    /// <summary>비교에 쓸 만한 정보가 있는가 (생활 스킬을 읽었는가)</summary>
    public bool HasSkills => Skills.Count > 0;

    /// <summary>두 지문의 내용이 같은가 (시각은 무시): 바뀐 것이 없으면 저장하지 않으려는 용도</summary>
    public bool SameContent(IdentityFingerprint o) =>
        Key == o.Key && Realm == o.Realm && Job == o.Job && Level == o.Level && Combat == o.Combat && Living == o.Living && Deca == o.Deca && MCash == o.MCash
        && Skills.Count == o.Skills.Count && Skills.All(kv => o.Skills.TryGetValue(kv.Key, out var v) && v[0] == kv.Value[0] && v[1] == kv.Value[1])
        && CharStorage.SequenceEqual(o.CharStorage) && AccountStorage.SequenceEqual(o.AccountStorage);
}

/// <summary>저장된 지문(Known)과 지금 읽은 지문(Now)이 같은 캐릭터일 가능성의 근거들</summary>
public sealed record FingerprintMatch(
    string Key, bool Compatible, int SkillsCompared, int SkillViolations, double SkillProgress, double CharStorageJaccard, double AccountStorageJaccard, string? Note);

public static class FingerprintComparer
{
    /// <summary>
    /// known → now 로 시간이 흘렀을 때 같은 캐릭터라면 반드시 지켜지는 것: 모든 생활 스킬의 (레벨, 경험치)가 줄지 않는다.
    /// 하나라도 줄었으면 다른 캐릭터다(Compatible=false). SkillProgress = 비교한 스킬 중 값이 늘어난 것의 비율(0이면 완전히 같음).
    /// 창고는 겹침 정도(Jaccard)만 본다: 가방과 달리 천천히 변하므로 같은 캐릭터면 높다.
    /// </summary>
    public static FingerprintMatch Compare(IdentityFingerprint known, IdentityFingerprint now)
    {
        int compared = 0, violations = 0, grew = 0;
        foreach (var (name, k) in known.Skills)
        {
            if (!now.Skills.TryGetValue(name, out var n)) continue;
            compared++;
            if (n[0] < k[0] || (n[0] == k[0] && n[1] < k[1])) violations++;
            else if (n[0] != k[0] || n[1] != k[1]) grew++;
        }
        string? note = null;
        if (compared == 0) note = !known.HasSkills || !now.HasSkills ? "생활 스킬을 못 읽음" : "겹치는 스킬 없음";
        var progress = compared == 0 ? 0 : (double)grew / compared;
        return new FingerprintMatch(known.Key, violations == 0, compared, violations, progress, Jaccard(known.CharStorage, now.CharStorage), Jaccard(known.AccountStorage, now.AccountStorage), note);
    }

    /// <summary>두 집합의 겹침(0~1). 둘 다 비었으면 판단할 수 없어 -1.</summary>
    public static double Jaccard(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return -1;
        var set = new HashSet<string>(a, StringComparer.Ordinal);
        var inter = b.Count(set.Contains);
        var union = set.Count + b.Count - inter;
        return union == 0 ? -1 : (double)inter / union;
    }
}
