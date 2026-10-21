using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MobiMate;

/// <summary>게임 CLI `capabilities`의 명령 하나(설명·본문 예·출력 예·메모).</summary>
public sealed record CliCommandInfo(string Command, string? Description, string? BodyExample, string? OutputExample, string? Note);

/// <summary>한 명령에서 바뀐 것: 출력 예에 새로 생기거나 사라진 필드 이름, 그리고 설명·본문·메모 같은 글이 바뀌었는지.</summary>
public sealed record CliChangedCommand(string Command, IReadOnlyList<string> AddedFields, IReadOnlyList<string> RemovedFields, bool TextChanged);

/// <summary>두 시점의 CLI 명령 목록을 비교한 결과.</summary>
public sealed record CliChange(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<CliChangedCommand> Changed)
{
    public bool Any => Added.Count + Removed.Count + Changed.Count > 0;

    /// <summary>사람이 읽는 요약(로그·파일용).</summary>
    public string Summary()
    {
        if (!Any) return "바뀐 것 없음";
        var sb = new StringBuilder();
        if (Added.Count > 0) sb.AppendLine($"새 명령 {Added.Count}개: {string.Join(", ", Added)}");
        if (Removed.Count > 0) sb.AppendLine($"사라진 명령 {Removed.Count}개: {string.Join(", ", Removed)}");
        foreach (var c in Changed)
        {
            var parts = new List<string>();
            if (c.AddedFields.Count > 0) parts.Add($"새 필드 {string.Join(", ", c.AddedFields)}");
            if (c.RemovedFields.Count > 0) parts.Add($"사라진 필드 {string.Join(", ", c.RemovedFields)}");
            if (parts.Count == 0 && c.TextChanged) parts.Add("설명·메모가 바뀜");
            sb.AppendLine($"{c.Command}: {string.Join(" / ", parts)}");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// 게임 CLI 업데이트 감지(순수, 2026-10-22 승운 요청: "CLI가 업데이트됐을지 모르니 간간히 확인"). 앱이 `capabilities`를 저장해 두고
/// 다시 읽을 때마다 비교해 새 명령·사라진 명령·출력 필드 변화를 알려 준다. 새 필드가 생기면 앱 모델과 화면에 쓸 수 있는지 살펴본다.
/// </summary>
public static class CliCapabilityDiff
{
    private static readonly Regex FieldToken = new(@"\b[A-Z][A-Za-z0-9]+\b", RegexOptions.Compiled);

    /// <summary>`capabilities` 응답({ "commands": [...] })을 명령 이름 → 정보로. 읽을 수 없으면 null.</summary>
    public static Dictionary<string, CliCommandInfo>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("commands", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
            var map = new Dictionary<string, CliCommandInfo>(StringComparer.Ordinal);
            string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object || Str(e, "Command") is not { Length: > 0 } name) continue;
                map[name] = new CliCommandInfo(name, Str(e, "Description"), Str(e, "BodyExample"), Str(e, "OutputExample"), Str(e, "Note"));
            }
            return map.Count == 0 ? null : map;
        }
        catch (JsonException) { return null; }
    }

    public static CliChange Compare(IReadOnlyDictionary<string, CliCommandInfo> baseline, IReadOnlyDictionary<string, CliCommandInfo> current)
    {
        var added = current.Keys.Except(baseline.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var removed = baseline.Keys.Except(current.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var changed = new List<CliChangedCommand>();
        foreach (var name in current.Keys.Intersect(baseline.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            var a = baseline[name]; var b = current[name];
            var fa = Tokens(a.OutputExample); var fb = Tokens(b.OutputExample);
            var addedFields = fb.Except(fa).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedFields = fa.Except(fb).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var text = a.Description != b.Description || a.BodyExample != b.BodyExample || a.OutputExample != b.OutputExample || a.Note != b.Note;
            if (addedFields.Count > 0 || removedFields.Count > 0 || text)
                changed.Add(new CliChangedCommand(name, addedFields, removedFields, text));
        }
        return new CliChange(added, removed, changed);
    }

    private static HashSet<string> Tokens(string? output) => output == null ? new() : FieldToken.Matches(output).Select(m => m.Value).ToHashSet();
}
