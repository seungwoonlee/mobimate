using System;
using System.Collections.Generic;
using System.Linq;

namespace MobiMate
{
    public class HomeworkEvaluationContext
    {
        public string CharacterKey { get; set; } = string.Empty;
        public CharacterInfo? Character { get; set; }
        public EnvironmentInfo? Environment { get; set; }
        public ActivityInfo? Activity { get; set; }
        public List<MissionItem>? DailyMissions { get; set; }
        public List<MissionItem>? WeeklyMissions { get; set; }
        public AlteringWorksResponse? AlteringWorks { get; set; }
        public List<QuestItem>? Quests { get; set; }
        public List<CurrencyItem>? Currencies { get; set; }
    }

    public class HomeworkTrackerService
    {
        private readonly HomeworkRepository _repository;

        public HomeworkRepository Repository => _repository;

        public HomeworkTrackerService(HomeworkRepository? repository = null)
        {
            _repository = repository ?? new HomeworkRepository();
        }

        public HomeworkCharacterRecord EvaluateAndSync(HomeworkEvaluationContext ctx, DateTime now)
        {
            var characterKey = !string.IsNullOrWhiteSpace(ctx.CharacterKey)
                ? ctx.CharacterKey
                : (ctx.Character != null ? $"{ctx.Character.RealmName}_{ctx.Character.JobName}" : "Default_Player");

            var record = _repository.GetOrCreateRecord(characterKey, now);

            bool changed = false;

            foreach (var def in _repository.MasterList)
            {
                if (!record.Items.TryGetValue(def.Id, out var state))
                    continue;

                // 이미 완료 상태인 경우
                if (state.IsCompleted)
                    continue;

                bool newlyDone = false;

                switch (def.AutoMode)
                {
                    case AutoDetectMode.DirectMission:
                        if (CheckDirectMission(def, ctx, out int currentCount, out int goalCount, out bool isDone))
                        {
                            state.CurrentCount = currentCount;
                            state.GoalCount = goalCount;
                            if (isDone)
                            {
                                state.IsCompleted = true;
                                state.IsAutoDetected = true;
                                state.CompletedAt = now;
                                changed = true;
                                newlyDone = true;
                            }
                        }
                        break;

                    case AutoDetectMode.AlteringFacility:
                        if (ctx.AlteringWorks != null)
                        {
                            // 완료된 가공물이 있거나 수거 가능한 상태
                            if (ctx.AlteringWorks.CompletedCount > 0 ||
                                (ctx.AlteringWorks.Works != null && ctx.AlteringWorks.Works.Any(w => w.IsDone)))
                            {
                                state.CurrentCount = 1;
                                state.GoalCount = 1;
                                state.IsCompleted = true;
                                state.IsAutoDetected = true;
                                state.CompletedAt = now;
                                changed = true;
                                newlyDone = true;
                            }
                        }
                        break;

                    case AutoDetectMode.EnvironmentCombat:
                        if (CheckEnvironmentOrCombat(def, ctx))
                        {
                            state.CurrentCount = 1;
                            state.GoalCount = 1;
                            state.IsCompleted = true;
                            state.IsAutoDetected = true;
                            state.CompletedAt = now;
                            changed = true;
                            newlyDone = true;
                        }
                        break;

                    case AutoDetectMode.QuestTracker:
                        if (CheckQuestTracker(def, ctx))
                        {
                            state.CurrentCount = 1;
                            state.GoalCount = 1;
                            state.IsCompleted = true;
                            state.IsAutoDetected = true;
                            state.CompletedAt = now;
                            changed = true;
                            newlyDone = true;
                        }
                        break;
                }

                // 공유 풀(SharedPoolId: 예 필드 보스 주간 1회 택1) 동기화
                if (newlyDone && !string.IsNullOrEmpty(def.SharedPoolId))
                {
                    SyncSharedPool(record, def.SharedPoolId, true, true, now);
                }
            }

            if (changed)
            {
                _repository.SaveRecord(record);
            }

            return record;
        }

        private void SyncSharedPool(HomeworkCharacterRecord record, string poolId, bool isCompleted, bool isAuto, DateTime now)
        {
            foreach (var sibling in _repository.MasterList.Where(m => m.SharedPoolId == poolId))
            {
                if (record.Items.TryGetValue(sibling.Id, out var sibState))
                {
                    sibState.IsCompleted = isCompleted;
                    sibState.IsAutoDetected = isAuto;
                    sibState.CompletedAt = isCompleted ? now : null;
                    sibState.CurrentCount = isCompleted ? sibState.GoalCount : 0;
                    sibState.LastUpdated = now;
                }
            }
        }

        private bool CheckDirectMission(HomeworkDefinition def, HomeworkEvaluationContext ctx, out int currentCount, out int goalCount, out bool isDone)
        {
            currentCount = 0;
            goalCount = def.GoalCount;
            isDone = false;

            var missions = def.Period == HomeworkPeriod.Daily
                ? ctx.DailyMissions
                : ctx.WeeklyMissions;

            if (missions == null || missions.Count == 0)
                return false;

            // 주간 전체 미션 완수 특수 체크
            if (def.Id == "weekly_missions_all")
            {
                int completedMissions = missions.Count(m => m.IsCompleted || m.CurrentCount >= m.GoalCount);
                currentCount = completedMissions;
                goalCount = Math.Max(15, missions.Count);
                isDone = completedMissions >= goalCount;
                return true;
            }

            foreach (var m in missions)
            {
                foreach (var kw in def.MatchKeywords)
                {
                    if ((m.Title != null && m.Title.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                        (m.Description != null && m.Description.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    {
                        currentCount = m.CurrentCount;
                        goalCount = m.GoalCount > 0 ? m.GoalCount : def.GoalCount;
                        isDone = m.IsCompleted || currentCount >= goalCount;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool CheckEnvironmentOrCombat(HomeworkDefinition def, HomeworkEvaluationContext ctx)
        {
            // 1. 주간 미션 중에 해당 보스/던전 처치 미션이 있는지 먼저 확인
            if (ctx.WeeklyMissions != null)
            {
                foreach (var m in ctx.WeeklyMissions)
                {
                    foreach (var kw in def.MatchKeywords)
                    {
                        if ((m.Title != null && m.Title.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                            (m.Description != null && m.Description.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (m.IsCompleted || m.CurrentCount >= m.GoalCount)
                                return true;
                        }
                    }
                }
            }

            // 2. 현재 공간/채널 및 교전 타겟 감지
            string spaceName = ctx.Environment?.GameSpaceDisplayName ?? "";
            string channelName = ctx.Environment?.ChannelName ?? "";
            string targetName = ctx.Activity?.AutoPlayTargetDisplayName ?? "";

            bool spaceMatched = def.MatchKeywords.Any(kw =>
                (!string.IsNullOrEmpty(spaceName) && spaceName.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(channelName) && channelName.Contains(kw, StringComparison.OrdinalIgnoreCase)));

            bool targetMatched = !string.IsNullOrEmpty(targetName) &&
                def.MatchKeywords.Any(kw => targetName.Contains(kw, StringComparison.OrdinalIgnoreCase));

            // 보스와 교전 중이거나 해당 보스 전용 공간/채널에 진입하여 교전 중인 경우 감지
            if (targetMatched && (ctx.Activity?.IsInCombat == true || ctx.Activity?.IsAutoPlaying == true))
            {
                return true;
            }

            if (spaceMatched && ctx.Activity?.IsInCombat == true)
            {
                return true;
            }

            return false;
        }

        private bool CheckQuestTracker(HomeworkDefinition def, HomeworkEvaluationContext ctx)
        {
            // 1. 퀘스트 트래커(get_quests) 검사
            if (ctx.Quests != null)
            {
                foreach (var q in ctx.Quests)
                {
                    bool titleMatch = def.MatchKeywords.Any(kw =>
                        q.QuestTitle != null && q.QuestTitle.Contains(kw, StringComparison.OrdinalIgnoreCase));

                    if (titleMatch)
                    {
                        // 모든 목표 완수 또는 퀘스트 완료
                        if (q.Objectives == null || q.Objectives.Count == 0 || q.Objectives.All(o => o.IsCompleted))
                            return true;
                    }
                }
            }

            // 2. 주간 미션 내 레이드 클리어 항목 확인
            if (ctx.WeeklyMissions != null)
            {
                foreach (var m in ctx.WeeklyMissions)
                {
                    foreach (var kw in def.MatchKeywords)
                    {
                        if ((m.Title != null && m.Title.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                            (m.Description != null && m.Description.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (m.IsCompleted || m.CurrentCount >= m.GoalCount)
                                return true;
                        }
                    }
                }
            }

            // 3. 전투 타겟 및 공간 교차 검증
            string targetName = ctx.Activity?.AutoPlayTargetDisplayName ?? "";

            if (!string.IsNullOrEmpty(targetName) &&
                def.MatchKeywords.Any(kw => targetName.Contains(kw, StringComparison.OrdinalIgnoreCase)) &&
                (ctx.Activity?.IsInCombat == true || ctx.Activity?.IsAutoPlaying == true))
            {
                return true;
            }

            return false;
        }

        public void ToggleManual(string homeworkId, string characterKey, DateTime now)
        {
            var record = _repository.GetOrCreateRecord(characterKey, now);
            if (record.Items.TryGetValue(homeworkId, out var state))
            {
                var def = _repository.MasterList.FirstOrDefault(m => m.Id == homeworkId);
                bool nextCompleted = !state.IsCompleted;

                if (def != null && !string.IsNullOrEmpty(def.SharedPoolId))
                {
                    SyncSharedPool(record, def.SharedPoolId, nextCompleted, false, now);
                }
                else
                {
                    state.IsCompleted = nextCompleted;
                    state.IsAutoDetected = false; // 수동 조작
                    state.CompletedAt = state.IsCompleted ? now : null;
                    state.CurrentCount = state.IsCompleted ? state.GoalCount : 0;
                    state.LastUpdated = now;
                }

                _repository.SaveRecord(record);
            }
        }

        public List<HomeworkItemViewItem> GetViewItems(string characterKey, HomeworkCategory filterCategory, DateTime now)
        {
            var record = _repository.GetOrCreateRecord(characterKey, now);
            var result = new List<HomeworkItemViewItem>();

            foreach (var def in _repository.MasterList)
            {
                if (filterCategory != HomeworkCategory.All && def.Category != filterCategory)
                    continue;

                record.Items.TryGetValue(def.Id, out var state);

                bool isCompleted = state?.IsCompleted ?? false;
                int currentCount = state?.CurrentCount ?? 0;
                int goalCount = state?.GoalCount ?? def.GoalCount;
                bool isAuto = state?.IsAutoDetected ?? false;

                result.Add(new HomeworkItemViewItem
                {
                    Id = def.Id,
                    Category = def.Category,
                    CategoryDisplayName = def.CategoryDisplayName,
                    Period = def.Period,
                    PeriodDisplayName = def.PeriodDisplayName,
                    SharedPoolId = def.SharedPoolId,
                    Title = def.Title,
                    Subtitle = def.Subtitle,
                    Icon = def.Icon,
                    IsCompleted = isCompleted,
                    CurrentCount = currentCount,
                    GoalCount = goalCount,
                    IsAutoDetected = isAuto
                });
            }

            // 미완료 항목 우선 정렬, 그 다음 카테고리/ID 순
            return result.OrderBy(i => i.IsCompleted).ThenBy(i => i.Category).ThenBy(i => i.Id).ToList();
        }

        public (int dailyDone, int dailyTotal, int weeklyDone, int weeklyTotal) GetProgressStats(string characterKey, DateTime now)
        {
            var record = _repository.GetOrCreateRecord(characterKey, now);

            int dailyDone = 0, dailyTotal = 0;
            int weeklyDone = 0, weeklyTotal = 0;

            var countedPools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var def in _repository.MasterList)
            {
                if (!string.IsNullOrEmpty(def.SharedPoolId))
                {
                    if (countedPools.Contains(def.SharedPoolId))
                        continue;

                    countedPools.Add(def.SharedPoolId);

                    // 풀 내에 완료된 항목이 하나라도 있으면 풀 완료
                    var poolDefs = _repository.MasterList.Where(m => m.SharedPoolId == def.SharedPoolId).ToList();
                    bool poolDone = poolDefs.Any(p => record.Items.TryGetValue(p.Id, out var s) && s.IsCompleted);

                    if (def.Period == HomeworkPeriod.Daily)
                    {
                        dailyTotal++;
                        if (poolDone) dailyDone++;
                    }
                    else
                    {
                        weeklyTotal++;
                        if (poolDone) weeklyDone++;
                    }
                }
                else
                {
                    record.Items.TryGetValue(def.Id, out var state);
                    bool done = state?.IsCompleted ?? false;

                    if (def.Period == HomeworkPeriod.Daily)
                    {
                        dailyTotal++;
                        if (done) dailyDone++;
                    }
                    else
                    {
                        weeklyTotal++;
                        if (done) weeklyDone++;
                    }
                }
            }

            return (dailyDone, dailyTotal, weeklyDone, weeklyTotal);
        }
    }
}
