using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MobiMate
{
    public enum HomeworkCategory
    {
        All,
        Daily,
        Weekly,
        FieldBoss,
        Abyss,
        Raid,
        Shop
    }

    public enum HomeworkPeriod
    {
        Daily,
        Weekly
    }

    public enum HomeworkShareType
    {
        Character,
        Account
    }

    public enum AutoDetectMode
    {
        DirectMission,       // 일일/주간 미션 1:1 직결
        AlteringFacility,    // 가공대 대기열/수거 직결
        EnvironmentCombat,   // 공간명 + 보스 전투 타겟 감지
        QuestTracker,        // 레이드/가이드 퀘스트 트래커 감지
        CurrencyDelta,       // 재화/아이템 획득 델타 감지
        ManualOnly           // 외부 웹/캐시샵 등 수동 체크 전용
    }

    public class HomeworkDefinition
    {
        public string Id { get; set; } = string.Empty;
        public HomeworkCategory Category { get; set; }
        public HomeworkPeriod Period { get; set; }
        public HomeworkShareType ShareType { get; set; }
        public AutoDetectMode AutoMode { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Icon { get; set; } = "📋";
        public int GoalCount { get; set; } = 1;
        public string RewardSummary { get; set; } = string.Empty;
        public List<string> MatchKeywords { get; set; } = new();

        public string CategoryDisplayName => Category switch
        {
            HomeworkCategory.Daily => "일일숙제",
            HomeworkCategory.Weekly => "주간숙제",
            HomeworkCategory.FieldBoss => "필드보스",
            HomeworkCategory.Abyss => "어비스",
            HomeworkCategory.Raid => "레이드",
            HomeworkCategory.Shop => "상점/교환",
            _ => "기타"
        };

        public string PeriodDisplayName => Period switch
        {
            HomeworkPeriod.Daily => "매일 06시 리셋",
            HomeworkPeriod.Weekly => "매주 목 06시 리셋",
            _ => ""
        };
    }

    public class HomeworkItemState
    {
        public string Id { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public int CurrentCount { get; set; }
        public int GoalCount { get; set; } = 1;
        public DateTime? CompletedAt { get; set; }
        public bool IsAutoDetected { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    public class HomeworkCharacterRecord
    {
        public string CharacterKey { get; set; } = string.Empty;
        public DateTime LastDailyReset { get; set; }
        public DateTime LastWeeklyReset { get; set; }
        public Dictionary<string, HomeworkItemState> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class HomeworkItemViewItem
    {
        public string Id { get; set; } = string.Empty;
        public HomeworkCategory Category { get; set; }
        public string CategoryDisplayName { get; set; } = string.Empty;
        public HomeworkPeriod Period { get; set; }
        public string PeriodDisplayName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Icon { get; set; } = "📋";
        public bool IsCompleted { get; set; }
        public int CurrentCount { get; set; }
        public int GoalCount { get; set; } = 1;
        public string ProgressText => $"{CurrentCount}/{GoalCount}";
        public bool IsAutoDetected { get; set; }
        public string DetectionBadge => IsCompleted
            ? (IsAutoDetected ? "⚡ 자동 감지 완료" : "✍️ 수동 완료")
            : "진행 필요";
        public string DetectionBadgeBrush => IsCompleted
            ? (IsAutoDetected ? "#22C55E" : "#3B82F6")
            : "#94A3B8";
        public string CardBackground => IsCompleted ? "#1E293B" : "#0F172A";
        public string CardOpacity => IsCompleted ? "0.6" : "1.0";
    }
}
