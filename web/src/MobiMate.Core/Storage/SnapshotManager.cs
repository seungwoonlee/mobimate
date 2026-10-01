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
    public bool HasMissionBaseline { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string GetKey() => $"{RealmName}_{JobName}";

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
    public long AttackPower { get; set; }
    public long DefencePower { get; set; }
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
    public double WeightDiff => Current.WeightCurrent - Baseline.WeightCurrent;
    public long GoldDiff => Baseline.HasCurrencyBaseline ? (Current.Gold - Baseline.Gold) : 0;
    public long WingsDiff => Baseline.HasCurrencyBaseline ? (Current.Wings - Baseline.Wings) : 0;
    public long NyangDiff => Baseline.HasCurrencyBaseline ? (Current.NyangToken - Baseline.NyangToken) : 0;
    public int MissionDiff => Baseline.HasMissionBaseline ? (Current.CompletedDailyMissions - Baseline.CompletedDailyMissions) : 0;
}

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

        EnsureStorageDirectoryAndMigrate();
        LoadSnapshots();
        LoadCharacterDb();
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

    private void LoadSnapshots()
    {
        var list = _store.Load<List<CharacterSnapshot>>(_storageFile);
        lock (_lock)
        {
            _savedSnapshots.Clear();
            if (list == null) return;
            foreach (var s in list) _savedSnapshots[s.GetKey()] = s;
        }
    }

    private void LoadCharacterDb()
    {
        var list = _store.Load<List<CharacterProfile>>(_dbStorageFile);
        lock (_lock)
        {
            _characterDb.Clear();
            if (list == null) return;
            foreach (var p in list) _characterDb[p.CharacterKey] = p;
        }
    }

    public void SaveSnapshotsNow()
    {
        List<CharacterSnapshot> toSave;
        List<CharacterProfile> dbToSave;
        lock (_lock)
        {
            toSave = _savedSnapshots.Values.Select(s => s.Clone()).ToList();
            dbToSave = _characterDb.Values.Select(p => new CharacterProfile
            {
                CharacterKey = p.CharacterKey,
                RealmName = p.RealmName,
                JobName = p.JobName,
                CustomName = p.CustomName,
                FirstSeen = p.FirstSeen,
                LastSeen = p.LastSeen,
                History = new List<CharacterHistoryRecord>(p.History)
            }).ToList();
        }

        _store.Save(_storageFile, toSave);
        _store.Save(_dbStorageFile, dbToSave);
    }

    public void SaveSnapshotsAsync()
    {
        Task.Run(SaveSnapshotsNow);
    }

    public CharacterProfile? GetProfile(string realm, string job)
    {
        var key = $"{realm}_{job}";
        lock (_lock)
        {
            return _characterDb.TryGetValue(key, out var p) ? p : null;
        }
    }

    /// <summary>마지막으로 저장된 전투력 (서버를 다시 켠 직후 전투력 재확인의 기준, FR-DT-10). 없으면 null.</summary>
    public long? GetSavedCombat(string realm, string job)
    {
        lock (_lock)
        {
            return _savedSnapshots.TryGetValue($"{realm}_{job}", out var s) && s.CombatScore > 0 ? s.CombatScore : null;
        }
    }

    public void SetCustomName(string realm, string job, string customName)
    {
        var key = $"{realm}_{job}";
        lock (_lock)
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
            SaveSnapshotsAsync();
        }
    }

    public List<CharacterProfile> GetAllProfiles()
    {
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

        if (copied > 0)
        {
            LoadSnapshots();
            LoadCharacterDb();
        }
        return copied;
    }

    public SessionDelta UpdateSnapshot(
        CharacterInfo? ch,
        List<CurrencyItem>? currencies,
        List<MissionItem>? dailyMissions)
    {
        var realm = string.IsNullOrWhiteSpace(ch?.RealmName) ? "에린" : ch.RealmName;
        var job = string.IsNullOrWhiteSpace(ch?.JobName) ? "밀레시안" : ch.JobName;
        var level = ch?.Level ?? 1;
        var title = ch?.Title ?? "";
        var combatScore = ch?.CombatScore?.Value ?? 0;
        var mdef = ch?.ArcaneResistance?.Value ?? 0;
        var weight = ch?.Vitals?.WeightCurrent ?? 0.0;

        var key = $"{realm}_{job}";

        lock (_lock)
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
            current.Level = level;
            current.Title = title;
            current.CombatScore = combatScore;
            current.ArcaneResistance = mdef;
            current.WeightCurrent = weight;
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
                    WeightCurrent = weight,
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
            }

            _savedSnapshots[key] = current;

            // 3. 캐릭터 프로필 및 누적 히스토리 DB 갱신
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
            }
            profile.LastSeen = DateTime.Now;

            // 히스토리 중복 방지 (수치 변동 또는 최소 5분 경과 시 누적 기록)
            var lastRecord = profile.History.LastOrDefault();
            bool shouldRecord = lastRecord == null ||
                (DateTime.Now - lastRecord.Timestamp).TotalMinutes >= 5 ||
                lastRecord.CombatScore != current.CombatScore ||
                lastRecord.ArcaneResistance != current.ArcaneResistance ||
                lastRecord.Level != current.Level ||
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
                    LivingScore = ch?.LivingScore?.Value ?? 0,
                    AttackPower = ch?.AttackPower?.Value ?? 0,
                    DefencePower = ch?.DefencePower?.Value ?? 0,
                    WeightCurrent = current.WeightCurrent,
                    Gold = current.Gold,
                    Wings = current.Wings,
                    NyangToken = current.NyangToken,
                    CompletedDailyMissions = current.CompletedDailyMissions
                });
            }

            SaveSnapshotsAsync();

            return new SessionDelta(baseline, current);
        }
    }

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

