using System.Linq;
using Xunit;

namespace MobiMate.Tests;

public class DungeonCutoffTests
{
    [Fact]
    public void MasterContents_HasExactOfficialStructure()
    {
        var contents = DungeonCutoffService.MasterContents;
        Assert.Equal(4, contents.Count);

        // 1. 어비스: 6종 (입문, 어려움, 매어, 지옥 1, 지옥 2, 지옥 3)
        var abyss = contents.First(c => c.Id == "abyss");
        Assert.Equal(6, abyss.Tiers.Count);
        Assert.Equal("입문", abyss.Tiers[0].TierName);
        Assert.Equal("지옥 3", abyss.Tiers[5].TierName);
        Assert.Equal(102500, abyss.Tiers[5].MinEntryCombat);
        Assert.Equal(7200, abyss.Tiers[5].MinEntryMdef);
        Assert.Equal(124000, abyss.Tiers[5].OverwhelmCombat);
        Assert.Equal(7800, abyss.Tiers[5].OverwhelmMdef);

        // 2. 화이트 서큐버스: 2종 (어려움, 매우 어려움)
        var succubus = contents.First(c => c.Id == "white_succubus");
        Assert.Equal(2, succubus.Tiers.Count);
        Assert.Equal("어려움", succubus.Tiers[0].TierName);
        Assert.Equal("매우 어려움", succubus.Tiers[1].TierName);
        Assert.Equal(0, succubus.Tiers[0].MinEntryCombat);
        Assert.Equal(50000, succubus.Tiers[1].MinEntryCombat);

        // 3. 에이렐: 2종 (어려움, 매우 어려움)
        var airel = contents.First(c => c.Id == "airel");
        Assert.Equal(2, airel.Tiers.Count);
        Assert.Equal("어려움", airel.Tiers[0].TierName);
        Assert.Equal("매우 어려움", airel.Tiers[1].TierName);
        Assert.Equal(43500, airel.Tiers[0].MinEntryCombat);
        Assert.Equal(88500, airel.Tiers[1].MinEntryCombat);
        Assert.Equal(3000, airel.Tiers[1].MinEntryMdef);

        // 4. 카브락: 2종 (입문, 어려움)
        var cavrak = contents.First(c => c.Id == "cavrak");
        Assert.Equal(2, cavrak.Tiers.Count);
        Assert.Equal("입문", cavrak.Tiers[0].TierName);
        Assert.Equal("어려움", cavrak.Tiers[1].TierName);
        Assert.Equal(65000, cavrak.Tiers[0].MinEntryCombat);
        Assert.Equal(90000, cavrak.Tiers[1].MinEntryCombat);
        Assert.Equal(3100, cavrak.Tiers[1].MinEntryMdef);
    }

    [Theory]
    // 어비스 지옥 1 (입장 87,500 / 저항 3,500)
    // 투력 102,832, 저항 4,736 -> 둘 다 충족 -> 입장 가능 (True)
    [InlineData(102832, 4736, 87500, 3500, true)]
    // 투력 80,000, 저항 4,736 -> 투력 미달 -> 입장 불가 (False)
    [InlineData(80000, 4736, 87500, 3500, false)]
    // 투력 102,832, 저항 3,000 -> 마도저항 미달 -> 입장 불가 (False)
    [InlineData(102832, 3000, 87500, 3500, false)]
    // 어비스 지옥 2 (입장 95,000 / 저항 6,000)
    // 투력 102,832, 저항 4,736 -> 투력은 되나 저항 4736 < 6000 미달 -> 입장 불가 (False)
    [InlineData(102832, 4736, 95000, 6000, false)]
    public void CanEnter_RequiresBothCombatAndMdef(long combat, long mdef, long reqCombat, long reqMdef, bool expectedCanEnter)
    {
        var tier = new DungeonCutoffTier("테스트", reqCombat, reqCombat + 5000, reqCombat + 15000, reqMdef, reqMdef + 1000);
        Assert.Equal(expectedCanEnter, tier.CanEnter(combat, mdef));
    }

    [Fact]
    public void EvaluateBigCloud_ProducesAccurateRecommendations()
    {
        // 承雲의 빅클라우드: 투력 102,832 / 마도저항 4,736
        long combat = 102832;
        long mdef = 4736;

        var results = DungeonCutoffService.EvaluateAllContents(combat, mdef);
        Assert.Equal(4, results.Count);

        // 1. 어비스:
        // 지옥 1(입장 87,500 / 저항 3,500)은 입장 가능.
        // 1. 어비스:
        // 최고 입장은 지옥 1
        var abyss = results.First(c => c.ContentId == "abyss");
        Assert.Equal("지옥 1", abyss.MaxEntryTier);
        Assert.Equal("지옥 1", abyss.RecommendedTier);
        Assert.Contains("압도 근접", abyss.RecommendedStatusBadge);
        Assert.Contains("압도 전투력의 97%", abyss.StatusDetailText);
        Assert.Contains("투력 2,168 더 채우면 압도", abyss.StatusDetailText);
        // 지옥 2는 압도 투력 115,000(90%=103,500) 대비 668 부족, 저항 6,600 대비 1,864 부족
        Assert.Contains("투력 668, 저항 1,864", abyss.NextTierGuideText);
        Assert.Equal("#39D353", abyss.NextTierGuideFg);

        // 2. 화이트 서큐버스:
        // 매우 어려움(입장 50,000 / 압도 64,000)을 102,832로 압도 초과 달성!
        var succubus = results.First(c => c.ContentId == "white_succubus");
        Assert.Equal("매우 어려움", succubus.MaxEntryTier);
        Assert.Equal("매우 어려움", succubus.RecommendedTier);
        Assert.Contains("압도", succubus.RecommendedStatusBadge);
        Assert.Equal("⚡ 압도 달성", succubus.StatusDetailText);
        Assert.Contains("최고 난이도 도전 가능", succubus.NextTierGuideText);
        Assert.Equal("#A371F7", succubus.NextTierGuideFg);

        // 3. 에이렐:
        // 매우 어려움(입장 88,500 / 저항 3,000) 충족 -> 최고 입장은 매우 어려움!
        var airel = results.First(c => c.ContentId == "airel");
        Assert.Equal("매우 어려움", airel.MaxEntryTier);
        // 압도치(107,000)에 102,832로 부족 4,168 (96%)
        Assert.Contains("압도 근접", airel.RecommendedStatusBadge);
        Assert.Contains("압도 전투력의 96%", airel.StatusDetailText);
        Assert.Contains("투력 4,168 더 채우면 압도", airel.StatusDetailText);
        Assert.Contains("최고 난이도 도전 가능", airel.NextTierGuideText);

        // 4. 카브락:
        // 어려움(입장 90,000 / 저항 3,100) 충족 -> 최고 입장은 어려움!
        var cavrak = results.First(c => c.ContentId == "cavrak");
        Assert.Equal("어려움", cavrak.MaxEntryTier);
        // 어려움 권장(95,000) 충족, 압도치(109,000)에 투력 -6,168 부족 (94%)
        Assert.Equal("어려움", cavrak.RecommendedTier);
        Assert.Contains("압도 근접", cavrak.RecommendedStatusBadge);
        Assert.Contains("압도 전투력의 94%", cavrak.StatusDetailText);
        Assert.Contains("투력 6,168 더 채우면 압도", cavrak.StatusDetailText);
        Assert.Contains("최고 난이도 도전 가능", cavrak.NextTierGuideText);
    }

    [Fact]
    public void LowSpecCharacter_ShowsLockedOrIntroAppropriately()
    {
        // 저스펙 캐릭터: 투력 40,000 / 마도저항 0
        var results = DungeonCutoffService.EvaluateAllContents(40000, 0);

        // 어비스: 입문 최소 50,000 미달 -> 입장 불가
        var abyss = results.First(c => c.ContentId == "abyss");
        Assert.Equal("입장 불가", abyss.MaxEntryTier);
        Assert.Contains("부족", abyss.StatusDetailText);
        Assert.Contains("입문", abyss.NextTierGuideText);
        Assert.Equal("#FF7B72", abyss.NextTierGuideFg);

        // 화서큐: 어려움(입장 0) -> 입장 가능 & 압도 달성
        var succubus = results.First(c => c.ContentId == "white_succubus");
        Assert.Equal("어려움", succubus.MaxEntryTier);
        Assert.Equal("어려움", succubus.RecommendedTier);

        // 에이렐: 입문 어려움(43,500) 미달 -> 입장 불가
        var airel = results.First(c => c.ContentId == "airel");
        Assert.Equal("입장 불가", airel.MaxEntryTier);
        Assert.Contains("어려움", airel.NextTierGuideText);

        // 카브락: 입문(65,000) 미달 -> 입장 불가
        var cavrak = results.First(c => c.ContentId == "cavrak");
        Assert.Equal("입장 불가", cavrak.MaxEntryTier);
        Assert.Contains("입문", cavrak.NextTierGuideText);
    }
}
