using System.Diagnostics;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Lan;
using MobiMate.Web.Services;

namespace MobiMate.Web.Endpoints;

public sealed record LanRequest(bool? Enabled);

/// <summary>서버 식별·접속 종류·메타, LAN 모드와 접속 주소(QR) (인증은 없다: 요구사양 Q8).</summary>
public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        // 서버 식별 (FR-MB-13 mDNS 판정용). IP 주소로 연 페이지가 이름 주소로 확인하므로 교차 출처 요청이다.
        app.MapGet("/api/ping", (HttpContext ctx, ServerIdentity id) =>
        {
            ctx.Response.Headers.AccessControlAllowOrigin = "*";
            return Results.Json(new { serverId = id.ServerId }, ApiResults.Json);
        });

        // 이 화면을 연 곳이 게임 PC인가(local) 폰·태블릿인가(lan): 폰에서는 PC 전용 버튼(폰으로 보기·CLI 경로)을 숨기는 화면 편의다
        app.MapGet("/api/session", (HttpContext ctx) => ApiResults.Ok(new { kind = ClientId.IsLocal(ctx) ? "local" : "lan" }));

        app.MapGet("/api/meta", (ServerIdentity id, LanService lan) => ApiResults.Ok(new
        {
            version = id.Version,
            serverId = id.ServerId,
            chatCountMode = ChatText.Mode,
            chatMaxLength = ChatText.MaxLength,
            lan = LanView(lan.Status),
            wpfRunning = WpfRunning(),
        }));

        // LAN 모드 켜기/끄기 (NFR-03)
        app.MapPut("/api/lan", async (LanRequest req, LanService lan, CancellationToken ct) =>
        {
            if (req.Enabled == null) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "enabled가 필요합니다.");
            var s = await lan.SetEnabledAsync(req.Enabled.Value, ct);
            return s == null ? ApiResults.Error(503, "STORAGE_UNAVAILABLE", "설정을 저장하지 못했습니다.") : ApiResults.Ok(LanView(s));
        });

        // 폰·태블릿 접속 주소 (QR용). QR은 항상 IP 주소로 연다. 이름 주소(n)는 폰 페이지가 /api/ping으로 확인한 뒤 옮겨 간다 (FR-MB-10·13).
        app.MapGet("/api/lan/share", (LanService lan) =>
        {
            var s = lan.Status;
            if (!s.Active) return ApiResults.Error(StatusCodes.Status409Conflict, "LAN_OFF", "LAN 모드가 꺼져 있거나 쓸 수 없습니다. 설정에서 LAN 모드를 켜 주세요.");
            return ApiResults.Ok(new
            {
                urlIp = $"http://{s.Addresses[0]}:{s.Port}/" + (s.MdnsName is { } nm ? $"?n={Uri.EscapeDataString(nm)}" : ""),
                urlName = s.MdnsName is { } n ? $"http://{n}:{s.Port}/" : null,
                addresses = s.Addresses,
            });
        });
    }

    internal static object LanView(LanStatus s) => new
    {
        enabled = s.Enabled, active = s.Active, hosts = s.Addresses, mdnsName = s.MdnsName, port = s.Port,
        error = s.Error == LanError.None ? null : s.Error.ToString(), networkPrivate = s.NetworkPrivate,
    };

    internal static bool WpfRunning()
    {
        try { return Process.GetProcessesByName("MobiMate").Length > 0; }
        catch (InvalidOperationException) { return false; }
    }
}
