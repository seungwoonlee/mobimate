using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MabiMate;

public class GameCliService
{
    private static readonly string DefaultCliPath = @"C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe";

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

    public async Task<(bool success, string output, string error)> RunRawAsync(string command, string[]? args = null, string? stdinJson = null)
    {
        if (!IsCliAvailable)
        {
            return (false, "", $"CLI 실행 파일을 찾을 수 없습니다: {CliPath}");
        }

        try
        {
            using var proc = new Process();
            proc.StartInfo.FileName = CliPath;
            proc.StartInfo.Arguments = args != null && args.Length > 0
                ? $"{command} {string.Join(" ", args)}"
                : command;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = stdinJson != null;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            proc.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            proc.StartInfo.CreateNoWindow = true;

            proc.Start();

            if (stdinJson != null)
            {
                await proc.StandardInput.WriteAsync(stdinJson);
                proc.StandardInput.Close();
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            await proc.WaitForExitAsync();

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            if (proc.ExitCode != 0 && string.IsNullOrEmpty(stdout))
            {
                return (false, stdout, string.IsNullOrEmpty(stderr) ? $"종료 코드 {proc.ExitCode}" : stderr);
            }

            return (true, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    public async Task<(bool success, T? data, string error)> RunJsonAsync<T>(string command, string[]? args = null, string? stdinJson = null)
    {
        var (ok, stdout, err) = await RunRawAsync(command, args, stdinJson);
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

    public async Task<(bool success, string message)> SendGameChatAsync(string message)
    {
        // write_chat 은 커맨드라인 인자로 메시지 전달 (따옴표 감싸기)
        var escaped = "\"" + message.Replace("\"", "\\\"") + "\"";
        var (ok, stdout, err) = await RunRawAsync("write_chat", new[] { escaped });
        if (!ok) return (false, err);

        return (true, stdout);
    }
}
