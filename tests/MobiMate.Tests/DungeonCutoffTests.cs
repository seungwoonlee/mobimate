using System.Linq;
using Xunit;

namespace MobiMate.Tests;

public class DungeonCutoffTests
{
    [Fact]
    public void MasterList_Contains_Abyss_And_Raids()
    {
        var list = DungeonCutoffService.MasterList;
        Assert.NotEmpty(list);
        Assert.Contains(list, d => d.Id == "abyss_runda");
        Assert.Contains(list, d => d.Id == "raid_cavrak");
        Assert.Contains(list, d => d.Id == "raid_airel");
        Assert.Contains(list, d => d.Id == "raid_white_succubus");
    }

    [Theory]
    // 1. 카브락 입문 (투력 65000 / 마도저항 2000, 압도 2500)
    // 투력 111161, 마도저항 6766인 경우 -> 압도 달성 (+40%)
    [InlineData(111161, 6766, DungeonEntryStatus.Overwhelmed, "⚡ 압도 달성 (+40%)")]
    // 투력 111161, 마도저항 2100인 경우 -> 입장 가능 (압도는 미달)
    [InlineData(111161, 2100, DungeonEntryStatus.Ready, "✓ 입장 가능")]
    // 투력 111161, 마도저항 1500인 경우 -> 페널티 주의 (투력은 충분하나 마도저항 미달)
    [InlineData(111161, 1500, DungeonEntryStatus.Warning, "⚠️ 페널티 주의")]
    // 투력 50000, 마도저항 6766인 경우 -> 입장 불가 (투력 미달)
    [InlineData(50000, 6766, DungeonEntryStatus.Locked, "🔒 입장 불가")]
    public void EvaluateTier_CavrakIntro_EvaluatesCorrectStatus(long combat, long mdef, DungeonEntryStatus expectedStatus, string expectedBadge)
    {
        var cavrakDef = DungeonCutoffService.MasterList.First(d => d.Id == "raid_cavrak");
        var introTier = cavrakDef.Tiers.First(t => t.TierName == "입문");

        var result = DungeonCutoffService.EvaluateTier(introTier, combat, mdef);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedBadge, result.StatusBadgeText);
    }

    [Fact]
    public void EvaluateAll_Calculates_HighestCleared_And_NextTarget()
    {
        // 承雲의 힐러 스펙: 투력 111161, 마도저항 6766
        var results = DungeonCutoffService.EvaluateAll(111161, 6766);
        Assert.NotEmpty(results);

        var runda = results.First(r => r.Id == "abyss_runda");
        // 지옥 2 (투력 110,000 / 마도저항 6000, 압도 6600)까지 지옥2 압도 달성!
        Assert.NotNull(runda.HighestClearedTier);
        Assert.Equal("지옥 2", runda.HighestClearedTier.TierName);
        Assert.Equal(DungeonEntryStatus.Overwhelmed, runda.HighestClearedTier.Status);

        var cavrak = results.First(r => r.Id == "raid_cavrak");
        // 카브락 매우 어려움(투력 110,000 / 마도저항 6000, 압도 6600)까지 압도 달성!
        Assert.NotNull(cavrak.HighestClearedTier);
        Assert.Equal("매우 어려움", cavrak.HighestClearedTier.TierName);
        Assert.Equal(DungeonEntryStatus.Overwhelmed, cavrak.HighestClearedTier.Status);
    }

    [Fact]
    public void BuildCardViewModels_AppliesFilter_Correctly()
    {
        // 1. 전체 카드
        var allCards = DungeonCutoffService.BuildCardViewModels(102832, 4736, "All");
        Assert.Equal(11, allCards.Count); // 어비스 5 + 서큐버스 1 + 에이렐 2 + 카브락 3 = 11

        // 2. 어비스 필터
        var abyssCards = DungeonCutoffService.BuildCardViewModels(102832, 4736, "Abyss");
        Assert.Equal(5, abyssCards.Count);
        Assert.All(abyssCards, c => Assert.Equal("Abyss", c.Category));

        // 3. 레이드 필터
        var raidCards = DungeonCutoffService.BuildCardViewModels(102832, 4736, "Raid");
        Assert.Equal(6, raidCards.Count);
        Assert.All(raidCards, c => Assert.Equal("Raid", c.Category));

        // 4. 카드 속성 무결성 검증
        var cavrakHard = raidCards.First(c => c.DisplayName.Contains("카브락") && c.DisplayName.Contains("어려움"));
        Assert.Equal("[레이드]", cavrakHard.CategoryBadgeText);
        Assert.Contains("4,736 / 4,400", cavrakHard.MdefProgressText); // 마도저항 4736, 필요치 4400 달성
        Assert.Contains("95,000", cavrakHard.ReqCombatText); // 권장 전투력 95,000 충족
    }
}

