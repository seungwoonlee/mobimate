using System.Text;
using MobiMate.Web.Hosting;

namespace MobiMate.Web.Services;

/// <summary>마지막으로 CLI를 점검한 결과(화면·진단용).</summary>
public sealed record CliCheckResult(DateTimeOffset CheckedAt, int Commands, bool FirstBaseline, DateTimeOffset? ChangedAt, string? Summary);

/// <summary>
/// 게임 CLI 업데이트 감지(2026-10-22 승운 요청: 간간히 확인): 게임에 연결되면 `capabilities`를 읽어 저장해 둔 기준(cli_capabilities.json)과 비교하고,
/// 이후 6시간마다 다시 한다. 새 명령·사라진 명령·출력 필드 변화가 있으면 로그와 cli_changes.txt에 남기고 기준을 새로 저장한다.
/// 사라진 명령이 있으면 앱 기능이 깨질 수 있어 경고로, 나머지는 정보로 남긴다. 호출은 게임 연결 중에 6시간에 한 번뿐이다.
/// </summary>
public sealed class CliChangeWatcher(IGameCli cli, StatusMonitor status, MobiMateOptions options, ILogger<CliChangeWatcher> log) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(6);
    private static readonly TimeSpan WaitForGame = TimeSpan.FromSeconds(20);

    private readonly object _lock = new();
    private CliCheckResult? _last;

    /// <summary>마지막 점검 결과. 아직 점검 전이면 null.</summary>
    public CliCheckResult? Last { get { lock (_lock) return _last; } }

    private string BaselinePath => Path.Combine(options.StorageDir, "cli_capabilities.json");
    private string ChangesPath => Path.Combine(options.StorageDir, "cli_changes.txt");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (options.WatchInterval <= TimeSpan.Zero) return;   // 감시를 끈 설정(시험)에서는 돌지 않는다
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (status.State == GameConnection.Connected)
                {
                    try { await CheckAsync(DateTimeOffset.UtcNow, ct); await Task.Delay(Every, ct); continue; }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                    catch (Exception ex) { log.LogDebug(ex, "CLI 업데이트 점검 실패"); }
                }
                await Task.Delay(WaitForGame, ct);   // 연결을 기다리거나 실패 뒤 잠시 쉰다
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>한 번 점검한다. 기준이 없으면 지금 값을 기준으로 저장하고, 있으면 비교한다.</summary>
    internal async Task<CliCheckResult?> CheckAsync(DateTimeOffset now, CancellationToken ct)
    {
        var run = await cli.RunAsync(new CliCommand("capabilities", Timeout: TimeSpan.FromSeconds(15)), ct);
        if (!run.Ok || CliCapabilityDiff.Parse(run.Stdout) is not { } current) return null;   // 못 읽었으면 기준을 건드리지 않는다

        CliCheckResult result;
        Directory.CreateDirectory(options.StorageDir);
        var baseline = File.Exists(BaselinePath) ? CliCapabilityDiff.Parse(File.ReadAllText(BaselinePath, Encoding.UTF8)) : null;
        if (baseline == null)
        {
            File.WriteAllText(BaselinePath, run.Stdout, new UTF8Encoding(false));
            log.LogInformation("게임 CLI 명령 {N}개를 기준으로 저장했습니다. 다음 점검부터 바뀐 것을 알려 드립니다.", current.Count);
            result = new CliCheckResult(now, current.Count, true, null, null);
        }
        else
        {
            var change = CliCapabilityDiff.Compare(baseline, current);
            if (!change.Any)
            {
                result = new CliCheckResult(now, current.Count, false, _last?.ChangedAt, _last?.Summary);
            }
            else
            {
                var summary = change.Summary();
                if (change.Removed.Count > 0) log.LogWarning("게임 CLI가 바뀌었습니다 (사라진 명령 있음 — 앱 기능 확인 필요):\n{Summary}", summary);
                else log.LogInformation("게임 CLI가 업데이트된 것 같습니다:\n{Summary}", summary);
                File.AppendAllText(ChangesPath, $"[{now.ToLocalTime():yyyy-MM-dd HH:mm}]\n{summary}\n\n", new UTF8Encoding(false));
                File.WriteAllText(BaselinePath, run.Stdout, new UTF8Encoding(false));   // 다음부터는 이 상태가 기준이다
                result = new CliCheckResult(now, current.Count, false, now, summary);
            }
        }
        lock (_lock) _last = result;
        return result;
    }
}
