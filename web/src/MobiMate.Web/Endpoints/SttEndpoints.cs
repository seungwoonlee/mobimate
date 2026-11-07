using System.Net;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Lan;
using MobiMate.Web.Voice;

namespace MobiMate.Web.Endpoints;

public sealed record SttSettingsRequest(bool? AutoSend, int? MaxSeconds, string? DeviceId, int? Threads, string? Hotkey, string? HotkeyMode, bool? Cue, bool? Hotwords, bool? Spacing, int? SpacingLevel, bool? Refine = null, string? RefineModel = null, int? RefineMs = null, bool? History = null);
public sealed record VocabLearningRequest(bool? LearningOn);
public sealed record VocabPairRequest(string? From, string? To, string? Action);
public sealed record VocabWordRequest(string? Word);
public sealed record HistoryCorrectRequest(string? Id, string? Text);
public sealed record HistoryIdRequest(string? Id);

/// <summary>
/// 음성 입력 요청 검사 (FR-VC-10). 요구사양 Q8로 Host·Origin 검사를 없앴지만, 마이크를 켜는 요청은 예외로 둔다:
/// 다른 웹페이지가 몰래 PC 마이크를 켜는 것을 막는다. 쓰기 요청은 JSON 본문과 사용자 지정 헤더가 필요하다
/// (다른 출처에서 이 둘을 보내려면 사전 요청이 필요한데, 서버는 CORS를 허용하지 않는다). Origin이 있으면 이 서버 주소와 같아야 한다.
/// </summary>
public static class SttGuard
{
    public const string HeaderName = "X-MobiMate-Req";

    /// <summary>
    /// 요청의 Host가 이 서버의 주소인가 — 루프백 IP, localhost, 지금 열어 둔 LAN IP·mDNS 이름만 허용한다.
    /// 다른 웹페이지가 도메인을 이 PC로 돌려 붙는 "DNS 리바인딩"이면 Origin과 Host가 같아 보이고 원격 IP도 루프백이므로,
    /// Host를 직접 확인해야 막을 수 있다 (FR-VC-10).
    /// </summary>
    public static bool HostAllowed(HttpContext ctx, ILanHosts lan)
    {
        var host = ctx.Request.Host.Host;
        if (string.IsNullOrEmpty(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)) return true;
        return lan.Current.Any(h => h.Equals(host, StringComparison.OrdinalIgnoreCase));
    }

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

    /// <summary>설정 변경·학습 내용·모델 설치·마이크 시험은 PC 본체에서만 (FR-VC-10 ⑤)</summary>
    public static IResult? CheckPcOnly(HttpContext ctx) =>
        ClientId.IsLocal(ctx) ? null : ApiResults.Error(StatusCodes.Status403Forbidden, "PC_ONLY", "이 설정은 PC 본체에서만 바꿀 수 있습니다.");
}

/// <summary>마이크 입력 크기 분류 (설정 화면의 "마이크 테스트")</summary>
public static class MicLevelClass
{
    /// <summary>none: 거의 무음(음소거·권한 막힘·연결 문제) / low: 작음 / ok: 알맞음 / loud: 너무 커서 찌그러질 수 있음</summary>
    public static string Classify(double peakDb) => peakDb < -60 ? "none" : peakDb < -30 ? "low" : peakDb > -1 ? "loud" : "ok";
}

/// <summary>음성 입력 (요구사양 §11.13, §7)</summary>
public static class SttEndpoints
{
    /// <summary>설정 저장과 단축키 등록을 한 묶음으로: 동시에 두 요청이 오면 저장된 값과 등록된 값이 어긋날 수 있다</summary>
    private static readonly object SettingsGate = new();

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/stt");

        // 음성 입력은 게임 PC에서만 쓴다: 폰·태블릿(LAN)은 읽기든 쓰기든 모두 거절한다.
        // 읽기(인식 결과·상태)도 Host가 이 서버 주소일 때만 (DNS 리바인딩 방어).
        api.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            if (!SttGuard.HostAllowed(http, http.RequestServices.GetRequiredService<ILanHosts>()))
                return ApiResults.Error(StatusCodes.Status403Forbidden, "STT_FORBIDDEN", "허용되지 않은 주소로 접속했습니다.");
            if (SttGuard.CheckPcOnly(http) is { } notPc) return notPc;
            return await next(ctx);
        });

        api.MapGet("/status", (SttService stt, ISttEngineProvider engines, HotkeyService hotkeys, SttModelInstaller installer, WebSettingsStore s, IVoiceRefiner refiner) =>
        {
            var snap = stt.Snapshot();
            var cur = s.Current;
            var hk = hotkeys.Status;
            var elevation = ElevationProbe.Detect();
            return ApiResults.Ok(new
            {
                state = snap.State.ToString().ToLowerInvariant(),
                sessionId = snap.SessionId,
                byDevice = snap.ByDevice,
                device = snap.DeviceName,
                modelInstalled = engines.IsInstalled,
                install = SttModelInstaller.Dto(installer.State),
                autoSend = cur.SttAutoSend,
                maxSeconds = cur.SttMaxSeconds,
                hotkey = new { key = hk.Key, registered = hk.Registered, error = hk.Error, mode = cur.SttHotkeyMode },
                elevation = new { app = elevation.App, game = elevation.Game, mismatch = elevation.Mismatch },
                refine = new { enabled = refiner.Status.Enabled, model = refiner.Status.Model, cloud = refiner.Status.Cloud, lastError = refiner.Status.LastError, lastMs = refiner.Status.LastMs },
            });
        });

        api.MapGet("/last", (SttService stt) =>
        {
            var f = stt.Last();
            return ApiResults.Ok(f == null ? null : new { sessionId = f.SessionId, text = f.Text, original = f.Original, applied = f.Applied, autoSent = f.AutoSent, at = f.At });
        });

        api.MapGet("/devices", (IAudioSource audio, WebSettingsStore s) =>
            ApiResults.Ok(new { devices = audio.Devices(), selectedId = s.Current.SttDeviceId }));

        api.MapPost("/start", (HttpContext ctx, SttService stt, WebSettingsStore s) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            var r = stt.Start(ClientId.Of(ctx));
            return r.Status switch
            {
                SttStartStatus.Started => ApiResults.Ok(new { sessionId = r.SessionId }),
                SttStartStatus.Cancelled => ApiResults.Ok(new { sessionId = r.SessionId, cancelled = true }),   // 시작하는 중에 중지가 왔다
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

        // 마이크 시험: 1.5초 동안 입력 크기를 잰다 (말하면서 눌러 보라고 안내한다). 듣는 중이면 거절.
        api.MapPost("/devices/test", async (HttpContext ctx, SttService stt, CancellationToken ct) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            if (SttGuard.CheckPcOnly(ctx) is { } notPc) return notPc;
            var o = await stt.TestMicAsync(TimeSpan.FromMilliseconds(1500), ct);
            if (o.Busy) return ApiResults.Error(StatusCodes.Status409Conflict, "STT_BUSY", "듣는 중이거나 시험 중입니다.");
            if (o.Failure != null) return ApiResults.Error(StatusCodes.Status503ServiceUnavailable, SttService.ErrorCode(o.Failure.Kind), o.Failure.Message);
            var r = o.Result!;
            var level = new AudioLevel(r.Peak, r.Rms);
            return ApiResults.Ok(new { device = r.Device, fallback = r.Fallback, peakDb = Math.Round(level.PeakDb, 1), rmsDb = Math.Round(level.RmsDb, 1), level = MicLevelClass.Classify(level.PeakDb) });
        });

        api.MapGet("/model", (SttModelLocator locator, SttModelInstaller installer) =>
            ApiResults.Ok(new { installed = locator.IsInstalled, missing = locator.Missing(), totalBytes = locator.Files.Sum(f => f.Size), install = SttModelInstaller.Dto(installer.State) }));

        api.MapPost("/model/install", (HttpContext ctx, SttModelInstaller installer) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            if (SttGuard.CheckPcOnly(ctx) is { } notPc) return notPc;
            var r = installer.Start();   // 이미 받는 중이거나 설치돼 있어도 오류가 아니다: 현재 상태를 돌려준다
            return ApiResults.Ok(new { started = r == SttInstallStart.Started, alreadyInstalled = r == SttInstallStart.AlreadyInstalled, install = SttModelInstaller.Dto(installer.State) });
        });

        // ── 학습 내용 (FR-VC-08 ⑥): 보기·끄기·지우기는 PC 본체에서만. 낱말에 이름이 섞일 수 있어 폰에서는 보지 못하게 한다. ──
        api.MapGet("/vocab", (HttpContext ctx, VoiceVocabulary v) =>
            SttGuard.CheckPcOnly(ctx) is { } bad ? bad : ApiResults.Ok(v.View()));

        api.MapPut("/vocab", (HttpContext ctx, VocabLearningRequest req, VoiceVocabulary v) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            if (SttGuard.CheckPcOnly(ctx) is { } notPc) return notPc;
            if (req.LearningOn is { } on) v.SetLearning(on);
            return ApiResults.Ok(v.View());
        });

        api.MapPost("/vocab/pair", (HttpContext ctx, VocabPairRequest req, VoiceVocabulary v) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            if (SttGuard.CheckPcOnly(ctx) is { } notPc) return notPc;
            if (string.IsNullOrEmpty(req.From) || string.IsNullOrEmpty(req.To)) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "from과 to가 필요합니다.");
            var found = req.Action switch
            {
                "on" => v.SetPairOff(req.From, req.To, false),
                "off" => v.SetPairOff(req.From, req.To, true),
                "delete" => v.RemovePair(req.From, req.To),
                _ => (bool?)null,
            };
            if (found == null) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "action은 on, off, delete 중 하나입니다.");
            return found.Value ? ApiResults.Ok(v.View()) : ApiResults.Error(StatusCodes.Status404NotFound, "NOT_FOUND", "그런 항목이 없습니다.");
        });

        api.MapPost("/vocab/word", (HttpContext ctx, VocabWordRequest req, VoiceVocabulary v) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            if (SttGuard.CheckPcOnly(ctx) is { } notPc) return notPc;
            if (string.IsNullOrEmpty(req.Word)) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "word가 필요합니다.");
            return v.RemoveWord(req.Word) ? ApiResults.Ok(v.View()) : ApiResults.Error(StatusCodes.Status404NotFound, "NOT_FOUND", "그런 낱말이 없습니다.");
        });

        // ── 지난 음성 기록 보정 (FR-VC-16): 보고, 직접 고쳐서 배우고, 지운다. 이 PC에서만 ──
        api.MapGet("/history", (VoiceHistory h) => ApiResults.Ok(h.List().Select(HistoryView)));

        api.MapPost("/history/correct", (HttpContext ctx, HistoryCorrectRequest req, VoiceHistory h, VoiceVocabulary v) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            var text = ChatText.Sanitize(req.Text);
            if (text.Length == 0 || text.Length > VoiceHistory.MaxTextChars)
                return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", $"고친 문장은 1~{VoiceHistory.MaxTextChars}자입니다.");
            if (string.IsNullOrEmpty(req.Id) || h.Get(req.Id) is not { } e) return ApiResults.Error(StatusCodes.Status404NotFound, "NOT_FOUND", "그 기록이 없습니다(7일이 지났거나 지웠습니다).");
            var learned = v.LearnCorrection(text, e.Original);
            if (learned < 0) return ApiResults.Error(StatusCodes.Status409Conflict, "LEARNING_OFF", "누적 학습이 꺼져 있어 배우지 못했습니다. 학습을 켜 주세요.");
            h.MarkCorrected(e.Id, text);
            return ApiResults.Ok(new { learned, entry = HistoryView(h.Get(e.Id)!) });
        });

        api.MapPost("/history/delete", (HttpContext ctx, HistoryIdRequest req, VoiceHistory h) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            return !string.IsNullOrEmpty(req.Id) && h.Delete(req.Id) ? ApiResults.Ok(new { }) : ApiResults.Error(StatusCodes.Status404NotFound, "NOT_FOUND", "그 기록이 없습니다.");
        });

        api.MapPost("/history/clear", (HttpContext ctx, VoiceHistory h) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            h.Clear();
            return ApiResults.Ok(new { });
        });

        api.MapPost("/vocab/clear", (HttpContext ctx, VoiceVocabulary v) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            if (SttGuard.CheckPcOnly(ctx) is { } notPc) return notPc;
            v.Clear();
            return ApiResults.Ok(v.View());
        });

        // 설치된 Ollama 모델 목록 (교정 모델 고르기용). 이 PC의 Ollama만 부른다.
        api.MapGet("/refine/models", async (HttpContext ctx, WebSettingsStore s) =>
        {
            if (SttGuard.CheckPcOnly(ctx) is { } bad) return bad;
            var names = await new OllamaService().GetInstalledModelsAsync(ctx.RequestAborted);
            var models = names.Where(n => !n.Contains("embed", StringComparison.OrdinalIgnoreCase))
                .Select(n => new { name = n, cloud = OllamaVoiceRefiner.IsCloud(n) }).OrderBy(m => m.cloud).ThenBy(m => m.name).ToList();
            return ApiResults.Ok(new { online = names.Count > 0, models, selected = s.Current.SttRefineModel ?? OllamaVoiceRefiner.DefaultModel });
        });

        api.MapGet("/settings", (HttpContext ctx, WebSettingsStore s) =>
        {
            if (SttGuard.CheckPcOnly(ctx) is { } bad) return bad;
            return ApiResults.Ok(ToDto(s.Current));
        });

        api.MapPut("/settings", (HttpContext ctx, SttSettingsRequest req, WebSettingsStore s, HotkeyService hotkeys, IVoiceRefiner refiner) =>
        {
            if (SttGuard.CheckWrite(ctx) is { } bad) return bad;
            if (SttGuard.CheckPcOnly(ctx) is { } notPc) return notPc;
            if (req.MaxSeconds is < 1 or > 60) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "듣는 시간은 1~60초입니다.");
            if (req.SpacingLevel is < 0 or > 2) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "띄어쓰기 강도는 0(보수적)~2(적극적)입니다.");
            if (req.RefineMs is < 500 or > 10000) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "다듬기 제한 시간은 0.5~10초입니다.");
            if (req.RefineModel is { Length: > 100 } || (req.RefineModel?.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ?? false)) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "모델 이름이 올바르지 않습니다.");
            if (req.Threads is < 1 or > 4) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "스레드 수는 1~4입니다.");
            if (req.DeviceId is { Length: > 300 } || (req.DeviceId?.Any(char.IsControl) ?? false)) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "마이크 ID가 올바르지 않습니다.");
            if (req.HotkeyMode is not (null or "hold" or "toggle")) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "단축키 방식은 hold 또는 toggle입니다.");
            if (!string.IsNullOrWhiteSpace(req.Hotkey) && HotkeyParser.Parse(req.Hotkey) == null)
                return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "단축키 형식이 올바르지 않습니다. Ctrl·Alt·Win 중 하나와 키를 함께 쓰거나 F1~F24를 쓰세요. (예: Ctrl+Alt+V, F9)");

            bool threadsChanged, ok;
            HotkeyStatus hk;
            lock (SettingsGate)
            {
            // 스레드 수와 핫워드는 인식기를 만들 때 한 번 읽으므로 바뀌면 앱을 다시 켜야 적용된다
            threadsChanged = (req.Threads != null && req.Threads != s.Current.SttThreads) || (req.Hotwords != null && req.Hotwords != s.Current.SttHotwords);
            var hotkeyChanged = req.Hotkey != null;
            ok = s.Update(x =>
            {
                if (req.AutoSend != null) x.SttAutoSend = req.AutoSend.Value;
                if (req.MaxSeconds != null) x.SttMaxSeconds = req.MaxSeconds.Value;
                if (req.Threads != null) x.SttThreads = req.Threads.Value;
                if (req.DeviceId != null) x.SttDeviceId = req.DeviceId.Length == 0 ? null : req.DeviceId;   // 빈 문자열 = 시스템 기본 마이크
                if (req.Hotkey != null) x.SttHotkey = string.IsNullOrWhiteSpace(req.Hotkey) ? null : HotkeyParser.Parse(req.Hotkey)!.Display;
                if (req.HotkeyMode != null) x.SttHotkeyMode = req.HotkeyMode;
                if (req.Cue != null) x.SttCue = req.Cue.Value;
                if (req.Hotwords != null) x.SttHotwords = req.Hotwords.Value;
                if (req.Spacing != null) x.SttSpacing = req.Spacing.Value;
                if (req.History != null) x.SttHistory = req.History.Value;
                if (req.SpacingLevel != null) x.SttSpacingLevel = req.SpacingLevel.Value;
                if (req.Refine != null) x.SttRefine = req.Refine.Value;
                if (req.RefineModel != null) x.SttRefineModel = req.RefineModel.Length == 0 ? null : req.RefineModel;
                if (req.RefineMs != null) x.SttRefineMs = req.RefineMs.Value;
            });
            // 단축키는 저장한 즉시 다시 등록한다. 다른 프로그램이 쓰는 조합이면 등록은 실패하지만 설정은 남기고 이유를 알려 준다.
            hk = ok && hotkeyChanged ? hotkeys.Apply(s.Current.SttHotkey) : hotkeys.Status;
            }
            if (!ok) return ApiResults.Error(StatusCodes.Status500InternalServerError, "SAVE_FAILED", "설정을 저장하지 못했습니다.");
            if (req.Refine == true || req.RefineModel != null) _ = Task.Run(refiner.WarmUpAsync);   // 새로 고른 모델을 미리 올려 첫 문장이 느리지 않게
            // 스레드 수는 인식 엔진을 만들 때 한 번 읽으므로 앱을 다시 켜야 적용된다
            return ApiResults.Ok(new { settings = ToDto(s.Current), restartRequired = threadsChanged, hotkey = new { key = hk.Key, registered = hk.Registered, error = hk.Error } });
        });
    }

    private static object HistoryView(HistoryEntry e) => new
    {
        id = e.Id, at = new DateTimeOffset(DateTime.SpecifyKind(e.At, DateTimeKind.Utc)), original = e.Original, text = e.Text, autoSent = e.AutoSent, corrected = e.Corrected,
    };

    private static object ToDto(WebSettings s) => new
    {
        autoSend = s.SttAutoSend,
        maxSeconds = s.SttMaxSeconds,
        deviceId = s.SttDeviceId,
        threads = s.SttThreads,
        hotkey = s.SttHotkey,
        hotkeyMode = s.SttHotkeyMode,
        cue = s.SttCue,
        hotwords = s.SttHotwords,
        spacing = s.SttSpacing,
        history = s.SttHistory,
        spacingLevel = s.SttSpacingLevel,
        refine = s.SttRefine,
        refineModel = s.SttRefineModel ?? OllamaVoiceRefiner.DefaultModel,
        refineMs = s.SttRefineMs,
    };
}
