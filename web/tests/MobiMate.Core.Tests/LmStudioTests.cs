using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MobiMate.Core.Tests;

public class LmStudioTests
{
    private sealed class FakeLm(params string[] models) : LmStudioService
    {
        public override Task<List<string>> GetModelsAsync(CancellationToken ct = default) => Task.FromResult(models.ToList());
    }

    private sealed class NoOllamaSvc : OllamaService
    {
        public override Task<List<string>> GetInstalledModelsAsync(CancellationToken ct = default) => Task.FromResult(new List<string>());
    }

    [Fact]
    public void ParseModels_SkipsEmbeddingModels()
    {
        const string json = "{\"data\":[{\"id\":\"qwen/qwen3.5-9b\"},{\"id\":\"text-embedding-nomic-embed-text-v1.5\"},{\"id\":\"google/gemma-4-12b-qat\"},{}],\"object\":\"list\"}";
        Assert.Equal(new[] { "qwen/qwen3.5-9b", "google/gemma-4-12b-qat" }, LmStudioService.ParseModels(json));
    }

    [Fact]
    public void ParseDelta_ReadsContent_AndIgnoresOtherChunks()
    {
        Assert.Equal("안녕", LmStudioService.ParseDelta("{\"choices\":[{\"delta\":{\"content\":\"안녕\"}}]}"));
        Assert.Null(LmStudioService.ParseDelta("{\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}"));
        Assert.Null(LmStudioService.ParseDelta("{\"choices\":[]}"));
        Assert.Null(LmStudioService.ParseDelta("not json"));
    }

    [Fact]
    public async Task Manager_ListsLmStudioModels_AsFreeLocalEngines()
    {
        var m = new AiEngineManager(new NoOllamaSvc(), (_, _) => Task.FromResult<string?>(null), new FakeLm("qwen/qwen3.5-9b"));
        var engines = await m.DiscoverEnginesAsync();
        var lm = Assert.Single(engines, e => e.Info.Type == AiEngineType.LmStudio);
        Assert.Equal("lmstudio:qwen/qwen3.5-9b", lm.Info.Id);
        Assert.Equal(AiCostTier.LocalFree, lm.Info.CostTier);
        Assert.Equal("builtin:guide", m.CurrentEngine!.Info.Id);   // 자동 선택은 하지 않는다
    }

    [Fact]
    public async Task Manager_RestoresLastUsedLmStudioModel()
    {
        var m = new AiEngineManager(new NoOllamaSvc(), (_, _) => Task.FromResult<string?>(null), new FakeLm("a/b"));
        await m.DiscoverEnginesAsync(default, "lmstudio:a/b");
        Assert.Equal("lmstudio:a/b", m.CurrentEngine!.Info.Id);
    }

    [Fact]
    public async Task Manager_WithoutLmStudio_HasNoLmStudioEngines()
    {
        var m = new AiEngineManager(new NoOllamaSvc(), (_, _) => Task.FromResult<string?>(null));
        Assert.DoesNotContain(await m.DiscoverEnginesAsync(), e => e.Info.Type == AiEngineType.LmStudio);
    }
}

public class OllamaCloudTierTests
{
    [Theory]
    [InlineData("glm-5.3:cloud", true)]
    [InlineData("gemma4:31b-cloud", true)]
    [InlineData("gemma4:e4b", false)]
    public void CloudModels_AreAccountBilled(string model, bool paid)
    {
        var e = new OllamaAiEngine(model, new OllamaService());
        Assert.Equal(paid ? AiCostTier.Paid : AiCostTier.LocalFree, e.Info.CostTier);
        Assert.Equal(!paid, e.Info.IsZeroCost);
    }
}
