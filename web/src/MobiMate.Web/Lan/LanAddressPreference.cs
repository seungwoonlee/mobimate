using System.Net;

namespace MobiMate.Web.Lan;

/// <summary>
/// 폰에 알려 줄 주소의 우선순위 (2026-10-23 승운: QR이 이상한 주소를 가리켰다). 개인 네트워크로 판정된 어댑터가 여럿이면 순서가 어댑터 순서에 따라
/// 달라, Tailscale 같은 VPN의 100.64.0.0/10 주소(집 와이파이의 폰은 닿지 못한다)가 첫 번째로 QR에 들어갔다.
/// 집·사무실 사설 주소(192.168/16 · 10/8 · 172.16/12)를 먼저, 그 밖의 주소, 마지막에 100.64/10(VPN·통신사 NAT) 순으로 둔다.
/// </summary>
public static class LanAddressPreference
{
    public static int Rank(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a) || a.GetAddressBytes() is not { Length: 4 } b) return 3;
        if (b[0] == 192 && b[1] == 168) return 0;
        if (b[0] == 10) return 0;
        if (b[0] == 172 && b[1] is >= 16 and <= 31) return 0;
        if (b[0] == 100 && b[1] is >= 64 and <= 127) return 2;   // CGNAT 대역: Tailscale 등
        return 1;
    }

    /// <summary>우선순위가 같으면 원래 순서를 지킨다.</summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> addresses) =>
        addresses.Select((ip, i) => (ip, i)).OrderBy(x => Rank(x.ip)).ThenBy(x => x.i).Select(x => x.ip).ToList();
}
