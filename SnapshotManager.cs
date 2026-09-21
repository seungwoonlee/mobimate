using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string GetKey() => $"{RealmName}_{JobName}";
}

public class SessionDelta
{
    public CharacterSnapshot Baseline { get; set; }
    public CharacterSnapshot Current { get; set; }

    public SessionDelta(CharacterSnapshot baseline, CharacterSnapshot current)
    {
        Baseline = baseline;
        Current = current;
    }

    public long CombatScoreDiff => Current.CombatScore - Baseline.CombatScore;
    public double WeightDiff => Current.WeightCurrent - Baseline.WeightCurrent;
    public long GoldDiff => Current.Gold - Baseline.Gold;
    public long WingsDiff => Current.Wings - Baseline.Wings;
    public long NyangDiff => Current.NyangToken - Baseline.NyangToken;
    public int MissionDiff => Current.CompletedDailyMissions - Baseline.CompletedDailyMissions;
}

public class SnapshotManager
{
    private static readonly string StorageFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "character_snapshots.json");
    private readonly Dictionary<string, CharacterSnapshot> _savedSnapshots = new();
    private readonly Dictionary<string, CharacterSnapshot> _sessionBaselines = new();

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
                    foreach (var s in list)
                    {
                        _savedSnapshots[s.GetKey()] = s;
                    }
                }
            }
        }
        catch { }
    }

    public void SaveSnapshots()
    {
        try
        {
            var json = JsonSerializer.Serialize(_savedSnapshots.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorageFile, json);
        }
        catch { }
    }

    public SessionDelta UpdateSnapshot(
        CharacterInfo? ch,
        List<CurrencyItem>? currencies,
        List<MissionItem>? dailyMissions)
    {
        var realm = ch?.RealmName ?? "에린";
        var job = ch?.JobName ?? "밀레시안";
        var level = ch?.Level ?? 1;
        var title = ch?.Title ?? "";
        var combatScore = ch?.CombatScore?.Value ?? 0;
        var weight = ch?.Vitals?.WeightCurrent ?? 0.0;

        long gold = 0, wings = 0, nyang = 0;
        if (currencies != null)
        {
            foreach (var c in currencies)
            {
                if (c.DisplayName == "골드") gold = c.Amount;
                else if (c.DisplayName == "정령의 날개") wings = c.Amount;
                else if (c.DisplayName == "냥 토큰") nyang = c.Amount;
            }
        }

        int completedMissions = dailyMissions != null ? dailyMissions.Count(m => m.IsCompleted) : 0;

        var current = new CharacterSnapshot
        {
            RealmName = realm,
            JobName = job,
            Level = level,
            Title = title,
            CombatScore = combatScore,
            WeightCurrent = weight,
            Gold = gold,
            Wings = wings,
            NyangToken = nyang,
            CompletedDailyMissions = completedMissions,
            Timestamp = DateTime.Now
        };

        var key = current.GetKey();

        // 1. 이번 앱 기동 후 최초 세션 베이스라인 고정
        if (!_sessionBaselines.ContainsKey(key))
        {
            // 이전에 저장된 스냅샷이 있으면 그것을 참고하거나 현재를 베이스라인으로 설정
            _sessionBaselines[key] = current;
        }

        var baseline = _sessionBaselines[key];
        _savedSnapshots[key] = current;
        SaveSnapshots();

        return new SessionDelta(baseline, current);
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
