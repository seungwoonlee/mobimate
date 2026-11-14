using System;
using System.Collections.Generic;
using System.Linq;

namespace MobiMate;

/// <summary>
/// 각 난이도별 상세 기준 스펙
/// </summary>
public class DungeonCutoffTier
{
    public string TierName { get; set; } = "";
    public long MinEntryCombat { get; set; }        // 입장 최소 전투력 (미달 시 입장 불가)
    public long RecommendedCombat { get; set; }     // 권장 전투력
    public long OverwhelmCombat { get; set; }       // 압도 전투력
    public long MinEntryMdef { get; set; }          // 입장(요구) 마도저항 (미달 시 입장 불가)
    public long OverwhelmMdef { get; set; }         // 압도 마도저항
    public string Description { get; set; } = "";

    public DungeonCutoffTier(string tierName, long minCombat, long reqCombat, long overCombat, long minMdef, long overMdef, string desc = "")
    {
        TierName = tierName;
        MinEntryCombat = minCombat;
        RecommendedCombat = reqCombat;
        OverwhelmCombat = overCombat;
        MinEntryMdef = minMdef;
        OverwhelmMdef = overMdef;
        Description = desc;
    }

    /// <summary>
    /// 최소 입장 가능 여부 (전투력과 마도저항 둘 다 충족 필수)
    /// </summary>
    public bool CanEnter(long combat, long mdef) => combat >= MinEntryCombat && mdef >= MinEntryMdef;

    /// <summary>
    /// 완전 압도 달성 여부 (전투력과 마도저항 모두 압도치 도달)
    /// </summary>
    public bool IsOverwhelmed(long combat, long mdef) => combat >= OverwhelmCombat && (OverwhelmMdef == 0 || mdef >= OverwhelmMdef);

    /// <summary>
    /// 압도에 살짝 모자란 근접 상태 판정 (권장치 이상이며 압도 기준 92% 이상 또는 투력 5,000 / 저항 300 이내)
    /// </summary>
    public bool IsNearOverwhelm(long combat, long mdef, out long combatShortage, out long mdefShortage)
    {
        combatShortage = Math.Max(0, OverwhelmCombat - combat);
        mdefShortage = OverwhelmMdef > 0 ? Math.Max(0, OverwhelmMdef - mdef) : 0;

        if (IsOverwhelmed(combat, mdef)) return false;
        if (!CanEnter(combat, mdef) || combat < RecommendedCombat) return false;

        bool combatNear = combatShortage <= 5000 || ((double)combat / OverwhelmCombat >= 0.92);
        bool mdefNear = OverwhelmMdef == 0 || mdefShortage <= 300 || ((double)mdef / OverwhelmMdef >= 0.85);

        return combatNear && mdefNear;
    }
}

/// <summary>
/// 4대 콘텐츠 정의 (어비스, 화서큐, 에이렐, 카브락)
/// </summary>
public class DungeonContentDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<DungeonCutoffTier> Tiers { get; set; } = new();
}

/// <summary>
/// 콘텐츠별 맞춤 추천 카드 뷰모델
/// </summary>
public class ContentRecommendationViewModel
{
    public string ContentId { get; set; } = "";
    public string ContentName { get; set; } = "";
    public string Icon { get; set; } = "";

    // 1. 최고 입장 가능 난이도
    public string MaxEntryTier { get; set; } = "입장 불가";
    public string MaxEntryTierBadgeBg { get; set; } = "#1C2E46";
    public string MaxEntryTierBadgeFg { get; set; } = "#58A6FF";

    // 2. 맞춤 추천 난이도 & 상태
    public string RecommendedTier { get; set; } = "-";
    public string RecommendedStatusBadge { get; set; } = "";
    public string RecommendedStatusBg { get; set; } = "#2E1C48";
    public string RecommendedStatusFg { get; set; } = "#D2A8FF";
    public string RecommendedStatusBorder { get; set; } = "#9B59B6";

    // 3. 상태 및 모자란 수치 상세 가이드 (2행)
    public string StatusDetailText { get; set; } = "";

    // 4. 상위 난이도 도전 가이드 (3행)
    public string NextTierGuideText { get; set; } = "";
    public string NextTierGuideFg { get; set; } = "#39D353";
}

public static class DungeonCutoffService
{
    /// <summary>
    /// 承雲의 인게임 실측 데이터 100% 동기화 마스터 리스트 (사족 서브타이틀 전면 제거)
    /// </summary>
    public static List<DungeonContentDefinition> MasterContents { get; } = new()
    {
        // 1. 어비스 - 총 6단계
        new DungeonContentDefinition
        {
            Id = "abyss",
            Name = "어비스",
            Icon = "🌀",
            Tiers = new List<DungeonCutoffTier>
            {
                new("입문", 50000, 56000, 64500, 0, 1000),
                new("어려움", 63000, 66000, 75000, 1000, 1600),
                new("매우 어려움", 76000, 80000, 92000, 2200, 2700),
                new("지옥 1", 87500, 92000, 105000, 3500, 4400),
                new("지옥 2", 95000, 100000, 115000, 6000, 6600),
                new("지옥 3", 102500, 108000, 124000, 7200, 7800)
            }
        },

        // 2. 화이트 서큐버스 - 총 2단계
        new DungeonContentDefinition
        {
            Id = "white_succubus",
            Name = "화이트 서큐버스",
            Icon = "👑",
            Tiers = new List<DungeonCutoffTier>
            {
                new("어려움", 0, 27000, 31100, 0, 0),
                new("매우 어려움", 50000, 57500, 64000, 0, 0)
            }
        },

        // 3. 에이렐 - 총 2단계
        new DungeonContentDefinition
        {
            Id = "airel",
            Name = "에이렐",
            Icon = "❄️",
            Tiers = new List<DungeonCutoffTier>
            {
                new("어려움", 43500, 50000, 57500, 0, 0),
                new("매우 어려움", 88500, 93000, 107000, 3000, 3500)
            }
        },

        // 4. 카브락 - 총 2단계
        new DungeonContentDefinition
        {
            Id = "cavrak",
            Name = "카브락",
            Icon = "🐲",
            Tiers = new List<DungeonCutoffTier>
            {
                new("입문", 65000, 72000, 82500, 2000, 2500),
                new("어려움", 90000, 95000, 109000, 3100, 3700)
            }
        }
    };

    /// <summary>
    /// 단일 콘텐츠에 대한 최고 입장 가능 난이도 및 맞춤 추천 평가
    /// </summary>
    public static ContentRecommendationViewModel EvaluateContent(DungeonContentDefinition content, long combat, long mdef)
    {
        var vm = new ContentRecommendationViewModel
        {
            ContentId = content.Id,
            ContentName = content.Name,
            Icon = content.Icon
        };

        // 1. 최고 입장 가능 난이도 탐색
        var maxEnterableTier = content.Tiers.LastOrDefault(t => t.CanEnter(combat, mdef));
        if (maxEnterableTier != null)
        {
            vm.MaxEntryTier = maxEnterableTier.TierName;
            vm.MaxEntryTierBadgeBg = "#1C2E46";
            vm.MaxEntryTierBadgeFg = "#58A6FF";
        }
        else
        {
            var firstTier = content.Tiers.First();
            long entryCombatLack = Math.Max(0, firstTier.MinEntryCombat - combat);
            long entryMdefLack = Math.Max(0, firstTier.MinEntryMdef - mdef);
            vm.MaxEntryTier = "입장 불가";
            vm.MaxEntryTierBadgeBg = "#2D1D24";
            vm.MaxEntryTierBadgeFg = "#F85149";
            vm.RecommendedTier = "입장 불가";
            vm.RecommendedStatusBadge = "🔒 불가";
            vm.RecommendedStatusBg = "#2D1D24";
            vm.RecommendedStatusFg = "#F85149";
            vm.RecommendedStatusBorder = "#DA3633";

            var reasons = new List<string>();
            if (entryCombatLack > 0) reasons.Add($"투력 {entryCombatLack:N0} 부족");
            if (entryMdefLack > 0) reasons.Add($"저항 {entryMdefLack:N0} 부족");
            vm.StatusDetailText = $"{firstTier.TierName} 기준 {string.Join(", ", reasons)}";
            vm.NextTierGuideText = $"💡 {firstTier.TierName} 단계 입장 스펙 달성 필요";
            vm.NextTierGuideFg = "#FF7B72";
            return vm;
        }

        // 2. 추천 난이도 탐색
        // 承雲 원칙:
        // (1) 마도저항 무관용: 압도 마도저항(OverwhelmMdef)을 100% 충족해야만 추천 가능. 단 1이라도 부족하면 추천단계 강등!
        // (2) 전투력 압도 10% 추천 허용: 권장 전투력 이상이고 압도 전투력의 90% 이상(combat >= (OverwhelmCombat * 9) / 10)이면 추천 가능.
        var enterableTiers = content.Tiers.Where(t => t.CanEnter(combat, mdef)).ToList();

        var qualifyingTiers = enterableTiers.Where(t =>
            (t.OverwhelmMdef == 0 || mdef >= t.OverwhelmMdef) &&
            (combat >= t.RecommendedCombat && combat >= (t.OverwhelmCombat * 9) / 10)
        ).ToList();

        DungeonCutoffTier targetTier;
        if (qualifyingTiers.Count > 0)
        {
            targetTier = qualifyingTiers.Last();
        }
        else
        {
            // 마도저항 압도가 충족되는 하위 난이도 중 최상위 탐색
            var safeMdefTiers = enterableTiers.Where(t => t.OverwhelmMdef == 0 || mdef >= t.OverwhelmMdef).ToList();
            if (safeMdefTiers.Count > 0)
            {
                targetTier = safeMdefTiers.Last();
            }
            else
            {
                targetTier = maxEnterableTier;
            }
        }

        vm.RecommendedTier = targetTier.TierName;

        // 3. 2행 상태 텍스트 (StatusDetailText)
        // 承雲 원칙: 압도전투력의 몇%라고 표시, 얼마의 전투력을 더 채워야 압도가 된다고 출력
        int pct = targetTier.OverwhelmCombat > 0
            ? Math.Min(99, (int)((double)combat / targetTier.OverwhelmCombat * 100))
            : 100;
        long cLack = Math.Max(0, targetTier.OverwhelmCombat - combat);

        if (targetTier.IsOverwhelmed(combat, mdef))
        {
            vm.RecommendedStatusBadge = "⚡ 압도";
            vm.RecommendedStatusBg = "#2E1C48";
            vm.RecommendedStatusFg = "#D2A8FF";
            vm.RecommendedStatusBorder = "#9B59B6";

            vm.StatusDetailText = "⚡ 압도 달성";
        }
        else if (combat >= (targetTier.OverwhelmCombat * 9) / 10 && (targetTier.OverwhelmMdef == 0 || mdef >= targetTier.OverwhelmMdef))
        {
            vm.RecommendedStatusBadge = "🔥 압도 근접";
            vm.RecommendedStatusBg = "#3B2A14";
            vm.RecommendedStatusFg = "#F5D061";
            vm.RecommendedStatusBorder = "#E5A93C";

            vm.StatusDetailText = $"🔥 압도 전투력의 {pct}% (투력 {cLack:N0} 더 채우면 압도)";
        }
        else
        {
            vm.RecommendedStatusBadge = "⚠️ 턱걸이";
            vm.RecommendedStatusBg = "#382914";
            vm.RecommendedStatusFg = "#D29922";
            vm.RecommendedStatusBorder = "#BB8009";

            var lackParts = new List<string>();
            lackParts.Add($"압도 전투력의 {pct}% (투력 {cLack:N0} 더 채우면 압도)");
            if (targetTier.OverwhelmMdef > 0 && mdef < targetTier.OverwhelmMdef)
                lackParts.Add($"저항 {targetTier.OverwhelmMdef - mdef:N0} 부족");

            vm.StatusDetailText = $"⚠️ {string.Join(", ", lackParts)}";
        }

        // 4. 3행 상위 난이도 도전 가이드 (NextTierGuideText)
        int targetIdx = content.Tiers.IndexOf(targetTier);
        if (targetIdx >= 0 && targetIdx < content.Tiers.Count - 1)
        {
            var nextTier = content.Tiers[targetIdx + 1];
            long nextReqCombat = Math.Max(nextTier.RecommendedCombat, (nextTier.OverwhelmCombat * 9) / 10);
            long nextCombatLack = Math.Max(0, nextReqCombat - combat);
            long nextMdefLack = nextTier.OverwhelmMdef > 0 ? Math.Max(0, nextTier.OverwhelmMdef - mdef) : 0;

            vm.NextTierGuideFg = "#39D353"; // 눈에 잘 띄는 네온 라임 그린
            if (nextCombatLack > 0 && nextMdefLack > 0)
            {
                vm.NextTierGuideText = $"💡 투력 {nextCombatLack:N0}, 저항 {nextMdefLack:N0} 올리면 상위({nextTier.TierName}) 도전 가능";
            }
            else if (nextMdefLack > 0)
            {
                vm.NextTierGuideText = $"💡 저항 {nextMdefLack:N0}만 올리면 상위({nextTier.TierName}) 도전 가능";
            }
            else if (nextCombatLack > 0)
            {
                vm.NextTierGuideText = $"💡 투력 {nextCombatLack:N0}만 올리면 상위({nextTier.TierName}) 도전 가능";
            }
            else
            {
                vm.NextTierGuideText = $"💡 상위({nextTier.TierName}) 즉시 도전 가능!";
            }
        }
        else
        {
            vm.NextTierGuideText = "💡 최고 난이도 도전 가능";
            vm.NextTierGuideFg = "#A371F7"; // 라벤더 퍼플
        }

        return vm;
    }

    /// <summary>
    /// 4대 핵심 콘텐츠 전체 평가
    /// </summary>
    public static List<ContentRecommendationViewModel> EvaluateAllContents(long combat, long mdef)
    {
        return MasterContents.Select(c => EvaluateContent(c, combat, mdef)).ToList();
    }
}
