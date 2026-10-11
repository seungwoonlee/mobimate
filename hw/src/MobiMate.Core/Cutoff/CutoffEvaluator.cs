using System.Reflection;
using System.Text.Json;

namespace MobiMate;

/// <summary>난이도 하나의 기준 (REQUIREMENTS §5.12 FR-CO-01). 마도저항 0 = 조건 없음.</summary>
public sealed record CutoffTier(string Name, long MinCombat, long RecCombat, long OverCombat, long MinMdef, long OverMdef)
{
    public bool CanEnter(long combat, long mdef) => combat >= MinCombat && mdef >= MinMdef;

    /// <summary>마도저항 무관용: 압도 마도저항을 1이라도 못 채우면 거짓.</summary>
    public bool MdefSafe(long mdef) => OverMdef == 0 || mdef >= OverMdef;

    /// <summary>압도 전투력의 90% (정수 나눗셈, WPF판과 같음).</summary>
    public long NinetyPct => OverCombat * 9 / 10;
}

public sealed record CutoffContent(string Id, string Name, string Icon, IReadOnlyList<CutoffTier> Tiers);

public enum CutoffStatus { Locked, Marginal, Near, Overwhelm }   // 입장 불가 / 턱걸이 / 압도 근접 / 압도

public sealed record CutoffShort(long Combat, long Mdef);

/// <summary>상위 난이도 가이드 (FR-CO-05). Top이면 추천이 이미 최고 난이도.</summary>
public sealed record CutoffNext(string? Tier, long CombatShort, long MdefShort, bool Top)
{
    public bool ReadyNow => !Top && CombatShort == 0 && MdefShort == 0;
}

public sealed record CutoffResult(
    string Id, string Name, string Icon,
    string? MaxEntryTier, string? RecommendedTier, CutoffStatus Status,
    int OverwhelmPct, long CombatToOverwhelm, long MdefShort,
    CutoffNext? Next, CutoffShort? EntryShort, string? EntryTier);

/// <summary>
/// 어비스·레이드 컷오프 추천 (FR-CO-02~05). WPF판 v1.2.0 DungeonCutoffService.EvaluateContent와 같은 규칙이다.
/// 차이: 달성률은 64비트 정수 나눗셈(WPF는 실수라 드물게 1%p 낮음). 문구·색은 만들지 않는다(클라이언트 몫).
/// </summary>
public static class CutoffEvaluator
{
    public static CutoffResult Evaluate(CutoffContent c, long combat, long mdef)
    {
        var enterable = c.Tiers.Where(t => t.CanEnter(combat, mdef)).ToList();
        if (enterable.Count == 0)
        {
            var first = c.Tiers[0];
            return new CutoffResult(c.Id, c.Name, c.Icon, null, null, CutoffStatus.Locked, 0, 0, 0, null,
                new CutoffShort(Math.Max(0, first.MinCombat - combat), Math.Max(0, first.MinMdef - mdef)), first.Name);
        }
        var maxEntry = enterable[^1];

        // FR-CO-03: ① 마도저항 압도 + 전투력(권장 이상, 압도 90% 이상) → ② 마도저항 압도만 → ③ 입장 가능한 최고
        var target = enterable.LastOrDefault(t => t.MdefSafe(mdef) && combat >= t.RecCombat && combat >= t.NinetyPct)
                     ?? enterable.LastOrDefault(t => t.MdefSafe(mdef))
                     ?? maxEntry;

        // FR-CO-04
        var overwhelmed = combat >= target.OverCombat && target.MdefSafe(mdef);
        var status = overwhelmed ? CutoffStatus.Overwhelm
            : combat >= target.NinetyPct && target.MdefSafe(mdef) ? CutoffStatus.Near
            : CutoffStatus.Marginal;
        var pct = overwhelmed ? 100 : (int)Math.Min(99, combat * 100 / target.OverCombat);
        var combatLack = Math.Max(0, target.OverCombat - combat);
        var mdefLack = target.OverMdef > 0 ? Math.Max(0, target.OverMdef - mdef) : 0;

        // FR-CO-05
        CutoffNext next;
        var idx = IndexOf(c.Tiers, target);
        if (idx >= 0 && idx < c.Tiers.Count - 1)
        {
            var n = c.Tiers[idx + 1];
            var need = Math.Max(n.RecCombat, n.NinetyPct);
            next = new CutoffNext(n.Name, Math.Max(0, need - combat), n.OverMdef > 0 ? Math.Max(0, n.OverMdef - mdef) : 0, false);
        }
        else next = new CutoffNext(null, 0, 0, true);

        return new CutoffResult(c.Id, c.Name, c.Icon, maxEntry.Name, target.Name, status, pct, combatLack, mdefLack, next, null, null);
    }

    public static IReadOnlyList<CutoffResult> EvaluateAll(CutoffCatalog catalog, long combat, long mdef) =>
        catalog.Contents.Select(c => Evaluate(c, combat, mdef)).ToList();

    private static int IndexOf(IReadOnlyList<CutoffTier> tiers, CutoffTier t)
    {
        for (var i = 0; i < tiers.Count; i++) if (ReferenceEquals(tiers[i], t)) return i;
        return -1;
    }
}

/// <summary>
/// 컷오프 기준표 (FR-CO-01): 내장 cutoff_catalog.json 위에 사용자 폴더의 cutoff_catalog.override.json을 콘텐츠 ID 단위로 덮어쓴다.
/// 검증에 실패한 콘텐츠는 빼고 Warnings에 이유를 남긴다.
/// </summary>
public sealed class CutoffCatalog
{
    public const string OverrideFileName = "cutoff_catalog.override.json";
    private const string ResourceName = "MobiMate.Cutoff.cutoff_catalog.json";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public IReadOnlyList<CutoffContent> Contents { get; }
    public IReadOnlyList<string> Warnings { get; }

    private CutoffCatalog(IReadOnlyList<CutoffContent> contents, IReadOnlyList<string> warnings)
    {
        Contents = contents;
        Warnings = warnings;
    }

    public static CutoffCatalog Load(string? storageDir = null)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                      ?? throw new InvalidOperationException($"내장 리소스가 없습니다: {ResourceName}");
        var warnings = new List<string>();
        var list = Parse(JsonSerializer.Deserialize<FileDto>(s, Json), warnings, "내장");

        if (storageDir != null)
        {
            var path = Path.Combine(storageDir, OverrideFileName);
            if (File.Exists(path))
            {
                try
                {
                    var over = Parse(JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), Json), warnings, "덮어쓰기");
                    foreach (var o in over)
                    {
                        var i = list.FindIndex(c => c.Id == o.Id);
                        if (i >= 0) list[i] = o; else list.Add(o);
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"{OverrideFileName}을 읽지 못해 내장 기준표를 씁니다: {ex.Message}");
                }
            }
        }
        return new CutoffCatalog(list, warnings);
    }

    /// <summary>검증 규칙 (FR-CO-01). 문제가 있으면 이유, 없으면 null.</summary>
    public static string? Validate(CutoffContent c)
    {
        if (string.IsNullOrWhiteSpace(c.Id) || c.Tiers.Count == 0) return "ID 또는 난이도가 비어 있음";
        for (var i = 0; i < c.Tiers.Count; i++)
        {
            var t = c.Tiers[i];
            if (string.IsNullOrWhiteSpace(t.Name)) return $"{i + 1}번째 난이도: 이름이 비어 있음";
            if (t.MinCombat < 0 || t.MinMdef < 0 || t.OverMdef < 0) return $"{t.Name}: 음수 값";
            if (t.OverCombat <= 0) return $"{t.Name}: 압도 전투력은 0보다 커야 함";
            if (!(t.MinCombat <= t.RecCombat && t.RecCombat <= t.OverCombat)) return $"{t.Name}: 입장 ≤ 권장 ≤ 압도 전투력이 아님";
            if (t.OverMdef > 0 && t.MinMdef > t.OverMdef) return $"{t.Name}: 입장 마도저항이 압도 마도저항보다 큼";
            if (i > 0 && t.MinCombat < c.Tiers[i - 1].MinCombat) return $"{t.Name}: 난이도 순서(입장 전투력 오름차순)가 아님";
        }
        return null;
    }

    private static List<CutoffContent> Parse(FileDto? dto, List<string> warnings, string source)
    {
        var result = new List<CutoffContent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in dto?.Contents ?? new())
        {
            if (!seen.Add(c.Id ?? ""))
            {
                warnings.Add($"[{source}] {c.Id} 제외: 같은 ID가 두 번 있음");
                continue;
            }
            var content = new CutoffContent(c.Id ?? "", c.Name ?? c.Id ?? "", c.Icon ?? "",
                (c.Tiers ?? new()).Select(t => new CutoffTier(t.Name ?? "", t.MinCombat, t.RecCombat, t.OverCombat, t.MinMdef, t.OverMdef)).ToList());
            if (Validate(content) is { } why) warnings.Add($"[{source}] {content.Id} 제외: {why}");
            else result.Add(content);
        }
        return result;
    }

    private sealed class FileDto
    {
        public List<ContentDto>? Contents { get; set; }
    }

    private sealed class ContentDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Icon { get; set; }
        public List<TierDto>? Tiers { get; set; }
    }

    private sealed class TierDto
    {
        public string? Name { get; set; }
        public long MinCombat { get; set; }
        public long RecCombat { get; set; }
        public long OverCombat { get; set; }
        public long MinMdef { get; set; }
        public long OverMdef { get; set; }
    }
}
