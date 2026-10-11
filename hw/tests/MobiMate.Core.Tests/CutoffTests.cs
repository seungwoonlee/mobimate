namespace MobiMate.Tests;

/// <summary>TST-11 컷오프 판정: WPF판 v1.2.0 DungeonCutoffTests 사례 + 웹판 규칙(정수 달성률, 카탈로그 검증·덮어쓰기).</summary>
public class CutoffTests
{
    private static readonly CutoffCatalog Catalog = CutoffCatalog.Load();

    private static CutoffResult Eval(string id, long combat, long mdef) =>
        CutoffEvaluator.Evaluate(Catalog.Contents.Single(c => c.Id == id), combat, mdef);

    [Fact]
    public void Catalog_HasMeasuredStructure_AndNoWarnings()
    {
        Assert.Empty(Catalog.Warnings);
        Assert.Equal(new[] { "abyss", "white_succubus", "airel", "cavrak" }, Catalog.Contents.Select(c => c.Id));
        var abyss = Catalog.Contents[0];
        Assert.Equal(6, abyss.Tiers.Count);
        Assert.Equal(new CutoffTier("지옥 3", 102500, 108000, 124000, 7200, 7800), abyss.Tiers[5]);
        Assert.Equal(2, Catalog.Contents[1].Tiers.Count);
        Assert.Equal(0, Catalog.Contents[1].Tiers[0].MinCombat);
        Assert.Equal(3000, Catalog.Contents[2].Tiers[1].MinMdef);
        Assert.Equal(3100, Catalog.Contents[3].Tiers[1].MinMdef);
    }

    [Theory]
    [InlineData(102832, 4736, 87500, 3500, true)]
    [InlineData(80000, 4736, 87500, 3500, false)]
    [InlineData(102832, 3000, 87500, 3500, false)]
    [InlineData(102832, 4736, 95000, 6000, false)]
    public void CanEnter_RequiresBothCombatAndMdef(long combat, long mdef, long reqCombat, long reqMdef, bool expected)
    {
        var tier = new CutoffTier("테스트", reqCombat, reqCombat + 5000, reqCombat + 15000, reqMdef, reqMdef + 1000);
        Assert.Equal(expected, tier.CanEnter(combat, mdef));
    }

    [Fact]
    public void MeasuredCharacter_102832_4736_MatchesWpfResults()
    {
        var abyss = Eval("abyss", 102832, 4736);
        Assert.Equal("지옥 1", abyss.MaxEntryTier);
        Assert.Equal("지옥 1", abyss.RecommendedTier);
        Assert.Equal(CutoffStatus.Near, abyss.Status);
        Assert.Equal(97, abyss.OverwhelmPct);
        Assert.Equal(2168, abyss.CombatToOverwhelm);
        Assert.Equal(new CutoffNext("지옥 2", 668, 1864, false), abyss.Next);

        var succubus = Eval("white_succubus", 102832, 4736);
        Assert.Equal("매우 어려움", succubus.RecommendedTier);
        Assert.Equal(CutoffStatus.Overwhelm, succubus.Status);
        Assert.True(succubus.Next!.Top);

        var airel = Eval("airel", 102832, 4736);
        Assert.Equal("매우 어려움", airel.MaxEntryTier);
        Assert.Equal(CutoffStatus.Near, airel.Status);
        Assert.Equal(96, airel.OverwhelmPct);
        Assert.Equal(4168, airel.CombatToOverwhelm);
        Assert.True(airel.Next!.Top);

        var cavrak = Eval("cavrak", 102832, 4736);
        Assert.Equal("어려움", cavrak.RecommendedTier);
        Assert.Equal(CutoffStatus.Near, cavrak.Status);
        Assert.Equal(94, cavrak.OverwhelmPct);
        Assert.Equal(6168, cavrak.CombatToOverwhelm);
    }

    [Fact]
    public void LowSpec_40000_0_IsLockedWhereEntryNotMet()
    {
        var abyss = Eval("abyss", 40000, 0);
        Assert.Equal(CutoffStatus.Locked, abyss.Status);
        Assert.Null(abyss.MaxEntryTier);
        Assert.Equal("입문", abyss.EntryTier);
        Assert.Equal(new CutoffShort(10000, 0), abyss.EntryShort);

        var succubus = Eval("white_succubus", 40000, 0);
        Assert.Equal("어려움", succubus.MaxEntryTier);
        Assert.Equal("어려움", succubus.RecommendedTier);

        Assert.Equal("어려움", Eval("airel", 40000, 0).EntryTier);
        var cavrak = Eval("cavrak", 40000, 0);
        Assert.Equal(new CutoffShort(25000, 2000), cavrak.EntryShort);
    }

    [Fact]
    public void MdefOneShort_DemotesRecommendation()
    {
        // 지옥 1 압도 마도저항 4,400 → 4,399면 지옥 1 추천 불가, 매우 어려움으로 강등
        Assert.Equal("지옥 1", Eval("abyss", 102832, 4400).RecommendedTier);
        Assert.Equal("매우 어려움", Eval("abyss", 102832, 4399).RecommendedTier);
    }

    [Fact]
    public void CombatBelowNinetyPct_DemotesRecommendation()
    {
        // 지옥 1 압도 105,000의 90% = 94,500 (권장 92,000보다 큼)
        Assert.Equal("지옥 1", Eval("abyss", 94500, 4400).RecommendedTier);
        Assert.Equal("매우 어려움", Eval("abyss", 94499, 4400).RecommendedTier);
    }

    [Fact]
    public void NoQualifyingTier_FallsBackToMdefSafe_ThenMaxEntry()
    {
        // ② 입문 입장·마도저항 압도(1,000)는 되지만 권장 56,000 미달 → 마도저항 압도만 충족한 최고 = 입문, 턱걸이
        var r = Eval("abyss", 55000, 1000);
        Assert.Equal("입문", r.RecommendedTier);
        Assert.Equal(CutoffStatus.Marginal, r.Status);
        Assert.Equal(85, r.OverwhelmPct);
        // ③ 카브락 입문 입장(2,000)은 되지만 압도 마도저항 2,500 미달 → 입장 가능한 최고, 저항 부족 표시
        var m = Eval("cavrak", 91000, 2400);
        Assert.Equal("입문", m.MaxEntryTier);
        Assert.Equal("입문", m.RecommendedTier);
        Assert.Equal(CutoffStatus.Marginal, m.Status);
        Assert.Equal(100, m.MdefShort);
    }

    [Fact]
    public void Pct_IsCappedAt99_AndUsesExactIntegerDivision()
    {
        Assert.Equal(99, Eval("abyss", 104999, 4400).OverwhelmPct);   // 99.999% → 99, 압도 아님
        Assert.Equal(CutoffStatus.Overwhelm, Eval("abyss", 105000, 4400).Status);
        // 42,750 / 75,000 = 57% (WPF판은 실수 계산으로 56%)
        var t = new CutoffTier("t", 0, 0, 75000, 0, 0);
        var r = CutoffEvaluator.Evaluate(new CutoffContent("x", "x", "", new[] { t }), 42750, 0);
        Assert.Equal(57, r.OverwhelmPct);
    }

    [Fact]
    public void NextTier_ReadyNow_WhenBothMet()
    {
        var c = new CutoffContent("x", "x", "", new[]
        {
            new CutoffTier("a", 0, 10, 100, 0, 0),
            new CutoffTier("b", 1000, 2000, 3000, 0, 50),
        });
        // a 추천 조건은 충족하지만 b는 입장 불가(1,000 미만)
        var r = CutoffEvaluator.Evaluate(c, 999, 60);
        Assert.Equal("a", r.RecommendedTier);
        Assert.Equal(new CutoffNext("b", 1701, 0, false), r.Next);   // max(권장 2,000, 90% 2,700) - 999
        Assert.False(r.Next!.ReadyNow);
    }

    [Theory]
    [InlineData(0, 0, 0, "압도 전투력은 0보다")]
    [InlineData(100, 50, 200, "입장 ≤ 권장 ≤ 압도")]
    public void Validate_RejectsBadTiers(long min, long rec, long over, string reason)
    {
        var c = new CutoffContent("x", "x", "", new[] { new CutoffTier("t", min, rec, over, 0, 0) });
        Assert.Contains(reason, CutoffCatalog.Validate(c));
    }

    [Fact]
    public void Validate_RejectsUnorderedTiers_AndMdefAboveOverwhelm()
    {
        Assert.NotNull(CutoffCatalog.Validate(new CutoffContent("x", "x", "", new[] { new CutoffTier("a", 100, 100, 200, 0, 0), new CutoffTier("b", 50, 60, 70, 0, 0) })));
        Assert.NotNull(CutoffCatalog.Validate(new CutoffContent("x", "x", "", new[] { new CutoffTier("a", 0, 0, 10, 500, 400) })));
    }

    [Fact]
    public void GameStateCache_ClearPerCharacter_DropsCurrenciesAndMissions()
    {
        var s = new GameStateCache();
        s.UpdateCurrencies(new[] { new CurrencyItem("골드", 1) });
        s.UpdateDailyMissions(new[] { new MissionItem("m", "", 1, 1, true, true) });
        s.ClearPerCharacter();
        Assert.Null(s.Currencies);
        Assert.Null(s.DailyMissions);
    }

    [Fact]
    public void Override_ReplacesContentById_AndBadOverrideIsIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-cutoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, CutoffCatalog.OverrideFileName), """
                { "contents": [
                  { "id": "cavrak", "name": "카브락", "tiers": [ { "name": "입문", "minCombat": 1, "recCombat": 2, "overCombat": 3, "minMdef": 0, "overMdef": 0 } ] },
                  { "id": "broken", "name": "깨짐", "tiers": [ { "name": "t", "minCombat": 5, "recCombat": 1, "overCombat": 9 } ] }
                ] }
                """);
            var cat = CutoffCatalog.Load(dir);
            Assert.Single(cat.Contents.Single(c => c.Id == "cavrak").Tiers);
            Assert.DoesNotContain(cat.Contents, c => c.Id == "broken");
            Assert.Contains(cat.Warnings, w => w.Contains("broken"));
            Assert.Equal(6, cat.Contents.Single(c => c.Id == "abyss").Tiers.Count);   // 나머지는 내장 그대로

            File.WriteAllText(Path.Combine(dir, CutoffCatalog.OverrideFileName), "{ 깨진 json");
            var fallback = CutoffCatalog.Load(dir);
            Assert.Equal(2, fallback.Contents.Single(c => c.Id == "cavrak").Tiers.Count);
            Assert.NotEmpty(fallback.Warnings);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
