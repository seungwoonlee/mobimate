namespace MobiMate;

/// <summary>한 번의 조회 결과 묶음. 조회에 실패한 항목은 null로 둔다.</summary>
public sealed record HomeworkObservation(
    IReadOnlyList<MissionItem>? DailyMissions = null,
    IReadOnlyList<MissionItem>? WeeklyMissions = null,
    IReadOnlyList<QuestItem>? Quests = null,
    ActivityInfo? Activity = null,
    EnvironmentInfo? Environment = null,
    AlteringWorksResponse? AlteringWorks = null,
    bool CollectedByApp = false,
    IReadOnlyList<CurrencyItem>? Currencies = null,
    IReadOnlyList<ItemData>? Items = null,
    GrowthGuideResponse? GrowthGuide = null);

public sealed record HomeworkEvaluation(IReadOnlyList<string> ChangedIds, IReadOnlyList<string> InProgressIds);

/// <summary>
/// 숙제 판정 (요구사양서 §4.3·§5.11, 상세설계 §2.8). 오탐 0 원칙:
/// - 자동 완료는 확정 증거(정확히 일치하는 1:1 미션, 전체 미션 개수, 모든 목표가 끝난 퀘스트, 수거 관찰)일 때만
/// - 항목마다 판정 방식 하나만 적용 (H-10)
/// - 보스 교전·공간 입장은 "진행 중" 신호일 뿐 완료가 아니다 (H-1)
/// - 수동 설정한 항목은 이번 주기 동안 건드리지 않는다, 완료는 리셋 전까지 유지한다
/// </summary>
public static partial class HomeworkEvaluator
{
    public static readonly TimeSpan AlteringCompareWindow = TimeSpan.FromMinutes(10);

    /// <summary>리셋 시각이 지난 주기의 모든 항목을 초기화한다 (H-5). 서버가 꺼져 있던 동안의 밀린 리셋도 처리된다.</summary>
    public static List<string> ApplyResets(HomeworkLedger ledger, HomeworkCatalog catalog, HomeworkShare share, DateTimeOffset nowUtc)
    {
        var changed = new List<string>();
        var lastDaily = KstClock.LastDailyReset(nowUtc);
        var lastWeekly = KstClock.LastWeeklyReset(nowUtc);
        var dailyDue = ledger.LastDailyResetUtc < lastDaily;
        var weeklyDue = ledger.LastWeeklyResetUtc < lastWeekly;
        if (!dailyDue && !weeklyDue) return changed;

        foreach (var def in catalog.Items.Where(d => d.Share == share))
        {
            var due = def.Period == HomeworkPeriod.Daily ? dailyDue : weeklyDue;
            if (due && ledger.Items.Remove(def.Id)) changed.Add(def.Id);
            if (due) { ledger.QuestSightings?.Remove(def.Id); ledger.QuestContinuityBroken?.Remove(def.Id); ledger.QuestVanishPending?.Remove(def.Id); ledger.RaidAreaSince?.Remove(ClearKey(def)); ledger.RunCounts?.Remove(def.Id); }
        }
        if (weeklyDue) { ledger.ClearMissionCounts = null; ledger.ClearMissionCountsUtc = null; ledger.ProgressSeen = null; }   // 이번 주기에 본 클리어 미션 카운터·진행 신호는 지난 주 것이다
        if (dailyDue) ledger.LastDailyResetUtc = lastDaily;
        if (weeklyDue) ledger.LastWeeklyResetUtc = lastWeekly;
        return changed;
    }

    /// <param name="characterJustSwitched">직전 판정과 캐릭터가 다르면 true. 이때는 가공 개수를 기록만 하고 비교하지 않는다 (FR-HW-05).</param>
    public static HomeworkEvaluation Evaluate(
        HomeworkLedger character, HomeworkLedger account, HomeworkCatalog catalog, HomeworkObservation obs, DateTimeOffset nowUtc,
        bool characterJustSwitched = false)
    {
        var changed = new List<string>();
        var inProgress = new List<string>();
        var alteringCollected = ObserveAltering(character, obs, nowUtc, characterJustSwitched);
        // 성장 가이드는 캐릭터별이라 캐릭터를 바꾼 직후 첫 관찰(이전 캐릭터 값일 수 있다)은 쓰지 않는다. 같은 내용이면 다시 쓰지 않는다.
        if (!characterJustSwitched && obs.GrowthGuide?.Contents is { Count: > 0 } guideList && !SameGuide(character.GrowthGuide, guideList))
            character.GrowthGuide = guideList.ToList();
        var questsUsable = !characterJustSwitched && obs.Quests != null;   // 캐릭터를 바꾼 직후의 퀘스트 목록은 이전 캐릭터 것일 수 있다
        var raidSuggestions = ObserveRaidTokens(character, catalog, obs, nowUtc, questsUsable);
        if (characterJustSwitched) character.ClearMissionCountsUtc = null;   // 이 캐릭터를 지켜보지 않은 동안 계정 카운터는 다른 캐릭터 몫으로 올랐을 수 있다
        // 클리어 미션 카운터(레이드·어비스·필드 보스): 카운터가 오른 순간은 더 확실한 근거라, 같은 항목의 약한 근거(지역 진입 등)를 덮는다.
        // 애매해서 항목을 못 가린 "클리어 추정"은 이미 있는 근거를 약하게 만들지 않도록 비어 있는 항목에만 담는다.
        if (!characterJustSwitched)
            foreach (var (id, sug) in ObserveClearMissions(character, catalog, obs, nowUtc))
                if (sug.Code == "missionClearedHere" || !raidSuggestions.ContainsKey(id)) raidSuggestions[id] = sug;


        foreach (var def in catalog.Items.Where(d => d.Active))
        {
            var ledger = def.Share == HomeworkShare.Account ? account : character;
            var existing = ledger.Items.GetValueOrDefault(def.Id);

            // 던전 클리어 상태(게임이 직접 알려 줌)가 같은 관찰의 현재 맵과 맞으면 확정 근거다 (요일 던전, 2026-10-12 실측).
            // 캐릭터를 바꾼 직후 관찰은 쓰지 않는다. Cleared는 처치 순간부터 약 17초만 유지된다.
            var clearedHere = !characterJustSwitched && def.ClearOnDungeonCleared && DungeonClearedHere(def, obs, nowUtc);

            // 게임 성장 가이드(콘텐츠 가이드)가 직접 알려 주는 완료·남은 횟수는 확정 근거다 (CLI가 주는 사실은 확정, 2026-11-15).
            // 완료는 수동 설정도 이긴다. 목표가 2 이상인 카운터(소환의 결계·검은 구멍)는 게임의 남은 횟수를 그대로 따른다.
            // 완료가 아닐 때 완료를 되돌리지는 않는다(가이드가 클리어보다 늦게 갱신될 수 있다): 되돌리는 것은 카운터뿐이다.
            if (!characterJustSwitched && GrowthEntry(def, obs) is { IsUnlocked: true } guide)
            {
                var gs = existing ?? new HomeworkItemState();
                if (ApplyGrowthGuide(def, gs, guide, nowUtc))
                {
                    ledger.Items[def.Id] = gs;
                    changed.Add(def.Id);
                }
                if (gs.Completed || def.EffectiveMode == HomeworkMode.ManualStepCounter) continue;   // 게임이 알려 준 항목은 다른 근거를 더 찾지 않는다
                existing = ledger.Items.GetValueOrDefault(def.Id);
            }

            // 퀘스트가 트래커에 다시 보이면 그 주기에 아직 끝내지 않은 것이 확실하다: 자동 완료는 물론 수동 완료도 취소한다
            // (확실한 증거는 수동 설정보다 우선한다. 사라져서 완료로 본 판정이 틀렸거나, 수동 체크가 잘못된 것이다)
            // 단, 던전 클리어 상태로 방금 완료한 것은 제외한다: 클리어 직후에는 나갈 때까지 퀘스트가 아직 목록에 남아 있다.
            if (def.EffectiveMode == HomeworkMode.QuestVanish && questsUsable
                && existing is { Completed: true }
                && !clearedHere && !RecentlyClearedByGame(existing, nowUtc)
                && !(def.RunGoal > 0 && existing.Auto && existing.CompletedAtUtc is { } runDoneAt && nowUtc - runDoneAt < RunReopenGrace)
                && QuestVisible(def, obs, now: nowUtc))
            {
                ledger.Items.Remove(def.Id);
                changed.Add(def.Id);
                existing = null;
            }

            // 던전 클리어: 확정 근거라 수동 미완료도 이긴다 — 단, 수동 설정 뒤에 생긴 클리어일 때만.
            // Cleared는 최대 약 17초 유지되므로 수동 설정이 ClearedStaleWindow 안이면 그 전부터 이어진 클리어일 수 있어 수동을 존중한다.
            if (clearedHere && existing is not { Completed: true }
                && !(existing is { ManualOverride: true, ManualAtUtc: { } manualAt } && nowUtc - manualAt < ClearedStaleWindow))
            {
                var cleared = existing ?? new HomeworkItemState();
                cleared.ManualOverride = false;
                cleared.ManualAtUtc = null;
                Complete(cleared, def.Goal, DungeonClearedEvidence, nowUtc);
                ledger.Items[def.Id] = cleared;
                changed.Add(def.Id);
                continue;
            }

            if (existing?.Completed != true && (def.HasProgressSignal && IsProgressSignal(def, obs) || InEntryAreaOnly(def, obs))) inProgress.Add(def.Id);
            // 수동으로 "미완료"로 둔 레이드에 클리어 증거(보상 증가)가 보이면 완료로 바꾸지는 않고 "완료 제안"만 남긴다.
            // 수동 설정을 존중하되, 놓친 클리어를 알려 준다 (제안은 수동 설정·완료·리셋 때 사라진다).
            if (existing is { ManualOverride: true, Completed: false } manual && raidSuggestions.TryGetValue(def.Id, out var manualSuggestion))
            {
                // 확실한 증거(증표 증가, 수동 설정 뒤의 새 레이드 지역 진입)는 수동 미완료보다 우선한다
                if (AutoCompletes(def, manualSuggestion) && IsDefinitiveOverManual(def, manual, manualSuggestion, character))
                {
                    var evidence = EvidenceOf(manualSuggestion);
                    manual.ManualOverride = false;
                    Complete(manual, def.Goal, evidence, nowUtc);
                    changed.Add(def.Id);
                    continue;
                }
                if (manual.Suggestion != manualSuggestion)
                {
                    manual.Suggestion = manualSuggestion;
                    changed.Add(def.Id);
                }
                continue;
            }
            // 더는 필요 없는 "클리어 추정"은 치운다: 같은 묶음의 항목이 끝났거나(어느 것인지 확인됨),
            // 지켜보던 중에 오른 것이 아닌 옛 추정(From >= To: 주간 미션이 캐릭터별이라고 잘못 알던 때 처음 본 값으로 만든 것)이다.
            if (existing?.Suggestion is { Code: "missionCleared" } old && !raidSuggestions.ContainsKey(def.Id)
                && (old.From >= old.To || GroupResolved(def, catalog, character)))
            {
                existing.Suggestion = null;
                changed.Add(def.Id);
            }
            if (existing is { ManualOverride: true } or { Completed: true }) continue;

            // 빈 상태를 미리 만들지 않는다. 값이 바뀔 때만 장부에 넣는다.
            var st = existing ?? new HomeworkItemState();
            var before = (st.Completed, st.Count, st.Goal, st.Suggestion, st.Evidence, st.RemainingCount);
            switch (def.EffectiveMode)
            {
                case HomeworkMode.DirectMission:
                    EvaluateDirectMission(def, st, obs, nowUtc);
                    break;
                case HomeworkMode.MissionTotal:
                    EvaluateMissionTotal(def, st, obs, nowUtc);
                    break;
                case HomeworkMode.QuestAllObjectives:
                    EvaluateQuest(def, st, obs, nowUtc);
                    break;
                case HomeworkMode.AlteringCollected when alteringCollected != null:
                    Complete(st, def.Goal, alteringCollected, nowUtc);
                    break;
                case HomeworkMode.QuestVanish when questsUsable:
                    EvaluateQuestVanish(def, st, character, obs, nowUtc);
                    break;
                case HomeworkMode.QuestSuffix when questsUsable:
                    EvaluateQuestSuffix(def, st, obs, nowUtc);
                    break;
            }
            if (!st.Completed && def.RunGoal > 0)
            {
                CountRun(def, st, character, raidSuggestions.GetValueOrDefault(def.Id), nowUtc);
            }
            else if (!st.Completed && raidSuggestions.TryGetValue(def.Id, out var suggestion))
            {
                if (AutoCompletes(def, suggestion)) Complete(st, def.Goal, EvidenceOf(suggestion), nowUtc);
                else st.Suggestion = suggestion;
            }
            if ((st.Completed, st.Count, st.Goal, st.Suggestion, st.Evidence, st.RemainingCount) != before)
            {
                ledger.Items[def.Id] = st;
                changed.Add(def.Id);
            }
        }
        return new HomeworkEvaluation(changed, inProgress);
    }

    private static bool SameGuide(List<GrowthGuideItem>? a, List<GrowthGuideItem> b) => a != null && a.Count == b.Count && a.Zip(b).All(p => p.First == p.Second);

    /// <summary>
    /// 이 캐릭터에서 이 숙제가 잠겨 있으면 게임의 안내 문구를, 아니면 null. 성장 가이드를 읽은 적이 없으면 null(모르는 것은 잠겼다고 하지 않는다).
    /// </summary>
    public static string? GrowthLockHint(HomeworkDefinition def, IReadOnlyList<GrowthGuideItem>? guide)
    {
        var target = def.GrowthLockTarget ?? def.GrowthTarget;
        if (target == null || guide is not { Count: > 0 }) return null;
        var day = def.GrowthDay ?? (def.Period == HomeworkPeriod.Daily ? "Daily" : "Weekly");
        var e = guide.FirstOrDefault(g => string.Equals(g.Target, target, StringComparison.Ordinal) && string.Equals(g.DayType, day, StringComparison.OrdinalIgnoreCase));
        if (e == null) return def.GrowthLockWhenAbsent ? "아직 열리지 않았어요" : null;
        return e.IsUnlocked ? null : (string.IsNullOrWhiteSpace(e.UnlockHint) ? "아직 열리지 않았어요" : e.UnlockHint);
    }

    /// <summary>이 숙제에 해당하는 성장 가이드 항목 (없거나 가이드를 못 읽었으면 null)</summary>
    internal static GrowthGuideItem? GrowthEntry(HomeworkDefinition def, HomeworkObservation obs)
    {
        if (def.GrowthTarget is not { Length: > 0 } target || obs.GrowthGuide?.Contents is not { } list) return null;
        var day = def.GrowthDay ?? (def.Period == HomeworkPeriod.Daily ? "Daily" : "Weekly");
        return list.FirstOrDefault(g => string.Equals(g.Target, target, StringComparison.Ordinal) && string.Equals(g.DayType, day, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>성장 가이드 값을 항목에 반영한다. 바뀌었으면 true.</summary>
    internal static bool ApplyGrowthGuide(HomeworkDefinition def, HomeworkItemState st, GrowthGuideItem g, DateTimeOffset now)
    {
        var goal = Math.Max(1, def.Goal);
        var done = g.IsCompleted || (goal > 1 && g.RemainingCount == 0);
        if (done)
        {
            if (st.Completed && st.Count >= goal && !st.ManualOverride) return false;
            st.ManualOverride = false;
            st.ManualAtUtc = null;
            Complete(st, goal, "성장 가이드(게임): 완료", now);
            return true;
        }
        if (goal > 1 && g.RemainingCount is { } left)
        {
            var count = Math.Clamp(goal - left, 0, goal);
            if (!st.Completed && st.Count == count && st.Goal == goal && !st.ManualOverride) return false;
            st.Completed = false;
            st.Auto = true;
            st.CompletedAtUtc = null;
            st.ManualOverride = false;
            st.ManualAtUtc = null;
            st.Count = count;
            st.Goal = goal;
            st.RemainingCount = left;
            st.Suggestion = null;
            st.Evidence = $"성장 가이드(게임): {count}/{goal}";
            return true;
        }
        return false;
    }

    private static IReadOnlyList<MissionItem>? MissionsFor(HomeworkDefinition def, HomeworkObservation obs) =>
        def.Period == HomeworkPeriod.Daily ? obs.DailyMissions : obs.WeeklyMissions;

    private static string PeriodName(HomeworkDefinition def) => def.Period == HomeworkPeriod.Daily ? "일일" : "주간";

    private static void EvaluateDirectMission(HomeworkDefinition def, HomeworkItemState st, HomeworkObservation obs, DateTimeOffset now)
    {
        var titles = def.MissionTitles.Select(HomeworkText.Normalize).ToHashSet();
        var m = MissionsFor(def, obs)?.FirstOrDefault(x => titles.Contains(HomeworkText.Normalize(x.Title)));
        if (m == null) return;

        // 목표 수가 0으로 오면 진행도는 근거가 아니다. 게임의 완료 표시(IsCompleted)만 믿는다.
        var goal = m.GoalCount > 0 ? m.GoalCount : def.Goal;
        st.Goal = goal;
        st.Count = Math.Clamp(m.CurrentCount, 0, goal);
        var done = m.IsCompleted || (m.GoalCount > 0 && m.CurrentCount >= m.GoalCount);
        if (done) Complete(st, goal, $"{PeriodName(def)} 미션 '{m.Title}' {Math.Min(Math.Max(m.CurrentCount, m.IsCompleted ? goal : 0), goal)}/{goal}", now);
    }

    private static void EvaluateMissionTotal(HomeworkDefinition def, HomeworkItemState st, HomeworkObservation obs, DateTimeOffset now)
    {
        var list = MissionsFor(def, obs);
        if (list == null || list.Count == 0) return;
        var done = list.Count(x => x.IsCompleted || (x.GoalCount > 0 && x.CurrentCount >= x.GoalCount));
        st.Goal = list.Count;
        st.Count = done;
        if (done == list.Count) Complete(st, done, $"{PeriodName(def)} 미션 {done}/{list.Count} 완료", now);
    }

    private static void EvaluateQuest(HomeworkDefinition def, HomeworkItemState st, HomeworkObservation obs, DateTimeOffset now)
    {
        if (obs.Quests == null) return;
        var titles = def.QuestTitles.Select(HomeworkText.Normalize).ToHashSet();
        var q = obs.Quests.FirstOrDefault(x =>
            titles.Contains(HomeworkText.Normalize(x.QuestTitle)) &&
            x.Objectives is { Count: > 0 } objs && objs.All(o => o.IsCompleted));   // H-2: 목표 0개·일부 완료는 근거 아님
        if (q != null) Complete(st, def.Goal, $"퀘스트 '{HomeworkText.StripTags(q.QuestTitle)}' 목표 {q.Objectives!.Count}개 모두 완료", now);
    }


    public const string DungeonClearedEvidence = "던전 클리어(게임 상태)";

    /// <summary>Cleared 상태의 최대 유지 시간(약 17초)에 여유를 둔 값. 수동 설정이 이보다 최근이면 그 전의 클리어일 수 있다.</summary>
    public static readonly TimeSpan ClearedStaleWindow = TimeSpan.FromSeconds(25);

    /// <summary>클리어 상태로 완료한 직후, 나가기 전까지 퀘스트가 아직 보이는 동안은 완료를 취소하지 않는다. 이 시간이 지나도 보이면 취소(오탐 회복).</summary>
    public static readonly TimeSpan ClearedQuestGrace = TimeSpan.FromMinutes(2);

    /// <summary>여러 번 깨는 콘텐츠가 목표 횟수로 완료된 직후, 목록에 잠깐 남은 퀘스트로 되돌리지 않는 시간.</summary>
    private static readonly TimeSpan RunReopenGrace = TimeSpan.FromMinutes(3);

    /// <summary>
    /// 여러 번 깨는 콘텐츠(RunGoal, 뱅가드)의 이번 주 횟수: 전용 지역 안 보상이 들어오면 +1, 목표에 닿으면 완료. 퀘스트가 보일 때의 N/M은 EvaluateQuestVanish가 맞춰 둔다.
    /// </summary>
    private static void CountRun(HomeworkDefinition def, HomeworkItemState st, HomeworkLedger character, HomeworkSuggestion? reward, DateTimeOffset now)
    {
        var counts = character.RunCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var n = counts.GetValueOrDefault(def.Id);
        if (reward is { Code: "areaReward" })
        {
            n = Math.Min(def.RunGoal, n + 1);
            counts[def.Id] = n;
            st.Evidence = $"전용 지역 안에서 {reward.Item} +{reward.To - reward.From:N0}: 클리어 관찰, 이번 주 {n}/{def.RunGoal}";
        }
        if (!counts.ContainsKey(def.Id)) return;
        if (n >= def.RunGoal)
        {
            st.RemainingCount = null;
            Complete(st, def.Goal, $"이번 주 {def.Title} {Math.Min(n, def.RunGoal)}/{def.RunGoal} 클리어", now);   // 3회 시절에 쌓인 값(2/1)이 그대로 보이지 않게
            return;
        }
        st.RemainingCount = def.RunGoal > 1 ? def.RunGoal - n : null;   // 1회짜리는 "남은 1회"를 보이지 않는다(완료/미완료만)
    }

    private static bool RecentlyClearedByGame(HomeworkItemState st, DateTimeOffset now) =>
        st.Evidence == DungeonClearedEvidence && st.CompletedAtUtc is { } at && now - at < ClearedQuestGrace;

    /// <summary>같은 관찰에서 던전 상태가 Cleared이고 현재 맵이 이 항목의 지역(접두어·이름)이면 true. 활동·맵이 없으면 false.</summary>
    private static bool DungeonClearedHere(HomeworkDefinition def, HomeworkObservation obs, DateTimeOffset nowUtc)
    {
        if (obs.Activity?.Dungeon is not { IsCleared: true }) return false;
        var space = HomeworkText.Normalize(obs.Environment?.GameSpaceDisplayName);
        if (space.Length == 0) return false;
        // 요일마다 입장할 수 있는 던전이 정해져 있다 (월·목 빛나는 동굴, 화·금 눈부신 저택, 수·토 찬란한 유적): 오늘 것이 아닌 던전의 클리어는 요일 던전이 아니다.
        var prefixes = def.DaySpaceNamePrefixes.Count > 0
            ? def.DaySpaceNamePrefixes.GetValueOrDefault(DayKeys[(int)KstClock.GameDayOfWeek(nowUtc)]) ?? new List<string>()
            : def.SpaceNamePrefixes;
        return prefixes.Any(p => space.StartsWith(HomeworkText.Normalize(p), StringComparison.Ordinal))
            || def.SpaceNames.Any(n => space == HomeworkText.Normalize(n));
    }

    public const string VanishEvidence = "퀘스트가 목록에서 사라짐";

    /// <summary>
    /// 끊김 없는 관찰이 필요한 퀘스트(뱅가드 브리치)가 "목록에 없음"으로 계속 보여야 완료로 확정하는 시간.
    /// 로딩·맵 이동·캐릭터 정보가 비는 순간에는 퀘스트 목록이 일부만 와서 한 번의 관찰만으로는 완료로 오인한다 (2026-10-12 실측).
    /// </summary>
    public static readonly TimeSpan VanishConfirmWindow = TimeSpan.FromSeconds(30);

    private static readonly string[] DayKeys = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

    /// <summary>오늘(게임 기준 06:00 경계) 트래커에 있어야 할 퀘스트 이름들 (정규화). 요일별 이름이 없으면 QuestTitles.</summary>
    private static HashSet<string> TodayQuestTitles(HomeworkDefinition def, DateTimeOffset nowUtc)
    {
        var titles = def.DayQuestTitles.Count > 0
            ? def.DayQuestTitles.GetValueOrDefault(DayKeys[(int)KstClock.GameDayOfWeek(nowUtc)]) ?? new List<string>()
            : def.QuestTitles;
        return titles.Select(HomeworkText.Normalize).ToHashSet();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\(\d+\)$")]
    private static partial System.Text.RegularExpressions.Regex RemainingSuffix();

    [System.Text.RegularExpressions.GeneratedRegex(@"\((\d+)\)\s*$")]
    private static partial System.Text.RegularExpressions.Regex RemainingSuffixCapture();

    private static bool MatchesQuest(HomeworkDefinition def, HashSet<string> titles, string? questTitle)
    {
        var n = HomeworkText.Normalize(questTitle);
        // 남은 횟수를 이름 뒤 "(N)"으로 보여 주는 퀘스트(뱅가드 브리치): 횟수는 이름의 일부가 아니다. "(3)"이 붙어도 같은 퀘스트다
        if (def.ShowRemaining) n = RemainingSuffix().Replace(n, "");
        return n.Length > 0 && (def.TitleContains ? titles.Any(t => n.Contains(t, StringComparison.Ordinal)) : titles.Contains(n));
    }

    private static bool QuestVisible(HomeworkDefinition def, HomeworkObservation obs, DateTimeOffset now)
    {
        var titles = TodayQuestTitles(def, now);
        return obs.Quests?.Any(q => MatchesQuest(def, titles, q.QuestTitle)) == true;
    }

    /// <summary>
    /// 요일 던전처럼 하루 한 번 받는 퀘스트: 트래커에 보이면 미완료가 확정이고(이 주기에 본 시각을 남김),
    /// 이번 주기에 본 적이 있는데 지금 목록에서 사라졌으면 완료로 본다. 한 번도 못 봤으면 판단하지 않는다.
    /// 빈 목록은 조회가 덜 된 것일 수 있어 "사라짐"의 근거로 쓰지 않는다.
    /// </summary>
    private static void EvaluateQuestVanish(HomeworkDefinition def, HomeworkItemState st, HomeworkLedger character, HomeworkObservation obs, DateTimeOffset now)
    {
        var titles = TodayQuestTitles(def, now);
        var sightings = character.QuestSightings ??= new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var cycleStart = KstClock.LastReset(def.Period, now);
        var visible = obs.Quests!.FirstOrDefault(q => MatchesQuest(def, titles, q.QuestTitle));
        var broken = character.QuestContinuityBroken?.Contains(def.Id) == true;
        // 미완료 확인 전용 이름(요일 던전 소탕): 근거만 남기고, 목격 기록은 하지 않는다(사라짐 → 완료 판정을 무장하지 않는다)
        var sightOnly = def.SightOnlyQuestTitles.Select(HomeworkText.Normalize).ToHashSet();
        if (visible == null && obs.Quests!.FirstOrDefault(q => sightOnly.Contains(HomeworkText.Normalize(q.QuestTitle))) is { } hint)
        {
            st.Evidence = $"추천 활동 '{HomeworkText.StripTags(hint.QuestTitle)}'이 목록에 있음: 이번 주기 미완료";
            return;
        }
        if (visible != null)
        {
            if (!sightings.TryGetValue(def.Id, out var at) || at < cycleStart) sightings[def.Id] = now;   // 주기당 한 번만 기록(저장 횟수를 줄인다)
            if (broken) character.QuestContinuityBroken!.Remove(def.Id);   // 다시 보인다: 여기서부터 끊김 없이 지켜본다
            character.QuestVanishPending?.Remove(def.Id);                  // 잠깐 안 보였을 뿐: 사라짐 확인을 처음부터 다시 한다
            st.RemainingCount = def.ShowRemaining && def.RunGoal != 1 ? RemainingOf(visible) : null;
            st.Evidence = $"퀘스트 '{HomeworkText.StripTags(visible.QuestTitle)}'가 아직 목록에 있음: 이번 주기 미완료" + (st.RemainingCount is { } r ? $" (남은 {r}회)" : "");
            if (def.RunGoal > 0 && st.RemainingCount is { } left)   // 게임이 보여 주는 N/M이 이번 주 횟수의 정답이다
                (character.RunCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))[def.Id] = Math.Max(0, def.RunGoal - left);
            return;
        }
        if (def.RunGoal > 0) return;   // 안 보이는 것은 근거가 아니다(뱅가드 퀘스트는 다른 퀘스트에 따라 보였다 안 보였다 한다)
        if (obs.Quests!.Count == 0) return;
        if (!sightings.TryGetValue(def.Id, out var seen) || seen < cycleStart) return;
        // 공간 이름이 비어 있으면 로딩·맵 이동 중이다: 이때의 퀘스트 목록은 믿지 않는다 (조회에 실패한 null과는 구분한다)
        if (def.Continuous && obs.Environment is { } env && string.IsNullOrWhiteSpace(env.GameSpaceDisplayName)) return;
        if (def.Continuous && broken)
        {
            // 캐릭터를 바꾼 사이에 사라졌다: 그 사이 무슨 일인지 모른다. 완료로 보지 않고 확인했던 값도 거둔다(미확인으로 돌아간다)
            sightings.Remove(def.Id);
            character.QuestContinuityBroken!.Remove(def.Id);
            st.Evidence = null;
            st.RemainingCount = null;
            return;
        }
        if (def.Continuous)
        {
            // 처음 안 보인 순간은 완료가 아니라 "확인 대기"다. 같은 주기에 일정 시간 뒤에도 계속 없을 때만 완료로 확정한다.
            var pending = character.QuestVanishPending ??= new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
            if (!pending.TryGetValue(def.Id, out var since) || since < cycleStart || since < seen)
            {
                pending[def.Id] = now;
                return;
            }
            if (now - since < VanishConfirmWindow) return;
            pending.Remove(def.Id);
        }
        Complete(st, def.Goal, $"{VanishEvidence}: 이번 주기에 보였다가 사라져 완료로 판단", now);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(\d+)\s*/\s*(\d+)")]
    private static partial System.Text.RegularExpressions.Regex Progress();

    /// <summary>
    /// 퀘스트에 남은 횟수가 있으면 읽는다: 이름 뒤 "(N)"이 가장 우선(뱅가드 브리치는 이름 뒤 숫자가 남은 횟수다, 승운 확인 2026-10-11).
    /// 없으면 목표마다 (Goal − Count)의 합, 그것도 없으면 이름의 "N/M"(M − N). 모르면 null.
    /// 완료한 목표(IsCompleted)는 0으로 센다.
    /// </summary>
    internal static int? RemainingOf(QuestItem q)
    {
        var suffix = RemainingSuffixCapture().Match(HomeworkText.StripTags(q.QuestTitle));
        if (suffix.Success && int.Parse(suffix.Groups[1].Value) is > 0 and var left) return left;
        if (q.Objectives is { Count: > 0 } objs && objs.Any(o => o.Goal is > 0))
        {
            var sum = objs.Where(o => o.Goal is > 0).Sum(o => o.IsCompleted ? 0 : Math.Max(0, o.Goal!.Value - Math.Max(0, o.Count ?? 0)));
            return sum > 0 ? sum : null;
        }
        var m = Progress().Match(HomeworkText.StripTags(q.QuestTitle));
        if (m.Success && int.Parse(m.Groups[2].Value) is var total && int.Parse(m.Groups[1].Value) is var done && total > done) return total - done;
        return null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\((\d+)\)$")]
    private static partial System.Text.RegularExpressions.Regex SuffixCount();

    /// <summary>
    /// 주간 목표 정기 의뢰: 클리어하면 퀘스트 이름 뒤에 "(1)", "(2)"…가 붙는다 (승운 확인 2026-10-02).
    /// 접미사가 있으면 이번 주기에 클리어했고, 이름만 보이면 아직 클리어 전이다. 이름이 목록에 없으면 판단하지 않는다.
    /// </summary>
    private static void EvaluateQuestSuffix(HomeworkDefinition def, HomeworkItemState st, HomeworkObservation obs, DateTimeOffset now)
    {
        var baseTitle = HomeworkText.Normalize(def.QuestTitles[0]);
        foreach (var q in obs.Quests!)
        {
            var n = HomeworkText.Normalize(q.QuestTitle);
            if (n == baseTitle)
            {
                var done = q.Objectives?.Count(o => o.IsCompleted) ?? 0;
                var total = q.Objectives?.Count ?? 0;
                st.Evidence = total > 0 ? $"이름 뒤 (1) 표시가 없음: 이번 주 아직 클리어 전 (목표 {done}/{total})" : "이름 뒤 (1) 표시가 없음: 이번 주 아직 클리어 전";
                return;
            }
            if (!n.StartsWith(baseTitle, StringComparison.Ordinal)) continue;
            var m = SuffixCount().Match(n[baseTitle.Length..]);
            if (m.Success && int.Parse(m.Groups[1].Value) >= 1)
            {
                Complete(st, def.Goal, $"퀘스트 이름에 ({m.Groups[1].Value}) 표시: 이번 주기 클리어", now);
                return;
            }
        }
    }

    /// <summary>
    /// 가공 수거 관찰 (FR-HW-05, H-4). 수거 근거 문구를 돌려주고, 근거가 없으면 null.
    /// 캐릭터 장부에 (시설|작업명)별 완료 작업 수를 남겨 다음 조회와 비교한다. 조회에 실패했으면 아무것도 바꾸지 않는다.
    /// </summary>
    private static string? ObserveAltering(HomeworkLedger character, HomeworkObservation obs, DateTimeOffset now, bool characterJustSwitched)
    {
        if (obs.CollectedByApp) return "앱에서 가공물 수거 성공";

        // 조회 실패(null)·빈 응답은 비교에도 기록에도 쓰지 않는다 (H-4, FR-HW-05)
        if (obs.AlteringWorks?.Works is not { Count: > 0 }) return null;

        var current = obs.AlteringWorks.Works
            .Where(w => w.IsCompleted || w.RemainingSeconds <= 0)
            .GroupBy(w => $"{w.FacilityName}|{w.DisplayName}")
            .ToDictionary(g => g.Key, g => g.Count());

        string? evidence = null;
        var prev = character.AlteringDoneCounts;
        var fresh = !characterJustSwitched && character.AlteringObservedUtc is { } at && now - at <= AlteringCompareWindow;
        if (prev != null && fresh)
        {
            var dropped = prev.FirstOrDefault(kv => current.GetValueOrDefault(kv.Key) < kv.Value);
            if (dropped.Key != null) evidence = $"완료된 가공물 '{dropped.Key.Split('|')[^1]}' 수거 관찰";
        }

        character.AlteringDoneCounts = current;
        character.AlteringObservedUtc = now;
        return evidence;
    }

    /// <summary>
    /// 레이드 클리어 관찰 (FR-HW-17, H-11). 레이드는 캐릭터마다 종류별로 주 1회만 보상을 주므로, 보상 재화·아이템이
    /// 늘어난 것을 그 레이드의 "이번 주 클리어"로 본다 (카브락 = 원정의 증거 18개 이상, 에이렐 = 선율을 품은 하프 조각, 화이트 서큐버스 = 꿈을 비추는 거울 조각).
    /// - 비교 기준은 그 캐릭터가 마지막으로 관찰된 수량이다(캐릭터별 장부, 이번 주간 주기 안). 다른 캐릭터를 사이에 관찰했어도,
    ///   그 캐릭터로 다시 접속했을 때 늘어난 것을 알아챈다(호출하는 쪽이 접속 직후엔 재화·아이템을 새로 읽는다).
    /// - 조회 실패(null)·빈 목록은 비교에도 기록에도 쓰지 않는다.
    /// - 늘어난 양이 TokenMinIncrease보다 작으면 클리어가 아니다(다른 레이드의 주말 보너스 +2 등). 기준값은 그대로 갱신한다.
    /// - 목록에 없는 재화는 0으로 보지 않는다(불완전한 목록 → 0 → 늘어남 오탐 방지). 아이템은 0개면 목록에서 사라지므로
    ///   목록을 받았다면 없는 것은 0개다.
    /// </summary>
    private static Dictionary<string, HomeworkSuggestion> ObserveRaidTokens(
        HomeworkLedger character, HomeworkCatalog catalog, HomeworkObservation obs, DateTimeOffset now, bool questsUsable)
    {
        var result = new Dictionary<string, HomeworkSuggestion>(StringComparer.OrdinalIgnoreCase);

        // 클리어 퀘스트(필드 보스 "영역 나가기")가 트래커에 보이면 클리어다. 비어 있는 목록은 판단하지 않는다(처치 순간 잠깐 빈다).
        if (questsUsable && obs.Quests is { Count: > 0 })
        {
            var since = character.RaidAreaSince ??= new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in catalog.Items.Where(d => d.Active && d.ClearQuestTitles.Count > 0))
            {
                var titles = d.ClearQuestTitles.Select(HomeworkText.Normalize).ToHashSet();
                var seen = obs.Quests.FirstOrDefault(q => titles.Contains(HomeworkText.Normalize(q.QuestTitle)));
                if (seen != null)
                {
                    since.TryAdd(ClearKey(d), now);
                    result[d.Id] = new HomeworkSuggestion("bossCleared", HomeworkText.StripTags(seen.QuestTitle), 0, 0);
                }
                else since.Remove(ClearKey(d));
            }
        }

        // 레이드 지역에 들어와 있으면 가장 우선인 근거다 (보상 비교와 무관하게 판정한다)
        var space = HomeworkText.Normalize(obs.Environment?.GameSpaceDisplayName);
        var wasIn = new HashSet<string>(character.RaidAreaSince?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);   // 방금까지 있었던 지역
        if (space.Length > 0)
        {
            var since = character.RaidAreaSince ??= new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in catalog.Items.Where(d => d.Active && (d.EntrySpaceNames.Count > 0 || d.EntrySpacePrefixes.Count > 0 || d.TokenRequiresSpace)))
            {
                var areas = d.EntrySpaceNames.Count > 0 || d.EntrySpacePrefixes.Count > 0 ? d.EntrySpaceNames : d.SpaceNames;
                if (areas.Any(n => HomeworkText.Normalize(n) == space) || d.EntrySpacePrefixes.Any(p => space.StartsWith(HomeworkText.Normalize(p), StringComparison.Ordinal)))
                {
                    since.TryAdd(d.Id, now);
                    if (d.EntrySpaceNames.Count > 0 && d.EntryCompletes)
                        result[d.Id] =new HomeworkSuggestion("raidMapEntered", HomeworkText.StripTags(obs.Environment!.GameSpaceDisplayName), 0, 0);
                }
                else since.Remove(d.Id);
            }
        }

        var haveCurrencies = obs.Currencies is { Count: > 0 };
        var haveItems = obs.Items is { Count: > 0 };
        if (!haveCurrencies && !haveItems) return result;

        // 보상 재화(TokenCurrency)와 전용 지역 안 보상(AreaRewards)을 같이 비교한다
        var entries = catalog.Items.Where(d => d.Active && !string.IsNullOrWhiteSpace(d.TokenCurrency))
            .Select(d => (Def: d, Name: d.TokenCurrency!, IsItem: d.TokenIsItem, Min: (long)Math.Max(1, d.TokenMinIncrease), NeedsArea: d.TokenRequiresSpace, AreaReward: false))
            .Concat(catalog.Items.Where(d => d.Active && (d.EntrySpaceNames.Count > 0 || d.EntrySpacePrefixes.Count > 0))
                .SelectMany(d => d.AreaRewards.Select(r => (Def: d, Name: r.Key, IsItem: false, Min: Math.Max(1L, r.Value), NeedsArea: true, AreaReward: true))))
            .ToList();
        if (entries.Count == 0) return result;

        var currencies = haveCurrencies
            ? obs.Currencies!.GroupBy(c => HomeworkText.Normalize(c.DisplayName)).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount))
            : new Dictionary<string, long>();
        var items = haveItems
            ? obs.Items!.Where(i => string.Equals(i.Location, "inventory", StringComparison.OrdinalIgnoreCase))
                .GroupBy(i => HomeworkText.Normalize(i.DisplayName)).ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Count))
            : new Dictionary<string, long>();

        var samePeriod = character.RaidTokensObservedUtc is { } at && at >= character.LastWeeklyResetUtc;
        var prev = samePeriod ? character.RaidTokens : null;   // 지난 주기 기준값은 버린다
        var next = new Dictionary<string, long>(prev ?? new Dictionary<string, long>());
        var observed = false;
        var recentBase = samePeriod && now - character.RaidTokensObservedUtc!.Value <= AreaRewardFresh;   // 지역 보상은 직전 관찰과의 차이만 본다
        foreach (var (d, name, isItem, min, needsArea, areaReward) in entries)
        {
            var key = HomeworkText.Normalize(name);
            long now_;
            if (isItem)
            {
                if (!haveItems) continue;
                now_ = items.GetValueOrDefault(key);
            }
            else if (!currencies.TryGetValue(key, out now_)) continue;   // 목록에 없음: 기준값 유지, 비교 안 함
            observed = true;
            var inArea = !needsArea || wasIn.Contains(d.Id) || character.RaidAreaSince?.ContainsKey(d.Id) == true;
            if (inArea && (!areaReward || recentBase) && prev != null && prev.TryGetValue(key, out var before) && now_ - before >= min && !result.ContainsKey(d.Id))
                result[d.Id] = new HomeworkSuggestion(areaReward ? "areaReward" : "raidTokenIncreased", name, before, now_);
            next[key] = now_;
        }
        if (!observed) return result;

        character.RaidTokens = next;
        character.RaidTokensObservedUtc = now;
        return result;
    }

    /// <summary>주간 미션 카운터를 이 간격 안에 다시 읽었을 때만 "지켜보던 중 오름"으로 본다(관찰 주기 20초~1분에 여유. 같은 계정의 다른 캐릭터로 바꿔 레이드를 깨고 돌아오는 데는 이보다 오래 걸린다).</summary>
    private static readonly TimeSpan MissionWatchGap = TimeSpan.FromMinutes(10);

    /// <summary>지역 보상(AreaRewards) 비교에 쓰는 직전 관찰의 최대 나이.</summary>
    private static readonly TimeSpan AreaRewardFresh = TimeSpan.FromMinutes(5);

    /// <summary>같은 클리어 묶음(ClearGroup)의 항목이 이 캐릭터에 하나라도 끝났다.</summary>
    private static bool GroupResolved(HomeworkDefinition def, HomeworkCatalog catalog, HomeworkLedger character) =>
        def.ClearGroup is { } g && catalog.Items.Any(d => string.Equals(d.ClearGroup, g, StringComparison.OrdinalIgnoreCase)
            && character.Items.TryGetValue(d.Id, out var st) && st.Completed);

    /// <summary>카운터가 오른 순간에 그 항목의 지역에 있거나 최근에 보스와 싸웠으면 그 항목이라고 보는 시간.</summary>
    private static readonly TimeSpan ClearSignalRecent = TimeSpan.FromMinutes(20);

    private static bool AreaNow(HomeworkDefinition d, string normalizedSpace) =>
        normalizedSpace.Length > 0 && d.EntrySpaceNames.Concat(d.SpaceNames).Any(n => HomeworkText.Normalize(n) == normalizedSpace);

    /// <summary>
    /// 클리어 미션 카운터(레이드·어비스·필드 보스, 2026-10-22 승운 승인): 주간 미션은 캐릭터별이라 그 캐릭터의 "레이드 1회 토벌 N/1" 같은 카운터가
    /// 오른 순간이 그 캐릭터의 클리어다. 미션은 어느 항목인지 알려 주지 않으므로 다음 순서로 가린다.
    /// ① 오른 순간 그 항목의 전용 지역에 있다 ② 최근 20분 안에 그 항목의 보스와 싸웠거나 지역에 있었다 ③ 남은 후보가 하나뿐이다.
    /// 가려지면 "missionClearedHere"(자동 완료), 못 가리면 아직 끝나지 않은 항목 모두에 "missionCleared"(클리어 추정, 눌러서 확인)를 남긴다.
    /// - 이 묶음의 항목이 이미 하나라도 끝났으면 카운터는 그것으로 설명되므로 아무것도 하지 않는다(레이드·어비스는 미션 목표가 1이라 첫 클리어만 센다. 필드 보스는 택1).
    /// - 지역·보스로 가리는 것은 카운터가 **막 오른** 관찰에서만 한다. 그 뒤 다른 레이드 지역에 들어갔다고 그 레이드를 완료로 보면 안 된다.
    /// - 처음 보는 값(이번 주기의 기준)이 이미 1 이상이면 언제 어디서 깼는지 모르므로 후보가 하나뿐일 때만 가리고 아니면 추정으로 둔다.
    /// - 미션 목록이 없거나 해당 미션이 없으면 아무것도 건드리지 않는다(기준값도 그대로).
    /// </summary>
    private static Dictionary<string, HomeworkSuggestion> ObserveClearMissions(HomeworkLedger character, HomeworkCatalog catalog, HomeworkObservation obs, DateTimeOffset now)
    {
        var result = new Dictionary<string, HomeworkSuggestion>(StringComparer.OrdinalIgnoreCase);
        if (obs.WeeklyMissions is not { Count: > 0 } missions || catalog.ClearMissions.Count == 0) return result;

        var space = HomeworkText.Normalize(obs.Environment?.GameSpaceDisplayName);
        var seen = character.ProgressSeen ??= new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var counts = character.ClearMissionCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var readAt = character.ClearMissionCountsUtc ??= new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        foreach (var (group, descriptions) in catalog.ClearMissions)
        {
            var items = catalog.Items.Where(d => d.Active && string.Equals(d.ClearGroup, group, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var d in items)
                if (AreaNow(d, space) || IsProgressSignal(d, obs)) seen[d.Id] = now;   // 지금 지역 안이거나 보스와 싸우는 중

            var wanted = descriptions.Select(HomeworkText.Normalize).ToHashSet();
            var matched = missions.Where(m => wanted.Contains(HomeworkText.Normalize(m.Description))).ToList();
            if (matched.Count == 0) continue;

            var current = matched.Max(m => Math.Clamp(m.CurrentCount, 0, m.GoalCount > 0 ? m.GoalCount : Math.Max(m.CurrentCount, 0)));
            var watching = readAt.TryGetValue(group, out var lastRead) && now - lastRead <= MissionWatchGap;
            readAt[group] = now;
            int? before = watching && counts.TryGetValue(group, out var p) ? p : null;   // 오래전 값과의 차이는 같은 계정의 다른 캐릭터 몫일 수 있다
            counts[group] = current;
            if (current <= 0) continue;
            if (items.Any(d => character.Items.TryGetValue(d.Id, out var st) && st.Completed)) continue;   // 이미 이 묶음의 항목이 끝났다
            if (items.Count == 0) continue;

            var description = HomeworkText.StripTags(matched[0].Description);
            var justRose = before is { } b && current > b;
            if (!justRose) continue;   // 계정 공유 카운터: 처음 본 값이나 지켜보지 않은 동안 오른 값은 이 캐릭터의 근거가 아니다
            HomeworkDefinition? pick = null;
            if (justRose)
            {
                var inArea = items.Where(d => AreaNow(d, space)).ToList();
                if (inArea.Count == 1) pick = inArea[0];
                else if (inArea.Count == 0)
                {
                    var recent = items.Where(d => seen.TryGetValue(d.Id, out var t) && now - t <= ClearSignalRecent).ToList();
                    if (recent.Count == 1) pick = recent[0];
                }
            }
            if (pick == null && items.Count == 1) pick = items[0];

            if (pick != null) result[pick.Id] = new HomeworkSuggestion("missionClearedHere", description, before!.Value, current);
            else foreach (var d in items) result[d.Id] = new HomeworkSuggestion("missionCleared", description, before!.Value, current);
        }
        return result;
    }

    /// <summary>수동 미완료를 이길 확실한 증거인가: 증표 증가는 항상, 지역 진입은 수동 설정 뒤에 새로 들어온 경우만(옛 기록은 시각이 없어 인정).</summary>
    private static bool IsDefinitiveOverManual(HomeworkDefinition def, HomeworkItemState manual, HomeworkSuggestion s, HomeworkLedger character)
    {
        if (!IsPresenceCode(s.Code)) return true;   // 증표·아이템 증가는 늘어난 순간이 새 증거다
        if (manual.ManualAtUtc is not { } at) return true;
        var key = s.Code == "bossCleared" ? ClearKey(def) : def.Id;
        return character.RaidAreaSince?.GetValueOrDefault(key) is { } since && since > at;
    }

    /// <summary>"있는 동안 계속 참"인 증거(지역 안에 있음, 클리어 퀘스트가 보임): 수동 설정 뒤에 새로 생긴 경우만 수동을 이긴다</summary>
    private static bool IsPresenceCode(string code) => code is "raidMapEntered" or "bossCleared";

    private static string ClearKey(HomeworkDefinition d) => "clear:" + d.Id;

    /// <summary>제안이 곧바로 자동 완료가 되는가: 미션 카운터로 항목이 가려졌으면 예, 못 가려 "추정"만 있으면 아니오(수동 확인), 그 밖은 증표·존재 근거 규칙.</summary>
    private static bool AutoCompletes(HomeworkDefinition def, HomeworkSuggestion s) => s.Code switch
    {
        "missionClearedHere" => true,
        "areaReward" => def.RunGoal == 0,   // 여러 번 깨는 콘텐츠(뱅가드)는 횟수만 센다
        "missionCleared" => false,
        _ => def.TokenAuto || IsPresenceCode(s.Code),
    };

    private static string EvidenceOf(HomeworkSuggestion s) => s.Code switch
    {
        "missionClearedHere" => $"주간 미션 '{s.Item}' {s.From}→{s.To} 증가: 이 캐릭터가 클리어",
        "areaReward" => $"전용 지역 안에서 {s.Item} {s.From:N0} → {s.To:N0} (+{s.To - s.From:N0}): 클리어 보상",
        "raidMapEntered" => $"전용 지역 '{s.Item}' 진입",
        "bossCleared" => $"퀘스트 '{s.Item}' 등장: 보스 처치",
        _ => $"{s.Item} {s.From} → {s.To} 증가 관찰",
    };

    /// <summary>진입이 완료가 아닌 항목(레이드)의 전용 지역 안에 있다: 전투 여부와 무관하게 "진행 중"이다.</summary>
    private static bool InEntryAreaOnly(HomeworkDefinition def, HomeworkObservation obs) =>
        !def.EntryCompletes && def.EntrySpaceNames.Count > 0 && AreaNow(def, HomeworkText.Normalize(obs.Environment?.GameSpaceDisplayName));

    private static bool IsProgressSignal(HomeworkDefinition def, HomeworkObservation obs)
    {
        if (obs.Activity?.IsInCombat != true) return false;   // H-10: 자동 사냥만으로는 신호 아님

        var target = HomeworkText.Normalize(obs.Activity.AutoPlayTargetDisplayName);
        if (target.Length > 0 && def.BossNames.Any(b => target.Contains(HomeworkText.Normalize(b)))) return true;

        var space = HomeworkText.Normalize(obs.Environment?.GameSpaceDisplayName);   // H-10: 채널명은 쓰지 않는다
        return space.Length > 0 && def.SpaceNames.Any(s => space == HomeworkText.Normalize(s));
    }

    private static void Complete(HomeworkItemState st, int count, string evidence, DateTimeOffset now)
    {
        st.Goal ??= count;
        st.Completed = true;
        st.Auto = true;
        st.Count = count;
        st.CompletedAtUtc = now;
        st.Evidence = evidence;
        st.Suggestion = null;
        st.RemainingCount = null;
    }
}
