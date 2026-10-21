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
    [property: JsonPropertyName("AutoPlayTargetDisplayName")] string? AutoPlayTargetDisplayName = null,
    [property: JsonPropertyName("Dungeon")] DungeonState? Dungeon = null,
    [property: JsonPropertyName("IsReviving")] bool IsReviving = false,
    [property: JsonPropertyName("Mode")] ActivityMode? Mode = null,
    [property: JsonPropertyName("Performance")] ActivityPerformance? Performance = null,
    [property: JsonPropertyName("Interaction")] ActivityInteraction? Interaction = null
)
{
    // 2026-10-22 점검: 게임 업데이트(10-15) 뒤 get_activity에서 IsGathering·IsAltering·IsCrafting·IsPlayingInstrument·IsSitting·IsOverweight·
    // CurrentAction·CanStopCurrentAction이 없어지고 Mode·Performance·Interaction 묶음이 생겼다. 옛 필드는 항상 기본값이 되므로 새 필드로도 읽는다.

    /// <summary>지금 정지 버튼이 보이는가(stop_action이 받아들여지는 상태): 연주·의자 앉기·자동 사냥·운반·채집.</summary>
    public bool CanStop() => CanStopCurrentAction || string.Equals(Mode?.MainButtonState, "Stop", StringComparison.OrdinalIgnoreCase);

    /// <summary>악기를 연주 중인가.</summary>
    public bool IsPlayingMusic() => IsPlayingInstrument || Performance?.IsPlaying == true;

    /// <summary>의자에 앉아 있는가. /앉기로 앉은 것은 게임이 알려 주지 않는다(stand_up 설명).</summary>
    public bool IsSittingOnChair() => IsSitting || string.Equals(Mode?.SitState, "Sitting", StringComparison.OrdinalIgnoreCase);
}

/// <summary>get_activity.Mode: 메인 버튼 상태(Hide·Stop·Combat·Interaction·Compass·…)·탈것·앉기·미니게임·하우징 편집.</summary>
public record ActivityMode(
    [property: JsonPropertyName("MainButtonState")] string? MainButtonState = null,
    [property: JsonPropertyName("MountPartState")] string? MountPartState = null,
    [property: JsonPropertyName("SitState")] string? SitState = null,
    [property: JsonPropertyName("IsPlayingMiniGame")] bool IsPlayingMiniGame = false,
    [property: JsonPropertyName("IsHousingEditMode")] bool IsHousingEditMode = false);

/// <summary>get_activity.Performance: 내 악기 연주 상태.</summary>
public record ActivityPerformance(
    [property: JsonPropertyName("IsPlaying")] bool IsPlaying = false,
    [property: JsonPropertyName("InstrumentName")] string? InstrumentName = null,
    [property: JsonPropertyName("MusicTitle")] string? MusicTitle = null);

/// <summary>get_activity.Interaction: 지금 상호작용할 수 있는 것(채집·대화·던전 입구 …).</summary>
public record ActivityInteraction(
    [property: JsonPropertyName("HasTarget")] bool HasTarget = false,
    [property: JsonPropertyName("IsTargetAttackable")] bool IsTargetAttackable = false,
    [property: JsonPropertyName("AvailableInteractionType")] string? AvailableInteractionType = null,
    [property: JsonPropertyName("LastRunningInteractionType")] string? LastRunningInteractionType = null,
    [property: JsonPropertyName("TargetKind")] string? TargetKind = null);

/// <summary>
/// get_activity의 던전 상태 (실측 2026-10-12, 페카 고분 심층). State = NotInDungeon | Entering | InProgress | Cleared.
/// Cleared는 보스를 처치한 순간부터 나갈 때까지 약 17초만 유지된다. 사냥터는 던전이 아니라 NotInDungeon으로 온다.
/// </summary>
public record DungeonState(
    [property: JsonPropertyName("State")] string? State = null,
    [property: JsonPropertyName("IsBossBattleInProgress")] bool IsBossBattleInProgress = false
)
{
    public const string Cleared = "Cleared";

    /// <summary>던전 안(입장 중·진행 중)인가</summary>
    public bool IsInside => string.Equals(State, "Entering", StringComparison.OrdinalIgnoreCase) || string.Equals(State, "InProgress", StringComparison.OrdinalIgnoreCase);

    public bool IsCleared => string.Equals(State, Cleared, StringComparison.OrdinalIgnoreCase);
}

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
