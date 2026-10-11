using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MobiMate.Tests;

public class AiEngineTests
{
    [Fact]
    public async Task BuiltInGuideEngine_ReturnsValidAdvice_AndIsZeroCost()
    {
        var guide = new BuiltInGuideEngine();

        Assert.True(guide.Info.IsZeroCost);
        Assert.Equal(AiEngineType.BuiltInGuide, guide.Info.Type);

        // 1. 전투력 질의
        var combatRes = await guide.GenerateResponseAsync("전투력 빠르게 올리려면?", "직업: 전사, 전투력: 15,000점");
        Assert.True(combatRes.Success);
        Assert.Contains("룬 장착", combatRes.Reply);
        Assert.Contains("장비 각인", combatRes.Reply);

        // 2. 골드 질의
        var goldRes = await guide.GenerateResponseAsync("골드 파밍 어떻게 해?", "골드: 50,000");
        Assert.True(goldRes.Success);
        Assert.Contains("일일 미션", goldRes.Reply);
        Assert.Contains("생활 가공", goldRes.Reply);

        // 3. 채집 질의
        var gatherRes = await guide.GenerateResponseAsync("철광석 채집 위치 알려줘", "");
        Assert.True(gatherRes.Success);
        Assert.Contains("두갈드 아일", gatherRes.Reply);

        // 4. 캐릭터 진단 질의
        var diagRes = await guide.GenerateResponseAsync("현재 내 캐릭터 진단해줘", "- 직업: 격투가 (Lv.100)\n- 전투력: 88,000점\n- 가방 무게: 850/1000");
        Assert.True(diagRes.Success);
        Assert.Contains("진단 리포트", diagRes.Reply);
        Assert.Contains("격투가", diagRes.Reply);

        // 5. 가방/창고 정리 질의
        var warehouseRes = await guide.GenerateResponseAsync("가방 무게 초과 방지 및 계정 창고 정리법 알려줘", "");
        Assert.True(warehouseRes.Success);
        Assert.Contains("계정 창고", warehouseRes.Reply);
        Assert.Contains("원자재", warehouseRes.Reply);

        // 6. 응답 본문에 반복 팁 문구가 완전히 제거되었는지 검증
        Assert.DoesNotContain("💡 팁:", combatRes.Reply);
        Assert.DoesNotContain("💡 팁:", warehouseRes.Reply);
    }

    [Fact]
    public async Task DiscoverEngines_AlwaysIncludesBuiltInGuide_AndGuaranteesZeroCostDefault()
    {
        var manager = new AiEngineManager(probeCli: (_, _) => Task.FromResult<string?>(null));
        var engines = await manager.DiscoverEnginesAsync(CancellationToken.None);

        Assert.NotEmpty(engines);

        // 내장 가이드 엔진은 어떤 PC 환경에서도 무조건 포함되어야 함
        var builtIn = engines.FirstOrDefault(e => e.Info.Type == AiEngineType.BuiltInGuide);
        Assert.NotNull(builtIn);
        Assert.True(builtIn.Info.IsZeroCost);

        // 자동 선택된 기본 엔진은 의도치 않은 과금 방지를 위해 반드시 IsZeroCost == true 여야 하며, 무조건 BuiltInGuide여야 함
        var defaultEngine = manager.CurrentEngine;
        Assert.NotNull(defaultEngine);
        Assert.True(defaultEngine.Info.IsZeroCost, "자동 기본 선택 엔진은 반드시 완전 무료(IsZeroCost == true)여야 합니다.");
        Assert.Equal(AiEngineType.BuiltInGuide, defaultEngine.Info.Type);
    }

    [Fact]
    public void CliAgentEngine_ZeroCostIsFalse_ToPreventAutoSelection()
    {
        var dummyPath = @"C:\dummy\agy.exe";
        var cliEngine = new CliAgentEngine("agy", dummyPath, "Antigravity CLI");

        // CLI 에이전트는 사용자가 직접 선택해야만 하므로 IsZeroCost는 false여야 함
        Assert.False(cliEngine.Info.IsZeroCost);
        Assert.Equal(AiEngineType.CliAgent, cliEngine.Info.Type);
    }

    [Fact]
    public async Task SetCurrentEngine_SwitchesEngineCorrectly()
    {
        var manager = new AiEngineManager(probeCli: (_, _) => Task.FromResult<string?>(null));
        var engines = await manager.DiscoverEnginesAsync(CancellationToken.None);

        Assert.NotEmpty(engines);

        // 내장 가이드로 명시적 전환
        manager.SetCurrentEngine("builtin:guide");
        Assert.NotNull(manager.CurrentEngine);
        Assert.Equal("builtin:guide", manager.CurrentEngine.Info.Id);
    }
}
