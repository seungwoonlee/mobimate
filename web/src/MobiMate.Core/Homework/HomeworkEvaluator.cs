namespace MobiMate;

/// <summary>한 번의 조회 결과 묶음. 조회에 실패한 항목은 null로 둔다.</summary>
public sealed record HomeworkObservation(
    IReadOnlyList<MissionItem>? DailyMissions = null,
    IReadOnlyList<MissionItem>? WeeklyMissions = null,
    IReadOnlyList<QuestItem>? Quests = null,
    ActivityInfo? Activity = null,
    EnvironmentInfo? Environment = null,
    AlteringWorksResponse? AlteringWorks = null,
    bool CollectedByApp = false);

public sealed record HomeworkEvaluation(IReadOnlyList<string> ChangedIds, IReadOnlyList<string> InProgressIds);

/// <summary>
/// 숙제 판정 (요구사양서 §4.3·§5.11, 상세설계 §2.8). 오탐 0 원칙:
/// - 자동 완료는 확정 증거(정확히 일치하는 1:1 미션, 전체 미션 개수, 모든 목표가 끝난 퀘스트, 수거 관찰)일 때만
/// - 항목마다 판정 방식 하나만 적용 (H-10)
/// - 보스 교전·공간 입장은 "진행 중" 신호일 뿐 완료가 아니다 (H-1)
/// - 수동 설정한 항목은 이번 주기 동안 건드리지 않는다, 완료는 리셋 전까지 유지한다
/// </summary>
public static class HomeworkEvaluator
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

        foreach (var def in catalog.Items)
        {
            var ledger = def.Share == HomeworkShare.Account ? account : character;
            var existing = ledger.Items.GetValueOrDefault(def.Id);

            if (existing?.Completed != true && def.HasProgressSignal && IsProgressSignal(def, obs)) inProgress.Add(def.Id);
            if (existing is { ManualOverride: true } or { Completed: true }) continue;

            // 빈 상태를 미리 만들지 않는다. 값이 바뀔 때만 장부에 넣는다.
            var st = existing ?? new HomeworkItemState();
            var before = (st.Completed, st.Count, st.Goal);
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
            }
            if ((st.Completed, st.Count, st.Goal) != before)
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
    }
}
