using System;
using System.Collections.Generic;
using System.Linq;

namespace MobiMate;

public enum DungeonEntryStatus
{
    Overwhelmed, // 마도저항 >= 압도치 (최종 피해량 +40% 증폭 버프)
    Ready,       // 마도저항 >= 필요치 && 전투력 >= 권장치 (정상 입장 가능)
    Warning,     // 전투력 >= 권장치이나 마도저항 미달 (피해 감소 & 피격 증가 페널티)
    Locked       // 최소 입장 조건(전투력 또는 마도저항) 미달
}

public class DungeonCutoffTier
{
    public string TierName { get; set; } = "";
    public long RequiredCombatScore { get; set; }
    public long RequiredArcaneResistance { get; set; }
    public long OverwhelmArcaneResistance { get; set; }
    public string Description { get; set; } = "";

    public DungeonCutoffTier(string tierName, long combat, long mdef, long overwhelm, string desc = "")
    {
        TierName = tierName;
        RequiredCombatScore = combat;
        RequiredArcaneResistance = mdef;
        OverwhelmArcaneResistance = overwhelm;
        Description = desc;
    }
}

public class DungeonCutoffDefinition
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = ""; // "어비스", "주간 레이드"
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<DungeonCutoffTier> Tiers { get; set; } = new();
}

public class DungeonTierEvaluation
{
    public string TierName { get; set; } = "";
    public long RequiredCombatScore { get; set; }
    public long RequiredArcaneResistance { get; set; }
    public long OverwhelmArcaneResistance { get; set; }
    public DungeonEntryStatus Status { get; set; }
    public string StatusBadgeText { get; set; } = "";
    public string StatusBadgeColorHex { get; set; } = "#5865F2";
    public double CombatProgressPercent { get; set; }
    public double MdefProgressPercent { get; set; }
    public long CombatDelta { get; set; }
    public long MdefDelta { get; set; }
    public long OverwhelmDelta { get; set; }
}

public class DungeonEvaluationResult
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<DungeonTierEvaluation> Tiers { get; set; } = new();
    public DungeonTierEvaluation? HighestClearedTier { get; set; }
    public DungeonTierEvaluation? NextTargetTier { get; set; }
}

public static class DungeonCutoffService
{
    public static List<DungeonCutoffDefinition> MasterList { get; } = new()
    {
        // 1. 룬다 어비스 (허상의 정박지, 광기의 동굴, 흩어진 물길 공통 규격)
        new DungeonCutoffDefinition
        {
            Id = "abyss_runda",
            Category = "어비스 던전",
            Title = "심층 어비스 3종 (허상/광기/물길)",
            Subtitle = "허상의 정박지 · 광기의 동굴 · 흩어진 물길 (공통 규격)",
            Icon = "🌀",
            Tiers = new List<DungeonCutoffTier>
            {
                new("입문", 50000, 0, 1000, "솔플 및 입문"),
                new("어려움", 65000, 1000, 1600, "중급 장비 파밍"),
                new("매우 어려움", 78000, 2200, 2700, "에픽 룬 각인"),
                new("지옥 1", 92000, 3500, 4400, "시즌 2 최상위 엔드게임"),
                new("지옥 2", 110000, 6000, 6600, "극악 지옥 난이도")
            }
        },

        // 2. 화이트 서큐버스 (시즌 1 레이드)
        new DungeonCutoffDefinition
        {
            Id = "raid_white_succubus",
            Category = "주간 레이드",
            Title = "화이트 서큐버스",
            Subtitle = "몽환의 라비 던전 4인 파티 레이드",
            Icon = "👑",
            Tiers = new List<DungeonCutoffTier>
            {
                new("일반", 27000, 0, 0, "기본 레이드 (부활 5회 제한)")
            }
        },

        // 3. 얼음 여왕 에이렐 (시즌 2 4인 레이드)
        new DungeonCutoffDefinition
        {
            Id = "raid_airel",
            Category = "주간 레이드",
            Title = "얼음 여왕 에이렐",
            Subtitle = "혹한의 성채 4인 기믹 & 리듬 레이드",
            Icon = "❄️",
            Tiers = new List<DungeonCutoffTier>
            {
                new("어려움", 75000, 1600, 2200, "에이렐 입문 및 룬 파밍"),
                new("매우 어려움", 88000, 3500, 4400, "엔드급 에이렐 장비 파밍")
            }
        },

        // 4. 흑룡 카브락 (시즌 2 8인 대규모 레이드)
        new DungeonCutoffDefinition
        {
            Id = "raid_cavrak",
            Category = "주간 레이드",
            Title = "흑룡 카브락",
            Subtitle = "고대 사막 8인 대규모 연합 레이드",
            Icon = "🐲",
            Tiers = new List<DungeonCutoffTier>
            {
                new("입문", 65000, 2000, 2500, "카브락 연합 입문"),
                new("어려움", 95000, 4400, 5500, "숙련 8인 파티 토벌"),
                new("매우 어려움", 110000, 6000, 6600, "최상위 카브락 종결 도전")
            }
        }
    };

    public static DungeonTierEvaluation EvaluateTier(DungeonCutoffTier tier, long currentCombat, long currentMdef)
    {
        var status = DungeonEntryStatus.Locked;
        string badgeText;
        string badgeColor;

        bool hasCombat = currentCombat >= tier.RequiredCombatScore;
        bool hasMdefReq = currentMdef >= tier.RequiredArcaneResistance;
        bool hasOverwhelm = tier.OverwhelmArcaneResistance > 0 && currentMdef >= tier.OverwhelmArcaneResistance;

        if (hasOverwhelm && hasCombat)
        {
            status = DungeonEntryStatus.Overwhelmed;
            badgeText = "⚡ 압도 달성 (+40%)";
            badgeColor = "#3FB950"; // 밝은 초록
        }
        else if (hasMdefReq && hasCombat)
        {
            status = DungeonEntryStatus.Ready;
            badgeText = "✓ 입장 가능";
            badgeColor = "#58A6FF"; // 블루
        }
        else if (hasCombat && !hasMdefReq)
        {
            status = DungeonEntryStatus.Warning;
            badgeText = "⚠️ 페널티 주의";
            badgeColor = "#D29922"; // 옐로우/오렌지
        }
        else
        {
            status = DungeonEntryStatus.Locked;
            badgeText = "🔒 입장 불가";
            badgeColor = "#F85149"; // 레드
        }

        double combatPct = tier.RequiredCombatScore > 0
            ? Math.Min(100.0, (double)currentCombat / tier.RequiredCombatScore * 100.0)
            : 100.0;

        long mdefTarget = tier.OverwhelmArcaneResistance > 0 ? tier.OverwhelmArcaneResistance : tier.RequiredArcaneResistance;
        double mdefPct = mdefTarget > 0
            ? Math.Min(100.0, (double)currentMdef / mdefTarget * 100.0)
            : 100.0;

        return new DungeonTierEvaluation
        {
            TierName = tier.TierName,
            RequiredCombatScore = tier.RequiredCombatScore,
            RequiredArcaneResistance = tier.RequiredArcaneResistance,
            OverwhelmArcaneResistance = tier.OverwhelmArcaneResistance,
            Status = status,
            StatusBadgeText = badgeText,
            StatusBadgeColorHex = badgeColor,
            CombatProgressPercent = combatPct,
            MdefProgressPercent = mdefPct,
            CombatDelta = currentCombat - tier.RequiredCombatScore,
            MdefDelta = currentMdef - tier.RequiredArcaneResistance,
            OverwhelmDelta = tier.OverwhelmArcaneResistance > 0 ? currentMdef - tier.OverwhelmArcaneResistance : 0
        };
    }

    public static List<DungeonEvaluationResult> EvaluateAll(long currentCombat, long currentMdef)
    {
        var results = new List<DungeonEvaluationResult>();

        foreach (var def in MasterList)
        {
            var res = new DungeonEvaluationResult
            {
                Id = def.Id,
                Category = def.Category,
                Title = def.Title,
                Subtitle = def.Subtitle,
                Icon = def.Icon
            };

            foreach (var tier in def.Tiers)
            {
                var eval = EvaluateTier(tier, currentCombat, currentMdef);
                res.Tiers.Add(eval);
            }

            res.HighestClearedTier = res.Tiers.LastOrDefault(t => t.Status == DungeonEntryStatus.Overwhelmed || t.Status == DungeonEntryStatus.Ready);
            res.NextTargetTier = res.Tiers.FirstOrDefault(t => t.Status != DungeonEntryStatus.Overwhelmed && t.Status != DungeonEntryStatus.Ready);

            results.Add(res);
        }

        return results;
    }

    public static List<DungeonCardViewModel> BuildCardViewModels(long currentCombat, long currentMdef, string filter = "All")
    {
        var cards = new List<DungeonCardViewModel>();

        foreach (var def in MasterList)
        {
            if (filter == "Abyss" && !def.Category.Contains("어비스")) continue;
            if (filter == "Raid" && !def.Category.Contains("레이드")) continue;

            bool isAbyss = def.Category.Contains("어비스");
            string catBadge = isAbyss ? "[어비스]" : "[레이드]";
            string catBg = isAbyss ? "#1C2E46" : "#3B2A14";
            string catFg = isAbyss ? "#58A6FF" : "#E5A93C";

            foreach (var tier in def.Tiers)
            {
                var eval = EvaluateTier(tier, currentCombat, currentMdef);
                var card = new DungeonCardViewModel
                {
                    Category = isAbyss ? "Abyss" : "Raid",
                    DisplayName = $"{def.Title} - {tier.TierName}",
                    CategoryBadgeText = catBadge,
                    CategoryBadgeBg = catBg,
                    CategoryBadgeFg = catFg,
                    StatusBadgeText = eval.StatusBadgeText,
                    ProgressMax = tier.OverwhelmArcaneResistance > 0 ? tier.OverwhelmArcaneResistance : (tier.RequiredArcaneResistance > 0 ? tier.RequiredArcaneResistance : 100),
                    ProgressValue = Math.Min(currentMdef, tier.OverwhelmArcaneResistance > 0 ? tier.OverwhelmArcaneResistance : tier.RequiredArcaneResistance)
                };

                // 상태별 비주얼 테마 매핑
                switch (eval.Status)
                {
                    case DungeonEntryStatus.Overwhelmed:
                        card.StatusBadgeBg = "#2E1C48";
                        card.StatusBadgeFg = "#D2A8FF";
                        card.StatusBadgeBorder = "#9B59B6";
                        card.CardBorderBrush = "#6E40C9";
                        card.ProgressBrush = "#9B59B6"; // 보라색
                        card.MdefTextBrush = "#D2A8FF";
                        card.MdefProgressText = tier.OverwhelmArcaneResistance > 0 
                            ? $"{currentMdef:N0} / {tier.OverwhelmArcaneResistance:N0} (압도 달성)" 
                            : $"{currentMdef:N0} (달성)";
                        card.EffectBoxBg = "#1F152E";
                        card.EffectSummaryText = "⚡ 피해량 +40% 압도 증폭 버프 적용!";
                        card.EffectSummaryBrush = "#D2A8FF";
                        break;

                    case DungeonEntryStatus.Ready:
                        card.StatusBadgeBg = "#1C3829";
                        card.StatusBadgeFg = "#3FB950";
                        card.StatusBadgeBorder = "#2EA043";
                        card.CardBorderBrush = "#238636";
                        card.ProgressBrush = "#2EA043"; // 초록색
                        card.MdefTextBrush = "#3FB950";
                        card.MdefProgressText = tier.OverwhelmArcaneResistance > 0
                            ? $"{currentMdef:N0} / {tier.RequiredArcaneResistance:N0} (압도까지 -{Math.Abs(eval.OverwhelmDelta):N0})"
                            : $"{currentMdef:N0} / {tier.RequiredArcaneResistance:N0}";
                        card.EffectBoxBg = "#122619";
                        card.EffectSummaryText = "✓ 마도 압력 없음 (정상 피해 100%)";
                        card.EffectSummaryBrush = "#3FB950";
                        break;

                    case DungeonEntryStatus.Warning:
                        card.StatusBadgeBg = "#382914";
                        card.StatusBadgeFg = "#D29922";
                        card.StatusBadgeBorder = "#BB8009";
                        card.CardBorderBrush = "#9E6A03";
                        card.ProgressBrush = "#D29922"; // 주황색
                        card.MdefTextBrush = "#D29922";
                        card.MdefProgressText = $"{currentMdef:N0} / {tier.RequiredArcaneResistance:N0} (부족 -{Math.Abs(eval.MdefDelta):N0})";
                        card.EffectBoxBg = "#2B1D0E";
                        card.EffectSummaryText = "⚠️ 마도 압력 발생 (가하는 피해 감소 페널티)";
                        card.EffectSummaryBrush = "#D29922";
                        break;

                    default: // Locked
                        card.StatusBadgeBg = "#2D1D24";
                        card.StatusBadgeFg = "#F85149";
                        card.StatusBadgeBorder = "#DA3633";
                        card.CardBorderBrush = "#30363D";
                        card.ProgressBrush = "#484F58"; // 회색
                        card.MdefTextBrush = "#8B949E";
                        card.MdefProgressText = tier.RequiredArcaneResistance > 0
                            ? $"{currentMdef:N0} / {tier.RequiredArcaneResistance:N0}"
                            : "-";
                        card.EffectBoxBg = "#161B22";
                        card.EffectSummaryText = "🔒 입장 조건 미달 (전투력/마도저항 필요)";
                        card.EffectSummaryBrush = "#8B949E";
                        break;
                }

                // 권장 전투력 상태 텍스트
                if (currentCombat >= tier.RequiredCombatScore)
                {
                    card.ReqCombatText = $"{tier.RequiredCombatScore:N0} (충족)";
                    card.CombatStatusBrush = "#3FB950";
                }
                else
                {
                    long diff = tier.RequiredCombatScore - currentCombat;
                    card.ReqCombatText = $"{tier.RequiredCombatScore:N0} (부족 -{diff:N0})";
                    card.CombatStatusBrush = "#F85149";
                }

                cards.Add(card);
            }
        }

        return cards;
    }
}

public class DungeonCardViewModel
{
    public string Category { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string CategoryBadgeText { get; set; } = "";
    public string CategoryBadgeBg { get; set; } = "#1C2E46";
    public string CategoryBadgeFg { get; set; } = "#58A6FF";
    public string StatusBadgeText { get; set; } = "";
    public string StatusBadgeBg { get; set; } = "#1C3829";
    public string StatusBadgeFg { get; set; } = "#3FB950";
    public string StatusBadgeBorder { get; set; } = "#2EA043";
    public string CardBorderBrush { get; set; } = "#2E3A52";
    public string MdefProgressText { get; set; } = "";
    public string MdefTextBrush { get; set; } = "#D2A8FF";
    public double ProgressMax { get; set; } = 100;
    public double ProgressValue { get; set; } = 0;
    public string ProgressBrush { get; set; } = "#3FB950";
    public string ReqCombatText { get; set; } = "";
    public string CombatStatusBrush { get; set; } = "#E5A93C";
    public string EffectBoxBg { get; set; } = "#161B22";
    public string EffectSummaryText { get; set; } = "";
    public string EffectSummaryBrush { get; set; } = "#8B949E";
}
