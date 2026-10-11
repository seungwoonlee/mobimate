namespace MobiMateHomeWork.Services;

/// <summary>
/// 숙제 판정 연결부: 조회 결과를 모아 HomeworkService에 넘기고, 바뀌면 <see cref="AppEvents.HomeworkChanged"/>로 알린다.
/// 조회에 실패한 항목은 null로 넘겨 판정 근거에서 뺀다.
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
    public async Task<string?> ObserveAsync(CancellationToken ct)
    {
        var tMe = q.Get<CharacterInfo>("get_my_info", ct);
        var tD = q.Get<List<MissionItem>>("get_daily_missions", ct);
        var tW = q.Get<List<MissionItem>>("get_weekly_missions", ct);
        var tQ = q.Get<List<QuestItem>>("get_quests", ct);
        var tA = q.Get<ActivityInfo>("get_activity", ct);
        var tE = q.Get<EnvironmentInfo>("get_current_environment", ct);
        var tW2 = q.Get<AlteringWorksResponse>("get_altering_works", ct);
        var tC = q.Get<List<CurrencyItem>>("get_currencies", ct);   // 레이드 증표 관찰
        var tI = q.Get<List<ItemData>>("get_items", ct);            // 레이드 고유 보상 아이템 (에이렐 하프 조각·서큐버스 거울 조각)
        await Task.WhenAll(tMe, tD, tW, tQ, tA, tE, tW2, tC, tI);

        if (tMe.Result.Value is not { } ch || string.IsNullOrWhiteSpace(ch.RealmName) || string.IsNullOrWhiteSpace(ch.JobName)) return null;
        var key = KeyOf(ch, tC.Result.Value);
        var currencies = tC.Result.Value;
        var items = tI.Result.Value;
        // 캐릭터가 바뀌었으면 재화·아이템 캐시가 이전 캐릭터 것일 수 있다: 새로 읽은 값으로 판정한다(레이드 클리어 비교는 캐릭터별 기준값과 하므로 틀린 캐릭터 값이 섞이면 안 된다).
        // 퀘스트는 판정기가 이번 관찰을 건너뛰고, 다음 조회는 새로 받게 한다.
        if (homework.LastObservedCharacter is { } last && !last.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            q.Invalidate("get_currencies", "get_items", "get_quests");
            var tC2 = q.Get<List<CurrencyItem>>("get_currencies", ct);
            var tI2 = q.Get<List<ItemData>>("get_items", ct);
            await Task.WhenAll(tC2, tI2);
            currencies = tC2.Result.Value;
            items = tI2.Result.Value;
        }
        if (!string.Equals(_loggedKey, key, StringComparison.Ordinal))
        {
            _loggedKey = key;
            log.LogInformation("숙제 관찰: {Key}", key);
        }
        Evaluate(key, new HomeworkObservation(
            tD.Result.Value, tW.Result.Value, tQ.Result.Value, tA.Result.Value, tE.Result.Value, tW2.Result.Value,
            Currencies: currencies, Items: items));
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
