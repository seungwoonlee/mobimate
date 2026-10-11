namespace MobiMateHomeWork.ViewModels;

/// <summary>
/// 직업 뱃지(직업 색 원 + 첫 글자)의 색. 직업 이름 → 색은 고정 표이고, 표에 없는 직업은 이름에서 안정적으로(실행마다 같게) 정한다.
/// 모든 색은 어두운 글자(#0F1115) 위에서 4.5:1 이상이다(테스트가 검사한다).
/// </summary>
public static class JobPalette
{
    /// <summary>뱃지 위 글자색.</summary>
    public const string LetterColor = "#0F1115";

    public const string Warrior = "#E8806A";
    public const string Archer = "#93D35B";
    public const string Thief = "#B793FF";
    public const string Mage = "#6AB0FF";
    public const string Bard = "#FF92C8";
    public const string Healer = "#4FD1C5";

    /// <summary>표에 없는 직업이 이름 해시로 고르는 색.</summary>
    public static readonly IReadOnlyList<string> Fallback =
    [
        "#C9A57F", "#9DACCB", "#D993B3", "#92CBA9", "#CBC47F", "#A393DB", "#7FC6CB", "#DBA393",
    ];

    // 게임의 직업 이름(공백 제거). 웹앱 직업 이미지 표와 같은 이름들이다.
    private static readonly Dictionary<string, string> Table = new()
    {
        ["견습전사"] = Warrior, ["기사"] = Warrior, ["전사"] = Warrior, ["대검전사"] = Warrior, ["검술사"] = Warrior,
        ["견습궁수"] = Archer, ["궁수"] = Archer, ["석궁사수"] = Archer, ["장궁병"] = Archer,
        ["견습도적"] = Thief, ["도적"] = Thief, ["격투가"] = Thief, ["듀얼블레이드"] = Thief,
        ["견습마법사"] = Mage, ["전격술사"] = Mage, ["마법사"] = Mage, ["화염술사"] = Mage, ["빙결술사"] = Mage,
        ["견습음유시인"] = Bard, ["음유시인"] = Bard, ["댄서"] = Bard, ["악사"] = Bard,
        ["견습힐러"] = Healer, ["암흑술사"] = Healer, ["힐러"] = Healer, ["사제"] = Healer, ["수도사"] = Healer,
    };

    private static string Normalize(string? job) => new((job ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());

    public static string ColorOf(string? job)
    {
        var name = Normalize(job);
        if (Table.TryGetValue(name, out var c)) return c;
        if (name.Length == 0) return Fallback[0];
        // FNV-1a: string.GetHashCode는 실행마다 값이 달라 쓰지 않는다
        uint h = 2166136261;
        foreach (var ch in name) { h ^= ch; h *= 16777619; }
        return Fallback[(int)(h % (uint)Fallback.Count)];
    }

    /// <summary>뱃지에 쓰는 첫 글자(직업을 모르면 물음표).</summary>
    public static string LetterOf(string? job)
    {
        var name = (job ?? "").Trim();
        return name.Length == 0 ? "?" : char.ConvertFromUtf32(char.ConvertToUtf32(name, 0));
    }
}
