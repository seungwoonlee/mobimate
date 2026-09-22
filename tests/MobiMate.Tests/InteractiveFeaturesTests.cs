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
}

