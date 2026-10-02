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
    IReadOnlyList<CurrencyItem>? Currencies = null);

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
            if (due) ledger.QuestSightings?.Remove(def.Id);
        }
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
        var raidSuggestions = ObserveRaidTokens(character, catalog, obs, nowUtc, characterJustSwitched);

        // 캐릭터를 바꾼 직후의 퀘스트 목록은 이전 캐릭터 것일 수 있다: 퀘스트 기반 판정은 이번 관찰을 건너뛴다
        var questsUsable = !characterJustSwitched && obs.Quests != null;

        foreach (var def in catalog.Items.Where(d => d.Active))
        {
            var ledger = def.Share == HomeworkShare.Account ? account : character;
            var existing = ledger.Items.GetValueOrDefault(def.Id);

            // 사라져서 완료로 본 퀘스트가 같은 주기에 다시 보이면 사라진 것이 완료가 아니었다(트래커 슬롯 변경 등): 완료를 취소한다
            if (def.EffectiveMode == HomeworkMode.QuestVanish && questsUsable
                && existing is { Completed: true, Auto: true, ManualOverride: false } && existing.Evidence?.StartsWith(VanishEvidence, StringComparison.Ordinal) == true
                && QuestVisible(def, obs, now: nowUtc))
            {
                ledger.Items.Remove(def.Id);
                changed.Add(def.Id);
                existing = null;
            }

            if (existing?.Completed != true && def.HasProgressSignal && IsProgressSignal(def, obs)) inProgress.Add(def.Id);
            if (existing is { ManualOverride: true } or { Completed: true }) continue;

            // 빈 상태를 미리 만들지 않는다. 값이 바뀔 때만 장부에 넣는다.
            var st = existing ?? new HomeworkItemState();
            var before = (st.Completed, st.Count, st.Goal, st.Suggestion, st.Evidence);
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
            if (!st.Completed && raidSuggestions.TryGetValue(def.Id, out var suggestion))
            {
                if (def.TokenAuto) Complete(st, def.Goal, $"{suggestion.Item} {suggestion.From} → {suggestion.To} 증가 관찰", nowUtc);
                else st.Suggestion = suggestion;
            }
            if ((st.Completed, st.Count, st.Goal, st.Suggestion, st.Evidence) != before)
            {
                ledger.Items[def.Id] = st;
                changed.Add(def.Id);
            }
        }
        return new HomeworkEvaluation(changed, inProgress);
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


    public const string VanishEvidence = "퀘스트가 목록에서 사라짐";

    private static readonly string[] DayKeys = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

    /// <summary>오늘(게임 기준 06:00 경계) 트래커에 있어야 할 퀘스트 이름들 (정규화). 요일별 이름이 없으면 QuestTitles.</summary>
    private static HashSet<string> TodayQuestTitles(HomeworkDefinition def, DateTimeOffset nowUtc)
    {
        var titles = def.DayQuestTitles.Count > 0
            ? def.DayQuestTitles.GetValueOrDefault(DayKeys[(int)KstClock.GameDayOfWeek(nowUtc)]) ?? new List<string>()
            : def.QuestTitles;
        return titles.Select(HomeworkText.Normalize).ToHashSet();
    }

    private static bool MatchesQuest(HomeworkDefinition def, HashSet<string> titles, string? questTitle)
    {
        var n = HomeworkText.Normalize(questTitle);
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
        if (visible != null)
        {
            if (!sightings.TryGetValue(def.Id, out var at) || at < cycleStart) sightings[def.Id] = now;   // 주기당 한 번만 기록(저장 횟수를 줄인다)
            st.Evidence = $"퀘스트 '{HomeworkText.StripTags(visible.QuestTitle)}'가 아직 목록에 있음: 이번 주기 미완료";
            return;
        }
        if (obs.Quests!.Count == 0) return;
        if (sightings.TryGetValue(def.Id, out var seen) && seen >= cycleStart)
            Complete(st, def.Goal, $"{VanishEvidence}: 이번 주기에 보였다가 사라져 완료로 판단", now);
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
    /// 레이드 증표 관찰 (FR-HW-17, H-11). 같은 캐릭터를 이어서 관찰했고(사이에 다른 캐릭터 없음), 직전 관찰이 이번 주간 주기 안이며,
    /// 증표 수량이 늘었으면 그 레이드 항목에 제안을 돌려준다. 완료 처리는 하지 않는다.
    /// - 조회 실패(null)·빈 목록은 비교에도 기록에도 쓰지 않는다.
    /// - 목록에 없는 증표는 0으로 보지 않는다: 그 증표의 기준값을 그대로 두고 비교에서 뺀다(불완전한 목록 → 0 → 늘어남 오탐 방지).
    /// - 캐릭터가 바뀐 관찰은 기준값을 지우기만 한다(재화 캐시가 이전 캐릭터 것일 수 있음). 다음 관찰부터 새로 쌓는다.
    /// </summary>
    private static Dictionary<string, HomeworkSuggestion> ObserveRaidTokens(
        HomeworkLedger character, HomeworkCatalog catalog, HomeworkObservation obs, DateTimeOffset now, bool characterJustSwitched)
    {
        var result = new Dictionary<string, HomeworkSuggestion>(StringComparer.OrdinalIgnoreCase);
        if (characterJustSwitched)
        {
            character.RaidTokens = null;
            character.RaidTokensObservedUtc = null;
            return result;
        }
        if (obs.Currencies is not { Count: > 0 }) return result;

        var defs = catalog.Items.Where(d => !string.IsNullOrWhiteSpace(d.TokenCurrency)).ToList();
        if (defs.Count == 0) return result;

        var amounts = obs.Currencies
            .GroupBy(c => HomeworkText.Normalize(c.DisplayName))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        var samePeriod = character.RaidTokensObservedUtc is { } at && at >= character.LastWeeklyResetUtc;
        var prev = samePeriod ? character.RaidTokens : null;   // 지난 주기 기준값은 버린다
        var next = new Dictionary<string, long>(prev ?? new Dictionary<string, long>());
        foreach (var d in defs)
        {
            var key = HomeworkText.Normalize(d.TokenCurrency);
            if (!amounts.TryGetValue(key, out var now_)) continue;   // 목록에 없음: 기준값 유지, 비교 안 함
            if (prev != null && prev.TryGetValue(key, out var before) && now_ > before)
                result[d.Id] = new HomeworkSuggestion("raidTokenIncreased", d.TokenCurrency!, before, now_);
            next[key] = now_;
        }

        character.RaidTokens = next;
        character.RaidTokensObservedUtc = now;
        return result;
    }

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
    }
}
