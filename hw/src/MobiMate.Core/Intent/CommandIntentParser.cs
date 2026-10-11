using System.Text.RegularExpressions;

namespace MobiMate;

public enum IntentKind { None, Stop, CheckDailyMissions, InventoryDiet, CollectWorks, Gather }

/// <param name="Count">입력에 수량이 있으면 그 값, 없으면 null (호출자가 채집 도우미 목표치로 채운다)</param>
public sealed record CommandIntent(IntentKind Kind, string? ItemName = null, int? Count = null)
{
    public static readonly CommandIntent None = new(IntentKind.None);

    /// <summary>바로 실행해도 되는 의도인지. 정지만 즉시 실행하고, 채집·수거는 확인 카드를 거친다 (FR-AI-05).</summary>
    public bool ExecutesImmediately => Kind == IntentKind.Stop;
}

/// <summary>
/// AI 질문을 LLM에 넘기기 전에 가로채는 자연어 명령 판정 (FR-AI-05, D-07).
/// WPF판은 Contains("정지")로 판정해서 "정지 기능 알려줘"도 정지했다. 여기서는 정지를 명령형 단문으로만 받는다.
/// 판정 순서: 정지 → 숙제 → 다이어트 → 수거 → 채집.
/// </summary>
public static partial class CommandIntentParser
{
    private static readonly string[] GatherVerbs = { "채집", "캐줘", "캐라", "캐러", "모아줘", "수집", "낚시", "낚아" };

    [GeneratedRegex(@"^(긴급)?(정지|멈춰|스톱|그만)(해|해줘|해라)?$")]
    private static partial Regex StopRegex();

    [GeneratedRegex(@"[\s\p{P}\p{S}]")]
    private static partial Regex StripRegex();

    // 아이템명 바로 뒤에 올 수 있는 말: 조사·수량·동사. 다른 명사가 이어지면("사과 파이") 다른 물건으로 본다.
    private static readonly string[] AfterItem = { "을", "를", "좀", "도", "만", "이랑", "랑", "하고", "은", "는" };

    [GeneratedRegex(@"(\d+)\s*(개|마리)")]
    private static partial Regex UnitCountRegex();

    [GeneratedRegex(@"(?<![\w.])(\d+)(?![\w.])")]
    private static partial Regex BareCountRegex();

    public static CommandIntent Parse(string? input, IReadOnlyCollection<string>? gatherableNames = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return CommandIntent.None;
        var q = input.Trim();

        if (StopRegex().IsMatch(StripRegex().Replace(q, "")))
            return new CommandIntent(IntentKind.Stop);

        if (q.Contains("숙제") || q.Contains("일일 미션") || q.Contains("일일미션"))
            return new CommandIntent(IntentKind.CheckDailyMissions);

        if (q.Contains("다이어트") || q.Contains("가방 정리") || q.Contains("가방정리") || q.Contains("무게 줄여"))
            return new CommandIntent(IntentKind.InventoryDiet);

        if (q.Contains("수거") || q.Contains("작업대") || q.Contains("가공물"))
            return new CommandIntent(IntentKind.CollectWorks);

        if (gatherableNames is { Count: > 0 } && GatherVerbs.Any(q.Contains))
        {
            var item = gatherableNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderByDescending(n => n.Length)
                .FirstOrDefault(n => MentionsItem(q, n));
            if (item != null)
            {
                return new CommandIntent(IntentKind.Gather, item, ParseCount(q));
            }
        }

        return CommandIntent.None;
    }

    private static bool MentionsItem(string q, string name)
    {
        for (var i = q.IndexOf(name, StringComparison.OrdinalIgnoreCase); i >= 0;
             i = q.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = i + name.Length;
            var rest = q[end..].TrimStart();
            if (rest.Length == 0 || char.IsDigit(rest[0]) || GatherVerbs.Any(rest.StartsWith)) return true;

            // 조사 뒤에서 낱말이 끝나야 조사로 인정한다: "사과를 15", "사과 좀 캐줘"는 인정, "사과도끼"·"사과 도끼"는 제외
            var particle = AfterItem.FirstOrDefault(rest.StartsWith);
            if (particle != null && (rest.Length == particle.Length || !char.IsLetter(rest[particle.Length]))) return true;
        }
        return false;
    }

    private static int? ParseCount(string q)
    {
        var m = UnitCountRegex().Match(q);
        if (!m.Success) m = BareCountRegex().Match(q);
        return m.Success && int.TryParse(m.Groups[1].Value, out var c) && c > 0 ? c : null;
    }
}
