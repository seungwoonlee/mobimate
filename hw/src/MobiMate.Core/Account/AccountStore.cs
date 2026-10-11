namespace MobiMate;

/// <summary>
/// accounts.json 저장소 (v1.5): 캐릭터-계정 묶음과 계정별 멤버십. 모든 기기가 같이 본다.
/// 공유 저장 패치: 같은 폴더를 다른 프로세스(숙제 앱)도 쓰므로 수정은 "서비스 잠금 → 공유 잠금 → 파일이 바뀌었으면 다시 읽기 → 변경 → 저장"으로 한다.
/// 읽지 못하면(잠김·교체 중) 메모리 값을 지키고 이번 수정을 미룬다(false).
/// </summary>
public sealed class AccountStore
{
    public const string FileName = "accounts.json";
    private readonly JsonFileStore _store = new();
    private readonly string _path;
    private readonly object _lock = new();
    private readonly SharedStorageLock _shared;
    private readonly SharedFileTracker _tracker;
    private AccountData _data = new();
    private bool _loaded;

    public AccountStore(string storageDir)
    {
        _path = Path.Combine(storageDir, FileName);
        _shared = SharedStorageLock.For(storageDir);
        _tracker = new SharedFileTracker(_path);
        lock (_lock) _shared.Run(() => RefreshLocked(out _));
    }

    /// <summary>공유 잠금과 _lock 안에서 부른다. 쓸 수 있는 최신 상태면 true.</summary>
    private bool RefreshLocked(out bool reloaded)
    {
        reloaded = false;
        switch (_tracker.Refresh<AccountData>(_loaded, out var loaded))
        {
            case RefreshResult.Unavailable:
                return false;
            case RefreshResult.Reloaded:
                _data = Normalize(loaded);
                _loaded = true;
                reloaded = true;
                break;
        }
        return _loaded;
    }

    /// <summary>
    /// 데이터를 바꾸고 바뀌었으면 저장한다. <paramref name="change"/>가 true를 돌려주면 바뀐 것이다.
    /// 저장하지 못하거나 최신 파일을 읽지 못하면 false이고 메모리 값은 그대로다.
    /// </summary>
    public bool Update(Func<AccountData, bool> change)
    {
        lock (_lock)
        {
            return _shared.Run(() =>
            {
                if (!RefreshLocked(out _)) return false;
                var next = Clone(_data);
                if (!change(next)) return true;
                if (!_store.Save(_path, next)) return false;
                _tracker.MarkWritten();
                _data = next;
                return true;
            });
        }
    }

    /// <summary>읽기용 복사본. 파일이 바뀌었으면 먼저 다시 읽는다.</summary>
    public AccountData Snapshot()
    {
        if (_loaded && !_tracker.IsStale)
        {
            lock (_lock) return Clone(_data);
        }
        lock (_lock)
        {
            try { _shared.Run(() => RefreshLocked(out _)); }
            catch (IOException) { /* 잠금을 못 얻으면 메모리 값으로 */ }
            return Clone(_data);
        }
    }

    /// <summary>다른 프로세스가 파일을 바꿔서 다시 읽었으면 true (변경 감지기가 부른다). 자기 쓰기는 바뀐 것으로 치지 않는다.</summary>
    public bool RefreshExternal()
    {
        if (_loaded && !_tracker.IsStale) return false;
        lock (_lock)
        {
            return _shared.Run(() =>
            {
                RefreshLocked(out var reloaded);
                return reloaded;
            });
        }
    }

    private static AccountData Normalize(AccountData? d)
    {
        d ??= new AccountData();
        d.Accounts ??= new();
        d.Assign ??= new();
        d.Order ??= new();
        return d;
    }

    private static AccountData Clone(AccountData d) => new()
    {
        Accounts = d.Accounts.ToDictionary(x => x.Key, x => new AccountInfo { MembershipExpiresAtUtc = x.Value.MembershipExpiresAtUtc }),
        Assign = d.Assign.ToDictionary(x => x.Key, x => new AccountAssignment { Account = x.Value.Account, Manual = x.Value.Manual }),
        Order = [.. d.Order],
    };
}
