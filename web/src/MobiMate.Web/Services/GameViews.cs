using System.Collections.Concurrent;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

/// <summary>
/// 화면용 조회 결과를 만든다 (§7 조회 API). CLI 호출은 모두 GameQueries(캐시·단일 비행)를 거친다.
/// 헤더를 읽을 때마다 GameStateCache·SnapshotManager(세션 변화량)를 갱신하고 SSE "header"로 다른 기기에도 알린다.
/// </summary>
public sealed class GameViews(GameQueries q, GameStateCache state, SnapshotManager snapshots, SseHub hub, CutoffCatalog cutoffs,
    FavoritesStore favorites, AccountStore accounts, WorkCategoryCatalog workKinds, CombatScoreGuard guard, MobiMateOptions options, IHostApplicationLifetime life, ILogger<GameViews> log)
{
    private readonly ConcurrentDictionary<string, Dictionary<string, int>> _bagBaselines = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _unclassifiedWorks = new();   // 분류표에 없는 가공품은 한 번만 기록한다 (FR-DT-20)

    public static string CharacterKey(CharacterInfo? ch) =>
        $"{(string.IsNullOrWhiteSpace(ch?.RealmName) ? "에린" : ch!.RealmName)}_{(string.IsNullOrWhiteSpace(ch?.JobName) ? "밀레시안" : ch!.JobName)}";

    /// <summary>
    /// 어비스·레이드 컷오프 추천 (FR-CO). 헤더가 오래됐으면 새로 읽고, 실패하면 마지막 값으로 계산한다(FR-CO-06).
    /// 캐릭터를 한 번도 읽지 못했으면 null.
    /// </summary>
    public async Task<CliData<object>> CutoffsAsync(CancellationToken ct)
    {
        CliFailure? failure = null;
        if (state.IsHeaderStale)
        {
            var h = await HeaderAsync(ct);
            if (!h.Ok) failure = h.Failure;
        }
        var ch = state.Character;
        if (ch == null) return new CliData<object>(null, failure ?? new CliFailure(CliFailureKind.Failed, "캐릭터 정보가 없습니다."), DateTimeOffset.UtcNow);
        // 신선도는 마지막으로 헤더를 받은 시각이다. 이번 갱신이 실패했으면 stale로 알린다 (FR-CO-06, FR-CN-03).
        return new CliData<object>(CutoffView(ch, stale: failure != null), null, state.HeaderFetchedAt ?? DateTimeOffset.UtcNow);
    }

    public object CutoffView(CharacterInfo ch, bool stale = false)
    {
        long combat = ch.CombatScore?.Value ?? 0, mdef = ch.ArcaneResistance?.Value ?? 0;
        return new
        {
            combat, mdef, stale,
            contents = CutoffEvaluator.EvaluateAll(cutoffs, combat, mdef).Select(r => new
            {
                r.Id, r.Name, r.Icon, r.MaxEntryTier, r.RecommendedTier,
                status = r.Status switch { CutoffStatus.Overwhelm => "overwhelm", CutoffStatus.Near => "near", CutoffStatus.Marginal => "marginal", _ => "locked" },
                r.OverwhelmPct, r.CombatToOverwhelm, r.MdefShort,
                // FR-CO-08: 최고 난이도면 next = null (화면은 "최고 난이도 도전 가능"), 입장 불가면 entryShort
                next = r.Next is { Top: false } n ? new { tier = n.Tier, combatShort = n.CombatShort, mdefShort = n.MdefShort, readyNow = n.ReadyNow } : null,
                entryShort = r.EntryShort == null ? null : new { tier = r.EntryTier, combatShort = r.EntryShort.Combat, mdefShort = r.EntryShort.Mdef },
            }),
        };
    }

    public static string WeightLevel(double pct) => pct >= 100 ? "danger" : pct >= 95 ? "warn" : "ok";

    /// <summary>
    /// 전투력 재확인 (FR-DT-10): 새로 읽은 전투력이 마지막으로 받아들인 값보다 낮으면 이전 값을 대신 보여 주고
    /// 잠시 뒤 다시 읽는다. 다시 읽어도 계속 낮으면 실제 하락으로 받아들인다.
    /// </summary>
    private CharacterInfo Stabilize(CharacterInfo ch, string key)
    {
        if (!SnapshotManager.IsRealCharacter(ch)) return ch;   // 캐릭터 선택창의 빈 정보는 재확인 대상이 아니다
        if (snapshots.IsSubJob(key, ch)) return ch;             // 부직업으로 잠시 바꾼 전투력은 주직업 기록과 비교하지 않는다
        if (ch.CombatScore is not { } score) return ch;
        var saved = snapshots.GetSavedCombat(key);
        var v = guard.Evaluate(key, score.Value, saved);
        if (!v.Suspect) return ch;
        if (guard.TryBeginRecheck(key)) _ = Task.Run(() => RecheckCombatAsync(key, v.Value));
        return ch with { CombatScore = score with { Value = v.Value } };
    }

    private async Task RecheckCombatAsync(string key, long known)
    {
        var stop = life.ApplicationStopping;   // 앱을 끄면 재확인도 멈춘다
        try
        {
            long best = 0;
            for (var i = 0; i < 2; i++)
            {
                await Task.Delay(options.CombatRecheckDelay, stop);
                q.Invalidate("get_my_info");
                var r = await q.Get<CharacterInfo>("get_my_info", stop);
                if (!r.Ok || r.Value == null || snapshots.ResolveKey(r.Value, state.Currencies) != key) continue;   // 읽지 못했거나 캐릭터가 바뀜
                var now = r.Value.CombatScore?.Value ?? 0;
                if (now >= known) { best = now; break; }
                best = Math.Max(best, now);
            }
            if (best > 0 && best < known) { guard.ConfirmLow(key, best); log.LogInformation("전투력 {Known} → {Now}: 다시 읽어도 같아 실제 값으로 받아들입니다 ({Key})", known, best, key); }
            await HeaderAsync(stop);   // 받아들인 값으로 화면을 갱신한다 (SSE header)
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            log.LogWarning(ex, "전투력 재확인 실패");
        }
        finally
        {
            guard.EndRecheck(key);
        }
    }

    public async Task<CliData<object>> HeaderAsync(CancellationToken ct)
    {
        var tCh = q.Get<CharacterInfo>("get_my_info", ct);
        var tAct = q.Get<ActivityInfo>("get_activity", ct);
        var tEnv = q.Get<EnvironmentInfo>("get_current_environment", ct);
        await Task.WhenAll(tCh, tAct, tEnv);
        var (ch, act, env) = (tCh.Result, tAct.Result, tEnv.Result);
        if (!ch.Ok) return new CliData<object>(null, ch.Failure, ch.FetchedAt);

        // 캐릭터 선택창: 서버·직업이 비고 레벨 0인 정보가 들어온다. 캐릭터로 치지 않고 마지막 실제 캐릭터를 흐리게 보여 준다.
        if (!SnapshotManager.IsRealCharacter(ch.Value))
        {
            if (state.Character is not { } last)
                return new CliData<object>(null, new CliFailure(CliFailureKind.Failed, "캐릭터를 선택하는 중입니다. 게임에서 캐릭터를 고르면 보입니다."), ch.FetchedAt);
            var emptySnap = new CharacterSnapshot();
            var held = BuildHeader(last, state.CurrentKey ?? CharacterKey(last), state.Activity, state.Environment, new SessionDelta(emptySnap, emptySnap), selecting: true);
            hub.Broadcast("header", held);
            return new CliData<object>(held, null, ch.FetchedAt);
        }
        // 같은 서버·직업의 캐릭터가 여럿일 수 있다: 이전 읽기와 달라 보이면 재화(데카·M캐시)를 새로 읽어 어느 캐릭터인지 정한다
        var key = await ResolveKeyAsync(ch.Value!, ct);
        if (ch.Value != null) ch = ch with { Value = Stabilize(ch.Value, key) };

        // 캐릭터가 바뀌었으면 캐시된 재화·미션은 이전 캐릭터 것이다: 새 캐릭터의 세션 기준값에 쓰지 않고 재화 캐시를 비운다
        var switched = state.Character != null && (state.CurrentKey ?? CharacterKey(state.Character)) != key;
        if (switched)
        {
            q.Invalidate("get_currencies", "get_daily_missions");
            state.ClearPerCharacter();
        }
        state.UpdateHeader(ch.Value, act.Value, env.Value);
        state.SetCurrentKey(key);
        if (!switched && IsMixedRead(key))
        {
            // 캐릭터를 바꾼 직후: 재화는 새 캐릭터 것인데 캐릭터 정보는 아직 이전 캐릭터다. 이전 캐릭터 기록을 더럽히지 않고 곧 다시 읽는다.
            var none = new CharacterSnapshot();
            var heldDto = BuildHeader(ch.Value!, key, act.Value, env.Value, new SessionDelta(none, none));
            hub.Broadcast("header", heldDto);
            return new CliData<object>(heldDto, null, ch.FetchedAt);
        }
        var delta = snapshots.UpdateSnapshot(ch.Value, switched ? null : state.Currencies?.ToList(), switched ? null : state.DailyMissions?.ToList(), key);
        var dto = BuildHeader(ch.Value!, key, act.Value, env.Value, delta);
        hub.Broadcast("header", dto);
        return new CliData<object>(dto, null, ch.FetchedAt);
    }

    /// <summary>캐시된 재화가 지금 캐릭터(key)가 아니라 다른 캐릭터의 것으로 보이면 true. 캐릭터 정보·재화 캐시를 비워 다시 읽게 한다.</summary>
    private bool IsMixedRead(string key)
    {
        if (state.Currencies == null || !snapshots.WalletBelongsToOther(key, state.Currencies)) return false;
        q.Invalidate("get_my_info", "get_currencies");
        state.ClearPerCharacter();
        return true;
    }

    /// <summary>지금 읽은 캐릭터의 기록 이름. 직전 읽기와 같아 보이면 그대로, 아니면 재화를 새로 읽어 정한다 (CharacterIdentity).</summary>
    private async Task<string> ResolveKeyAsync(CharacterInfo ch, CancellationToken ct)
    {
        if (state.CurrentKey is { } known && CharacterIdentity.SameReading(state.Character, ch)) return known;
        var before = state.Currencies;
        q.Invalidate("get_currencies");
        var cr = await q.Get<List<CurrencyItem>>("get_currencies", ct);
        // 주↔부직업 전환은 골드·데카·M캐시가 그대로이므로 다른 캐릭터로 접속한 것이 아니다
        return snapshots.DetectJobSwap(state.CurrentKey, ch, cr.Ok ? cr.Value : null, before)
            ?? snapshots.ResolveKey(ch, cr.Ok ? cr.Value : null);
    }

    private object BuildHeader(CharacterInfo ch, string key, ActivityInfo? act, EnvironmentInfo? env, SessionDelta delta, bool selecting = false)
    {
        var v = ch.Vitals;
        var pct = v is { WeightMax: > 0 } ? v.WeightCurrent / v.WeightMax * 100 : 0;
        var profile = snapshots.GetProfileByKey(key);
        return new
        {
            characterKey = key,
            selecting,   // 캐릭터 선택창: 아래 값은 마지막으로 본 캐릭터의 것이다
            character = new
            {
                realm = ch.RealmName, job = ch.JobName, level = ch.Level, title = ch.Title,
                nickname = string.IsNullOrWhiteSpace(profile?.CustomName) ? null : profile!.CustomName,
                combatScore = ch.CombatScore?.Value ?? 0, combatDelta = delta.CombatScoreDiff,
            },
            // 4대 점수 (WPF판 v1.2.0): 전투력·마도저항은 세션 변화량 포함
            scores = new
            {
                combat = ch.CombatScore?.Value ?? 0, combatDelta = delta.CombatScoreDiff,
                mdef = ch.ArcaneResistance?.Value ?? 0, mdefDelta = delta.ArcaneResistanceDiff,
                living = ch.LivingScore?.Value ?? 0, attract = ch.AttractivenessScore?.Value ?? 0,
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
        var character = tCh.Result.Value == null ? null : Stabilize(tCh.Result.Value, state.CurrentKey ?? CharacterKey(tCh.Result.Value));
        return new CliData<object>(new { character, activity = tAct.Result.Value }, null, tCh.Result.FetchedAt);
    }

    public async Task<CliData<object>> InventoryAsync(CancellationToken ct)
    {
        var tCh = q.Get<CharacterInfo>("get_my_info", ct);
        var tItems = q.Get<List<ItemData>>("get_items", ct);
        await Task.WhenAll(tCh, tItems);
        if (!tItems.Result.Ok) return new CliData<object>(null, tItems.Result.Failure, tItems.Result.FetchedAt);

        var items = tItems.Result.Value ?? new();
        var baseline = _bagBaselines.GetOrAdd(state.CurrentKey ?? CharacterKey(tCh.Result.Value), _ =>
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
                favorite = favorites.IsItem(i.DisplayName),
                sessionDelta = IsBag(i.Location) ? Math.Max(0, bagTotals.GetValueOrDefault(i.DisplayName) - baseline.GetValueOrDefault(i.DisplayName)) : 0,
            }),
        }, null, tItems.Result.FetchedAt);
    }

    public async Task<CliData<object>> CurrenciesAsync(CancellationToken ct)
    {
        var r = await q.Get<List<CurrencyItem>>("get_currencies", ct);
        if (!r.Ok) return new CliData<object>(null, r.Failure, r.FetchedAt);
        if (state.CurrentKey is { } curKey && snapshots.WalletBelongsToOther(curKey, r.Value))
        {
            // 위 헤더와 같은 경우: 캐릭터 정보가 따라올 때까지 기록하지 않는다
            q.Invalidate("get_my_info", "get_currencies");
            return new CliData<object>(new { items = r.Value, session = new { gold = 0L, wings = 0L, nyang = 0L } }, null, r.FetchedAt);
        }
        state.UpdateCurrencies(r.Value);
        var delta = snapshots.UpdateSnapshot(state.Character, r.Value, state.DailyMissions?.ToList(), state.CurrentKey);
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
            works = tA.Result.Value?.Works?.Select(w =>
            {
                var kind = workKinds.Classify(w.DisplayName, w.FacilityName);
                if (kind == WorkKinds.Other && w.DisplayName != null && _unclassifiedWorks.TryAdd(w.DisplayName, 0))
                    log.LogInformation("가공 분류표에 없는 가공품: {Name} (작업대 {Facility}) — work_categories.override.json에 더하면 분류됩니다", w.DisplayName, w.FacilityName);
                return new
                {
                    name = w.DisplayName, facility = w.FacilityName, remainingSeconds = w.RemainingSeconds,
                    done = w.IsCompleted || w.RemainingSeconds <= 0, remainingText = DisplayFormat.RemainingTime(w.RemainingSeconds),
                    kind, kindLabel = workKinds.LabelOf(kind),
                };
            }),
            gatherables = tG.Result.Value?.Items?.Select(g => new { name = g.DisplayName, toolOk = g.ToolOk, inBag = bag.GetValueOrDefault(g.DisplayName), favorite = favorites.IsGather(g.DisplayName) }),
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
                title = e.Pc.Title, realm = e.Pc.RealmName, job = e.Pc.JobName, level = e.Pc.Level, combatScore = e.Pc.CombatScore,
                distance = Math.Round(e.Pc.Distance, 1), inCombat = e.Pc.IsInCombat, relation = e.Relation, relationLabel = e.RelationLabel,
                isStronger = e.IsStronger,
            }),
        }, null, r.FetchedAt);
    }

    /// <summary>기록에 있는 캐릭터인가 (멤버십·계정 지정 같은 요청의 대상 확인)</summary>
    public bool IsKnownCharacter(string key) => snapshots.GetAllProfiles().Any(p => p.CharacterKey == key);

    /// <summary>지금 접속 중인 캐릭터인가 (접속 중인 캐릭터는 지울 수 없다: 곧바로 다시 기록된다)</summary>
    public bool IsCurrentCharacter(string key) => state.Character is { } c && SnapshotManager.IsRealCharacter(c) && (state.CurrentKey ?? CharacterKey(c)) == key;

    /// <summary>캐릭터 기록과 계정 정보를 지운다. 기록에 없으면 false.</summary>
    public bool RemoveCharacter(string key)
    {
        if (!snapshots.RemoveCharacter(key)) return false;
        accounts.Update(d => { AccountGrouper.RemoveCharacter(d, key); return true; });
        return true;
    }

    /// <summary>지금 접속 중인 캐릭터를 읽어 기록에 남긴다(헤더 + 재화). 캐릭터 선택창이면 아무것도 남기지 않는다.</summary>
    public async Task RecordNowAsync(CancellationToken ct)
    {
        var h = await HeaderAsync(ct);
        if (h.Ok && SnapshotManager.IsRealCharacter(state.Character)) await CurrenciesAsync(ct);
    }

    /// <summary>
    /// 내 캐릭터 전체 현황 (FR-AL): 화면이 열려 있는 동안 지금 캐릭터의 값을 새로 읽어 기록한 뒤 현황을 만든다.
    /// </summary>
    public async Task<object> CharactersAsync(CancellationToken ct)
    {
        try { await RecordNowAsync(ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { log.LogDebug(ex, "전체 현황 갱신 중 읽기 실패 (기록된 값으로 보여 줍니다)"); }
        return Characters();
    }

    /// <summary>
    /// 내 캐릭터 전체 현황: 캐릭터 기록(마지막으로 본 값)을 계정별로 묶는다. 게임은 지금 접속한 캐릭터만 알려 주므로
    /// 지금 캐릭터는 실시간 값, 나머지는 마지막으로 관찰한 값이다. 같은 계정은 데카·M캐시가 같았던 적이 있는 캐릭터끼리 묶는다.
    /// 은동전·마족 공물은 마지막으로 본 값에서 시간이 지난 만큼 충전된 예상 보유량이다 (CoinForecast).
    /// </summary>
    public object Characters()
    {
        var nowUtc = DateTime.UtcNow;
        var currentKey = state.Character is { } cur && SnapshotManager.IsRealCharacter(cur) ? state.CurrentKey ?? CharacterKey(cur) : null;
        var profiles = snapshots.GetAllProfiles()
            .Where(p => !(p.RealmName == "에린" && p.JobName == "밀레시안") && p.History.Any(h => h.Level > 0))   // 캐릭터 선택창에서 생긴 옛 가짜 기록은 숨긴다
            .ToList();

        // 같은 계정 묶기: 한 번 묶이면 이후 값이 어긋나도 유지된다
        var histories = profiles.Select(p => new CharacterCurrencyHistory(p.CharacterKey,
            p.History.TakeLast(400).Select(h => (h.Deca, h.MCash)).Distinct().ToList())).ToList();
        accounts.Update(d => AccountGrouper.Reconcile(d, histories));
        var data = accounts.Snapshot();

        long Held(Func<CurrencyItem, bool> pick, long? last) => state.Currencies?.FirstOrDefault(pick)?.Amount ?? last ?? 0;
        var cards = profiles.Select(p =>
        {
            var last = p.History.LastOrDefault();
            var live = p.CharacterKey == currentKey ? state.Character : null;
            var curr = live != null ? state.Currencies : null;
            long Cur(string[] names, long? prev) => curr?.FirstOrDefault(c => names.Contains(c.DisplayName))?.Amount ?? prev ?? 0;
            var lastSeenUtc = live != null ? nowUtc : p.LastSeen.ToUniversalTime();
            return new CharCard(
                p.CharacterKey, p.RealmName, p.JobName, string.IsNullOrWhiteSpace(p.CustomName) ? null : p.CustomName, live != null,
                live?.Level ?? last?.Level ?? 0, live?.Title ?? last?.Title ?? "",
                live?.CombatScore?.Value ?? last?.CombatScore ?? 0, live?.ArcaneResistance?.Value ?? last?.ArcaneResistance ?? 0,
                live?.LivingScore?.Value ?? last?.LivingScore ?? 0, live?.AttractivenessScore?.Value ?? last?.AttractivenessScore ?? 0,
                Cur(new[] { "골드" }, last?.Gold), Cur(new[] { "데카" }, last?.Deca), Cur(new[] { "M캐시", "M캐쉬" }, last?.MCash),
                Cur(new[] { "은동전" }, last?.SilverCoin), Cur(new[] { "마족 공물", "마족공물" }, last?.DemonTribute), lastSeenUtc);
        }).ToList();

        var groups = cards.GroupBy(c => AccountGrouper.AccountOf(data, c.Key)).Select(g =>
        {
            var id = g.Key;
            var member = AccountGrouper.IsMember(data, id, nowUtc);
            var reference = g.OrderByDescending(c => c.LastSeenUtc).First();   // 가장 최근에 본 캐릭터의 값이 계정의 최신 값이다
            data.Accounts.TryGetValue(id, out var info);
            var members = g.OrderByDescending(c => c.Combat).Select(c =>
            {
                var silver = CoinForecast.Silver(c.Silver, c.LastSeenUtc, nowUtc, member);
                var tribute = CoinForecast.Tribute(c.Tribute, c.LastSeenUtc, nowUtc, member);
                var stale = g.Count() > 1 && c.Key != reference.Key && (c.Deca != reference.Deca || c.MCash != reference.MCash);
                return new
                {
                    key = c.Key, variant = VariantOf(c.Key), realm = c.Realm, job = c.Job, nickname = c.Nickname, isCurrent = c.IsCurrent, level = c.Level, title = c.Title,
                    combat = c.Combat, mdef = c.Mdef, living = c.Living, attract = c.Attract, gold = c.Gold, deca = c.Deca, mcash = c.MCash,
                    lastSeen = new DateTimeOffset(c.LastSeenUtc, TimeSpan.Zero),
                    silver = CoinView(silver), tribute = CoinView(tribute),
                    stale,   // 데카·M캐시가 계정의 최신 값과 다르다: 그 캐릭터는 마지막 접속 이후 동기화되지 않았다
                    urgency = Math.Max(silver.Percent, tribute.Percent),
                };
            }).ToList();
            return new
            {
                id,
                name = members[0].nickname ?? members[0].realm,   // 대표(전투력이 가장 높은) 캐릭터 이름
                solo = id.StartsWith("solo:", StringComparison.Ordinal),
                deca = reference.Deca, mcash = reference.MCash,
                membership = new { expiresAt = info?.MembershipExpiresAtUtc is { } e ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)) : (DateTimeOffset?)null, active = member },
                caps = new { silver = CoinForecast.SilverCap(member), tribute = CoinForecast.TributeCap(member) },
                hasCurrent = members.Any(m => m.isCurrent),
                manualRank = data.Order.IndexOf(id),   // 사용자가 정한 순서(없으면 -1)
                topCombat = members.Max(m => m.combat),
                members,
            };
        })
        // 지금 접속한 캐릭터의 계정을 맨 위에, 나머지는 계정 안 최고 전투력 순
        .OrderByDescending(g => g.hasCurrent).ThenByDescending(g => g.topCombat).ToList();

        return new { nowUtc = new DateTimeOffset(nowUtc, TimeSpan.Zero), currentAccountId = groups.FirstOrDefault(g => g.hasCurrent)?.id, accounts = groups };
    }

    /// <summary>같은 서버·직업의 몇 번째 캐릭터인가 (기본 1, "…#2"는 2)</summary>
    private static int VariantOf(string key) => key.LastIndexOf('#') is var i and >= 0 && int.TryParse(key[(i + 1)..], out var n) ? n : 1;

    private sealed record CharCard(string Key, string Realm, string Job, string? Nickname, bool IsCurrent, int Level, string Title, long Combat, long Mdef, long Living, long Attract,
        long Gold, long Deca, long MCash, long Silver, long Tribute, DateTime LastSeenUtc);

    private static object CoinView(CoinState c) => new
    {
        held = c.Held, expected = c.Expected, cap = c.Cap, percent = Math.Round(c.Percent, 4),
        level = c.Level switch { CoinLevel.Full => "full", CoinLevel.Near => "near", _ => "ok" },
        minutesToFull = Math.Round(c.MinutesToFull),
    };

    public static bool IsBag(string? loc) => loc != null && (loc.Equals("Bag", StringComparison.OrdinalIgnoreCase) || loc.Equals("inventory", StringComparison.OrdinalIgnoreCase));

    public static string NormalizeLocation(string? loc) => loc?.ToLowerInvariant() switch
    {
        "bag" or "inventory" => "bag",
        "accountstorage" or "account_storage" => "account",
        "characterstorage" or "character_storage" => "character",
        _ => loc ?? "",
    };
}
