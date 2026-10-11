namespace MobiMate;

/// <summary>한 캐릭터 기록의 마지막 상태 (같은 서버·직업 후보 비교용)</summary>
public sealed record IdentityCandidate(string Key, int Level, long Living, long Combat, long Deca, long MCash, DateTime LastSeen);

/// <summary>지금 읽은 캐릭터 정보. 데카·M캐시는 재화를 아직 못 읽었으면 null.</summary>
public sealed record IdentityReading(int Level, long Living, long Combat, long? Deca, long? MCash);

/// <summary>캐릭터가 가진 재화 (골드는 캐릭터별, 데카·M캐시는 계정 공유)</summary>
public sealed record Wallet(long Deca, long MCash, long Gold);

/// <summary>
/// 같은 서버·같은 직업의 캐릭터를 구분한다. 게임이 캐릭터 이름·고유번호를 주지 않아 "서버_직업"만으로는
/// 다른 계정의 같은 직업 캐릭터가 한 기록으로 합쳐지기 때문이다 (승운 확인 2026-10-02).
///
/// 판정 순서:
/// 1. 레벨·생활력은 줄어들 수 없다: 지금 값이 기록보다 낮으면 그 기록의 캐릭터가 아니다.
/// 2. 데카·M캐시가 기록과 다르면 다른 계정(= 다른 캐릭터)으로 먼저 본다. 단, 같은 캐릭터가 데카를 썼을 수도 있으므로
///    레벨이 거의 그대로(+2 이내)이고 전투력도 비슷하면(85~120%) 같은 캐릭터로 본다.
/// 3. 남은 후보 중 데카·M캐시가 같은 것 → 레벨이 가까운 것 → 전투력이 가까운 것 → 최근에 본 것 순으로 고른다.
/// 4. 후보가 없으면 "서버_직업#2"처럼 새 캐릭터로 만든다. 잘못 합쳐지거나 나뉜 것은 카드에서 직접 분리·삭제한다.
/// </summary>
public static class CharacterIdentity
{
    public const int SpendDriftLevels = 2;
    public const double SpendDriftMinRatio = 0.85, SpendDriftMaxRatio = 1.20;

    public static string BaseKey(string? realm, string? job) =>
        $"{(string.IsNullOrWhiteSpace(realm) ? "에린" : realm)}_{(string.IsNullOrWhiteSpace(job) ? "밀레시안" : job)}";

    public static bool IsVariantOf(string key, string baseKey) => key == baseKey || key.StartsWith(baseKey + "#", StringComparison.Ordinal);

    /// <summary>비어 있는 다음 이름: 기본 이름이 비었으면 기본 이름, 아니면 #2, #3 …</summary>
    public static string NextKey(string baseKey, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.Ordinal);
        if (!set.Contains(baseKey)) return baseKey;
        for (var n = 2; ; n++) if (!set.Contains($"{baseKey}#{n}")) return $"{baseKey}#{n}";
    }

    public static string Resolve(string baseKey, IReadOnlyList<IdentityCandidate> candidates, IdentityReading r)
    {
        var mine = candidates.Where(c => IsVariantOf(c.Key, baseKey)).ToList();
        if (mine.Count == 0) return baseKey;

        bool readingPair = r.Deca is { } d && r.MCash is { } m && AccountGrouper.IsDistinctive((d, m));
        var scored = new List<(IdentityCandidate C, bool SamePair)>();
        foreach (var c in mine)
        {
            if (r.Level < c.Level) continue;                                  // 레벨은 내려가지 않는다
            if (r.Living > 0 && c.Living > 0 && r.Living < c.Living) continue;   // 생활력도 마찬가지
            var candPair = AccountGrouper.IsDistinctive((c.Deca, c.MCash));
            var same = readingPair && candPair && r.Deca == c.Deca && r.MCash == c.MCash;
            if (readingPair && candPair && !same)                            // 데카·M캐시가 다르면 다른 계정으로 먼저 본다
            {
                var drift = r.Level - c.Level <= SpendDriftLevels && c.Combat > 0
                    && r.Combat >= c.Combat * SpendDriftMinRatio && r.Combat <= c.Combat * SpendDriftMaxRatio;
                if (!drift) continue;                                         // 데카를 쓴 같은 캐릭터가 아니라면 다른 캐릭터
            }
            scored.Add((c, same));
        }
        if (scored.Count == 0) return NextKey(baseKey, mine.Select(c => c.Key));
        return scored
            .OrderByDescending(s => s.SamePair)
            .ThenBy(s => r.Level - s.C.Level)
            .ThenBy(s => Math.Abs(r.Combat - s.C.Combat))
            .ThenByDescending(s => s.C.LastSeen)
            .First().C.Key;
    }

    /// <summary>
    /// 주직업 ↔ 부직업 전환인가. 무기를 바꿔 들면 직업이 바뀌지만 같은 캐릭터이므로 골드·데카·M캐시는 그대로다.
    /// 알던 직업과 다른 직업이 읽혔고(서버는 같음) 세 재화가 모두 같으면 다른 캐릭터로 접속한 것이 아니라 직업만 잠시 바뀐 것이다.
    /// 데카·M캐시는 같은 계정의 캐릭터끼리도 같으므로 캐릭터별로 다른 골드가 결정적이다 (승운 확인 2026-10-02).
    /// 골드는 전환 사이에 조금 변할 수 있어 0.1%까지 허용한다.
    /// </summary>
    public static bool IsJobSwap(string? knownRealm, string? knownJob, string? realm, string? job, Wallet? known, Wallet? now)
    {
        if (known == null || now == null || string.IsNullOrWhiteSpace(job) || string.IsNullOrWhiteSpace(knownJob) || job == knownJob) return false;
        if (!string.IsNullOrWhiteSpace(realm) && !string.IsNullOrWhiteSpace(knownRealm) && realm != knownRealm) return false;
        return SameWallet(known, now);
    }

    public static bool SameWallet(Wallet a, Wallet b) =>
        a.Gold > 0 && b.Gold > 0 && a.Deca == b.Deca && a.MCash == b.MCash
        && Math.Abs(a.Gold - b.Gold) <= Math.Max(a.Gold, b.Gold) * GoldDriftRatio;

    public const double GoldDriftRatio = 0.001;

    /// <summary>
    /// 칭호는 불러오는 데 시간이 걸려 잠깐 비어 올 수 있다: 비어 있으면 알던 칭호를 그대로 쓴다. 칭호가 바뀐 것이 확인될 때(비어 있지 않은 값)만 바꾼다.
    /// </summary>
    public static CharacterInfo KeepTitle(CharacterInfo ch, string? known) =>
        string.IsNullOrWhiteSpace(ch.Title) && !string.IsNullOrWhiteSpace(known) ? ch with { Title = known } : ch;

    /// <summary>
    /// 앞서 읽은 캐릭터와 같은 캐릭터로 이어 봐도 되는 읽기인가 (서버·직업·레벨이 같고 전투력이 5%, 생활력이 1% 안쪽).
    /// 아니면 캐릭터가 바뀌었을 수 있으니 재화를 새로 읽어 다시 판정한다. 잘못 의심해도 판정 결과가 같으면 달라지는 것은 없다.
    /// </summary>
    public static bool SameReading(CharacterInfo? a, CharacterInfo? b)
    {
        if (a == null || b == null || a.RealmName != b.RealmName || a.JobName != b.JobName || a.Level != b.Level) return false;
        static bool Close(long x, long y, double pct) => Math.Abs(x - y) <= Math.Max(x, y) * pct;
        return Close(a.CombatScore?.Value ?? 0, b.CombatScore?.Value ?? 0, 0.05) && Close(a.LivingScore?.Value ?? 0, b.LivingScore?.Value ?? 0, 0.01);
    }
}
