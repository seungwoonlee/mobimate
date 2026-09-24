using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Security;

namespace MobiMate.Web.Lan;

/// <summary>
/// Kestrel 엔드포인트용 설정 공급자 (상세설계 §3.6). 값을 바꾸면 OnReload로 Kestrel이 바뀐 엔드포인트만 다시 바인딩한다.
/// "Loopback"은 고정이고 "Lan0..n"만 넣고 뺀다. 앱 구성과 섞이지 않게 전용 구성 루트로만 Kestrel에 묶는다.
/// </summary>
public sealed class KestrelEndpointSource : IConfigurationSource
{
    public KestrelEndpointProvider Provider { get; } = new();
    public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
}

public sealed class KestrelEndpointProvider : ConfigurationProvider
{
    public IReadOnlyList<IPAddress> Lan { get; private set; } = Array.Empty<IPAddress>();

    public void SetEndpoints(int port, IEnumerable<IPAddress> lan)
    {
        var list = lan.ToList();
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Kestrel:Endpoints:Loopback:Url"] = $"http://127.0.0.1:{port}",
        };
        // 이름을 주소로 만든다. 순번이면 앞 주소가 빠질 때 남은 주소까지 다시 바인딩되어 연결이 끊긴다.
        foreach (var ip in list) data[$"Kestrel:Endpoints:{EndpointName(ip)}:Url"] = $"http://{ip}:{port}";
        Lan = list;
        if (Data.Count == data.Count && data.All(kv => Data.TryGetValue(kv.Key, out var v) && v == kv.Value)) return;
        Data = data;
        OnReload();
    }

    public static string EndpointName(IPAddress ip) => "Lan_" + ip.ToString().Replace('.', '_');
}

public enum ProbeResult { Free, InUse, Reserved }

/// <summary>포트 확인. Kestrel은 재바인딩 실패를 로그로만 남기므로 설정을 바꾸기 전·후에 직접 확인한다.</summary>
public interface IPortProbe
{
    ProbeResult CanBind(IPAddress address, int port);
    Task<bool> IsListeningAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken ct);
}

public sealed class TcpPortProbe : IPortProbe
{
    public ProbeResult CanBind(IPAddress address, int port)
    {
        try
        {
            var l = new TcpListener(address, port);
            l.Start();
            l.Stop();
            return ProbeResult.Free;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            return ProbeResult.Reserved;   // Hyper-V 등이 예약한 포트 범위
        }
        catch (SocketException)
        {
            return ProbeResult.InUse;
        }
    }

    public async Task<bool> IsListeningAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                using var c = new TcpClient();
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(TimeSpan.FromMilliseconds(500));
                await c.ConnectAsync(address, port, t.Token);
                return true;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                await Task.Delay(150, ct);
            }
        }
        return false;
    }
}

public enum LanError { None, Pending, NotPrivate, ProfileUnknown, NoAddress, PortInUse, PortReserved, PortChanged, BindFailed }

public sealed record LanStatus(bool Enabled, bool Active, IReadOnlyList<string> Addresses, string? MdnsName, int Port, LanError Error, bool? NetworkPrivate);

/// <summary>
/// LAN 모드 (NFR-03·04, SEC-01·10, 상세설계 §3.6).
/// - 설정이 켜져 있고, 개인 네트워크 판정이 두 번 연속 나오고, 고정 포트를 쓸 수 있을 때만 LAN 엔드포인트를 연다.
/// - 내릴 때: 허용 호스트에서 먼저 빼고 → LAN 주소로 들어온 SSE를 닫고 → Kestrel 설정을 바꾼다.
/// - 올릴 때: Kestrel 설정을 바꾸고 → 실제로 연결되는지 확인한 뒤 → 허용 호스트에 넣는다.
/// - 네트워크 변경 이벤트가 오면 즉시 내리고, 디바운스 뒤 다시 판정한다. 그 밖에는 10초마다 판정한다.
/// - 판정할 수 없으면(COM 실패) 열지 않는다 (SEC-10).
/// </summary>
public sealed class LanService(
    KestrelEndpointSource endpoints, INetworkProfileSource profile, IPortProbe probe, IMdnsAdvertiser mdns,
    WebSettingsStore settings, ServerIdentity identity, MobiMateOptions options, SseHub hub, ILogger<LanService> log)
    : BackgroundService, ILanHosts
{
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BindCheckTimeout = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private int _eventFlag;
    private volatile LanStatus _status = new(false, false, Array.Empty<string>(), null, 0, LanError.None, null);
    private volatile IReadOnlyCollection<string> _hosts = Array.Empty<string>();
    private volatile HashSet<IPAddress> _serving = new();
    private List<LanAddress> _bound = new();
    private HashSet<IPAddress> _seenPrivate = new();   // 직전 판정에서 개인으로 본 주소 (두 번 연속 확인용)

    public IReadOnlyCollection<string> Current => _hosts;
    public bool Serves(IPAddress local) => _serving.Contains(local);
    public LanStatus Status => _status;

    /// <summary>설정을 바꾸고(저장) 다시 판정한다. 켤 때는 두 번 연속 판정을 기다린다. 저장 실패면 null.</summary>
    public async Task<LanStatus?> SetEnabledAsync(bool enabled, CancellationToken ct)
    {
        if (!settings.Update(s => s.LanEnabled = enabled)) return null;
        var s = await ReconcileAsync(ct);
        if (enabled && s.Error == LanError.Pending)
        {
            await Task.Delay(options.LanConfirmDelay, ct);
            s = await ReconcileAsync(ct);
        }
        return s;
    }

    /// <summary>현재 설정·네트워크 상태에 맞춰 엔드포인트·허용 호스트·mDNS를 맞춘다.</summary>
    /// <param name="ct">게이트를 기다리는 동안만 취소한다. 일단 시작한 바인딩 변경은 끝까지 한다(중간에 멈추면 Kestrel과 상태가 어긋난다).</param>
    /// <param name="networkChanged">네트워크 변경 이벤트: 모두 내리고 판정 기록을 지운 뒤 판정하지 않고 끝낸다 (디바운스 뒤 두 번 연속 판정).</param>
    public async Task<LanStatus> ReconcileAsync(CancellationToken ct = default, bool networkChanged = false)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var enabled = settings.Current.LanEnabled;
            var port = identity.Port;
            var error = LanError.None;
            bool? isPrivate = null;

            if (networkChanged)
            {
                await DownAsync(_bound.Select(b => b.Address).ToHashSet());
                _seenPrivate.Clear();
                return Publish(enabled, port, enabled ? LanError.Pending : LanError.None, null);
            }

            IReadOnlyList<LanAddress> found = Array.Empty<LanAddress>();
            if (enabled)
            {
                var r = profile.PrivateAddresses();
                if (r == null) error = LanError.ProfileUnknown;
                else
                {
                    found = r;
                    isPrivate = r.Count > 0;
                    if (r.Count == 0) error = AnyLanAddress() ? LanError.NotPrivate : LanError.NoAddress;
                }
            }

            // 1) 더 이상 개인 네트워크가 아닌 주소는 즉시 내린다
            var foundSet = found.Select(f => f.Address).ToHashSet();
            var gone = _bound.Where(b => !foundSet.Contains(b.Address)).Select(b => b.Address).ToHashSet();
            if (gone.Count > 0) await DownAsync(gone);

            // 2) 새 주소는 두 번 연속 개인으로 판정됐을 때만 올린다
            var fresh = found.Where(f => _bound.All(b => !b.Address.Equals(f.Address))).ToList();
            var confirmed = fresh.Where(f => _seenPrivate.Contains(f.Address)).ToList();
            if (fresh.Count > confirmed.Count && error == LanError.None) error = LanError.Pending;
            _seenPrivate = foundSet;

            if (confirmed.Count > 0)
            {
                if (port != identity.PreferredPort) error = LanError.PortChanged;   // 로컬 전용 대체 기동 중 (NFR-04)
                else
                {
                    var ready = new List<LanAddress>();
                    foreach (var c in confirmed)
                    {
                        switch (probe.CanBind(c.Address, port))
                        {
                            case ProbeResult.Free: ready.Add(c); break;
                            case ProbeResult.Reserved: error = LanError.PortReserved; break;
                            default: error = LanError.PortInUse; break;
                        }
                    }
                    if (ready.Count > 0 && !await UpAsync(ready, port)) error = LanError.BindFailed;
                }
            }

            if (_bound.Count > 0 && error is LanError.Pending) error = LanError.None;   // 일부는 이미 열려 있다
            return Publish(enabled, port, error, isPrivate);
        }
        finally
        {
            _gate.Release();
        }
    }

    private LanStatus Publish(bool enabled, int port, LanError error, bool? isPrivate)
    {
        var next = new LanStatus(enabled, _bound.Count > 0, _bound.Select(b => b.Address.ToString()).ToList(), _bound.Count > 0 ? mdns.CurrentName : null, port, error, isPrivate);
        var prev = _status;
        _status = next;
        if (prev.Active != next.Active || prev.Error != next.Error || !prev.Addresses.SequenceEqual(next.Addresses) || prev.MdnsName != next.MdnsName)
        {
            log.LogInformation("LAN 모드: {State} [{Addresses}] {Name} 상태={Error}", next.Active ? "켜짐" : "꺼짐", string.Join(",", next.Addresses), next.MdnsName, next.Error);
            hub.Broadcast("state.changed", new { keys = new[] { "lan" } });
        }
        return next;
    }

    /// <summary>내리기: 허용 호스트에서 먼저 빼고 → 그 주소로 들어온 SSE를 닫고 → Kestrel·mDNS를 바꾼다.</summary>
    private async Task DownAsync(HashSet<IPAddress> remove)
    {
        if (remove.Count == 0) return;
        var keep = _bound.Where(b => !remove.Contains(b.Address)).ToList();
        _bound = keep;
        PublishHosts(keep, null);   // 이름도 일단 뺀다 (재광고 결과로 다시 넣음)
        hub.DisconnectWhere(local => local != null && remove.Contains(local));
        endpoints.Provider.SetEndpoints(identity.Port, keep.Select(b => b.Address));
        await RestartMdnsAsync(keep);
        PublishHosts(keep, mdns.CurrentName);
    }

    /// <summary>올리기: Kestrel 설정 → 실제 연결 확인 → 허용 호스트. 확인이 안 되면 설정을 되돌린다.</summary>
    private async Task<bool> UpAsync(List<LanAddress> add, int port)
    {
        var ok = new List<LanAddress>();
        try
        {
            endpoints.Provider.SetEndpoints(port, _bound.Concat(add).Select(b => b.Address));
            foreach (var a in add)
                if (await probe.IsListeningAsync(a.Address, port, BindCheckTimeout, CancellationToken.None)) ok.Add(a);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "LAN 엔드포인트 확인 실패");
            ok.Clear();
        }

        _bound = _bound.Concat(ok).ToList();
        endpoints.Provider.SetEndpoints(port, _bound.Select(b => b.Address));   // 확인된 주소만 남긴다 (실패분 되돌림)
        if (ok.Count > 0) await RestartMdnsAsync(_bound);
        PublishHosts(_bound, mdns.CurrentName);
        return ok.Count == add.Count;
    }

    /// <summary>mDNS는 부가 기능이다. 실패해도 LAN 상태를 흔들지 않고 IP 주소로만 서비스한다.</summary>
    private async Task RestartMdnsAsync(List<LanAddress> bound)
    {
        try
        {
            if (bound.Count == 0 || !options.Mdns) await mdns.StopAsync();
            else await mdns.StartAsync(bound, CancellationToken.None);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "mDNS 광고 실패. 폰에서는 IP 주소로 접속합니다.");
            try { await mdns.StopAsync(); } catch { }
        }
    }

    private void PublishHosts(List<LanAddress> bound, string? name)
    {
        var hosts = bound.Select(b => b.Address.ToString()).ToList();
        if (name != null && bound.Count > 0) hosts.Add(name);
        _serving = bound.Select(b => b.Address).ToHashSet();
        _hosts = hosts;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailability;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                LanStatus s;
                try
                {
                    if (Interlocked.Exchange(ref _eventFlag, 0) == 1)
                    {
                        await ReconcileAsync(stoppingToken, networkChanged: true);   // 즉시 내린다 (판정하지 않음)
                        // 이벤트가 그칠 때까지 기다린다. 그동안 온 이벤트는 이미 내린 상태라 다시 내릴 것이 없다.
                        do
                        {
                            DrainWake();
                            await Task.Delay(options.LanConfirmDelay, stoppingToken);
                        } while (Interlocked.Exchange(ref _eventFlag, 0) == 1);
                    }
                    s = await ReconcileAsync(stoppingToken);   // 1회차 판정 → Pending이면 아래에서 짧게 기다려 2회차
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "LAN 상태 판정 실패");
                    s = _status;
                }
                var wait = s.Error == LanError.Pending ? options.LanConfirmDelay : RecheckInterval;
                try { await _wake.WaitAsync(wait, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailability;
            // 트레이·API의 판정과 겹치지 않게 게이트를 잡고 멈춘다
            var held = await _gate.WaitAsync(TimeSpan.FromSeconds(5));
            try { await mdns.StopAsync(); }
            finally { if (held) _gate.Release(); }
        }
    }

    private void DrainWake()
    {
        while (_wake.Wait(0)) { }
    }

    /// <summary>테스트·설정 변경용: 네트워크가 바뀐 것처럼 처리한다.</summary>
    public void NotifyNetworkChanged()
    {
        Interlocked.Exchange(ref _eventFlag, 1);
        _wake.Release();
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => NotifyNetworkChanged();
    private void OnNetworkAvailability(object? sender, NetworkAvailabilityEventArgs e) => NotifyNetworkChanged();

    private static bool AnyLanAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(u => NlmNetworkProfileSource.IsLanCandidate(u.Address));
}
