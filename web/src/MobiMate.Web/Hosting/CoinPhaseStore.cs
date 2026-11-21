using System.Text.Json;

namespace MobiMate.Web.Hosting;

public enum CoinKind { Silver, Tribute }

public sealed class CoinPhaseEntry
{
    public List<int[]> Intervals { get; set; } = new();
    public long? LastHeld { get; set; }
    public DateTime? LastObsUtc { get; set; }
}

/// <summary>
/// 캐릭터별 은동전·마족 공물 +1 위상 저장소 (coin_phase.json, 웹앱 전용). 읽을 때마다 <see cref="CoinPhase"/>를 좁히고,
/// 처음에는 과거 재화 변화 기록(currency_changes.jsonl)의 +1 감지 시각으로 위상을 잡는다.
/// </summary>
public sealed class CoinPhaseStore
{
    public const string FileName = "coin_phase.json";
    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(2);

    private readonly object _lock = new();
    private readonly string _path, _changesLog;
    private readonly JsonFileStore _store = new();
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, CoinPhase> _phases = new(StringComparer.Ordinal);
    private DateTime _lastSave = DateTime.MinValue;
    private bool _dirty;

    public CoinPhaseStore(string storageDir, Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        _path = Path.Combine(storageDir, FileName);
        _changesLog = Path.Combine(storageDir, "currency_changes.jsonl");
        var loaded = _store.TryLoad<Dictionary<string, CoinPhaseEntry>>(_path, out var data) == LoadStatus.Loaded ? data : null;
        if (loaded != null)
            foreach (var (k, e) in loaded)
            {
                var kind = k.EndsWith("|tribute", StringComparison.Ordinal) ? CoinKind.Tribute : CoinKind.Silver;
                var c = Make(kind);
                c.LoadIntervals(e.Intervals, e.LastHeld, e.LastObsUtc);
                _phases[k] = c;
            }
        else BootstrapFromLog();
    }

    private static CoinPhase Make(CoinKind kind) => kind == CoinKind.Silver ? CoinPhase.Silver() : CoinPhase.Tribute();
    private static string KeyOf(string character, CoinKind kind) => character + (kind == CoinKind.Silver ? "|silver" : "|tribute");

    /// <summary>방금 읽은 은동전·마족 공물 보유량을 그 캐릭터의 관찰로 알린다. null이면 그 재화는 읽지 못한 것이다.</summary>
    public void Observe(string character, DateTime nowUtc, long? silver, long? tribute)
    {
        if (string.IsNullOrEmpty(character)) return;
        lock (_lock)
        {
            if (silver is { } s) ObserveOne(character, CoinKind.Silver, nowUtc, s);
            if (tribute is { } t) ObserveOne(character, CoinKind.Tribute, nowUtc, t);
            if (_dirty && _now() - _lastSave >= SaveEvery) SaveLocked();
        }
    }

    private void ObserveOne(string character, CoinKind kind, DateTime nowUtc, long held)
    {
        var k = KeyOf(character, kind);
        if (!_phases.TryGetValue(k, out var c)) _phases[k] = c = Make(kind);
        var before = c.Estimate();
        c.Observe(nowUtc, held);
        var after = c.Estimate();
        if (before?.Width != after?.Width || (before?.Phase ?? -1) != (after?.Phase ?? -1)) _dirty = true;   // 위상이 좁혀졌거나 바뀌었을 때만 저장 대상
    }

    /// <summary>알려진 위상(주기 안의 초)과 폭(초). 모르면 null.</summary>
    public (double Phase, int Width)? Estimate(string character, CoinKind kind)
    {
        lock (_lock) return _phases.TryGetValue(KeyOf(character, kind), out var c) ? c.Estimate() : null;
    }

    public double? PhaseOf(string character, CoinKind kind) => Estimate(character, kind)?.Phase;

    public void Flush() { lock (_lock) if (_dirty) SaveLocked(); }

    private void SaveLocked()
    {
        var data = _phases.ToDictionary(kv => kv.Key, kv => new CoinPhaseEntry { Intervals = kv.Value.ToIntervals(), LastHeld = kv.Value.LastHeld, LastObsUtc = kv.Value.LastObsUtc });
        _store.Save(_path, data);
        _lastSave = _now();
        _dirty = false;
    }

    /// <summary>처음 한 번: 과거 변화 기록의 "+1" 감지 시각으로 위상을 잡는다 (기록 시각은 이 PC의 지역 시각).</summary>
    private void BootstrapFromLog()
    {
        try
        {
            if (!File.Exists(_changesLog)) return;
            var ticks = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(_changesLog))
            {
                if (line.Length < 20 || !line.Contains("\"diff\":1}", StringComparison.Ordinal)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!DateTime.TryParse(root.GetProperty("at").GetString(), out var local)) continue;
                    var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local);
                    var who = root.GetProperty("character").GetString() ?? "";
                    foreach (var ch in root.GetProperty("changes").EnumerateArray())
                    {
                        if (ch.GetProperty("diff").GetInt64() != 1) continue;
                        var name = ch.GetProperty("name").GetString();
                        var kind = name == "은동전" ? CoinKind.Silver : name is "마족 공물" or "마족공물" ? CoinKind.Tribute : (CoinKind?)null;
                        if (kind is not { } kd) continue;
                        var k = KeyOf(who, kd);
                        if (!ticks.TryGetValue(k, out var list)) ticks[k] = list = new();
                        list.Add(utc);
                    }
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException) { }
            }
            foreach (var (k, list) in ticks)
            {
                var c = Make(k.EndsWith("|tribute", StringComparison.Ordinal) ? CoinKind.Tribute : CoinKind.Silver);
                // 최근 기록을 쓴다: 위상은 상한에서 소비하면 바뀔 수 있어 오래된 것은 믿지 않는다
                if (c.Bootstrap(list.OrderByDescending(x => x).Take(80).ToList())) _phases[k] = c;
            }
            if (_phases.Count > 0) SaveLocked();
        }
        catch (IOException) { }
    }
}
