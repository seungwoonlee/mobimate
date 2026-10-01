using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Lan;
using MobiMate.Web.Services;

namespace MobiMate.Web.Endpoints;

public sealed record GatherRequest(string? DisplayName, int? Count);
public sealed record CollectRequest(string? DisplayName);
public sealed record HomeworkSetRequest(bool? Completed, int? Count);
public sealed record HomeworkResetRequest(string? Scope);
public sealed record NicknameRequest(string? Nickname);
public sealed record FavoriteRequest(string? Name, bool? Favorite);
public sealed record MembershipRequest(string? Character, int? Days, int? Hours);
public sealed record AssignRequest(string? Character, string? Account);

/// <summary>게임 조회·조작·숙제 API (요구사양서 §7).</summary>
public static class GameEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/status", (StatusMonitor s) => ApiResults.Ok(new { state = StatusMonitor.Wire(s.State), since = s.Since }));

        api.MapGet("/header", (GameViews v, CancellationToken ct) => Respond(v.HeaderAsync(ct)));
        api.MapGet("/character", (GameViews v, CancellationToken ct) => Respond(v.CharacterAsync(ct)));
        api.MapGet("/inventory", (GameViews v, CancellationToken ct) => Respond(v.InventoryAsync(ct)));
        api.MapGet("/currencies", (GameViews v, CancellationToken ct) => Respond(v.CurrenciesAsync(ct)));
        api.MapGet("/missions", (GameViews v, CancellationToken ct) => Respond(v.MissionsAsync(ct)));
        api.MapGet("/life", (GameViews v, CancellationToken ct) => Respond(v.LifeAsync(ct)));
        api.MapGet("/nearby", (GameViews v, CancellationToken ct) => Respond(v.NearbyAsync(ct)));
        api.MapGet("/characters", async (GameViews v, CancellationToken ct) => ApiResults.Ok(await v.CharactersAsync(ct)));

        // ── 계정 (v1.5): 멤버십 등록(남은 기간 입력), 캐릭터를 계정에 직접 묶기·풀기 ──
        api.MapPut("/membership", (MembershipRequest req, AccountStore accounts, GameViews v, SseHub hub) =>
        {
            if (string.IsNullOrWhiteSpace(req.Character)) return ApiResults.Error(400, "VALIDATION", "캐릭터가 필요합니다.");
            var days = req.Days ?? 0; var hours = req.Hours ?? 0;
            if (days is < 0 or > 400 || hours is < 0 or > 23) return ApiResults.Error(400, "VALIDATION", "남은 기간은 0~400일, 0~23시간으로 입력해 주세요.");
            if (!v.IsKnownCharacter(req.Character)) return ApiResults.Error(404, "NOT_FOUND", "기록에 없는 캐릭터입니다.");
            DateTime? expires = days == 0 && hours == 0 ? null : DateTime.UtcNow.AddDays(days).AddHours(hours);   // 0일 0시간 = 등록 해제
            if (!accounts.Update(d => { AccountGrouper.SetMembership(d, req.Character!, expires); return true; }))
                return ApiResults.Error(503, "STORAGE_UNAVAILABLE", "멤버십을 저장하지 못했습니다.");
            hub.Broadcast("state.changed", new { keys = new[] { "accounts" } });
            return ApiResults.Ok(new { expiresAt = expires });
        });
        api.MapPut("/accounts/assign", (AssignRequest req, AccountStore accounts, GameViews v, SseHub hub) =>
        {
            if (string.IsNullOrWhiteSpace(req.Character)) return ApiResults.Error(400, "VALIDATION", "캐릭터가 필요합니다.");
            if (!v.IsKnownCharacter(req.Character)) return ApiResults.Error(404, "NOT_FOUND", "기록에 없는 캐릭터입니다.");
            string? target = null;
            if (!accounts.Update(d => { target = AccountGrouper.AssignManually(d, req.Character!, req.Account); return true; }))
                return ApiResults.Error(503, "STORAGE_UNAVAILABLE", "저장하지 못했습니다.");
            hub.Broadcast("state.changed", new { keys = new[] { "accounts" } });
            return ApiResults.Ok(new { account = target });
        });

        // ── 즐겨찾기 (FR-DT-15·21): 가방 아이템 / 채집물 ──
        api.MapGet("/favorites", (FavoritesStore f) =>
        {
            var (items, gather) = f.Snapshot();
            return ApiResults.Ok(new { items, gather });
        });
        api.MapPut("/favorites/{kind}", (string kind, FavoriteRequest req, FavoritesStore f, SseHub hub) =>
        {
            if (!Enum.TryParse<FavoriteKind>(kind, ignoreCase: true, out var k)) return ApiResults.Error(404, "NOT_FOUND", "알 수 없는 즐겨찾기 종류입니다.");
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 100 || req.Favorite == null) return ApiResults.Error(400, "VALIDATION", "이름과 favorite 값이 필요합니다.");
            if (!f.Set(k, name, req.Favorite.Value, out var tooMany))
                return tooMany ? ApiResults.Error(400, "VALIDATION", $"즐겨찾기는 {FavoritesStore.MaxPerKind}개까지입니다.") : ApiResults.Error(503, "STORAGE_UNAVAILABLE", "즐겨찾기를 저장하지 못했습니다.");
            hub.Broadcast("state.changed", new { keys = new[] { "favorites" } });
            return ApiResults.Ok(new { name, favorite = req.Favorite.Value });
        });

        api.MapGet("/cutoffs", (GameViews v, CancellationToken ct) => Respond(v.CutoffsAsync(ct)));

        // 개요 (FR-OV-01): 한 번에 여러 조회를 묶는다. 헤더 외 항목은 실패해도 null로 두고 나머지를 보여 준다.
        api.MapGet("/overview", async (GameViews v, HomeworkWatcher hw, GameStateCache state, CancellationToken ct) =>
        {
            var header = await v.HeaderAsync(ct);
            if (!header.Ok) return ApiResults.CliError(header.Failure!);
            var tCur = v.CurrenciesAsync(ct);
            var tMis = v.MissionsAsync(ct);
            var tLife = v.LifeAsync(ct);
            var tNear = v.NearbyAsync(ct);
            var tHw = hw.ObserveAsync(ct);
            await Task.WhenAll(tCur, tMis, tLife, tNear, tHw);
            var key = tHw.Result ?? GameViews.CharacterKey(state.Character);

            var board = hw.Service.GetBoard(key);
            return ApiResults.Ok(new
            {
                header = header.Value,
                currencies = tCur.Result.Value,
                missions = tMis.Result.Value,
                life = tLife.Result.Value,
                nearby = tNear.Result.Value,
                homework = new { daily = board.Daily, weekly = board.Weekly, nextDailyReset = board.NextDailyResetUtc, nextWeeklyReset = board.NextWeeklyResetUtc },
                cutoffs = state.Character is { } ch ? v.CutoffView(ch) : null,
            }, header.FetchedAt);
        });

        // ── 숙제 (FR-HW) ──
        api.MapGet("/homework", async (string? category, GameViews v, HomeworkWatcher hw, GameStateCache state, CancellationToken ct) =>
        {
            // 헤더를 새로 고쳐 두면 수동 체크(PUT)·초기화가 지금 캐릭터를 쓴다. 판정 키는 관측 묶음의 캐릭터로 정한다.
            if (state.IsHeaderStale)
            {
                var h = await v.HeaderAsync(ct);
                if (!h.Ok && state.Character == null) return ApiResults.CliError(h.Failure!);
            }
            var observed = await hw.ObserveAsync(ct);
            if (observed == null && state.Character == null) return NoCharacter();
            var key = observed ?? GameViews.CharacterKey(state.Character);
            var board = hw.Service.GetBoard(key, string.IsNullOrWhiteSpace(category) ? null : category);
            return ApiResults.Ok(new { characterKey = key, board.Cards, board.Daily, board.Weekly, board.NextDailyResetUtc, board.NextWeeklyResetUtc });
        });

        api.MapPut("/homework/{id}", (string id, HomeworkSetRequest req, HomeworkWatcher hw, GameStateCache state) =>
        {
            if (state.Character == null) return NoCharacter();
            if (req.Completed == null && req.Count == null) return ApiResults.Error(400, "VALIDATION", "completed 또는 count가 필요합니다.");
            try
            {
                var change = hw.Service.Set(GameViews.CharacterKey(state.Character), id, req.Completed, req.Count);
                hw.Notify(change);
                return ApiResults.Ok(change);
            }
            catch (KeyNotFoundException) { return ApiResults.Error(404, "NOT_FOUND", $"알 수 없는 숙제: {id}"); }
            catch (IOException ex) { return ApiResults.Error(503, "STORAGE_UNAVAILABLE", ex.Message); }
        });

        api.MapPost("/homework/reset", (HomeworkResetRequest req, HomeworkWatcher hw, GameStateCache state) =>
        {
            if (state.Character == null) return NoCharacter();
            var includeAccount = req.Scope switch
            {
                "character" => false,
                "characterAndAccount" => true,
                _ => (bool?)null,
            };
            if (includeAccount == null) return ApiResults.Error(400, "VALIDATION", "scope는 character 또는 characterAndAccount입니다.");
            try
            {
                var change = hw.Service.ResetAll(GameViews.CharacterKey(state.Character), includeAccount.Value);
                hw.Notify(change);
                return ApiResults.Ok(change);
            }
            catch (IOException ex) { return ApiResults.Error(503, "STORAGE_UNAVAILABLE", ex.Message); }
        });

        // ── 조작 ──
        api.MapPost("/actions/stop", async (GameActions a, CancellationToken ct) =>
        {
            var r = await a.StopAsync(ct);
            return r.Ok ? ApiResults.Ok(new { stopped = true }) : ApiResults.Error(502, "CLI_FAILED", r.Error ?? "정지 실패");
        });

        api.MapPost("/actions/gather", async (HttpContext ctx, GatherRequest req, GameActions a, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.DisplayName)) return ApiResults.Error(400, "VALIDATION", "displayName이 필요합니다.");
            if (req.Count is < 0 or > 999) return ApiResults.Error(400, "VALIDATION", "count는 0~999입니다.");
            var (result, job) = await a.StartGatherAsync(req.DisplayName.Trim(), req.Count, ClientId.Of(ctx), ct);
            return result switch
            {
                StartGatherResult.Started => ApiResults.Accepted(new { jobId = job!.JobId, item = job.Item, count = job.Count }),
                StartGatherResult.Conflict => ApiResults.Error(409, "CONFLICT", $"이미 '{job!.Item}' 채집이 진행 중입니다."),
                StartGatherResult.CliMissing => ApiResults.Error(503, "CLI_MISSING", "게임 CLI를 찾을 수 없습니다."),
                _ => ApiResults.Error(400, "VALIDATION", $"채집할 수 없는 아이템입니다: {req.DisplayName}"),
            };
        });

        api.MapPost("/actions/collect", async (CollectRequest? req, GameActions a, CancellationToken ct) =>
        {
            var (ok, name, error) = await a.CollectAsync(req?.DisplayName, ct);
            return ok ? ApiResults.Ok(new { collected = name }) : ApiResults.Error(name == null ? 409 : 502, name == null ? "NOTHING_TO_COLLECT" : "CLI_FAILED", error ?? "수거 실패");
        });

        api.MapPut("/profile/nickname", (NicknameRequest req, SnapshotManager snapshots, GameStateCache state, SseHub hub) =>
        {
            var ch = state.Character;
            if (ch == null) return NoCharacter();
            var nick = (req.Nickname ?? "").Trim();
            if (nick.Length > 20) return ApiResults.Error(400, "VALIDATION", "별칭은 20자 이하입니다.");
            snapshots.SetCustomName(ch.RealmName ?? "에린", ch.JobName ?? "밀레시안", nick);
            hub.Broadcast("state.changed", new { keys = new[] { "header" } });
            return ApiResults.Ok(new { nickname = nick.Length == 0 ? null : nick });
        });
    }

    private static IResult NoCharacter() =>
        ApiResults.Error(409, "NO_CHARACTER", "아직 캐릭터 정보를 받지 못했습니다. 게임 연결을 확인하세요.");

    private static async Task<IResult> Respond(Task<CliData<object>> task)
    {
        var r = await task;
        return r.Ok ? ApiResults.Ok(r.Value, r.FetchedAt) : ApiResults.CliError(r.Failure!);
    }
}
