using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MobiMate;

/// <summary>
/// 로컬에 실행 중인 Ollama의 특정 모델(gemma4, deepseek 등)과 통신하는 IAiEngine 구현체입니다.
/// </summary>
public class OllamaAiEngine : IAiEngine
{
    private readonly OllamaService _service;

    public AiEngineInfo Info { get; }

    public OllamaAiEngine(string modelName, OllamaService service)
    {
        _service = service;
        Info = new AiEngineInfo(
            Id: $"ollama:{modelName}",
            DisplayName: $"[무료/로컬] 🦙 Ollama: {modelName}",
            Type: AiEngineType.Ollama,
            TargetModel: modelName,
            Description: "완전 무료(0원), 로컬 PC GPU/CPU 기반 오프라인 LLM",
            IsZeroCost: true
        );
    }

    public async Task<AiResponse> GenerateResponseAsync(string prompt, string gameContext, CancellationToken ct = default)
    {
        var (success, reply, error) = await _service.AskAiAsync(Info.TargetModel, prompt, gameContext, ct);
        return new AiResponse(success, reply, error);
    }

    public IAsyncEnumerable<string> StreamAsync(string prompt, string gameContext, CancellationToken ct = default)
        => _service.AskAiStreamAsync(Info.TargetModel, prompt, gameContext, ct);
}
