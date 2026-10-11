using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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
    [property: JsonPropertyName("IsGathering")] bool IsGathering,
    [property: JsonPropertyName("IsAltering")] bool IsAltering,
    [property: JsonPropertyName("IsCrafting")] bool IsCrafting,
    [property: JsonPropertyName("IsPlayingInstrument")] bool IsPlayingInstrument,
    [property: JsonPropertyName("IsSitting")] bool IsSitting,
    [property: JsonPropertyName("IsOverweight")] bool IsOverweight,
    [property: JsonPropertyName("CurrentAction")] string? CurrentAction,
    [property: JsonPropertyName("CanStopCurrentAction")] bool CanStopCurrentAction,
    [property: JsonPropertyName("IsDead")] bool IsDead = false,
    [property: JsonPropertyName("AutoPlayTargetDisplayName")] string? AutoPlayTargetDisplayName = null
);

// 3. 환경 (get_current_environment)
public record EnvironmentInfo(
    [property: JsonPropertyName("ChannelDisplayName")] string? ChannelName,
    [property: JsonPropertyName("Weather")] string? Weather,
    [property: JsonPropertyName("ErinnNow")] string? ErinnNow,
    [property: JsonPropertyName("GameSpaceDisplayName")] string? GameSpaceDisplayName = null
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

public record AlteringWorkItem(
    [property: JsonPropertyName("DisplayName")] string DisplayName,
    [property: JsonPropertyName("FacilityName")] string FacilityName,
    [property: JsonPropertyName("State")] string State,
    [property: JsonPropertyName("IsCompleted")] bool IsCompleted,
    [property: JsonPropertyName("RemainingSeconds")] int RemainingSeconds
);

// 8. 채집 (get_gatherable_items)
public record GatherableItem(
    [property: JsonPropertyName("DisplayName")] string DisplayName,
    [property: JsonPropertyName("ToolOk")] bool ToolOk
);

public record GatherableResponse(
    [property: JsonPropertyName("items")] List<GatherableItem> Items
);

// 9-1. 퀘스트 트래커 (get_quests). 제목·목표 문구에 유니티 리치텍스트 태그가 섞여 온다 (K-04).
public record QuestObjective(
    [property: JsonPropertyName("Description")] string? Description,
    [property: JsonPropertyName("IsCompleted")] bool IsCompleted,
    [property: JsonPropertyName("Count")] int? Count = null,
    [property: JsonPropertyName("Goal")] int? Goal = null
);

public record QuestItem(
    [property: JsonPropertyName("QuestTitle")] string? QuestTitle,
    [property: JsonPropertyName("Source")] string? Source,
    [property: JsonPropertyName("SourceDisplayName")] string? SourceDisplayName,
    [property: JsonPropertyName("Objectives")] List<QuestObjective>? Objectives
);
