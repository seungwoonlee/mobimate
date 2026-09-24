using System.Net;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Security;

/// <summary>LAN 모드에서 허용할 호스트 이름(현재 LAN IP, mDNS 이름). S3b에서 실제 구현으로 바뀐다.</summary>
public interface ILanHosts
{
    /// <summary>허용 호스트 이름(현재 연 LAN IP, 광고 중인 mDNS 이름).</summary>
    IReadOnlyCollection<string> Current { get; }

    /// <summary>이 로컬 주소로 지금 LAN 서비스를 하고 있는가.</summary>
    bool Serves(IPAddress local);
}

public sealed class NoLanHosts : ILanHosts
{
    public IReadOnlyCollection<string> Current => Array.Empty<string>();
    public bool Serves(IPAddress local) => false;
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
        // LAN을 내린 뒤에도 Kestrel이 정리 중인 keep-alive 연결로 요청이 올 수 있다. 받은 주소가 지금 서비스 중인 주소가 아니면 거부한다.
        var local = ctx.Connection.LocalIpAddress;
        // 연결 주소와 Host를 묶는다: LAN 주소로 들어온 연결은 LAN 호스트 이름(IP·mDNS)만, 루프백 연결은 허용 목록 전체 (SEC-01).
        var viaLan = local != null && !IPAddress.IsLoopback(local);
        var localOk = !viaLan || lanHosts.Serves(local!);
        var host = ctx.Request.Host.Host;
        var hostOk = viaLan ? lanHosts.Current.Contains(host, StringComparer.OrdinalIgnoreCase) : IsAllowedHost(host);
        if (!localOk || !hostOk || ctx.Request.Host.Port != identity.Port)
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

    private bool IsAllowedHost(string host) => IsAllowedHost(host, lanHosts);

    private bool IsAllowedOrigin(string origin) => IsAllowedOrigin(origin, lanHosts, identity.Port);

    public static bool IsAllowedHost(string host, ILanHosts lan) =>
        LoopbackNames.Contains(host, StringComparer.OrdinalIgnoreCase) || lan.Current.Contains(host, StringComparer.OrdinalIgnoreCase);

    /// <summary>허용 출처: http + 허용 호스트 + 서버 포트 (SEC-02). 포트가 다르면 같은 PC의 다른 서버다.</summary>
    public static bool IsAllowedOrigin(string? origin, ILanHosts lan, int port)
    {
        if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp && IsAllowedHost(uri.Host, lan) && uri.Port == port;
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
