namespace MobiMateHomeWork.Tests;

/// <summary>가방 무게 튐 방지 (H6 1차 피드백 1): 맵 이동 때 비정상 값이 화면·설정에 남지 않는다.</summary>
public class WeightGuardTests
{
    [Theory]
    [InlineData(500, 1000, 1000, WeightVerdict.Ok)]
    [InlineData(1030, 0, 1030, WeightVerdict.Ok)]                       // 최대를 못 읽음: 저장된 최대 기준으로 정상
    [InlineData(500, 0, 0, WeightVerdict.Ok)]                        // 저장된 최대도 없음: 비교 근거가 없으니 정상
    [InlineData(double.NaN, 1000, 1000, WeightVerdict.NotFinite)]
    [InlineData(500, double.PositiveInfinity, 1000, WeightVerdict.NotFinite)]
    [InlineData(-1, 1000, 1000, WeightVerdict.Negative)]
    [InlineData(500, -5, 1000, WeightVerdict.Negative)]
    [InlineData(100_001, 0, 0, WeightVerdict.TooLarge)]
    [InlineData(3001, 1000, 1000, WeightVerdict.CurrentOverMax)]
    [InlineData(3000, 1000, 1000, WeightVerdict.Ok)]                    // 경계: 3배는 정상
    [InlineData(500, 3001, 1000, WeightVerdict.MaxOverSaved)]
    [InlineData(3001, 0, 1000, WeightVerdict.CurrentOverSavedMax)]
    public void Evaluate(double current, double max, double saved, WeightVerdict expected) =>
        Assert.Equal(expected, WeightReading.Evaluate(current, max, saved > 0 ? saved : null));

    private static VitalsInfo V(double cur, double max) => new(100, 100, cur, max, 0, 0, 0);

    [Fact]
    public void Abnormal_ShowsLastGood_AndLogsOncePerMinutePerCharacter()
    {
        var clock = DateTimeOffset.UtcNow;
        var logs = new MemoryLoggerProvider();
        var guard = new WeightGuard(new LoggerFactory([logs]).CreateLogger<WeightGuard>(), () => clock);

        var good = guard.Resolve("A", "콜헨", V(500, 1000), 1000);
        Assert.False(good.Abnormal);
        Assert.Equal(500, good.View!.Current);

        var bad = guard.Resolve("A", "로딩", V(9_999_999, 9_999_999), 1000);
        Assert.True(bad.Abnormal);
        Assert.Equal(500, bad.View!.Current);                           // 마지막 정상 값
        guard.Resolve("A", "로딩", V(8_888_888, 1), 1000);
        Assert.Single(logs.Entries, e => e.Level == LogLevel.Information);   // 1분 안에는 한 번만

        guard.Resolve("B", "x", V(-5, 1000), 1000);                     // 다른 캐릭터는 따로 센다
        Assert.Equal(2, logs.Entries.Count(e => e.Level == LogLevel.Information));
        Assert.Null(guard.Resolve("B", "x", V(-5, 1000), 1000).View);   // 정상 값을 본 적 없으면 null

        clock += TimeSpan.FromSeconds(61);
        guard.Resolve("A", "로딩", V(9_999_999, 9_999_999), 1000);
        Assert.Equal(3, logs.Entries.Count(e => e.Level == LogLevel.Information));
        Assert.Contains("9999999", logs.Entries.Last().Message);        // 원래 값이 남는다
    }

    [Fact]
    public async Task AbnormalReading_IsNotShown_AndItsMaxIsNotRemembered()
    {
        await using var h = await AppHarness.StartAsync();
        const string Aira = FakeGame.SampleKey;
        h.Game.SetVitals(500, 1000);
        await h.RecordAsync();
        Assert.Equal(1000, h.Settings.CharacterInfo(Aira)!.WeightMax);

        h.Game.SetVitals(7_000_000, 7_000_000);   // 맵 이동 순간의 튀는 값
        await h.RecordAsync();
        var shown = h.Session.Characters().Card(Aira).Weight!;
        Assert.Equal(500, shown.Current);
        Assert.Equal(1000, shown.Max);
        Assert.Equal(500, h.Log.Headers[^1].Weight!.Current);
        Assert.Equal(1000, h.Settings.CharacterInfo(Aira)!.WeightMax);   // 비정상 최대를 저장하지 않았다

        h.Game.SetVitals(600, 1000);              // 돌아오면 바로 정상 값
        await h.RecordAsync();
        Assert.Equal(600, h.Session.Characters().Card(Aira).Weight!.Current);
    }
}
