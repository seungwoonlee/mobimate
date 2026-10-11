using System.Text.Json;
using System.Text.RegularExpressions;

namespace MobiMate;

/// <param name="FromAi">AI가 만든 초안이면 true, 규칙 기반 대체 초안이면 false</param>
public sealed record PersonaDraft(string Name, string Emoji, string Prompt, bool FromAi)
{
    public CustomPersona ToCustomPersona() => new() { Name = Name, TagEmoji = Emoji, SystemPrompt = Prompt };
}

/// <summary>
/// AI 페르소나 생성 (FR-CH-07). 자연어 요청 → AI가 이름·이모지·말투 지침을 JSON으로 만든다.
/// 내장 가이드 엔진이거나 AI 실패·형식 오류면 규칙 기반 초안을 만든다(WPF판 v1.1.0과 같은 규칙).
/// 결과는 초안이며 저장은 호출자가 사용자 확인 후 한다.
/// </summary>
public static partial class PersonaGenerator
{
    public static readonly TimeSpan AiTimeout = TimeSpan.FromSeconds(20);
    private const int MaxNameLength = 20;
    private const int MaxPromptLength = 300;

    public static async Task<PersonaDraft> GenerateAsync(string request, IAiEngine? engine, CancellationToken ct = default)
    {
        var concept = (request ?? "").Trim();
        if (engine != null && engine.Info.Type != AiEngineType.BuiltInGuide && concept.Length > 0)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(AiTimeout);
                var res = await engine.GenerateResponseAsync(BuildPrompt(concept), "너는 마비노기 모바일 페르소나 생성기다. JSON으로만 답하라.", cts.Token);
                if (res.Success && TryParse(res.Reply) is { } draft) return draft;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 시간 초과 → 규칙 기반
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // AI 오류 → 규칙 기반
            }
        }
        return Fallback(concept);
    }

    internal static string BuildPrompt(string concept) =>
        "당신은 마비노기 모바일 인게임 페르소나 디자이너입니다.\n" +
        $"사용자가 다음 컨셉의 페르소나 생성을 요청했습니다: \"{concept}\"\n\n" +
        "반드시 아래 JSON 형식으로만 응답하세요:\n" +
        "{\n" +
        "  \"name\": \"간결한 페르소나 이름 (예: 공주기사 스타일)\",\n" +
        "  \"emoji\": \"어울리는 이모지 1개 (예: 🛡️)\",\n" +
        "  \"prompt\": \"인게임 채팅에서 쓸 말투·성격·어미 특징 지침 (2~3문장)\"\n" +
        "}";

    [GeneratedRegex(@"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline)]
    private static partial Regex FencedJson();

    [GeneratedRegex(@"\{.*\}", RegexOptions.Singleline)]
    private static partial Regex BareJson();

    public static PersonaDraft? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = FencedJson().Match(raw);
        var json = m.Success ? m.Groups[1].Value : BareJson().Match(raw) is { Success: true } b ? b.Value : null;
        if (json == null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var name = Str(root, "name");
            var prompt = Str(root, "prompt");
            var emoji = Str(root, "emoji");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prompt)) return null;
            return new PersonaDraft(
                ChatText.Truncate(name, MaxNameLength),
                string.IsNullOrWhiteSpace(emoji) ? "🎭" : ChatText.Truncate(emoji, 4),
                ChatText.Truncate(prompt, MaxPromptLength),
                FromAi: true);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Str(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
    }

    public static PersonaDraft Fallback(string concept)
    {
        var name = string.IsNullOrWhiteSpace(concept) ? "맞춤 스타일" : ChatText.Truncate(concept.Trim(), MaxNameLength - 4);
        if (!name.EndsWith("스타일") && !name.EndsWith("말투")) name += " 스타일";
        return new PersonaDraft(name, PickEmoji(concept), FallbackPrompt(name, concept), FromAi: false);
    }

    public static string PickEmoji(string? concept)
    {
        var c = (concept ?? "").ToLowerInvariant();
        bool Any(params string[] k) => k.Any(c.Contains);
        if (Any("기사", "검", "전사", "팔라딘")) return "🛡️";
        if (Any("공주", "영애", "여왕", "황녀")) return "👸";
        if (Any("마법", "위저드", "현자", "소서러")) return "🔮";
        if (Any("요정", "정령", "숲")) return "🧚";
        if (Any("메이드", "집사", "하녀")) return "🎀";
        if (Any("악마", "마왕", "타락")) return "😈";
        if (Any("도적", "암살", "고양이", "냥")) return "🐱";
        if (Any("열혈", "용사", "불꽃", "히어로")) return "🔥";
        if (Any("사투리", "아재", "바다")) return "🌊";
        if (Any("아이돌", "댄서", "스타")) return "✨";
        if (Any("돈", "부자", "상인", "구두쇠")) return "💰";
        return "🎭";
    }

    private static string FallbackPrompt(string name, string? concept)
    {
        var c = concept ?? "";
        if (c.Contains("공주") || c.Contains("기사"))
            return $"{name}에 걸맞은 당당하고 기품 넘치는 고결한 말투. 기사도의 명예를 중시하며, 어미로 '~하옵니다', '나의 검을 걸고!' 등을 사용함.";
        if (c.Contains("츤데레") || c.Contains("메이드"))
            return $"{name}에 어울리는 도도하지만 속으로는 주인을 챙기는 츤데레 말투. 어미로 '~라구요!', '흥!' 등을 사용함.";
        if (c.Contains("열혈") || c.Contains("용사"))
            return $"{name}에 어울리는 패기 넘치고 활기찬 주인공 말투. 동료들을 독려하며 희망찬 대사를 즐겨 사용함.";
        return $"{name}에 어울리는 개성 넘치고 몰입감 있는 말투. 현재 게임 상황(전투, 채집, 휴식)에 맞추어 생생하고 재치 있게 반응함.";
    }
}
