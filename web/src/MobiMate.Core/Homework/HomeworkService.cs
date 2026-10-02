namespace MobiMate;

public enum HomeworkCardStatus
{
    Pending,        // 미완료
    InProgress,     // 진행 신호 있음 (보스 교전·공간 입장), 완료 아님
    PoolDone,       // 같은 공유 풀의 다른 항목이 완료 (택1 완료)
    AutoDone,       // 자동 완료
    ManualDone      // 수동 완료
}

public sealed record HomeworkCard(
    string Id, string Category, string CategoryTitle, HomeworkPeriod Period, HomeworkShare Share, HomeworkMode Mode,
    string Title, string Subtitle, string Icon, int Count, int Goal, HomeworkCardStatus Status,
    string? Evidence, string? Pool, string? Reward, string? NeedsMeasurement, HomeworkSuggestion? Suggestion = null)
{
    public bool IsDone => Status is HomeworkCardStatus.AutoDone or HomeworkCardStatus.ManualDone or HomeworkCardStatus.PoolDone;
}

/// <summary>캐릭터 카드에 보여 줄 자동 판정 숙제 한 줄. State: done / todo / unknown.</summary>
public sealed record HomeworkAutoStatus(string Id, string Title, string Period, string State, string? Evidence);

public sealed record HomeworkProgress(int Done, int Total);

public sealed record HomeworkBoard(
    IReadOnlyList<HomeworkCard> Cards, HomeworkProgress Daily, HomeworkProgress Weekly,
    DateTimeOffset NextDailyResetUtc, DateTimeOffset NextWeeklyResetUtc);

public enum HomeworkChangeReason { Auto, Manual, Reset }

public sealed record HomeworkChange(string CharacterKey, IReadOnlyList<string> Ids, HomeworkChangeReason Reason);

/// <summary>
/// 스마트 숙제 트래커 (요구사양서 §5.11). 카탈로그·저장소·평가기를 묶는다. 스레드 안전(내부 잠금).
/// - 기록은 메모리에 두고, 바뀐 것이 있을 때만 저장한다 (이 폴더는 웹앱만 쓴다).
/// - 파일을 일시적으로 읽지 못하면 빈 기록으로 덮어쓰지 않는다: 판정은 건너뛰고, 수동 조작은 IOException으로 실패시킨다.
/// - 진행 통계: 상점·교환은 제외하고, 공유 풀은 1건으로 센다.
/// </summary>
public sealed class HomeworkService
{
    public static readonly TimeSpan SignalLifetime = TimeSpan.FromMinutes(10);

    private readonly HomeworkCatalog _catalog;
    private readonly HomeworkStore _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private readonly Dictionary<string, Dictionary<string, DateTimeOffset>> _signals = new(StringComparer.OrdinalIgnoreCase);
    private HomeworkFile? _file;
    private string? _lastEvaluatedCharacter;

    public HomeworkCatalog Catalog => _catalog;

    /// <summary>마지막으로 판정한 캐릭터 키 (캐릭터 전환 감지용).</summary>
    public string? LastObservedCharacter
    {
        get { lock (_lock) return _file?.LastObservedCharacter ?? _lastEvaluatedCharacter; }
    }

    public HomeworkService(HomeworkCatalog catalog, HomeworkStore store, Func<DateTimeOffset>? utcNow = null)
    {
        _catalog = catalog;
        _store = store;
        _now = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>조회 결과로 자동 판정한다. 바뀐 항목(또는 진행 신호)이 있으면 변경을 돌려준다(없으면 null).</summary>
    public HomeworkChange? Evaluate(string characterKey, HomeworkObservation obs)
    {
        RequireKey(characterKey);
        lock (_lock)
        {
            if (!TryLoad(out var file)) return null;   // 읽기 실패: 이번 판정 건너뜀 (빈 기록으로 덮어쓰지 않음)
            var now = _now();
            var (character, resetIds, created) = Prepare(file, characterKey, now);

            // 파일에 남긴 마지막 관찰 캐릭터와 비교한다(재기동 뒤에도 "사이에 다른 캐릭터"를 안다, H2)
            var last = file.LastObservedCharacter ?? _lastEvaluatedCharacter;
            var switched = last != null && !last.Equals(characterKey, StringComparison.OrdinalIgnoreCase);
            _lastEvaluatedCharacter = characterKey;
            var lastChanged = !characterKey.Equals(file.LastObservedCharacter, StringComparison.OrdinalIgnoreCase);
            file.LastObservedCharacter = characterKey;

            var alteringBefore = character.AlteringDoneCounts;
            var tokensBefore = character.RaidTokens;
            var sightingsBefore = character.QuestSightings == null ? null : new Dictionary<string, DateTimeOffset>(character.QuestSightings);
            var result = HomeworkEvaluator.Evaluate(character, file.Account, _catalog, obs, now, switched);
            // 관찰 시각만 바뀐 경우는 메모리에만 두고 저장하지 않는다 (개수 내용이 바뀌었을 때만 저장)
            var alteringChanged = !SameCounts(alteringBefore, character.AlteringDoneCounts) || !SameAmounts(tokensBefore, character.RaidTokens);
            var sightingsChanged = !SameSightings(sightingsBefore, character.QuestSightings);

            var signals = SignalsFor(characterKey);
            foreach (var id in result.InProgressIds) signals[id] = now;

            if ((created || resetIds.Count > 0 || result.ChangedIds.Count > 0 || alteringChanged || sightingsChanged || lastChanged) && !_store.Save(file))
                _file = null;   // 저장 실패: 메모리 변경을 버리고 다음 호출에서 파일을 다시 읽는다

            var ids = resetIds.Concat(result.ChangedIds).Concat(result.InProgressIds).Distinct().ToList();
            if (ids.Count == 0) return null;
            var reason = result.ChangedIds.Count == 0 && resetIds.Count > 0 ? HomeworkChangeReason.Reset : HomeworkChangeReason.Auto;
            return new HomeworkChange(characterKey, ids, reason);
        }
    }

    /// <summary>
    /// 수동 설정 (FR-HW-08). 뒤집기가 아니라 목표 상태를 지정한다(여러 기기에서 동시에 눌러도 결과가 같다).
    /// 단계 카운터 항목은 count로, 나머지는 completed로 지정한다.
    /// </summary>
    public HomeworkChange Set(string characterKey, string id, bool? completed = null, int? count = null)
    {
        RequireKey(characterKey);
        var def = _catalog.Find(id) ?? throw new KeyNotFoundException($"알 수 없는 숙제: {id}");
        if (completed == null && count == null) throw new ArgumentException("completed 또는 count가 필요합니다.");

        lock (_lock)
        {
            var file = LoadOrThrow();
            var now = _now();
            var (character, _, _) = Prepare(file, characterKey, now);
            var ledger = def.Share == HomeworkShare.Account ? file.Account : character;
            var st = ledger.Items.GetValueOrDefault(def.Id) ?? new HomeworkItemState();

            var newCount = count.HasValue ? Math.Clamp(count.Value, 0, def.Goal) : completed == true ? def.Goal : 0;
            var done = count.HasValue ? newCount >= def.Goal : completed == true;

            st.Count = newCount;
            st.Goal = def.Goal;
            st.Completed = done;
            st.CompletedAtUtc = done ? now : null;
            st.Auto = false;
            st.Evidence = null;
            st.Suggestion = null;
            st.ManualOverride = true;
            ledger.Items[def.Id] = st;
            SaveOrThrow(file);
            return new HomeworkChange(characterKey, new[] { def.Id }, HomeworkChangeReason.Manual);
        }
    }

    /// <summary>전체 체크 초기화 (FR-HW-11). 이번 주기 상태를 모두 지운다. 되돌릴 수 없으므로 호출 전에 사용자 확인을 받는다.</summary>
    public HomeworkChange ResetAll(string characterKey, bool includeAccount)
    {
        RequireKey(characterKey);
        lock (_lock)
        {
            var file = LoadOrThrow();
            var (character, _, _) = Prepare(file, characterKey, _now());
            var ids = character.Items.Keys.ToList();
            character.Items.Clear();
            character.AlteringDoneCounts = null;
            character.AlteringObservedUtc = null;
            if (includeAccount)
            {
                ids.AddRange(file.Account.Items.Keys);
                file.Account.Items.Clear();
            }
            _signals.Remove(characterKey);
            SaveOrThrow(file);
            return new HomeworkChange(characterKey, ids, HomeworkChangeReason.Reset);
        }
    }

    public HomeworkBoard GetBoard(string characterKey, string? category = null)
    {
        RequireKey(characterKey);
        lock (_lock)
        {
            var now = _now();
            // 읽기에 실패해도 화면은 보여 준다(빈 기록 기준, 저장하지 않음)
            var loaded = TryLoad(out var file);
            if (!loaded) file = new HomeworkFile();
            var (character, resetIds, created) = Prepare(file, characterKey, now);
            if (loaded && (created || resetIds.Count > 0)) _store.Save(file);

            var signals = _signals.GetValueOrDefault(characterKey);
            var all = _catalog.Items.Where(d => d.Active).Select(def => ToCard(def, file, character, signals, now)).ToList();

            var order = _catalog.Items.Select((d, i) => (d.Id, i)).ToDictionary(x => x.Id, x => x.i);
            var cards = all
                .Where(c => category == null || c.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.IsDone)
                .ThenBy(c => order[c.Id])
                .ToList();

            return new HomeworkBoard(cards, Progress(all, HomeworkPeriod.Daily), Progress(all, HomeworkPeriod.Weekly),
                KstClock.NextDailyReset(now), KstClock.NextWeeklyReset(now));
        }
    }

    /// <summary>
    /// 자동 판정되는 숙제(AutoCheck)의 캐릭터별 현황: 전체 탭의 캐릭터 카드용. 읽기 전용이다(리셋을 적용해 저장하지 않고, 지난 주기의 값은 없는 것으로 본다).
    /// done = 완료, todo = 이번 주기에 직접 확인한 미완료, unknown = 이번 주기에 아직 확인하지 못함(미완료로 보여 준다).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<HomeworkAutoStatus>> GetAutoStatuses(IEnumerable<string> characterKeys)
    {
        lock (_lock)
        {
            var now = _now();
            TryLoad(out var file);
            var lastDaily = KstClock.LastDailyReset(now);
            var lastWeekly = KstClock.LastWeeklyReset(now);
            var defs = _catalog.Items.Where(d => d.Active && d.AutoCheck && d.Share == HomeworkShare.Character).ToList();
            var result = new Dictionary<string, IReadOnlyList<HomeworkAutoStatus>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in characterKeys)
            {
                file.Characters.TryGetValue(key, out var ledger);
                var list = new List<HomeworkAutoStatus>();
                foreach (var def in defs)
                {
                    var cycleStart = def.Period == HomeworkPeriod.Daily ? lastDaily : lastWeekly;
                    var fresh = ledger != null && (def.Period == HomeworkPeriod.Daily ? ledger.LastDailyResetUtc : ledger.LastWeeklyResetUtc) >= cycleStart;
                    var st = fresh ? ledger!.Items.GetValueOrDefault(def.Id) : null;
                    string state;
                    if (st?.Completed == true) state = "done";
                    else if (def.EffectiveMode == HomeworkMode.QuestVanish) state = fresh && ledger!.QuestSightings?.GetValueOrDefault(def.Id) >= cycleStart ? "todo" : "unknown";
                    else if (def.EffectiveMode == HomeworkMode.QuestSuffix) state = st?.Evidence != null ? "todo" : "unknown";
                    else if (!string.IsNullOrWhiteSpace(def.TokenCurrency)) state = fresh && ledger!.RaidTokensObservedUtc >= cycleStart ? "todo" : "unknown";
                    else state = "unknown";
                    list.Add(new HomeworkAutoStatus(def.Id, def.Title, def.Period == HomeworkPeriod.Daily ? "daily" : "weekly", state, st?.Evidence));
                }
                result[key] = list;
            }
            return result;
        }
    }

    private HomeworkCard ToCard(HomeworkDefinition def, HomeworkFile file, HomeworkLedger character,
        Dictionary<string, DateTimeOffset>? signals, DateTimeOffset now)
    {
        var st = (def.Share == HomeworkShare.Account ? file.Account : character).Items.GetValueOrDefault(def.Id);
        HomeworkCardStatus status;
        if (st?.Completed == true) status = st.Auto ? HomeworkCardStatus.AutoDone : HomeworkCardStatus.ManualDone;
        else if (def.Pool != null && PoolDone(def.Pool, file, character)) status = HomeworkCardStatus.PoolDone;
        else if (signals != null && signals.TryGetValue(def.Id, out var at) && now - at <= SignalLifetime) status = HomeworkCardStatus.InProgress;
        else status = HomeworkCardStatus.Pending;

        return new HomeworkCard(def.Id, def.Category, HomeworkCatalog.CategoryTitles.GetValueOrDefault(def.Category, def.Category),
            def.Period, def.Share, def.EffectiveMode, def.Title, def.Subtitle, def.Icon,
            st?.Count ?? 0, st?.Goal ?? def.Goal, status, st?.Evidence, def.Pool, def.Reward, def.NeedsMeasurement,
            status is HomeworkCardStatus.Pending or HomeworkCardStatus.InProgress ? st?.Suggestion : null);
    }

    /// <summary>공유 풀 완료 = 풀 안 항목 중 하나라도 완료. 형제 상태를 복사하지 않고 매번 계산한다.</summary>
    private bool PoolDone(string pool, HomeworkFile file, HomeworkLedger character) =>
        _catalog.Items.Where(d => d.Pool == pool)
            .Any(d => (d.Share == HomeworkShare.Account ? file.Account : character).Items.GetValueOrDefault(d.Id)?.Completed == true);

    private static HomeworkProgress Progress(IReadOnlyList<HomeworkCard> cards, HomeworkPeriod period)
    {
        var relevant = cards.Where(c => c.Period == period && c.Category != "shop").ToList();
        var singles = relevant.Where(c => c.Pool == null).ToList();
        var pools = relevant.Where(c => c.Pool != null).GroupBy(c => c.Pool!).ToList();
        var done = singles.Count(c => c.IsDone) + pools.Count(g => g.Any(c => c.IsDone));
        return new HomeworkProgress(done, singles.Count + pools.Count);
    }

    /// <summary>처음 한 번 파일을 읽어 메모리에 둔다. 읽기에 실패하면 다음 호출에서 다시 시도한다.</summary>
    private bool TryLoad(out HomeworkFile file)
    {
        _file ??= _store.TryLoad();
        file = _file ?? new HomeworkFile();
        return _file != null;
    }

    private HomeworkFile LoadOrThrow() =>
        TryLoad(out var f) ? f : throw new IOException("숙제 기록 파일을 읽을 수 없어 변경하지 않았습니다. 잠시 후 다시 시도하세요.");

    private void SaveOrThrow(HomeworkFile file)
    {
        if (_store.Save(file)) return;
        _file = null;   // 사용자에게 실패를 알렸으니 메모리 변경도 버린다(다음 호출에서 파일을 다시 읽음)
        throw new IOException("숙제 기록을 저장하지 못했습니다.");
    }

    private static bool SameCounts(Dictionary<string, int>? a, Dictionary<string, int>? b) =>
        a == null ? b == null : b != null && a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private static bool SameSightings(Dictionary<string, DateTimeOffset>? a, Dictionary<string, DateTimeOffset>? b) =>
        a == null ? b == null || b.Count == 0 : b != null && a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private static bool SameAmounts(Dictionary<string, long>? a, Dictionary<string, long>? b) =>
        a == null ? b == null : b != null && a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private Dictionary<string, DateTimeOffset> SignalsFor(string characterKey) =>
        _signals.TryGetValue(characterKey, out var s) ? s : _signals[characterKey] = new(StringComparer.OrdinalIgnoreCase);

    /// <returns>캐릭터 장부, 리셋된 항목 ID, 새 장부를 만들었는지</returns>
    private (HomeworkLedger Character, List<string> ResetIds, bool Created) Prepare(HomeworkFile file, string characterKey, DateTimeOffset now)
    {
        var created = false;
        if (file.Account.LastDailyResetUtc == default)
        {
            Stamp(file.Account, now);
            created = true;
        }
        if (!file.Characters.TryGetValue(characterKey, out var character))
        {
            file.Characters[characterKey] = character = new HomeworkLedger();
            Stamp(character, now);
            created = true;
        }

        var reset = HomeworkEvaluator.ApplyResets(file.Account, _catalog, HomeworkShare.Account, now);
        reset.AddRange(HomeworkEvaluator.ApplyResets(character, _catalog, HomeworkShare.Character, now));
        if (reset.Count > 0 && _signals.TryGetValue(characterKey, out var sig)) foreach (var id in reset) sig.Remove(id);
        return (character, reset, created);
    }

    private static void Stamp(HomeworkLedger ledger, DateTimeOffset now)
    {
        ledger.LastDailyResetUtc = KstClock.LastDailyReset(now);
        ledger.LastWeeklyResetUtc = KstClock.LastWeeklyReset(now);
    }

    private static void RequireKey(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey)) throw new ArgumentException("캐릭터 키가 필요합니다.", nameof(characterKey));
    }
}
