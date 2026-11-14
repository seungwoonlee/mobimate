using System.Text.Json;
using MobiMateHomeWork.Hosting;

namespace MobiMateHomeWork.Services;

/// <summary>
/// 조회 캐시 + 단일 비행 (NFR-07, 상세설계 §3.4).
/// - 같은 키의 동시 요청은 CLI 호출 하나를 공유한다.
/// - 성공 결과는 TTL(기본 3초, 캐릭터·맵·활동은 읽는 쪽이 5초까지) 동안 재사용한다. 유지 시간은 읽는 쪽이 정한다. 실패 결과는 진행 중에만 공유하고 캐시하지 않는다.
/// - 공유 작업에는 호출자 토큰을 넘기지 않는다(명령 타임아웃만 적용). 호출자는 WaitAsync(ct)로 기다린다.
///   그래서 한 기기가 요청을 취소해도 같은 결과를 기다리는 다른 기기의 요청은 끊기지 않는다.
/// - 진행 중인 항목을 무효화하면 표에서 뺀다. 그 결과는 이미 기다리는 호출자에게만 가고 캐시되지 않으며,
///   다음 요청은 새로 조회한다 (조작 직전에 시작된 조회가 조작 뒤의 값으로 캐시되지 않게).
/// </summary>
public sealed class QueryCache
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly Func<DateTimeOffset> _now;

    public QueryCache(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    public Task<CliResult> GetAsync(string key, Func<Task<CliResult>> factory, TimeSpan ttl, CancellationToken ct)
    {
        Task<CliResult> task;
        lock (_lock)
        {
            var now = _now();
            if (_entries.TryGetValue(key, out var e) &&
                (!e.Task.IsCompleted || (e.Task.IsCompletedSuccessfully && e.Task.Result.Ok && now - e.CompletedAt <= ttl)))
            {
                task = e.Task;
            }
            else
            {
                var entry = new Entry();
                entry.Task = Run(factory, entry);
                _entries[key] = entry;
                task = entry.Task;
            }
        }
        return task.WaitAsync(ct);
    }

    public void Invalidate(params string[] keys)
    {
        lock (_lock)
        {
            foreach (var k in keys)
                _entries.Remove(k);
        }
    }

    private async Task<CliResult> Run(Func<Task<CliResult>> factory, Entry entry)
    {
        await Task.Yield();   // CLI 프로세스 시작을 lock 밖에서 하도록 (다른 키 조회를 막지 않게)
        try
        {
            return await factory();
        }
        finally
        {
            entry.CompletedAt = _now();
        }
    }

    private sealed class Entry
    {
        public Task<CliResult> Task = null!;
        public DateTimeOffset CompletedAt;
    }
}

/// <summary>게임 CLI 조회를 캐시를 거쳐 형식화된 값으로 돌려준다.</summary>
public sealed class GameQueries(IGameCli cli, QueryCache cache, AppOptions options, ILogger<GameQueries>? log = null)
{
    /// <summary>게임과 연결이 끊겼다고 응답받으면 이 시간 동안은 CLI를 부르지 않고 바로 실패시킨다(끊긴 CLI는 호출마다 5초 기다려 쌓이기 때문).</summary>
    public static readonly TimeSpan DownFor = TimeSpan.FromSeconds(3);
    private long _downUntilTicks;
    private readonly Dictionary<string, DateTimeOffset> _lastLogged = new();
    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };

    public IGameCli Cli => cli;

    /// <param name="maxAge">이 값보다 오래된 캐시는 쓰지 않는다. 비우면 명령별 기본 유지 시간(<see cref="AppOptions.CacheTtlFor"/>).
    /// 주기적으로 읽는 쪽(전환 감시)은 기본 유지 시간을 직접 주어, 재사용용으로 늘린 유지 시간에 가려 낡은 값을 읽지 않게 한다.</param>
    public async Task<CliData<T>> Get<T>(string command, CancellationToken ct = default, TimeSpan? maxAge = null)
    {
        if (!cli.IsAvailable) return new CliData<T>(default, new CliFailure(CliFailureKind.Missing, $"게임 CLI를 찾을 수 없습니다: {cli.CliPath}"), DateTimeOffset.UtcNow);

        if (DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref _downUntilTicks))
            return new CliData<T>(default, new CliFailure(CliFailureKind.Failed, "게임과 연결되어 있지 않습니다."), DateTimeOffset.UtcNow);

        var r = await cache.GetAsync(command, () => cli.RunAsync(new CliCommand(command)), maxAge ?? options.CacheTtlFor(command), ct);
        var at = DateTimeOffset.UtcNow;
        if (!r.Ok)
        {
            var kind = r.TimedOut ? CliFailureKind.Timeout : CliFailureKind.Failed;
            Note(command, kind, r.Error ?? "게임 CLI 호출 실패", r);
            return new CliData<T>(default, new CliFailure(kind, r.Error ?? "게임 CLI 호출 실패"), at);
        }
        // 오류 응답({"error":…}·{"pipe":"disconnected",…})을 값으로 읽지 않는다: 배열 명령은 PARSE_ERROR, 객체 명령은 "빈 값의 성공"이 되어 조용히 틀린다
        if (CliEnvelope.TryReadError(r.Stdout, out var cliError))
        {
            if (CliEnvelope.PipeConnected(r.Stdout) == false) Interlocked.Exchange(ref _downUntilTicks, (at + DownFor).UtcTicks);   // 끊김: 잠시 CLI를 부르지 않는다
            Note(command, CliFailureKind.Failed, cliError, r);
            return new CliData<T>(default, new CliFailure(CliFailureKind.Failed, cliError), at);
        }
        try
        {
            return new CliData<T>(JsonSerializer.Deserialize<T>(r.Stdout, Read), null, at);
        }
        catch (JsonException ex)
        {
            return new CliData<T>(default, new CliFailure(CliFailureKind.Parse, $"응답을 해석하지 못했습니다: {ex.Message}"), at);
        }
    }

    /// <summary>CLI 실패를 로그에 남긴다(같은 명령·같은 사유는 60초에 한 번만). 화면에는 보이지 않는 일시적인 실패가 대부분이라 정보 수준.</summary>
    private void Note(string command, CliFailureKind kind, string message, CliResult r)
    {
        if (log == null) return;
        var key = $"{command}|{kind}|{message}";
        var now = DateTimeOffset.UtcNow;
        lock (_lastLogged)
        {
            if (_lastLogged.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(60)) return;
            _lastLogged[key] = now;
            if (_lastLogged.Count > 200) _lastLogged.Clear();
        }
        log.LogInformation("CLI 실패: {Command} / {Kind} / {Message} (대기 {Queue}ms, 실행 {Exec}ms)", command, kind, message, (int)r.QueueWait.TotalMilliseconds, (int)r.Exec.TotalMilliseconds);
    }

    public void Invalidate(params string[] commands) => cache.Invalidate(commands);
}
