using System.Net;
using System.Net.Sockets;

namespace MobiMate.Web.Lan;

/// <summary>mDNS로 이름을 알리는 역할. 실제로 광고 중인 이름을 돌려준다 (없으면 null).</summary>
public interface IMdnsAdvertiser
{
    string? CurrentName { get; }
    Task<string?> StartAsync(IReadOnlyList<LanAddress> addresses, CancellationToken ct);
    Task StopAsync();
}

/// <summary>
/// 최소 mDNS 응답기 (상세설계 §3.6). 개인 네트워크 어댑터에서 들어온 우리 이름의 A 질의에만 답한다.
/// - 소켓 하나를 0.0.0.0:5353(주소 재사용)에 묶고, 대상 어댑터마다 224.0.0.251에 가입한다.
/// - 받은 패킷의 도착 어댑터(IP_PKTINFO)를 보고, 대상 어댑터가 아니면 답하지 않는다. 답에는 그 어댑터의 주소만 싣는다.
/// - 광고 전에 1초 동안 이름을 질의해 다른 호스트가 쓰고 있으면 mobimate-2.local, -3.local 순으로 바꾼다.
/// - 멈출 때 TTL 0 응답(goodbye)을 보낸다.
/// </summary>
public sealed class MdnsResponder(ILogger<MdnsResponder> log) : IMdnsAdvertiser, IDisposable
{
    public const string BaseName = "mobimate";
    private const uint Ttl = 120;
    private static readonly TimeSpan ProbeWindow = TimeSpan.FromSeconds(1);

    private readonly object _lock = new();
    private readonly SemaphoreSlim _send = new(1, 1);   // 멀티캐스트 인터페이스 지정과 전송을 한 묶음으로
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Dictionary<int, LanAddress> _byIndex = new();
    private volatile string? _name;

    public string? CurrentName => _name;

    public async Task<string?> StartAsync(IReadOnlyList<LanAddress> addresses, CancellationToken ct)
    {
        await StopAsync();
        var targets = addresses.Where(a => a.InterfaceIndex >= 0).GroupBy(a => a.InterfaceIndex).ToDictionary(g => g.Key, g => g.First());
        if (targets.Count == 0) return null;

        Socket socket;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            socket.Bind(new IPEndPoint(IPAddress.Any, MdnsCodec.Port));
            foreach (var t in targets.Values)
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MdnsCodec.Group, t.Address));
        }
        catch (SocketException ex)
        {
            log.LogWarning(ex, "mDNS 소켓을 열지 못했습니다. 폰에서는 IP 주소로 접속합니다.");
            return null;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_lock)
        {
            _socket = socket;
            _cts = cts;
            _byIndex = targets;
            _name = null;
        }

        string? chosen = null;
        for (var n = 1; n <= 9 && chosen == null; n++)
        {
            var candidate = n == 1 ? $"{BaseName}.local" : $"{BaseName}-{n}.local";
            if (!await InUseAsync(socket, targets.Values, candidate, cts.Token)) chosen = candidate;
        }
        if (chosen == null)
        {
            log.LogWarning("mDNS 이름이 모두 사용 중입니다. 폰에서는 IP 주소로 접속합니다.");
            await StopAsync();
            return null;
        }

        _name = chosen;
        _loop = Task.Run(() => ReceiveLoop(socket, cts.Token));
        await AnnounceAsync(chosen, Ttl);
        log.LogInformation("mDNS 이름 광고: {Name} ({Addresses})", chosen, string.Join(", ", targets.Values.Select(t => t.Address)));
        return chosen;
    }

    public async Task StopAsync()
    {
        Socket? socket;
        CancellationTokenSource? cts;
        Task? loop;
        string? name;
        lock (_lock)
        {
            (socket, cts, loop, name) = (_socket, _cts, _loop, _name);
            _socket = null;
            _cts = null;
            _loop = null;
            _name = null;
        }
        if (socket == null) return;
        if (name != null)
        {
            try { await SendAllAsync(socket, name, 0); } catch (SocketException) { }   // goodbye
        }
        cts?.Cancel();
        socket.Dispose();
        if (loop != null) try { await loop; } catch { }
        cts?.Dispose();
    }

    private async Task AnnounceAsync(string name, uint ttl)
    {
        var socket = _socket;
        if (socket == null) return;
        await SendAllAsync(socket, name, ttl);
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            if (_socket == socket && _name == name) try { await SendAllAsync(socket, name, ttl); } catch (SocketException) { } catch (ObjectDisposedException) { }
        });
    }

    private async Task SendAllAsync(Socket socket, string name, uint ttl)
    {
        foreach (var t in _byIndex.Values)
            await SendViaAsync(socket, t.Address, MdnsCodec.BuildResponse(name, t.Address, ttl), new IPEndPoint(MdnsCodec.Group, MdnsCodec.Port), CancellationToken.None);
    }

    private async Task SendViaAsync(Socket socket, IPAddress iface, byte[] data, IPEndPoint dest, CancellationToken ct)
    {
        await _send.WaitAsync(ct);
        try
        {
            if (dest.Address.Equals(MdnsCodec.Group))
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, iface.GetAddressBytes());
            await socket.SendToAsync(data, SocketFlags.None, dest, ct);
        }
        finally
        {
            _send.Release();
        }
    }

    /// <summary>이름을 질의하고 1초 안에 우리 주소가 아닌 A 응답이 오면 사용 중으로 본다.</summary>
    private async Task<bool> InUseAsync(Socket socket, IEnumerable<LanAddress> targets, string name, CancellationToken ct)
    {
        var ours = targets.Select(t => t.Address).ToHashSet();
        foreach (var t in targets)
            await SendViaAsync(socket, t.Address, MdnsCodec.BuildQuery(name), new IPEndPoint(MdnsCodec.Group, MdnsCodec.Port), ct);

        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(ProbeWindow);
        var buf = new byte[9000];
        try
        {
            while (true)
            {
                var r = await socket.ReceiveFromAsync(buf, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), window.Token);
                if (MdnsCodec.TryParse(buf.AsSpan(0, r.ReceivedBytes), out var m) &&
                    MdnsCodec.AddressesFor(m!, name).Any(a => !ours.Contains(a)))
                    return true;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task ReceiveLoop(Socket socket, CancellationToken ct)
    {
        var buf = new byte[9000];
        while (!ct.IsCancellationRequested)
        {
            SocketReceiveMessageFromResult r;
            try
            {
                r = await socket.ReceiveMessageFromAsync(buf, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset) { continue; }
            catch (SocketException ex)
            {
                log.LogDebug(ex, "mDNS 수신 오류");
                continue;
            }

            var name = _name;
            if (name == null || !_byIndex.TryGetValue(r.PacketInformation.Interface, out var target)) continue;   // 개인 어댑터가 아닌 곳에서 온 질의
            if (!MdnsCodec.TryParse(buf.AsSpan(0, r.ReceivedBytes), out var m) || MdnsCodec.AsksFor(m!, name) is not { } q) continue;

            var from = (IPEndPoint)r.RemoteEndPoint;
            try
            {
                if (from.Port != MdnsCodec.Port)
                {
                    // 레거시 유니캐스트 질의: 질문자에게 직접, 같은 id로
                    await SendViaAsync(socket, target.Address, MdnsCodec.BuildResponse(name, target.Address, Ttl, m!.Id, q), from, ct);
                }
                else
                {
                    var dest = q.UnicastResponse ? from : new IPEndPoint(MdnsCodec.Group, MdnsCodec.Port);
                    await SendViaAsync(socket, target.Address, MdnsCodec.BuildResponse(name, target.Address, Ttl), dest, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex)
            {
                log.LogDebug(ex, "mDNS 응답 전송 실패");
            }
        }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
