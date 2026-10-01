namespace MobiMate.Web.Hosting;

/// <summary>accounts.json 저장소 (v1.5): 캐릭터-계정 묶음과 계정별 멤버십. 모든 기기가 같이 본다.</summary>
public sealed class AccountStore
{
    public const string FileName = "accounts.json";
    private readonly JsonFileStore _store = new();
    private readonly string _path;
    private readonly object _lock = new();
    private AccountData _data;

    public AccountStore(string storageDir)
    {
        _path = Path.Combine(storageDir, FileName);
        _data = _store.Load<AccountData>(_path) ?? new AccountData();
    }

    /// <summary>
    /// 데이터를 바꾸고 바뀌었으면 저장한다. <paramref name="change"/>가 true를 돌려주면 바뀐 것이다.
    /// 저장에 실패하면 false이고 메모리 값도 되돌린다.
    /// </summary>
    public bool Update(Func<AccountData, bool> change)
    {
        lock (_lock)
        {
            var next = Clone(_data);
            if (!change(next)) return true;
            if (!_store.Save(_path, next)) return false;
            _data = next;
            return true;
        }
    }

    /// <summary>읽기용 복사본</summary>
    public AccountData Snapshot()
    {
        lock (_lock) return Clone(_data);
    }

    private static AccountData Clone(AccountData d) => new()
    {
        Accounts = d.Accounts.ToDictionary(x => x.Key, x => new AccountInfo { MembershipExpiresAtUtc = x.Value.MembershipExpiresAtUtc }),
        Assign = d.Assign.ToDictionary(x => x.Key, x => new AccountAssignment { Account = x.Value.Account, Manual = x.Value.Manual }),
    };
}
