using System.Text;

namespace MobiMate;

public enum ChatCountMode
{
    CodePoint, // 유니코드 코드포인트 (이모지 1자) — 잠정 기본값
    Utf16      // UTF-16 코드 단위 (WPF판 string.Length, 이모지 2자)
}

/// <summary>
/// 게임 채팅 글자 수 세기·절단의 단일 규칙 (FR-GC-02).
/// 게임이 실제로 세는 단위는 TST-07(M3) 실측 후 <see cref="Mode"/>로 확정한다.
/// </summary>
public static class ChatText
{
    public const int MaxLength = 50;

    public static ChatCountMode Mode { get; set; } = ChatCountMode.CodePoint;

    public static int Count(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (Mode == ChatCountMode.Utf16) return s.Length;
        var n = 0;
        foreach (var _ in s.EnumerateRunes()) n++;
        return n;
    }

    /// <summary>현재 Mode 기준 max 이하로 자른다. 서러게이트 페어를 쪼개지 않는다.</summary>
    public static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s) || max <= 0) return "";
        if (Count(s) <= max) return s;

        var sb = new StringBuilder();
        var used = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            var cost = Mode == ChatCountMode.Utf16 ? rune.Utf16SequenceLength : 1;
            if (used + cost > max) break;
            sb.Append(rune.ToString());
            used += cost;
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>개행을 공백으로 바꾸고 앞뒤 공백을 없앤다.</summary>
    public static string Sanitize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
