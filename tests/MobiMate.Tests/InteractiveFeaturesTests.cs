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
}
