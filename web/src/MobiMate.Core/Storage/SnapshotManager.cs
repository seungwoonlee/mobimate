using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MobiMate;

public class CharacterSnapshot
{
    public string RealmName { get; set; } = "";
    public string JobName { get; set; } = "";
    public int Level { get; set; }
    public string Title { get; set; } = "";
    public long CombatScore { get; set; }
    public long ArcaneResistance { get; set; }
    public double WeightCurrent { get; set; }
    public long Gold { get; set; }
    public long Wings { get; set; }
    public long NyangToken { get; set; }
    public int CompletedDailyMissions { get; set; }
    public bool HasCurrencyBaseline { get; set; }
    /// <summary>false면 가방 무게 기준값이 비정상 읽기로 만들어져 아직 믿을 수 없다(첫 정상 값으로 다시 잡는다). 기본 true라 이전 기록은 그대로다.</summary>
    public bool HasWeightBaseline { get; set; } = true;
    public bool HasMissionBaseline { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>같은 서버·직업의 다른 캐릭터를 구분하는 뒤꼬리("#2" 등). 기본 캐릭터는 비어 있다.</summary>
    public string KeySuffix { get; set; } = "";

    public string GetKey() => $"{RealmName}_{JobName}{KeySuffix}";

    public CharacterSnapshot Clone() => (CharacterSnapshot)MemberwiseClone();
}

public class CharacterHistoryRecord
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int Level { get; set; }
    public string Title { get; set; } = "";
    public long CombatScore { get; set; }
    public long ArcaneResistance { get; set; }
    public long LivingScore { get; set; }
    /// <summary>매력 점수 (전체 캐릭터 현황 카드용, v1.5)</summary>
    public long AttractivenessScore { get; set; }
    /// <summary>데카 보유량 (전체 캐릭터 현황 카드용, v1.5)</summary>
    public long Deca { get; set; }
    /// <summary>M캐시 보유량. 데카와 함께 같은 계정을 알아내는 근거다 (계정 공유 재화)</summary>
    public long MCash { get; set; }
    /// <summary>은동전·마족 공물 보유량: 마지막으로 본 값으로 지금 예상 보유량을 계산한다 (CoinForecast)</summary>
    public long SilverCoin { get; set; }
    public long DemonTribute { get; set; }
    public long AttackPower { get; set; }
    public long DefencePower { get; set; }
    /// <summary>기본 스탯(힘·솜씨·지력·의지·행운)·최대 체력·데코 점수: 스탯 탭이 접속하지 않은 캐릭터의 마지막 값도 보여주려고 기록한다. 0 = 아직 못 받음.</summary>
    public long Str { get; set; }
    public long Dex { get; set; }
    public long Int { get; set; }
    public long Will { get; set; }
    public long Luck { get; set; }
    public long HealthMax { get; set; }
    public long DecorScore { get; set; }
    public double WeightCurrent { get; set; }
    public long Gold { get; set; }
    public long Wings { get; set; }
    public long NyangToken { get; set; }
    public int CompletedDailyMissions { get; set; }
}

public class CharacterProfile
{
    public string CharacterKey { get; set; } = "";
    public string RealmName { get; set; } = "";
    public string JobName { get; set; } = "";
    public string CustomName { get; set; } = ""; // 사용자 지정 닉네임/별칭
    public DateTime FirstSeen { get; set; } = DateTime.Now;
    public DateTime LastSeen { get; set; } = DateTime.Now;
    public List<CharacterHistoryRecord> History { get; set; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(CustomName)
        ? $"[{RealmName}] {JobName}"
        : $"[{RealmName}] {CustomName} ({JobName})";
}

public class SessionDelta
{
    public CharacterSnapshot Baseline { get; }
    public CharacterSnapshot Current { get; }

    public SessionDelta(CharacterSnapshot baseline, CharacterSnapshot current)
    {
        Baseline = baseline;
        Current = current;
    }

    public long CombatScoreDiff => Current.CombatScore - Baseline.CombatScore;
    public long ArcaneResistanceDiff => Current.ArcaneResistance - Baseline.ArcaneResistance;
    public double WeightDiff => Baseline.HasWeightBaseline ? Current.WeightCurrent - Baseline.WeightCurrent : 0;
    public long GoldDiff => Baseline.HasCurrencyBaseline ? (Current.Gold - Baseline.Gold) : 0;
    public long WingsDiff => Baseline.HasCurrencyBaseline ? (Current.Wings - Baseline.Wings) : 0;
    public long NyangDiff => Baseline.HasCurrencyBaseline ? (Current.NyangToken - Baseline.NyangToken) : 0;
    public int MissionDiff => Baseline.HasMissionBaseline ? (Current.CompletedDailyMissions - Baseline.CompletedDailyMissions) : 0;
}

/// <summary>
/// 캐릭터 스냅숏·기록 DB 관리. 같은 저장 폴더를 다른 프로세스(숙제 앱)도 쓸 수 있으므로(공유 저장 패치) 모든 수정은
/// "서비스 잠금(_lock) → 공유 잠금(_shared) → 파일이 바뀌었으면 다시 읽기 → 변경 → 바뀐 파일만 저장" 순서로 한다.
/// 두 파일(스냅숏·기록 DB)은 같은 공유 잠금 안에서 함께 저장한다. 다시 읽기는 TryLoad만 쓰므로, 잠김·교체 중이면
/// 메모리를 비우지 않고 이번 저장을 미룬다(dirty 표시를 남겨 다음에 저장).
/// </summary>
public class SnapshotManager
{
    private readonly string _storageDir;
    private readonly string _storageFile;
    private readonly string _dbStorageFile;
    private readonly string _customPersonasFile;
    private readonly string _legacyStorageFile;
    private readonly string _legacyDbStorageFile;

    private readonly Dictionary<string, CharacterSnapshot> _savedSnapshots = new();
    private readonly Dictionary<string, CharacterProfile> _characterDb = new();
    private readonly Dictionary<string, CharacterSnapshot> _sessionBaselines = new();
    private readonly object _lock = new();
    private readonly JsonFileStore _store = new();
    private readonly SharedStorageLock _shared;
    private readonly SharedFileTracker _snapTracker;
    private readonly SharedFileTracker _dbTracker;
    private bool _snapLoaded, _dbLoaded;   // 한 번이라도 파일에서 읽었는가 (못 읽었으면 빈 메모리를 저장해 덮어쓰지 않는다)
    private bool _snapDirty, _dbDirty;     // 메모리에만 있는 변경 (저장을 미뤘거나 실패)

    public string StorageDirectory => _storageDir;
    public string SnapshotsFilePath => _storageFile;
    public string CharacterDbFilePath => _dbStorageFile;
    public string CustomPersonasFilePath => _customPersonasFile;

    /// <summary>웹앱 전용 저장 폴더. WPF판(master)과 코드·데이터를 모두 분리한다.</summary>
    public static string DefaultStorageDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MobiMateWeb");

    /// <summary>WPF판 저장 폴더. 웹앱은 여기에 절대 쓰지 않고, ImportMissingFrom으로 읽어서 복사만 한다.</summary>
    public static string WpfStorageDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MobiMate");

    public SnapshotManager(string? customStorageDir = null)
    {
        _storageDir = !string.IsNullOrWhiteSpace(customStorageDir)
            ? customStorageDir
            : DefaultStorageDirectory;

        _storageFile = Path.Combine(_storageDir, "character_snapshots.json");
        _dbStorageFile = Path.Combine(_storageDir, "character_history_db.json");
        _customPersonasFile = Path.Combine(_storageDir, "custom_personas.json");
        _legacyStorageFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "character_snapshots.json");
        _legacyDbStorageFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "character_history_db.json");

        _shared = SharedStorageLock.For(_storageDir);
        _snapTracker = new SharedFileTracker(_storageFile);
        _dbTracker = new SharedFileTracker(_dbStorageFile);

        lock (_lock)
        {
            _shared.Run(() =>
            {
                EnsureStorageDirectoryAndMigrate();
                RefreshAndReplay();
            });
        }
    }

    private void EnsureStorageDirectoryAndMigrate()
    {
        try
        {
            if (!Directory.Exists(_storageDir))
            {
                Directory.CreateDirectory(_storageDir);
            }

            // 기존 레거시 디렉터리에 파일이 남아있고 새 스토리지에 없으면 자동 복사 이전
            if (!File.Exists(_storageFile) && File.Exists(_legacyStorageFile))
            {
                try { File.Copy(_legacyStorageFile, _storageFile, overwrite: false); } catch { }
            }
            if (!File.Exists(_dbStorageFile) && File.Exists(_legacyDbStorageFile))
            {
                try { File.Copy(_legacyDbStorageFile, _dbStorageFile, overwrite: false); } catch { }
            }
        }
        catch
        {
            // 디렉터리 생성 및 마이그레이션 실패 시 무해 처리
        }
    }

    /// <summary>
    /// 공유 잠금과 _lock 안에서 부른다. 파일이 마지막으로 읽거나 쓴 뒤 바뀌었으면 다시 읽는다(TryLoad만 사용).
    /// 두 파일 모두 쓸 수 있는 최신 상태면 true. 잠김·교체 중이면 false: 메모리는 그대로 두고 호출자가 저장을 미룬다.
    /// </summary>
    private bool RefreshLocked() => RefreshLocked(out _);

    private bool RefreshLocked(out bool reloaded)
    {
        reloaded = false;
        var ok = true;

        var r1 = _snapTracker.Refresh<List<CharacterSnapshot>>(_snapLoaded, out var snaps);
        if (r1 == RefreshResult.Reloaded)
        {
            _savedSnapshots.Clear();
            if (snaps != null) foreach (var s in snaps) _savedSnapshots[s.GetKey()] = s;
            _snapLoaded = true;
            _snapDirty = false;
            reloaded = true;
        }
        else if (r1 == RefreshResult.KeepMemory) _snapDirty = true;   // 파일이 사라짐·격리됨: 메모리가 기준, 다음 저장이 다시 만든다
        else if (r1 == RefreshResult.Unavailable) ok = false;

        var r2 = _dbTracker.Refresh<List<CharacterProfile>>(_dbLoaded, out var profiles);
        if (r2 == RefreshResult.Reloaded)
        {
            _characterDb.Clear();
            if (profiles != null) foreach (var p in profiles) _characterDb[p.CharacterKey] = p;
            _dbLoaded = true;
            _dbDirty = false;
            reloaded = true;
        }
        else if (r2 == RefreshResult.KeepMemory) _dbDirty = true;
        else if (r2 == RefreshResult.Unavailable) ok = false;

        return ok;
    }

    // 읽기 실패(잠김)로 저장을 미룬 변경. 다시 읽어 메모리가 교체되면 사라지므로, "다시 적용할 수 있는 변경 함수"로 보관했다가
    // 성공적으로 다시 읽은 뒤 재적용해 저장한다. 대상(종류:캐릭터 키)이 같으면 덮어써서 쌓이지 않고, 상한을 넘으면 로그를 남기고 가장 오래된 것을 버린다.
    private const int MaxPending = 500;
    private readonly Dictionary<string, Action> _pending = new();

    private void Defer(string target, Action apply)
    {
        _pending.Remove(target);   // 나중 변경이 이긴다(순서도 뒤로)
        _pending[target] = apply;
        if (_pending.Count > MaxPending)
        {
            var oldest = _pending.Keys.First();
            _pending.Remove(oldest);
            System.Diagnostics.Trace.TraceWarning($"저장을 미룬 변경이 {MaxPending}개를 넘어 가장 오래된 것({oldest})을 버렸습니다.");
        }
    }

    private bool RefreshAndReplay() => RefreshAndReplay(out _);

    /// <summary>
    /// 공유 잠금과 _lock 안에서 부른다. 다시 읽고(<see cref="RefreshLocked(out bool)"/>), 성공했으면 미룬 변경을 재적용해 저장한다.
    /// 다시 읽지 않았으면(메모리가 이미 그 변경을 담고 있다) 큐만 비우고 dirty 표시대로 저장한다.
    /// </summary>
    private bool RefreshAndReplay(out bool reloaded)
    {
        var ok = RefreshLocked(out reloaded);
        if (!ok) return false;
        if (_pending.Count > 0)
        {
            var ops = _pending.Values.ToList();
            _pending.Clear();
            if (reloaded) foreach (var op in ops) op();
        }
        if (_snapDirty || _dbDirty) SaveLocked();
        return true;
    }

    /// <summary>읽기 경로용: 파일이 바뀌었으면 다시 읽는다(잠금 없이 stat만으로 먼저 확인). 실패해도 메모리 값으로 계속한다.</summary>
    private void EnsureFresh()
    {
        if (_snapLoaded && _dbLoaded && !_snapTracker.IsStale && !_dbTracker.IsStale) return;
        try
        {
            lock (_lock) _shared.Run(() => RefreshAndReplay());
        }
        catch (IOException) { /* 잠금을 못 얻으면 메모리 값으로 */ }
    }

    /// <summary>
    /// 다른 프로세스가 파일을 바꿨으면 다시 읽고 true. 변경 감지기가 부른다. 자기 쓰기는 바뀐 것으로 치지 않는다.
    /// </summary>
    public bool RefreshExternal()
    {
        if (_snapLoaded && _dbLoaded && !_snapTracker.IsStale && !_dbTracker.IsStale) return false;
        lock (_lock)
        {
            return _shared.Run(() =>
            {
                RefreshAndReplay(out var reloaded);
                return reloaded;
            });
        }
    }

    /// <summary>공유 잠금과 _lock 안에서 부른다. 메모리에서 바뀐(dirty) 파일을 같은 잠금 안에서 함께 저장한다. 실패하면 dirty를 남겨 다음에 다시 저장한다.</summary>
    private bool SaveLocked(bool snapshots = false, bool db = false)
    {
        snapshots |= _snapDirty;
        db |= _dbDirty;
        var ok = true;
        if (snapshots)
        {
            if (_store.Save(_storageFile, _savedSnapshots.Values.Select(s => s.Clone()).ToList())) { _snapTracker.MarkWritten(); _snapDirty = false; }
            else { _snapDirty = true; ok = false; }
        }
        if (db)
        {
            var dbToSave = _characterDb.Values.Select(p => new CharacterProfile
            {
                CharacterKey = p.CharacterKey,
                RealmName = p.RealmName,
                JobName = p.JobName,
                CustomName = p.CustomName,
                FirstSeen = p.FirstSeen,
                LastSeen = p.LastSeen,
                History = new List<CharacterHistoryRecord>(p.History)
            }).ToList();
            if (_store.Save(_dbStorageFile, dbToSave)) { _dbTracker.MarkWritten(); _dbDirty = false; }
            else { _dbDirty = true; ok = false; }
        }
        return ok;
    }

    /// <summary>메모리의 스냅숏·기록 DB를 그대로 저장한다(다시 읽지 않는다). 내부 수정 경로는 이 메서드를 쓰지 않고 공유 잠금 안에서 저장한다.</summary>
    public void SaveSnapshotsNow()
    {
        lock (_lock) _shared.Run(() => SaveLocked(true, true));
    }

    /// <summary>호환용 이름. 순서가 뒤집히지 않도록 더는 Task.Run을 쓰지 않고 공유 잠금 안에서 동기로 저장한다.</summary>
    public void SaveSnapshotsAsync() => SaveSnapshotsNow();

    public CharacterProfile? GetProfile(string realm, string job) => GetProfileByKey($"{realm}_{job}");

    public CharacterProfile? GetProfileByKey(string key)
    {
        EnsureFresh();
        lock (_lock)
        {
            return _characterDb.TryGetValue(key, out var p) ? p : null;
        }
    }

    /// <summary>
    /// 지금 읽은 캐릭터가 어느 기록인지 정한다 (같은 서버·직업의 캐릭터가 여럿일 수 있다, CharacterIdentity).
    /// 재화를 읽지 못했으면 null을 넘긴다: 그때는 레벨·생활력만으로 판단한다. 읽기만 하고 기록은 만들지 않는다.
    /// </summary>
    public string ResolveKey(CharacterInfo? ch, IEnumerable<CurrencyItem>? currencies = null)
    {
        EnsureFresh();
        var baseKey = CharacterIdentity.BaseKey(ch?.RealmName, ch?.JobName);
        long? deca = null, mcash = null;
        if (currencies != null)
        {
            var list = currencies as IList<CurrencyItem> ?? currencies.ToList();
            deca = list.FirstOrDefault(c => c.DisplayName == "데카")?.Amount ?? 0;
            mcash = list.FirstOrDefault(c => c.DisplayName is "M캐시" or "M캐쉬")?.Amount ?? 0;
        }
        List<IdentityCandidate> cands;
        lock (_lock)
        {
            cands = _characterDb.Values.Where(p => CharacterIdentity.IsVariantOf(p.CharacterKey, baseKey)).Select(p =>
            {
                var last = p.History.LastOrDefault();
                var pair = p.History.AsEnumerable().Reverse().Take(30).FirstOrDefault(h => AccountGrouper.IsDistinctive((h.Deca, h.MCash)));   // 가장 최근의 쓸 만한 값
                return new IdentityCandidate(p.CharacterKey, last?.Level ?? 0, last?.LivingScore ?? 0, last?.CombatScore ?? 0, pair?.Deca ?? 0, pair?.MCash ?? 0, p.LastSeen);
            }).ToList();
        }
        return CharacterIdentity.Resolve(baseKey, cands, new IdentityReading(ch?.Level ?? 0, ch?.LivingScore?.Value ?? 0, ch?.CombatScore?.Value ?? 0, deca, mcash));
    }

    /// <summary>재화 목록에서 골드·데카·M캐시를 읽는다. 골드를 못 찾으면 null (재화를 읽지 못한 것으로 본다).</summary>
    public static Wallet? WalletOf(IEnumerable<CurrencyItem>? currencies)
    {
        if (currencies == null) return null;
        var list = currencies as IList<CurrencyItem> ?? currencies.ToList();
        var gold = list.FirstOrDefault(c => c.DisplayName == "골드")?.Amount;
        if (gold == null) return null;
        return new Wallet(list.FirstOrDefault(c => c.DisplayName == "데카")?.Amount ?? 0,
            list.FirstOrDefault(c => c.DisplayName is "M캐시" or "M캐쉬")?.Amount ?? 0, gold.Value);
    }

    /// <summary>
    /// 지금 읽은 캐릭터가 접속 중이던 캐릭터(currentKey)의 부직업(주↔부직업 전환)이면 currentKey를, 아니면 null을 돌려준다.
    /// 이전 재화는 앞서 읽어 둔 값(before)과 그 기록의 마지막 값 둘 다 비교한다 (CharacterIdentity.IsJobSwap).
    /// </summary>
    public string? DetectJobSwap(string? currentKey, CharacterInfo? ch, IEnumerable<CurrencyItem>? now, IEnumerable<CurrencyItem>? before)
    {
        if (currentKey == null || !IsRealCharacter(ch) || WalletOf(now) is not { } cur) return null;
        EnsureFresh();
        CharacterProfile? p; Wallet? last = null;
        lock (_lock)
        {
            if (!_characterDb.TryGetValue(currentKey, out p)) return null;
            var h = p.History.AsEnumerable().Reverse().FirstOrDefault(r => r.Gold > 0);
            if (h != null) last = new Wallet(h.Deca, h.MCash, h.Gold);
        }
        bool Swap(Wallet? w) => CharacterIdentity.IsJobSwap(p.RealmName, p.JobName, ch!.RealmName, ch.JobName, w, cur);
        // 앞서 읽어 둔 재화(before)가 이미 새 캐릭터의 재화일 수 있다(재화는 캐릭터 정보보다 먼저 바뀐다): 그 캐릭터의 기록(last)과 골드가
        // 크게 다르면 믿지 않는다. 믿으면 새 캐릭터로 접속했는데 "같은 재화 + 다른 직업"이라 부직업 전환으로 오인해 이전 캐릭터에 머문다.
        var beforeWallet = WalletOf(before);
        var beforeIsTrustworthy = beforeWallet == null || last == null
            || Math.Abs(beforeWallet.Gold - last.Gold) <= Math.Max(beforeWallet.Gold, last.Gold) * 0.10;
        if (!((beforeIsTrustworthy && Swap(beforeWallet)) || Swap(last))) return null;
        // 이 골드가 이미 알고 있는 다른 캐릭터의 골드와 같으면 직업 전환이 아니라 그 캐릭터로 접속한 것이다
        // (게임이 캐릭터 정보는 늦게, 재화는 먼저 바꿔 알려 주면 이전 캐릭터 기록에 새 캐릭터의 골드가 섞여 전환처럼 보인다)
        return WalletBelongsToOther(currentKey, cur.Gold, ignoreOwn: true) ? null : currentKey;   // 기록이 이미 섞였더라도 다른 캐릭터와 같으면 전환이 아니다
    }

    /// <summary>
    /// 이 골드가 key가 아닌 다른 캐릭터가 마지막으로 가졌던 골드와 같은가 (key 자신의 마지막 골드와는 다를 때만).
    /// 캐릭터를 바꾼 직후 캐릭터 정보는 아직 이전 캐릭터인데 재화는 새 캐릭터 것으로 읽힐 때, 이전 캐릭터 기록에 쓰지 않게 거르는 근거다.
    /// </summary>
    public bool WalletBelongsToOther(string key, long gold, bool ignoreOwn = false)
    {
        const long MinGold = 100_000;   // 작은 값은 우연히 같을 수 있다
        if (gold < MinGold) return false;
        bool Near(long a) => a >= MinGold && Math.Abs(a - gold) <= Math.Max(a, gold) * CharacterIdentity.GoldDriftRatio;
        EnsureFresh();
        lock (_lock)
        {
            long LastGold(CharacterProfile p) => p.History.AsEnumerable().Reverse().FirstOrDefault(r => r.Gold > 0)?.Gold ?? 0;
            if (!ignoreOwn && _characterDb.TryGetValue(key, out var own) && Near(LastGold(own))) return false;
            return _characterDb.Values.Any(p => p.CharacterKey != key && Near(LastGold(p)));
        }
    }

    public bool WalletBelongsToOther(string key, IEnumerable<CurrencyItem>? currencies) =>
        WalletOf(currencies) is { } w && WalletBelongsToOther(key, w.Gold);

    /// <summary>이 기록의 캐릭터가 지금은 다른 직업(부직업)을 끼고 있는가. 그동안의 읽기는 기록·전투력 재확인에 쓰지 않는다.</summary>
    public bool IsSubJob(string key, CharacterInfo? ch)
    {
        if (ch == null || string.IsNullOrWhiteSpace(ch.JobName)) return false;
        EnsureFresh();
        lock (_lock) return _characterDb.TryGetValue(key, out var p) && !string.IsNullOrWhiteSpace(p.JobName) && p.JobName != ch.JobName;
    }

    /// <summary>
    /// 한 기록에 두 캐릭터가 섞였을 때(같은 서버·직업) 가장 최근 값을 새 캐릭터로 떼어 낸다. 기록이 하나뿐이면 null.
    /// 이후에는 그 값과 가까운 읽기가 새 캐릭터로 이어진다.
    /// </summary>
    public string? SplitLatest(string key)
    {
        string? newKey = null;
        lock (_lock) return _shared.Run(() =>
        {
            if (!RefreshAndReplay()) return null;   // 최신 기록을 읽지 못했다: 나누지 않고 미룬다
            if (!_characterDb.TryGetValue(key, out var p) || p.History.Count < 2) return null;
            var last = p.History[^1];
            p.History.RemoveAt(p.History.Count - 1);
            var baseKey = CharacterIdentity.BaseKey(p.RealmName, p.JobName);
            newKey = CharacterIdentity.NextKey(baseKey, _characterDb.Keys.Where(k => CharacterIdentity.IsVariantOf(k, baseKey)));
            _characterDb[newKey] = new CharacterProfile
            {
                CharacterKey = newKey, RealmName = p.RealmName, JobName = p.JobName,
                FirstSeen = last.Timestamp, LastSeen = DateTime.Now, History = new List<CharacterHistoryRecord> { last },
            };
            var prev = p.History[^1];
            if (_savedSnapshots.TryGetValue(key, out var snap))
            {
                var moved = snap.Clone(); moved.KeySuffix = newKey[baseKey.Length..];
                _savedSnapshots[newKey] = moved;
                snap.Level = prev.Level; snap.CombatScore = prev.CombatScore; snap.ArcaneResistance = prev.ArcaneResistance; snap.Title = prev.Title;
            }
            _sessionBaselines.Remove(key);
            p.LastSeen = prev.Timestamp;
            _snapDirty = _dbDirty = true;
            SaveLocked();
            return newKey;
        });
    }

    /// <summary>마지막으로 저장된 전투력 (서버를 다시 켠 직후 전투력 재확인의 기준, FR-DT-10). 없으면 null.</summary>
    public long? GetSavedCombat(string realm, string job) => GetSavedCombat($"{realm}_{job}");

    public long? GetSavedCombat(string key)
    {
        EnsureFresh();
        lock (_lock)
        {
            return _savedSnapshots.TryGetValue(key, out var s) && s.CombatScore > 0 ? s.CombatScore : null;
        }
    }

    public void SetCustomName(string realm, string job, string customName) => SetCustomName($"{realm}_{job}", realm, job, customName);

    public void SetCustomName(string key, string realm, string job, string customName)
    {
        lock (_lock) _shared.Run(() =>
        {
            var fresh = RefreshAndReplay();   // 못 읽었으면 메모리만 고치고 저장은 미룬 변경 큐에 넣는다
            ApplyName();
            if (!fresh || !SaveLocked()) Defer("n:" + key, ApplyName);

            void ApplyName()
            {
            if (!_characterDb.TryGetValue(key, out var p))
            {
                p = new CharacterProfile
                {
                    CharacterKey = key,
                    RealmName = realm,
                    JobName = job,
                    FirstSeen = DateTime.Now,
                    LastSeen = DateTime.Now
                };
                _characterDb[key] = p;
            }
            p.CustomName = customName.Trim();
            _dbDirty = true;
            }
        });
    }

    /// <summary>필요 없는 캐릭터의 기록을 지운다(승운 요청). 다시 접속하면 새 기록으로 다시 생긴다. 지운 게 있으면 true.</summary>
    public bool RemoveCharacter(string characterKey)
    {
        lock (_lock)
        {
            return _shared.Run(() =>
            {
                if (!RefreshAndReplay()) return false;   // 최신 기록을 읽지 못했다: 지우지 않고 미룬다
                var removed = _characterDb.Remove(characterKey);
                removed |= _savedSnapshots.Remove(characterKey);
                _sessionBaselines.Remove(characterKey);
                if (removed)
                {
                    _snapDirty = _dbDirty = true;
                    SaveLocked();
                }
                return removed;
            });
        }
    }

    public List<CharacterProfile> GetAllProfiles()
    {
        EnsureFresh();
        lock (_lock)
        {
            return _characterDb.Values.OrderByDescending(p => p.LastSeen).ToList();
        }
    }

    /// <summary>
    /// 다른 폴더(보통 WPF판)의 기록 파일 중 이 저장소에 **없는 파일만** 복사해 온다 (NFR-13, 파일별 규칙).
    /// 원본 폴더에는 쓰지 않는다. 가져온 파일 수를 돌려준다. 가져온 뒤 메모리 상태를 다시 읽는다.
    /// (숙제 기록은 형식 변환이 필요해 HomeworkStore.ImportFromWpfIfMissing이 따로 가져온다.)
    /// </summary>
    public int ImportMissingFrom(string sourceDir)
    {
        if (string.IsNullOrWhiteSpace(sourceDir) ||
            Path.GetFullPath(sourceDir).TrimEnd('\\', '/').Equals(Path.GetFullPath(_storageDir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var targets = new[] { _storageFile, _dbStorageFile, _customPersonasFile };

        lock (_lock)
        {
            return _shared.Run(() =>
            {
                var copied = 0;
                foreach (var target in targets)
                {
                    var source = Path.Combine(sourceDir, Path.GetFileName(target));
                    try
                    {
                        if (File.Exists(target) || !File.Exists(source)) continue;
                        Directory.CreateDirectory(_storageDir);
                        File.Copy(source, target, overwrite: false);
                        copied++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // 복사 실패는 빈 저장소로 시작하는 것과 같다
                    }
                }

                if (copied > 0) RefreshAndReplay();   // 새로 생긴 파일을 읽는다
                return copied;
            });
        }
    }

    /// <summary>
    /// 실제 캐릭터의 정보인가. 캐릭터 선택창에서는 서버·직업이 비고 레벨이 0인 정보가 들어온다. 그런 정보는 캐릭터가 아니다.
    /// </summary>
    public static bool IsRealCharacter(CharacterInfo? ch) =>
        ch != null && !string.IsNullOrWhiteSpace(ch.RealmName) && !string.IsNullOrWhiteSpace(ch.JobName) && ch.Level > 0;

    public SessionDelta UpdateSnapshot(
        CharacterInfo? ch,
        List<CurrencyItem>? currencies,
        List<MissionItem>? dailyMissions,
        string? characterKey = null)
    {
        if (!IsRealCharacter(ch))
        {
            // 기록하지 않는다: "에린 · 밀레시안 · Lv.0" 같은 가짜 캐릭터가 생기지 않게 한다
            var empty = new CharacterSnapshot();
            return new SessionDelta(empty, empty);
        }
        var realm = string.IsNullOrWhiteSpace(ch?.RealmName) ? "에린" : ch.RealmName;
        var job = string.IsNullOrWhiteSpace(ch?.JobName) ? "밀레시안" : ch.JobName;
        var level = ch?.Level ?? 1;
        var title = ch?.Title ?? "";
        var combatScore = ch?.CombatScore?.Value ?? 0;
        var mdef = ch?.ArcaneResistance?.Value ?? 0;
        var weightOk = ch?.Vitals is { } vitals && vitals.IsWeightSane();   // 비정상 읽기(현재 0·최대 3.4e38)는 기록하지 않는다
        var weight = weightOk ? ch!.Vitals!.WeightCurrent : 0.0;

        var key = characterKey ?? ResolveKey(ch, currencies);   // 같은 서버·직업의 다른 캐릭터는 "서버_직업#2"로 구분한다
        if (characterKey != null && IsSubJob(characterKey, ch))
        {
            // 부직업으로 잠시 바꾼 상태: 주직업 기록에 부직업의 전투력·레벨이 섞이지 않게 기록하지 않는다
            var empty = new CharacterSnapshot();
            return new SessionDelta(empty, empty);
        }
        var suffix = key.StartsWith($"{realm}_{job}", StringComparison.Ordinal) ? key[$"{realm}_{job}".Length..] : "";

        lock (_lock) return _shared.Run(() =>
        {
            var fresh = RefreshAndReplay();   // 못 읽었으면(잠김) 메모리만 갱신하고 저장은 미룬 변경 큐에 넣는다
            var delta = ApplyUpdate();
            if (!fresh || ((_snapDirty || _dbDirty) && !SaveLocked())) Defer("u:" + key, () => ApplyUpdate());   // 저장하지 못한 변경도 같은 큐에 둔다
            return delta;

            // 다시 적용할 수 있는 변경: 메모리에만 반영한다(저장은 호출자가 한다)
            SessionDelta ApplyUpdate()
            {
            _savedSnapshots.TryGetValue(key, out var prevSaved);

            // 1. 현재 데이터 구성 (미수신 필드는 이전 저장값 보존)
            var current = prevSaved != null ? prevSaved.Clone() : new CharacterSnapshot
            {
                RealmName = realm,
                JobName = job
            };

            current.RealmName = realm;
            current.JobName = job;
            current.KeySuffix = suffix;
            current.Level = level;
            current.Title = title;
            current.CombatScore = combatScore;
            current.ArcaneResistance = mdef;
            if (weightOk || prevSaved == null) current.WeightCurrent = weight;   // 못 읽은 때는 이전 저장값을 둔다
            current.Timestamp = DateTime.Now;

            // 재화 데이터가 제공된 경우에만 갱신
            if (currencies != null)
            {
                foreach (var c in currencies)
                {
                    if (c.DisplayName == "골드") current.Gold = c.Amount;
                    else if (c.DisplayName == "정령의 날개") current.Wings = c.Amount;
                    else if (c.DisplayName == "냥 토큰") current.NyangToken = c.Amount;
                }
            }

            // 미션 데이터가 제공된 경우에만 갱신
            if (dailyMissions != null)
            {
                current.CompletedDailyMissions = dailyMissions.Count(m => m.IsCompleted);
            }

            // 2. 세션 베이스라인 관리 (최초 생성 또는 유효 데이터 수신 시 필드별 확정)
            if (!_sessionBaselines.TryGetValue(key, out var baseline))
            {
                baseline = new CharacterSnapshot
                {
                    RealmName = realm,
                    JobName = job,
                    Level = level,
                    Title = title,
                    CombatScore = combatScore,
                    ArcaneResistance = mdef,
                    WeightCurrent = weightOk ? weight : current.WeightCurrent,
                    HasWeightBaseline = weightOk,
                    Timestamp = DateTime.Now
                };

                if (currencies != null)
                {
                    baseline.Gold = current.Gold;
                    baseline.Wings = current.Wings;
                    baseline.NyangToken = current.NyangToken;
                    baseline.HasCurrencyBaseline = true;
                }

                if (dailyMissions != null)
                {
                    baseline.CompletedDailyMissions = current.CompletedDailyMissions;
                    baseline.HasMissionBaseline = true;
                }

                _sessionBaselines[key] = baseline;
            }
            else
            {
                // 베이스라인이 이미 있지만 재화/미션 데이터가 이번에 처음으로 들어온 경우 필드 베이스라인 확정
                if (!baseline.HasCurrencyBaseline && currencies != null)
                {
                    baseline.Gold = current.Gold;
                    baseline.Wings = current.Wings;
                    baseline.NyangToken = current.NyangToken;
                    baseline.HasCurrencyBaseline = true;
                }

                if (!baseline.HasMissionBaseline && dailyMissions != null)
                {
                    baseline.CompletedDailyMissions = current.CompletedDailyMissions;
                    baseline.HasMissionBaseline = true;
                }

                if (!baseline.HasWeightBaseline && weightOk)
                {
                    baseline.WeightCurrent = current.WeightCurrent;
                    baseline.HasWeightBaseline = true;
                }
            }

            var snapChanged = prevSaved == null || !SameSnapshot(prevSaved, current);   // 시각(Timestamp)만 다른 것은 바뀐 것이 아니다
            _savedSnapshots[key] = current;

            // 3. 캐릭터 프로필 및 누적 히스토리 DB 갱신
            var profileCreated = false;
            if (!_characterDb.TryGetValue(key, out var profile))
            {
                profile = new CharacterProfile
                {
                    CharacterKey = key,
                    RealmName = realm,
                    JobName = job,
                    FirstSeen = DateTime.Now,
                    LastSeen = DateTime.Now
                };
                _characterDb[key] = profile;
                profileCreated = true;
            }
            profile.LastSeen = DateTime.Now;   // 기록할 것이 없는 호출은 메모리에만 반영한다(저장은 값이 바뀌거나 5분마다 쌓는 기록과 함께)

            // 히스토리 중복 방지 (수치 변동 또는 최소 5분 경과 시 누적 기록)
            var lastRecord = profile.History.LastOrDefault();
            // 이번에 받지 못한 값은 이전 기록 값을 이어 간다 (재화·캐릭터 정보는 따로 조회되므로)
            var living = ScoreReading.OrLast(ch?.LivingScore?.Value, lastRecord?.LivingScore);   // 0은 못 읽은 값(캐릭터 전환 직후)
            var attract = ScoreReading.OrLast(ch?.AttractivenessScore?.Value, lastRecord?.AttractivenessScore);
            var deca = currencies?.FirstOrDefault(c => c.DisplayName == "데카")?.Amount ?? lastRecord?.Deca ?? 0;
            // 재화 목록을 받았으면 그 안에 없는 값은 0이다(예: 아직 얻지 못한 재화). 받지 못했으면 이전 기록을 잇는다.
            long Money(string[] names, long? prev) => currencies == null ? prev ?? 0 : currencies.FirstOrDefault(c => names.Contains(c.DisplayName))?.Amount ?? 0;
            var mcash = Money(new[] { "M캐시", "M캐쉬" }, lastRecord?.MCash);
            var silver = Money(new[] { "은동전" }, lastRecord?.SilverCoin);
            var tribute = Money(new[] { "마족 공물", "마족공물" }, lastRecord?.DemonTribute);
            bool shouldRecord = lastRecord == null ||
                (attract > 0 && lastRecord.AttractivenessScore != attract) ||
                (deca > 0 && lastRecord.Deca != deca) ||
                (currencies != null && (lastRecord.MCash != mcash || lastRecord.SilverCoin != silver || lastRecord.DemonTribute != tribute)) ||
                (DateTime.Now - lastRecord.Timestamp).TotalMinutes >= 5 ||
                lastRecord.CombatScore != current.CombatScore ||
                lastRecord.ArcaneResistance != current.ArcaneResistance ||
                lastRecord.Level != current.Level ||
                StatsChanged(lastRecord, ch) ||
                (current.Gold > 0 && lastRecord.Gold != current.Gold) ||
                (current.Wings > 0 && lastRecord.Wings != current.Wings) ||
                (current.NyangToken > 0 && lastRecord.NyangToken != current.NyangToken) ||
                lastRecord.CompletedDailyMissions != current.CompletedDailyMissions;

            if (shouldRecord)
            {
                profile.History.Add(new CharacterHistoryRecord
                {
                    Timestamp = DateTime.Now,
                    Level = current.Level,
                    Title = current.Title,
                    CombatScore = current.CombatScore,
                    ArcaneResistance = current.ArcaneResistance,
                    LivingScore = living,
                    AttractivenessScore = attract,
                    Deca = deca,
                    MCash = mcash,
                    SilverCoin = silver,
                    DemonTribute = tribute,
                    AttackPower = ch?.AttackPower?.Value ?? 0,
                    DefencePower = ch?.DefencePower?.Value ?? 0,
                    Str = ch?.STR?.Value ?? 0, Dex = ch?.DEX?.Value ?? 0, Int = ch?.INT?.Value ?? 0, Will = ch?.WILL?.Value ?? 0, Luck = ch?.LUCK?.Value ?? 0,
                    HealthMax = ch?.HealthMax?.Value ?? 0, DecorScore = ch?.DecorScore?.Value ?? 0,
                    WeightCurrent = current.WeightCurrent,
                    Gold = current.Gold,
                    Wings = current.Wings,
                    NyangToken = current.NyangToken,
                    CompletedDailyMissions = current.CompletedDailyMissions
                });
            }

            // 기록할 것이 없으면 저장하지 않는다 (4초 주기 감시가 파일을 갈아 쓰지 않게)
            if (snapChanged) _snapDirty = true;
            if (profileCreated || shouldRecord) _dbDirty = true;

            return new SessionDelta(baseline, current);
            }
        });
    }

    /// <summary>스탯 탭용 값이 지난 기록과 다른가(받은 값만 비교한다: 응답에 없으면 변화로 보지 않는다).</summary>
    private static bool StatsChanged(CharacterHistoryRecord last, CharacterInfo? ch)
    {
        if (ch == null) return false;
        static bool Diff(long recorded, ScoreVal? v) => v != null && v.Value > 0 && v.Value != recorded;
        return Diff(last.Str, ch.STR) || Diff(last.Dex, ch.DEX) || Diff(last.Int, ch.INT) || Diff(last.Will, ch.WILL) || Diff(last.Luck, ch.LUCK)
            || Diff(last.AttackPower, ch.AttackPower) || Diff(last.DefencePower, ch.DefencePower) || Diff(last.HealthMax, ch.HealthMax) || Diff(last.DecorScore, ch.DecorScore);
    }

    private static bool SameSnapshot(CharacterSnapshot a, CharacterSnapshot b) =>
        a.RealmName == b.RealmName && a.JobName == b.JobName && a.KeySuffix == b.KeySuffix && a.Level == b.Level && a.Title == b.Title
        && a.CombatScore == b.CombatScore && a.ArcaneResistance == b.ArcaneResistance && a.WeightCurrent == b.WeightCurrent
        && a.Gold == b.Gold && a.Wings == b.Wings && a.NyangToken == b.NyangToken
        && a.CompletedDailyMissions == b.CompletedDailyMissions && a.HasCurrencyBaseline == b.HasCurrencyBaseline && a.HasMissionBaseline == b.HasMissionBaseline;

    public static string FormatDiff(long diff, string unit = "")
    {
        if (diff == 0) return "";
        var sign = diff > 0 ? "+" : "";
        var arrow = diff > 0 ? "▲" : "▼";
        return $"{sign}{diff:N0}{unit} {arrow}";
    }

    public static string FormatWeightDiff(double diff)
    {
        if (Math.Abs(diff) < 0.05) return "";
        var sign = diff > 0 ? "+" : "";
        var arrow = diff > 0 ? "▲" : "▼";
        return $"{sign}{diff:F1} {arrow}";
    }

    // ================= 4. 커스텀 페르소나 영구 저장 (custom_personas.json) =================
    public List<CustomPersona> LoadCustomPersonas()
    {
        return _store.Load<List<CustomPersona>>(_customPersonasFile) ?? new List<CustomPersona>();
    }

    public void SaveCustomPersona(CustomPersona persona)
    {
        if (persona == null || string.IsNullOrWhiteSpace(persona.Name)) return;

        lock (_lock)
        {
            var list = LoadCustomPersonas();
            var idx = list.FindIndex(p => p.Id == persona.Id);
            if (idx >= 0) list[idx] = persona;
            else list.Add(persona);
            _store.Save(_customPersonasFile, list);
        }
    }

    public bool DeleteCustomPersona(string personaId)
    {
        if (string.IsNullOrWhiteSpace(personaId)) return false;

        lock (_lock)
        {
            var list = LoadCustomPersonas();
            if (list.RemoveAll(p => p.Id == personaId) == 0) return false;
            return _store.Save(_customPersonasFile, list);
        }
    }
}

