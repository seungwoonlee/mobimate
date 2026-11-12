using Xunit;

namespace MobiMate.Tests;

/// <summary>계정 공통 재화 (2026-11-13): 목록·우선순위·패키지 포인트의 시즌 이름</summary>
public class SharedCurrenciesTests
{
    private static CurrencyItem C(string n, long a) => new(n, a);

    [Fact]
    public void Order_IsTheOneTheUserChose_AndHasSevenKinds() =>
        Assert.Equal(new[] { "M캐시", "데카", "길드 토큰", "패키지 포인트", "캐시샵 포인트", "환생석", "웨카" }, SharedCurrencies.Order);

    [Theory]
    [InlineData("M캐시", 0)]
    [InlineData("M캐쉬", 0)]
    [InlineData("데카", 1)]
    [InlineData("패키지 포인트: 그랜드 앙상블", 3)]
    [InlineData("패키지 포인트: 다음 시즌", 3)]
    [InlineData("길드 토큰", 2)]
    [InlineData("웨카", 6)]
    [InlineData("골드", -1)]
    [InlineData("은동전", -1)]
    [InlineData("정령의 날개", -1)]
    [InlineData("클래스 마스터 메달", -1)]   // 공유 창고에 모아 둔 탓에 같아 보일 뿐이다
    [InlineData("", -1)]
    [InlineData(null, -1)]
    public void RankOf_FindsOnlyAccountCurrencies(string? name, int rank) => Assert.Equal(rank, SharedCurrencies.RankOf(name));

    [Fact]
    public void Pick_KeepsOnlyAccountCurrencies()
    {
        var d = SharedCurrencies.Pick(new[] { C("골드", 5), C("데카", 3202), C("M캐쉬", 2109), C("냥 토큰", 9), C("패키지 포인트: 그랜드 앙상블", 0) });
        Assert.Equal(3, d.Count);
        Assert.Equal(2109, d["M캐시"]);   // 옛 표기도 M캐시로 모은다
        Assert.Equal(3202, d["데카"]);
    }

    [Fact]
    public void Ordered_FollowsPriority_AndSplitsThePackageSeasonName()
    {
        var items = new Dictionary<string, long> { ["길드 토큰"] = 5, ["데카"] = 4919, ["패키지 포인트: 그랜드 앙상블"] = 0, ["M캐시"] = 180 };
        var v = SharedCurrencies.Ordered(items);
        Assert.Equal(new[] { "M캐시", "데카", "길드 토큰", "패키지 포인트" }, v.Select(x => x.Label));
        Assert.Equal("그랜드 앙상블", v[3].Sub);
        Assert.Null(v[0].Sub);
    }

    [Fact]
    public void Merge_ANewSeasonReplacesTheOldPackagePoint_ButOtherValuesStay()
    {
        var stored = new Dictionary<string, long> { ["데카"] = 100, ["패키지 포인트: 옛 시즌"] = 7, ["웨카"] = 3 };
        var read = new Dictionary<string, long> { ["데카"] = 120, ["패키지 포인트: 새 시즌"] = 0 };
        var m = SharedCurrencies.Merge(stored, read);
        Assert.False(m.ContainsKey("패키지 포인트: 옛 시즌"));
        Assert.Equal(0, m["패키지 포인트: 새 시즌"]);
        Assert.Equal(120, m["데카"]);
        Assert.Equal(3, m["웨카"]);   // 이번에 못 읽은 것은 마지막 값을 둔다
    }

    [Fact]
    public void Merge_WithoutAPackagePointInTheReading_KeepsTheStoredOne()
    {
        var m = SharedCurrencies.Merge(new Dictionary<string, long> { ["패키지 포인트: 시즌"] = 4 }, new Dictionary<string, long> { ["데카"] = 1 });
        Assert.Equal(4, m["패키지 포인트: 시즌"]);
    }
}
