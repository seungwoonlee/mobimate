using System.Text.Json;
namespace MobiMate.Tests;

public class CrossAppRegressionTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "mobimate-cross-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset now = new(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(9));
    public CrossAppRegressionTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }
    private HomeworkService Service() => new(catalog, new HomeworkStore(dir), () => now);

    [Fact]
    public void WeeklyResetClearsScarsButKeepsSeasonRecords()
    {
        var store = new HomeworkStore(dir);
        var old = KstClock.LastWeeklyReset(now);
        var file = new HomeworkFile();
        file.Characters["c"] = new HomeworkLedger { LastWeeklyResetUtc = old, LastDailyResetUtc = KstClock.LastDailyReset(now), Items = new() {
            ["raid_scars"] = new() { Completed = true }, ["weekly_nightmare_drill"] = new() { Count = 22 },
            ["weekly_nightmare_rank"] = new() { Count = 119, Completed = true }, ["weekly_memorial_saga"] = new() { Count = 8, Completed = true } } };
        Assert.True(store.Lock.Run(() => store.Save(file)));
        now = now.AddDays(2);
        Service().GetBoard("c");
        var items = store.Load().Characters["c"].Items;
        Assert.False(items.ContainsKey("raid_scars"));
        Assert.Equal(22, items["weekly_nightmare_drill"].Count);
        Assert.Equal(119, items["weekly_nightmare_rank"].Count);
        Assert.Equal(8, items["weekly_memorial_saga"].Count);
        var config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var exe = Path.Combine(Root(), "web", "tools", "SharedStorageHarness", "bin", config, "net8.0", "SharedStorageHarness.exe");
        using var process = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add(dir); process.StartInfo.ArgumentList.Add("verify-reset"); process.StartInfo.ArgumentList.Add(now.ToString("O"));
        Assert.True(process.Start());
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("웹 교차 검사 시간 초과"); }
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(22, store.Load().Characters["c"].Items["weekly_nightmare_drill"].Count);
    }

    [Fact]
    public void ResetAllKeepsSeasonRecords()
    {
        var store = new HomeworkStore(dir);
        var file = new HomeworkFile();
        file.Characters["c"] = new HomeworkLedger { Items = new() { ["weekly_nightmare_drill"] = new() { Count = 22 }, ["weekly_memorial_saga"] = new() { Count = 8 }, ["raid_scars"] = new() { Completed = true } } };
        Assert.True(store.Lock.Run(() => store.Save(file)));
        Service().ResetAll("c", false);
        var items = store.Load().Characters["c"].Items;
        Assert.Equal(22, items["weekly_nightmare_drill"].Count);
        Assert.Equal(8, items["weekly_memorial_saga"].Count);
        Assert.False(items.ContainsKey("raid_scars"));
    }

    [Fact]
    public void SnapshotRoundTripPreservesWebWeightBaselineFlag()
    {
        var snapshot = JsonSerializer.Deserialize<CharacterSnapshot>("{\"HasWeightBaseline\":false}")!;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
        Assert.False(json.RootElement.GetProperty("HasWeightBaseline").GetBoolean());
    }

    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !(Directory.Exists(Path.Combine(d.FullName, "web")) && Directory.Exists(Path.Combine(d.FullName, "hw")))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("저장소를 찾지 못했습니다.");
    }

    [Fact]
    public void CatalogsStayIdentical()
    {
        var root = Root();
        Assert.Equal(File.ReadAllText(Path.Combine(root, "web", "src", "MobiMate.Core", "Homework", "homework_catalog.json")),
            File.ReadAllText(Path.Combine(root, "hw", "src", "MobiMate.Core", "Homework", "homework_catalog.json")));
    }

    [Fact]
    public async Task WebAndHomeworkProcessesPreserveEachOthersWrites()
    {
        const int count = 90;
        var config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var root = Root();
        string Exe(string app) => Path.Combine(root, app, "tools", "SharedStorageHarness", "bin", config, "net8.0", "SharedStorageHarness.exe");
        async Task Run(string app, string role)
        {
            using var process = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo(Exe(app)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            process.StartInfo.ArgumentList.Add(dir); process.StartInfo.ArgumentList.Add(role); process.StartInfo.ArgumentList.Add(count.ToString());
            Assert.True(process.Start());
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw; }
            Assert.True(process.ExitCode == 0, $"{app}: {await output} {await error}");
        }
        File.WriteAllText(Path.Combine(dir, "character_snapshots.json"), "[{\"RealmName\":\"검증\",\"JobName\":\"기준\",\"HasWeightBaseline\":false}]");
        await Task.WhenAll(Run("web", "A"), Run("hw", "B"));
        using var snapshotJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "character_snapshots.json")));
        var baseline = snapshotJson.RootElement.EnumerateArray().Single(x => x.GetProperty("RealmName").GetString() == "검증");
        Assert.False(baseline.GetProperty("HasWeightBaseline").GetBoolean());
        var definitions = catalog.Items.Where(d => d.Active && d.Share == HomeworkShare.Character).ToList();
        var file = new HomeworkStore(dir).Load();
        foreach (var def in definitions)
            Assert.True(file.Characters["shared_char"].Items.GetValueOrDefault(def.Id)?.Completed == true, def.Id);
        var assignments = new AccountStore(dir).Snapshot().Assign;
        var profiles = new SnapshotManager(dir).GetAllProfiles().Select(x => x.CharacterKey).ToHashSet();
        foreach (var role in new[] { "A", "B" })
        {
            for (var i = 1; i < count; i += 3) Assert.Contains($"류트_{role}{i}", profiles);
            for (var i = 2; i < count; i += 3) Assert.Equal(role, assignments[$"acct_{role}_{i}"].Account);
            int mine = definitions.Where((_, i) => i % 2 == (role == "A" ? 0 : 1)).Count();
            for (var i = mine * 3; i < count; i += 3)
                Assert.True(file.Characters[$"hw_{role}_{i}"].Items[definitions[0].Id].Completed);
        }
    }

    [Fact]
    public void SeasonValuesAndScarsAppearAndSurviveWeeklyReset()
    {
        var svc = Service();
        svc.Set("c", "raid_scars", true);
        svc.Set("c", "weekly_nightmare_drill", count: 22);
        svc.Set("c", "weekly_nightmare_rank", count: 99999);
        svc.Set("c", "weekly_memorial_saga", count: 42);
        var current = svc.GetAutoStatuses(["c"])["c"].ToDictionary(x => x.Id);
        Assert.Equal("done", current["raid_scars"].State);
        Assert.Equal(22, current["weekly_nightmare_drill"].Value);
        Assert.Equal(99999, current["weekly_nightmare_rank"].Value);
        now = now.AddDays(2);
        var next = svc.GetAutoStatuses(["c"])["c"].ToDictionary(x => x.Id);
        Assert.Equal("unknown", next["raid_scars"].State);
        Assert.Equal(22, next["weekly_nightmare_drill"].Value);
        Assert.Equal(99999, next["weekly_nightmare_rank"].Value);
        Assert.Equal(42, next["weekly_memorial_saga"].Value);
    }

    [Fact]
    public void SeasonEditRejectsStaleValueAndKeepsOtherAppsChange()
    {
        var a = Service(); var b = Service();
        a.Set("c", "weekly_memorial_saga", count: 5);
        b.Set("c", "weekly_memorial_saga", count: 8);
        Assert.Throws<InvalidOperationException>(() => a.SetSeasonIfUnchanged("c", "weekly_memorial_saga", 6, 5));
        Assert.Equal(8, new HomeworkStore(dir).Load().Characters["c"].Items["weekly_memorial_saga"].Count);
        a.SetSeasonIfUnchanged("c", "weekly_memorial_saga", 9, 8);
        Assert.Equal(9, new HomeworkStore(dir).Load().Characters["c"].Items["weekly_memorial_saga"].Count);
    }
}
