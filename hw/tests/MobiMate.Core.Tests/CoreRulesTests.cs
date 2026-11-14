namespace MobiMate.Tests;

public class JsonFileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-store-" + Guid.NewGuid().ToString("N"));

    public JsonFileStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void FirstSave_UsesMove_ThenReplaceKeepsOneBackup()
    {
        var store = new JsonFileStore();
        var path = Path.Combine(_dir, "a.json");

        Assert.True(store.Save(path, new List<int> { 1 }));
        Assert.False(File.Exists(path + ".bak"));

        Assert.True(store.Save(path, new List<int> { 1, 2 }));
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(new List<int> { 1, 2 }, store.Load<List<int>>(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp.*"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ 깨진 json")]
    public void CorruptedFile_IsQuarantined_AndLoadReturnsNull(string content)
    {
        var store = new JsonFileStore();
        var path = Path.Combine(_dir, "b.json");
        File.WriteAllText(path, content);

        Assert.Null(store.Load<List<int>>(path));
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_dir, "b.json.corrupted.*.bak"));
    }

    [Fact]
    public void ConcurrentSaves_NeverLeaveBrokenJson()
    {
        var store = new JsonFileStore();
        var path = Path.Combine(_dir, "c.json");
        Parallel.For(0, 200, i => store.Save(path, Enumerable.Range(0, i % 20).ToList()));
        Assert.NotNull(store.Load<List<int>>(path));
    }
}

public class GameStateCacheTests
{
    [Fact]
    public void Summaries_UseLatestData_AndDescribeActivity()
    {
        var now = DateTimeOffset.Parse("2026-09-23T12:00:00+09:00");
        var cache = new GameStateCache(() => now);
        Assert.True(cache.IsHeaderStale);

        var ch = new CharacterInfo("칭호", "아이라", 100, "격투가", new ScoreVal("전투력", 88737), null, null, null, null, null, null, null,
            null, null, null, null, null, null, new VitalsInfo(1, 1, 1040, 1030, 0, 100, 0));
        var act = new ActivityInfo(false, false, false, true, false, false, false, false, true, "Gathering", true);
        cache.UpdateHeader(ch, act, new EnvironmentInfo("던바튼 광장", "Sunny", "2959-4-23 15:51"));
        cache.UpdateCurrencies(new List<CurrencyItem> { new("골드", 6_000_000) });

        Assert.False(cache.IsHeaderStale);
        Assert.Equal("채집 중", GameStateCache.DescribeActivity(cache.Activity));
        Assert.Equal("던바튼 광장", cache.LocationSummary());
        Assert.Contains("101.0%", GameStateCache.WeightSummary(cache.Character?.Vitals));
        Assert.Equal(6_000_000, cache.Currencies!.Single(c => c.DisplayName == "골드").Amount);

        now = now.AddSeconds(31);
        Assert.True(cache.IsHeaderStale);
    }

    [Fact]
    public void GameStateCache_ClearPerCharacter_DropsCurrenciesAndMissions()
    {
        var s = new GameStateCache();
        s.UpdateCurrencies(new[] { new CurrencyItem("골드", 1) });
        s.UpdateDailyMissions(new[] { new MissionItem("m", "", 1, 1, true, true) });
        s.ClearPerCharacter();
        Assert.Null(s.Currencies);
        Assert.Null(s.DailyMissions);
    }
}
