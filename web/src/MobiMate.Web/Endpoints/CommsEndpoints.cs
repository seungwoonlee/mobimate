using System.Collections.Concurrent;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Security;
using MobiMate.Web.Services;

namespace MobiMate.Web.Endpoints;

public sealed record ChatRequest(string? Text, bool? AutoEmote, string? Source = null);
public sealed record ChatterRequest(string? Persona, string? CustomId);
public sealed record PersonaRequest(string? Name, string? Emoji, string? Prompt);
public sealed record PersonaGenerateRequest(string? Request);
public sealed record EngineSelectRequest(string? Id, bool? ConfirmPaid);
public sealed record AskRequest(string? Text);
public sealed record SettingsRequest(int? MaxRefreshSec, bool? AutoEmoteDefault, string? CliPath, string? ChatterPersona = null);
public sealed record ClientErrorRequest(string? Message, string? Source, string? Stack);

/// <summary>채팅·아무말·페르소나·AI·설정·SSE (요구사양서 §7).</summary>
public static class CommsEndpoints
{
    private const int MaxNameLength = 20;
    private const int MaxPromptLength = 300;
    private static readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Window)> ClientErrorRate = new();

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ── 게임 채팅 (FR-GC) ──
        api.MapPost("/chat/game/preview", (ChatRequest req, WebSettingsStore s) =>
            ApiResults.Ok(ChatService.Preview(req.Text, req.AutoEmote ?? s.Current.AutoEmoteDefault)));

        api.MapPost("/chat/game", async (HttpContext ctx, ChatRequest req, ChatService chat, WebSettingsStore s, CancellationToken ct) =>
        {
            // 출처는 "직접" 또는 "아무말 · <페르소나>"만 받는다 (FR-GC-05). 그 밖의 값은 "직접"으로 둔다
            var source = req.Source is { Length: <= 40 } src && src.StartsWith("아무말", StringComparison.Ordinal) ? src : "직접";
            var (status, entry) = await chat.SendAsync(req.Text, req.AutoEmote ?? s.Current.AutoEmoteDefault, source, SecurityMiddleware.DeviceOf(ctx)!.Id, ct);
            return status switch
            {
                ChatSendStatus.Sent => ApiResults.Ok(entry),
                ChatSendStatus.Empty => ApiResults.Error(400, "VALIDATION", "보낼 내용이 없습니다."),
                ChatSendStatus.Duplicate => ApiResults.Error(409, "DUPLICATE", "같은 문장을 방금 보냈습니다."),
                _ => ApiResults.Error(502, "CLI_FAILED", entry?.Error ?? "채팅 전송 실패"),
            };
        });

        api.MapGet("/chat/game/log", (ChatService chat) => ApiResults.Ok(chat.Log()));

        // ── 아무말 한마디 (FR-CH-02) ──
        api.MapPost("/chatter/line", async (ChatterRequest req, ChatterLineService lines, SnapshotManager snapshots, GameStateCache state, GameViews views, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ChatterPersona>(req.Persona, ignoreCase: true, out var persona))
                return ApiResults.Error(400, "VALIDATION", "알 수 없는 페르소나입니다.");
            CustomPersona? custom = null;
            if (persona == ChatterPersona.Custom)
            {
                custom = snapshots.LoadCustomPersonas().FirstOrDefault(p => p.Id == req.CustomId);
                if (custom == null) return ApiResults.Error(404, "NOT_FOUND", "없는 커스텀 페르소나입니다.");
            }
            if (state.IsHeaderStale) await views.HeaderAsync(ct);
            var line = await lines.GenerateLineAsync(persona, custom, state.BuildChatterContext(), ct);
            return ApiResults.Ok(new { text = line.Raw, final = line.Text, usedFallback = line.UsedFallback, count = ChatText.Count(line.Text) });
        });

        // ── 커스텀 페르소나 (FR-CH-04, FR-CH-07) ──
        api.MapGet("/personas", (SnapshotManager s) => ApiResults.Ok(s.LoadCustomPersonas()));

        api.MapPost("/personas", (PersonaRequest req, SnapshotManager s, SseHub hub) =>
        {
            if (Validate(req) is { } err) return err;
            var p = new CustomPersona { Name = req.Name!.Trim(), TagEmoji = Emoji(req.Emoji), SystemPrompt = req.Prompt!.Trim() };
            s.SaveCustomPersona(p);
            hub.Broadcast("state.changed", new { keys = new[] { "personas" } });
            return ApiResults.Ok(p);
        });

        api.MapPut("/personas/{id}", (string id, PersonaRequest req, SnapshotManager s, SseHub hub) =>
        {
            if (Validate(req) is { } err) return err;
            var p = s.LoadCustomPersonas().FirstOrDefault(x => x.Id == id);
            if (p == null) return ApiResults.Error(404, "NOT_FOUND", "없는 페르소나입니다.");
            p.Name = req.Name!.Trim();
            p.TagEmoji = Emoji(req.Emoji);
            p.SystemPrompt = req.Prompt!.Trim();
            s.SaveCustomPersona(p);
            hub.Broadcast("state.changed", new { keys = new[] { "personas" } });
            return ApiResults.Ok(p);
        });

        api.MapDelete("/personas/{id}", (string id, SnapshotManager s, SseHub hub) =>
        {
            if (!s.DeleteCustomPersona(id)) return ApiResults.Error(404, "NOT_FOUND", "없는 페르소나입니다.");
            hub.Broadcast("state.changed", new { keys = new[] { "personas" } });
            return ApiResults.Ok(new { deleted = id });
        });

        api.MapPost("/personas/generate", async (PersonaGenerateRequest req, AiEngineManager engines, AiService ai, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Request) || req.Request.Length > 200) return ApiResults.Error(400, "VALIDATION", "요청은 1~200자입니다.");
            await ai.EnsureDiscoveredAsync(ct);
            var draft = await PersonaGenerator.GenerateAsync(req.Request, engines.CurrentEngine, ct);
            return ApiResults.Ok(draft);   // 초안만 돌려주고 저장하지 않는다
        });

        // ── AI 도우미 (FR-AI) ──
        api.MapGet("/ai/engines", async (AiService ai, CancellationToken ct) =>
        {
            await ai.EnsureDiscoveredAsync(ct);
            return ApiResults.Ok(ai.Describe());
        });

        api.MapPost("/ai/engines/discover", async (AiService ai, SseHub hub, CancellationToken ct) =>
        {
            await ai.DiscoverAsync(ct);
            hub.Broadcast("state.changed", new { keys = new[] { "engine" } });
            return ApiResults.Ok(ai.Describe());
        });

        api.MapPut("/ai/engines/current", async (EngineSelectRequest req, AiService ai, CancellationToken ct) =>
        {
            await ai.EnsureDiscoveredAsync(ct);
            return ai.Select(req.Id ?? "", req.ConfirmPaid == true) switch
            {
                SelectEngineResult.Selected => ApiResults.Ok(ai.Describe()),
                SelectEngineResult.NotFound => ApiResults.Error(404, "NOT_FOUND", "없는 엔진입니다."),
                SelectEngineResult.NeedsPaidConfirmation => ApiResults.Error(409, "CONFIRM_PAID", "계정 과금 엔진입니다. 확인 후 다시 요청하세요."),
                _ => ApiResults.Error(503, "STORAGE_UNAVAILABLE", "설정을 저장하지 못했습니다."),
            };
        });

        api.MapPost("/ai/ask", async (HttpContext ctx, AskRequest req, AiService ai) =>
        {
            if (string.IsNullOrWhiteSpace(req.Text) || req.Text.Length > 500)
            {
                await ApiResults.Error(400, "VALIDATION", "질문은 1~500자입니다.").ExecuteAsync(ctx);
                return;
            }
            try
            {
                await ai.AskAsync(ctx, req.Text.Trim(), ctx.RequestAborted);
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
            {
                // 사용자가 중지함 (FR-AI-08)
            }
        });

        // ── 설정 (FR-ST) ──
        api.MapGet("/settings", (WebSettingsStore s, GameCli cli) => ApiResults.Ok(new
        {
            s.Current.MaxRefreshSec, s.Current.AutoEmoteDefault, s.Current.LanEnabled, s.Current.ChatterPersona, cliPath = cli.CliPath, cliAvailable = cli.IsAvailable,
        }));

        api.MapPut("/settings", async (HttpContext ctx, SettingsRequest req, WebSettingsStore s, GameCli cli, StatusMonitor status, SseHub hub) =>
        {
            if (req.MaxRefreshSec is < 15 or > 3600) return ApiResults.Error(400, "VALIDATION", "자동 갱신 최대 주기는 15~3600초입니다.");
            if (req.ChatterPersona != null && !IsPersonaValue(req.ChatterPersona)) return ApiResults.Error(400, "VALIDATION", "알 수 없는 페르소나입니다.");
            if (req.CliPath != null)
            {
                if (!AuthEndpoints.IsLoopback(ctx)) return ApiResults.Error(403, "LOOPBACK_ONLY", "CLI 경로는 게임 PC에서만 바꿀 수 있습니다.");
                if (CliPathValidator.Validate(req.CliPath) is { } why) return ApiResults.Error(400, "VALIDATION", why);
            }
            if (!s.Update(x =>
                {
                    if (req.MaxRefreshSec is { } m) x.MaxRefreshSec = m;
                    if (req.AutoEmoteDefault is { } a) x.AutoEmoteDefault = a;
                    if (req.CliPath != null) x.CliPath = req.CliPath;
                    if (req.ChatterPersona != null) x.ChatterPersona = req.ChatterPersona;
                }))
                return ApiResults.Error(503, "STORAGE_UNAVAILABLE", "설정을 저장하지 못했습니다.");

            if (req.CliPath != null)
            {
                cli.CliPath = req.CliPath;
                await status.CheckNowAsync(ctx.RequestAborted);
            }
            hub.Broadcast("state.changed", new { keys = new[] { "settings" } });
            return ApiResults.Ok(new { s.Current.MaxRefreshSec, s.Current.AutoEmoteDefault, s.Current.ChatterPersona, cliPath = cli.CliPath, cliAvailable = cli.IsAvailable });
        });

        // ── 클라이언트 오류 보고 (FR-ST-03): 기기별 분당 10건 ──
        api.MapPost("/client-errors", (HttpContext ctx, ClientErrorRequest req, ILogger<ClientErrorRequest> log) =>
        {
            var id = SecurityMiddleware.DeviceOf(ctx)!.Id;
            var now = DateTimeOffset.UtcNow;
            var cur = ClientErrorRate.AddOrUpdate(id, _ => (1, now), (_, v) => now - v.Window > TimeSpan.FromMinutes(1) ? (1, now) : (v.Count + 1, v.Window));
            if (cur.Count > 10) return ApiResults.Error(429, "RATE_LIMITED", "오류 보고가 너무 많습니다.");
            log.LogWarning("클라이언트 오류 [{Device}] {Source}: {Message}", id, Trim(req.Source, 100), Trim(req.Message, 300));
            return ApiResults.Ok(new { received = true });
        });

        // ── SSE ──
        api.MapGet("/events", (HttpContext ctx, SseHub hub) => hub.Serve(ctx, SecurityMiddleware.DeviceOf(ctx)!.Id));
    }

    private static bool IsPersonaValue(string v) =>
        (v.StartsWith("custom:", StringComparison.Ordinal) && v.Length is > 7 and <= 64)
        || (Enum.TryParse<ChatterPersona>(v, out var p) && p != ChatterPersona.Custom);

    private static IResult? Validate(PersonaRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > MaxNameLength) return ApiResults.Error(400, "VALIDATION", $"이름은 1~{MaxNameLength}자입니다.");
        if (string.IsNullOrWhiteSpace(req.Prompt) || req.Prompt.Trim().Length > MaxPromptLength) return ApiResults.Error(400, "VALIDATION", $"말투 지침은 1~{MaxPromptLength}자입니다.");
        return null;
    }

    private static string Emoji(string? e) => string.IsNullOrWhiteSpace(e) ? "🎭" : ChatText.Truncate(e.Trim(), 4);

    private static string Trim(string? s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
}

/// <summary>CLI 경로 검증 (SEC-09): 로컬 고정 드라이브의 존재하는 .exe만. UNC·네트워크 드라이브·상대 경로·링크 우회 거부.</summary>
public static class CliPathValidator
{
    public static string? Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\") || path.StartsWith("//") || !Path.IsPathFullyQualified(path))
            return "로컬 드라이브의 절대 경로만 쓸 수 있습니다.";
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return ".exe 파일만 지정할 수 있습니다.";
        try
        {
            var root = Path.GetPathRoot(path);
            if (root == null || new DriveInfo(root).DriveType != DriveType.Fixed) return "로컬 고정 드라이브만 쓸 수 있습니다.";
            var info = new FileInfo(path);
            if (!info.Exists) return "파일이 없습니다.";
            if (info.LinkTarget != null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                if (target == null || Validate(target) is { } why) return $"링크 대상이 허용되지 않습니다: {target}";
            }
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return "경로를 확인할 수 없습니다.";
        }
    }
}
