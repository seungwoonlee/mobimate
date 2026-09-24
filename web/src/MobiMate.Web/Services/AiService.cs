using System.Text.Json;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

public enum SelectEngineResult { Selected, NotFound, NeedsPaidConfirmation, SaveFailed }

/// <summary>
/// AI 도우미 (FR-AI): 엔진 감지·선택(무료만 자동 복원, 유료는 확인 필요), 자연어 명령 판정, 응답 스트리밍(NDJSON).
/// 응답은 요청한 기기에게만 보내고 SSE로 뿌리지 않는다 (FR-AI-04). 대화 본문은 로그에 쓰지 않는다.
/// </summary>
public sealed class AiService(AiEngineManager engines, WebSettingsStore settings, GameStateCache state, GameViews views,
    GameActions actions, GameQueries q, SseHub hub, ILogger<AiService> log)
{
    private readonly SemaphoreSlim _discover = new(1, 1);
    private bool _discovered;

    public async Task EnsureDiscoveredAsync(CancellationToken ct = default)
    {
        if (_discovered) return;
        await DiscoverAsync(ct);
    }

    public async Task DiscoverAsync(CancellationToken ct = default)
    {
        await _discover.WaitAsync(ct);
        try
        {
            await engines.DiscoverEnginesAsync(ct, settings.Current.AiEngineId);
            _discovered = true;
            log.LogInformation("AI 엔진 {Count}개 감지, 현재 {Id}", engines.AvailableEngines.Count, engines.CurrentEngine?.Info.Id);
        }
        finally
        {
            _discover.Release();
        }
    }

    public object Describe() => new
    {
        current = engines.CurrentEngine?.Info.Id,
        engines = engines.AvailableEngines.Select(e => new
        {
            id = e.Info.Id, name = e.Info.DisplayName, type = e.Info.Type, costTier = e.Info.CostTier, description = e.Info.Description,
        }),
    };

    public SelectEngineResult Select(string id, bool confirmPaid)
    {
        var engine = engines.AvailableEngines.FirstOrDefault(e => e.Info.Id == id);
        if (engine == null) return SelectEngineResult.NotFound;
        if (engine.Info.CostTier == AiCostTier.Paid && !confirmPaid) return SelectEngineResult.NeedsPaidConfirmation;
        if (!settings.Update(s => s.AiEngineId = id)) return SelectEngineResult.SaveFailed;
        engines.SetCurrentEngine(id);
        hub.Broadcast("state.changed", new { keys = new[] { "engine" } });
        return SelectEngineResult.Selected;
    }

    /// <summary>질문 처리. 한 줄에 JSON 객체 하나씩(NDJSON) 쓴다 (상세설계 §5.3).</summary>
    public async Task AskAsync(HttpContext ctx, string text, CancellationToken ct)
    {
        ctx.Response.ContentType = "application/x-ndjson; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";

        async Task Emit(object o)
        {
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(o, ApiResults.Json) + "\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }

        await EnsureDiscoveredAsync(ct);
        var gatherables = state.Gatherables?.Select(g => g.DisplayName).ToList();
        if (gatherables == null)
        {
            var g = await q.Get<GatherableResponse>("get_gatherable_items", ct);
            state.UpdateGatherables(g.Value?.Items);
            gatherables = g.Value?.Items?.Select(x => x.DisplayName).ToList() ?? new();
        }

        var intent = CommandIntentParser.Parse(text, gatherables);
        switch (intent.Kind)
        {
            case IntentKind.Stop:
                var r = await actions.StopAsync(ct);
                await Emit(new { type = "action", kind = "stop", ok = r.Ok, message = r.Ok ? "행동 정지를 요청했습니다." : $"정지 실패: {r.Error}" });
                await Emit(new { type = "done" });
                return;
            case IntentKind.CheckDailyMissions:
                await Emit(new { type = "navigate", to = "/homework?tab=daily" });
                await Emit(new { type = "token", t = "남은 일일 숙제를 열었습니다." });
                await Emit(new { type = "done" });
                return;
            case IntentKind.InventoryDiet:
                await Emit(new { type = "navigate", to = "/inventory?loc=diet" });
                await Emit(new { type = "token", t = "가방 다이어트 필터를 켰습니다. 이번 접속에 늘어난 잡템이 위에 보입니다." });
                await Emit(new { type = "done" });
                return;
            case IntentKind.CollectWorks:
                var works = await q.Get<AlteringWorksResponse>("get_altering_works", ct);
                await Emit(new
                {
                    type = "intent", kind = "collect",
                    items = works.Value?.Works?.Where(w => w.IsCompleted || w.RemainingSeconds <= 0).Select(w => w.DisplayName) ?? Array.Empty<string>(),
                });
                await Emit(new { type = "done" });
                return;
            case IntentKind.Gather:
                await Emit(new { type = "intent", kind = "gather", item = intent.ItemName, count = intent.Count, wingsCost = 5 });
                await Emit(new { type = "done" });
                return;
        }

        if (state.IsHeaderStale) await views.HeaderAsync(ct);
        var engine = engines.CurrentEngine;
        if (engine == null)
        {
            await Emit(new { type = "error", message = "선택된 AI 엔진이 없습니다." });
            return;
        }

        try
        {
            await foreach (var token in engine.StreamAsync(text, state.BuildAiContext(), ct))
                await Emit(new { type = "token", t = token });
            await Emit(new { type = "done", engine = engine.Info.Id });
        }
        catch (AiEngineException ex)
        {
            await Emit(new { type = "error", message = ex.Message });
        }
    }
}
