using System.Text.Json;
using MobiMate.Web.Hosting;

namespace MobiMate.Web.Infrastructure;

/// <summary>
/// 조회 캐시 + 단일 비행 (NFR-07, 상세설계 §3.4).
/// - 같은 키의 동시 요청은 CLI 호출 하나를 공유한다.
/// - 성공 결과는 TTL(기본 3초) 동안 재사용한다. 실패 결과는 진행 중에만 공유하고 캐시하지 않는다.
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
public sealed class GameQueries(IGameCli cli, QueryCache cache, MobiMateOptions options)
{
    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };

    public IGameCli Cli => cli;

    public async Task<CliData<T>> Get<T>(string command, CancellationToken ct = default)
    {
        if (!cli.IsAvailable) return new CliData<T>(default, new CliFailure(CliFailureKind.Missing, $"게임 CLI를 찾을 수 없습니다: {cli.CliPath}"), DateTimeOffset.UtcNow);

        var r = await cache.GetAsync(command, () => cli.RunAsync(new CliCommand(command)), options.QueryCacheTtl, ct);
        var at = DateTimeOffset.UtcNow;
        if (!r.Ok)
        {
            var kind = r.TimedOut ? CliFailureKind.Timeout : CliFailureKind.Failed;
            return new CliData<T>(default, new CliFailure(kind, r.Error ?? "게임 CLI 호출 실패"), at);
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

    public void Invalidate(params string[] commands) => cache.Invalidate(commands);
}
