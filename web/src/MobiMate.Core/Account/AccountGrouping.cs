namespace MobiMate;

/// <summary>accounts.json: 캐릭터를 계정으로 묶은 결과와 계정별 멤버십. 게임이 계정 번호를 주지 않아 데카·M캐시로 같은 계정을 알아낸다.</summary>
public sealed class AccountData
{
    public Dictionary<string, AccountInfo> Accounts { get; set; } = new();
    /// <summary>캐릭터 키 → 소속 계정. 한 번 묶이면 이후 값이 어긋나도 그대로 둔다(동기화가 늦은 것이지 잘못 묶인 게 아니다).</summary>
    public Dictionary<string, AccountAssignment> Assign { get; set; } = new();
}

public sealed class AccountInfo
{
    /// <summary>멤버십 만료 시각(UTC). 없으면 미가입으로 본다. 사용자가 게임 캐시샵의 남은 기간을 입력해 등록한다.</summary>
    public DateTime? MembershipExpiresAtUtc { get; set; }
}

public sealed class AccountAssignment
{
    public string Account { get; set; } = "";
    /// <summary>사용자가 직접 정한 소속: 자동 묶기가 옮기지 않는다.</summary>
    public bool Manual { get; set; }
}

/// <summary>한 캐릭터가 지금까지 가졌던 (데카, M캐시) 값들. 같은 계정의 캐릭터는 같은 순간에 같은 값을 갖는다.</summary>
public sealed record CharacterCurrencyHistory(string Key, IReadOnlyCollection<(long Deca, long MCash)> Pairs);

public static class AccountGrouper
{
    /// <summary>
    /// 묶는 근거로 쓸 수 있는 값인가. 데카·M캐시가 둘 다 있거나 합이 100 이상이어야 한다.
    /// (0이나 아주 작은 값은 서로 다른 계정에서도 우연히 같을 수 있다.)
    /// </summary>
    public static bool IsDistinctive((long Deca, long MCash) p) => (p.Deca > 0 && p.MCash > 0) || p.Deca + p.MCash >= 100;

    /// <summary>계정 id. 묶이지 않은 캐릭터는 혼자만의 계정(solo:키)이다.</summary>
    public static string AccountOf(AccountData data, string key) => data.Assign.TryGetValue(key, out var a) ? a.Account : $"solo:{key}";

    /// <summary>
    /// 자동 묶기: 두 캐릭터가 (데카, M캐시) 값이 같았던 적이 있으면 같은 계정으로 묶는다. 이미 묶인 건 풀지 않는다.
    /// 직접 정한(Manual) 캐릭터는 옮기지 않는다. 바뀐 것이 있으면 true.
    /// </summary>
    public static bool Reconcile(AccountData data, IReadOnlyList<CharacterCurrencyHistory> chars)
    {
        var byPair = new Dictionary<(long, long), SortedSet<string>>();
        foreach (var c in chars)
            foreach (var p in c.Pairs.Where(IsDistinctive))
            {
                if (!byPair.TryGetValue((p.Deca, p.MCash), out var set)) byPair[(p.Deca, p.MCash)] = set = new SortedSet<string>(StringComparer.Ordinal);
                set.Add(c.Key);
            }

        var changed = false;
        for (var pass = 0; pass < 6; pass++)   // 합쳐진 계정이 다른 묶음에 영향을 줄 수 있어 안정될 때까지 반복
        {
            var passChanged = false;
            foreach (var set in byPair.Values.Where(s => s.Count > 1).OrderBy(s => s.Min, StringComparer.Ordinal))
            {
                // 직접 정한(Manual) 캐릭터는 자동으로 옮기거나 끌어오지 않는다: 사용자가 일부러 뺀 캐릭터를 다시 붙이지 않게 한다
                var manualKeys = set.Where(k => data.Assign.TryGetValue(k, out var m) && m.Manual).ToList();
                var autoKeys = set.Except(manualKeys).ToList();
                if (autoKeys.Count == 0) continue;
                var autoAccounts = autoKeys.Where(data.Assign.ContainsKey).Select(k => data.Assign[k].Account).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                var manualAccounts = manualKeys.Select(k => data.Assign[k].Account).Distinct().ToList();
                string target;
                if (autoAccounts.Count > 0) target = autoAccounts[0];               // 이미 묶인 계정 중 앞선 것
                else if (manualAccounts.Count == 1) target = manualAccounts[0];     // 직접 정한 계정이 하나뿐이면 거기에 합류
                else if (manualAccounts.Count == 0) target = NewAccount(data);      // 아무도 묶이지 않았으면 새 계정
                else continue;                                                      // 직접 정한 계정이 여럿이면 판단하지 않는다
                foreach (var key in autoKeys)
                {
                    if (data.Assign.TryGetValue(key, out var cur))
                    {
                        if (cur.Account == target) continue;
                        MoveAccount(data, cur.Account, target, exceptManual: true);   // 계정이 합쳐진다: 그 계정의 나머지 캐릭터도 함께
                        passChanged = true;
                    }
                    else
                    {
                        data.Assign[key] = new AccountAssignment { Account = target };
                        passChanged = true;
                    }
                }
            }
            changed |= passChanged;
            if (!passChanged) break;
        }
        return changed;
    }

    private static void MoveAccount(AccountData data, string from, string to, bool exceptManual)
    {
        foreach (var (key, a) in data.Assign.ToList())
            if (a.Account == from && !(exceptManual && a.Manual)) data.Assign[key] = new AccountAssignment { Account = to };
        // 멤버십은 하나만 남는다(더 늦게 끝나는 쪽)
        if (data.Accounts.TryGetValue(from, out var src) && src.MembershipExpiresAtUtc is { } exp)
        {
            if (!data.Accounts.TryGetValue(to, out var dst)) data.Accounts[to] = dst = new AccountInfo();
            if (dst.MembershipExpiresAtUtc is null || dst.MembershipExpiresAtUtc < exp) dst.MembershipExpiresAtUtc = exp;
        }
        if (!data.Assign.Values.Any(a => a.Account == from)) data.Accounts.Remove(from);
    }

    public static string NewAccount(AccountData data)
    {
        var n = data.Accounts.Count + 1;
        string id;
        do id = $"acc-{n++}"; while (data.Accounts.ContainsKey(id) || data.Assign.Values.Any(a => a.Account == id));
        data.Accounts[id] = new AccountInfo();
        return id;
    }

    /// <summary>사용자가 직접 소속을 정한다. account가 null이거나 "new"이면 새(혼자만의) 계정으로 뺀다.</summary>
    public static string AssignManually(AccountData data, string characterKey, string? account)
    {
        var target = string.IsNullOrEmpty(account) || account == "new" ? NewAccount(data) : account;
        if (!data.Accounts.ContainsKey(target) && !target.StartsWith("solo:", StringComparison.Ordinal)) data.Accounts[target] = new AccountInfo();
        if (target.StartsWith("solo:", StringComparison.Ordinal)) target = NewAccount(data);
        var old = data.Assign.TryGetValue(characterKey, out var cur) ? cur.Account : null;
        data.Assign[characterKey] = new AccountAssignment { Account = target, Manual = true };
        if (old != null && old != target && !data.Assign.Values.Any(a => a.Account == old) && data.Accounts.TryGetValue(old, out var oi) && oi.MembershipExpiresAtUtc is null)
            data.Accounts.Remove(old);
        return target;
    }

    /// <summary>멤버십 만료 시각을 정한다(null이면 해제). 묶이지 않은 캐릭터면 혼자만의 계정을 만들어 저장한다.</summary>
    public static string SetMembership(AccountData data, string characterKey, DateTime? expiresAtUtc)
    {
        if (!data.Assign.TryGetValue(characterKey, out var a))
        {
            a = new AccountAssignment { Account = NewAccount(data) };
            data.Assign[characterKey] = a;
        }
        if (!data.Accounts.TryGetValue(a.Account, out var info)) data.Accounts[a.Account] = info = new AccountInfo();
        info.MembershipExpiresAtUtc = expiresAtUtc;
        return a.Account;
    }

    public static bool IsMember(AccountData data, string accountId, DateTime nowUtc) =>
        data.Accounts.TryGetValue(accountId, out var i) && i.MembershipExpiresAtUtc is { } e && e > nowUtc;
}
