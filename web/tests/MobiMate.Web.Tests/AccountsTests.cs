using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MobiMate.Web.Tests;

/// <summary>
/// 전체 현황의 계정 묶음·은동전/마족 공물 예상·멤버십 (v1.5 요청 3·5).
/// 가짜 CLI의 현재 캐릭터는 아이라_격투가(데카 10,285 · M캐시 5,619 · 은동전 120 · 마족 공물 9)다.
/// 저장소에 다른 캐릭터 기록을 미리 넣어 두고 확인한다.
/// </summary>
public class AccountsTests
{
    private static string Record(DateTime at, int level, long combat, long deca, long mcash, long silver, long tribute) =>
        $$"""{ "Timestamp": "{{at:s}}", "Level": {{level}}, "Title": "", "CombatScore": {{combat}}, "ArcaneResistance": 3000, "LivingScore": 1000, "AttractivenessScore": 5000, "Gold": 500000, "Deca": {{deca}}, "MCash": {{mcash}}, "SilverCoin": {{silver}}, "DemonTribute": {{tribute}} }""";

    private static string Profile(string realm, string job, DateTime lastSeen, params string[] records) =>
        $$"""{ "CharacterKey": "{{realm}}_{{job}}", "RealmName": "{{realm}}", "JobName": "{{job}}", "CustomName": "", "FirstSeen": "{{lastSeen.AddDays(-30):s}}", "LastSeen": "{{lastSeen:s}}", "History": [ {{string.Join(",", records)}} ] }""";

    /// <summary>
    /// 아이라_격투가(지금 접속)와 같은 계정으로 보이는 도적(예전에 같은 데카·M캐시를 가졌다가 3일 전 값이 어긋남),
    /// 전혀 다른 계정의 궁수를 미리 넣은 저장 폴더
    /// </summary>
    private static string Seed(out DateTime threeDaysAgo)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-acc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var t3 = threeDaysAgo = DateTime.Now.AddDays(-3);
        var t5 = DateTime.Now.AddDays(-5);
        var db = $$"""
            [
              {{Profile("바람", "도적", t3,
                  Record(t5, 100, 99000, 10285, 5619, 90, 3),
                  Record(t3, 100, 99000, 10000, 5619, 100, 4))}},
              {{Profile("바람", "궁수", t3,
                  Record(t3, 90, 70000, 777, 888, 10, 1))}}
            ]
            """;
        File.WriteAllText(Path.Combine(dir, "character_history_db.json"), db);
        return dir;
    }

    private static string Quote(string s) => JsonSerializer.Serialize(s);

    private static JsonElement Card(JsonElement data, string key) => ViewsTests.Cards(data).First(c => c.GetProperty("key").GetString() == key);

    [Fact]
    public async Task SameDecaAndMCash_GroupsCharactersIntoOneAccount_AndTheyStayGroupedAfterTheyDrift()
    {
        var dir = Seed(out _);
        try
        {
            using var host = new TestHost(dir) { KeepStorage = true };
            var c = await host.LocalAsync();
            var data = await TestHost.Data(await c.GetAsync("/api/characters"));

            var accounts = data.GetProperty("accounts").EnumerateArray().ToList();
            Assert.Equal(2, accounts.Count);                                                   // 아이라+도적 / 궁수
            var main = accounts[0];                                                            // 지금 접속한 캐릭터의 계정이 맨 위
            Assert.Equal(data.GetProperty("currentAccountId").GetString(), main.GetProperty("id").GetString());
            Assert.Equal(new[] { "바람_도적", "아이라_격투가" }, main.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("key").GetString()).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(10285, main.GetProperty("deca").GetInt64());
            Assert.Equal(5619, main.GetProperty("mcash").GetInt64());

            // 계정 안에서는 전투력이 높은 순 (도적 99,000 > 격투가 88,737)
            Assert.Equal("바람_도적", main.GetProperty("members")[0].GetProperty("key").GetString());
            Assert.True(accounts[1].GetProperty("solo").GetBoolean() || accounts[1].GetProperty("members").GetArrayLength() == 1);

            // 값이 어긋난 도적은 "동기화 안 됨", 지금 캐릭터는 아니다
            Assert.True(Card(data, "바람_도적").GetProperty("stale").GetBoolean());
            Assert.False(Card(data, "아이라_격투가").GetProperty("stale").GetBoolean());
            Assert.False(Card(data, "바람_궁수").GetProperty("stale").GetBoolean());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Coins_AreForecastFromTheLastSeenAmount_WithMembershipDecidingTheCap()
    {
        var dir = Seed(out _);
        try
        {
            using var host = new TestHost(dir) { KeepStorage = true };
            var c = await host.LocalAsync();

            var data = await TestHost.Data(await c.GetAsync("/api/characters"));
            var rogue = Card(data, "바람_도적");
            // 미가입(상한 100 / 10): 3일 전 은동전 100 → 이미 가득(충전 멈춤), 마족 공물 4 + 6 = 10 → 가득
            Assert.Equal(100, rogue.GetProperty("silver").GetProperty("cap").GetInt64());
            Assert.Equal("full", rogue.GetProperty("silver").GetProperty("level").GetString());
            Assert.Equal(10, rogue.GetProperty("tribute").GetProperty("expected").GetInt64());
            Assert.Equal("full", rogue.GetProperty("tribute").GetProperty("level").GetString());
            // 지금 접속 중인 캐릭터는 읽은 값 그대로 (은동전 120은 미가입 상한 100을 넘어 가득, 마족 공물 9/10은 곧 가득)
            var me = Card(data, "아이라_격투가");
            Assert.Equal(120, me.GetProperty("silver").GetProperty("expected").GetInt64());
            Assert.Equal(9, me.GetProperty("tribute").GetProperty("expected").GetInt64());
            Assert.Equal("near", me.GetProperty("tribute").GetProperty("level").GetString());

            // 멤버십 등록(남은 27일 3시간): 상한이 150 / 15로 늘어 충전이 이어진다
            // 남은 일수만 입력한다: 만료 = (오늘 + 27일 − 1일) 다음 새벽 6시
            var put = await c.PutAsJsonAsync("/api/membership", new { character = "아이라_격투가", days = 27 });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var expiresAt = (await TestHost.Data(put)).GetProperty("expiresAt").GetDateTimeOffset().UtcDateTime;
            Assert.InRange((expiresAt - MembershipClock.ExpiryLocal(DateTime.Now, 27).ToUniversalTime()).TotalMinutes, -1, 1);
            data = await TestHost.Data(await c.GetAsync("/api/characters"));
            var main = data.GetProperty("accounts")[0];
            Assert.True(main.GetProperty("membership").GetProperty("active").GetBoolean());
            Assert.Equal(150, main.GetProperty("caps").GetProperty("silver").GetInt64());
            rogue = Card(data, "바람_도적");
            Assert.Equal(150, rogue.GetProperty("silver").GetProperty("expected").GetInt64());     // 100 + 3일×48 = 상한 150
            Assert.Equal("full", rogue.GetProperty("silver").GetProperty("level").GetString());
            Assert.Equal(10, rogue.GetProperty("tribute").GetProperty("expected").GetInt64());     // 4 + 6 = 10 (상한 15 아래)
            Assert.Equal("ok", rogue.GetProperty("tribute").GetProperty("level").GetString());     // 10/15 = 67%

            // 해제: 0일 0시간
            await c.PutAsJsonAsync("/api/membership", new { character = "아이라_격투가", days = 0 });
            data = await TestHost.Data(await c.GetAsync("/api/characters"));
            Assert.False(data.GetProperty("accounts")[0].GetProperty("membership").GetProperty("active").GetBoolean());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Membership_RejectsBadInput()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/membership", new { days = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/membership", new { character = "a_b", days = 401 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/membership", new { character = "a_b", days = -1 })).StatusCode);
        // 기록에 없는 캐릭터에는 저장하지 않는다 (남는 계정 항목이 생기지 않게)
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/membership", new { character = "없는_캐릭터", days = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/accounts/assign", new { character = "없는_캐릭터", account = "new" })).StatusCode);
    }

    [Fact]
    public async Task ManualAssignment_MovesACharacterToAnotherAccount_AndSurvivesARestart()
    {
        var dir = Seed(out _);
        try
        {
            string archerAccount;
            using (var host = new TestHost(dir) { KeepStorage = true })
            {
                var c = await host.LocalAsync();
                var data = await TestHost.Data(await c.GetAsync("/api/characters"));
                var mainId = data.GetProperty("accounts")[0].GetProperty("id").GetString()!;
                archerAccount = data.GetProperty("accounts").EnumerateArray().First(a => a.GetProperty("members").EnumerateArray().Any(m => m.GetProperty("key").GetString() == "바람_궁수")).GetProperty("id").GetString()!;

                // 궁수를 아이라의 계정으로 직접 묶는다
                Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/accounts/assign", new { character = "바람_궁수", account = mainId })).StatusCode);
                data = await TestHost.Data(await c.GetAsync("/api/characters"));
                Assert.Single(data.GetProperty("accounts").EnumerateArray());
                Assert.Equal(3, data.GetProperty("accounts")[0].GetProperty("members").GetArrayLength());

                // 도적을 직접 "새 계정"으로 뺀다: 이후 자동 묶기가 다시 붙이지 않는다
                await c.PutAsJsonAsync("/api/accounts/assign", new { character = "바람_도적", account = "new" });
                data = await TestHost.Data(await c.GetAsync("/api/characters"));
                Assert.Equal(2, data.GetProperty("accounts").GetArrayLength());
                await Task.Delay(200);
            }
            using (var host = new TestHost(dir) { KeepStorage = true })
            {
                var c = await host.LocalAsync();
                var data = await TestHost.Data(await c.GetAsync("/api/characters"));
                var rogueAccount = data.GetProperty("accounts").EnumerateArray().First(a => a.GetProperty("members").EnumerateArray().Any(m => m.GetProperty("key").GetString() == "바람_도적"));
                Assert.Equal(1, rogueAccount.GetProperty("members").GetArrayLength());           // 그대로 따로 있다
                Assert.NotEqual(archerAccount, rogueAccount.GetProperty("id").GetString());
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task AccountOrder_IsSavedAndReturnedAsManualRank()
    {
        var dir = Seed(out _);
        try
        {
            using var host = new TestHost(dir) { KeepStorage = true };
            var c = await host.LocalAsync();
            var ids = (await TestHost.Data(await c.GetAsync("/api/characters"))).GetProperty("accounts").EnumerateArray().Select(a => a.GetProperty("id").GetString()!).ToList();
            Assert.Equal(2, ids.Count);
            Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/accounts/order", new { order = new[] { ids[1], ids[0] } })).StatusCode);
            var accounts = (await TestHost.Data(await c.GetAsync("/api/characters"))).GetProperty("accounts").EnumerateArray().ToList();
            Assert.Equal(1, accounts.First(a => a.GetProperty("id").GetString() == ids[0]).GetProperty("manualRank").GetInt32());
            Assert.Equal(0, accounts.First(a => a.GetProperty("id").GetString() == ids[1]).GetProperty("manualRank").GetInt32());
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/accounts/order", new { })).StatusCode);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task DeleteCharacter_RemovesItsRecord_ButNotTheOneThatIsConnected()
    {
        var dir = Seed(out _);
        try
        {
            using var host = new TestHost(dir) { KeepStorage = true };
            var c = await host.LocalAsync();
            await c.GetAsync("/api/characters");                                                // 지금 캐릭터를 기록한다
            Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync("/api/characters?key=" + Uri.EscapeDataString("아이라_격투가"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/characters?key=" + Uri.EscapeDataString("없는_캐릭터"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync("/api/characters?key=" + Uri.EscapeDataString("바람_궁수"))).StatusCode);
            var data = await TestHost.Data(await c.GetAsync("/api/characters"));
            Assert.DoesNotContain(ViewsTests.Cards(data), x => x.GetProperty("key").GetString() == "바람_궁수");
            Assert.Equal(1, data.GetProperty("accounts").GetArrayLength());                     // 궁수만 있던 계정도 사라졌다
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Recorder_RecordsTheCurrentCharacterCoinsAndCurrencies()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var data = await TestHost.Data(await c.GetAsync("/api/characters"));
        var me = ViewsTests.Cards(data).Single();
        Assert.Equal(10285, me.GetProperty("deca").GetInt64());
        Assert.Equal(5619, me.GetProperty("mcash").GetInt64());
        Assert.Equal(120, me.GetProperty("silver").GetProperty("held").GetInt64());
        Assert.Equal(9, me.GetProperty("tribute").GetProperty("held").GetInt64());
    }
}
