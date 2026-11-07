using System.Text.RegularExpressions;

namespace MobiMate.Web.Voice;

/// <summary>
/// 인식기가 소음·신호음을 이모티콘 같은 글자로 잘못 읽는 것을 걸러 낸다 (승운 2026-11-07: 채팅 앞에 "NO:)"가 붙음).
/// 한국어 모델의 글자 목록에 영문 낱자와 ":" ")"가 들어 있어서 소리가 불분명하면 "NO:)안녕"처럼 나온다.
/// </summary>
public static partial class VoiceNoise
{
    // 영문 1~3자 + 눈웃음 모양("NO:)", "OK;("), 그리고 눈웃음 모양만(":)")
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]{1,3}\s?[:;][)(]")]
    private static partial Regex LetterEmoticon();

    [GeneratedRegex(@"[:;][)(]")]
    private static partial Regex Emoticon();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = Emoticon().Replace(LetterEmoticon().Replace(text, " "), " ");
        return Spaces().Replace(t, " ").Trim();
    }
}
