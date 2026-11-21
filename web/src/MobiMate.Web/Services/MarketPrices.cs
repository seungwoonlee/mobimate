using System.Text.Json.Serialization;
using MobiMate.Web.Hosting;

namespace MobiMate.Web.Services;

public sealed record MarketPricesResponse([property: JsonPropertyName("Results")] List<MarketQueryResult>? Results);
public sealed record MarketQueryResult(
    [property: JsonPropertyName("Query")] string? Query,
    [property: JsonPropertyName("Matched")] bool Matched,
    [property: JsonPropertyName("Rows")] List<MarketRow>? Rows);
public sealed record MarketRow(
    [property: JsonPropertyName("DisplayName")] string? DisplayName,
    [property: JsonPropertyName("InStock")] bool InStock,
    [property: JsonPropertyName("MinUnitPrice")] long? MinUnitPrice);

/// <summary>한 아이템의 거래소 최저 단가(데카). Tradable=false면 거래소에 없는 아이템, Price=null이면 지금 매물이 없다.</summary>
public sealed class MarketPrice
{
    public long? Price { get; set; }
    public bool Tradable { get; set; }
    public DateTime AtUtc { get; set; }
}

/// <summary>
/// 거래소 가격 (market_prices.json, 웹앱 전용). 거래소 조회는 CLI 큐를 차지해 다른 조회를 느리게 하므로(2026-11-22 승운),
/// 같은 아이템은 1시간에 한 번만 다시 읽는다. 화면은 저장된 값을 바로 받고, 오래됐거나 처음 보는 이름이 있으면 뒤에서 조금씩 나눠 읽는다.
/// </summary>
public sealed class MarketPriceService
{
    public const string FileName = "market_prices.json";
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(1);
    /// <summary>처음 보는 이름만 읽는 짧은 주기(새 재료가 생겼을 때 1시간을 기다리지 않게)</summary>
    public static readonly TimeSpan NewNamesEvery = TimeSpan.FromMinutes(5);
    private const int Batch = 30;
    private static readonly TimeSpan BetweenBatches = TimeSpan.FromSeconds(2);   // 사이사이 다른 조회가 큐에 들어올 수 있게

    private readonly object _lock = new();
    private readonly JsonFileStore _store = new();
    private readonly string _path;
    private readonly IGameCli _cli;
    private readonly ILogger<MarketPriceService> _log;
    private readonly Func<DateTime> _now;
    private Dictionary<string, MarketPrice> _prices;
    private DateTime _lastFullRun = DateTime.MinValue, _lastNewRun = DateTime.MinValue;
    private Task? _running;

    public MarketPriceService(string storageDir, IGameCli cli, ILogger<MarketPriceService> log, Func<DateTime>? now = null)
    {
        _path = Path.Combine(storageDir, FileName);
        _cli = cli; _log = log;
        _now = now ?? (() => DateTime.UtcNow);
        _prices = _store.Load<Dictionary<string, MarketPrice>>(_path) ?? new();
        if (_prices.Count > 0) _lastFullRun = _prices.Values.Max(p => p.AtUtc);   // 재시작해도 1시간 주기를 지킨다
    }

    public IReadOnlyDictionary<string, MarketPrice> Snapshot() { lock (_lock) return new Dictionary<string, MarketPrice>(_prices); }

    /// <summary>
    /// 이 이름들의 가격이 필요하다고 알린다. 1시간이 지났으면 오래된 것 전부를, 아니면 5분에 한 번 처음 보는 이름만 뒤에서 읽는다.
    /// 이미 읽는 중이면 아무것도 하지 않는다. 기다리지 않는다.
    /// </summary>
    public void Want(IEnumerable<string> names)
    {
        if (!_cli.IsAvailable) return;
        List<string> todo;
        lock (_lock)
        {
            if (_running is { IsCompleted: false }) return;
            var now = _now();
            var all = names.Where(n => n.Length >= 2).Distinct(StringComparer.Ordinal).ToList();
            if (now - _lastFullRun >= RefreshEvery)
            {
                todo = all.Where(n => !_prices.TryGetValue(n, out var p) || now - p.AtUtc >= RefreshEvery).ToList();
                if (todo.Count == 0) return;
                _lastFullRun = now; _lastNewRun = now;
            }
            else if (now - _lastNewRun >= NewNamesEvery)
            {
                todo = all.Where(n => !_prices.ContainsKey(n)).ToList();
                if (todo.Count == 0) return;
                _lastNewRun = now;
            }
            else return;
            _running = Task.Run(() => FetchAsync(todo));
        }
    }

    private async Task FetchAsync(List<string> names)
    {
        try
        {
            for (var i = 0; i < names.Count; i += Batch)
            {
                if (i > 0) await Task.Delay(BetweenBatches);
                var chunk = names.Skip(i).Take(Batch).ToList();
                var body = new { names = chunk.Select(n => $"\"{n}\"").ToArray() };   // 따옴표 = 정확히 같은 이름만
                var (ok, data, error) = await _cli.RunJsonAsync<MarketPricesResponse>("get_market_prices", body, TimeSpan.FromSeconds(10));
                if (!ok || data?.Results == null)
                {
                    _log.LogInformation("거래소 가격 조회 실패: {Error}", error);
                    return;   // 연결이 끊겼거나 거래소가 잠겼다: 다음 주기에 다시
                }
                var now = _now();
                lock (_lock)
                {
                    for (var k = 0; k < chunk.Count && k < data.Results.Count; k++)
                    {
                        var r = data.Results[k];
                        var rows = (r.Rows ?? new()).Where(x => x.DisplayName == chunk[k] || r.Rows!.Count == 1).ToList();
                        var price = rows.Where(x => x.InStock && x.MinUnitPrice is > 0).Select(x => x.MinUnitPrice).Min();
                        _prices[chunk[k]] = new MarketPrice { Tradable = r.Matched && rows.Count > 0, Price = price, AtUtc = now };
                    }
                    _store.Save(_path, _prices);
                }
            }
        }
        catch (Exception ex) { _log.LogDebug(ex, "거래소 가격 조회 중 오류"); }
    }
}
