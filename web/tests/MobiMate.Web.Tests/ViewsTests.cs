using System.Net;
using System.Net.Http.Json;

namespace MobiMate.Web.Tests;

/// <summary>v1.5 화면용 API: 즐겨찾기(FR-DT-15·21), 가공 종류(FR-DT-20), 전체 캐릭터 현황(FR-AL)</summary>
public class ViewsTests
{
    /// <summary>/api/characters 응답({ accounts: [{ members: [...] }] })의 모든 캐릭터 카드</summary>
    internal static List<System.Text.Json.JsonElement> Cards(System.Text.Json.JsonElement data) =>
        data.GetProperty("accounts").EnumerateArray().SelectMany(a => a.GetProperty("members").EnumerateArray()).ToList();

    [Theory]
    [InlineData(700.0, 1080.0, true)]
    [InlineData(0.0, 3.40282347E+38, false)]   // 로딩·전환 중 게임이 주는 터무니없는 최대치 (2026-10-12 로그)
    [InlineData(10.0, 0.0, false)]
    [InlineData(double.NaN, 1080.0, false)]
    public void SaneWeight_RejectsAbsurdReadings(double current, double max, bool expected)
        => Assert.Equal(expected, MobiMate.Web.Services.GameViews.SaneWeight(new VitalsInfo(100, 100, current, max, 0, 0, 0)));

    [Fact]
    public void SaneWeight_NullIsNotSane() => Assert.False(MobiMate.Web.Services.GameViews.SaneWeight(null));

    [Theory]
    [InlineData(true, true, false, false)]     // 평소: 1초 읽기는 하지 않는다(기존 읽기 주기가 맡는다)
    [InlineData(true, true, true, true)]       // 의심 조건이 시작되면 1초마다
    [InlineData(true, false, true, false)]     // 던전·레이드 밖이면 하지 않는다
    [InlineData(false, true, true, false)]     // 게임에 연결돼 있지 않으면 하지 않는다
    public void AutoWatcher_ReadsEverySecondOnlyWhileSuspicionIsCounting(bool connected, bool inInstance, bool needsFast, bool expected)
        => Assert.Equal(expected, Services.AutoWatcher.ShouldRead(connected, inInstance, needsFast));

    [Fact]
    public async Task Nearby_HasAutoSuspectFields_AndNobodyIsSuspectedOutsideAnInstance()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var near = await TestHost.Data(await c.GetAsync("/api/nearby"));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, near.GetProperty("missing").ValueKind);
        Assert.Equal(0, near.GetProperty("missing").GetArrayLength());
        Assert.NotEmpty(near.GetProperty("players").EnumerateArray());
        Assert.All(near.GetProperty("players").EnumerateArray(), p => Assert.Equal(System.Text.Json.JsonValueKind.Null, p.GetProperty("autoSuspect").ValueKind));
    }

    [Fact]
    public async Task Favorites_AreSavedAndShownOnInventoryAndGatherables()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();

        var inv = await TestHost.Data(await c.GetAsync("/api/inventory"));
        var name = inv.GetProperty("items")[0].GetProperty("name").GetString()!;
        Assert.All(inv.GetProperty("items").EnumerateArray(), i => Assert.False(i.GetProperty("favorite").GetBoolean()));

        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/favorites/items", new { name, favorite = true })).StatusCode);
        inv = await TestHost.Data(await c.GetAsync("/api/inventory"));
        Assert.All(inv.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("name").GetString() == name), i => Assert.True(i.GetProperty("favorite").GetBoolean()));

        var life = await TestHost.Data(await c.GetAsync("/api/life"));
        var gname = life.GetProperty("gatherables")[0].GetProperty("name").GetString()!;
        Assert.False(life.GetProperty("gatherables")[0].GetProperty("favorite").GetBoolean());
        await c.PutAsJsonAsync("/api/favorites/gather", new { name = gname, favorite = true });
        life = await TestHost.Data(await c.GetAsync("/api/life"));
        Assert.True(life.GetProperty("gatherables").EnumerateArray().First(g => g.GetProperty("name").GetString() == gname).GetProperty("favorite").GetBoolean());

        var all = await TestHost.Data(await c.GetAsync("/api/favorites"));
        Assert.Equal(name, all.GetProperty("items")[0].GetString());
        Assert.Equal(gname, all.GetProperty("gather")[0].GetString());

        await c.PutAsJsonAsync("/api/favorites/items", new { name, favorite = false });
        Assert.Equal(0, (await TestHost.Data(await c.GetAsync("/api/favorites"))).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Favorites_SurviveARestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-fav-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var host = new TestHost(dir) { KeepStorage = true })
            {
                var c = await host.LocalAsync();
                await c.PutAsJsonAsync("/api/favorites/items", new { name = "질긴 가죽", favorite = true });
            }
            using (var host = new TestHost(dir) { KeepStorage = true })
            {
                var c = await host.LocalAsync();
                Assert.Equal("질긴 가죽", (await TestHost.Data(await c.GetAsync("/api/favorites"))).GetProperty("items")[0].GetString());
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Favorites_RejectBadInput()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/favorites/nothing", new { name = "a", favorite = true })).StatusCode);
        var empty = await c.PutAsJsonAsync("/api/favorites/items", new { name = "  ", favorite = true });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/favorites/items", new { name = new string('가', 101), favorite = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/favorites/items", new { name = "a" })).StatusCode);
    }

    [Fact]
    public async Task Life_WorksCarryTheirKind()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var works = (await TestHost.Data(await c.GetAsync("/api/life"))).GetProperty("works").EnumerateArray().ToList();
        Assert.NotEmpty(works);
        string Kind(string n) => works.First(w => w.GetProperty("name").GetString() == n).GetProperty("kind").GetString()!;
        Assert.Equal("leather", Kind("질긴 가죽"));
        Assert.Equal("cloth", Kind("고급 옷감"));
        Assert.Equal("cloth", Kind("최고급 실크"));
        Assert.All(works, w => Assert.False(string.IsNullOrEmpty(w.GetProperty("kindLabel").GetString())));
    }

    [Fact]
    public async Task Characters_ListsTheCurrentOneWithLiveValuesFirst()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.GetAsync("/api/header");        // 프로필·기록이 생긴다
        await c.GetAsync("/api/currencies");

        var list = Cards(await TestHost.Data(await c.GetAsync("/api/characters")));
        var me = list[0];
        Assert.True(me.GetProperty("isCurrent").GetBoolean());
        Assert.Equal("아이라_격투가", me.GetProperty("key").GetString());
        Assert.Equal(88737, me.GetProperty("combat").GetInt64());
        Assert.Equal(19745, me.GetProperty("attract").GetInt64());
        Assert.Equal(23011, me.GetProperty("living").GetInt64());
        Assert.True(me.GetProperty("gold").GetInt64() > 0);
        Assert.True(me.GetProperty("deca").GetInt64() > 0);
    }

    [Fact]
    public async Task Characters_RememberOtherCharactersAfterTheyLeave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-chars-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var host = new TestHost(dir) { KeepStorage = true })
            {
                var c = await host.LocalAsync();
                await c.GetAsync("/api/header");
                await c.GetAsync("/api/currencies");
                await Task.Delay(300);   // 저장은 비동기
            }
            using (var host = new TestHost(dir) { KeepStorage = true })
            {
                var c = await host.LocalAsync();   // 서버를 다시 켠 뒤에도 같은 캐릭터 기록이 이어진다
                var list = Cards(await TestHost.Data(await c.GetAsync("/api/characters")));
                var me = Assert.Single(list);
                Assert.Equal(88737, me.GetProperty("combat").GetInt64());
                Assert.Equal(19745, me.GetProperty("attract").GetInt64());
                Assert.True(me.GetProperty("deca").GetInt64() > 0);
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
