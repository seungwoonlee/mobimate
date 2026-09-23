using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MobiMate.Tests;

public class InteractiveFeaturesTests
{
    [Fact]
    public async Task AiEngineManager_DiscoversEngines_WithCorrectPriorityOrder()
    {
        var manager = new AiEngineManager();
        var engines = await manager.DiscoverEnginesAsync(CancellationToken.None);

        Assert.NotEmpty(engines);

        // 1순위: 내장 기본 가이드 엔진이어야 함 (AI 사용하지 않음)
        Assert.Equal(AiEngineType.BuiltInGuide, engines[0].Info.Type);
        Assert.Equal("builtin:guide", engines[0].Info.Id);

        // 현재 선택된 엔진도 1순위 기본값이어야 함
        Assert.NotNull(manager.CurrentEngine);
        Assert.Equal(AiEngineType.BuiltInGuide, manager.CurrentEngine!.Info.Type);

        // 유료 CLI 엔진들은 Ollama보다 앞에 배치되어야 함
        var firstCliIdx = -1;
        var firstOllamaIdx = -1;
        for (int i = 0; i < engines.Count; i++)
        {
            if (engines[i].Info.Type == AiEngineType.CliAgent && firstCliIdx == -1) firstCliIdx = i;
            if (engines[i].Info.Type == AiEngineType.Ollama && firstOllamaIdx == -1) firstOllamaIdx = i;
        }

        if (firstCliIdx != -1 && firstOllamaIdx != -1)
        {
            Assert.True(firstCliIdx < firstOllamaIdx, "유료 CLI 에이전트가 무료 Ollama보다 상위에 정렬되어야 합니다.");
        }
    }

    [Theory]
    [InlineData(850.0, 1000.0, "정상", false, false)]  // 85% -> 정상
    [InlineData(949.0, 1000.0, "정상", false, false)]  // 94.9% -> 정상
    [InlineData(950.0, 1000.0, "주의", true, false)]   // 95.0% -> 주의
    [InlineData(990.0, 1000.0, "주의", true, false)]   // 99.0% -> 주의
    [InlineData(1000.0, 1000.0, "과적", false, true)]  // 100.0% -> 과적
    [InlineData(1050.0, 1000.0, "과적", false, true)]  // 105.0% -> 과적
    public void WeightThreshold_ClassifiesCorrectly(double current, double max, string expectedState, bool isWarning, bool isOverload)
    {
        var pct = (current / max) * 100.0;
        string state;
        if (pct >= 100.0) state = "과적";
        else if (pct >= 95.0) state = "주의";
        else state = "정상";

        Assert.Equal(expectedState, state);
        Assert.Equal(isWarning, state == "주의");
        Assert.Equal(isOverload, state == "과적");
    }

    [Fact]
    public void InventoryDietFilter_FiltersUnlockedBagItemsAndSortsByCount()
    {
        var items = new List<ItemData>
        {
            new("Bag", "사과", "Material", 10, false),
            new("Bag", "다이아몬드", "Equipment", 1, true),          // 잠김 -> 다이어트 대상 제외
            new("Bag", "철광석", "Material", 50, false),
            new("AccountStorage", "나무 장작", "Material", 30, false), // 계정창고 -> 가방 다이어트 제외
            new("Bag", "마나포션", "Consumable", 25, false),
            new("Bag", "에르그 결정", "Special", 100, true)          // 잠김 -> 다이어트 대상 제외
        };

        // 다이어트 필터 조건: Bag && !IsLocked
        var dietItems = items
            .Where(i => i.Location.Equals("Bag", StringComparison.OrdinalIgnoreCase) && !i.IsLocked)
            .OrderByDescending(i => i.Count)
            .ToList();

        Assert.Equal(3, dietItems.Count);
        Assert.Equal("철광석", dietItems[0].DisplayName);  // 50개 1위
        Assert.Equal("마나포션", dietItems[1].DisplayName); // 25개 2위
        Assert.Equal("사과", dietItems[2].DisplayName);    // 10개 3위
    }

    [Fact]
    public void MissionFiltering_ExcludesCompletedMissions_ShowsOnlyRemaining()
    {
        var missions = new List<MissionItem>
        {
            new("일일 던전 클리어", "던전 3회 완료", 3, 3, true, true),          // 3/3 완료 -> 제외
            new("채집 2회", "아무 채집이나 2회 수행", 2, 2, false, false),       // 2/2 달성 -> 제외
            new("동물 교감", "펫과 1회 교감", 0, 1, false, false),             // 0/1 잔여 -> 포함
            new("장비 분해", "장비 5개 분해", 1, 5, false, false),             // 1/5 잔여 -> 포함
            new("마을 주민 대화", "NPC와 1회 대화", 1, 1, true, false)         // 1/1 완료 -> 제외
        };

        // UI 필터링 규칙: !IsCompleted && CurrentCount < GoalCount
        var remainingMissions = missions
            .Where(m => !m.IsCompleted && m.CurrentCount < m.GoalCount)
            .ToList();

        Assert.Equal(2, remainingMissions.Count);
        Assert.Equal("동물 교감", remainingMissions[0].Title);
        Assert.Equal("장비 분해", remainingMissions[1].Title);
    }

    [Fact]
    public void ItemViewItem_DeltaAndDietSorting_PrioritizesIncreasedItems()
    {
        var items = new List<ItemViewItem>
        {
            new() { Location = "Bag", DisplayName = "나무 장작", Count = 100, InitialCount = 100 }, // Delta = 0
            new() { Location = "Bag", DisplayName = "철광석", Count = 60, InitialCount = 10 },     // Delta = 50 (급증)
            new() { Location = "Bag", DisplayName = "사과", Count = 15, InitialCount = 0 },         // Delta = 15 (급증)
            new() { Location = "Bag", DisplayName = "포션", Count = 5, InitialCount = 20 }          // Delta = 0 (감소)
        };

        Assert.Equal(50, items[1].DeltaCount);
        Assert.True(items[1].HasDelta);
        Assert.Equal("(+50개 급증 ▲)", items[1].DeltaBadge);

        Assert.Equal(0, items[0].DeltaCount);
        Assert.False(items[0].HasDelta);
        Assert.Equal("", items[0].DeltaBadge);

        // 다이어트 모드 정렬: 급증량 내림차순 -> 현재 수량 내림차순
        var dietSorted = items
            .OrderByDescending(i => i.DeltaCount)
            .ThenByDescending(i => i.Count)
            .ToList();

        Assert.Equal("철광석", dietSorted[0].DisplayName);  // Delta 50 1위
        Assert.Equal("사과", dietSorted[1].DisplayName);    // Delta 15 2위
        Assert.Equal("나무 장작", dietSorted[2].DisplayName); // Delta 0, Count 100 3위
        Assert.Equal("포션", dietSorted[3].DisplayName);     // Delta 0, Count 5 4위
    }

    [Theory]
    [InlineData(10, 50, 40, false)] // 보유 10 / 목표 50 -> 부족 40개, 미달성
    [InlineData(50, 50, 0, true)]   // 보유 50 / 목표 50 -> 부족 0개, 달성
    [InlineData(75, 50, 0, true)]   // 보유 75 / 목표 50 -> 부족 0개, 달성
    [InlineData(0, 100, 100, false)]// 보유 0 / 목표 100 -> 부족 100개, 미달성
    public void GatherableDisplayItem_ComputesNeededCountAndTargetReachedCorrectly(
        int currentBagCount, int targetCount, int expectedNeeded, bool expectedReached)
    {
        var item = new GatherableDisplayItem
        {
            DisplayName = "나무 장작",
            Category = "벌목",
            ToolOk = true,
            CurrentBagCount = currentBagCount,
            TargetCount = targetCount
        };

        Assert.Equal(expectedNeeded, item.NeededCount);
        Assert.Equal(expectedReached, item.IsTargetReached);
        Assert.Contains($"보유 {currentBagCount}", item.ProgressText);
        Assert.Contains($"목표 {targetCount}", item.ProgressText);
        Assert.Contains($"부족 {expectedNeeded}개", item.ProgressText);
    }

    [Fact]
    public void InventoryLocation_Mapping_And_MultipleSlotSum_CalculatesExactBagTotal()
    {
        var items = new List<ItemData>
        {
            new("inventory", "사과", "Ingredient", 100, false), // 가방 슬롯 1: 100개
            new("inventory", "사과", "Ingredient", 25, false),  // 가방 슬롯 2: 25개
            new("account_storage", "사과", "Ingredient", 50, false), // 계정창고: 50개 (가방 제외)
            new("character_storage", "사과", "Ingredient", 10, false), // 캐릭터창고: 10개 (가방 제외)
            new("Bag", "나무 장작", "Material", 30, false),      // 레거시 Bag 위치도 가방 인식
            new("inventory", "나무 장작", "Material", 40, false) // inventory 위치: 40개
        };

        static bool IsBag(string loc) =>
            loc.Equals("inventory", StringComparison.OrdinalIgnoreCase) || loc.Equals("bag", StringComparison.OrdinalIgnoreCase);

        // 가방 내 사과 총합: 100 + 25 = 125개
        var appleBagTotal = items.Where(i => IsBag(i.Location) && i.DisplayName == "사과").Sum(i => i.Count);
        Assert.Equal(125, appleBagTotal);

        // 가방 내 나무 장작 총합: 30 + 40 = 70개
        var woodBagTotal = items.Where(i => IsBag(i.Location) && i.DisplayName == "나무 장작").Sum(i => i.Count);
        Assert.Equal(70, woodBagTotal);
    }

    [Fact]
    public void AdaptiveRefreshController_DefaultState_Is15Seconds()
    {
        var controller = new AdaptiveRefreshController();
        Assert.Equal(15, controller.CurrentIntervalSec);
        Assert.False(controller.HasUserActivity);
    }

    [Fact]
    public void AdaptiveRefreshController_IncrementsBy15_WhenIdle_UpToMaxLimit()
    {
        var controller = new AdaptiveRefreshController();
        Assert.Equal(15, controller.CurrentIntervalSec);

        // 1st tick: 15 -> 30
        var t1 = controller.OnTick();
        Assert.Equal(30, t1);
        Assert.Equal(30, controller.CurrentIntervalSec);

        // 2nd tick: 30 -> 45
        var t2 = controller.OnTick();
        Assert.Equal(45, t2);

        // 3rd tick: 45 -> 60
        var t3 = controller.OnTick();
        Assert.Equal(60, t3);

        // Continuously tick up to max 300
        for (int i = 0; i < 30; i++)
        {
            controller.OnTick();
        }
        Assert.Equal(300, controller.CurrentIntervalSec);

        // Does not exceed 300
        var next = controller.OnTick();
        Assert.Equal(300, next);
        Assert.Equal(300, controller.CurrentIntervalSec);
    }

    [Fact]
    public void AdaptiveRefreshController_ResetsTo15_ImmediatelyOnUserActivity()
    {
        var controller = new AdaptiveRefreshController();
        controller.OnTick(); // 30
        controller.OnTick(); // 45
        controller.OnTick(); // 60
        Assert.Equal(60, controller.CurrentIntervalSec);

        // User interacts (keyboard or mouse)
        controller.RecordUserActivity();
        Assert.True(controller.HasUserActivity);
        Assert.Equal(15, controller.CurrentIntervalSec);

        // Tick after user activity keeps 15 and consumes activity flag
        var nextInterval = controller.OnTick();
        Assert.Equal(15, nextInterval);
        Assert.False(controller.HasUserActivity);

        // Next tick without activity increments to 30
        var idleInterval = controller.OnTick();
        Assert.Equal(30, idleInterval);
    }

    [Fact]
    public void AdaptiveRefreshController_Reset_RestoresInitialState()
    {
        var controller = new AdaptiveRefreshController();
        controller.OnTick();
        controller.OnTick();
        controller.RecordUserActivity();

        controller.Reset();
        Assert.Equal(15, controller.CurrentIntervalSec);
        Assert.False(controller.HasUserActivity);
    }

    [Theory]
    [InlineData("1040.0 / 1030.0 (101.0%)", true)]   // 소수점 101.0% -> 과적
    [InlineData("1030.0 / 1030.0 (100.0%)", true)]   // 정확히 100.0% -> 과적
    [InlineData("105%", true)]                       // 정수형 105% -> 과적
    [InlineData("1029.9 / 1030.0 (99.9%)", false)]   // 99.9% -> 과적 아님
    [InlineData("850.0 / 1000.0 (85.0%)", false)]    // 85.0% -> 과적 아님
    [InlineData("정상", false)]
    public void DetermineCategory_BagOverload_MatchesDecimalPercentage_W01(string weightSummary, bool isOverload)
    {
        var ctx = new ChatterContext("류트", "전사", 50, 10000, "휴식", "티르코네일", weightSummary, "10000");
        var category = PersonaTemplates.DetermineCategory(ctx);

        if (isOverload)
        {
            Assert.Equal("가방_과적", category);
        }
        else
        {
            Assert.NotEqual("가방_과적", category);
        }
    }

    [Theory]
    [InlineData("정지", true)]
    [InlineData("긴급 정지!", true)]
    [InlineData("멈춰", true)]
    [InlineData("스톱", true)]
    [InlineData("그만해", true)]
    [InlineData("그만해줘", true)]
    [InlineData("정지 기능 알려줘", false)]          // 질문 형태는 정지가 아니어야 함 (W-04)
    [InlineData("멈춰 있는 캐릭터는 어떻게 해?", false)]
    [InlineData("자동 정지 설정이 있나요?", false)]
    [InlineData("정지해선 안 돼", false)]
    public void CommandIntentParser_StopCommand_StrictMatching_W04(string input, bool expectedStop)
    {
        var intent = CommandIntentParser.Parse(input);
        Assert.Equal(expectedStop, intent.Kind == IntentKind.Stop);
    }

    [Theory]
    [InlineData("공주기사 스타일 페르소나 추가해줘", true, "공주기사 스타일")]
    [InlineData("츤데레 메이드 페르소나 만들어줘", true, "츤데레 메이드 스타일")]
    [InlineData("열혈용사 페르소나 등록", true, "열혈용사 스타일")]
    [InlineData("사과 10개 채집해줘", false, null)]
    [InlineData("오늘 일일 숙제 뭐야?", false, null)]
    public void CommandIntentParser_PersonaCreationIntent(string input, bool expectedCreate, string? expectedConcept)
    {
        var intent = CommandIntentParser.Parse(input);
        Assert.Equal(expectedCreate, intent.Kind == IntentKind.CreatePersona);
        if (expectedCreate && expectedConcept != null)
        {
            Assert.Equal(expectedConcept, intent.PersonaConcept);
        }
    }

    [Fact]
    public void PersonaGeneratorService_TryParseJson_ValidMarkdown()
    {
        var jsonText = "```json\n{\n  \"name\": \"공주기사 스타일\",\n  \"emoji\": \"🛡️\",\n  \"prompt\": \"기사도의 명예를 걸고 싸우는 당당한 공주기사.\"\n}\n```";
        var parsed = PersonaGeneratorService.TryParseJsonPersona(jsonText);

        Assert.NotNull(parsed);
        Assert.Equal("공주기사 스타일", parsed.Name);
        Assert.Equal("🛡️", parsed.TagEmoji);
        Assert.Equal("🛡️ 공주기사 스타일", parsed.DisplayName);
        Assert.Equal("기사도의 명예를 걸고 싸우는 당당한 공주기사.", parsed.SystemPrompt);
    }

    [Fact]
    public void PersonaGeneratorService_Fallback_PicksAppropriateEmoji()
    {
        var p1 = PersonaGeneratorService.GenerateFallbackPersona("공주기사");
        Assert.Equal("🛡️", p1.TagEmoji);
        Assert.Contains("공주기사", p1.Name);

        var p2 = PersonaGeneratorService.GenerateFallbackPersona("츤데레 메이드");
        Assert.Equal("🎀", p2.TagEmoji);

        var p3 = PersonaGeneratorService.GenerateFallbackPersona("대마법사");
        Assert.Equal("🔮", p3.TagEmoji);
    }

    [Theory]
    [InlineData("대검전사", "⚔️")]
    [InlineData("검방전사", "⚔️")]
    [InlineData("장궁궁수", "🏹")]
    [InlineData("원소술사", "🔮")]
    [InlineData("사제", "✝️")]
    [InlineData("격투가", "🗡️")]
    [InlineData("암살자", "🗡️")]
    [InlineData("음유시인", "🎵")]
    [InlineData("미등록직업", "⭐")]
    [InlineData("", "⭐")]
    public void MainWindow_GetJobIcon_ReturnsAppropriateEmoji(string? jobName, string expectedIcon)
    {
        var icon = MainWindow.GetJobIcon(jobName);
        Assert.Equal(expectedIcon, icon);
    }

    [Theory]
    [InlineData("골드", "💰 기본 통화")]
    [InlineData("M캐시", "💰 기본 통화")]
    [InlineData("다이아몬드", "💰 기본 통화")]
    [InlineData("정령의 날개", "⚔️ 성장 & 강화")]
    [InlineData("환생석", "⚔️ 성장 & 강화")]
    [InlineData("룬 조각", "⚔️ 성장 & 강화")]
    [InlineData("데카", "⚔️ 성장 & 강화")]
    [InlineData("냥 토큰", "🎫 토큰 & 교환")]
    [InlineData("길드 토큰", "🎫 토큰 & 교환")]
    [InlineData("레이드 증표", "🎫 토큰 & 교환")]
    [InlineData("신비한 큐브", "📦 기타 재화")]
    public void MainWindow_ClassifyCurrencyCategory_ClassifiesCorrectly(string currencyName, string expectedCategory)
    {
        var cat = MainWindow.ClassifyCurrencyCategory(currencyName);
        Assert.Equal(expectedCategory, cat);
    }

    [Fact]
    public void NearPcViewItem_SortingAndStrongerDetection()
    {
        long myCombatScore = 80000;
        var pcs = new List<NearPcItem>
        {
            new("아이라", "초보", 10.0, 50, "궁수", 70000, false, false, false, false),
            new("아이라", "영웅", 15.0, 100, "대검전사", 95000, true, false, false, false),
            new("아이라", "달인", 5.0, 90, "원소술사", 85000, false, false, false, false)
        };

        var viewItems = pcs
            .OrderByDescending(p => p.CombatScore)
            .ThenBy(p => p.Distance)
            .Select(p => NearPcViewItem.FromRaw(p, MainWindow.GetJobIcon(p.JobName), myCombatScore > 0 && p.CombatScore > myCombatScore))
            .ToList();

        // 1위: 95,000 (대검전사)
        Assert.Equal("대검전사", viewItems[0].JobName);
        Assert.Equal(95000, viewItems[0].CombatScore);
        Assert.True(viewItems[0].IsStronger);
        Assert.Equal("⚔️ 강력 (나보다 높음)", viewItems[0].StrongerBadge);
        Assert.True(viewItems[0].IsSameGuild);
        Assert.Equal("🛡️ 우리 길드원", viewItems[0].GuildBadge);
        Assert.Equal("⚔️", viewItems[0].JobIcon);
        Assert.Equal("⚔️ 대검전사 Lv.100", viewItems[0].JobWithLevel);
        Assert.Equal(BrushHelper.Red, viewItems[0].CombatScoreBrush);
        Assert.Equal(BrushHelper.Green, viewItems[0].GuildBadgeBrush);

        // 2위: 85,000 (원소술사)
        Assert.Equal("원소술사", viewItems[1].JobName);
        Assert.True(viewItems[1].IsStronger);
        Assert.False(viewItems[1].IsSameGuild);
        Assert.Equal("🔮", viewItems[1].JobIcon);
        Assert.Equal("🔮 원소술사 Lv.90", viewItems[1].JobWithLevel);
        Assert.Equal(BrushHelper.Red, viewItems[1].CombatScoreBrush);
        Assert.Equal(BrushHelper.Gray, viewItems[1].GuildBadgeBrush);

        // 3위: 70,000 (궁수)
        Assert.Equal("궁수", viewItems[2].JobName);
        Assert.False(viewItems[2].IsStronger);
        Assert.Empty(viewItems[2].StrongerBadge);
        Assert.Equal("🏹", viewItems[2].JobIcon);
        Assert.Equal("🏹 궁수 Lv.50", viewItems[2].JobWithLevel);
        Assert.Equal(BrushHelper.Gold, viewItems[2].CombatScoreBrush);
        Assert.Equal(BrushHelper.Gray, viewItems[2].GuildBadgeBrush);
    }

    [Fact]
    public void AlteringWorkItem_ConvenienceProperties()
    {
        var doneWork = new AlteringWorkItem("최고급 가죽", "방직기", "Completed", true, 0);
        Assert.True(doneWork.IsDone);
        Assert.Equal("수거 대기 ✅", doneWork.StatusText);
        Assert.Equal("#4EBA6F", doneWork.StatusColor);
        Assert.Equal(BrushHelper.Green, doneWork.StatusBrush);

        var ongoingWork = new AlteringWorkItem("철괴", "용광로", "Working", false, 45);
        Assert.False(ongoingWork.IsDone);
        Assert.Equal("45초 남음 ⏳", ongoingWork.StatusText);
        Assert.Equal("#F5D061", ongoingWork.StatusColor);
        Assert.Equal(BrushHelper.Gold, ongoingWork.StatusBrush);
    }
}

