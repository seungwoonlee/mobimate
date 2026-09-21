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
    private static readonly string StorageFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "character_snapshots.json");
    private readonly Dictionary<string, CharacterSnapshot> _savedSnapshots = new();
    private readonly Dictionary<string, CharacterSnapshot> _sessionBaselines = new();
    private readonly object _lock = new();

    public SnapshotManager()
    {
        LoadSnapshots();
    }

    private void LoadSnapshots()
    {
        try
        {
            if (File.Exists(StorageFile))
            {
                var json = File.ReadAllText(StorageFile);
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
        }
        catch
        {
            // 손상된 파일 등의 경우 기본값 유지
        }
    }

    public void SaveSnapshotsAsync()
    {
        List<CharacterSnapshot> toSave;
        lock (_lock)
        {
            toSave = _savedSnapshots.Values.Select(s => s.Clone()).ToList();
        }

        Task.Run(() =>
        {
            try
            {
                var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
                var tempFile = StorageFile + ".tmp";
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, StorageFile, overwrite: true);
            }
            catch
            {
                // 백그라운드 I/O 예외 무해 처리
            }
        });
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
}
