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

    public string StorageDirectory => _storageDir;
    public string SnapshotsFilePath => _storageFile;
    public string CharacterDbFilePath => _dbStorageFile;
    public string CustomPersonasFilePath => _customPersonasFile;

    public SnapshotManager(string? customStorageDir = null)
    {
        _storageDir = !string.IsNullOrWhiteSpace(customStorageDir)
            ? customStorageDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MobiMate");

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
        lock (_lock)
        {
            _savedSnapshots.Clear();
        }

        try
        {
            if (!File.Exists(_storageFile)) return;

            var fi = new FileInfo(_storageFile);
            if (fi.Length == 0)
            {
                QuarantineCorruptedFile(_storageFile);
                return;
            }

            var json = File.ReadAllText(_storageFile);
            var list = JsonSerializer.Deserialize<List<CharacterSnapshot>>(json);
            if (list != null)
            {
                lock (_lock)
                {
                    foreach (var s in list)
                    {
                        _savedSnapshots[s.GetKey()] = s;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // JSON 손상 시 안전하게 격리 후 빈 상태로 신규 복구
            QuarantineCorruptedFile(_storageFile);
        }
        catch
        {
            // 기타 I/O 오류 무해 처리 (인메모리 모드로 안전 가동)
        }
    }

    private void LoadCharacterDb()
    {
        lock (_lock)
        {
            _characterDb.Clear();
        }

        try
        {
            if (!File.Exists(_dbStorageFile)) return;

            var fi = new FileInfo(_dbStorageFile);
            if (fi.Length == 0)
            {
                QuarantineCorruptedFile(_dbStorageFile);
                return;
            }

            var json = File.ReadAllText(_dbStorageFile);
            var list = JsonSerializer.Deserialize<List<CharacterProfile>>(json);
            if (list != null)
            {
                lock (_lock)
                {
                    foreach (var p in list)
                    {
                        _characterDb[p.CharacterKey] = p;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // JSON 손상 시 안전하게 격리 후 빈 상태로 신규 복구
            QuarantineCorruptedFile(_dbStorageFile);
        }
        catch
        {
            // 기타 I/O 오류 무해 처리
        }
    }

    private static void QuarantineCorruptedFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var bak = $"{filePath}.corrupted.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                File.Move(filePath, bak, overwrite: true);
            }
        }
        catch
        {
            // 격리 이동 실패 시에도 크래시를 방지하고 계속 진행
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

        try
        {
            if (!Directory.Exists(_storageDir))
            {
                Directory.CreateDirectory(_storageDir);
            }

            var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
            var tempFile = _storageFile + $".tmp.{Guid.NewGuid():N}";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _storageFile, overwrite: true);

            var dbJson = JsonSerializer.Serialize(dbToSave, new JsonSerializerOptions { WriteIndented = true });
            var dbTempFile = _dbStorageFile + $".tmp.{Guid.NewGuid():N}";
            File.WriteAllText(dbTempFile, dbJson);
            File.Move(dbTempFile, _dbStorageFile, overwrite: true);
        }
        catch
        {
            // I/O 예외 무해 처리
        }
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
        lock (_lock)
        {
            try
            {
                if (File.Exists(_customPersonasFile))
                {
                    var json = File.ReadAllText(_customPersonasFile);
                    var list = JsonSerializer.Deserialize<List<CustomPersona>>(json);
                    return list ?? new List<CustomPersona>();
                }
            }
            catch { }
            return new List<CustomPersona>();
        }
    }

    public void SaveCustomPersona(CustomPersona persona)
    {
        if (persona == null || string.IsNullOrWhiteSpace(persona.Name)) return;

        lock (_lock)
        {
            try
            {
                var list = LoadCustomPersonas();
                var idx = list.FindIndex(p => p.Id == persona.Id);
                if (idx >= 0)
                {
                    list[idx] = persona;
                }
                else
                {
                    list.Add(persona);
                }

                if (!Directory.Exists(_storageDir))
                {
                    Directory.CreateDirectory(_storageDir);
                }

                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_customPersonasFile, json);
            }
            catch { }
        }
    }

    public bool DeleteCustomPersona(string personaId)
    {
        if (string.IsNullOrWhiteSpace(personaId)) return false;

        lock (_lock)
        {
            try
            {
                var list = LoadCustomPersonas();
                var removed = list.RemoveAll(p => p.Id == personaId);
                if (removed > 0)
                {
                    var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_customPersonasFile, json);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}

