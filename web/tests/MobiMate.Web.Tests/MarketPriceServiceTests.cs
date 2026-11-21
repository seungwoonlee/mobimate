using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MobiMate;
using MobiMate.Web.Services;
using Xunit;

namespace MobiMate.Web.Tests;

public class MarketPriceServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-mkt-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    public MarketPriceServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>요청한 이름마다 이름 길이 × 10 데카로 답하고, '철 광석'은 거래소에 없다고 답한다</summary>
    private sealed class FakeMarket : IGameCli
    {
        public int Calls;
        public List<string> Asked = new();
        public bool IsAvailable => true;
        public string CliPath => "fake";
        public Task<CliResult> RunAsync(CliCommand cmd, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            var names = JsonSerializer.SerializeToElement(cmd.Body).GetProperty("names").EnumerateArray().Select(e => e.GetString()!.Trim('"')).ToList();
            lock (Asked) Asked.AddRange(names);
            var results = names.Select(n => n == "철 광석"
                ? new { Query = n, Matched = false, Rows = Array.Empty<object>() }
                : new { Query = n, Matched = true, Rows = new object[] { new { DisplayName = n, InStock = true, MinUnitPrice = (long)n.Length * 10 } } });
            return Task.FromResult(new CliResult(true, JsonSerializer.Serialize(new { Results = results }), null, TimeSpan.Zero, TimeSpan.Zero));
        }
    }

    private MarketPriceService Make(FakeMarket cli) => new(_dir, cli, NullLogger<MarketPriceService>.Instance, () => _now);

    private static async Task Settle(MarketPriceService s, int expected)
    {
        for (var i = 0; i < 100 && s.Snapshot().Count < expected; i++) await Task.Delay(20);
    }

    [Fact]
    public async Task ReadsPrices_AndMarksUntradableItems()
    {
        var cli = new FakeMarket();
        var s = Make(cli);
        s.Want(["백금강괴", "철 광석"]);
        await Settle(s, 2);
        Assert.Equal(40, s.Snapshot()["백금강괴"].Price);
        Assert.False(s.Snapshot()["철 광석"].Tradable);
    }

    [Fact]
    public async Task SameNamesAreNotReadAgainWithinAnHour_AndAreAfterIt()
    {
        var cli = new FakeMarket();
        var s = Make(cli);
        s.Want(["백금강괴"]);
        await Settle(s, 1);
        _now += TimeSpan.FromMinutes(30);
        s.Want(["백금강괴"]);
        await Task.Delay(100);
        Assert.Equal(1, cli.Calls);
        _now += TimeSpan.FromMinutes(31);
        s.Want(["백금강괴"]);
        for (var i = 0; i < 100 && cli.Calls < 2; i++) await Task.Delay(20);
        Assert.Equal(2, cli.Calls);
    }

    [Fact]
    public async Task NewNamesAreReadAfterFiveMinutes_WithoutRereadingKnownOnes()
    {
        var cli = new FakeMarket();
        var s = Make(cli);
        s.Want(["백금강괴"]);
        await Settle(s, 1);
        _now += TimeSpan.FromMinutes(6);
        s.Want(["백금강괴", "운철괴"]);
        await Settle(s, 2);
        Assert.Equal(["백금강괴", "운철괴"], cli.Asked);
    }

    [Fact]
    public async Task PricesSurviveARestart_AndTheHourlyLimitStillHolds()
    {
        var cli = new FakeMarket();
        var s = Make(cli);
        s.Want(["백금강괴"]);
        await Settle(s, 1);
        var cli2 = new FakeMarket();
        var again = Make(cli2);
        Assert.Equal(40, again.Snapshot()["백금강괴"].Price);
        again.Want(["백금강괴"]);
        await Task.Delay(100);
        Assert.Equal(0, cli2.Calls);
    }
}
