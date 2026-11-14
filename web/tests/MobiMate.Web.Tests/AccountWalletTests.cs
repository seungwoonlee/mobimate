using MobiMate.Web.Hosting;
using Xunit;

namespace MobiMate.Web.Tests;

public class AccountWalletTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-wallet-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    public AccountWalletTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private AccountWalletStore Make() => new(_dir, () => _now);
    private static CurrencyItem C(string n, long a) => new(n, a);

    [Fact]
    public void Observe_StoresOnlyAccountCurrencies_WithWhoAndWhen()
    {
        var w = Make();
        w.Observe("acc-1", new[] { C("골드", 99), C("데카", 3202), C("M캐시", 2109) }, "아이라 힐러");
        var e = w.Get("acc-1")!;
        Assert.Equal(new[] { "데카", "M캐시" }.OrderBy(x => x), e.Items.Keys.OrderBy(x => x));
        Assert.Equal("아이라 힐러", e.By);
        Assert.Equal(_now, e.AtUtc);
    }

    [Fact]
    public void Observe_FromAnotherCharacterOfTheSameAccount_ReplacesWithTheNewestValues()
    {
        var w = Make();
        w.Observe("acc-1", new[] { C("데카", 3000) }, "힐러");
        _now = _now.AddHours(5);
        w.Observe("acc-1", new[] { C("데카", 2950), C("길드 토큰", 5) }, "검술");
        var e = w.Get("acc-1")!;
        Assert.Equal(2950, e.Items["데카"]);
        Assert.Equal(5, e.Items["길드 토큰"]);
        Assert.Equal("검술", e.By);
    }

    [Fact]
    public void Observe_SurvivesARestart()
    {
        Make().Observe("acc-2", new[] { C("웨카", 8) }, "궁수");
        Assert.Equal(8, Make().Get("acc-2")!.Items["웨카"]);
    }

    [Fact]
    public void Observe_NothingToRecord_DoesNotCreateAnEntry()
    {
        var w = Make();
        w.Observe("acc-3", new[] { C("골드", 1) }, "x");
        w.Observe("acc-3", null, "x");
        Assert.Null(w.Get("acc-3"));
    }

    [Fact]
    public void Observe_SameValuesWithinAMinute_DoNotRewriteTheFile()
    {
        var w = Make();
        w.Observe("acc-1", new[] { C("데카", 1) }, "힐러");
        var path = Path.Combine(_dir, AccountWalletStore.FileName);
        var t0 = File.GetLastWriteTimeUtc(path);
        Thread.Sleep(30);
        _now = _now.AddSeconds(20);
        w.Observe("acc-1", new[] { C("데카", 1) }, "힐러");
        Assert.Equal(t0, File.GetLastWriteTimeUtc(path));
    }
}

public class LivingSkillsApiTests
{
    [Fact]
    public async Task Life_ReturnsTheCurrentCharactersLivingSkills_HighestLevelFirst()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.GetAsync("/api/header");   // 캐릭터 정보를 읽어 둔다
        var life = await TestHost.Data(await c.GetAsync("/api/life"));
        var skills = life.GetProperty("skills");
        Assert.Equal(2, skills.GetArrayLength());
        Assert.Equal("약초 채집", skills[0].GetProperty("name").GetString());   // Lv.32가 먼저
        Assert.Equal(32, skills[0].GetProperty("level").GetInt32());
        Assert.Equal(27480, skills[0].GetProperty("maxExp").GetInt64());
        Assert.Equal("낚시", skills[1].GetProperty("name").GetString());
    }
}
