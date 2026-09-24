using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MobiMate.Web.Lan;

/// <summary>LAN에 노출해도 되는 주소 하나 (개인 네트워크 어댑터의 IPv4).</summary>
public sealed record LanAddress(IPAddress Address, int InterfaceIndex, string AdapterName);

/// <summary>현재 개인 네트워크 주소 목록. 판정할 수 없으면 null (= LAN을 켜지 않는다, SEC-10).</summary>
public interface INetworkProfileSource
{
    IReadOnlyList<LanAddress>? PrivateAddresses();
}

/// <summary>
/// Windows 네트워크 목록 관리자(NLM)로 어댑터별 네트워크 범주(공용·개인·도메인)를 구한다 (NFR-03, 상세설계 §3.6).
/// COMReference 대신 [ComImport]로 직접 선언한다 (dotnet publish 호환, NFR-02).
/// 범주가 '개인'인 연결의 어댑터만 대상으로 하고, 그 어댑터의 Up 상태 IPv4 유니캐스트 주소를 돌려준다.
/// </summary>
public sealed class NlmNetworkProfileSource(ILogger<NlmNetworkProfileSource> log) : INetworkProfileSource
{
    private static readonly Guid ClsidNetworkListManager = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
    private const int CategoryPrivate = 1;

    public IReadOnlyList<LanAddress>? PrivateAddresses()
    {
        HashSet<Guid> privateAdapters;
        try
        {
            privateAdapters = PrivateAdapterIds();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TypeLoadException or PlatformNotSupportedException)
        {
            log.LogWarning(ex, "네트워크 범주를 확인하지 못했습니다. LAN 모드를 켜지 않습니다.");
            return null;
        }

        var result = new List<LanAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (!Guid.TryParse(nic.Id, out var id) || !privateAdapters.Contains(id)) continue;
            var props = nic.GetIPProperties();
            var index = props.GetIPv4Properties()?.Index ?? -1;
            foreach (var ua in props.UnicastAddresses)
            {
                if (IsLanCandidate(ua.Address)) result.Add(new LanAddress(ua.Address, index, nic.Name));
            }
        }
        return result;
    }

    /// <summary>IPv4이고 루프백·링크 로컬(169.254/16)이 아닌 주소.</summary>
    public static bool IsLanCandidate(IPAddress a)
    {
        if (a.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a)) return false;
        var b = a.GetAddressBytes();
        return !(b[0] == 169 && b[1] == 254) && !(b[0] == 0);
    }

    private static HashSet<Guid> PrivateAdapterIds()
    {
        var type = Type.GetTypeFromCLSID(ClsidNetworkListManager, throwOnError: true)!;
        var nlm = (INetworkListManager)Activator.CreateInstance(type)!;
        var set = new HashSet<Guid>();
        try
        {
            var conns = nlm.GetNetworkConnections();
            var buf = new INetworkConnection[1];
            while (true)
            {
                uint fetched = 0;
                var hr = conns.Next(1, buf, ref fetched);
                if (hr != 0 || fetched == 0) break;
                var conn = buf[0];
                var net = conn.GetNetwork();
                if (net.GetCategory() == CategoryPrivate) set.Add(conn.GetAdapterId());
                Marshal.ReleaseComObject(net);
                Marshal.ReleaseComObject(conn);
            }
            Marshal.ReleaseComObject(conns);
        }
        finally
        {
            Marshal.ReleaseComObject(nlm);
        }
        return set;
    }

    // ── NLM COM 선언 (netlistmgr.h, IDispatch 이후 vtable 순서 그대로) ──

    [ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkListManager
    {
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetNetworks(int flags);
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetNetwork(Guid networkId);
        IEnumNetworkConnections GetNetworkConnections();
        INetworkConnection GetNetworkConnection(Guid connectionId);
        short IsConnectedToInternet();
        short IsConnected();
        int GetConnectivity();
    }

    [ComImport, Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IEnumNetworkConnections
    {
        [return: MarshalAs(UnmanagedType.IUnknown)] object NewEnum();
        [PreserveSig]
        int Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] INetworkConnection[] rgelt, ref uint fetched);
        void Skip(uint celt);
        void Reset();
        IEnumNetworkConnections Clone();
    }

    [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkConnection
    {
        INetwork GetNetwork();
        short IsConnectedToInternet();
        short IsConnected();
        int GetConnectivity();
        Guid GetConnectionId();
        Guid GetAdapterId();
        int GetDomainType();
    }

    [ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetwork
    {
        [return: MarshalAs(UnmanagedType.BStr)] string GetName();
        void SetName([MarshalAs(UnmanagedType.BStr)] string name);
        [return: MarshalAs(UnmanagedType.BStr)] string GetDescription();
        void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);
        Guid GetNetworkId();
        int GetDomainType();
        IEnumNetworkConnections GetNetworkConnections();
        void GetTimeCreatedAndConnected(out uint createdLow, out uint createdHigh, out uint connectedLow, out uint connectedHigh);
        short IsConnectedToInternet();
        short IsConnected();
        int GetConnectivity();
        int GetCategory();
        void SetCategory(int category);
    }
}
