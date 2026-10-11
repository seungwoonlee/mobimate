using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MobiMate;

/// <summary>
/// 시스템에 설치된 CLI 에이전트(agy, claude, codex 등)를 안전하게 호출하는 IAiEngine 구현체입니다.
/// ProcessStartInfo.ArgumentList 사용으로 커맨드 인젝션을 원천 차단하며,
/// entireProcessTree 강제 종료로 고아 프로세스 누수를 방지합니다.
/// </summary>
public class CliAgentEngine : IAiEngine
{
    private readonly string _executablePath;
    private readonly string _commandName;
    private static readonly SemaphoreSlim _cliLock = new(1, 1);

    public AiEngineInfo Info { get; }

    public CliAgentEngine(string commandName, string executablePath, string friendlyName)
    {
        _commandName = commandName.ToLowerInvariant();
        _executablePath = executablePath;

        Info = new AiEngineInfo(
            Id: $"cli:{_commandName}",
            DisplayName: $"[에이전트/계정] {friendlyName} ({_commandName})",
            Type: AiEngineType.CliAgent,
            TargetModel: _commandName,
            Description: "시스템 설치형 AI 에이전트 (사용자 계정/토큰 정책 적용, 수동 선택 전용)",
            IsZeroCost: false // 유료/크레딧 가능성으로 자동 기본 선택 완전 배제
        );
    }

    public async Task<AiResponse> GenerateResponseAsync(string prompt, string gameContext, CancellationToken ct = default)
    {
        await _cliLock.WaitAsync(ct);
        try
        {
            var combinedPrompt = $"[마비노기 모바일 상황]\n{gameContext}\n\n[사용자 질문]\n{prompt}\n\n간결하고 명확하게 마비노기 모바일 유저 관점에서 조언해주세요.";

            var psi = new ProcessStartInfo
            {
                FileName = _executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // 커맨드 인젝션 차단을 위한 ArgumentList 안전 추가
            if (_commandName == "claude")
            {
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add(combinedPrompt);
            }
            else if (_commandName == "agy")
            {
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add(combinedPrompt);
            }
            else if (_commandName == "codex")
            {
                psi.ArgumentList.Add("exec");
                psi.ArgumentList.Add(combinedPrompt);
            }
            else
            {
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add(combinedPrompt);
            }

            using var proc = new Process { StartInfo = psi };
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                return new AiResponse(false, "", $"에이전트 프로세스 시작 실패: {ex.Message}");
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(linkedCts.Token);

            try
            {
                await proc.WaitForExitAsync(linkedCts.Token);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (proc.ExitCode != 0)
                {
                    var err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    return new AiResponse(false, "", $"에이전트 실행 실패 (코드 {proc.ExitCode}): {err.Trim()}");
                }

                if (string.IsNullOrWhiteSpace(stdout))
                {
                    return new AiResponse(false, "", "에이전트로부터 빈 응답이 반환되었습니다.");
                }

                return new AiResponse(true, stdout.Trim());
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true); // 고아 프로세스 누수 완전 차단
                    }
                }
                catch { }

                return new AiResponse(false, "", "에이전트 응답 대기 시간이 초과되었습니다 (20초).");
            }
            catch (Exception ex)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch { }

                return new AiResponse(false, "", $"에이전트 통신 오류: {ex.Message}");
            }
        }
        finally
        {
            _cliLock.Release();
        }
    }
}
