using System.Diagnostics;
using System.Net;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Lan;
using MobiMate.Web.Security;
using MobiMate.Web.Services;

namespace MobiMate.Web.Endpoints;

public sealed record PairingConfirmRequest(string? Code, string? DeviceName);
public sealed record LanRequest(bool? Enabled);

/// <summary>기동 코드 교환, 세션·메타, 페어링 (SEC-03~08, 상세설계 §3.3·§5.1).</summary>
public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        // 기동 코드 → 로컬 기기 토큰 쿠키 → 302로 코드를 URL에서 지운다 (SEC-06)
        app.MapGet("/auth/boot", (HttpContext ctx, string? code, BootCodes boot, DeviceStore devices) =>
        {
            var now = DateTimeOffset.UtcNow;
            if (devices.Authenticate(ctx.Request.Cookies[SecurityMiddleware.CookieName], now) != null) return Results.Redirect("/");
            if (!IsLoopback(ctx) || !boot.Consume(code, now))
                return Results.Content(UnauthorizedPage, "text/html; charset=utf-8", statusCode: StatusCodes.Status401Unauthorized);

            var (device, token) = devices.Issue(DeviceName(ctx.Request.Headers.UserAgent.ToString(), local: true), DeviceKinds.Local, now);
            SecurityMiddleware.SetDeviceCookie(ctx, token, device);
            return Results.Redirect("/");
        });

        // 서버 식별 (FR-MB-13 mDNS 판정용). 인증 불필요, 개인 데이터 없음.
        // IP 주소로 연 페이지가 mDNS 이름 주소로 확인하므로 교차 출처 요청이다. 허용 출처(SEC-01 호스트)만 CORS를 연다 (SEC-03).
        app.MapGet("/api/ping", (HttpContext ctx, ServerIdentity id, ILanHosts lan) =>
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (SecurityMiddleware.IsAllowedOrigin(origin, lan, id.Port))
            {
                ctx.Response.Headers.AccessControlAllowOrigin = origin;
                ctx.Response.Headers.Vary = "Origin";
            }
            return Results.Json(new { serverId = id.ServerId }, ApiResults.Json);
        });

        app.MapGet("/api/session", (HttpContext ctx, DeviceStore devices) =>
        {
            var d = SecurityMiddleware.DeviceOf(ctx)!;
            return ApiResults.Ok(new { deviceId = d.Id, deviceName = d.Name, kind = d.Kind, csrf = devices.CsrfFor(d.Id), expiresAt = d.ExpiresAt });
        });

        app.MapGet("/api/meta", (ServerIdentity id, LanService lan) => ApiResults.Ok(new
        {
            version = id.Version,
            serverId = id.ServerId,
            chatCountMode = ChatText.Mode,
            chatMaxLength = ChatText.MaxLength,
            lan = LanView(lan.Status),
            wpfRunning = WpfRunning(),
        }));

        // LAN 모드 켜기/끄기 (NFR-03, SEC-08 게임 PC 전용)
        app.MapPut("/api/lan", async (LanRequest req, LanService lan, CancellationToken ct) =>
        {
            if (req.Enabled == null) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "enabled가 필요합니다.");
            var s = await lan.SetEnabledAsync(req.Enabled.Value, ct);
            return s == null ? ApiResults.Error(503, "STORAGE_UNAVAILABLE", "설정을 저장하지 못했습니다.") : ApiResults.Ok(LanView(s));
        }).AddEndpointFilter<LoopbackOnly>();

        var pairing = app.MapGroup("/api/pairing");
        // QR은 항상 IP 주소로 연다(urlIp). 이름 주소(urlName)는 폰 페이지가 /api/ping으로 확인한 뒤 옮겨 간다 (FR-MB-10·13).
        pairing.MapPost("/start", (PairingService svc, LanService lan) =>
        {
            var s = lan.Status;
            if (!s.Active) return ApiResults.Error(StatusCodes.Status409Conflict, "LAN_OFF", "LAN 모드가 꺼져 있거나 쓸 수 없습니다. 설정에서 LAN 모드를 켜 주세요.");
            var (code, expiresAt) = svc.Start(DateTimeOffset.UtcNow);
            return ApiResults.Ok(new
            {
                code, expiresAt,
                // QR은 IP 주소로 연다. 광고 중인 이름(n)을 함께 실어 폰 페이지가 이름 주소를 확인할 수 있게 한다 (FR-MB-13)
                urlIp = $"http://{s.Addresses[0]}:{s.Port}/pair?code={code}" + (s.MdnsName is { } nm ? $"&n={Uri.EscapeDataString(nm)}" : ""),
                urlName = s.MdnsName is { } n ? $"http://{n}:{s.Port}/pair?code={code}" : null,
                addresses = s.Addresses,
            });
        }).AddEndpointFilter<LoopbackOnly>();

        pairing.MapPost("/confirm", (HttpContext ctx, PairingConfirmRequest req, PairingService svc, DeviceStore devices, SseHub hub) =>
        {
            var now = DateTimeOffset.UtcNow;
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            switch (svc.Confirm(req.Code?.Trim(), ip, now))
            {
                case PairingResult.Ok:
                    var name = string.IsNullOrWhiteSpace(req.DeviceName) ? DeviceName(ctx.Request.Headers.UserAgent.ToString(), local: false) : req.DeviceName!;
                    var (device, token) = devices.Issue(name, DeviceKinds.Lan, now);
                    SecurityMiddleware.SetDeviceCookie(ctx, token, device);
                    hub.Broadcast("toast", new { level = "ok", message = $"📱 {device.Name} 연결됨" });
                    return ApiResults.Ok(new { deviceId = device.Id, deviceName = device.Name, expiresAt = device.ExpiresAt });
                case PairingResult.Expired:
                    return ApiResults.Error(StatusCodes.Status410Gone, "PAIRING_EXPIRED", "코드가 만료되었습니다. PC에서 새 코드를 받아 주세요.");
                case PairingResult.Locked:
                    return ApiResults.Error(StatusCodes.Status429TooManyRequests, "PAIRING_LOCKED", "실패가 많아 10분 동안 잠겼습니다.");
                default:
                    return ApiResults.Error(StatusCodes.Status400BadRequest, "PAIRING_INVALID", "코드가 맞지 않습니다.");
            }
        });

        pairing.MapGet("/devices", (DeviceStore devices) => ApiResults.Ok(devices.List().Select(d => new
        {
            id = d.Id, name = d.Name, kind = d.Kind, createdAt = d.CreatedAt, lastSeenAt = d.LastSeenAt, expiresAt = d.ExpiresAt,
        }))).AddEndpointFilter<LoopbackOnly>();

        pairing.MapDelete("/devices/{id}", (string id, DeviceStore devices, SseHub hub) =>
        {
            if (!devices.Revoke(id)) return ApiResults.Error(StatusCodes.Status404NotFound, "NOT_FOUND", "없는 기기입니다.");
            var closed = hub.Disconnect(id);
            return ApiResults.Ok(new { revoked = id, closedStreams = closed });
        }).AddEndpointFilter<LoopbackOnly>();
    }

    internal static object LanView(LanStatus s) => new
    {
        enabled = s.Enabled, active = s.Active, hosts = s.Addresses, mdnsName = s.MdnsName, port = s.Port,
        error = s.Error == LanError.None ? null : s.Error.ToString(), networkPrivate = s.NetworkPrivate,
    };

    public static bool IsLoopback(HttpContext ctx) => ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);

    internal static bool WpfRunning()
    {
        try { return Process.GetProcessesByName("MobiMate").Length > 0; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>User-Agent로 기기 이름을 추정한다 (예: "Android · Samsung Internet"). 설정에서 바꿀 수 있다.</summary>
    internal static string DeviceName(string ua, bool local)
    {
        var os = ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Android") ? "Android"
            : ua.Contains("Windows") ? "Windows" : ua.Contains("Mac OS") ? "Mac" : "기기";
        var browser = ua.Contains("SamsungBrowser") ? "Samsung Internet" : ua.Contains("Edg/") ? "Edge"
            : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : ua.Contains("Firefox/") ? "Firefox" : "브라우저";
        return local ? $"이 PC · {browser}" : $"{os} · {browser}";
    }

    private const string UnauthorizedPage = """
        <!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>연결 인증 필요</title>
        <body style="font-family:system-ui,'Malgun Gothic',sans-serif;padding:24px;max-width:36rem;margin:auto;line-height:1.6">
        <h1>연결 인증이 필요합니다</h1>
        <p>이 링크는 만료되었거나 이미 사용되었습니다.</p>
        <ul><li>게임 PC라면 작업 표시줄 트레이의 <b>MobiMate → 브라우저 열기</b>를 누르세요.</li>
        <li>폰·태블릿이라면 PC 화면의 <b>📱 폰으로 보기</b> QR을 다시 찍으세요.</li></ul>
        </body></html>
        """;
}
