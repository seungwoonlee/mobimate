using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MobiMate;

/// <summary>게임 CLI 호출 레인. 호출자가 고르지 않고 명령 이름으로 자동 결정된다.</summary>
public enum CliLane
{
    General,     // 조회·짧은 조작: 전역 단일 큐
    LongRunning, // execute_gathering (최대 120초): 동시 1건
    Priority     // stop_action·stand_up: 어떤 큐도 기다리지 않음
}

public sealed record CliCommand(
    string Name,
    IReadOnlyList<string>? Args = null,
    object? Body = null,
    TimeSpan? Timeout = null)
{
    public CliLane Lane => CliLanes.For(Name);
}

public sealed record CliResult(bool Ok, string Stdout, string? Error, TimeSpan QueueWait, TimeSpan Exec, bool TimedOut = false, bool Killed = false);

public interface IGameCli
{
    bool IsAvailable { get; }
    string CliPath { get; }
    Task<CliResult> RunAsync(CliCommand cmd, CancellationToken ct = default);
}

public static class CliLanes
{
    public static CliLane For(string command) => command switch
    {
        "execute_gathering" => CliLane.LongRunning,
        "stop_action" or "stand_up" => CliLane.Priority,
        _ => CliLane.General
    };

    public static TimeSpan DefaultTimeout(string command) => command switch
    {
        "execute_gathering" => TimeSpan.FromSeconds(120),
        "get_items" => TimeSpan.FromSeconds(5),
        "complete_altering_work" => TimeSpan.FromSeconds(6),
        _ => TimeSpan.FromSeconds(4)
    };
}

/// <summary>
/// MabinogiMobile_CLI.exe 실행기 (NFR-06, NFR-08).
/// 인자는 ArgumentList로만 전달하고, JSON 본문은 직렬화기로 만들어 마지막 인자로 넣는다(현행 CLI 규격).
/// </summary>
public sealed class GameCli : IGameCli
{
    public const string DefaultCliPath = @"C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe";
    public const string CliPathEnvVar = "MABINOGI_MOBILE_CLI";

    private static readonly JsonSerializerOptions BodyJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions ReadJson = new() { PropertyNameCaseInsensitive = true };

    private readonly SemaphoreSlim _general = new(1, 1);
    private readonly SemaphoreSlim _longRunning = new(1, 1);
    private readonly Dictionary<int, Process> _longRunningProcs = new();
    private readonly HashSet<int> _killedPids = new();
    private readonly object _procLock = new();

    public string CliPath { get; set; }
    public bool IsAvailable => File.Exists(CliPath);

    public GameCli(string? cliPath = null)
    {
        var env = Environment.GetEnvironmentVariable(CliPathEnvVar);
        CliPath = !string.IsNullOrWhiteSpace(cliPath) ? cliPath
            : !string.IsNullOrWhiteSpace(env) && File.Exists(env) ? env
            : DefaultCliPath;
    }

    public async Task<CliResult> RunAsync(CliCommand cmd, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return new CliResult(false, "", $"CLI 실행 파일을 찾을 수 없습니다: {CliPath}", TimeSpan.Zero, TimeSpan.Zero);
        }

        var gate = cmd.Lane switch
        {
            CliLane.General => _general,
            CliLane.LongRunning => _longRunning,
            _ => null
        };

        var queued = Stopwatch.StartNew();
        if (gate != null) await gate.WaitAsync(ct);
        var queueWait = queued.Elapsed;

        try
        {
            return await ExecuteAsync(cmd, queueWait, ct);
        }
        finally
        {
            gate?.Release();
        }
    }

    /// <summary>장시간 레인에서 실행 중인 프로세스를 강제 종료한다 (M1 실측 실패 시 대안 경로).</summary>
    public int KillLongRunning()
    {
        lock (_procLock)
        {
            var n = 0;
            foreach (var p in _longRunningProcs.Values)
            {
                try { if (!p.HasExited) { _killedPids.Add(p.Id); p.Kill(entireProcessTree: true); n++; } } catch { }
            }
            return n;
        }
    }

    private async Task<CliResult> ExecuteAsync(CliCommand cmd, TimeSpan queueWait, CancellationToken ct)
    {
        var timeout = cmd.Timeout ?? CliLanes.DefaultTimeout(cmd.Name);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var exec = Stopwatch.StartNew();

        using var proc = new Process();
        proc.StartInfo.FileName = CliPath;
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.RedirectStandardOutput = true;
        proc.StartInfo.RedirectStandardError = true;
        proc.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        proc.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        proc.StartInfo.CreateNoWindow = true;
        proc.StartInfo.ArgumentList.Add(cmd.Name);
        if (cmd.Args != null)
        {
            foreach (var a in cmd.Args) proc.StartInfo.ArgumentList.Add(a);
        }
        if (cmd.Body != null)
        {
            proc.StartInfo.ArgumentList.Add(JsonSerializer.Serialize(cmd.Body, cmd.Body.GetType(), BodyJson));
        }

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return new CliResult(false, "", $"CLI 시작 실패: {ex.Message}", queueWait, exec.Elapsed);
        }

        var tracked = cmd.Lane == CliLane.LongRunning;
        if (tracked) lock (_procLock) _longRunningProcs[proc.Id] = proc;

        try
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(linked.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(linked.Token);
            await proc.WaitForExitAsync(linked.Token);
            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            bool killed;
            lock (_procLock) killed = _killedPids.Remove(proc.Id);
            if (killed)
            {
                return new CliResult(false, stdout, "강제 종료됨 (KillLongRunning)", queueWait, exec.Elapsed, Killed: true);
            }

            if (proc.ExitCode != 0 && string.IsNullOrEmpty(stdout))
            {
                return new CliResult(false, "", string.IsNullOrEmpty(stderr) ? $"종료 코드 {proc.ExitCode}" : stderr, queueWait, exec.Elapsed);
            }
            return new CliResult(true, stdout, string.IsNullOrEmpty(stderr) ? null : stderr, queueWait, exec.Elapsed);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            return new CliResult(false, "", "게임 CLI 응답 시간 초과 (타임아웃)", queueWait, exec.Elapsed, TimedOut: true);
        }
        catch (Exception ex)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            return new CliResult(false, "", ex.Message, queueWait, exec.Elapsed);
        }
        finally
        {
            if (tracked)
            {
                lock (_procLock)
                {
                    _longRunningProcs.Remove(proc.Id);
                    _killedPids.Remove(proc.Id); // OCE 경로로 빠져도 표시가 남아 같은 PID의 다음 프로세스에 붙지 않게
                }
            }
        }
    }

    internal static T? ParseJson<T>(string stdout) => JsonSerializer.Deserialize<T>(stdout, ReadJson);
}

/// <summary>IGameCli 공통 편의 메서드.</summary>
public static class GameCliExtensions
{
    public static async Task<(bool Ok, T? Data, string? Error)> RunJsonAsync<T>(
        this IGameCli cli, string command, object? body = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var r = await cli.RunAsync(new CliCommand(command, Body: body, Timeout: timeout), ct);
        if (!r.Ok) return (false, default, r.Error);
        try
        {
            return (true, GameCli.ParseJson<T>(r.Stdout), null);
        }
        catch (JsonException ex)
        {
            var head = r.Stdout.Length > 500 ? r.Stdout[..500] : r.Stdout;
            return (false, default, $"JSON 파싱 오류: {ex.Message} (응답: {head})");
        }
    }

    /// <summary>게임 전체 채팅 1건 전송 (write_chat + argv). 개행 치환·50자 절단은 ChatText 규칙을 따른다.</summary>
    public static async Task<(bool Ok, string? Error)> SendGameChatAsync(this IGameCli cli, string message, CancellationToken ct = default)
    {
        var text = ChatText.Truncate(ChatText.Sanitize(message), ChatText.MaxLength);
        if (text.Length == 0) return (false, "채팅 내용이 비어 있습니다.");
        var r = await cli.RunAsync(new CliCommand("write_chat", Args: new[] { text }), ct);
        return (r.Ok, r.Error);
    }
}
