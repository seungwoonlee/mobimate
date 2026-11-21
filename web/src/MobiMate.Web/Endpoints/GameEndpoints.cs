using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Lan;
using MobiMate.Web.Services;

namespace MobiMate.Web.Endpoints;

public sealed record GatherRequest(string? DisplayName, int? Count);
public sealed record GatherQueueRequest(List<GatherRequest>? Items);
public sealed record CollectRequest(string? DisplayName);
/// <summary>CharacterKey가 없으면 지금 접속한 캐릭터. 있으면 알려진 다른 캐릭터의 숙제를 수동으로 고친다(숙제 화면의 캐릭터별 칸).</summary>
public sealed record HomeworkSetRequest(bool? Completed, int? Count, string? CharacterKey = null);
public sealed record HomeworkResetRequest(string? Scope);
public sealed record NicknameRequest(string? Nickname);
public sealed record MergeCharactersRequest(string? From, string? Into);
public sealed record FavoriteRequest(string? Name, bool? Favorite);
public sealed record StockTargetRequest(string? Name, int? Target);
public sealed record StockTimedRequest(string? Name, bool? Timed);
public sealed record MembershipRequest(string? Character, int? Days);
public sealed record AssignRequest(string? Character, string? Account);
public sealed record OrderRequest(List<string>? Order);
public sealed record SplitRequest(string? Key);

/// <summary>게임 조회·조작·숙제 API (요구사양서 §7).</summary>
public static class GameEndpoints
{
    /// <summary>"서버_직업#2" → "서버_직업" (같은 서버·직업인지 가린다)</summary>
    private static string BaseOf(string key) { var i = key.IndexOf('#'); return i < 0 ? key : key[..i]; }

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/status", (StatusMonitor s) => ApiResults.Ok(new { state = StatusMonitor.Wire(s.State), since = s.Since }));

        // 게임 CLI 업데이트 점검 결과(2026-10-22): 마지막 점검 시각·명령 수·마지막으로 바뀐 내용. 점검 전이면 값이 비어 있다.
        api.MapGet("/cli/changes", (CliChangeWatcher w) => ApiResults.Ok(new
        {
            checkedAt = w.Last?.CheckedAt, commands = w.Last?.Commands, firstBaseline = w.Last?.FirstBaseline,
            changedAt = w.Last?.ChangedAt, summary = w.Last?.Summary,
        }));

        api.MapGet("/header", (GameViews v, CancellationToken ct) => Respond(v.HeaderAsync(ct)));
        api.MapGet("/character", (GameViews v, CancellationToken ct) => Respond(v.CharacterAsync(ct)));
        api.MapGet("/inventory", (GameViews v, CancellationToken ct) => Respond(v.InventoryAsync(ct)));
        api.MapGet("/currencies", (GameViews v, CancellationToken ct) => Respond(v.CurrenciesAsync(ct)));
        api.MapGet("/missions", (GameViews v, CancellationToken ct) => Respond(v.MissionsAsync(ct)));
        api.MapGet("/life", (GameViews v, CancellationToken ct) => Respond(v.LifeAsync(ct)));
        api.MapGet("/nearby", (GameViews v, CancellationToken ct) => Respond(v.NearbyAsync(ct)));
        // 직업 이미지: 첫 실행 때 내려받아 둔 전신 일러스트. 없으면 404(화면이 SVG 아이콘으로 대신한다)
        api.MapGet("/class-image/{id}", (string id, ClassImages images, HttpContext ctx) =>
        {
            if (images.Existing(id) is not { } path) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(path, "image/png");
        });
        api.MapGet("/class-images", (ClassImages images) => ApiResults.Ok(images.Status().Select(s => new { id = s.Id, available = s.Available })));

        api.MapGet("/characters", async (GameViews v, CancellationToken ct) => ApiResults.Ok(await v.CharactersAsync(ct)));

        // ── 계정 (v1.5): 멤버십 등록(남은 기간 입력), 캐릭터를 계정에 직접 묶기·풀기 ──
        api.MapPut("/membership", (MembershipRequest req, AccountStore accounts, GameViews v, SseHub hub) =>
        {
            if (string.IsNullOrWhiteSpace(req.Character)) return ApiResults.Error(400, "VALIDATION", "캐릭터가 필요합니다.");
            var days = req.Days ?? 0;
            if (days is < 0 or > 400) return ApiResults.Error(400, "VALIDATION", "남은 일수는 0~400으로 입력해 주세요.");
            if (!v.IsKnownCharacter(req.Character)) return ApiResults.Error(404, "NOT_FOUND", "기록에 없는 캐릭터입니다.");
            // 남은 일수만 받는다: 만료는 (오늘 + 남은 일수) 다음 새벽 6시. 0일 = 등록 해제
            DateTime? expires = days == 0 ? null : MembershipClock.ExpiryLocal(DateTime.Now, days).ToUniversalTime();
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

        // 계정 표시 순서를 사용자가 직접 정한다 (전체 탭 "내 순서")
        api.MapPut("/accounts/order", (OrderRequest req, AccountStore accounts, SseHub hub) =>
        {
            if (req.Order is null || req.Order.Count > 200) return ApiResults.Error(400, "VALIDATION", "순서 목록이 필요합니다.");
            if (!accounts.Update(d => { AccountGrouper.SetOrder(d, req.Order); return true; }))
                return ApiResults.Error(503, "STORAGE_UNAVAILABLE", "순서를 저장하지 못했습니다.");
            hub.Broadcast("state.changed", new { keys = new[] { "accounts" } });
            return ApiResults.Ok(new { order = req.Order });
        });
        // 같은 서버·직업의 다른 캐릭터가 한 기록으로 합쳐졌을 때, 지금 접속한 캐릭터를 새 캐릭터로 떼어 낸다.
        api.MapPost("/characters/split", (SplitRequest req, GameViews v, GameStateCache state, SnapshotManager snapshots, SseHub hub) =>
        {
            if (string.IsNullOrWhiteSpace(req.Key)) return ApiResults.Error(400, "VALIDATION", "캐릭터가 필요합니다.");
            if (!v.IsCurrentCharacter(req.Key)) return ApiResults.Error(409, "CONFLICT", "지금 접속한 캐릭터만 나눌 수 있습니다.");
            var created = snapshots.SplitLatest(req.Key);
            if (created == null) return ApiResults.Error(409, "CONFLICT", "나눌 기록이 부족합니다. 기록이 두 개 이상 쌓인 뒤에 다시 시도해 주세요.");
            state.SetCurrentKey(created);
            hub.Broadcast("state.changed", new { keys = new[] { "accounts", "header" } });
            return ApiResults.Ok(new { key = created });
        });
        // 같은 캐릭터가 둘로 갈라진 기록(예: 레벨이 올라 "아이라_힐러#3"이 새로 생긴 것)을 하나로 합친다. from의 기록·숙제·계정 배정이 into로 옮겨지고 from은 없어진다.
        // 서버·직업이 같은 기록끼리만 합칠 수 있다. 접속 중인 캐릭터가 from이면 합친 뒤 into로 이어 간다.
        api.MapPost("/characters/merge", (MergeCharactersRequest req, GameViews v, GameStateCache state, CharacterMerger merger, HomeworkWatcher hw, SnapshotManager snapshots, AccountStore accounts, SseHub hub) =>
        {
            if (string.IsNullOrWhiteSpace(req.From) || string.IsNullOrWhiteSpace(req.Into)) return ApiResults.Error(400, "VALIDATION", "합칠 캐릭터와 대상이 필요합니다.");
            if (req.From == req.Into) return ApiResults.Error(400, "VALIDATION", "같은 캐릭터끼리는 합칠 수 없습니다.");
            if (!v.IsKnownCharacter(req.From) || !v.IsKnownCharacter(req.Into)) return ApiResults.Error(404, "NOT_FOUND", "기록에 없는 캐릭터입니다.");
            if (BaseOf(req.From) != BaseOf(req.Into)) return ApiResults.Error(409, "CONFLICT", "서버와 직업이 같은 기록끼리만 합칠 수 있습니다.");
            var wasCurrent = v.IsCurrentCharacter(req.From);
            var r = merger.Merge(req.From, req.Into);
            if (!r.Ok) return ApiResults.Error(409, "CONFLICT", r.Message);
            // 파일이 바뀌었으니 각 저장소가 다시 읽는다(10초 폴링을 기다리지 않는다)
            hw.Service.RefreshExternal(); snapshots.RefreshExternal(); accounts.RefreshExternal();
            if (wasCurrent) state.SetCurrentKey(req.Into);
            hub.Broadcast("state.changed", new { keys = new[] { "accounts", "header", "profile" } });
            hub.Broadcast("homework.changed", new { characterKey = req.Into, ids = Array.Empty<string>(), reason = "merge" });
            return ApiResults.Ok(new { from = req.From, into = req.Into, r.Message, r.HistoryRecords, r.HomeworkItems });
        });
        // 필요 없는 캐릭터 기록 삭제. 접속 중인 캐릭터는 곧바로 다시 기록되므로 지울 수 없다.
        api.MapDelete("/characters", (string? key, GameViews v, SseHub hub) =>
        {
            if (string.IsNullOrWhiteSpace(key)) return ApiResults.Error(400, "VALIDATION", "캐릭터가 필요합니다.");
            if (!v.IsKnownCharacter(key)) return ApiResults.Error(404, "NOT_FOUND", "기록에 없는 캐릭터입니다.");
            if (v.IsCurrentCharacter(key)) return ApiResults.Error(409, "CONFLICT", "지금 접속 중인 캐릭터는 지울 수 없습니다.");
            v.RemoveCharacter(key);
            hub.Broadcast("state.changed", new { keys = new[] { "accounts" } });
            return ApiResults.Ok(new { removed = key });
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

        // 계정 전체 보유량 (가방 탭): 캐릭터별로 마지막에 읽은 가방·캐릭터 창고와 계정 창고를 계정 단위로 합산한다
        api.MapGet("/stock", (StockStore s, AccountStore accounts, GameStateCache state, FavoritesStore fav) =>
        {
            var data = accounts.Snapshot();
            var keys = s.Keys();
            var groups = keys.GroupBy(k => AccountGrouper.AccountOf(data, k)).ToList();
            var times = s.ReadTimes(keys);
            var current = state.CurrentKey is { } ck ? AccountGrouper.AccountOf(data, ck) : null;
            return ApiResults.Ok(new
            {
                currentAccount = current,
                currentCharacter = state.CurrentKey,
                accounts = groups.Select(g => new
                {
                    id = g.Key,
                    characters = g.Select(k => new { key = k, atUtc = new DateTimeOffset(times[k], TimeSpan.Zero) }).OrderBy(c => c.atUtc).ToList(),
                    rows = s.Aggregate(g).Select(r => new
                    {
                        name = r.Name, category = r.Category, total = r.Total, bag = r.Bag, storage = r.Storage, account = r.Account,
                        holders = r.Holders.Select(h => new { character = h.Character, bag = h.Bag, storage = h.Storage, atUtc = new DateTimeOffset(h.AtUtc, TimeSpan.Zero) }),
                    }),
                }),
                prefs = s.Prefs(),
                favorites = fav.Snapshot().Items,
            });
        });
        api.MapPut("/stock/target", (StockTargetRequest req, StockStore s, SseHub hub) =>
        {
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 100 || req.Target is null or < 0) return ApiResults.Error(400, "VALIDATION", "이름과 0 이상의 목표 개수가 필요합니다.");
            if (!s.SetTarget(name, req.Target.Value)) return ApiResults.Error(503, "STORAGE_UNAVAILABLE", "목표를 저장하지 못했습니다(너무 많거나 저장소를 쓸 수 없습니다).");
            hub.Broadcast("state.changed", new { keys = new[] { "stock" } });
            return ApiResults.Ok(new { name, target = req.Target.Value });
        });
        api.MapPut("/stock/timed", (StockTimedRequest req, StockStore s, SseHub hub) =>
        {
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 100 || req.Timed == null) return ApiResults.Error(400, "VALIDATION", "이름과 timed 값이 필요합니다.");
            if (!s.SetTimed(name, req.Timed.Value)) return ApiResults.Error(503, "STORAGE_UNAVAILABLE", "표시를 저장하지 못했습니다.");
            hub.Broadcast("state.changed", new { keys = new[] { "stock" } });
            return ApiResults.Ok(new { name, timed = req.Timed.Value });
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
            var key = tHw.Result ?? state.CurrentKey ?? GameViews.CharacterKey(state.Character);

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
            var key = observed ?? state.CurrentKey ?? GameViews.CharacterKey(state.Character);
            var board = hw.Service.GetBoard(key, string.IsNullOrWhiteSpace(category) ? null : category);
            return ApiResults.Ok(new { characterKey = key, board.Cards, board.Daily, board.Weekly, board.NextDailyResetUtc, board.NextWeeklyResetUtc });
        });

        api.MapPut("/homework/{id}", (string id, HomeworkSetRequest req, HomeworkWatcher hw, GameStateCache state, GameViews v) =>
        {
            if (req.Completed == null && req.Count == null) return ApiResults.Error(400, "VALIDATION", "completed 또는 count가 필요합니다.");
            string key;
            if (!string.IsNullOrWhiteSpace(req.CharacterKey))
            {
                if (!v.IsKnownCharacter(req.CharacterKey)) return ApiResults.Error(404, "NOT_FOUND", "알 수 없는 캐릭터입니다.");
                key = req.CharacterKey;
            }
            else
            {
                if (state.Character == null) return NoCharacter();
                key = state.CurrentKey ?? GameViews.CharacterKey(state.Character);
            }
            try
            {
                var change = hw.Service.Set(key, id, req.Completed, req.Count);
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
                var change = hw.Service.ResetAll(state.CurrentKey ?? GameViews.CharacterKey(state.Character), includeAccount.Value);
                hw.Notify(change);
                return ApiResults.Ok(change);
            }
            catch (IOException ex) { return ApiResults.Error(503, "STORAGE_UNAVAILABLE", ex.Message); }
        });

        // 나이트메어 드릴 점수(구역·랭킹)만 모든 캐릭터에서 초기화 (시즌이 바뀔 때). 화면이 확인 대화상자를 먼저 띄운다.
        api.MapPost("/homework/reset-drill", (HomeworkWatcher hw, GameStateCache state) =>
        {
            try
            {
                var (count, change) = hw.Service.ResetDrillAllCharacters(state.CurrentKey ?? (state.Character == null ? "" : GameViews.CharacterKey(state.Character)));
                if (change != null) hw.Notify(change);
                return ApiResults.Ok(new { characters = count });
            }
            catch (IOException ex) { return ApiResults.Error(503, "STORAGE_UNAVAILABLE", ex.Message); }
        });

        // 메모리얼 사가 층 수만 모든 캐릭터에서 초기화 (드릴과 따로)
        api.MapPost("/homework/reset-saga", (HomeworkWatcher hw, GameStateCache state) =>
        {
            try
            {
                var (count, change) = hw.Service.ResetSagaAllCharacters(state.CurrentKey ?? (state.Character == null ? "" : GameViews.CharacterKey(state.Character)));
                if (change != null) hw.Notify(change);
                return ApiResults.Ok(new { characters = count });
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
            if (req.Count is < 0 or > GameActions.MaxTarget) return ApiResults.Error(400, "VALIDATION", $"count는 0~{GameActions.MaxTarget}입니다.");
            var (result, job) = await a.StartGatherAsync(req.DisplayName.Trim(), req.Count, ClientId.Of(ctx), ct);
            return result switch
            {
                StartGatherResult.Started => ApiResults.Accepted(new { jobId = job!.JobId, item = job.Item, count = job.Count, target = job.Target, batches = job.Batches }),
                StartGatherResult.Conflict => ApiResults.Error(409, "CONFLICT", $"이미 '{job!.Item}' 채집이 진행 중입니다."),
                StartGatherResult.CliMissing => ApiResults.Error(503, "CLI_MISSING", "게임 CLI를 찾을 수 없습니다."),
                _ => ApiResults.Error(400, "VALIDATION", $"채집할 수 없는 아이템입니다: {req.DisplayName}"),
            };
        });

        // 채집 큐: 선택한 항목을 순서대로 목표 수량만큼 채운다. 자리를 비워도 서버가 이어서 진행한다.
        api.MapPost("/actions/gather/queue", async (HttpContext ctx, GatherQueueRequest req, GameActions a, CancellationToken ct) =>
        {
            var items = req.Items;
            if (items is not { Count: > 0 and <= 20 }) return ApiResults.Error(400, "VALIDATION", "채집할 항목을 1~20개 보내 주세요.");
            if (items.Any(i => string.IsNullOrWhiteSpace(i.DisplayName) || i.Count is null or <= 0 or > GameActions.MaxTarget))
                return ApiResults.Error(400, "VALIDATION", $"항목마다 displayName과 count(1~{GameActions.MaxTarget})가 필요합니다.");
            var (result, job) = await a.StartQueueAsync(items.Select(i => (i.DisplayName!.Trim(), i.Count!.Value)).ToList(), ClientId.Of(ctx), ct);
            return result switch
            {
                StartGatherResult.Started => ApiResults.Accepted(new { jobId = job!.JobId, item = job.Item, count = job.Count, target = job.Target, batches = job.Batches, queued = job.Queued.Select(x => new { item = x.Item, count = x.Count }) }),
                StartGatherResult.Conflict => ApiResults.Error(409, "CONFLICT", $"이미 '{job!.Item}' 채집이 진행 중입니다."),
                StartGatherResult.CliMissing => ApiResults.Error(503, "CLI_MISSING", "게임 CLI를 찾을 수 없습니다."),
                _ => ApiResults.Error(400, "VALIDATION", "채집할 수 없는 아이템이 있습니다."),
            };
        });

        api.MapGet("/actions/gather/current", (GameActions a) => ApiResults.Ok(new { job = a.Snapshot() }));

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
            snapshots.SetCustomName(state.CurrentKey ?? GameViews.CharacterKey(ch), ch.RealmName ?? "에린", ch.JobName ?? "밀레시안", nick);
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
