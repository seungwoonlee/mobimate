using System.Text.RegularExpressions;

namespace MobiMate;

/// <summary>랭킹 한 줄 읽은 결과. Found가 false면 그 이름의 순위가 없다("결과가 없습니다").</summary>
public sealed record RankReading(bool Found, int? Rank, long? Score, string? Name, string? ClassName, long? CombatScore, long? LivingScore, long? AttractScore)
{
    public static readonly RankReading NotFound = new(false, null, null, null, null, null, null, null);
}

/// <summary>
/// 넥슨 랭킹 검색 응답에서 검색한 캐릭터의 항목(<c>&lt;li class="item … on"&gt;</c>)을 읽는다 (2026-10-05 실측 형식).
/// 순위는 <c>&lt;dt&gt;N위&lt;/dt&gt;</c>, 이름은 <c>data-charactername</c>, 점수는 <c>&lt;dd class="type_N"&gt;</c>다.
/// 종합 랭킹은 점수가 4개다: 합계, 전투력, 생활력, 매력 순.
/// 형식이 다르면 null (조회 실패와 "순위 없음"을 구분하려고 NotFound와 다르게 돌려준다).
/// </summary>
public static partial class RankingParser
{
    [GeneratedRegex(@"<li\s+class=""item[^""]*\bon\b[^""]*"">(?<body>.*?)</li>", RegexOptions.Singleline)]
    private static partial Regex OnItem();

    [GeneratedRegex(@"<dt>\s*(?<n>[\d,]+)\s*위\s*</dt>")]
    private static partial Regex RankText();

    [GeneratedRegex(@"data-charactername=""(?<n>[^""]*)""")]
    private static partial Regex NameAttr();

    [GeneratedRegex(@"<dt>\s*클래스\s*</dt>\s*<dd[^>]*>\s*(?<c>[^<]*?)\s*</dd>")]
    private static partial Regex ClassText();

    [GeneratedRegex(@"<dd\s+class=""type_(?<t>\d)"">\s*(?<v>[\d,]+)\s*</dd>")]
    private static partial Regex Score();

    /// <param name="html">검색 응답 전체, 또는 북마크릿이 보낸 <c>on</c> 항목 하나</param>
    public static RankReading? Parse(string? html, RankKind kind)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        if (html.Contains("결과가 없습니다", StringComparison.Ordinal)) return RankReading.NotFound;

        var m = OnItem().Match(html);
        var body = m.Success ? m.Groups["body"].Value : html;   // 항목 조각만 온 경우는 <li>가 없을 수 있다
        var rank = RankText().Match(body);
        if (!rank.Success) return null;

        var scores = Score().Matches(body).Select(x => (Type: int.Parse(x.Groups["t"].Value), Value: long.Parse(x.Groups["v"].Value.Replace(",", "")))).ToList();
        if (scores.Count == 0) return null;
        // 종합은 type 속성이 아니라 순서로 구분한다 (합계, 전투력, 생활력, 매력)
        long? combat = null, living = null, attract = null;
        long score;
        if (kind == RankKind.Total)
        {
            if (scores.Count < 4) return null;
            score = scores[0].Value; combat = scores[1].Value; living = scores[2].Value; attract = scores[3].Value;
        }
        else
        {
            score = scores[0].Value;
            if (kind == RankKind.Combat) combat = score; else if (kind == RankKind.Living) living = score; else attract = score;
        }

        var name = NameAttr().Match(body) is { Success: true } nm ? System.Net.WebUtility.HtmlDecode(nm.Groups["n"].Value) : null;
        var cls = ClassText().Match(body) is { Success: true } cm ? System.Net.WebUtility.HtmlDecode(cm.Groups["c"].Value).Trim() : null;
        return new RankReading(true, int.Parse(rank.Groups["n"].Value.Replace(",", "")), score, name, cls, combat, living, attract);
    }
}
