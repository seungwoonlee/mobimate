using System.Net;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Security;

/// <summary>LAN 모드에서 허용할 호스트 이름(현재 LAN IP, mDNS 이름). S3b에서 실제 구현으로 바뀐다.</summary>
public interface ILanHosts
{
    IReadOnlyCollection<string> Current { get; }
}

public sealed class NoLanHosts : ILanHosts
{
    public IReadOnlyCollection<string> Current => Array.Empty<string>();
}

/// <summary>
/// 요청 보안 파이프라인 (상세설계 §3.2):
/// Host 검증(SEC-01, 421) → Origin 검증(SEC-02, 상태 변경 요청 403) → 예외 경로 → 세션(SEC-03, 401) → CSRF(SEC-04, 403).
/// 정적 파일(앱 셸)은 세션 없이 준다. 루프백 전용 엔드포인트는 <see cref="LoopbackOnly"/> 필터가 막는다(SEC-08).
/// </summary>
public sealed class SecurityMiddleware(RequestDelegate next, DeviceStore devices, ILanHosts lanHosts, MobiMate.Web.Hosting.ServerIdentity identity)
{
    public const string CookieName = "mm_dev";
    public const string CsrfHeader = "X-MobiMate-Csrf";
    public const string DeviceItem = "mm.device";

    private static readonly string[] LoopbackNames = { "localhost", "127.0.0.1", "[::1]", "::1" };
    private static readonly string[] SessionExempt = { "/auth/boot", "/api/ping", "/api/pairing/confirm" };
    private static readonly string[] CsrfExempt = { "/api/pairing/confirm" };

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!IsAllowedHost(ctx.Request.Host.Host) || ctx.Request.Host.Port != identity.Port)
        {
            await ApiResults.Write(ctx, StatusCodes.Status421MisdirectedRequest, "HOST_NOT_ALLOWED", "허용되지 않은 호스트입니다.");
            return;
        }

        var changesState = !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method);
        if (changesState && !IsAllowedOrigin(ctx.Request.Headers.Origin.ToString()))
        {
            await ApiResults.Write(ctx, StatusCodes.Status403Forbidden, "ORIGIN_NOT_ALLOWED", "요청 출처가 허용되지 않았습니다.");
            return;
        }

        var path = ctx.Request.Path.Value ?? "/";
        var isApi = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.Equals("/api", StringComparison.OrdinalIgnoreCase);
        if (!isApi || SessionExempt.Any(p => path.TrimEnd('/').Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(ctx);
            return;
        }

        var device = devices.Authenticate(ctx.Request.Cookies[CookieName], DateTimeOffset.UtcNow);
        if (device == null)
        {
            await ApiResults.Write(ctx, StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "연결 인증이 필요합니다. PC에서는 트레이의 '브라우저 열기'를, 폰·태블릿에서는 QR 페어링을 다시 해 주세요.");
            return;
        }
        ctx.Items[DeviceItem] = device;

        if (changesState && !CsrfExempt.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)) &&
            !devices.CheckCsrf(device.Id, ctx.Request.Headers[CsrfHeader].ToString()))
        {
            await ApiResults.Write(ctx, StatusCodes.Status403Forbidden, "CSRF", "보안 확인 값이 없거나 맞지 않습니다. 화면을 새로 고쳐 주세요.");
            return;
        }

        await next(ctx);
    }

    private bool IsAllowedHost(string host) =>
        LoopbackNames.Contains(host, StringComparer.OrdinalIgnoreCase) || lanHosts.Current.Contains(host, StringComparer.OrdinalIgnoreCase);

    private bool IsAllowedOrigin(string origin)
    {
        if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        // 포트까지 같아야 한다. 같은 PC의 다른 로컬 서버(다른 포트)는 다른 출처다 (SEC-02).
        return uri.Scheme == Uri.UriSchemeHttp && IsAllowedHost(uri.Host) && uri.Port == identity.Port;
    }

    public static DeviceRecord? DeviceOf(HttpContext ctx) => ctx.Items[DeviceItem] as DeviceRecord;

    public static void SetDeviceCookie(HttpContext ctx, string token, DeviceRecord device) =>
        ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Secure = false,   // LAN은 HTTP (SEC-10 위험 수용)
            Expires = device.ExpiresAt ?? DateTimeOffset.UtcNow.AddYears(10),
            IsEssential = true,
        });
}

/// <summary>루프백(이 PC)에서 온 요청만 허용 (SEC-08).</summary>
public sealed class LoopbackOnly : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var ip = context.HttpContext.Connection.RemoteIpAddress;
        if (ip == null || !IPAddress.IsLoopback(ip))
            return ApiResults.Error(StatusCodes.Status403Forbidden, "LOOPBACK_ONLY", "이 작업은 게임 PC에서만 할 수 있습니다.");
        return await next(context);
    }
}
