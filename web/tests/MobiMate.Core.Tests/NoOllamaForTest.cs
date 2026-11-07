using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MobiMate.Tests;

/// <summary>Ollama가 하나도 설치되지 않은 PC를 흉내 낸다. 이 PC의 실제 Ollama와 무관하게 시험하려고 쓴다.</summary>
internal sealed class NoOllamaForTest : OllamaService
{
    public override Task<List<string>> GetInstalledModelsAsync(CancellationToken ct = default) => Task.FromResult(new List<string>());
}
