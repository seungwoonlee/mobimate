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

    /// <summary>설치된 모델을 정해 주는 가짜 Ollama (이 PC의 실제 Ollama와 무관하게 시험한다)</summary>
    private sealed class FakeOllama(params string[] models) : OllamaService
    {
        public override Task<List<string>> GetInstalledModelsAsync(CancellationToken ct = default) => Task.FromResult(models.ToList());
    }

    private static AiEngineManager Manager(params string[] models) => new(new FakeOllama(models), (_, _) => Task.FromResult<string?>(null));

    [Fact]
    public async Task Default_IsGemma4E4b_WhenOllamaHasIt_AndNothingWasUsedBefore()
    {
        var m = Manager("qwen3.5:9b", "gemma4:e4b", "gemma4:31b-cloud");
        await m.DiscoverEnginesAsync(CancellationToken.None, null);
        Assert.Equal("ollama:gemma4:e4b", m.CurrentEngine!.Info.Id);
    }

    [Fact]
    public async Task Default_IsTheLastUsedEngine_EvenIfAnotherIsPreferredByTheAutoRule()
    {
        var m = Manager("gemma4:e4b", "qwen3.5:9b");
        await m.DiscoverEnginesAsync(CancellationToken.None, "ollama:qwen3.5:9b");
        Assert.Equal("ollama:qwen3.5:9b", m.CurrentEngine!.Info.Id);
    }

    [Fact]
    public async Task Default_FallsBackToTheAutoRule_WhenTheLastUsedEngineIsGoneOrPaid_AndNeverPicksCloud()
    {
        var gone = Manager("gemma4:e4b");
        await gone.DiscoverEnginesAsync(CancellationToken.None, "ollama:없는모델");
        Assert.Equal("ollama:gemma4:e4b", gone.CurrentEngine!.Info.Id);

        var cloudOnly = Manager("qwen3.5:cloud", "glm-5.3:cloud", "qwen2.5-coder:7b");
        await cloudOnly.DiscoverEnginesAsync(CancellationToken.None, null);
        Assert.Equal("builtin:guide", cloudOnly.CurrentEngine!.Info.Id);   // 클라우드·코드 전용 모델은 자동으로 고르지 않는다
    }

    [Theory]
    [InlineData("gemma4:e4b", "gemma4:12b", "gemma4:e4b")]
    [InlineData("qwen3.5:9b", "gemma4:e2b", "gemma4:e2b")]      // 9B·12B급은 자동으로 고르지 않는다 (4B급 이하만)
    [InlineData("gemma4:e2b", "x:cloud", "gemma4:e2b")]
    public void PickAutoOllama_FollowsThePriorityList(string a, string b, string expect) => Assert.Equal(expect, AiEngineManager.PickAutoOllama(new[] { b, a }));

    [Fact]
    public async Task Default_NeverAutoPicksABigModel_OnlyFourBillionClassOrSmaller()
    {
        var m = Manager("qwen3.5:9b", "gemma4:12b", "exaone3.5:7.8b");
        await m.DiscoverEnginesAsync(CancellationToken.None, null);
        Assert.Equal("builtin:guide", m.CurrentEngine!.Info.Id);
    }

    [Fact]
    public async Task Default_RestoresThePaidEngineTheUserUsedLast()
    {
        var m = new AiEngineManager(new FakeOllama("gemma4:e4b"), (cmd, _) => Task.FromResult<string?>(cmd == "claude" ? @"C:ake\claude.exe" : null));
        await m.DiscoverEnginesAsync(CancellationToken.None, "cli:claude");
        Assert.Equal("cli:claude", m.CurrentEngine!.Info.Id);
        await m.DiscoverEnginesAsync(CancellationToken.None, null);   // 기록이 없으면 유료는 자동으로 고르지 않는다
        Assert.Equal("ollama:gemma4:e4b", m.CurrentEngine!.Info.Id);
    }

    [Fact]
    public async Task DiscoverEngines_AlwaysIncludesBuiltInGuide_AndGuaranteesZeroCostDefault()
    {
        var manager = Manager();
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
        var manager = Manager();
        var engines = await manager.DiscoverEnginesAsync(CancellationToken.None);

        Assert.NotEmpty(engines);

        // 내장 가이드로 명시적 전환
        manager.SetCurrentEngine("builtin:guide");
        Assert.NotNull(manager.CurrentEngine);
        Assert.Equal("builtin:guide", manager.CurrentEngine.Info.Id);
    }
}
