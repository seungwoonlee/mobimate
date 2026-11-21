using MobiMate;
using MobiMate.Web.Hosting;
using Xunit;

namespace MobiMate.Web.Tests;

public class StockStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-stock-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    public StockStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private StockStore Make() => new(_dir, () => _now);
    private static ItemData It(string loc, string name, int n, string cat = "재료") => new(loc, name, cat, n, false);

    [Fact]
    public void SumsBagsAndCharacterStoragesOfEveryCharacter_AndCountsAccountStorageOnce()
    {
        var s = Make();
        s.Observe("몰리_힐러", [It("inventory", "철괴", 5), It("character_storage", "철괴", 10), It("account_storage", "철괴", 100)], _now);
        _now += TimeSpan.FromMinutes(10);
        s.Observe("몰리_궁수", [It("inventory", "철괴", 7), It("account_storage", "철괴", 100)], _now);   // 같은 계정 창고를 또 본 것: 더하면 안 된다
        var row = s.Aggregate(["몰리_힐러", "몰리_궁수"]).Single(r => r.Name == "철괴");
        Assert.Equal(12, row.Bag);
        Assert.Equal(10, row.Storage);
        Assert.Equal(100, row.Account);
        Assert.Equal(122, row.Total);
        Assert.Equal(2, row.Holders.Count);
    }

    [Fact]
    public void LaterReadOfAnotherCharacter_UsesTheNewestAccountStorage()
    {
        var s = Make();
        s.Observe("A_힐러", [It("account_storage", "석탄", 50)], _now);
        _now += TimeSpan.FromHours(1);
        s.Observe("A_궁수", [It("account_storage", "석탄", 30)], _now);   // 그 사이 20개를 썼다
        Assert.Equal(30, s.Aggregate(["A_힐러", "A_궁수"]).Single(r => r.Name == "석탄").Total);
    }

    [Fact]
    public void ColorTagsAreStripped_AndEmptyReadsAreIgnored()
    {
        var s = Make();
        s.Observe("A_힐러", [It("inventory", "<color=#fff>철괴</color>", 3)], _now);
        s.Observe("A_힐러", [], _now);   // 읽기 실패: 이전 값을 지우면 안 된다
        Assert.Equal(3, s.Aggregate(["A_힐러"]).Single(r => r.Name == "철괴").Total);
    }

    [Fact]
    public void OnlyCharactersOfTheGivenAccountAreSummed()
    {
        var s = Make();
        s.Observe("A_힐러", [It("inventory", "철괴", 3)], _now);
        s.Observe("B_힐러", [It("inventory", "철괴", 9)], _now);
        Assert.Equal(3, s.Aggregate(["A_힐러"]).Single().Total);
    }

    [Fact]
    public void DataAndPrefsSurviveARestart()
    {
        var s = Make();
        s.Observe("A_힐러", [It("inventory", "철괴", 3)], _now);
        s.Flush();
        Assert.True(s.SetTarget("철괴", 50));
        Assert.True(s.SetTimed("철괴", true));
        var again = Make();
        Assert.Equal(3, again.Aggregate(["A_힐러"]).Single().Total);
        Assert.Equal(50, again.Prefs().Targets["철괴"]);
        Assert.Contains("철괴", again.Prefs().Timed);
        Assert.True(again.SetTarget("철괴", 0));
        Assert.Empty(again.Prefs().Targets);
    }
}
