using MobiMateHomeWork.Layout;
using MobiMateHomeWork.ViewModels;
using static MobiMateHomeWork.Tests.BoardKit;

namespace MobiMateHomeWork.Tests;

/// <summary>H4 순수 계산: 칸 만들기(웹앱 cellsOf 규칙), 툴팁, 직업 뱃지 색, 카드 값 서식(천 단위·예상·무게 단계).</summary>
public class BoardModelTests
{
    // ── 틀: 웹앱 HW_GROUPS와 같은 id·이름 ──

    [Fact]
    public void Layout_HasTheWebAppGroupsAndCells()
    {
        Assert.Equal(["일일·주간", "레이드", "어비스", "필드"], HomeworkBoardLayout.Groups.Select(g => g.Title));
        Assert.Equal([3, 3, 3, 1], HomeworkBoardLayout.Groups.Select(g => g.Cells.Count));
        Assert.Equal(
            ["daily_day_dungeon", "weekly_guild_regular", "weekly_vanguard_breach", "raid_cavrak", "raid_airel", "raid_white_succubus",
             "abyss_illusory_anchorage", "abyss_madness_cave", "abyss_scattered_waterway", "field_boss"],
            HomeworkBoardLayout.Cells.Select(c => c.Id));
        Assert.Equal(
            ["요일던전", "길드의뢰", "뱅가드", "카브락", "에이렐", "화이트 서큐버스", "허상의 정박지", "광기의 동굴", "흩어진 물길", "필드보스"],
            HomeworkBoardLayout.Cells.Select(c => c.Label));
        // 좁게·보통 머리의 두 글자 약칭
        Assert.Equal(["요일", "길드", "뱅가", "카브", "에이", "화이", "허상", "광기", "물길", "보스"], HomeworkBoardLayout.Cells.Select(c => c.Short));
        Assert.All(HomeworkBoardLayout.Cells, c => Assert.Equal(2, c.Short.Length));
        // 필드 보스 여섯: 웹앱 BOSSES와 같은 id·순서
        Assert.Equal(
            [("fieldboss_peri", "페리"), ("fieldboss_crabbach", "크라브바흐"), ("fieldboss_krama", "크라마"),
             ("fieldboss_drochenem", "드로흐에넴"), ("fieldboss_tormog", "토르모그"), ("fieldboss_angrbahan", "앙그르바한")],
            HomeworkBoardLayout.Bosses.Select(b => (b.Id, b.Title)));
    }

    [Fact]
    public void Layout_TheCatalogKnowsEveryCellId_AndTheBossPool()
    {
        var catalog = HomeworkCatalog.LoadEmbedded();
        foreach (var c in HomeworkBoardLayout.Cells.Where(c => !c.IsBoss)) Assert.NotNull(catalog.Find(c.Id));
        foreach (var b in HomeworkBoardLayout.Bosses)
        {
            var def = catalog.Find(b.Id);
            Assert.NotNull(def);
            Assert.Equal(HomeworkBoardLayout.FieldBossPool, def!.Pool);
            Assert.Equal(b.Title, def.Title);
        }
    }

    // ── 칸 만들기 ──

    [Fact]
    public void CellsOf_AlwaysTenCells_MissingStatusIsUnknown()
    {
        var cells = HomeworkBoardLayout.CellsOf(Char("a"));
        Assert.Equal(10, cells.Count);
        Assert.All(cells, c => Assert.Equal(CellState.Unknown, c.State));
        Assert.All(cells, c => Assert.Null(c.Left));
    }

    [Fact]
    public void CellsOf_MapsStatesEvidenceAndRemaining()
    {
        var c = Char("a", hw:
        [
            St("daily_day_dungeon", "done", evidence: "퀘스트가 사라짐"),
            St("weekly_guild_regular", "todo", evidence: "정기 의뢰 (2)"),
            St("weekly_vanguard_breach", "todo", remaining: 2),
            St("raid_cavrak", "unknown", remaining: 5),       // 미확인이면 남은 횟수는 버린다
            St("raid_airel", "done", remaining: 1),           // 완료면 남은 횟수는 버린다
        ]);
        var cells = HomeworkBoardLayout.CellsOf(c).ToDictionary(x => x.Def.Id);
        Assert.Equal((CellState.Done, null, "퀘스트가 사라짐"), (cells["daily_day_dungeon"].State, cells["daily_day_dungeon"].Left, cells["daily_day_dungeon"].Evidence));
        Assert.Equal(CellState.Todo, cells["weekly_guild_regular"].State);
        Assert.Equal((CellState.Todo, 2), (cells["weekly_vanguard_breach"].State, cells["weekly_vanguard_breach"].Left));
        Assert.Equal((CellState.Unknown, null), (cells["raid_cavrak"].State, cells["raid_cavrak"].Left));
        Assert.Equal((CellState.Done, null), (cells["raid_airel"].State, cells["raid_airel"].Left));
        Assert.Equal(CellState.Unknown, cells["raid_white_succubus"].State);
    }

    [Fact]
    public void CellsOf_SuggestionOnlyWhileNotDone()
    {
        var sug = new HomeworkSuggestion("raidTokenIncreased", "원정의 증거: 카브락 레이드", 3, 4);
        var c = Char("a", hw: [St("raid_cavrak", "todo", suggestion: sug), St("raid_airel", "done", suggestion: sug), St("raid_white_succubus", "unknown", suggestion: sug)]);
        var cells = HomeworkBoardLayout.CellsOf(c).ToDictionary(x => x.Def.Id);
        Assert.Equal(sug, cells["raid_cavrak"].Suggestion);
        Assert.Null(cells["raid_airel"].Suggestion);
        Assert.Equal(sug, cells["raid_white_succubus"].Suggestion);
    }

    [Fact]
    public void CellsOf_FieldBoss_StateAndTheBossThatWasCleared()
    {
        var done = HomeworkBoardLayout.CellsOf(Char("a", boss: new HomeworkPoolStatus("done", "크라마"))).Last();
        Assert.Equal((CellState.Done, "크라마"), (done.State, done.Note));
        var todo = HomeworkBoardLayout.CellsOf(Char("a", boss: new HomeworkPoolStatus("todo", null))).Last();
        Assert.Equal((CellState.Todo, null), (todo.State, todo.Note));
        var unknown = HomeworkBoardLayout.CellsOf(Char("a")).Last();
        Assert.Equal(CellState.Unknown, unknown.State);
    }

    [Fact]
    public void CellsOf_InProgress_FollowsTheCharactersCurrentArea()
    {
        var abyss = HomeworkBoardLayout.CellsOf(Char("a", current: true, inProgress: ["abyss_illusory_anchorage"]));
        Assert.Equal(["abyss_illusory_anchorage"], abyss.Where(c => c.InProgress).Select(c => c.Def.Id));
        // 필드 보스 지역: 여섯 보스 어느 쪽이든 필드보스 칸 하나가 진행 중
        var boss = HomeworkBoardLayout.CellsOf(Char("a", current: true, inProgress: ["fieldboss_tormog"]));
        Assert.Equal(["field_boss"], boss.Where(c => c.InProgress).Select(c => c.Def.Id));
        Assert.DoesNotContain(HomeworkBoardLayout.CellsOf(Char("b")), c => c.InProgress);
    }

    // ── 칸 글·툴팁 ──

    [Fact]
    public void StatusText_ShowsRemainingCount()
    {
        string Text(string state, int? left) => HomeworkBoardLayout.StatusText(new CellData(HomeworkBoardLayout.Cells[2], state switch { "done" => CellState.Done, "todo" => CellState.Todo, _ => CellState.Unknown }, left, null, null, null, false));
        Assert.Equal("완료", Text("done", null));
        Assert.Equal("미완료", Text("todo", null));
        Assert.Equal("남은 2회", Text("todo", 2));
        Assert.Equal("미확인", Text("unknown", null));
    }

    [Fact]
    public void Tooltip_NamesTheCellAndItsEvidence_OrManual()
    {
        var cavrak = HomeworkBoardLayout.Cells.Single(c => c.Id == "raid_cavrak");
        Assert.StartsWith("카브락 · 완료 — 주간 미션 '악룡' 1/1", HomeworkBoardLayout.TooltipOf(new CellData(cavrak, CellState.Done, null, "주간 미션 '악룡' 1/1", null, null, false)));
        Assert.Contains("카브락 · 완료 — 수동", HomeworkBoardLayout.TooltipOf(new CellData(cavrak, CellState.Done, null, null, null, null, false)));
        Assert.Contains("누르면 미완료로 바꿉니다", HomeworkBoardLayout.TooltipOf(new CellData(cavrak, CellState.Done, null, null, null, null, false)));
        Assert.Contains("아직 확인 전", HomeworkBoardLayout.TooltipOf(new CellData(cavrak, CellState.Unknown, null, null, null, null, false)));
        Assert.Contains("누르면 완료로 바꿉니다", HomeworkBoardLayout.TooltipOf(new CellData(cavrak, CellState.Todo, null, null, null, null, false)));
        var vanguard = HomeworkBoardLayout.Cells.Single(c => c.Id == "weekly_vanguard_breach");
        Assert.Contains("뱅가드 · 남은 2회", HomeworkBoardLayout.TooltipOf(new CellData(vanguard, CellState.Todo, 2, null, null, null, false)));
    }

    [Fact]
    public void Tooltip_FieldBossShowsTheClearedBoss_SuggestionAndProgress()
    {
        var boss = HomeworkBoardLayout.Cells.Last();
        Assert.Contains("넘긴 보스: 크라마", HomeworkBoardLayout.TooltipOf(new CellData(boss, CellState.Done, null, null, "크라마", null, false)));
        Assert.Contains("넘긴 보스: 알 수 없음", HomeworkBoardLayout.TooltipOf(new CellData(boss, CellState.Done, null, null, null, null, false)));
        Assert.Contains("누르면 넘긴 보스를 고릅니다", HomeworkBoardLayout.TooltipOf(new CellData(boss, CellState.Todo, null, null, null, null, false)));

        var airel = HomeworkBoardLayout.Cells.Single(c => c.Id == "raid_airel");
        var tip = HomeworkBoardLayout.TooltipOf(new CellData(airel, CellState.Todo, null, null, null, new HomeworkSuggestion("raidTokenIncreased", "하프 조각", 3, 4), true));
        Assert.Contains("완료 제안: 하프 조각이(가) 3에서 4(으)로 늘었습니다", tip);
        Assert.Contains("진행 중", tip);
        Assert.Contains("레이드 지역(먼 바다의 바위 협곡)", HomeworkBoardLayout.SuggestionText(new HomeworkSuggestion("raidMapEntered", "먼 바다의 바위 협곡", 0, 0)));
    }

    // ── 직업 뱃지 ──

    [Theory]
    [InlineData("전사", JobPalette.Warrior)]
    [InlineData("대검 전사", JobPalette.Warrior)]   // 공백은 무시한다
    [InlineData("궁수", JobPalette.Archer)]
    [InlineData("격투가", JobPalette.Thief)]
    [InlineData("화염술사", JobPalette.Mage)]
    [InlineData("댄서", JobPalette.Bard)]
    [InlineData("사제", JobPalette.Healer)]
    public void JobPalette_KnownJobsUseTheFixedTable(string job, string color) => Assert.Equal(color, JobPalette.ColorOf(job));

    [Fact]
    public void JobPalette_UnknownJobs_GetAStableColorFromTheirName()
    {
        var first = JobPalette.ColorOf("연금술사");
        Assert.Contains(first, JobPalette.Fallback);
        Assert.Equal(first, JobPalette.ColorOf("연금술사"));         // 매번 같다
        Assert.Equal(first, JobPalette.ColorOf(" 연금 술사 "));      // 공백을 무시한 이름으로 정한다
        // 이름이 다르면 (대체로) 다른 색이 나오도록 퍼진다
        var names = new[] { "연금술사", "검사", "수호자", "정령사", "탐험가", "광대", "소환사", "투사" };
        Assert.True(names.Select(JobPalette.ColorOf).Distinct().Count() >= 3);
        Assert.Equal(JobPalette.Fallback[0], JobPalette.ColorOf(null));
    }

    [Fact]
    public void JobPalette_Letters_AndTheContrastWithTheLetterColor()
    {
        Assert.Equal("격", JobPalette.LetterOf("격투가"));
        Assert.Equal("?", JobPalette.LetterOf(""));
        Assert.Equal("?", JobPalette.LetterOf(null));
        var all = new[] { JobPalette.Warrior, JobPalette.Archer, JobPalette.Thief, JobPalette.Mage, JobPalette.Bard, JobPalette.Healer }.Concat(JobPalette.Fallback);
        foreach (var color in all)
            Assert.True(LayoutTests.Contrast(JobPalette.LetterColor, color) >= 4.5, $"{color}: {LayoutTests.Contrast(JobPalette.LetterColor, color):0.00}");
    }

    // ── 값 서식 ──

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(21350, "21,350")]
    [InlineData(1234567, "1,234,567")]
    public void Thousands_AddsSeparators(long n, string text) => Assert.Equal(text, CardFormat.Thousands(n));

    [Theory]
    [InlineData(9999, "9,999")]
    [InlineData(10000, "1만")]
    [InlineData(88737, "8.9만")]
    [InlineData(99000, "9.9만")]
    [InlineData(215000, "21.5만")]
    public void CompactCombat_UsesManUnits(long n, string text) => Assert.Equal(text, CardFormat.CompactCombat(n));

    [Fact]
    public void Ago_RelativeTimes()
    {
        Assert.Equal("접속 중", CardFormat.Ago(Now.AddDays(-9), Now, live: true));
        Assert.Equal("방금", CardFormat.Ago(Now.AddSeconds(-20), Now));
        Assert.Equal("5분 전", CardFormat.Ago(Now.AddMinutes(-5), Now));
        Assert.Equal("3시간 전", CardFormat.Ago(Now.AddHours(-3), Now));
        Assert.Equal("2일 전", CardFormat.Ago(Now.AddDays(-2).AddHours(-1), Now));
        Assert.Equal("방금", CardFormat.Ago(Now.AddMinutes(5), Now));   // 시계가 어긋나도 미래 시각을 보이지 않는다
    }

    [Theory]
    [InlineData(0.2, "1분 미만")]
    [InlineData(45, "45분")]
    [InlineData(60, "1시간")]
    [InlineData(200, "3시간 20분")]
    [InlineData(1500, "1일 1시간")]
    [InlineData(2880, "2일")]
    public void Duration_Text(double minutes, string text) => Assert.Equal(text, CardFormat.Duration(minutes));

    [Fact]
    public void Coin_Live_ShowsTheHeldAmount_WithoutAnEstimateTag()
    {
        var m = CardFormat.Coin(MeterKind.Silver, Coin(MeterKind.Silver, 20, Now, live: true), live: true, Now, Now);
        Assert.Equal("20/100", m.ValueText);
        Assert.False(m.Estimated);
        Assert.Equal(0.2, m.Fraction, 3);
        Assert.Equal(MeterLevel.Normal, m.Level);   // 80개 × 30분 = 40시간: 24시간 안에 차지 않는다
    }

    [Fact]
    public void Coin_NotLive_IsAnEstimate_ChargedByElapsedTime()
    {
        // 30분에 1개: 3시간 전에 40개 → 지금 46개
        var seen = Now.AddHours(-3);
        var m = CardFormat.Coin(MeterKind.Silver, Coin(MeterKind.Silver, 40, seen), live: false, seen, Now);
        Assert.Equal("46/100 예상", m.ValueText);
        Assert.True(m.Estimated);
        Assert.Contains("마지막으로 본 값 40 → 지금 예상 46", m.Tooltip);
        Assert.Contains("가득까지 약 1일 3시간", m.Tooltip);   // (100-46)개 × 30분 = 27시간
        Assert.Equal(MeterLevel.Normal, m.Level);          // 24시간보다 오래 걸리면 경고 없음
    }

    [Fact]
    public void Coin_FillingWithin24Hours_WarnsBeforeItIsFull_AndFullIsDanger()
    {
        var seen = Now.AddHours(-1);
        var near = CardFormat.Coin(MeterKind.Silver, Coin(MeterKind.Silver, 80, seen), false, seen, Now);   // 82개, 18개 남음 = 9시간
        Assert.Equal(MeterLevel.Warn, near.Level);
        Assert.True(near.HasBar);
        Assert.Contains("가득까지 약 9시간", near.Tooltip);

        var full = CardFormat.Coin(MeterKind.Silver, Coin(MeterKind.Silver, 100, seen), false, seen, Now);
        Assert.Equal(MeterLevel.Danger, full.Level);
        Assert.Equal("100/100", full.ValueText);              // 이미 가득: 늘지 않으니 예상 표시도 없다
        Assert.False(full.Estimated);
        Assert.Contains("이미 가득 찼습니다", full.Tooltip);
    }

    [Fact]
    public void Coin_Tribute_UsesTwelveHourSteps_AndKeepsRisingAsTimePasses()
    {
        var seen = Now.AddHours(-30);   // 12시간에 1개 → 2개 충전
        var coin = Coin(MeterKind.Tribute, 3, seen);
        var m1 = CardFormat.Coin(MeterKind.Tribute, coin, false, seen, Now);
        Assert.Equal("5/10 예상", m1.ValueText);
        // 30초 타이머가 시간을 흘려 다시 계산하면 값이 오른다
        var m2 = CardFormat.Coin(MeterKind.Tribute, coin, false, seen, Now.AddHours(12));
        Assert.Equal("6/10 예상", m2.ValueText);
    }

    [Theory]
    [InlineData(500, 1000, MeterLevel.Normal, "500/1,000 kg")]
    [InlineData(949, 1000, MeterLevel.Normal, "949/1,000 kg")]
    [InlineData(950, 1000, MeterLevel.Warn, "950/1,000 kg")]      // 95%↑ 주의
    [InlineData(999, 1000, MeterLevel.Warn, "999/1,000 kg")]
    [InlineData(1000, 1000, MeterLevel.Danger, "1,000/1,000 kg")] // 100%↑ 위험
    [InlineData(1100, 1000, MeterLevel.Danger, "1,100/1,000 kg")]
    public void Weight_Levels(double cur, double max, MeterLevel level, string text)
    {
        var m = CardFormat.Weight(WeightView.Of(cur, max));
        Assert.Equal(level, m.Level);
        Assert.Equal(text, m.ValueText);
        Assert.True(m.HasBar);
        Assert.InRange(m.Fraction, 0, 1);
    }

    [Fact]
    public void Weight_UnknownMax_ShowsOnlyTheCurrentWeight_NoBar()
    {
        var m = CardFormat.Weight(WeightView.Of(854.5651, null));
        Assert.Equal("854.6 kg", m.ValueText);
        Assert.False(m.HasBar);
        Assert.Equal(MeterLevel.Normal, m.Level);
        Assert.Contains("최대 무게를 아직 읽지 못했습니다", m.Tooltip);
        var zeroMax = CardFormat.Weight(WeightView.Of(300, 0));   // 0은 모르는 값
        Assert.False(zeroMax.HasBar);
    }

    [Fact]
    public void Weight_NoRecord_IsADash()
    {
        var m = CardFormat.Weight(null);
        Assert.Equal("—", m.ValueText);
        Assert.False(m.HasBar);
    }

    // ── 배치 단계 비율(4K) ──

    [Theory]
    [InlineData(LayoutTier.Wide, 1280, 1.0)]
    [InlineData(LayoutTier.Wide, 1200, 1.0)]
    [InlineData(LayoutTier.Wide, 1408, 1.1)]
    [InlineData(LayoutTier.Wide, 1536, 1.2)]
    [InlineData(LayoutTier.Wide, 2560, 1.2)]            // 최대 1.2배
    [InlineData(LayoutTier.Normal, 1199, 1.0)]
    [InlineData(LayoutTier.Narrow, 800, 1.0)]
    public void WideScale_GrowsOnlyBeyond1280_UpToOnePointTwo(LayoutTier tier, double width, double scale) =>
        Assert.Equal(scale, LayoutTiers.WideScale(tier, width), 2);

    [Fact]
    public void WideScale_NaNWidth_IsOne() => Assert.Equal(1.0, LayoutTiers.WideScale(LayoutTier.Wide, double.NaN));
}
