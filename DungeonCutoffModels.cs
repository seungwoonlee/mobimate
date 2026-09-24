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

    // 3. 상태 및 모자란 수치 상세 가이드 (마우스 호버 툴팁 전용)
    public string StatusDetailText { get; set; } = "";
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
            long cLack = Math.Max(0, firstTier.MinEntryCombat - combat);
            long mLack = Math.Max(0, firstTier.MinEntryMdef - mdef);
            vm.MaxEntryTier = "입장 불가";
            vm.MaxEntryTierBadgeBg = "#2D1D24";
            vm.MaxEntryTierBadgeFg = "#F85149";
            vm.RecommendedTier = "입장 불가";
            vm.RecommendedStatusBadge = "🔒 불가";
            vm.RecommendedStatusBg = "#2D1D24";
            vm.RecommendedStatusFg = "#F85149";
            vm.RecommendedStatusBorder = "#DA3633";

            var reasons = new List<string>();
            if (cLack > 0) reasons.Add($"투력 {cLack:N0} 부족");
            if (mLack > 0) reasons.Add($"저항 {mLack:N0} 부족");
            vm.StatusDetailText = $"입문 기준 {string.Join(", ", reasons)}";
            return vm;
        }

        // 2. 추천 난이도 탐색 (입장 가능한 난이도 중 상위 난이도 우선)
        var enterableTiers = content.Tiers.Where(t => t.CanEnter(combat, mdef)).ToList();

        // 1순위: 입장 가능하면서 권장 전투력(RecommendedCombat)을 충족하는 난이도 중 최상위 난이도
        var safeTiers = enterableTiers.Where(t => combat >= t.RecommendedCombat).ToList();
        DungeonCutoffTier targetTier;

        if (safeTiers.Count > 0)
        {
            targetTier = safeTiers.Last();
        }
        else
        {
            // 모든 입장 가능 난이도가 권장 미달이면 입장 가능한 최상위 난이도를 턱걸이로 추천
            targetTier = maxEnterableTier;
        }

        vm.RecommendedTier = targetTier.TierName;

        // 타겟 난이도의 상태 평가
        if (targetTier.IsOverwhelmed(combat, mdef))
        {
            vm.RecommendedStatusBadge = "⚡ 압도";
            vm.RecommendedStatusBg = "#2E1C48";
            vm.RecommendedStatusFg = "#D2A8FF";
            vm.RecommendedStatusBorder = "#9B59B6";

            vm.StatusDetailText = targetTier.OverwhelmMdef > 0 
                ? $"압도 충족 (기준 투력 {targetTier.OverwhelmCombat:N0} / 저항 {targetTier.OverwhelmMdef:N0})"
                : $"압도 충족 (기준 투력 {targetTier.OverwhelmCombat:N0})";
        }
        else if (targetTier.IsNearOverwhelm(combat, mdef, out var nearCLack, out var nearMLack))
        {
            vm.RecommendedStatusBadge = "🔥 압도 근접";
            vm.RecommendedStatusBg = "#3B2A14";
            vm.RecommendedStatusFg = "#F5D061";
            vm.RecommendedStatusBorder = "#E5A93C";

            var lackParts = new List<string>();
            if (nearCLack > 0) lackParts.Add($"투력 {nearCLack:N0} 부족");
            if (nearMLack > 0) lackParts.Add($"저항 {nearMLack:N0} 부족");

            vm.StatusDetailText = $"압도까지 {string.Join(", ", lackParts)}";
        }
        else if (combat >= targetTier.RecommendedCombat)
        {
            vm.RecommendedStatusBadge = "✓ 권장";
            vm.RecommendedStatusBg = "#1C3829";
            vm.RecommendedStatusFg = "#3FB950";
            vm.RecommendedStatusBorder = "#2EA043";

            long cLack = Math.Max(0, targetTier.OverwhelmCombat - combat);
            long mLack = targetTier.OverwhelmMdef > 0 ? Math.Max(0, targetTier.OverwhelmMdef - mdef) : 0;
            var lackParts = new List<string>();
            if (cLack > 0) lackParts.Add($"투력 {cLack:N0}");
            if (mLack > 0) lackParts.Add($"저항 {mLack:N0}");

            vm.StatusDetailText = lackParts.Count > 0 
                ? $"권장 충족 (압도까지 {string.Join(", ", lackParts)} 부족)"
                : "권장 충족";
        }
        else
        {
            vm.RecommendedStatusBadge = "⚠️ 턱걸이";
            vm.RecommendedStatusBg = "#382914";
            vm.RecommendedStatusFg = "#D29922";
            vm.RecommendedStatusBorder = "#BB8009";

            long reqLack = targetTier.RecommendedCombat - combat;
            vm.StatusDetailText = $"권장 투력보다 {reqLack:N0} 부족";
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
