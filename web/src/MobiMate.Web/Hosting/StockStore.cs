namespace MobiMate.Web.Hosting;

/// <summary>한 캐릭터를 마지막으로 읽었을 때의 보유량 (이름 → 개수). 가방·캐릭터 창고·(그 캐릭터가 본) 계정 창고를 따로 둔다.</summary>
public sealed class CharStock
{
    public DateTime AtUtc { get; set; }
    public Dictionary<string, int> Bag { get; set; } = new();
    public Dictionary<string, int> Storage { get; set; } = new();
    public Dictionary<string, int> Account { get; set; } = new();
}

public sealed class StockData
{
    public Dictionary<string, CharStock> Characters { get; set; } = new();
    /// <summary>아이템 이름 → 분류 이름 (게임이 주는 CategoryDisplayName)</summary>
    public Dictionary<string, string> Categories { get; set; } = new();
}

/// <summary>사용자가 정하는 값: 재료별 목표 개수</summary>
public sealed class StockPrefs
{
    public Dictionary<string, int> Targets { get; set; } = new();
}

public sealed record StockRow(
    string Name, string Category, int Total, int Bag, int Storage, int Account,
    IReadOnlyList<StockHolder> Holders, DateTime? OldestUtc);
public sealed record StockHolder(string Character, int Bag, int Storage, DateTime AtUtc);

/// <summary>
/// 계정 전체 보유량 저장소 (account_stock.json, 웹앱 전용). CLI는 지금 접속한 캐릭터의 가방·캐릭터 창고와 계정 창고만 알려 주므로,
/// 캐릭터를 읽을 때마다 그 몫을 저장해 두고 계정별로 합산한다. 캐릭터 → 계정 묶음은 합산할 때 정하므로 캐릭터를 합쳐도 따로 옮길 것이 없다.
/// 계정 창고는 같은 계정의 모든 캐릭터가 똑같이 보므로 가장 최근에 읽은 것을 쓴다(캐릭터마다 더하면 중복이다).
/// </summary>
public sealed class StockStore
{
    public const string FileName = "account_stock.json";
    public const string PrefsFileName = "stock_prefs.json";
    public const int MaxTargets = 500;
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(30);

    private readonly object _lock = new();
    private readonly JsonFileStore _store = new();
    private readonly string _path, _prefsPath;
    private readonly Func<DateTime> _now;
    private StockData _data;
    private StockPrefs _prefs;
    private DateTime _lastSave = DateTime.MinValue;
    private bool _dirty;

    public StockStore(string storageDir, Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        _path = Path.Combine(storageDir, FileName);
        _prefsPath = Path.Combine(storageDir, PrefsFileName);
        _data = _store.Load<StockData>(_path) ?? new StockData();
        _prefs = _store.Load<StockPrefs>(_prefsPath) ?? new StockPrefs();
    }

    private static string Loc(string? l) => (l ?? "").ToLowerInvariant().Replace("_", "");

    /// <summary>
    /// 방금 읽은 아이템 목록을 그 캐릭터의 보유량으로 둔다. 목록이 비어 왔으면 읽기 실패로 보고 무시한다(다 비어 있을 수는 없다).
    /// 값이 바뀌었을 때만 저장 대상으로 표시하고, 저장은 30초에 한 번만 한다.
    /// </summary>
    public void Observe(string characterKey, IReadOnlyCollection<ItemData>? items, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(characterKey) || items is not { Count: > 0 }) return;
        var cs = new CharStock { AtUtc = nowUtc };
        var cats = new Dictionary<string, string>();
        foreach (var i in items)
        {
            var name = HomeworkText.StripTags(i.DisplayName);
            if (name.Length == 0 || i.Count <= 0) continue;
            var target = Loc(i.Location) switch { "inventory" or "bag" => cs.Bag, "characterstorage" => cs.Storage, "accountstorage" => cs.Account, _ => null };
            if (target == null) continue;
            target[name] = target.GetValueOrDefault(name) + i.Count;
            if (!string.IsNullOrWhiteSpace(i.CategoryName)) cats[name] = i.CategoryName;
        }
        lock (_lock)
        {
            var prev = _data.Characters.GetValueOrDefault(characterKey);
            var same = prev != null && Same(prev.Bag, cs.Bag) && Same(prev.Storage, cs.Storage) && Same(prev.Account, cs.Account);
            if (same) { prev!.AtUtc = nowUtc; }
            else { _data.Characters[characterKey] = cs; _dirty = true; }
            foreach (var (n, c) in cats) if (!_data.Categories.TryGetValue(n, out var old) || old != c) { _data.Categories[n] = c; _dirty = true; }
            if (_dirty && _now() - _lastSave >= SaveEvery) SaveLocked();
        }
    }

    private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public IReadOnlyList<string> Keys() { lock (_lock) return _data.Characters.Keys.ToList(); }

    public void Flush() { lock (_lock) if (_dirty) SaveLocked(); }

    private void SaveLocked()
    {
        if (!_store.Save(_path, _data)) return;
        _lastSave = _now(); _dirty = false;
    }

    /// <summary>
    /// 한 계정의 합산. <paramref name="characterKeys"/>는 그 계정에 속한 캐릭터 키들이다.
    /// 합계 = 각 캐릭터 가방 + 각 캐릭터 창고 + 계정 창고(가장 최근에 읽은 캐릭터 것 하나).
    /// </summary>
    public IReadOnlyList<StockRow> Aggregate(IEnumerable<string> characterKeys)
    {
        lock (_lock)
        {
            var chars = characterKeys.Select(k => (Key: k, S: _data.Characters.GetValueOrDefault(k))).Where(x => x.S != null).ToList();
            var accountSrc = chars.OrderByDescending(x => x.S!.AtUtc).FirstOrDefault(x => x.S!.Account.Count > 0).S;
            var names = chars.SelectMany(x => x.S!.Bag.Keys.Concat(x.S.Storage.Keys)).Concat(accountSrc?.Account.Keys ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            var rows = new List<StockRow>();
            foreach (var n in names)
            {
                var holders = new List<StockHolder>();
                int bag = 0, sto = 0;
                foreach (var (key, s) in chars)
                {
                    var b = s!.Bag.GetValueOrDefault(n); var st = s.Storage.GetValueOrDefault(n);
                    if (b + st == 0) continue;
                    bag += b; sto += st;
                    holders.Add(new StockHolder(key, b, st, s.AtUtc));
                }
                var acc = accountSrc?.Account.GetValueOrDefault(n) ?? 0;
                rows.Add(new StockRow(n, _data.Categories.GetValueOrDefault(n, ""), bag + sto + acc, bag, sto, acc,
                    holders.OrderByDescending(h => h.Bag + h.Storage).ToList(), chars.Count == 0 ? null : chars.Min(x => x.S!.AtUtc)));
            }
            return rows.OrderByDescending(r => r.Total).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>계정에 속한 캐릭터별로 마지막으로 읽은 시각 (오래된 값은 화면이 알린다)</summary>
    public IReadOnlyDictionary<string, DateTime> ReadTimes(IEnumerable<string> characterKeys)
    {
        lock (_lock) return characterKeys.Where(_data.Characters.ContainsKey).ToDictionary(k => k, k => _data.Characters[k].AtUtc);
    }

    public StockPrefs Prefs() { lock (_lock) return new StockPrefs { Targets = new(_prefs.Targets) }; }

    /// <summary>목표 개수를 정한다(0 이하면 지운다). 저장 실패하면 false.</summary>
    public bool SetTarget(string name, int target)
    {
        lock (_lock)
        {
            var next = new StockPrefs { Targets = new(_prefs.Targets) };
            if (target <= 0) next.Targets.Remove(name);
            else { if (!next.Targets.ContainsKey(name) && next.Targets.Count >= MaxTargets) return false; next.Targets[name] = Math.Min(target, 99_999_999); }
            if (!_store.Save(_prefsPath, next)) return false;
            _prefs = next; return true;
        }
    }
}
