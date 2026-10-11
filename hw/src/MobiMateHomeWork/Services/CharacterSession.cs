using MobiMateHomeWork.Hosting;

namespace MobiMateHomeWork.Services;

/// <summary>
/// 캐릭터 식별·기록·현황 (웹앱 GameViews에서 숙제에 필요한 부분만 옮김). CLI 호출은 모두 GameQueries(캐시·단일 비행)를 거친다.
/// 헤더를 읽을 때마다 GameStateCache·SnapshotManager(세션 변화량)를 갱신하고 <see cref="AppEvents.HeaderChanged"/>로 알린다.
/// 캐릭터 선택창·재화 섞임·직업 전환·전투력 재확인은 웹앱과 똑같이 동작한다 — 바꾸면 캐릭터 기록이 섞인다.
/// 랭킹(BadgeOf)·컷오프·생활·인벤토리·주변 플레이어 조회는 옮기지 않았다.
/// </summary>
public sealed class CharacterSession(GameQueries q, GameStateCache state, SnapshotManager snapshots, AppEvents events,
    AccountStore accounts, HomeworkService homework, CombatScoreGuard guard, AppOptions options, AppSettingsStore settings,
    IHostApplicationLifetime life, WeightGuard weights, ILogger<CharacterSession> log)
{
    private string? _loggedKey;   // 마지막으로 "기록" 로그를 남긴 캐릭터 (캐릭터가 바뀔 때만 한 줄)

    public static string CharacterKey(CharacterInfo? ch) =>
        $"{(string.IsNullOrWhiteSpace(ch?.RealmName) ? "에린" : ch!.RealmName)}_{(string.IsNullOrWhiteSpace(ch?.JobName) ? "밀레시안" : ch!.JobName)}";

    /// <summary>지금 접속 중인 캐릭터의 기록 이름(없으면 null).</summary>
    public string? CurrentKey => state.Character is { } c && SnapshotManager.IsRealCharacter(c) ? state.CurrentKey ?? CharacterKey(c) : null;

    /// <summary>
    /// 전투력 재확인: 새로 읽은 전투력이 마지막으로 받아들인 값보다 낮으면 이전 값을 대신 보여 주고
    /// 잠시 뒤 다시 읽는다. 다시 읽어도 계속 낮으면 실제 하락으로 받아들인다.
    /// </summary>
    private CharacterInfo Stabilize(CharacterInfo ch, string key)
    {
        if (!SnapshotManager.IsRealCharacter(ch)) return ch;   // 캐릭터 선택창의 빈 정보는 재확인 대상이 아니다
        ch = KeepTitle(ch, key);
        if (snapshots.IsSubJob(key, ch)) return ch;             // 부직업으로 잠시 바꾼 전투력은 주직업 기록과 비교하지 않는다
        if (ch.CombatScore is not { } score) return ch;
        var saved = snapshots.GetSavedCombat(key);
        var v = guard.Evaluate(key, score.Value, saved);
        if (!v.Suspect) return ch;
        if (guard.TryBeginRecheck(key)) _ = Task.Run(() => RecheckCombatAsync(key, v.Value));
        return ch with { CombatScore = score with { Value = v.Value } };
    }

    /// <summary>칭호가 아직 안 불러와져 비어 있으면 알던 칭호(같은 캐릭터의 직전 읽기 → 저장된 기록)를 유지한다.</summary>
    private CharacterInfo KeepTitle(CharacterInfo ch, string key)
    {
        if (!string.IsNullOrWhiteSpace(ch.Title)) return ch;
        var known = state.CurrentKey == key ? state.Character?.Title : null;
        if (string.IsNullOrWhiteSpace(known))
            known = snapshots.GetProfileByKey(key)?.History.LastOrDefault(h => !string.IsNullOrWhiteSpace(h.Title))?.Title;
        return CharacterIdentity.KeepTitle(ch, known);
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
            await HeaderAsync(stop);   // 받아들인 값으로 화면을 갱신한다 (HeaderChanged)
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

    public async Task<CliData<HeaderView>> HeaderAsync(CancellationToken ct)
    {
        var tCh = q.Get<CharacterInfo>("get_my_info", ct);
        var tAct = q.Get<ActivityInfo>("get_activity", ct);
        var tEnv = q.Get<EnvironmentInfo>("get_current_environment", ct);
        await Task.WhenAll(tCh, tAct, tEnv);
        var (ch, act, env) = (tCh.Result, tAct.Result, tEnv.Result);
        if (!ch.Ok) return new CliData<HeaderView>(null, ch.Failure, ch.FetchedAt);

        // 캐릭터 선택창: 서버·직업이 비고 레벨 0인 정보가 들어온다. 캐릭터로 치지 않고 마지막 실제 캐릭터를 흐리게 보여 준다.
        if (!SnapshotManager.IsRealCharacter(ch.Value))
        {
            if (state.Character is not { } last)
                return new CliData<HeaderView>(null, new CliFailure(CliFailureKind.Failed, "캐릭터를 선택하는 중입니다. 게임에서 캐릭터를 고르면 보입니다."), ch.FetchedAt);
            var emptySnap = new CharacterSnapshot();
            var held = BuildHeader(last, state.CurrentKey ?? CharacterKey(last), state.Environment, new SessionDelta(emptySnap, emptySnap), selecting: true);
            events.RaiseHeaderChanged(held);
            return new CliData<HeaderView>(held, null, ch.FetchedAt);
        }
        // 같은 서버·직업의 캐릭터가 여럿일 수 있다: 이전 읽기와 달라 보이면 재화(데카·M캐시)를 새로 읽어 어느 캐릭터인지 정한다
        var key = await ResolveKeyAsync(ch.Value!, ct);
        if (ch.Value != null) ch = ch with { Value = Stabilize(ch.Value, key) };

        // 캐릭터가 바뀌었으면 캐시된 재화·미션은 이전 캐릭터 것이다: 새 캐릭터의 세션 기준값에 쓰지 않고 재화 캐시를 비운다
        var switched = state.Character != null && (state.CurrentKey ?? CharacterKey(state.Character)) != key;
        if (switched)
        {
            q.Invalidate("get_currencies", "get_daily_missions", "get_current_environment");   // 맵도 이전 캐릭터 것일 수 있다
            state.ClearPerCharacter();
        }
        state.UpdateHeader(ch.Value, act.Value, env.Value);
        state.SetCurrentKey(key);
        if (!switched && IsMixedRead(key))
        {
            // 캐릭터를 바꾼 직후: 재화는 새 캐릭터 것인데 캐릭터 정보는 아직 이전 캐릭터다. 이전 캐릭터 기록을 더럽히지 않고 곧 다시 읽는다.
            var none = new CharacterSnapshot();
            var heldDto = BuildHeader(ch.Value!, key, env.Value, new SessionDelta(none, none));   // 퀘스트는 이전 캐릭터 것일 수 있어 뺀다
            events.RaiseHeaderChanged(heldDto);
            return new CliData<HeaderView>(heldDto, null, ch.FetchedAt);
        }
        var delta = snapshots.UpdateSnapshot(ch.Value, switched ? null : state.Currencies?.ToList(), switched ? null : state.DailyMissions?.ToList(), key);
        if (!string.Equals(_loggedKey, key, StringComparison.Ordinal))
        {
            _loggedKey = key;
            log.LogInformation("캐릭터 기록: {Key} Lv.{Level} 전투력 {Combat}", key, ch.Value!.Level, ch.Value.CombatScore?.Value ?? 0);
        }
        // 현재 맵·가방 최대 무게는 게임 기록에 없다: 숙제 앱 설정 파일에 캐릭터별로 남긴다(바뀔 때만 저장). 캐릭터가 확정된 읽기에서만 남긴다.
        // 전환 직후 읽기는 맵·무게가 이전 캐릭터 것일 수 있어 건너뛴다(다음 읽기에서 저장).
        if (!switched)
        {
            // 비정상 무게 읽기의 최대는 저장하지 않는다 (맵 이동 때 튀는 값)
            var abnormal = weights.Resolve(key, env.Value?.GameSpaceDisplayName, ch.Value!.Vitals, settings.CharacterInfo(key)?.WeightMax).Abnormal;
            settings.RememberCharacter(key, env.Value?.GameSpaceDisplayName, abnormal ? null : ch.Value.Vitals?.WeightMax);
        }
        var dto = BuildHeader(ch.Value!, key, env.Value, delta);
        events.RaiseHeaderChanged(dto);
        return new CliData<HeaderView>(dto, null, ch.FetchedAt);
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

    /// <summary>지금 있는 지역이 레이드·어비스·요일 던전·필드 보스 지역이면 "진행 중" 표시 정보. 아니면 null.</summary>
    internal static InProgressInfo? InProgressOf(HomeworkCatalog catalog, EnvironmentInfo? env)
    {
        var def = catalog.FindByArea(env?.GameSpaceDisplayName);
        if (def == null) return null;
        var kind = def.Category switch { "raid" => "레이드", "abyss" => "어비스", "fieldBoss" => "필드 보스", _ => "요일 던전" };
        return new InProgressInfo(def.Id, kind, def.Title);
    }

    private HeaderView BuildHeader(CharacterInfo ch, string key, EnvironmentInfo? env, SessionDelta delta, bool selecting = false)
    {
        var v = ch.Vitals;
        var saved = settings.CharacterInfo(key);
        var profile = snapshots.GetProfileByKey(key);
        var lastRec = profile?.History.LastOrDefault();
        var map = string.IsNullOrWhiteSpace(env?.GameSpaceDisplayName) ? saved?.LastMap : env!.GameSpaceDisplayName;
        return new HeaderView(
            key, selecting, ch.RealmName, ch.JobName, ch.Level, ch.Title, string.IsNullOrWhiteSpace(profile?.CustomName) ? null : profile!.CustomName,
            ch.CombatScore?.Value ?? 0, delta.CombatScoreDiff, ch.ArcaneResistance?.Value ?? 0, delta.ArcaneResistanceDiff,
            ScoreReading.OrLast(ch.LivingScore?.Value, lastRec?.LivingScore), ScoreReading.OrLast(ch.AttractivenessScore?.Value, lastRec?.AttractivenessScore),
            InProgressOf(homework.Catalog, env), map,
            weights.Resolve(key, map, v, saved?.WeightMax).View);
    }

    /// <summary>재화를 읽어 기록한다. 캐릭터 정보가 따라올 때까지(재화가 다른 캐릭터 것으로 보이면) 기록하지 않는다.</summary>
    public async Task<CliData<List<CurrencyItem>>> CurrenciesAsync(CancellationToken ct)
    {
        var r = await q.Get<List<CurrencyItem>>("get_currencies", ct);
        if (!r.Ok) return r;
        if (state.CurrentKey is { } curKey && snapshots.WalletBelongsToOther(curKey, r.Value))
        {
            // 위 헤더와 같은 경우: 캐릭터 정보가 따라올 때까지 기록하지 않는다
            q.Invalidate("get_my_info", "get_currencies");
            return r;
        }
        state.UpdateCurrencies(r.Value);
        snapshots.UpdateSnapshot(state.Character, r.Value, state.DailyMissions?.ToList(), state.CurrentKey);
        return r;
    }

    /// <summary>기록에 있는 캐릭터인가 (이름 바꾸기·숙제 수동 체크 같은 요청의 대상 확인)</summary>
    public bool IsKnownCharacter(string key) => snapshots.GetAllProfiles().Any(p => p.CharacterKey == key);

    /// <summary>지금 접속 중인 캐릭터인가</summary>
    public bool IsCurrentCharacter(string key) => state.Character is { } c && SnapshotManager.IsRealCharacter(c) && (state.CurrentKey ?? CharacterKey(c)) == key;

    /// <summary>지금 접속 중인 캐릭터를 읽어 기록에 남긴다(헤더 + 재화). 캐릭터 선택창이면 아무것도 남기지 않는다.</summary>
    public async Task RecordNowAsync(CancellationToken ct)
    {
        var h = await HeaderAsync(ct);
        if (h.Ok && SnapshotManager.IsRealCharacter(state.Character)) await CurrenciesAsync(ct);
        events.RaiseCharactersChanged();
    }

    /// <summary>
    /// 내 캐릭터 전체 현황: 캐릭터 기록(마지막으로 본 값)을 계정별로 묶는다. 게임은 지금 접속한 캐릭터만 알려 주므로
    /// 지금 캐릭터는 실시간 값, 나머지는 마지막으로 관찰한 값이다. 같은 계정은 데카·M캐시가 같았던 적이 있는 캐릭터끼리 묶는다.
    /// 은동전·마족 공물은 마지막으로 본 값에서 시간이 지난 만큼 충전된 예상 보유량이다 (CoinForecast).
    /// </summary>
    public CharactersSnapshot Characters()
    {
        var nowUtc = DateTime.UtcNow;
        var currentKey = CurrentKey;
        var profiles = snapshots.GetAllProfiles()
            .Where(p => !(p.RealmName == "에린" && p.JobName == "밀레시안") && p.History.Any(h => h.Level > 0))   // 캐릭터 선택창에서 생긴 옛 가짜 기록은 숨긴다
            .ToList();

        // 같은 계정 묶기: 한 번 묶이면 이후 값이 어긋나도 유지된다
        var histories = profiles.Select(p => new CharacterCurrencyHistory(p.CharacterKey,
            p.History.TakeLast(400).Select(h => (h.Deca, h.MCash)).Distinct().ToList())).ToList();
        accounts.Update(d => AccountGrouper.Reconcile(d, histories));
        var data = accounts.Snapshot();

        var hwStatus = homework.GetAutoStatuses(profiles.Select(p => p.CharacterKey));
        var bossStatus = homework.GetFieldBossStatuses(profiles.Select(p => p.CharacterKey));
        var inProgress = InProgressOf(homework.Catalog, state.Environment);
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
                ScoreReading.OrLast(live?.LivingScore?.Value, last?.LivingScore), ScoreReading.OrLast(live?.AttractivenessScore?.Value, last?.AttractivenessScore),
                Cur(new[] { "데카" }, last?.Deca), Cur(new[] { "M캐시", "M캐쉬" }, last?.MCash),
                Cur(new[] { "은동전" }, last?.SilverCoin), Cur(new[] { "마족 공물", "마족공물" }, last?.DemonTribute), lastSeenUtc,
                live?.Vitals, last != null ? last.WeightCurrent : null,
                live != null ? state.Environment?.GameSpaceDisplayName : null);
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
                var saved = settings.CharacterInfo(c.Key);
                // 가방 무게: 접속 중이면 실시간(최대를 못 읽으면 저장된 값), 아니면 마지막 기록의 현재 무게 + 저장된 최대. 기록이 전혀 없으면 null.
                var liveWeight = c.LiveVitals is { } v ? weights.Resolve(c.Key, c.LiveMap, v, saved?.WeightMax) : null;
                WeightView? weight = liveWeight != null ? liveWeight.View ?? (c.LastWeight is > 0 ? WeightView.Of(c.LastWeight.Value, saved?.WeightMax) : null)
                    : c.LastWeight is > 0 ? WeightView.Of(c.LastWeight.Value, saved?.WeightMax) : null;   // 기록의 0은 "모름"
                var map = c.LiveMap is { Length: > 0 } lm ? lm : saved?.LastMap;
                return new CharacterView(
                    c.Key, VariantOf(c.Key), c.Realm, c.Job, c.Nickname, c.IsCurrent, c.Level, c.Title, map,
                    c.Combat, c.Mdef, c.Living, c.Attract,
                    CoinView.From(silver), CoinView.From(tribute), weight,
                    new DateTimeOffset(c.LastSeenUtc, TimeSpan.Zero), stale,
                    hwStatus.GetValueOrDefault(c.Key) ?? Array.Empty<HomeworkAutoStatus>(),
                    bossStatus.GetValueOrDefault(c.Key) ?? new HomeworkPoolStatus("unknown", null),
                    c.IsCurrent && inProgress != null ? [inProgress.Id] : [],
                    Math.Max(silver.Percent, tribute.Percent));
            }).ToList();
            return new AccountView(
                id, members[0].Nickname ?? members[0].Realm,   // 대표(전투력이 가장 높은) 캐릭터 이름
                id.StartsWith("solo:", StringComparison.Ordinal),
                new MembershipView(info?.MembershipExpiresAtUtc is { } e ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)) : null, member),
                new CoinCaps(CoinForecast.SilverCap(member), CoinForecast.TributeCap(member)),
                members.Any(m => m.IsCurrent),
                data.Order.IndexOf(id),   // 사용자가 정한 순서(없으면 -1)
                members.Max(m => m.Combat),
                members);
        })
        // 지금 접속한 캐릭터의 계정을 맨 위에, 나머지는 계정 안 최고 전투력 순
        .OrderByDescending(g => g.HasCurrent).ThenByDescending(g => g.TopCombat).ToList();

        return new CharactersSnapshot(new DateTimeOffset(nowUtc, TimeSpan.Zero), groups.FirstOrDefault(g => g.HasCurrent)?.Id, groups);
    }

    /// <summary>같은 서버·직업의 몇 번째 캐릭터인가 (기본 1, "…#2"는 2)</summary>
    private static int VariantOf(string key) => key.LastIndexOf('#') is var i and >= 0 && int.TryParse(key[(i + 1)..], out var n) ? n : 1;

    private sealed record CharCard(string Key, string Realm, string Job, string? Nickname, bool IsCurrent, int Level, string Title, long Combat, long Mdef, long Living, long Attract,
        long Deca, long MCash, long Silver, long Tribute, DateTime LastSeenUtc, VitalsInfo? LiveVitals, double? LastWeight, string? LiveMap);
}
