using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MobiMate;

/// <summary>
/// AI 엔진과 대화하여 새로운 페르소나를 기획·프롬프트화하고 구조화된 CustomPersona를 생성하는 서비스.
/// </summary>
public class PersonaGeneratorService
{
    private readonly IAiEngine? _engine;

    public PersonaGeneratorService(IAiEngine? engine = null)
    {
        _engine = engine;
    }

    /// <summary>
    /// 사용자 요청 쿼리를 바탕으로 AI를 활용해 CustomPersona를 생성.
    /// AI 엔진 실패 또는 내장 모드 시 규칙 기반 스마트 Fallback으로 무조건 성공 보장.
    /// </summary>
    public async Task<CustomPersona> GeneratePersonaAsync(string query, string concept, CancellationToken ct = default)
    {
        if (_engine != null && _engine.Info.Type != AiEngineType.BuiltInGuide)
        {
            try
            {
                var prompt =
                    $"당신은 마비노기 모바일 인게임 페르소나 디자이너입니다.\n" +
                    $"사용자가 다음 컨셉의 페르소나 생성을 요청했습니다: \"{query}\"\n\n" +
                    $"반드시 아래 JSON 포맷으로만 응답해주세요 (마크다운 ```json 코드블록 내 작성):\n" +
                    $"{{\n" +
                    $"  \"name\": \"간결한 페르소나 명칭 (예: 공주기사 스타일)\",\n" +
                    $"  \"emoji\": \"어울리는 이모지 1개 (예: 🛡️)\",\n" +
                    $"  \"prompt\": \"인게임 채팅에서 사용할 캐릭터의 상세한 말투, 성격, 어미 특징, 행동 묘사 지침 (2~3문장)\"\n" +
                    $"}}";

                var res = await _engine.GenerateResponseAsync(prompt, "너는 마비노기 모바일 페르소나 생성기다. JSON으로만 답하라.", ct);
                if (res.Success && !string.IsNullOrWhiteSpace(res.Reply))
                {
                    var parsed = TryParseJsonPersona(res.Reply);
                    if (parsed != null)
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // AI 엔진 실패 시 Fallback으로 전환
            }
        }

        // 스마트 Fallback 생성
        return GenerateFallbackPersona(concept);
    }

    public static CustomPersona? TryParseJsonPersona(string rawText)
    {
        try
        {
            var json = rawText.Trim();
            // ```json ... ``` 코드 블록 추출
            var match = Regex.Match(json, @"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline);
            if (match.Success)
            {
                json = match.Groups[1].Value.Trim();
            }
            else
            {
                var braceMatch = Regex.Match(json, @"\{.*?\}", RegexOptions.Singleline);
                if (braceMatch.Success) json = braceMatch.Value.Trim();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var pName) ? pName.GetString() : null;
            var emoji = root.TryGetProperty("emoji", out var pEmoji) ? pEmoji.GetString() : "🎭";
            var prompt = root.TryGetProperty("prompt", out var pPrompt) ? pPrompt.GetString() : null;

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(prompt))
            {
                return new CustomPersona
                {
                    Name = name.Trim(),
                    TagEmoji = string.IsNullOrWhiteSpace(emoji) ? "🎭" : emoji.Trim(),
                    SystemPrompt = prompt.Trim()
                };
            }
        }
        catch
        {
            // JSON 파싱 실패 무해 처리
        }

        return null;
    }

    public static CustomPersona GenerateFallbackPersona(string concept)
    {
        var name = string.IsNullOrWhiteSpace(concept) ? "맞춤 스타일" : concept.Trim();
        if (!name.EndsWith("스타일") && !name.EndsWith("말투"))
        {
            name += " 스타일";
        }

        var emoji = PickEmojiForConcept(concept);
        var prompt = BuildFallbackPrompt(name, concept);

        return new CustomPersona
        {
            Name = name,
            TagEmoji = emoji,
            SystemPrompt = prompt
        };
    }

    public static string PickEmojiForConcept(string concept)
    {
        var c = concept.ToLowerInvariant();
        if (c.Contains("기사") || c.Contains("검") || c.Contains("전사") || c.Contains("팔라딘")) return "🛡️";
        if (c.Contains("공주") || c.Contains("영애") || c.Contains("여왕") || c.Contains("황녀")) return "👸";
        if (c.Contains("마법") || c.Contains("위저드") || c.Contains("현자") || c.Contains("소서러")) return "🔮";
        if (c.Contains("요정") || c.Contains("정령") || c.Contains("숲")) return "🧚";
        if (c.Contains("메이드") || c.Contains("집사") || c.Contains("하녀")) return "🎀";
        if (c.Contains("악마") || c.Contains("마왕") || c.Contains("타락")) return "😈";
        if (c.Contains("도적") || c.Contains("암살") || c.Contains("고양이") || c.Contains("냥")) return "🐱";
        if (c.Contains("열혈") || c.Contains("용사") || c.Contains("불꽃") || c.Contains("히어로")) return "🔥";
        if (c.Contains("사투리") || c.Contains("아재") || c.Contains("바다")) return "🌊";
        if (c.Contains("아이돌") || c.Contains("댄서") || c.Contains("스타")) return "✨";
        if (c.Contains("돈") || c.Contains("부자") || c.Contains("상인") || c.Contains("구두쇠")) return "💰";
        return "🎭";
    }

    private static string BuildFallbackPrompt(string name, string concept)
    {
        if (concept.Contains("공주") || concept.Contains("기사"))
        {
            return $"{name}에 걸맞은 당당하고 기품 넘치는 고결한 말투. 기사도의 명예를 중시하며, 어미로 '~하옵니다', '나의 검을 걸고!' 등을 사용함.";
        }
        if (concept.Contains("츤데레") || concept.Contains("메이드"))
        {
            return $"{name}에 어울리는 도도하지만 속으로는 주인을 챙기는 츤데레 말투. 어미로 '~라구요!', '흥!' 등을 사용함.";
        }
        if (concept.Contains("열혈") || concept.Contains("용사"))
        {
            return $"{name}에 어울리는 패기 넘치고 활기찬 주인공 말투. 동료들을 독려하며 희망찬 대사를 즐겨 사용함.";
        }

        return $"{name}에 어울리는 개성 넘치고 몰입감 있는 말투. 현재 게임 상황(전투, 채집, 휴식)에 맞추어 생생하고 재치 있게 반응함.";
    }
}
