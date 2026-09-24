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
    public long OverwhelmMdef { get; set; }         // 압도 마도저항 (+40% 피해 버프)
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

        // 압도치까지 전투력 부족이 5,000 이내이거나 92% 이상 달성
        bool combatNear = combatShortage <= 5000 || ((double)combat / OverwhelmCombat >= 0.92);
        // 마도저항이 없거나, 부족분이 300 이내이거나 85% 이상 달성
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
    public string Subtitle { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<DungeonCutoffTier> Tiers { get; set; } = new();
}

/// <summary>
/// 세부 난이도 목록 표시용 뷰모델
/// </summary>
public class TierDetailViewModel
{
    public string TierName { get; set; } = "";
    public string EntryStatusText { get; set; } = "";
    public string EntryStatusBg { get; set; } = "#1C3829";
    public string EntryStatusFg { get; set; } = "#3FB950";
    public string SpecRequirementText { get; set; } = "";
    public string OverwhelmSpecText { get; set; } = "";
}

/// <summary>
/// 콘텐츠별 맞춤 추천 카드 뷰모델
/// </summary>
public class ContentRecommendationViewModel
{
    public string ContentId { get; set; } = "";
    public string ContentName { get; set; } = "";
    public string Subtitle { get; set; } = "";
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

    // 3. 상태 및 모자란 수치 상세 가이드
    public string StatusDetailText { get; set; } = "";
    public string StatusDetailFg { get; set; } = "#C9D1D9";

    // 4. 전체 난이도 펼쳐보기 리스트
    public List<TierDetailViewModel> TierDetails { get; set; } = new();
}

public static class DungeonCutoffService
{
    /// <summary>
    /// 承雲의 인게임 실측 데이터 100% 동기화 마스터 리스트
    /// </summary>
    public static List<DungeonContentDefinition> MasterContents { get; } = new()
    {
        // 1. 어비스 (허상의 정박지, 광기의 동굴, 흩어진 물길 공통) - 총 6단계
        new DungeonContentDefinition
        {
            Id = "abyss",
            Name = "어비스",
            Subtitle = "허상의 정박지 · 광기의 동굴 · 흩어진 물길 (공통 규격)",
            Icon = "🌀",
            Tiers = new List<DungeonCutoffTier>
            {
                new("입문", 50000, 56000, 64500, 0, 1000, "솔플 및 입문"),
                new("어려움", 63000, 66000, 75000, 1000, 1600, "중급 장비 파밍"),
                new("매우 어려움", 76000, 80000, 92000, 2200, 2700, "에픽 룬 각인"),
                new("지옥 1", 87500, 92000, 105000, 3500, 4400, "시즌 2 최상위 엔드게임"),
                new("지옥 2", 95000, 100000, 115000, 6000, 6600, "극악 지옥 난이도"),
                new("지옥 3", 102500, 108000, 124000, 7200, 7800, "현존 최종 종결 난이도")
            }
        },

        // 2. 화이트 서큐버스 - 총 2단계
        new DungeonContentDefinition
        {
            Id = "white_succubus",
            Name = "화이트 서큐버스",
            Subtitle = "몽환의 라비 4인 파티 주간 레이드",
            Icon = "👑",
            Tiers = new List<DungeonCutoffTier>
            {
                new("어려움", 0, 27000, 31100, 0, 0, "기본 레이드 (부활 5회 제한)"),
                new("매우 어려움", 50000, 57500, 64000, 0, 0, "화서큐 종결 도전")
            }
        },

        // 3. 에이렐 - 총 2단계
        new DungeonContentDefinition
        {
            Id = "airel",
            Name = "에이렐",
            Subtitle = "혹한의 성채 4인 기믹 & 리듬 주간 레이드",
            Icon = "❄️",
            Tiers = new List<DungeonCutoffTier>
            {
                new("어려움", 43500, 50000, 57500, 0, 0, "에이렐 입문 (마도저항 불필요)"),
                new("매우 어려움", 88500, 93000, 107000, 3000, 3500, "에이렐 엔드 파밍 (마도저항 필수)")
            }
        },

        // 4. 카브락 - 총 2단계
        new DungeonContentDefinition
        {
            Id = "cavrak",
            Name = "카브락",
            Subtitle = "고대 사막 8인 대규모 연합 주간 레이드",
            Icon = "🐲",
            Tiers = new List<DungeonCutoffTier>
            {
                new("입문", 65000, 72000, 82500, 2000, 2500, "카브락 연합 입문"),
                new("어려움", 90000, 95000, 109000, 3100, 3700, "숙련 8인 파티 토벌")
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
            Subtitle = content.Subtitle,
            Icon = content.Icon
        };

        // 1. 최고 입장 가능 난이도 탐색 (뒤에서부터 검사하여 최고 단계 획득)
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
            vm.RecommendedTier = "입장 조건 미달";
            vm.RecommendedStatusBadge = "🔒 입장 불가";
            vm.RecommendedStatusBg = "#2D1D24";
            vm.RecommendedStatusFg = "#F85149";
            vm.RecommendedStatusBorder = "#DA3633";

            var reasons = new List<string>();
            if (cLack > 0) reasons.Add($"최소 투력 {cLack:N0} 부족");
            if (mLack > 0) reasons.Add($"최소 저항 {mLack:N0} 부족");
            vm.StatusDetailText = $"입문 기준 {string.Join(", ", reasons)}입니다.";
            vm.StatusDetailFg = "#F85149";

            vm.TierDetails = BuildTierDetails(content.Tiers, combat, mdef);
            return vm;
        }

        // 2. 추천 난이도 탐색
        // 우선순위 1: 완전 압도 달성 최고 난이도
        var overwhelmedTier = content.Tiers.LastOrDefault(t => t.IsOverwhelmed(combat, mdef));

        // 우선순위 2: 압도 근접(살짝 모자란 상태) 최고 난이도
        long nearCombatLack = 0, nearMdefLack = 0;
        var nearOverwhelmTier = content.Tiers.LastOrDefault(t => t.IsNearOverwhelm(combat, mdef, out nearCombatLack, out nearMdefLack));

        if (overwhelmedTier != null && (nearOverwhelmTier == null || content.Tiers.IndexOf(overwhelmedTier) >= content.Tiers.IndexOf(nearOverwhelmTier)))
        {
            // 완전 압도 달성 난이도 추천
            vm.RecommendedTier = overwhelmedTier.TierName;
            vm.RecommendedStatusBadge = "⚡ 압도";
            vm.RecommendedStatusBg = "#2E1C48";
            vm.RecommendedStatusFg = "#D2A8FF";
            vm.RecommendedStatusBorder = "#9B59B6";

            var desc = overwhelmedTier.OverwhelmMdef > 0 
                ? $"압도 충족 (기준 투력 {overwhelmedTier.OverwhelmCombat:N0} / 저항 {overwhelmedTier.OverwhelmMdef:N0})"
                : $"압도 충족 (기준 투력 {overwhelmedTier.OverwhelmCombat:N0})";
            vm.StatusDetailText = desc;
            vm.StatusDetailFg = "#D2A8FF";
        }
        else if (nearOverwhelmTier != null)
        {
            // 살짝 모자란 압도 근접 난이도 추천!
            nearOverwhelmTier.IsNearOverwhelm(combat, mdef, out nearCombatLack, out nearMdefLack);
            vm.RecommendedTier = nearOverwhelmTier.TierName;
            vm.RecommendedStatusBadge = "🔥 압도 근접";
            vm.RecommendedStatusBg = "#3B2A14";
            vm.RecommendedStatusFg = "#F5D061";
            vm.RecommendedStatusBorder = "#E5A93C";

            var lackParts = new List<string>();
            if (nearCombatLack > 0) lackParts.Add($"투력 {nearCombatLack:N0} 부족");
            if (nearMdefLack > 0) lackParts.Add($"저항 {nearMdefLack:N0} 부족");

            vm.StatusDetailText = $"압도까지 {string.Join(", ", lackParts)}";
            vm.StatusDetailFg = "#F5D061";
        }
        else
        {
            // 압도 근접도 안 되는 경우: 입장 가능한 최고 난이도 추천
            var targetTier = maxEnterableTier;
            vm.RecommendedTier = targetTier.TierName;

            if (combat >= targetTier.RecommendedCombat)
            {
                vm.RecommendedStatusBadge = "✓ 권장 충족";
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
                vm.StatusDetailFg = "#3FB950";
            }
            else
            {
                vm.RecommendedStatusBadge = "⚠️ 턱걸이 입장";
                vm.RecommendedStatusBg = "#382914";
                vm.RecommendedStatusFg = "#D29922";
                vm.RecommendedStatusBorder = "#BB8009";

                long reqLack = targetTier.RecommendedCombat - combat;
                vm.StatusDetailText = $"권장 투력보다 {reqLack:N0} 부족";
                vm.StatusDetailFg = "#D29922";
            }
        }

        // 전체 세부 난이도 리스트 구성
        vm.TierDetails = BuildTierDetails(content.Tiers, combat, mdef);
        return vm;
    }

    private static List<TierDetailViewModel> BuildTierDetails(List<DungeonCutoffTier> tiers, long combat, long mdef)
    {
        var list = new List<TierDetailViewModel>();
        foreach (var t in tiers)
        {
            string statusText;
            string bg, fg;

            if (t.IsOverwhelmed(combat, mdef))
            {
                statusText = "⚡ 압도 달성";
                bg = "#2E1C48";
                fg = "#D2A8FF";
            }
            else if (t.IsNearOverwhelm(combat, mdef, out var cL, out var mL))
            {
                statusText = "🔥 압도 근접";
                bg = "#3B2A14";
                fg = "#F5D061";
            }
            else if (combat >= t.RecommendedCombat && (t.MinEntryMdef == 0 || mdef >= t.MinEntryMdef))
            {
                statusText = "✓ 권장 충족";
                bg = "#1C3829";
                fg = "#3FB950";
            }
            else if (t.CanEnter(combat, mdef))
            {
                statusText = "⚠️ 턱걸이";
                bg = "#382914";
                fg = "#D29922";
            }
            else
            {
                statusText = "🔒 입장 불가";
                bg = "#2D1D24";
                fg = "#F85149";
            }

            var specText = t.MinEntryMdef > 0 
                ? $"입장 투력 {t.MinEntryCombat:N0} / 저항 {t.MinEntryMdef:N0}" 
                : $"입장 투력 {t.MinEntryCombat:N0}";

            var overText = t.OverwhelmMdef > 0
                ? $"압도 투력 {t.OverwhelmCombat:N0} / 저항 {t.OverwhelmMdef:N0}"
                : $"압도 투력 {t.OverwhelmCombat:N0}";

            list.Add(new TierDetailViewModel
            {
                TierName = t.TierName,
                EntryStatusText = statusText,
                EntryStatusBg = bg,
                EntryStatusFg = fg,
                SpecRequirementText = specText,
                OverwhelmSpecText = overText
            });
        }
        return list;
    }

    /// <summary>
    /// 4대 핵심 콘텐츠 전체 평가
    /// </summary>
    public static List<ContentRecommendationViewModel> EvaluateAllContents(long combat, long mdef)
    {
        return MasterContents.Select(c => EvaluateContent(c, combat, mdef)).ToList();
    }
}
