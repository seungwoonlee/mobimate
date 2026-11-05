using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Lan;
using MobiMate.Web.Voice;

namespace MobiMate.Web.Endpoints;

public sealed record SttSettingsRequest(bool? AutoSend, bool? AllowLanStart, int? MaxSeconds, string? DeviceId, int? Threads);

/// <summary>
/// 음성 입력 요청 검사 (FR-VC-10). 요구사양 Q8로 Host·Origin 검사를 없앴지만, 마이크를 켜는 요청은 예외로 둔다:
/// 다른 웹페이지가 몰래 PC 마이크를 켜는 것을 막는다. 쓰기 요청은 JSON 본문과 사용자 지정 헤더가 필요하다
/// (다른 출처에서 이 둘을 보내려면 사전 요청이 필요한데, 서버는 CORS를 허용하지 않는다). Origin이 있으면 이 서버 주소와 같아야 한다.
/// </summary>
public static class SttGuard
{
    public const string HeaderName = "X-MobiMate-Req";

    /// <summary>통과하면 null, 아니면 응답</summary>
    public static IResult? CheckWrite(HttpContext ctx)
    {
        if (ctx.Request.Headers[HeaderName] != "1")
            return ApiResults.Error(StatusCodes.Status403Forbidden, "STT_FORBIDDEN", "요청 형식이 올바르지 않습니다.");
        if (ctx.Request.ContentType is not { } ct || !ct.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            return ApiResults.Error(StatusCodes.Status415UnsupportedMediaType, "STT_FORBIDDEN", "JSON 본문이 필요합니다.");
        if (ctx.Request.Headers.Origin.ToString() is { Length: > 0 } origin)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !string.Equals(uri.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
                return ApiResults.Error(StatusCodes.Status403Forbidden, "STT_FORBIDDEN", "다른 사이트에서 보낸 요청은 받지 않습니다.");
        }
        return null;
    }

    /// <summary>설정 변경·학습 내용·모델 설치는 PC 본체에서만 (FR-VC-10 ⑤)</summary>
    public static IResult? CheckPcOnly(HttpContext ctx) =>
        ClientId.IsLocal(ctx) ? null : ApiResults.Error(StatusCodes.Status403Forbidden, "PC_ONLY", "이 설정은 PC 본체에서만 바꿀 수 있습니다.");
}

/// <summary>음성 입력 (요구사양 §11.13, §7)</summary>
public static class SttEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/stt");

        api.MapGet("/status", (SttService stt, ISttEngineProvider engines, WebSettingsStore s) =>
        {
            var snap = stt.Snapshot();
            var cur = s.Current;
            return ApiResults.Ok(new
            {
                state = snap.State.ToString().ToLowerInvariant(),
                sessionId = snap.SessionId,
                byDevice = snap.ByDevice,
                device = snap.DeviceName,
                modelInstalled = engines.IsInstalled,
                autoSend = cur.SttAutoSend,
                allowLanStart = cur.SttAllowLanStart,
                maxSeconds = cur.SttMaxSeconds,
            });
        });

        api.MapGet("/last", (SttService stt) =>
        {
            var f = stt.Last();
            return ApiResults.Ok(f == null ? null : new { sessionId = f.SessionId, text = f.Text, original = f.Original, applied = f.Applied, at = f.At });
        });

        api.MapGet("/devices", (IAudioSource audio, WebSettingsStore s) =>
            ApiResults.Ok(new { devices = audio.Devices(), selectedId = s.Current.SttDeviceId }));

        api.MapPost("/start", (HttpContext ctx, SttService stt, WebSettingsStore s) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            if (!ClientId.IsLocal(ctx) && !s.Current.SttAllowLanStart)
                return ApiResults.Error(StatusCodes.Status403Forbidden, "STT_LAN_DISABLED", "폰·태블릿에서 음성 시작은 PC 설정에서 켜야 합니다.");

            var r = stt.Start(ClientId.Of(ctx));
            return r.Status switch
            {
                SttStartStatus.Started => ApiResults.Ok(new { sessionId = r.SessionId }),
                SttStartStatus.Busy => ApiResults.Error(StatusCodes.Status409Conflict, "STT_BUSY", "이미 듣고 있습니다."),
                SttStartStatus.ModelMissing => ApiResults.Error(StatusCodes.Status409Conflict, "STT_MODEL_MISSING", "음성 인식 모델이 설치되어 있지 않습니다."),
                SttStartStatus.MicFailed => ApiResults.Error(StatusCodes.Status503ServiceUnavailable, SttService.ErrorCode(r.Failure!.Kind), r.Failure.Message),
                _ => ApiResults.Error(StatusCodes.Status500InternalServerError, "STT_ENGINE_FAILED", r.Message ?? "음성 인식을 시작하지 못했습니다."),
            };
        });

        api.MapPost("/stop", (HttpContext ctx, SttService stt) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            return ApiResults.Ok(new { stopped = stt.Stop() });   // 듣는 중이 아니어도 오류가 아니다
        });

        api.MapGet("/settings", (HttpContext ctx, WebSettingsStore s) =>
        {
            if (SttGuard.CheckPcOnly(ctx) is { } bad) return bad;
            return ApiResults.Ok(ToDto(s.Current));
        });

        api.MapPut("/settings", (HttpContext ctx, SttSettingsRequest req, WebSettingsStore s) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            if (SttGuard.CheckPcOnly(ctx) is { } notPc) return notPc;
            if (req.MaxSeconds is < 1 or > 60) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "듣는 시간은 1~60초입니다.");
            if (req.Threads is < 1 or > 4) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "스레드 수는 1~4입니다.");
            var ok = s.Update(x =>
            {
                if (req.AutoSend != null) x.SttAutoSend = req.AutoSend.Value;
                if (req.AllowLanStart != null) x.SttAllowLanStart = req.AllowLanStart.Value;
                if (req.MaxSeconds != null) x.SttMaxSeconds = req.MaxSeconds.Value;
                if (req.Threads != null) x.SttThreads = req.Threads.Value;
                if (req.DeviceId != null) x.SttDeviceId = req.DeviceId.Length == 0 ? null : req.DeviceId;   // 빈 문자열 = 시스템 기본 마이크
            });
            return ok ? ApiResults.Ok(ToDto(s.Current)) : ApiResults.Error(StatusCodes.Status500InternalServerError, "SAVE_FAILED", "설정을 저장하지 못했습니다.");
        });
    }

    private static object ToDto(WebSettings s) => new
    {
        autoSend = s.SttAutoSend,
        allowLanStart = s.SttAllowLanStart,
        maxSeconds = s.SttMaxSeconds,
        deviceId = s.SttDeviceId,
        threads = s.SttThreads,
    };
}
