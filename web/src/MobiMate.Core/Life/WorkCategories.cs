using System.Reflection;
using System.Text.Json;

namespace MobiMate;

/// <summary>가공 종류 (FR-DT-20). 값은 화면과 주고받는 id다.</summary>
public static class WorkKinds
{
    public const string Metal = "metal", Wood = "wood", Leather = "leather", Cloth = "cloth", Medicine = "medicine", Food = "food", Other = "other";

    /// <summary>화면에 보여 주는 순서</summary>
    public static readonly string[] Order = { Metal, Wood, Leather, Cloth, Medicine, Food, Other };
}

/// <summary>work_categories.json의 형식 (내장 파일과 사용자 폴더의 덮어쓰기 파일이 같다)</summary>
public sealed class WorkCategoryData
{
    public Dictionary<string, string> Labels { get; set; } = new();
    public Dictionary<string, List<string>> Items { get; set; } = new();
    public Dictionary<string, string> Facilities { get; set; } = new();
    public Dictionary<string, List<string>> Keywords { get; set; } = new();
}

/// <summary>
/// 가공 대기열을 종류별로 묶는 분류표 (FR-DT-20). 판정 순서: ① 가공품 이름(공백·대소문자 무시) → ② 작업대 이름 → ③ 이름 키워드 → ④ 기타.
/// 코드에 박지 않고 데이터 파일로 둔다: 내장 표 위에 사용자 폴더의 work_categories.override.json이 있으면 더하고(이름 목록은 합치고, 작업대는 덮어쓴다)
/// 틀린 분류는 파일만 고쳐 반영한다.
/// </summary>
public sealed class WorkCategoryCatalog
{
    public const string OverrideFileName = "work_categories.override.json";
    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly Dictionary<string, string> _byName = new();
    private readonly Dictionary<string, string> _byFacility = new();
    private readonly Dictionary<string, List<string>> _keywords = new();
    private readonly Dictionary<string, string> _labels = new();

    public IReadOnlyDictionary<string, string> Labels => _labels;
    public IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();

    public static WorkCategoryCatalog LoadEmbedded() => Build(ReadEmbedded(), null);

    /// <summary>내장 표 + 사용자 덮어쓰기. 덮어쓰기 파일이 깨졌으면 무시하고 경고만 남긴다.</summary>
    public static WorkCategoryCatalog Load(string? storageDir)
    {
        WorkCategoryData? extra = null;
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(storageDir))
        {
            var path = Path.Combine(storageDir, OverrideFileName);
            try
            {
                if (File.Exists(path)) extra = JsonSerializer.Deserialize<WorkCategoryData>(File.ReadAllText(path), Read);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{OverrideFileName}을 읽지 못해 무시합니다: {ex.Message}");
            }
        }
        var c = Build(ReadEmbedded(), extra);
        c.Warnings = warnings;
        return c;
    }

    private static WorkCategoryData ReadEmbedded()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MobiMate.Life.work_categories.json")
            ?? throw new InvalidOperationException("내장 가공 분류표를 찾을 수 없습니다.");
        return JsonSerializer.Deserialize<WorkCategoryData>(s, Read) ?? new WorkCategoryData();
    }

    private static WorkCategoryCatalog Build(WorkCategoryData baseData, WorkCategoryData? extra)
    {
        var c = new WorkCategoryCatalog();
        foreach (var d in extra == null ? new[] { baseData } : new[] { baseData, extra })
        {
            foreach (var (k, v) in d.Labels) c._labels[k] = v;
            // 나중에 읽는 덮어쓰기가 같은 이름의 앞선 분류를 바꾼다
            foreach (var (kind, names) in d.Items) foreach (var n in names) c._byName[Norm(n)] = kind;
            foreach (var (fac, kind) in d.Facilities) c._byFacility[Norm(fac)] = kind;
            foreach (var (kind, words) in d.Keywords)
            {
                if (!c._keywords.TryGetValue(kind, out var list)) c._keywords[kind] = list = new();
                list.AddRange(words.Where(w => !string.IsNullOrWhiteSpace(w)));
            }
        }
        if (!c._labels.ContainsKey(WorkKinds.Other)) c._labels[WorkKinds.Other] = "기타 가공";
        return c;
    }

    /// <summary>가공품 이름과 작업대 이름으로 종류 id를 정한다. 모르면 "other".</summary>
    public string Classify(string? name, string? facility)
    {
        var n = Norm(name);
        if (n.Length > 0 && _byName.TryGetValue(n, out var byName)) return byName;
        var f = Norm(facility);
        if (f.Length > 0 && _byFacility.TryGetValue(f, out var byFacility)) return byFacility;
        if (n.Length > 0)
        {
            foreach (var kind in WorkKinds.Order)
                if (_keywords.TryGetValue(kind, out var words) && words.Any(w => n.Contains(Norm(w), StringComparison.Ordinal))) return kind;
        }
        return WorkKinds.Other;
    }

    public string LabelOf(string kind) => _labels.TryGetValue(kind, out var l) ? l : kind;

    private static string Norm(string? s) => string.IsNullOrWhiteSpace(s) ? "" : new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToLowerInvariant();
}
