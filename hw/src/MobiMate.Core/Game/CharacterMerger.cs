namespace MobiMate;

/// <summary>합치기 미리보기: 합쳐질 기록의 양 (확인 대화상자용).</summary>
public sealed record MergePreview(bool FromExists, bool IntoExists, int HistoryRecords, int HomeworkItems, bool HasSnapshot, bool HasAssignment);

/// <summary>합치기 결과. Ok가 아니면 아무것도 바뀌지 않았다(또는 되돌렸다).</summary>
public sealed record MergeResult(bool Ok, string Message, int HistoryRecords = 0, int HomeworkItems = 0);

/// <summary>
/// 같은 캐릭터가 둘로 갈라진 기록(예: 아이라_전사 / 아이라_전사#2)을 하나로 합친다 (2026-10-12).
/// 공유 저장 폴더의 네 파일(스냅숏·기록 DB·숙제 장부·계정)을 **한 공유 잠금 안에서** 읽고-고치고-쓴다.
/// - 서비스 잠금(SnapshotManager·HomeworkService)은 잡지 않는다: 잠금 순서(서비스 잠금 → 공유 잠금)를 거꾸로 타지 않기 위해서다.
///   다른 인스턴스(웹앱 역할)나 같은 프로세스의 저장소는 파일이 바뀐 것을 외부 변경으로 보고 다시 읽는다.
/// - 읽기에 하나라도 실패하면(잠김·손상) 아무것도 쓰지 않는다. 쓰다가 실패하면 이미 쓴 파일을 원래 내용으로 되돌린다.
/// - 이미 합쳐져 from이 없으면 NothingToMerge로 끝난다(다시 실행해도 안전).
/// </summary>
public sealed class CharacterMerger
{
    private readonly string _dir;
    private readonly HomeworkCatalog _catalog;
    private readonly Func<DateTimeOffset> _now;
    private readonly JsonFileStore _json = new();

    public CharacterMerger(string storageDir, HomeworkCatalog? catalog = null, Func<DateTimeOffset>? utcNow = null)
    {
        _dir = storageDir;
        _catalog = catalog ?? HomeworkCatalog.LoadEmbedded();
        _now = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    private string SnapPath => Path.Combine(_dir, "character_snapshots.json");
    private string DbPath => Path.Combine(_dir, "character_history_db.json");
    private string AccountPath => Path.Combine(_dir, "accounts.json");

    private sealed record Loaded(List<CharacterSnapshot> Snaps, List<CharacterProfile> Profiles, HomeworkFile Homework, AccountData Accounts);

    /// <summary>네 파일을 읽는다. 없는 파일은 빈 기록. 읽지 못하면(잠김·손상 격리) null.</summary>
    private Loaded? Read(HomeworkStore hw, out string? error)
    {
        error = null;
        var s = _json.TryLoad<List<CharacterSnapshot>>(SnapPath, out var snaps);
        var d = _json.TryLoad<List<CharacterProfile>>(DbPath, out var profiles);
        var a = _json.TryLoad<AccountData>(AccountPath, out var accounts);
        if (s is LoadStatus.Failed or LoadStatus.Quarantined || d is LoadStatus.Failed or LoadStatus.Quarantined || a is LoadStatus.Failed or LoadStatus.Quarantined)
        {
            error = "저장 파일을 읽지 못했습니다(다른 프로그램이 쓰는 중이거나 손상). 잠시 후 다시 시도하세요.";
            return null;
        }
        var file = hw.TryLoad();
        if (file == null) { error = "숙제 기록을 읽지 못했습니다. 잠시 후 다시 시도하세요."; return null; }
        accounts ??= new AccountData();
        accounts.Accounts ??= new(); accounts.Assign ??= new(); accounts.Order ??= new();
        return new Loaded(snaps ?? new(), profiles ?? new(), file, accounts);
    }

    public MergePreview Preview(string fromKey, string intoKey)
    {
        try
        {
            return SharedStorageLock.For(_dir).Run(() =>
            {
                var hw = new HomeworkStore(_dir);
                var l = Read(hw, out _);
                if (l == null) return new MergePreview(false, false, 0, 0, false, false);
                var from = l.Profiles.FirstOrDefault(p => p.CharacterKey == fromKey);
                var into = l.Profiles.Any(p => p.CharacterKey == intoKey);
                var items = l.Homework.Characters.TryGetValue(fromKey, out var led) ? led.Items.Count : 0;
                return new MergePreview(from != null, into, from?.History.Count ?? 0, items,
                    l.Snaps.Any(s => s.GetKey() == fromKey), l.Accounts.Assign.ContainsKey(fromKey));
            });
        }
        catch (IOException) { return new MergePreview(false, false, 0, 0, false, false); }
    }

    public MergeResult Merge(string fromKey, string intoKey)
    {
        if (string.IsNullOrWhiteSpace(fromKey) || string.IsNullOrWhiteSpace(intoKey)) return new MergeResult(false, "캐릭터 키가 필요합니다.");
        if (fromKey == intoKey) return new MergeResult(false, "같은 캐릭터끼리는 합칠 수 없습니다.");
        try { return SharedStorageLock.For(_dir).Run(() => MergeLocked(fromKey, intoKey)); }
        catch (IOException ex) { return new MergeResult(false, "저장소 잠금을 얻지 못했습니다: " + ex.Message); }
    }

    private MergeResult MergeLocked(string fromKey, string intoKey)
    {
        var hw = new HomeworkStore(_dir);
        var l = Read(hw, out var error);
        if (l == null) return new MergeResult(false, error!);

        var fromProfile = l.Profiles.FirstOrDefault(p => p.CharacterKey == fromKey);
        var intoProfile = l.Profiles.FirstOrDefault(p => p.CharacterKey == intoKey);
        var hasLedger = l.Homework.Characters.ContainsKey(fromKey);
        var hasSnap = l.Snaps.Any(s => s.GetKey() == fromKey);
        var hasAssign = l.Accounts.Assign.ContainsKey(fromKey);
        var fromObserved = string.Equals(l.Homework.LastObservedCharacter, fromKey, StringComparison.OrdinalIgnoreCase);
        if (fromProfile == null && !hasLedger && !hasSnap && !hasAssign && !fromObserved)
            return new MergeResult(true, $"{fromKey}의 기록이 이미 없습니다(합칠 것 없음).");
        if (intoProfile == null) return new MergeResult(false, $"합칠 대상 {intoKey}의 기록이 없습니다.");

        // 쓰기 전 원본(되돌리기용)
        var originals = new[] { SnapPath, DbPath, HwPath(hw), AccountPath }.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllText(p) : null);
        static string Ser(object o) => System.Text.Json.JsonSerializer.Serialize(o);
        var before = new Dictionary<string, string>
        {
            [SnapPath] = Ser(l.Snaps), [DbPath] = Ser(l.Profiles), [HwPath(hw)] = Ser(l.Homework), [AccountPath] = Ser(l.Accounts),
        };

        var movedRecords = fromProfile?.History.Count ?? 0;
        var movedItems = l.Homework.Characters.TryGetValue(fromKey, out var fromLedgerForCount) ? fromLedgerForCount.Items.Count : 0;

        // ── 기록 DB ──
        if (fromProfile != null)
        {
            var known = new HashSet<DateTime>(intoProfile.History.Select(h => h.Timestamp));
            foreach (var h in fromProfile.History) if (known.Add(h.Timestamp)) intoProfile.History.Add(h);   // 같은 시각은 into 쪽 하나
            intoProfile.History = intoProfile.History.OrderBy(h => h.Timestamp).ToList();
            intoProfile.FirstSeen = fromProfile.FirstSeen < intoProfile.FirstSeen ? fromProfile.FirstSeen : intoProfile.FirstSeen;
            intoProfile.LastSeen = fromProfile.LastSeen > intoProfile.LastSeen ? fromProfile.LastSeen : intoProfile.LastSeen;
            if (string.IsNullOrWhiteSpace(intoProfile.CustomName)) intoProfile.CustomName = fromProfile.CustomName;
            l.Profiles.Remove(fromProfile);
        }

        // ── 스냅숏: 더 최근 것을 into 이름으로 남긴다 ──
        var fromSnap = l.Snaps.FirstOrDefault(s => s.GetKey() == fromKey);
        if (fromSnap != null)
        {
            var intoSnap = l.Snaps.FirstOrDefault(s => s.GetKey() == intoKey);
            l.Snaps.Remove(fromSnap);
            if (intoSnap == null || fromSnap.Timestamp > intoSnap.Timestamp)
            {
                if (intoSnap != null) l.Snaps.Remove(intoSnap);
                var moved = fromSnap.Clone();
                moved.RealmName = intoProfile.RealmName; moved.JobName = intoProfile.JobName;
                var baseKey = $"{intoProfile.RealmName}_{intoProfile.JobName}";
                moved.KeySuffix = intoKey.StartsWith(baseKey, StringComparison.Ordinal) ? intoKey[baseKey.Length..] : "";
                l.Snaps.Add(moved);
            }
        }

        // ── 숙제 장부 ──
        var now = _now();
        if (l.Homework.Characters.TryGetValue(fromKey, out var fromLedger))
        {
            if (l.Homework.Characters.TryGetValue(intoKey, out var intoLedger)) MergeLedgers(fromLedger, intoLedger, _catalog, now);
            else l.Homework.Characters[intoKey] = fromLedger;
            l.Homework.Characters.Remove(fromKey);
        }
        if (fromObserved) l.Homework.LastObservedCharacter = intoKey;

        // ── 계정 ──
        if (hasAssign)
        {
            var assign = l.Accounts.Assign[fromKey];
            var intoHas = l.Accounts.Assign.ContainsKey(intoKey);
            if (!intoHas)
            {
                l.Accounts.Assign[intoKey] = new AccountAssignment { Account = assign.Account, Manual = assign.Manual };
                l.Accounts.Assign.Remove(fromKey);   // 같은 계정에 into가 남으므로 계정·멤버십은 그대로
            }
            else AccountGrouper.RemoveCharacter(l.Accounts, fromKey);
        }

        // ── 바뀐 파일만, 한 잠금 안에서 함께 쓴다. 실패하면 이미 쓴 것을 되돌린다 ──
        var plan = new List<(string Path, Func<bool> Write)>
        {
            (HwPath(hw), () => hw.Save(l.Homework)),
            (AccountPath, () => _json.Save(AccountPath, l.Accounts)),
            (SnapPath, () => _json.Save(SnapPath, l.Snaps)),
            (DbPath, () => _json.Save(DbPath, l.Profiles)),
        };
        var after = new Dictionary<string, string>
        {
            [SnapPath] = Ser(l.Snaps), [DbPath] = Ser(l.Profiles), [HwPath(hw)] = Ser(l.Homework), [AccountPath] = Ser(l.Accounts),
        };
        var written = new List<string>();
        foreach (var (path, write) in plan)
        {
            if (before[path] == after[path]) continue;   // 바뀐 파일만
            if (write()) { written.Add(path); continue; }
            foreach (var w in written) Restore(w, originals[w]);
            return new MergeResult(false, "저장하지 못해 합치기를 취소했습니다(원래대로 되돌림). 잠시 후 다시 시도하세요.");
        }
        return new MergeResult(true, $"{fromKey} → {intoKey} 합침 (기록 {movedRecords}건, 숙제 항목 {movedItems}개)", movedRecords, movedItems);
    }

    private static string HwPath(HomeworkStore hw) => hw.FilePath;

    private static void Restore(string path, string? text)
    {
        try { if (text != null) File.WriteAllText(path, text); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning($"합치기 되돌리기 실패({path}): {ex.Message}");
        }
    }

    // ── 숙제 장부 합치기 ──

    /// <summary>
    /// from 장부를 into 장부에 합친다 (into를 고친다). 먼저 두 장부 모두 현재 시각의 주기로 리셋을 적용해 지난 주기 항목을 없앤 뒤 합친다
    /// (오늘 체크가 from에, 어제 주기 기록이 into에 있으면 오늘 체크가 남는다).
    /// 같은 항목이 둘 다 있으면: 완료가 이긴다(둘 다 완료면 완료 시각이 이른 쪽) → 둘 다 미완료면 수동 설정이 더 최근인 쪽 → 단계 수가 큰 쪽 → into.
    /// 관찰 시각이 있는 값(가공 수량·레이드 증표)은 더 최근에 관찰한 쪽, 시각 기록(퀘스트 목격·레이드 지역 진입)은 항목별로 더 최근 값,
    /// 끊김 표시는 합집합(사라짐 = 완료 판정을 막는 안전한 쪽)으로 한다.
    /// </summary>
    public static void MergeLedgers(HomeworkLedger from, HomeworkLedger into, HomeworkCatalog catalog, DateTimeOffset nowUtc)
    {
        HomeworkEvaluator.ApplyResets(from, catalog, HomeworkShare.Character, nowUtc);
        HomeworkEvaluator.ApplyResets(into, catalog, HomeworkShare.Character, nowUtc);

        foreach (var (id, f) in from.Items)
        {
            if (!into.Items.TryGetValue(id, out var i) || PreferFrom(f, i)) into.Items[id] = f;
        }
        if (from.LastDailyResetUtc > into.LastDailyResetUtc) into.LastDailyResetUtc = from.LastDailyResetUtc;
        if (from.LastWeeklyResetUtc > into.LastWeeklyResetUtc) into.LastWeeklyResetUtc = from.LastWeeklyResetUtc;

        if (from.AlteringObservedUtc is { } fa && (into.AlteringObservedUtc is not { } ia || fa > ia))
        {
            into.AlteringDoneCounts = from.AlteringDoneCounts == null ? null : new Dictionary<string, int>(from.AlteringDoneCounts);
            into.AlteringObservedUtc = fa;
        }
        if (from.RaidTokensObservedUtc is { } fr && (into.RaidTokensObservedUtc is not { } ir || fr > ir))
        {
            into.RaidTokens = from.RaidTokens == null ? null : new Dictionary<string, long>(from.RaidTokens);
            into.RaidTokensObservedUtc = fr;
        }
        into.QuestSightings = MaxTimes(into.QuestSightings, from.QuestSightings);
        into.RaidAreaSince = MaxTimes(into.RaidAreaSince, from.RaidAreaSince);
        if (from.QuestContinuityBroken is { Count: > 0 })
        {
            into.QuestContinuityBroken ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in from.QuestContinuityBroken) into.QuestContinuityBroken.Add(id);
        }
    }

    /// <summary>같은 항목이 양쪽에 있을 때 from 쪽 값을 고를지.</summary>
    private static bool PreferFrom(HomeworkItemState from, HomeworkItemState into)
    {
        if (from.Completed != into.Completed) return from.Completed;
        if (from.Completed)
            return (from.CompletedAtUtc ?? DateTimeOffset.MaxValue) < (into.CompletedAtUtc ?? DateTimeOffset.MaxValue);
        var fm = from.ManualAtUtc ?? DateTimeOffset.MinValue;
        var im = into.ManualAtUtc ?? DateTimeOffset.MinValue;
        if (fm != im) return fm > im;
        return from.Count > into.Count;
    }

    private static Dictionary<string, DateTimeOffset>? MaxTimes(Dictionary<string, DateTimeOffset>? a, Dictionary<string, DateTimeOffset>? b)
    {
        if (b == null || b.Count == 0) return a;
        var r = a == null ? new Dictionary<string, DateTimeOffset>() : new Dictionary<string, DateTimeOffset>(a);
        foreach (var (k, v) in b) if (!r.TryGetValue(k, out var cur) || v > cur) r[k] = v;
        return r;
    }
}
