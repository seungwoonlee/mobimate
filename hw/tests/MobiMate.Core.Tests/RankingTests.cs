namespace MobiMate.Tests;

/// <summary>랭킹 등급·서버 표·응답 읽기 (v0.3). 응답 형식은 2026-10-05 넥슨 랭킹 페이지 실측이다.</summary>
public class RankingTests
{
    [Theory]
    [InlineData(1, RankTier.Gold)]
    [InlineData(10, RankTier.Gold)]
    [InlineData(11, RankTier.Orange)]
    [InlineData(100, RankTier.Orange)]
    [InlineData(101, RankTier.Pink)]
    [InlineData(1000, RankTier.Pink)]
    [InlineData(1001, RankTier.Purple)]
    [InlineData(10000, RankTier.Purple)]
    [InlineData(10001, RankTier.None)]
    [InlineData(0, RankTier.None)]
    [InlineData(-5, RankTier.None)]
    public void TierBoundaries(int rank, RankTier tier) => Assert.Equal(tier, RankTiers.Of(rank));

    [Fact]
    public void NoRank_HasNoTier() => Assert.Equal(RankTier.None, RankTiers.Of(null));

    [Theory]
    [InlineData("아이라", 2)]
    [InlineData(" 데이안 ", 1)]
    [InlineData("몰리", 8)]
    public void KnownServers(string realm, int id) => Assert.Equal(id, RankingServers.IdOf(realm));

    [Theory]
    [InlineData("에린")]     // 기본값으로 채워지는 이름: 서버가 아니다
    [InlineData("")]
    [InlineData(null)]
    public void UnknownServers_AreNull(string? realm) => Assert.Null(RankingServers.IdOf(realm));

    private static string Item(int rank, string name, string cls, string type, params string[] scores) =>
        $"<li class=\"item rank01 on\"> <div> <dl> <dt>{rank}위</dt> </dl> </div> <div> <dl> <dt>서버명</dt> <dd>아이라</dd> </dl> </div> " +
        $"<div> <dl> <dt>캐릭터명</dt> <dd data-charactername=\"{name}\">{name}</dd> </dl> </div> <div> <dl> <dt>클래스</dt> <dd class=\"warrior_4\"> {cls} </dd> </dl> </div> " +
        string.Concat(scores.Select((v, i) => $"<div> <dl> <dt>{type}</dt> <dd class=\"type_{i + 1}\"> {v} </dd> </dl> </div> ")) + "</li>";

    private static string Page(string onItem) =>
        "<ul class=\"list\"> <li class=\"item \"> <div> <dl> <dt>7위</dt> </dl> </div> <div> <dl> <dt>캐릭터명</dt> <dd data-charactername=\"다른사람\">다른사람</dd> </dl> </div> " +
        "<div> <dl> <dt>전투력 </dt> <dd class=\"type_1\"> 100,000 </dd> </dl> </div> </li> " + onItem + " </ul>";

    [Fact]
    public void Combat_ReadsTheOnItem_NotTheNeighbours()
    {
        var r = RankingParser.Parse(Page(Item(8, "헤니컵A", "검술사", "전투력", "141,099")), RankKind.Combat)!;
        Assert.True(r.Found);
        Assert.Equal(8, r.Rank);
        Assert.Equal(141099, r.Score);
        Assert.Equal(141099, r.CombatScore);
        Assert.Equal("헤니컵A", r.Name);
        Assert.Equal("검술사", r.ClassName);
    }

    [Fact]
    public void Total_HasFourScores_SumFirst()
    {
        var r = RankingParser.Parse(Item(1, "헤니컵A", "검술사", "종합 점수", "245,172", "141,099", "45,237", "58,836"), RankKind.Total)!;
        Assert.Equal(1, r.Rank);
        Assert.Equal(245172, r.Score);
        Assert.Equal(141099, r.CombatScore);
        Assert.Equal(45237, r.LivingScore);
        Assert.Equal(58836, r.AttractScore);
        Assert.Equal(r.Score, r.CombatScore + r.LivingScore + r.AttractScore);   // 종합 = 전투력 + 생활력 + 매력
    }

    [Theory]
    [InlineData(RankKind.Living)]
    [InlineData(RankKind.Attract)]
    public void LivingAndAttract_ReadTheirOwnScore(RankKind kind)
    {
        var r = RankingParser.Parse(Item(66, "헤니컵A", "검술사", "점수", "58,836"), kind)!;
        Assert.Equal(66, r.Rank);
        Assert.Equal(58836, kind == RankKind.Living ? r.LivingScore : r.AttractScore);
    }

    [Fact]
    public void ThousandsSeparators_InRank() =>
        Assert.Equal(1234, RankingParser.Parse(Item(1, "a", "검술사", "전투력", "10").Replace("1위", "1,234위"), RankKind.Combat)!.Rank);

    [Fact]
    public void NoResult_IsNotFound_NotAFailure()
    {
        var r = RankingParser.Parse("<div class=\"no_data\">결과가 없습니다.</div>", RankKind.Combat)!;
        Assert.False(r.Found);
        Assert.Null(r.Rank);
    }

    [Fact]
    public void HtmlEscapedNames_AreDecoded() =>
        Assert.Equal("A&B", RankingParser.Parse(Item(3, "A&amp;B", "궁수", "전투력", "1"), RankKind.Combat)!.Name);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("<html><body>보안 검사를 진행해 주세요</body></html>")]   // Cloudflare 검사 화면 등 형식이 다른 응답은 실패다 (순위 없음과 다르다)
    [InlineData("<li class=\"item on\"><dt>3위</dt></li>")]              // 점수가 없다
    public void UnexpectedFormat_IsNull(string? html) => Assert.Null(RankingParser.Parse(html, RankKind.Combat));

    [Fact]
    public void HugeOrOverflowingInput_IsNull_NotACrash()
    {
        Assert.Null(RankingParser.Parse(new string('x', 300_000), RankKind.Combat));                                   // 너무 크다
        Assert.Null(RankingParser.Parse(Item(1, "a", "검술사", "전투력", "1").Replace("1위", "99999999999999위"), RankKind.Combat));   // 순위가 정수 범위를 넘는다
        Assert.Null(RankingParser.Parse("<li class=\"item on\">" + string.Concat(Enumerable.Repeat("<dt>", 5000)), RankKind.Combat));   // 닫히지 않은 입력
    }

    [Fact]
    public void Total_WithFewerThanFourScores_IsNull() =>
        Assert.Null(RankingParser.Parse(Item(1, "a", "검술사", "종합 점수", "5", "3"), RankKind.Total));
}
