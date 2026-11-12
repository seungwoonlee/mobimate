namespace MobiMate.Web.Hosting;

/// <summary>한 계정이 마지막으로 읽은 공통 재화 (이름 → 보유량)</summary>
public sealed class WalletEntry
{
    public DateTime AtUtc { get; set; }
    public string By { get; set; } = "";
    public Dictionary<string, long> Items { get; set; } = new();
}

public sealed class WalletData
{
    public Dictionary<string, WalletEntry> Accounts { get; set; } = new();
}

/// <summary>
/// 계정별 공통 재화(<see cref="SharedCurrencies"/>)의 최신 값. 그 계정의 캐릭터 누구든 접속해서 재화를 읽으면 그 값이 계정의 최신 값이다.
/// 웹앱 전용 파일(account_wallet.json)이다: 숙제 앱과 공유하는 accounts.json의 형식을 바꾸지 않으려고 따로 둔다.
/// 값이 바뀌었을 때만 저장한다.
/// </summary>
public sealed class AccountWalletStore
{
    public const string FileName = "account_wallet.json";
    private readonly object _lock = new();
    private readonly string _path;
    private readonly JsonFileStore _store = new();
    private readonly Func<DateTime> _now;
    private WalletData _data = new();

    public AccountWalletStore(string storageDir, Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        _path = Path.Combine(storageDir, FileName);
        if (_store.TryLoad<WalletData>(_path, out var loaded) == LoadStatus.Loaded && loaded != null) _data = loaded;
    }

    /// <summary>방금 읽은 재화를 그 계정의 최신 값으로 기록한다. 계정 공통 재화가 하나도 없으면 아무것도 하지 않는다.</summary>
    public void Observe(string accountId, IEnumerable<CurrencyItem>? currencies, string by)
    {
        var read = SharedCurrencies.Pick(currencies);
        if (read.Count == 0 || string.IsNullOrEmpty(accountId)) return;
        lock (_lock)
        {
            _data.Accounts.TryGetValue(accountId, out var cur);
            var merged = SharedCurrencies.Merge(cur?.Items ?? new Dictionary<string, long>(), read);
            var same = cur != null && cur.Items.Count == merged.Count && merged.All(kv => cur.Items.TryGetValue(kv.Key, out var v) && v == kv.Value);
            if (same)
            {
                // 값이 같아도 "방금 확인했다"는 사실은 남긴다(저장은 1분에 한 번만: 읽을 때마다 파일을 쓰지 않는다)
                if (cur != null && (_now() - cur.AtUtc) < TimeSpan.FromMinutes(1) && cur.By == by) return;
            }
            _data.Accounts[accountId] = new WalletEntry { AtUtc = _now(), By = by, Items = merged };
            _store.Save(_path, _data);
        }
    }

    public WalletEntry? Get(string accountId)
    {
        lock (_lock) return _data.Accounts.TryGetValue(accountId, out var e) ? new WalletEntry { AtUtc = e.AtUtc, By = e.By, Items = new(e.Items) } : null;
    }
}
