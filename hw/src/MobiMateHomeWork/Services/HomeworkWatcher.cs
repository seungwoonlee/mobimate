namespace MobiMateHomeWork.Services;

/// <summary>
/// 숙제 판정 연결부: 조회 결과를 모아 HomeworkService에 넘기고, 바뀌면 <see cref="AppEvents.HomeworkChanged"/>로 알린다.
/// 조회에 실패한 항목·이번에 읽지 않은 항목(빠른 관찰)은 null로 넘겨 판정 근거에서 뺀다.
/// 웹앱과 달리 재화 변화 기록(CurrencyDeltaLog, currency_changes.jsonl)은 남기지 않는다 — 웹앱 전용 로그다.
/// </summary>
public sealed class HomeworkWatcher(HomeworkService homework, GameQueries q, AppEvents events, SnapshotManager snapshots, GameStateCache state, ILogger<HomeworkWatcher> log)
{
    private string? _loggedKey;   // 마지막으로 "관찰 시작" 로그를 남긴 캐릭터

    /// <summary>캐릭터의 기록 이름. 헤더가 이미 정한 이름이 있으면 그것을 쓰고, 아니면 재화로 같은 서버·직업의 다른 캐릭터와 구분해 정한다.</summary>
    public string KeyOf(CharacterInfo ch, IEnumerable<CurrencyItem>? currencies = null) =>
        state.CurrentKey is { } k && CharacterIdentity.SameReading(state.Character, ch) ? k
            : snapshots.DetectJobSwap(state.CurrentKey, ch, currencies ?? state.Currencies, state.Currencies) ?? snapshots.ResolveKey(ch, currencies ?? state.Currencies);

    public HomeworkService Service => homework;

    /// <summary>
    /// 미션·퀘스트와 같은 묶음에서 get_my_info를 읽어 그 결과로 캐릭터 키를 정한다.
    /// 캐시된 헤더의 캐릭터를 쓰면 캐릭터를 바꾼 직후 새 캐릭터의 미션이 이전 캐릭터에 기록될 수 있다.
    /// 캐릭터를 확인하지 못하면 판정하지 않고 null을 돌려준다.
    /// </summary>
    /// <param name="full">true면 9개 전부, false면 빠른 관찰(퀘스트 + 캐릭터 확인 + 현재 맵, 3개).
    /// 읽지 않은 항목은 null로 넘긴다 — 조회 실패와 같은 "근거 없음"이라 판정이 그 항목을 건너뛴다(판정기는 null을 "사라짐"으로 보지 않는다).</param>
    public async Task<string?> ObserveAsync(CancellationToken ct, bool full = true)
    {
        var tMe = q.Get<CharacterInfo>("get_my_info", ct);
        var tQ = q.Get<List<QuestItem>>("get_quests", ct);
        var tE = q.Get<EnvironmentInfo>("get_current_environment", ct);
        Task<CliData<List<MissionItem>>>? tD = null, tW = null;
        Task<CliData<ActivityInfo>>? tA = null;
        Task<CliData<AlteringWorksResponse>>? tW2 = null;
        Task<CliData<List<CurrencyItem>>>? tC = null;
        Task<CliData<List<ItemData>>>? tI = null;
        Task<CliData<GrowthGuideResponse>>? tG = null;
        if (full)
        {
            tD = q.Get<List<MissionItem>>("get_daily_missions", ct);
            tW = q.Get<List<MissionItem>>("get_weekly_missions", ct);
            tA = q.Get<ActivityInfo>("get_activity", ct);
            tW2 = q.Get<AlteringWorksResponse>("get_altering_works", ct);
            tC = q.Get<List<CurrencyItem>>("get_currencies", ct);   // 레이드 증표 관찰
            tI = q.Get<List<ItemData>>("get_items", ct);            // 레이드 고유 보상 아이템 (에이렐 하프 조각·서큐버스 거울 조각)
            tG = q.Get<GrowthGuideResponse>("get_growth_guide", ct);   // 게임이 직접 주는 콘텐츠 가이드(완료·남은 횟수·잠금)
        }
        await Task.WhenAll(new Task[] { tMe, tQ, tE }.Concat(new Task?[] { tD, tW, tA, tW2, tC, tI, tG }.OfType<Task>()));

        if (tMe.Result.Value is not { } ch || string.IsNullOrWhiteSpace(ch.RealmName) || string.IsNullOrWhiteSpace(ch.JobName)) return null;
        // 빠른 관찰은 재화를 읽지 않으므로 캐릭터를 감시가 알던 캐릭터와 같은 읽기로 확인할 수 있을 때만 쓴다.
        // 달라 보이면(전환·연결 직후) 재화로 키를 정해야 하니 전체 관찰로 다시 한다(앞서 읽은 값은 캐시에서 재사용).
        if (!full && !(state.CurrentKey != null && CharacterIdentity.SameReading(state.Character, ch)))
            return await ObserveAsync(ct, full: true);
        var currencies = tC?.Result.Value;   // 빠른 관찰이면 null: 키는 감시가 가진 재화(state.Currencies)로 정한다
        var key = KeyOf(ch, currencies);
        var items = tI?.Result.Value;
        // 캐릭터가 바뀌었으면 재화·아이템 캐시가 이전 캐릭터 것일 수 있다: 새로 읽은 값으로 판정한다(레이드 클리어 비교는 캐릭터별 기준값과 하므로 틀린 캐릭터 값이 섞이면 안 된다).
        // 퀘스트는 판정기가 이번 관찰을 건너뛰고, 다음 조회는 새로 받게 한다.
        if (homework.LastObservedCharacter is { } last && !last.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            q.Invalidate("get_currencies", "get_items", "get_quests", "get_weekly_missions", "get_daily_missions", "get_growth_guide");   // 미션도 캐릭터마다 다르다(2026-10-22 실측)
            if (full)
            {
                var tC2 = q.Get<List<CurrencyItem>>("get_currencies", ct);
                var tI2 = q.Get<List<ItemData>>("get_items", ct);
                await Task.WhenAll(tC2, tI2);
                currencies = tC2.Result.Value;
                items = tI2.Result.Value;
            }
        }
        if (!string.Equals(_loggedKey, key, StringComparison.Ordinal))
        {
            _loggedKey = key;
            log.LogInformation("숙제 관찰: {Key}", key);
        }
        Evaluate(key, new HomeworkObservation(
            tD?.Result.Value, tW?.Result.Value, tQ.Result.Value, tA?.Result.Value, tE.Result.Value, tW2?.Result.Value,
            Currencies: currencies, Items: items, GrowthGuide: tG?.Result.Value));
        return key;
    }

    public void Evaluate(string characterKey, HomeworkObservation obs)
    {
        var change = homework.Evaluate(characterKey, obs);
        if (change != null) Notify(change);
    }

    public void Notify(HomeworkChange change)
    {
        log.LogInformation("숙제 변경 [{Reason}]: {Key} {Ids}", change.Reason, change.CharacterKey, string.Join(",", change.Ids));
        events.RaiseHomeworkChanged(change);
    }
}
