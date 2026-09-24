using System.Collections.Concurrent;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

/// <summary>
/// 화면용 조회 결과를 만든다 (§7 조회 API). CLI 호출은 모두 GameQueries(캐시·단일 비행)를 거친다.
/// 헤더를 읽을 때마다 GameStateCache·SnapshotManager(세션 변화량)를 갱신하고 SSE "header"로 다른 기기에도 알린다.
/// </summary>
public sealed class GameViews(GameQueries q, GameStateCache state, SnapshotManager snapshots, SseHub hub)
{
    private readonly ConcurrentDictionary<string, Dictionary<string, int>> _bagBaselines = new(StringComparer.OrdinalIgnoreCase);

    public static string CharacterKey(CharacterInfo? ch) =>
        $"{(string.IsNullOrWhiteSpace(ch?.RealmName) ? "에린" : ch!.RealmName)}_{(string.IsNullOrWhiteSpace(ch?.JobName) ? "밀레시안" : ch!.JobName)}";

    public static string WeightLevel(double pct) => pct >= 100 ? "danger" : pct >= 95 ? "warn" : "ok";

    public async Task<CliData<object>> HeaderAsync(CancellationToken ct)
    {
        var tCh = q.Get<CharacterInfo>("get_my_info", ct);
        var tAct = q.Get<ActivityInfo>("get_activity", ct);
        var tEnv = q.Get<EnvironmentInfo>("get_current_environment", ct);
        await Task.WhenAll(tCh, tAct, tEnv);
        var (ch, act, env) = (tCh.Result, tAct.Result, tEnv.Result);
        if (!ch.Ok) return new CliData<object>(null, ch.Failure, ch.FetchedAt);

        state.UpdateHeader(ch.Value, act.Value, env.Value);
        var delta = snapshots.UpdateSnapshot(ch.Value, state.Currencies?.ToList(), state.DailyMissions?.ToList());
        var dto = BuildHeader(ch.Value!, act.Value, env.Value, delta);
        hub.Broadcast("header", dto);
        return new CliData<object>(dto, null, ch.FetchedAt);
    }

    private object BuildHeader(CharacterInfo ch, ActivityInfo? act, EnvironmentInfo? env, SessionDelta delta)
    {
        var v = ch.Vitals;
        var pct = v is { WeightMax: > 0 } ? v.WeightCurrent / v.WeightMax * 100 : 0;
        var profile = snapshots.GetProfile(ch.RealmName ?? "에린", ch.JobName ?? "밀레시안");
        return new
        {
            characterKey = CharacterKey(ch),
            character = new
            {
                realm = ch.RealmName, job = ch.JobName, level = ch.Level, title = ch.Title,
                nickname = string.IsNullOrWhiteSpace(profile?.CustomName) ? null : profile!.CustomName,
                combatScore = ch.CombatScore?.Value ?? 0, combatDelta = delta.CombatScoreDiff,
            },
            activity = new { text = GameStateCache.DescribeActivity(act), inCombat = act?.IsInCombat ?? false, canStop = act?.CanStopCurrentAction ?? false },
            location = new
            {
                channel = env?.ChannelName, space = env?.GameSpaceDisplayName, weather = env?.Weather,
                erinn = DisplayFormat.ErinnTime(env?.ErinnNow),
            },
            weight = v == null ? null : new
            {
                current = Math.Round(v.WeightCurrent, 1), max = v.WeightMax, pct = Math.Round(pct, 1),
                level = WeightLevel(pct), delta = Math.Round(delta.WeightDiff, 1),
            },
            session = new { gold = delta.GoldDiff, wings = delta.WingsDiff, nyang = delta.NyangDiff, dailyMissions = delta.MissionDiff },
        };
    }

    public async Task<CliData<object>> CharacterAsync(CancellationToken ct)
    {
        var tCh = q.Get<CharacterInfo>("get_my_info", ct);
        var tAct = q.Get<ActivityInfo>("get_activity", ct);
        await Task.WhenAll(tCh, tAct);
        if (!tCh.Result.Ok) return new CliData<object>(null, tCh.Result.Failure, tCh.Result.FetchedAt);
        return new CliData<object>(new { character = tCh.Result.Value, activity = tAct.Result.Value }, null, tCh.Result.FetchedAt);
    }

    public async Task<CliData<object>> InventoryAsync(CancellationToken ct)
    {
        var tCh = q.Get<CharacterInfo>("get_my_info", ct);
        var tItems = q.Get<List<ItemData>>("get_items", ct);
        await Task.WhenAll(tCh, tItems);
        if (!tItems.Result.Ok) return new CliData<object>(null, tItems.Result.Failure, tItems.Result.FetchedAt);

        var items = tItems.Result.Value ?? new();
        var baseline = _bagBaselines.GetOrAdd(CharacterKey(tCh.Result.Value), _ =>
            items.Where(i => IsBag(i.Location)).GroupBy(i => i.DisplayName).ToDictionary(g => g.Key, g => g.Sum(i => i.Count)));
        var bagTotals = items.Where(i => IsBag(i.Location)).GroupBy(i => i.DisplayName).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));

        var v = tCh.Result.Value?.Vitals;
        var pct = v is { WeightMax: > 0 } ? v.WeightCurrent / v.WeightMax * 100 : 0;
        return new CliData<object>(new
        {
            weight = v == null ? null : new { current = Math.Round(v.WeightCurrent, 1), max = v.WeightMax, pct = Math.Round(pct, 1), level = WeightLevel(pct) },
            items = items.Select(i => new
            {
                location = NormalizeLocation(i.Location), name = i.DisplayName, category = i.CategoryName, count = i.Count, locked = i.IsLocked,
                sessionDelta = IsBag(i.Location) ? Math.Max(0, bagTotals.GetValueOrDefault(i.DisplayName) - baseline.GetValueOrDefault(i.DisplayName)) : 0,
            }),
        }, null, tItems.Result.FetchedAt);
    }

    public async Task<CliData<object>> CurrenciesAsync(CancellationToken ct)
    {
        var r = await q.Get<List<CurrencyItem>>("get_currencies", ct);
        if (!r.Ok) return new CliData<object>(null, r.Failure, r.FetchedAt);
        state.UpdateCurrencies(r.Value);
        var delta = snapshots.UpdateSnapshot(state.Character, r.Value, state.DailyMissions?.ToList());
        return new CliData<object>(new
        {
            items = r.Value,
            session = new { gold = delta.GoldDiff, wings = delta.WingsDiff, nyang = delta.NyangDiff },
        }, null, r.FetchedAt);
    }

    public async Task<CliData<object>> MissionsAsync(CancellationToken ct)
    {
        var tD = q.Get<List<MissionItem>>("get_daily_missions", ct);
        var tW = q.Get<List<MissionItem>>("get_weekly_missions", ct);
        await Task.WhenAll(tD, tW);
        if (!tD.Result.Ok) return new CliData<object>(null, tD.Result.Failure, tD.Result.FetchedAt);
        state.UpdateDailyMissions(tD.Result.Value);
        return new CliData<object>(new { daily = Progress(tD.Result.Value), weekly = Progress(tW.Result.Value) }, null, tD.Result.FetchedAt);

        static object Progress(List<MissionItem>? list) => new
        {
            done = list?.Count(m => m.IsCompleted || (m.GoalCount > 0 && m.CurrentCount >= m.GoalCount)) ?? 0,
            total = list?.Count ?? 0,
            items = list ?? new(),
        };
    }

    public async Task<CliData<object>> LifeAsync(CancellationToken ct)
    {
        var tA = q.Get<AlteringWorksResponse>("get_altering_works", ct);
        var tG = q.Get<GatherableResponse>("get_gatherable_items", ct);
        var tI = q.Get<List<ItemData>>("get_items", ct);
        await Task.WhenAll(tA, tG, tI);
        if (!tA.Result.Ok && !tG.Result.Ok) return new CliData<object>(null, tA.Result.Failure, tA.Result.FetchedAt);

        state.UpdateGatherables(tG.Result.Value?.Items);
        var bag = (tI.Result.Value ?? new()).Where(i => IsBag(i.Location)).GroupBy(i => i.DisplayName).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
        return new CliData<object>(new
        {
            works = tA.Result.Value?.Works?.Select(w => new
            {
                name = w.DisplayName, facility = w.FacilityName, remainingSeconds = w.RemainingSeconds,
                done = w.IsCompleted || w.RemainingSeconds <= 0, remainingText = DisplayFormat.RemainingTime(w.RemainingSeconds),
            }),
            gatherables = tG.Result.Value?.Items?.Select(g => new { name = g.DisplayName, toolOk = g.ToolOk, inBag = bag.GetValueOrDefault(g.DisplayName) }),
        }, null, tA.Result.FetchedAt);
    }

    public async Task<CliData<object>> NearbyAsync(CancellationToken ct)
    {
        var r = await q.Get<List<NearPcItem>>("get_near_pcs", ct);
        if (!r.Ok) return new CliData<object>(null, r.Failure, r.FetchedAt);
        var my = state.Character?.CombatScore?.Value ?? 0;
        var ranked = NearbyRanker.Rank(r.Value, my);
        return new CliData<object>(new
        {
            count = ranked.Count,
            myCombatScore = my,
            players = ranked.Select(e => new
            {
                name = e.Pc.Title, realm = e.Pc.RealmName, job = e.Pc.JobName, level = e.Pc.Level, combatScore = e.Pc.CombatScore,
                distance = Math.Round(e.Pc.Distance, 1), inCombat = e.Pc.IsInCombat, relation = e.Relation, relationLabel = e.RelationLabel,
                isStronger = e.IsStronger,
            }),
        }, null, r.FetchedAt);
    }

    public static bool IsBag(string? loc) => loc != null && (loc.Equals("Bag", StringComparison.OrdinalIgnoreCase) || loc.Equals("inventory", StringComparison.OrdinalIgnoreCase));

    public static string NormalizeLocation(string? loc) => loc?.ToLowerInvariant() switch
    {
        "bag" or "inventory" => "bag",
        "accountstorage" or "account_storage" => "account",
        "characterstorage" or "character_storage" => "character",
        _ => loc ?? "",
    };
}
