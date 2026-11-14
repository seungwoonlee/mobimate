using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace MobiMate;

// 1. 캐릭터 상세 정보 (get_my_info)
public record CharacterInfo(
    [property: JsonPropertyName("Title")] string? Title,
    [property: JsonPropertyName("RealmName")] string? RealmName,
    [property: JsonPropertyName("Level")] int Level,
    [property: JsonPropertyName("EnabledCombatJobDisplayName")] string? JobName,
    [property: JsonPropertyName("CombatScore")] ScoreVal? CombatScore,
    [property: JsonPropertyName("LivingScore")] ScoreVal? LivingScore,
    [property: JsonPropertyName("AttractivenessScore")] ScoreVal? AttractivenessScore,
    [property: JsonPropertyName("DecorScore")] ScoreVal? DecorScore,
    [property: JsonPropertyName("HealthMax")] ScoreVal? HealthMax,
    [property: JsonPropertyName("AttackPower")] ScoreVal? AttackPower,
    [property: JsonPropertyName("DefencePower")] ScoreVal? DefencePower,
    [property: JsonPropertyName("ArcaneResistance")] ScoreVal? ArcaneResistance,
    [property: JsonPropertyName("STR")] ScoreVal? STR,
    [property: JsonPropertyName("DEX")] ScoreVal? DEX,
    [property: JsonPropertyName("INT")] ScoreVal? INT,
    [property: JsonPropertyName("LUCK")] ScoreVal? LUCK,
    [property: JsonPropertyName("WILL")] ScoreVal? WILL,
    [property: JsonPropertyName("PaladinStats")] PaladinStatsInfo? PaladinStats,
    [property: JsonPropertyName("Vitals")] VitalsInfo? Vitals
);

public record ScoreVal([property: JsonPropertyName("DisplayName")] string? DisplayName, [property: JsonPropertyName("Value")] long Value);

public record PaladinStatsInfo(
    [property: JsonPropertyName("PaladinAttackPower")] ScoreVal? PaladinAttackPower,
    [property: JsonPropertyName("PaladinDefencePower")] ScoreVal? PaladinDefencePower,
    [property: JsonPropertyName("JusticePower")] ScoreVal? JusticePower,
    [property: JsonPropertyName("JudgementPower")] ScoreVal? JudgementPower,
    [property: JsonPropertyName("OrderPower")] ScoreVal? OrderPower,
    [property: JsonPropertyName("BlessingPower")] ScoreVal? BlessingPower
);

public record VitalsInfo(
    [property: JsonPropertyName("HealthCurrent")] double HealthCurrent,
    [property: JsonPropertyName("HealthMax")] double HealthMax,
    [property: JsonPropertyName("InventoryWeightCurrent")] double WeightCurrent,
    [property: JsonPropertyName("InventoryWeightMax")] double WeightMax,
    [property: JsonPropertyName("SatietyValue")] double SatietyValue,
    [property: JsonPropertyName("SatietyMax")] double SatietyMax,
    [property: JsonPropertyName("ActiveBuffCount")] int ActiveBuffCount
);

// 2. 활동 상태 (get_activity)
public record ActivityInfo(
    [property: JsonPropertyName("IsAutoPlaying")] bool IsAutoPlaying,
    [property: JsonPropertyName("IsAutoTraveling")] bool IsAutoTraveling,
    [property: JsonPropertyName("IsInCombat")] bool IsInCombat,
    [property: JsonPropertyName("AutoPlayTargetDisplayName")] string? AutoPlayTargetDisplayName,
    [property: JsonPropertyName("IsGathering")] bool IsGathering,
    [property: JsonPropertyName("IsAltering")] bool IsAltering,
    [property: JsonPropertyName("IsCrafting")] bool IsCrafting,
    [property: JsonPropertyName("IsPlayingInstrument")] bool IsPlayingInstrument,
    [property: JsonPropertyName("IsSitting")] bool IsSitting,
    [property: JsonPropertyName("IsOverweight")] bool IsOverweight,
    [property: JsonPropertyName("CurrentAction")] string? CurrentAction,
    [property: JsonPropertyName("CanStopCurrentAction")] bool CanStopCurrentAction
);

// 3. 환경 (get_current_environment)
public record EnvironmentInfo(
    [property: JsonPropertyName("ChannelDisplayName")] string? ChannelName,
    [property: JsonPropertyName("GameSpaceDisplayName")] string? GameSpaceDisplayName,
    [property: JsonPropertyName("Weather")] string? Weather,
    [property: JsonPropertyName("ErinnNow")] string? ErinnNow
);

// 4. 재화 (get_currencies)
public record CurrencyItem(
    [property: JsonPropertyName("DisplayName")] string DisplayName,
    [property: JsonPropertyName("Amount")] long Amount
);

// 5. 아이템 (get_items)
public record ItemData(
    [property: JsonPropertyName("Location")] string Location,
    [property: JsonPropertyName("DisplayName")] string DisplayName,
    [property: JsonPropertyName("CategoryDisplayName")] string CategoryName,
    [property: JsonPropertyName("Count")] int Count,
    [property: JsonPropertyName("IsLocked")] bool IsLocked
);

// 6. 미션 (get_daily_missions, get_weekly_missions)
public record MissionItem(
    [property: JsonPropertyName("Title")] string Title,
    [property: JsonPropertyName("Description")] string Description,
    [property: JsonPropertyName("CurrentCount")] int CurrentCount,
    [property: JsonPropertyName("GoalCount")] int GoalCount,
    [property: JsonPropertyName("IsCompleted")] bool IsCompleted,
    [property: JsonPropertyName("IsRewardReceived")] bool IsRewardReceived
);

// 7. 가공 작업대 (get_altering_works)
public record AlteringWorksResponse(
    [property: JsonPropertyName("completedCount")] int CompletedCount,
    [property: JsonPropertyName("works")] List<AlteringWorkItem>? Works
);

public static class BrushHelper
{
    public static readonly SolidColorBrush Red = CreateFrozenBrush(0xFF, 0x6B, 0x6B);
    public static readonly SolidColorBrush Gold = CreateFrozenBrush(0xF5, 0xD0, 0x61);
    public static readonly SolidColorBrush Green = CreateFrozenBrush(0x4E, 0xBA, 0x6F);
    public static readonly SolidColorBrush Gray = CreateFrozenBrush(0x8E, 0x92, 0x97);

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public static class TimeFormatHelper
{
    public static string FormatRemainingTime(int totalSeconds)
    {
        if (totalSeconds <= 0) return "수거 대기 ✅";
        var t = TimeSpan.FromSeconds(totalSeconds);
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}시간 {t.Minutes}분 {t.Seconds}초";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}분 {t.Seconds}초";
        return $"{t.Seconds}초";
    }
}

public record AlteringWorkItem(
    [property: JsonPropertyName("DisplayName")] string DisplayName,
    [property: JsonPropertyName("FacilityName")] string FacilityName,
    [property: JsonPropertyName("State")] string State,
    [property: JsonPropertyName("IsCompleted")] bool IsCompleted,
    [property: JsonPropertyName("RemainingSeconds")] int RemainingSeconds
)
{
    public bool IsDone => IsCompleted || RemainingSeconds == 0;
    public string RemainingFormatted => TimeFormatHelper.FormatRemainingTime(RemainingSeconds);
    public string StatusText => IsDone ? "수거 대기 ✅" : $"{RemainingFormatted} 남음 ⏳";
    public string StatusColor => IsDone ? "#4EBA6F" : "#F5D061";
    public Brush StatusBrush => IsDone ? BrushHelper.Green : BrushHelper.Gold;
}

// 8. 채집 (get_gatherable_items)
public record GatherableItem(
    [property: JsonPropertyName("DisplayName")] string DisplayName,
    [property: JsonPropertyName("ToolOk")] bool ToolOk
);

public record GatherableResponse(
    [property: JsonPropertyName("items")] List<GatherableItem> Items
);

// 9. 주변 유저 (get_near_pcs)
public record NearPcItem(
    [property: JsonPropertyName("RealmName")] string RealmName,
    [property: JsonPropertyName("Title")] string? Title,
    [property: JsonPropertyName("Distance")] double Distance,
    [property: JsonPropertyName("Level")] int Level,
    [property: JsonPropertyName("EnabledCombatJobDisplayName")] string JobName,
    [property: JsonPropertyName("CombatScore")] long CombatScore,
    [property: JsonPropertyName("HasGuild")] bool HasGuild,
    [property: JsonPropertyName("IsSameGuild")] bool IsSameGuild,
    [property: JsonPropertyName("IsInParty")] bool IsInParty,
    [property: JsonPropertyName("IsFriend")] bool IsFriend,
    [property: JsonPropertyName("IsInCombat")] bool IsInCombat
);

// 10. 로그 및 메시지
public record ChatLogEntry(string Timestamp, string Message, bool IsSuccess, string? ErrorDetail = null);
public record AiMessageEntry(string Sender, string Text, bool IsUser);

// 11. UI 바인딩용 뷰모델
public class ItemViewItem
{
    public string Location { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int Count { get; set; }
    public bool IsLocked { get; set; }
    public int InitialCount { get; set; }
    public int DeltaCount => Math.Max(0, Count - InitialCount);
    public bool HasDelta => DeltaCount > 0;
    public string DeltaBadge => HasDelta ? $"(+{DeltaCount:N0}개 급증 ▲)" : "";
}

public class GatherableDisplayItem
{
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "채집";
    public bool ToolOk { get; set; }
    public int CurrentBagCount { get; set; }
    public int TargetCount { get; set; }
    public int NeededCount => Math.Max(0, TargetCount - CurrentBagCount);
    public string ProgressText => $"보유 {CurrentBagCount} / 목표 {TargetCount} (부족 {NeededCount}개)";
    public bool IsTargetReached => NeededCount == 0;
}

public class NearPcViewItem
{
    private NearPcItem _raw = null!;
    public NearPcItem Raw
    {
        get => _raw;
        set
        {
            _raw = value;
            if (value != null)
            {
                Distance = value.Distance;
                Level = value.Level;
                JobName = value.JobName;
                CombatScore = value.CombatScore;
                RealmName = value.RealmName;
                Title = value.Title;
                HasGuild = value.HasGuild;
                IsSameGuild = value.IsSameGuild;
                IsInParty = value.IsInParty;
                IsFriend = value.IsFriend;
                IsInCombat = value.IsInCombat;
            }
        }
    }

    public double Distance { get; set; }
    public int Level { get; set; }
    public string JobName { get; set; } = "";
    public string JobIcon { get; set; } = "⭐";
    public long CombatScore { get; set; }
    public string RealmName { get; set; } = "";
    public string? Title { get; set; }
    public bool HasGuild { get; set; }
    public bool IsSameGuild { get; set; }
    public bool IsInParty { get; set; }
    public bool IsFriend { get; set; }
    public bool IsInCombat { get; set; }
    public bool IsStronger { get; set; }

    public string JobDisplay => $"{JobIcon} {JobName}";
    public string JobWithLevel => $"{JobIcon} {JobName} Lv.{Level}";

    public string LevelWithGuild
    {
        get
        {
            var baseText = $"Lv.{Level}";
            if (IsInParty) return $"{baseText} [파티원 👥]";
            if (IsFriend) return $"{baseText} [친구 💖]";
            if (IsSameGuild) return $"{baseText} [우리 길드원 🛡️]";
            if (!HasGuild) return $"{baseText} (길드 없음)";
            return baseText;
        }
    }

    public string CombatScoreFormatted => IsInCombat ? $"{CombatScore:N0} (전투 중 ⚔️)" : $"{CombatScore:N0}";
    public string GuildBadge => IsSameGuild ? "🛡️ 우리 길드원" : "";
    public string GuildBadgeColor => IsSameGuild ? "#4EBA6F" : "#8E9297";
    public Brush GuildBadgeBrush => IsSameGuild ? BrushHelper.Green : BrushHelper.Gray;
    public string StrongerBadge => IsStronger ? "⚔️ 강력 (나보다 높음)" : "";
    public string CombatScoreColor => IsInCombat ? "#FF6B6B" : (IsStronger ? "#FF6B6B" : "#F5D061");
    public Brush CombatScoreBrush => IsInCombat ? BrushHelper.Red : (IsStronger ? BrushHelper.Red : BrushHelper.Gold);
    public string TitleWithRealm => string.IsNullOrEmpty(Title) ? $"[{RealmName}]" : $"[{Title}] ({RealmName})";

    public static NearPcViewItem FromRaw(NearPcItem p, string jobIcon, bool isStronger = false)
    {
        return new NearPcViewItem
        {
            Raw = p,
            JobIcon = jobIcon,
            IsStronger = isStronger
        };
    }
}

public class CurrencyCategoryGroup
{
    public string CategoryName { get; set; } = "";
    public List<CurrencyItem> Items { get; set; } = new();
}

// 12. 퀘스트 (get_quests)
public record QuestObjective(
    [property: JsonPropertyName("Description")] string Description,
    [property: JsonPropertyName("IsCompleted")] bool IsCompleted
);

public record QuestItem(
    [property: JsonPropertyName("QuestTitle")] string QuestTitle,
    [property: JsonPropertyName("Source")] string Source,
    [property: JsonPropertyName("SourceDisplayName")] string SourceDisplayName,
    [property: JsonPropertyName("Objectives")] List<QuestObjective> Objectives
);

