using System.Text.RegularExpressions;

namespace MobiMate.Web.Tests;

/// <summary>전투력 재확인 (FR-DT-10): 로딩 순간에 낮게 읽힌 전투력은 이전 값을 유지하다가 다시 읽어 확인한다.</summary>
public class CombatRecheckTests
{
    private static string SamplesSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "FakeCli", "Samples"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tools", "FakeCli", "Samples");
    }

    /// <summary>가짜 CLI가 읽는 샘플 폴더를 만들고, get_my_info의 전투력을 바꿀 수 있게 한다.</summary>
    private sealed class Scenario : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-samples-" + Guid.NewGuid().ToString("N"));

        public Scenario(long combat, string recheckDelay)
        {
            Directory.CreateDirectory(_dir);
            foreach (var cmd in new[] { "get_my_info", "get_activity", "get_current_environment" })
                File.Copy(Path.Combine(SamplesSource(), cmd + ".json"), Path.Combine(_dir, cmd + ".json"));
            SetCombat(combat);
            Environment.SetEnvironmentVariable("FAKECLI_SAMPLES_DIR", _dir);
            Environment.SetEnvironmentVariable("MobiMate__QueryCacheTtl", "00:00:00");
            Environment.SetEnvironmentVariable("MobiMate__CombatRecheckDelay", recheckDelay);
        }

        public void SetCombat(long combat)
        {
            var path = Path.Combine(_dir, "get_my_info.json");
            var text = File.ReadAllText(path);
            File.WriteAllText(path, Regex.Replace(text, "(\"CombatScore\"\\s*:\\s*\\{\\s*\"DisplayName\"\\s*:\\s*\"[^\"]*\"\\s*,\\s*\"Value\"\\s*:\\s*)\\d+", "${1}" + combat));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("FAKECLI_SAMPLES_DIR", null);
            Environment.SetEnvironmentVariable("MobiMate__QueryCacheTtl", null);
            Environment.SetEnvironmentVariable("MobiMate__CombatRecheckDelay", null);
            try { Directory.Delete(_dir, true); } catch { }
        }
    }

    private static async Task<long> Combat(HttpClient c) =>
        (await TestHost.Data(await c.GetAsync("/api/header"))).GetProperty("scores").GetProperty("combat").GetInt64();

    private static async Task<long> StatsCombat(HttpClient c) =>
        (await TestHost.Data(await c.GetAsync("/api/character"))).GetProperty("character").GetProperty("CombatScore").GetProperty("Value").GetInt64();

    [Fact]
    public async Task TransientDip_KeepsPreviousValue_AndNeverShowsTheLowOne()
    {
        using var s = new Scenario(100000, "00:00:00.400");
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(100000, await Combat(c));

        s.SetCombat(97000);                              // 로딩 순간: 3,000 낮게 읽힘
        Assert.Equal(100000, await Combat(c));           // 바로는 믿지 않고 이전 값을 보여 준다
        Assert.Equal(100000, await StatsCombat(c));      // 스탯 화면(원본 캐릭터 정보)도 같다
        s.SetCombat(100000);                             // 다시 읽을 때는 정상

        await Task.Delay(1200);
        Assert.Equal(100000, await Combat(c));
    }

    [Fact]
    public async Task RealDrop_IsAcceptedAfterTheRecheckStaysLow()
    {
        using var s = new Scenario(100000, "00:00:00.100");
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(100000, await Combat(c));

        s.SetCombat(96000);                              // 장비를 바꿔 실제로 낮아짐
        Assert.Equal(100000, await Combat(c));           // 처음에는 이전 값
        var deadline = DateTime.UtcNow.AddSeconds(8);
        long now;
        do { await Task.Delay(150); now = await Combat(c); } while (now != 96000 && DateTime.UtcNow < deadline);
        Assert.Equal(96000, now);                        // 다시 읽어도 같아 실제 값으로 받아들임
    }

    [Fact]
    public async Task HigherValue_IsAcceptedImmediately()
    {
        using var s = new Scenario(100000, "00:00:00.100");
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(100000, await Combat(c));
        s.SetCombat(105000);
        Assert.Equal(105000, await Combat(c));
    }
}
