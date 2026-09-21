using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MobiMate;

public class GameCliService
{
    private static readonly string DefaultCliPath = @"C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe";
    private readonly SemaphoreSlim _cliLock = new(1, 1);

    public string CliPath { get; private set; }

    public GameCliService()
    {
        var envPath = Environment.GetEnvironmentVariable("MABINOGI_MOBILE_CLI");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            CliPath = envPath;
        }
        else
        {
            CliPath = DefaultCliPath;
        }
    }

    public bool IsCliAvailable => File.Exists(CliPath);

    public async Task<(bool success, string output, string error)> RunRawAsync(
        string command,
        string[]? args = null,
        string? stdinJson = null,
        int timeoutSeconds = 5,
        CancellationToken ct = default)
    {
        if (!IsCliAvailable)
        {
            return (false, "", $"CLI 실행 파일을 찾을 수 없습니다: {CliPath}");
        }

        // 안전한 직렬화 큐: 동시 프로세스 경합 방지
        await _cliLock.WaitAsync(ct);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        Process? proc = null;
        try
        {
            proc = new Process();
            proc.StartInfo.FileName = CliPath;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = stdinJson != null;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            proc.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            proc.StartInfo.CreateNoWindow = true;

            // .NET ArgumentList로 OS 레벨 안전 인자 이스케이프 보장
            proc.StartInfo.ArgumentList.Add(command);
            if (args != null)
            {
                foreach (var arg in args)
                {
                    proc.StartInfo.ArgumentList.Add(arg);
                }
            }

            proc.Start();

            if (stdinJson != null)
            {
                await proc.StandardInput.WriteAsync(stdinJson.AsMemory(), linkedCts.Token);
                proc.StandardInput.Close();
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(linkedCts.Token);

            await proc.WaitForExitAsync(linkedCts.Token);

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            if (proc.ExitCode != 0 && string.IsNullOrEmpty(stdout))
            {
                return (false, stdout, string.IsNullOrEmpty(stderr) ? $"종료 코드 {proc.ExitCode}" : stderr);
            }

            return (true, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (proc != null && !proc.HasExited)
                {
                    proc.Kill(true); // 좀비 프로세스 방지
                }
            }
            catch { }

            return (false, "", "게임 CLI 응답 시간 초과 (타임아웃)");
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
        finally
        {
            proc?.Dispose();
            _cliLock.Release();
        }
    }

    public async Task<(bool success, T? data, string error)> RunJsonAsync<T>(
        string command,
        string[]? args = null,
        string? stdinJson = null,
        int timeoutSeconds = 5,
        CancellationToken ct = default)
    {
        var (ok, stdout, err) = await RunRawAsync(command, args, stdinJson, timeoutSeconds, ct);
        if (!ok) return (false, default, err);

        try
        {
            var data = JsonSerializer.Deserialize<T>(stdout, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return (true, data, "");
        }
        catch (Exception ex)
        {
            return (false, default, $"JSON 파싱 오류: {ex.Message} (응답: {stdout})");
        }
    }

    public async Task<(bool success, string message)> SendGameChatAsync(string message, CancellationToken ct = default)
    {
        // 서비스 레벨 방어 유효성 검증
        if (string.IsNullOrWhiteSpace(message))
        {
            return (false, "채팅 내용이 비어 있습니다.");
        }

        // 개행 문자 치환 및 50자 강제 방어
        var sanitized = message.Replace("\r", "").Replace("\n", " ").Trim();
        if (sanitized.Length > 50)
        {
            sanitized = sanitized[..50];
        }

        // ArgumentList로 안전하게 전달
        var (ok, stdout, err) = await RunRawAsync("write_chat", new[] { sanitized }, timeoutSeconds: 5, ct: ct);
        if (!ok) return (false, err);

        return (true, stdout);
    }
}
