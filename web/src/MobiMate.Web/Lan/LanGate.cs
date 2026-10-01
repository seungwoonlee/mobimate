using System.Net;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Lan;

/// <summary>현재 LAN 모드가 열어 둔 주소 (이름 주소 확인·표시용).</summary>
public interface ILanHosts
{
    /// <summary>현재 연 LAN IP들과 광고 중인 mDNS 이름.</summary>
    IReadOnlyCollection<string> Current { get; }

    /// <summary>이 로컬 주소로 지금 LAN 서비스를 하고 있는가.</summary>
    bool Serves(IPAddress local);
}

/// <summary>
/// LAN 모드를 껐는데도 Kestrel이 정리 중인 keep-alive 연결로 요청이 들어오는 것을 막는다 (LAN 끄기가 곧바로 효과를 내게).
/// 인증·출처 검사는 하지 않는다(요구사양 Q8). 루프백(이 PC)으로 들어온 요청은 항상 통과한다.
/// </summary>
public sealed class LanGate(RequestDelegate next, ILanHosts lanHosts)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var local = ctx.Connection.LocalIpAddress;
        if (local != null && !IPAddress.IsLoopback(local) && !lanHosts.Serves(local))
        {
            await ApiResults.Write(ctx, StatusCodes.Status421MisdirectedRequest, "LAN_OFF", "LAN 모드가 꺼져 있습니다.");
            return;
        }
        await next(ctx);
    }
}

/// <summary>요청을 보낸 기기의 표시용 식별자 = 원격 IP 주소 (채팅 중복 방지·채집 요청자 기록용).</summary>
public static class ClientId
{
    public static string Of(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static bool IsLocal(HttpContext ctx) => ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
}
