using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MobiMate.Web.Hosting;

namespace MobiMate.Web.Infrastructure;

/// <summary>
/// SSE 허브 (상세설계 §3.5). 연결마다 채널을 두고, LAN을 내리면 그 주소로 들어온 연결을 즉시 닫는다.
/// 이벤트: hello, status, header, toast, gather, chat.logged, state.changed, homework.changed, ping.
/// AI 대화는 여기로 보내지 않는다 (FR-AI-04).
/// </summary>
public sealed class SseHub(ServerIdentity identity, MobiMateOptions options, ILogger<SseHub> log)
{
    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    public int ConnectionCount => _connections.Count;

    public void Broadcast(string evt, object data)
    {
        var frame = Frame(evt, data);
        foreach (var c in _connections.Values) c.Channel.Writer.TryWrite(frame);
    }

    public void SendTo(string deviceId, string evt, object data)
    {
        var frame = Frame(evt, data);
        foreach (var c in _connections.Values.Where(c => c.DeviceId == deviceId)) c.Channel.Writer.TryWrite(frame);
    }

    /// <summary>받은 로컬 주소가 조건에 맞는 연결을 닫는다 (LAN을 내릴 때).</summary>
    public int DisconnectWhere(Func<System.Net.IPAddress?, bool> localMatches)
    {
        var n = 0;
        foreach (var c in _connections.Values.Where(c => localMatches(c.Local)))
        {
            c.Channel.Writer.TryComplete();
            n++;
        }
        return n;
    }

    public async Task Serve(HttpContext ctx, string deviceId)
    {
        var conn = new Connection(Guid.NewGuid().ToString("N")[..12], deviceId, ctx.Connection.LocalIpAddress,
            Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest }));
        _connections[conn.Id] = conn;
        var ct = ctx.RequestAborted;

        ctx.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await ctx.Response.WriteAsync(Frame("hello", new { serverId = identity.ServerId, connectionId = conn.Id }), ct);
            await ctx.Response.Body.FlushAsync(ct);

            using var ping = new PeriodicTimer(options.SsePingInterval);
            var pingTask = ping.WaitForNextTickAsync(ct).AsTask();
            Task<bool>? read = null;
            while (!ct.IsCancellationRequested)
            {
                read ??= conn.Channel.Reader.WaitToReadAsync(ct).AsTask();   // 끝나지 않은 대기는 다음 반복에서 재사용
                var done = await Task.WhenAny(read, pingTask);
                if (done == pingTask)
                {
                    if (!await pingTask) break;
                    await ctx.Response.WriteAsync(": ping\n\nevent: ping\ndata: {}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                    pingTask = ping.WaitForNextTickAsync(ct).AsTask();
                    continue;
                }
                var more = await read;
                read = null;
                if (!more) break;   // 채널 완료 = 서버가 연결을 닫음 (LAN 내리기 등)
                while (conn.Channel.Reader.TryRead(out var frame)) await ctx.Response.WriteAsync(frame, ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex)
        {
            log.LogDebug(ex, "SSE 연결 쓰기 실패");
        }
        finally
        {
            _connections.TryRemove(conn.Id, out _);
        }
    }

    private static string Frame(string evt, object data) =>
        $"event: {evt}\ndata: {JsonSerializer.Serialize(data, ApiResults.Json)}\n\n";

    private sealed record Connection(string Id, string DeviceId, System.Net.IPAddress? Local, Channel<string> Channel);
}
