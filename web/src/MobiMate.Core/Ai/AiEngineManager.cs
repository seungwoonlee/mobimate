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
    private readonly OllamaService _ollamaService;
    private readonly Func<string, CancellationToken, Task<string?>> _probeCli;

    public AiEngineManager(OllamaService? ollama = null, Func<string, CancellationToken, Task<string?>>? probeCli = null)
    {
        _ollamaService = ollama ?? new OllamaService();
        _probeCli = probeCli ?? ProbeCliAsync;
    }
    private readonly List<IAiEngine> _engines = new();
    private IAiEngine? _currentEngine;

    public IReadOnlyList<IAiEngine> AvailableEngines => _engines;
    public IAiEngine? CurrentEngine => _currentEngine;

    /// <param name="preferredEngineId">지난번 사용자가 고른 엔진 (FR-AI-02). 무료 엔진일 때만 자동 복원한다.
    /// 유료 CLI 엔진은 과금 방지를 위해 복원하지 않고 내장 가이드로 시작한다.</param>
    public async Task<List<IAiEngine>> DiscoverEnginesAsync(CancellationToken ct = default, string? preferredEngineId = null)
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
            var path = await _probeCli(cmd, ct);
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

        // 4. 기본 엔진 선정: ① 가장 최근에 사용한 엔진(유료 포함: 사용자가 직접 골랐던 것) ② 없으면 로컬 Ollama의 gemma4:e4b 또는 4B급 이하 모델 ③ 없으면 내장 가이드.
        //    처음 쓰는 PC에서 자동으로 고르는 것은 로컬 무료 모델뿐이다(유료 CLI·클라우드 모델은 자동 선택하지 않는다).
        _currentEngine = builtInGuide;
        var preferred = _engines.FirstOrDefault(e => e.Info.Id == preferredEngineId);
        if (preferred != null)
        {
            _currentEngine = preferred;
        }
        else if (PickAutoOllama(_engines.Where(e => e.Info.Type == AiEngineType.Ollama).Select(e => e.Info.TargetModel)) is { } auto)
        {
            _currentEngine = _engines.First(e => e.Info.Type == AiEngineType.Ollama && e.Info.TargetModel == auto);
        }

        return _engines;
    }

    /// <summary>추천 모델: 일반 PC에서도 돌아가는 4B급이고 한국어 답이 쓸 만하다. 엔진 목록에 "추천"으로 보인다.</summary>
    public const string RecommendedOllamaModel = "gemma4:e4b";

    /// <summary>자동 선택 후보(앞이 우선): 4B급 이하(용량 4GB 안팎)의 로컬 모델. 더 큰 모델·클라우드·코드 전용 모델은 자동으로 고르지 않는다.</summary>
    private static readonly string[] AutoOllamaOrder = { RecommendedOllamaModel, "qwen3.5:4b", "gemma4:e2b", "exaone3.5:2.4b", "qwen3.5:2b" };

    /// <summary>설치된 Ollama 모델 중 자동으로 쓸 것. 후보 목록에 있는 첫 모델, 없으면 null(내장 가이드).</summary>
    public static string? PickAutoOllama(IEnumerable<string?> installed)
    {
        var local = installed.Where(n => !string.IsNullOrWhiteSpace(n)
            && !n!.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) && !n.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase)).Select(n => n!).ToList();
        foreach (var want in AutoOllamaOrder)
        {
            var hit = local.FirstOrDefault(n => string.Equals(n, want, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return null;
    }

    public bool SetCurrentEngine(string engineId)
    {
        var found = _engines.FirstOrDefault(e => e.Info.Id == engineId);
        if (found == null) return false;
        _currentEngine = found;
        return true;
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
