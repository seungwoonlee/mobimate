using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MobiMate;

public enum IntentKind
{
    None,
    Stop,
    CheckDailyMissions,
    InventoryDiet,
    CollectWorks,
    Gather,
    CreatePersona
}

public sealed record CommandIntent(IntentKind Kind, string? ItemName = null, int? Count = null, string? PersonaConcept = null)
{
    public static readonly CommandIntent None = new(IntentKind.None);
    public bool ExecutesImmediately => Kind == IntentKind.Stop;
}

/// <summary>
/// AI 질문을 LLM에 넘기기 전에 가로채는 자연어 명령 판정기.
/// (W-04 긴급 정지 오동작 방지 및 AI 페르소나 생성 의도 감지)
/// </summary>
public static partial class CommandIntentParser
{
    private static readonly string[] GatherVerbs = { "채집", "캐줘", "캐라", "캐러", "모아줘", "수집", "낚시", "낚아" };
    private static readonly string[] PersonaCreateVerbs = { "추가", "생성", "만들어", "만들", "등록", "설정", "짜줘", "작성" };

    [GeneratedRegex(@"^(긴급)?(정지|멈춰|스톱|그만)(해|해줘|해라)?$")]
    private static partial Regex StopRegex();

    [GeneratedRegex(@"[\s\p{P}\p{S}]")]
    private static partial Regex StripRegex();

    [GeneratedRegex(@"(\d+)\s*(개|마리)")]
    private static partial Regex UnitCountRegex();

    [GeneratedRegex(@"(?<![\w.])(\d+)(?![\w.])")]
    private static partial Regex BareCountRegex();

    private static readonly string[] AfterItem = { "을", "를", "좀", "도", "만", "이랑", "랑", "하고", "은", "는" };

    public static CommandIntent Parse(string? input, IReadOnlyCollection<string>? gatherableNames = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return CommandIntent.None;
        var q = input.Trim();

        // 1. 긴급 정지: 짧은 단독 명령일 때만 인정 (W-04)
        var flat = StripRegex().Replace(q, "");
        if (StopRegex().IsMatch(flat))
            return new CommandIntent(IntentKind.Stop);

        // 2. 페르소나 생성/추가 의도
        if (IsPersonaCreationIntent(q))
        {
            var concept = ExtractPersonaConcept(q);
            return new CommandIntent(IntentKind.CreatePersona, PersonaConcept: concept);
        }

        // 3. 일일 숙제 점검
        if (q.Contains("숙제") || q.Contains("일일 미션") || q.Contains("일일미션"))
            return new CommandIntent(IntentKind.CheckDailyMissions);

        // 4. 가방 다이어트
        if (q.Contains("다이어트") || q.Contains("가방 정리") || q.Contains("가방정리") || q.Contains("무게 줄여"))
            return new CommandIntent(IntentKind.InventoryDiet);

        // 5. 가공 작업대 수거
        if (q.Contains("수거") || q.Contains("작업대") || q.Contains("가공물"))
            return new CommandIntent(IntentKind.CollectWorks);

        // 6. 채집/낚시
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

    public static bool IsPersonaCreationIntent(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return false;

        // "페르소나" 키워드 + 생성/추가 동사
        if (q.Contains("페르소나", StringComparison.OrdinalIgnoreCase))
        {
            if (PersonaCreateVerbs.Any(v => q.Contains(v, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // "(말투|스타일)" + 생성/추가 동사
        if ((q.Contains("말투", StringComparison.OrdinalIgnoreCase) || q.Contains("스타일", StringComparison.OrdinalIgnoreCase)) &&
            (q.Contains("추가", StringComparison.OrdinalIgnoreCase) || q.Contains("만들어", StringComparison.OrdinalIgnoreCase) || q.Contains("생성", StringComparison.OrdinalIgnoreCase)))
        {
            // 채집 관련 단어와 겹치지 않는 경우
            if (!GatherVerbs.Any(q.Contains))
                return true;
        }

        return false;
    }

    public static string ExtractPersonaConcept(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return "새 페르소나";

        // "공주기사 스타일 페르소나 추가해줘" -> "공주기사 스타일"
        // "츤데레 메이드 페르소나 만들어줘" -> "츤데레 메이드"
        var cleaned = Regex.Replace(q, @"(페르소나|추가해줘|추가해|추가|만들어줘|만들어|생성해줘|생성|등록해줘|등록|짜줘|설정해줘|해줘|해주세요|부탁해|스타일로)", " ").Trim();
        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            var joined = string.Join(" ", parts);
            if (!joined.EndsWith("스타일") && !joined.EndsWith("말투"))
                return joined + " 스타일";
            return joined;
        }

        return "맞춤 페르소나";
    }

    private static bool MentionsItem(string q, string name)
    {
        for (var i = q.IndexOf(name, StringComparison.OrdinalIgnoreCase); i >= 0;
             i = q.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = i + name.Length;
            var rest = q[end..].TrimStart();
            if (rest.Length == 0 || char.IsDigit(rest[0]) || GatherVerbs.Any(rest.StartsWith)) return true;

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
