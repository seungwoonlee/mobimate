using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
        private static readonly Regex UnityTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

        public HomeworkRepository Repository => _repository;

        public HomeworkTrackerService(HomeworkRepository? repository = null)
        {
            _repository = repository ?? new HomeworkRepository();
        }

        private static string StripTags(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return UnityTagRegex.Replace(text, string.Empty).Trim();
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

                // 유저가 수동으로 오버라이드한 경우 자동 감지가 덮어쓰지 않음
                if (state.ManualOverride)
                    continue;

                // 이미 완료 상태인 경우 (상태 영속화/Latch 보존)
                if (state.IsCompleted)
                    continue;

                bool newlyDone = false;

                // Layer 1: 오직 1:1 직결 미션 카운터(daily_missions_all, weekly_missions_all)만 공식 미션 카운터 직결
                if (def.AutoMode == AutoDetectMode.DirectMission &&
                    CheckLayer1OfficialMissions(def, ctx, out int missionCur, out int missionGoal, out bool missionDone))
                {
                    state.CurrentCount = missionCur;
                    state.GoalCount = missionGoal;
                    if (missionDone)
                    {
                        state.IsCompleted = true;
                        state.IsAutoDetected = true;
                        state.CompletedAt = now;
                        changed = true;
                        newlyDone = true;
                    }
                }

                // Layer 2: 진행 중 퀘스트 트래커 (get_quests) 리치텍스트 태그 박리 및 세부 목표 매칭
                if (!newlyDone && CheckLayer2QuestTracker(def, ctx))
                {
                    state.CurrentCount = state.GoalCount;
                    state.IsCompleted = true;
                    state.IsAutoDetected = true;
                    state.CompletedAt = now;
                    changed = true;
                    newlyDone = true;
                }

                // Layer 3: 실시간 전투 및 던전 공간 감지 (get_activity + get_environment)
                if (!newlyDone && CheckLayer3CombatOrSpace(def, ctx))
                {
                    state.CurrentCount = state.GoalCount;
                    state.IsCompleted = true;
                    state.IsAutoDetected = true;
                    state.CompletedAt = now;
                    changed = true;
                    newlyDone = true;
                }

                // Layer 4: 생활/가공 시설 대기열 완료 수거 감지
                if (!newlyDone && def.AutoMode == AutoDetectMode.AlteringFacility && CheckLayer4Altering(ctx))
                {
                    state.CurrentCount = 1;
                    state.GoalCount = 1;
                    state.IsCompleted = true;
                    state.IsAutoDetected = true;
                    state.CompletedAt = now;
                    changed = true;
                    newlyDone = true;
                }

                // Layer 5: 레이드 사후 정밀 감지 (퀘스트 소멸 후 주간 미션 완료 + 고유 레이드 증표 보유 교차 검증)
                if (!newlyDone && def.Category == HomeworkCategory.Raid && CheckLayer5RaidCompleted(def, ctx))
                {
                    state.CurrentCount = state.GoalCount;
                    state.IsCompleted = true;
                    state.IsAutoDetected = true;
                    state.CompletedAt = now;
                    changed = true;
                    newlyDone = true;
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
                    sibState.ManualOverride = !isAuto;
                    sibState.CompletedAt = isCompleted ? now : null;
                    sibState.CurrentCount = isCompleted ? sibState.GoalCount : 0;
                    sibState.LastUpdated = now;
                }
            }
        }

        /// <summary>
        /// Layer 1: 공식 미션 시스템(get_weekly_missions, get_daily_missions) 기반 전체 카운트 전용 판정
        /// 주의: 계정 도전과제(레이드 1회, 소환결계 1회 등)는 특정 캐릭터의 주간 숙제와 1:1 대응되지 않으므로 개별 숙제를 임의 완료 처리하지 않음(오탐 방지).
        /// </summary>
        private bool CheckLayer1OfficialMissions(HomeworkDefinition def, HomeworkEvaluationContext ctx, out int currentCount, out int goalCount, out bool isDone)
        {
            currentCount = 0;
            goalCount = def.GoalCount;
            isDone = false;

            var missions = def.Period == HomeworkPeriod.Daily
                ? ctx.DailyMissions
                : ctx.WeeklyMissions;

            if (missions == null || missions.Count == 0)
                return false;

            // 1. 주간 전체 미션 완수 카운트
            if (def.Id == "weekly_missions_all")
            {
                int completed = missions.Count(m => m.IsCompleted || m.CurrentCount >= m.GoalCount);
                currentCount = completed;
                goalCount = Math.Max(15, missions.Count);
                isDone = completed >= goalCount;
                return true;
            }

            // 2. 일일 전체 미션 완수 카운트
            if (def.Id == "daily_missions_all")
            {
                int completed = missions.Count(m => m.IsCompleted || m.CurrentCount >= m.GoalCount);
                currentCount = completed;
                goalCount = Math.Max(8, missions.Count);
                isDone = completed >= goalCount;
                return true;
            }

            // 3. AutoMode가 DirectMission으로 명시된 개별 미션 1:1 매칭 (daily_connect, daily_dungeon_3 등)
            if (def.AutoMode == AutoDetectMode.DirectMission)
            {
                foreach (var m in missions)
                {
                    var cleanTitle = (m.Title ?? "").Replace(" ", "");
                    var cleanDesc = (m.Description ?? "").Replace(" ", "");

                    foreach (var kw in def.MatchKeywords)
                    {
                        var cleanKw = (kw ?? "").Replace(" ", "");
                        if (string.IsNullOrEmpty(cleanKw)) continue;

                        if (cleanTitle.Contains(cleanKw, StringComparison.OrdinalIgnoreCase) ||
                            cleanDesc.Contains(cleanKw, StringComparison.OrdinalIgnoreCase))
                        {
                            currentCount = m.CurrentCount;
                            goalCount = m.GoalCount > 0 ? m.GoalCount : def.GoalCount;
                            isDone = m.IsCompleted || currentCount >= goalCount;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Layer 2: 진행 중인 퀘스트 트래커(get_quests) 텍스트 정규화 및 목표 상태 매칭
        /// </summary>
        private bool CheckLayer2QuestTracker(HomeworkDefinition def, HomeworkEvaluationContext ctx)
        {
            if (ctx.Quests == null || ctx.Quests.Count == 0)
                return false;

            foreach (var q in ctx.Quests)
            {
                var cleanTitle = StripTags(q.QuestTitle).Replace(" ", "");

                bool titleMatch = def.MatchKeywords.Any(kw =>
                {
                    var cleanKw = (kw ?? "").Replace(" ", "");
                    return !string.IsNullOrEmpty(cleanKw) && cleanTitle.Contains(cleanKw, StringComparison.OrdinalIgnoreCase);
                });

                if (titleMatch)
                {
                    // 목표가 없거나 모두 완료된 경우, 또는 하나 이상의 목표가 완료된 경우
                    if (q.Objectives == null || q.Objectives.Count == 0 || q.Objectives.All(o => o.IsCompleted) || q.Objectives.Any(o => o.IsCompleted))
                        return true;
                }

                // 목표 텍스트 내용에서 키워드 매칭 검사
                if (q.Objectives != null)
                {
                    foreach (var obj in q.Objectives)
                    {
                        var cleanDesc = StripTags(obj.Description).Replace(" ", "");
                        bool descMatch = def.MatchKeywords.Any(kw =>
                        {
                            var cleanKw = (kw ?? "").Replace(" ", "");
                            return !string.IsNullOrEmpty(cleanKw) && cleanDesc.Contains(cleanKw, StringComparison.OrdinalIgnoreCase);
                        });

                        if (descMatch && obj.IsCompleted)
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Layer 3: 실시간 전투 및 던전 공간 감지 (get_activity + get_environment)
        /// </summary>
        private bool CheckLayer3CombatOrSpace(HomeworkDefinition def, HomeworkEvaluationContext ctx)
        {
            string spaceName = (ctx.Environment?.GameSpaceDisplayName ?? "").Replace(" ", "");
            string channelName = (ctx.Environment?.ChannelName ?? "").Replace(" ", "");
            string targetName = (ctx.Activity?.AutoPlayTargetDisplayName ?? "").Replace(" ", "");

            bool targetMatched = !string.IsNullOrEmpty(targetName) &&
                def.MatchKeywords.Any(kw =>
                {
                    var cleanKw = (kw ?? "").Replace(" ", "");
                    return !string.IsNullOrEmpty(cleanKw) && targetName.Contains(cleanKw, StringComparison.OrdinalIgnoreCase);
                });

            bool spaceMatched = def.MatchKeywords.Any(kw =>
            {
                var cleanKw = (kw ?? "").Replace(" ", "");
                if (string.IsNullOrEmpty(cleanKw)) return false;
                return (!string.IsNullOrEmpty(spaceName) && spaceName.Contains(cleanKw, StringComparison.OrdinalIgnoreCase)) ||
                       (!string.IsNullOrEmpty(channelName) && channelName.Contains(cleanKw, StringComparison.OrdinalIgnoreCase));
            });

            // 1. 타겟 보스와 교전 중이거나 자동 사냥/전투 진행 중
            if (targetMatched && (ctx.Activity?.IsInCombat == true || ctx.Activity?.IsAutoPlaying == true || ctx.Activity?.CurrentAction?.Contains("Combat", StringComparison.OrdinalIgnoreCase) == true))
            {
                return true;
            }

            // 2. 해당 던전 공간 진입 및 전투 중
            if (spaceMatched && (ctx.Activity?.IsInCombat == true || ctx.Activity?.IsAutoPlaying == true || ctx.Activity?.CurrentAction?.Contains("Combat", StringComparison.OrdinalIgnoreCase) == true))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Layer 4: 생활/가공 시설 수거 상태 검사
        /// </summary>
        private static bool CheckLayer4Altering(HomeworkEvaluationContext ctx)
        {
            if (ctx.AlteringWorks == null) return false;

            return ctx.AlteringWorks.CompletedCount > 0 ||
                   (ctx.AlteringWorks.Works != null && ctx.AlteringWorks.Works.Any(w => w.IsDone));
        }

        /// <summary>
        /// Layer 5: 레이드 사후 정밀 감지
        /// 레이드는 토벌 완료 후 get_quests 목록에서 소멸하므로,
        /// 주간 미션("선장님, 출정합니다! 레이드 1회 토벌" 완료)과 get_currencies 내 해당 레이드 고유 원정 증거 토큰을 교차 검증하여 확정 판정함.
        /// [오탐 방지 가드레일]: 보유한 레이드 원정 증거 토큰 중 잔여량이 존재하는 레이드가 단 1종일 때만 100% 매핑하여 복수 레이드 동시 오탐 방지.
        /// </summary>
        private static bool CheckLayer5RaidCompleted(HomeworkDefinition def, HomeworkEvaluationContext ctx)
        {
            if (def.Category != HomeworkCategory.Raid) return false;

            // 1. 주간 미션에 "레이드 1회 토벌" 완료가 찍혀 있는지 확인
            bool hasWeeklyRaidDone = ctx.WeeklyMissions != null && ctx.WeeklyMissions.Any(m =>
                (m.Title?.Contains("출정") == true || m.Description?.Contains("레이드") == true) &&
                (m.IsCompleted || m.CurrentCount >= 1));

            if (!hasWeeklyRaidDone) return false;

            // 2. 재화(Currencies) 목록에서 원정의 증거 레이드 토큰 보유 검사
            if (ctx.Currencies == null || ctx.Currencies.Count == 0) return false;

            // 모든 레이드 원정의 증거 토큰 목록 추출 (Non-breaking space \u00A0 정규화)
            var activeRaidTokens = ctx.Currencies
                .Where(c => !string.IsNullOrEmpty(c.DisplayName) &&
                            c.DisplayName.Contains("원정의 증거") &&
                            c.DisplayName.Contains("레이드") &&
                            c.Amount > 0)
                .ToList();

            if (activeRaidTokens.Count == 0) return false;

            // [오탐 방지 가드레일 A]: 보유 레이드 토큰이 오직 1종류만 존재할 때 100% 확정 매핑
            // (복수 레이드 토큰 보유 시 이번 주에 어떤 레이드를 돌았는지 불명확하므로 오탐 방지를 위해 자동 완료 보류)
            if (activeRaidTokens.Count == 1)
            {
                var token = activeRaidTokens[0];
                var cleanTokenName = (token.DisplayName ?? "").Replace("\u00A0", " ").Replace(" ", "");

                bool kwMatch = def.MatchKeywords.Any(kw =>
                {
                    var cleanKw = (kw ?? "").Replace(" ", "");
                    return !string.IsNullOrEmpty(cleanKw) && cleanTokenName.Contains(cleanKw, StringComparison.OrdinalIgnoreCase);
                });

                return kwMatch;
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
                    state.ManualOverride = true;  // 유저 수동 조작 우선
                    state.CompletedAt = state.IsCompleted ? now : null;
                    state.CurrentCount = state.IsCompleted ? state.GoalCount : 0;
                    state.LastUpdated = now;
                }

                _repository.SaveRecord(record);
            }
        }

        public HomeworkCharacterRecord ResetAllManual(string characterKey, DateTime now)
        {
            return _repository.ResetRecord(characterKey, now);
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
