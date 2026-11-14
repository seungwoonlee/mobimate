using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MobiMate;

/// <summary>
/// 로컬 Ollama, 시스템 설치형 CLI 에이전트, 내장 마비 가이드 엔진을 통합 관리하고
/// 100% 무료 엔진 우선순위 정책에 따라 자동 감지 및 분기하는 오케스트레이터입니다.
/// </summary>
public class AiEngineManager
{
    private readonly OllamaService _ollamaService = new();
    private readonly List<IAiEngine> _engines = new();
    private IAiEngine? _currentEngine;

    public IReadOnlyList<IAiEngine> AvailableEngines => _engines;
    public IAiEngine? CurrentEngine => _currentEngine;

    public async Task<List<IAiEngine>> DiscoverEnginesAsync(CancellationToken ct = default)
    {
        _engines.Clear();

        // 1. [기본값] 내장 마비 가이드 엔진 (상시 1순위 등록, 기본값: AI 사용하지 않음)
        var builtInGuide = new BuiltInGuideEngine();
        _engines.Add(builtInGuide);

        // 2. [유료 AI 후보] 시스템 설치형 CLI 에이전트 프로빙 (Claude, Codex, Antigravity 등)
        var cliCandidates = new[]
        {
            ("claude", "🟣 Claude Code CLI (유료)"),
            ("codex", "🟢 OpenAI Codex CLI (유료)"),
            ("agy", "🤖 Antigravity CLI (유료)")
        };

        foreach (var (cmd, friendlyName) in cliCandidates)
        {
            var path = await ProbeCliAsync(cmd, ct);
            if (!string.IsNullOrEmpty(path))
            {
                _engines.Add(new CliAgentEngine(cmd, path, friendlyName));
            }
        }

        // 3. [무료 AI] 로컬 Ollama 모델 프로빙 (타임아웃 1.5초)
        try
        {
            using var ollamaCts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            using var linkedOllama = CancellationTokenSource.CreateLinkedTokenSource(ct, ollamaCts.Token);
            var ollamaModels = await _ollamaService.GetInstalledModelsAsync(linkedOllama.Token);

            foreach (var model in ollamaModels)
            {
                _engines.Add(new OllamaAiEngine(model, _ollamaService));
            }
        }
        catch { }

        // 4. 기본 엔진 자동 선정: AI 설치 여부와 무관하게 [기본값: AI 사용하지 않음] 고정
        _currentEngine = builtInGuide;

        return _engines;
    }

    public void SetCurrentEngine(string engineId)
    {
        var found = _engines.FirstOrDefault(e => e.Info.Id == engineId);
        if (found != null)
        {
            _currentEngine = found;
        }
    }

    public async Task<AiResponse> AskCurrentEngineAsync(string prompt, string gameContext, CancellationToken ct = default)
    {
        if (_currentEngine == null)
        {
            return new AiResponse(false, "", "선택된 AI 엔진이 없습니다.");
        }

        return await _currentEngine.GenerateResponseAsync(prompt, gameContext, ct);
    }

    private static async Task<string?> ProbeCliAsync(string command, CancellationToken ct)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(command);

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var outputTask = proc.StandardOutput.ReadLineAsync(linked.Token);
            await proc.WaitForExitAsync(linked.Token);

            if (proc.ExitCode == 0)
            {
                var line = await outputTask;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return line.Trim();
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
