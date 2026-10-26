using System.Net;
using System.Net.Http.Json;

namespace MobiMate.Web.Tests;

/// <summary>
/// 같은 캐릭터가 둘로 갈라진 기록을 합친다(2026-10-26 울랄라힐러: 레벨이 85→91로 올라 "아이라_힐러#3"이 새로 생겼다).
/// 가짜 CLI의 현재 캐릭터는 아이라_격투가다. 저장소에 "바람_도적"과 "바람_도적#2", 직업이 다른 "바람_궁수"를 미리 넣는다.
/// </summary>
public class MergeCharactersTests
{
    private static string Record(DateTime at, int level) =>
        $$"""{ "Timestamp": "{{at:s}}", "Level": {{level}}, "Title": "", "CombatScore": 90000, "ArcaneResistance": 3000, "LivingScore": 1000, "AttractivenessScore": 5000, "Gold": 500000, "Deca": 777, "MCash": 888, "SilverCoin": 10, "DemonTribute": 1 }""";

    private static string Profile(string key, string realm, string job, DateTime lastSeen, params string[] records) =>
        $$"""{ "CharacterKey": "{{key}}", "RealmName": "{{realm}}", "JobName": "{{job}}", "CustomName": "", "FirstSeen": "{{lastSeen.AddDays(-30):s}}", "LastSeen": "{{lastSeen:s}}", "History": [ {{string.Join(",", records)}} ] }""";

    private static string Seed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var t = DateTime.Now.AddDays(-2);
        File.WriteAllText(Path.Combine(dir, "character_history_db.json"), $$"""
            [
              {{Profile("바람_도적", "바람", "도적", t.AddDays(-1), Record(t.AddDays(-1), 85))}},
              {{Profile("바람_도적#2", "바람", "도적", t, Record(t, 91))}},
              {{Profile("바람_궁수", "바람", "궁수", t, Record(t, 90))}}
            ]
            """);
        return dir;
    }

    private static async Task<HttpResponseMessage> Merge(HttpClient c, string from, string into) =>
        await c.PostAsJsonAsync("/api/characters/merge", new { from, into });

    [Fact]
    public async Task MergingTheSplitRecord_MovesItsHistory_AndRemovesIt()
    {
        var dir = Seed();
        try
        {
            using var host = new TestHost(dir) { KeepStorage = true };
            var c = await host.LocalAsync();
            var res = await Merge(c, "바람_도적#2", "바람_도적");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var cards = ViewsTests.Cards(await TestHost.Data(await c.GetAsync("/api/characters"))).Select(x => x.GetProperty("key").GetString()).ToList();
            Assert.Contains("바람_도적", cards);
            Assert.DoesNotContain("바람_도적#2", cards);
            Assert.Contains("바람_궁수", cards);                       // 다른 캐릭터는 그대로
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task DifferentJobs_CannotBeMerged()
    {
        var dir = Seed();
        try
        {
            using var host = new TestHost(dir) { KeepStorage = true };
            var c = await host.LocalAsync();
            Assert.Equal(HttpStatusCode.Conflict, (await Merge(c, "바람_궁수", "바람_도적")).StatusCode);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task UnknownOrSameCharacter_IsRejected()
    {
        var dir = Seed();
        try
        {
            using var host = new TestHost(dir) { KeepStorage = true };
            var c = await host.LocalAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await Merge(c, "바람_도적#9", "바람_도적")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Merge(c, "바람_도적", "바람_도적")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Merge(c, "", "바람_도적")).StatusCode);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
