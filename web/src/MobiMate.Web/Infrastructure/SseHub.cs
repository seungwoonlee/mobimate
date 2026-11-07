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
public sealed class SseHub(ServerIdentity identity, MobiMateOptions options, IHostApplicationLifetime life, ILogger<SseHub> log)
{
    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    public int ConnectionCount => _connections.Count;

    /// <summary>이 PC(루프백)의 브라우저 탭이 붙어 있는 연결 수. 새 탭을 또 열지 판단할 때 쓴다.</summary>
    public int LocalConnectionCount => _connections.Values.Count(c => IsLoopback(c.DeviceId));

    private static bool IsLoopback(string deviceId) => System.Net.IPAddress.TryParse(deviceId, out var ip) && System.Net.IPAddress.IsLoopback(ip);

    // ── "방금까지 탭이 붙어 있었다" 표시 파일: 재시작·재배포(강제 종료)로 연결이 끊겨도 새 탭을 또 열지 않게 한다 ──
    private string MarkerPath => Path.Combine(options.StorageDir, "tab-open.marker");

    private void TouchMarker()
    {
        try { Directory.CreateDirectory(options.StorageDir); File.WriteAllText(MarkerPath, DateTimeOffset.UtcNow.ToString("O")); }
        catch (Exception ex) { log.LogDebug(ex, "탭 표시 파일 쓰기 실패"); }
    }

    private void ClearMarker()
    {
        try { File.Delete(MarkerPath); } catch (Exception ex) { log.LogDebug(ex, "탭 표시 파일 지우기 실패"); }
    }

    /// <summary>이 앱이 뜨기 직전(within 이내)까지 이 PC의 탭이 붙어 있었는가. 탭을 직접 닫았으면 표시 파일이 지워져 있다.</summary>
    public bool HadLocalTabRecently(TimeSpan within)
    {
        try { return File.Exists(MarkerPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(MarkerPath) <= within; }
        catch { return false; }
    }

    public void Broadcast(string evt, object data)
    {
        var frame = Frame(evt, data);
        foreach (var c in _connections.Values) c.Channel.Writer.TryWrite(frame);
    }

    /// <summary>
    /// 이 PC(루프백)에서 연 연결에만 보낸다. 음성 입력 이벤트(인식 중인 글자·확정 글자)는 PC 마이크로 말한 내용이므로
    /// 같은 와이파이의 다른 기기에 퍼뜨리지 않는다 (FR-VC-10).
    /// </summary>
    public void BroadcastLocal(string evt, object data)
    {
        var frame = Frame(evt, data);
        foreach (var c in _connections.Values)
            if (System.Net.IPAddress.TryParse(c.DeviceId, out var ip) && System.Net.IPAddress.IsLoopback(ip)) c.Channel.Writer.TryWrite(frame);
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
        var local = IsLoopback(deviceId);
        if (local) TouchMarker();
        // 앱을 끌 때 열려 있는 연결이 종료를 붙잡지 않게 한다 (브라우저 탭이 열려 있으면 Kestrel이 연결이 끝나기를 기다린다)
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, life.ApplicationStopping);
        var ct = linked.Token;

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
                    if (local) TouchMarker();
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
            // 마지막 탭이 닫혔고 앱이 끝나는 중이 아니면(= 사용자가 탭을 닫음) 표시를 지운다. 앱이 끝나는 중이면 남겨 둬서 다음 실행이 알게 한다.
            if (local && LocalConnectionCount == 0 && !life.ApplicationStopping.IsCancellationRequested) ClearMarker();
        }
    }

    private static string Frame(string evt, object data) =>
        $"event: {evt}\ndata: {JsonSerializer.Serialize(data, ApiResults.Json)}\n\n";

    private sealed record Connection(string Id, string DeviceId, System.Net.IPAddress? Local, Channel<string> Channel);
}
