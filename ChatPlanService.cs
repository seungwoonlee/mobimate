using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MobiMate;

public record ChatPlan(string FinalMessage, string? BehaviourCommand, string Emoji);

/// <summary>
/// 채팅 문맥을 분석하여 적절한 감정 이모티콘과 인게임 소셜 액션(/행동)을 조합하는 서비스입니다.
/// </summary>
public static class ChatPlanService
{
    private static readonly (string[] Keywords, string Emoji, string? Behaviour)[] Rules =
    [
        (new[] { "미안", "죄송", "사과" }, "😓", "/사과1"),
        (new[] { "축하", "ㅊㅋ" }, "🥳", "/축하해"),
        (new[] { "고마", "감사", "ㄳ" }, "😍", "/하트"),
        (new[] { "사랑", "좋아해" }, "😍", "/하트"),
        (new[] { "안녕", "반가", "어서", "하이", "ㅎㅇ" }, "😊", "/손인사1"),
        (new[] { "잘 가", "잘가", "수고", "이만 갈", "이만갈", "ㅂㅇ" }, "😉", "/손인사1"),
        (new[] { "화이팅", "힘내", "응원" }, "🥳", "/응원댄스"),
        (new[] { "ㅋㅋ", "ㅎㅎ", "웃기", "재밌" }, "🤣", "/웃기1"),
        (new[] { "슬프", "아쉽", "흑흑", "ㅠㅠ", "ㅜㅜ" }, "😢", "/울기1"),
        (new[] { "최고", "대박", "짱", "신난", "나이스" }, "😎", "/최고")
    ];

    private static readonly string[] NegationPrefixes = { "안 ", "안", "못 ", "못" };

    private static readonly Dictionary<string, string[]> Exclusions = new()
    {
        ["사과"] = new[] { "사과 팔", "사과팔", "사과나무", "사과 나무", "사과 파", "사과파이", "사과 주스", "사과주스", "사과 판", "사과판" }
    };

    public const string DefaultEmoji = "😊";
    public const int MaxChatLength = 50;

    public static ChatPlan BuildChatPlan(string rawMessage)
    {
        var trimmed = rawMessage.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new ChatPlan("", null, DefaultEmoji);
        }

        var lowered = trimmed.ToLowerInvariant();

        foreach (var (keywords, emoji, behaviour) in Rules)
        {
            if (keywords.Any(k => KeywordApplies(lowered, k)))
            {
                var combined = CombineSafe(trimmed, emoji);
                return new ChatPlan(combined, behaviour, emoji);
            }
        }

        return new ChatPlan(CombineSafe(trimmed, DefaultEmoji), null, DefaultEmoji);
    }

    /// <summary>
    /// 미리보기용 간략 힌트 문자열을 생성합니다. (예: "😊 /손인사1")
    /// </summary>
    public static string GetPreviewHint(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage)) return "";
        var plan = BuildChatPlan(rawMessage);
        return plan.BehaviourCommand != null 
            ? $"{plan.Emoji} {plan.BehaviourCommand}" 
            : $"{plan.Emoji}";
    }

    private static bool KeywordApplies(string message, string keyword)
    {
        int start = 0;
        while (true)
        {
            int index = message.IndexOf(keyword, start, StringComparison.Ordinal);
            if (index < 0) return false;

            start = index + keyword.Length;
            string before = message[..index];

            if (NegationPrefixes.Any(p => before.EndsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }

            if (Exclusions.TryGetValue(keyword, out var exclList))
            {
                if (exclList.Any(ex => message.AsSpan(index).StartsWith(ex, StringComparison.Ordinal)))
                {
                    continue;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// 서러게이트 페어 깨짐 없이 최대 50자 이내로 원문 + 공백 + 이모지를 결합합니다.
    /// </summary>
    private static string CombineSafe(string message, string emoji)
    {
        var suffix = " " + emoji;
        var maxBodyLength = MaxChatLength - suffix.Length; // 약 46자

        string safeBody;
        if (message.Length > maxBodyLength)
        {
            // 글자 중간의 서러게이트 페어가 쪼개지지 않도록 방어
            int cutLen = maxBodyLength;
            if (cutLen > 0 && char.IsHighSurrogate(message[cutLen - 1]))
            {
                cutLen--;
            }
            safeBody = message[..cutLen].TrimEnd();
        }
        else
        {
            safeBody = message;
        }

        return safeBody + suffix;
    }
}
