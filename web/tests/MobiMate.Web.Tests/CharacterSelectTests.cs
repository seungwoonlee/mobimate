using System.Text.RegularExpressions;

namespace MobiMate.Web.Tests;

/// <summary>
/// 캐릭터 선택창 (v1.5 요청 1): 서버·직업이 비고 레벨이 0인 정보는 캐릭터가 아니다.
/// "에린 · 밀레시안 · Lv.0" 같은 가짜 캐릭터를 기록하지 않고, 화면에는 마지막 실제 캐릭터를 흐리게 보여 준다.
/// </summary>
public class CharacterSelectTests
{
    private sealed class Samples : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-select-" + Guid.NewGuid().ToString("N"));

        public Samples()
        {
            Directory.CreateDirectory(_dir);
            var src = FindSamples();
            foreach (var cmd in new[] { "get_my_info", "get_activity", "get_current_environment", "get_currencies" })
                File.Copy(Path.Combine(src, cmd + ".json"), Path.Combine(_dir, cmd + ".json"));
            Environment.SetEnvironmentVariable("FAKECLI_SAMPLES_DIR", _dir);
            Environment.SetEnvironmentVariable("MobiMate__QueryCacheTtl", "00:00:00");
        }

        private static string FindSamples()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "tools", "FakeCli", "Samples"))) d = d.Parent;
            return Path.Combine(d!.FullName, "tools", "FakeCli", "Samples");
        }

        /// <summary>캐릭터 선택창에서 읽히는 정보: 이름·직업이 비고 레벨 0, 전투력 0</summary>
        public void ShowCharacterSelect()
        {
            var p = Path.Combine(_dir, "get_my_info.json");
            var t = File.ReadAllText(p);
            t = Regex.Replace(t, "\"RealmName\"\\s*:\\s*\"[^\"]*\"", "\"RealmName\": \"\"");
            t = Regex.Replace(t, "\"EnabledCombatJobDisplayName\"\\s*:\\s*\"[^\"]*\"", "\"EnabledCombatJobDisplayName\": \"\"");
            t = Regex.Replace(t, "\"Level\"\\s*:\\s*\\d+", "\"Level\": 0");
            t = Regex.Replace(t, "(\"CombatScore\"\\s*:\\s*\\{[^}]*\"Value\"\\s*:\\s*)\\d+", "${1}0");
            File.WriteAllText(p, t);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("FAKECLI_SAMPLES_DIR", null);
            Environment.SetEnvironmentVariable("MobiMate__QueryCacheTtl", null);
            try { Directory.Delete(_dir, true); } catch { }
        }
    }

    [Fact]
    public async Task CharacterSelectScreen_KeepsTheLastRealCharacter_AndMakesNoFakeOne()
    {
        using var s = new Samples();
        using var host = new TestHost();
        var c = await host.LocalAsync();

        var real = await TestHost.Data(await c.GetAsync("/api/header"));
        Assert.False(real.GetProperty("selecting").GetBoolean());
        Assert.Equal("아이라_격투가", real.GetProperty("characterKey").GetString());

        s.ShowCharacterSelect();
        var held = await TestHost.Data(await c.GetAsync("/api/header"));
        Assert.True(held.GetProperty("selecting").GetBoolean());                                   // 선택 중 표시
        Assert.Equal("아이라_격투가", held.GetProperty("characterKey").GetString());                  // 마지막 실제 캐릭터를 그대로
        Assert.Equal(88737, held.GetProperty("scores").GetProperty("combat").GetInt64());          // 전투력 0으로 덮지 않는다

        await c.GetAsync("/api/currencies");
        var list = (await TestHost.Data(await c.GetAsync("/api/characters"))).EnumerateArray().ToList();
        Assert.Single(list);                                                                       // "에린_밀레시안" 같은 가짜 캐릭터가 없다
        Assert.Equal("아이라_격투가", list[0].GetProperty("key").GetString());
    }

    [Fact]
    public async Task CharacterSelectScreen_WithNoRealCharacterYet_ExplainsInsteadOfInventing()
    {
        using var s = new Samples();
        s.ShowCharacterSelect();
        using var host = new TestHost();
        var c = await host.LocalAsync();

        var r = await c.GetAsync("/api/header");
        Assert.False(r.IsSuccessStatusCode);
        Assert.Contains("캐릭터를 선택하는 중", await r.Content.ReadAsStringAsync());

        await c.GetAsync("/api/currencies");   // 재화만 읽혀도 가짜 캐릭터를 만들지 않는다
        Assert.Equal(0, (await TestHost.Data(await c.GetAsync("/api/characters"))).GetArrayLength());
    }

    [Fact]
    public async Task OldFakeProfile_IsHiddenFromTheCharacterList()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-fake-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            // 이전 버전이 남긴 가짜 기록 (에린_밀레시안, 레벨 1)
            File.WriteAllText(Path.Combine(dir, "character_db.json"), """
                { "에린_밀레시안": { "CharacterKey": "에린_밀레시안", "RealmName": "에린", "JobName": "밀레시안", "CustomName": "", "FirstSeen": "2026-09-01T10:00:00", "LastSeen": "2026-09-01T10:00:00",
                  "History": [ { "Timestamp": "2026-09-01T10:00:00", "Level": 1, "Title": "", "CombatScore": 0, "Gold": 100 } ] } }
                """);
            using var host = new TestHost(dir) { KeepStorage = true };
            var c = await host.LocalAsync();
            Assert.Equal(0, (await TestHost.Data(await c.GetAsync("/api/characters"))).GetArrayLength());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
