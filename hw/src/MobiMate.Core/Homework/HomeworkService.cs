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
    string? Evidence, string? Pool, string? Reward, string? NeedsMeasurement, HomeworkSuggestion? Suggestion = null, int? Remaining = null)
{
    public bool IsDone => Status is HomeworkCardStatus.AutoDone or HomeworkCardStatus.ManualDone or HomeworkCardStatus.PoolDone;
}

/// <summary>캐릭터 카드에 보여 줄 자동 판정 숙제 한 줄. State: done / todo / unknown. Suggestion = 완료가 아닌데 클리어 증거가 보인 "완료 제안"(화면의 모서리 점).</summary>
public sealed record HomeworkAutoStatus(string Id, string Title, string Period, string State, string? Evidence, int? Remaining = null, string Category = "", HomeworkSuggestion? Suggestion = null, string? Locked = null, int? Value = null);

/// <summary>공유 풀(필드 보스 택1)의 캐릭터별 현황. State: done / todo / unknown, Item = 넘긴 보스 이름(완료일 때).</summary>
/// <param name="Guess">"클리어 추정": 필드 보스 미션 카운터는 올랐는데 어느 보스인지 못 가렸다(눌러서 확인).</param>
public sealed record HomeworkPoolStatus(string State, string? Item, bool Guess = false);

public sealed record HomeworkProgress(int Done, int Total);

public sealed record HomeworkBoard(
    IReadOnlyList<HomeworkCard> Cards, HomeworkProgress Daily, HomeworkProgress Weekly,
    DateTimeOffset NextDailyResetUtc, DateTimeOffset NextWeeklyResetUtc);

public enum HomeworkChangeReason { Auto, Manual, Reset, External }

public sealed record HomeworkChange(string CharacterKey, IReadOnlyList<string> Ids, HomeworkChangeReason Reason);

/// <summary>
/// 스마트 숙제 트래커 (요구사양서 §5.11). 카탈로그·저장소·평가기를 묶는다. 스레드 안전(내부 잠금).
/// - 기록은 메모리에 두고, 바뀐 것이 있을 때만 저장한다. 같은 폴더를 다른 프로세스(숙제 앱)도 쓸 수 있으므로(공유 저장 패치)
///   모든 수정은 "공유 잠금 → 파일이 바뀌었으면 다시 읽기 → 변경 → 바뀐 때만 저장"으로 한다 (잠금 순서: 서비스 잠금 → 공유 잠금).
/// - 파일을 일시적으로 읽지 못하면 빈 기록으로 덮어쓰지 않는다: 판정은 건너뛰고, 수동 조작은 IOException으로 실패시킨다.
/// - 진행 통계: 공유 풀은 1건으로 센다.
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
        get
        {
            lock (_lock)
            {
                EnsureFresh();
                return _file?.LastObservedCharacter ?? _lastEvaluatedCharacter;
            }
        }
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
        lock (_lock) return _store.Lock.Run(() => EvaluateLocked(characterKey, obs));
    }

    private HomeworkChange? EvaluateLocked(string characterKey, HomeworkObservation obs)
    {
        {
            if (!Refresh(out HomeworkFile file)) return null;   // 읽기 실패: 이번 판정 건너뜀 (빈 기록으로 덮어쓰지 않음)
            var before = Json(file);   // 저장할지는 "내용이 실제로 바뀌었는가"로 가른다(관찰 시각·목격 기록 포함)
            var now = _now();
            var (character, resetIds, created) = Prepare(file, characterKey, now);

            // 파일에 남긴 마지막 관찰 캐릭터와 비교한다(재기동 뒤에도 "사이에 다른 캐릭터"를 안다, H2)
            var last = file.LastObservedCharacter ?? _lastEvaluatedCharacter;
            var switched = last != null && !last.Equals(characterKey, StringComparison.OrdinalIgnoreCase);
            _lastEvaluatedCharacter = characterKey;
            var lastChanged = !characterKey.Equals(file.LastObservedCharacter, StringComparison.OrdinalIgnoreCase);
            // 캐릭터를 바꿨으면 이전 캐릭터를 끊김 없이 지켜본 것이 아니다: 끊김 없는 관찰이 필요한 항목에 표시만 남긴다
            // (목격 기록은 그대로 둔다: 카드에는 마지막으로 본 "미완료·남은 횟수"가 계속 보인다)
            var continuityBroken = false;
            if (switched && last != null && file.Characters.TryGetValue(last, out var prevLedger) && prevLedger.QuestSightings is { Count: > 0 } prevSightings)
                foreach (var d in _catalog.Items.Where(d => d.Continuous && prevSightings.ContainsKey(d.Id)))
                    continuityBroken |= (prevLedger.QuestContinuityBroken ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(d.Id);
            file.LastObservedCharacter = characterKey;

            var result = HomeworkEvaluator.Evaluate(character, file.Account, _catalog, obs, now, switched);

            var signals = SignalsFor(characterKey);
            foreach (var id in result.InProgressIds) signals[id] = now;

            // 판정에 쓰는 관찰 시각(RaidTokensObservedUtc·AlteringObservedUtc)도 저장 대상이다: 다른 프로세스가 이 값으로 판정한다 (공유 저장 패치).
            // 같은 내용이면 저장하지 않는다(4초 주기 감시가 파일을 갈아 쓰지 않게).
            var dirty = created || resetIds.Count > 0 || result.ChangedIds.Count > 0 || continuityBroken || lastChanged || Json(file) != before;
            if (dirty) SaveLocked(file);

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

        lock (_lock) return _store.Lock.Run(() => SetLocked(characterKey, def, completed, count));
    }

    public HomeworkChange SetSeasonIfUnchanged(string characterKey, string id, int count, int expected)
    {
        RequireKey(characterKey);
        var def = _catalog.Find(id) ?? throw new KeyNotFoundException(id);
        if (!def.SeasonReset) throw new ArgumentException("시즌 기록이 아닙니다.");
        lock (_lock) return _store.Lock.Run(() =>
        {
            var file = LoadOrThrow();
            var actual = file.Characters.GetValueOrDefault(characterKey)?.Items.GetValueOrDefault(id)?.Count ?? 0;
            if (actual != expected) throw new InvalidOperationException("다른 곳에서 시즌 기록이 변경되었습니다. 최신 값을 확인하고 다시 입력해 주세요.");
            return SetLocked(characterKey, def, null, count);
        });
    }

    private HomeworkChange SetLocked(string characterKey, HomeworkDefinition def, bool? completed, int? count)
    {
        {
            var file = LoadOrThrow();
            var now = _now();
            var (character, _, _) = Prepare(file, characterKey, now);
            var ledger = def.Share == HomeworkShare.Account ? file.Account : character;
            var st = ledger.Items.GetValueOrDefault(def.Id) ?? new HomeworkItemState();

            if (def.EffectiveMode == HomeworkMode.ManualValue)
            {
                // 수치 입력(랭킹 등): count가 값이다. 1 이상이면 입력된 것(완료 표시), 0이면 지운다(-).
                var value = count.HasValue ? Math.Clamp(count.Value, 0, def.Goal) : completed == true ? 1 : 0;
                if (value == 0) ledger.Items.Remove(def.Id);
                else
                {
                    st.Count = value;
                    st.Goal = def.Goal;
                    st.Completed = true;
                    st.CompletedAtUtc = now;
                    st.Auto = false;
                    st.Evidence = $"수동 입력: {value}";
                    st.Suggestion = null;
                    st.ManualOverride = true;
                    st.ManualAtUtc = now;
                    ledger.Items[def.Id] = st;
                }
                SaveOrThrow(file);
                return new HomeworkChange(characterKey, new[] { def.Id }, HomeworkChangeReason.Manual);
            }

            if (def.RunGoal > 0 && def.Share == HomeworkShare.Character)
            {
                // 여러 번 깨는 콘텐츠(RunGoal): 횟수(0~RunGoal)를 손으로 맞춘다. 횟수를 주면 그 값으로, 완료/미완료만 주면 끝/0으로 본다.
                // 일부만 한 상태(횟수 < 목표)는 수동 고정(ManualOverride)을 걸지 않는다: 이어서 자동 판정이 횟수를 세게 하려는 것이다
                // (퀘스트가 보이면 게임의 N/M이, 전용 지역 보상이 들어오면 +1이 이 값에서 이어진다).
                var run = count.HasValue ? Math.Clamp(count.Value, 0, def.RunGoal) : completed == true ? def.RunGoal : 0;
                var runDone = run >= def.RunGoal;
                // 목표에 닿은 수동 완료는 횟수를 남기지 않는다(퀘스트가 다시 보이면 게임의 N/M이 이긴다: 수동 완료는 퀘스트가 보이면 취소되는 기존 규칙 그대로)
                if (runDone) character.RunCounts?.Remove(def.Id);
                else (character.RunCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))[def.Id] = run;
                st.Count = runDone ? def.Goal : 0;
                st.Goal = def.Goal;
                st.Completed = runDone;
                st.CompletedAtUtc = runDone ? now : null;
                st.Auto = false;
                st.Suggestion = null;
                st.RemainingCount = runDone || def.RunGoal <= 1 ? null : def.RunGoal - run;
                st.Evidence = $"수동 설정: 이번 주 {run}/{def.RunGoal}";   // 미완료 확인으로도 쓰인다(전체 탭 카드가 "남은 N회"를 보인다)
                st.ManualOverride = runDone;
                st.ManualAtUtc = now;
                ledger.Items[def.Id] = st;
                SaveOrThrow(file);
                return new HomeworkChange(characterKey, new[] { def.Id }, HomeworkChangeReason.Manual);
            }

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
            st.ManualAtUtc = now;
            ledger.Items[def.Id] = st;
            SaveOrThrow(file);
            return new HomeworkChange(characterKey, new[] { def.Id }, HomeworkChangeReason.Manual);
        }
    }

    /// <summary>전체 체크 초기화 (FR-HW-11). 이번 주기 상태를 모두 지운다. 되돌릴 수 없으므로 호출 전에 사용자 확인을 받는다.</summary>
    public HomeworkChange ResetAll(string characterKey, bool includeAccount)
    {
        RequireKey(characterKey);
        lock (_lock) return _store.Lock.Run(() => ResetAllLocked(characterKey, includeAccount));
    }

    private HomeworkChange ResetAllLocked(string characterKey, bool includeAccount)
    {
        {
            var file = LoadOrThrow();
            var (character, _, _) = Prepare(file, characterKey, _now());
            var season = _catalog.Items.Where(d => d.SeasonReset).Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ids = character.Items.Keys.Where(k => !season.Contains(k)).ToList();
            foreach (var id in ids) character.Items.Remove(id);
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
            // 리셋·새 장부 생성이 필요하면 공유 잠금 안에서 "다시 읽기 → Prepare → 바뀐 때만 저장"을 한 묶음으로 한다(EvaluateLocked와 같은 형태).
            var current = false;
            try
            {
                current = _store.Lock.Run(() =>
                {
                    if (!Refresh(out HomeworkFile f)) return false;
                    var before = Json(f);
                    Prepare(f, characterKey, now);
                    if (Json(f) != before) SaveLocked(f);
                    return _file != null;
                });
            }
            catch (IOException) { /* 잠금을 못 얻으면 아래 복사본으로 화면만 만든다 */ }

            HomeworkFile file;
            HomeworkLedger character;
            if (current)
            {
                file = _file!;   // 방금 잠금 안에서 맞춘 최신 기록: 여기서는 읽기만 한다
                character = file.Characters[characterKey];
            }
            else
            {
                // 최신으로 읽지 못했다: 살아 있는 기록은 건드리지 않고, 메모리 값(없으면 빈 기록)의 복사본으로만 화면을 만든다(저장 없음)
                file = _file != null ? HomeworkStore.Clone(_file) : new HomeworkFile();
                character = Prepare(file, characterKey, now).Character;
            }

            var signals = _signals.GetValueOrDefault(characterKey);
            var all = _catalog.Items.Where(d => d.Active && d.EffectiveMode != HomeworkMode.ManualValue && !d.SeasonReset).Select(def => ToCard(def, file, character, signals, now)).ToList();

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
            var defs = _catalog.Items.Where(d => d.Active && (d.AutoCheck || d.Id == "raid_scars" || d.SeasonReset) && d.Share == HomeworkShare.Character).ToList();
            var result = new Dictionary<string, IReadOnlyList<HomeworkAutoStatus>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in characterKeys)
            {
                file.Characters.TryGetValue(key, out var ledger);
                var list = new List<HomeworkAutoStatus>();
                foreach (var def in defs)
                {
                    var cycleStart = def.Period == HomeworkPeriod.Daily ? lastDaily : lastWeekly;
                    var fresh = ledger != null && (def.SeasonReset || (def.Period == HomeworkPeriod.Daily ? ledger.LastDailyResetUtc : ledger.LastWeeklyResetUtc) >= cycleStart);
                    var st = fresh ? ledger!.Items.GetValueOrDefault(def.Id) : null;
                    string state;
                    if (st?.Completed == true) state = "done";
                    else if (def.EffectiveMode == HomeworkMode.QuestVanish) state = fresh && (ledger!.QuestSightings?.GetValueOrDefault(def.Id) >= cycleStart || st?.Evidence != null) ? "todo" : "unknown";
                    else if (def.EffectiveMode == HomeworkMode.QuestSuffix) state = st?.Evidence != null ? "todo" : "unknown";
                    // 레이드·어비스·필드 보스는 "아직 안 깼다"를 CLI로 확인할 길이 없다(게임의 "남은 클리어 전리품"은 CLI에 없음, 2026-10-22).
                    // 앱이 보기 전에 깼을 수 있으므로 완료를 본 게 아니면 미확인으로 둔다(근거 없이 "확인된 미완료"라 하지 않는다).
                    else if (def.ClearGroup != null) state = "unknown";
                    else if (!string.IsNullOrWhiteSpace(def.TokenCurrency)) state = fresh && ledger!.RaidTokensObservedUtc >= cycleStart ? "todo" : "unknown";
                    else state = "unknown";
                    list.Add(new HomeworkAutoStatus(def.Id, def.CardLabel ?? def.Title, def.Period == HomeworkPeriod.Daily ? "daily" : "weekly", state, st?.Evidence, state == "todo" ? st?.RemainingCount : null, def.Category,
                        state == "done" ? null : st?.Suggestion,
                        HomeworkEvaluator.GrowthLockHint(def, ledger?.GrowthGuide), def.SeasonReset ? st?.Count : null));   // 게임의 잠금 안내(레벨·퀘스트 조건): 입장 불가 표시에 쓴다
                }
                result[key] = list;
            }
            return result;
        }
    }

    /// <summary>
    /// 필드 보스 택1의 캐릭터별 현황: 이번 주기에 넘긴 보스가 있으면 done(보스 이름), 이번 주기 기록이 있는데 없으면 todo, 기록이 없으면 unknown.
    /// 읽기 전용(리셋을 적용해 저장하지 않고, 지난 주기의 값은 없는 것으로 본다).
    /// </summary>
    public IReadOnlyDictionary<string, HomeworkPoolStatus> GetFieldBossStatuses(IEnumerable<string> characterKeys, string pool = "field_boss_weekly")
    {
        lock (_lock)
        {
            TryLoad(out var file);
            var lastWeekly = KstClock.LastWeeklyReset(_now());
            var defs = _catalog.Items.Where(d => d.Active && d.Pool == pool && d.Share == HomeworkShare.Character).ToList();
            var result = new Dictionary<string, HomeworkPoolStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in characterKeys)
            {
                file.Characters.TryGetValue(key, out var ledger);
                var fresh = ledger != null && ledger.LastWeeklyResetUtc >= lastWeekly;
                var cleared = fresh ? defs.FirstOrDefault(d => ledger!.Items.GetValueOrDefault(d.Id)?.Completed == true) : null;
                // 택1이라 주간 미션 카운터가 오른 것만으로 이번 주는 끝났다: 어느 보스인지 몰라도 완료(보스 이름 없음)
                var counted = cleared == null && fresh && defs.Any(d => ledger!.Items.GetValueOrDefault(d.Id)?.Suggestion is { Code: "missionCleared" });
                result[key] = cleared != null ? new HomeworkPoolStatus("done", cleared.Title)
                    : counted ? new HomeworkPoolStatus("done", null)
                    : new HomeworkPoolStatus("unknown", null);   // 미완료는 CLI로 확인할 수 없다(위 GetAutoStatuses 참고)
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
            status is HomeworkCardStatus.Pending or HomeworkCardStatus.InProgress ? st?.Suggestion : null,
            status is HomeworkCardStatus.Pending or HomeworkCardStatus.InProgress ? st?.RemainingCount : null);
    }

    /// <summary>
    /// 공유 풀 완료 = 풀 안 항목 중 하나라도 완료, 또는 클리어 미션 카운터가 올랐는데 어느 항목인지 못 가린 경우("클리어 추정").
    /// 택1 풀(필드 보스)은 카운터가 오른 것만으로 이번 주기가 끝났다(2026-10-22 승운 결정). 형제 상태를 복사하지 않고 매번 계산한다.
    /// </summary>
    private bool PoolDone(string pool, HomeworkFile file, HomeworkLedger character) =>
        _catalog.Items.Where(d => d.Pool == pool)
            .Any(d => (d.Share == HomeworkShare.Account ? file.Account : character).Items.GetValueOrDefault(d.Id) is { } st
                && (st.Completed || st.Suggestion is { Code: "missionCleared" }));

    private static HomeworkProgress Progress(IReadOnlyList<HomeworkCard> cards, HomeworkPeriod period)
    {
        var relevant = cards.Where(c => c.Period == period).ToList();
        var singles = relevant.Where(c => c.Pool == null).ToList();
        var pools = relevant.Where(c => c.Pool != null).GroupBy(c => c.Pool!).ToList();
        var done = singles.Count(c => c.IsDone) + pools.Count(g => g.Any(c => c.IsDone));
        return new HomeworkProgress(done, singles.Count + pools.Count);
    }

    /// <summary>
    /// 읽기용: 파일이 바뀌었으면 다시 읽고(공유 잠금 안), 최신 기록이면 true. 읽지 못하면 false(file은 메모리 값 또는 빈 기록).
    /// 수정 경로는 이 메서드가 아니라 공유 잠금 안에서 <see cref="Refresh(out HomeworkFile)"/>를 쓴다.
    /// </summary>
    private bool TryLoad(out HomeworkFile file)
    {
        var ok = true;
        if (_file == null || _store.IsStale) ok = _store.Lock.Run(() => Refresh(out _));
        file = _file ?? new HomeworkFile();
        return ok && _file != null;
    }

    /// <summary>바뀌었으면 다시 읽는다(실패는 무시: 메모리 값을 그대로 쓴다). 서비스 잠금 안에서 부른다.</summary>
    private void EnsureFresh()
    {
        if (_file != null && !_store.IsStale) return;
        try { _store.Lock.Run(() => Refresh(out _)); }
        catch (IOException) { /* 잠금을 못 얻으면 메모리 값으로 */ }
    }

    /// <summary>
    /// 공유 잠금 안에서 메모리 기록을 최신으로 맞춘다. 다시 읽기는 TryLoad만 쓴다(Failed·교체 중이면 메모리 유지, 이번 작업 보류).
    /// 쓸 수 있는 최신 기록이 있으면 true.
    /// </summary>
    private bool Refresh(out HomeworkFile file) => Refresh(out file, out _);

    private bool Refresh(out HomeworkFile file, out bool reloaded)
    {
        reloaded = false;
        var r = _store.Refresh(_file != null, out var loaded);
        if (r == RefreshResult.Unavailable)
        {
            file = _file ?? new HomeworkFile();
            return false;
        }
        if (r == RefreshResult.Reloaded)
        {
            _file = loaded ?? new HomeworkFile();
            reloaded = true;
        }
        file = _file ??= new HomeworkFile();
        return true;
    }

    /// <summary>
    /// 다른 프로세스가 파일을 바꿨으면 다시 읽고, 내용이 실제로 달라졌으면 true. 변경 감지기가 부른다. 자기 쓰기는 바뀐 것으로 치지 않는다.
    /// </summary>
    public bool RefreshExternal()
    {
        lock (_lock)
        {
            if (_file != null && !_store.IsStale) return false;
            return _store.Lock.Run(() =>
            {
                var before = _file == null ? null : Json(_file);
                Refresh(out var f, out var reloaded);
                return reloaded && before != null && Json(f) != before;
            });
        }
    }

    private static string Json(HomeworkFile file) => System.Text.Json.JsonSerializer.Serialize(file);

    private HomeworkFile LoadOrThrow() =>
        Refresh(out HomeworkFile f) ? f : throw new IOException("숙제 기록 파일을 읽을 수 없어 변경하지 않았습니다. 잠시 후 다시 시도하세요.");

    private bool SaveLocked(HomeworkFile file)
    {
        if (_store.Save(file)) return true;
        _file = null;   // 저장 실패: 메모리 변경을 버리고 다음 호출에서 파일을 다시 읽는다
        return false;
    }

    private void SaveOrThrow(HomeworkFile file)
    {
        if (SaveLocked(file)) return;   // 실패하면 사용자에게 알리고 메모리 변경도 버린다(다음 호출에서 파일을 다시 읽음)
        throw new IOException("숙제 기록을 저장하지 못했습니다.");
    }

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
