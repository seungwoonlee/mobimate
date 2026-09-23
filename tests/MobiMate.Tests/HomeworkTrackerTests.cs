using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace MobiMate.Tests;

public class HomeworkTrackerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly HomeworkRepository _repo;
    private readonly HomeworkTrackerService _service;

    public HomeworkTrackerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MobiMate_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _repo = new HomeworkRepository(_tempDir);
        _service = new HomeworkTrackerService(_repo);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    [Fact]
    public void MasterList_ContainsAllCoreCategories_AndItems()
    {
        Assert.NotEmpty(_repo.MasterList);
        Assert.True(_repo.MasterList.Count >= 20, $"숙제 마스터 목록 개수({_repo.MasterList.Count})가 20개 이상이어야 합니다.");

        // 핵심 카테고리 존재 확인
        Assert.Contains(_repo.MasterList, x => x.Category == HomeworkCategory.Daily);
        Assert.Contains(_repo.MasterList, x => x.Category == HomeworkCategory.Weekly);
        Assert.Contains(_repo.MasterList, x => x.Category == HomeworkCategory.FieldBoss);
        Assert.Contains(_repo.MasterList, x => x.Category == HomeworkCategory.Abyss);
        Assert.Contains(_repo.MasterList, x => x.Category == HomeworkCategory.Raid);
        Assert.Contains(_repo.MasterList, x => x.Category == HomeworkCategory.Shop);

        // 공식 콘텐츠 가이드 일일/주간 달성 확인
        Assert.Contains(_repo.MasterList, x => x.Id == "daily_black_hole");
        Assert.Contains(_repo.MasterList, x => x.Id == "daily_day_dungeon");
        Assert.Contains(_repo.MasterList, x => x.Id == "daily_tower");
        Assert.Contains(_repo.MasterList, x => x.Id == "weekly_barrier_1_7");
        Assert.Contains(_repo.MasterList, x => x.Id == "weekly_vanguard_breach");

        // 필드 보스 6종 확인
        Assert.Contains(_repo.MasterList, x => x.Id == "fieldboss_peri");
        Assert.Contains(_repo.MasterList, x => x.Id == "fieldboss_crabbach");
        Assert.Contains(_repo.MasterList, x => x.Id == "fieldboss_krama");
        Assert.Contains(_repo.MasterList, x => x.Id == "fieldboss_drochenem");
        Assert.Contains(_repo.MasterList, x => x.Id == "fieldboss_tormog");
        Assert.Contains(_repo.MasterList, x => x.Id == "fieldboss_angrbahan");

        // 어비스 3종 확인
        Assert.Contains(_repo.MasterList, x => x.Id == "abyss_illusory_anchorage");
        Assert.Contains(_repo.MasterList, x => x.Id == "abyss_madness_cave");
        Assert.Contains(_repo.MasterList, x => x.Id == "abyss_scattered_waterway");

        // 레이드 3종 확인 및 구버전 타바르타스 미포함 확인
        Assert.Contains(_repo.MasterList, x => x.Id == "raid_cavrak");
        Assert.Contains(_repo.MasterList, x => x.Id == "raid_airel");
        Assert.Contains(_repo.MasterList, x => x.Id == "raid_white_succubus");
        Assert.DoesNotContain(_repo.MasterList, x => x.Id == "raid_tabartas");
    }

    [Fact]
    public void DailyResetTime_CalculatesCorrectly()
    {
        // 2026-09-24 10:00 -> 마지막 리셋은 당일 06:00
        var now1 = new DateTime(2026, 9, 24, 10, 0, 0);
        var lastReset1 = HomeworkRepository.GetLastDailyResetTime(now1);
        Assert.Equal(new DateTime(2026, 9, 24, 6, 0, 0), lastReset1);

        // 2026-09-24 05:00 -> 마지막 리셋은 전일 06:00
        var now2 = new DateTime(2026, 9, 24, 5, 0, 0);
        var lastReset2 = HomeworkRepository.GetLastDailyResetTime(now2);
        Assert.Equal(new DateTime(2026, 9, 23, 6, 0, 0), lastReset2);
    }

    [Fact]
    public void WeeklyResetTime_CalculatesThursday06()
    {
        // 2026-09-24는 목요일
        // 2026-09-24 08:00 (목요일 아침) -> 마지막 리셋은 2026-09-24 06:00
        var thursdayAfter = new DateTime(2026, 9, 24, 8, 0, 0);
        var lastWeekly = HomeworkRepository.GetLastWeeklyResetTime(thursdayAfter);
        Assert.Equal(new DateTime(2026, 9, 24, 6, 0, 0), lastWeekly);

        // 2026-09-24 04:00 (목요일 새벽) -> 마지막 리셋은 지난주 목요일 2026-09-17 06:00
        var thursdayBefore = new DateTime(2026, 9, 24, 4, 0, 0);
        var prevWeekly = HomeworkRepository.GetLastWeeklyResetTime(thursdayBefore);
        Assert.Equal(new DateTime(2026, 9, 17, 6, 0, 0), prevWeekly);
    }

    [Fact]
    public void EvaluateAndSync_AutoDetects_DailyMissionCompletion()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        var ctx = new HomeworkEvaluationContext
        {
            CharacterKey = "Aira_Striker",
            DailyMissions = new List<MissionItem>
            {
                new("에린에 돌아왔습니다", "접속하기", 1, 1, true, true),
                new("오늘도 던전 한 바퀴", "던전 3회 토벌", 3, 3, true, true),
                new("자급자족의 삶", "재료 아이템 3회 채집", 1, 3, false, false)
            }
        };

        var record = _service.EvaluateAndSync(ctx, now);

        Assert.NotNull(record);
        Assert.True(record.Items["daily_connect"].IsCompleted);
        Assert.True(record.Items["daily_connect"].IsAutoDetected);

        Assert.True(record.Items["daily_dungeon_3"].IsCompleted);
        Assert.True(record.Items["daily_dungeon_3"].IsAutoDetected);

        Assert.False(record.Items["daily_gather_3"].IsCompleted);
        Assert.Equal(1, record.Items["daily_gather_3"].CurrentCount);
    }

    [Fact]
    public void EvaluateAndSync_AutoDetects_AlteringWorks()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        var ctx = new HomeworkEvaluationContext
        {
            CharacterKey = "Aira_Striker",
            AlteringWorks = new AlteringWorksResponse(1, new List<AlteringWorkItem>
            {
                new("질긴 가죽", "가죽 작업대", "Completed", true, 0)
            })
        };

        var record = _service.EvaluateAndSync(ctx, now);

        Assert.True(record.Items["daily_altering"].IsCompleted);
        Assert.True(record.Items["daily_altering"].IsAutoDetected);
    }

    [Fact]
    public void EvaluateAndSync_AutoDetects_FieldBossCombat()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        var ctx = new HomeworkEvaluationContext
        {
            CharacterKey = "Aira_Striker",
            Activity = new ActivityInfo(
                IsAutoPlaying: true,
                IsAutoTraveling: false,
                IsInCombat: true,
                AutoPlayTargetDisplayName: "크라마",
                IsGathering: false,
                IsAltering: false,
                IsCrafting: false,
                IsPlayingInstrument: false,
                IsSitting: false,
                IsOverweight: false,
                CurrentAction: "Combat",
                CanStopCurrentAction: true
            ),
            Environment = new EnvironmentInfo(
                ChannelName: "케오 섬 1채널",
                GameSpaceDisplayName: "케오 섬",
                Weather: "Sunny",
                ErinnNow: "2959-4-23 15:51"
            )
        };

        var record = _service.EvaluateAndSync(ctx, now);

        Assert.True(record.Items["fieldboss_krama"].IsCompleted);
        Assert.True(record.Items["fieldboss_krama"].IsAutoDetected);
    }

    [Fact]
    public void ManualToggle_TogglesStateCorrectly()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        string key = "Aira_Striker";
        string targetId = "daily_cash_free_fashion";

        // 최초 상태는 미완료
        var items1 = _service.GetViewItems(key, HomeworkCategory.All, now);
        var target1 = items1.Find(x => x.Id == targetId);
        Assert.NotNull(target1);
        Assert.False(target1.IsCompleted);

        // 수동 완료 토글
        _service.ToggleManual(targetId, key, now);
        var items2 = _service.GetViewItems(key, HomeworkCategory.All, now);
        var target2 = items2.Find(x => x.Id == targetId);
        Assert.NotNull(target2);
        Assert.True(target2.IsCompleted);
        Assert.False(target2.IsAutoDetected); // 수동이므로 false
        Assert.Contains("수동 완료", target2.DetectionBadge);

        // 다시 토글하면 미완료로 복원
        _service.ToggleManual(targetId, key, now);
        var items3 = _service.GetViewItems(key, HomeworkCategory.All, now);
        var target3 = items3.Find(x => x.Id == targetId);
        Assert.NotNull(target3);
        Assert.False(target3.IsCompleted);
    }

    [Fact]
    public void ResetLogic_ClearsExpiredCompletions_OnDailyReset()
    {
        // 2026-09-23 20:00에 일일 완료
        var completedTime = new DateTime(2026, 9, 23, 20, 0, 0);
        string key = "Aira_Striker";
        _service.ToggleManual("daily_connect", key, completedTime);

        // 같은 날(리셋 전, 2026-09-24 04:00) 확인 시 여전히 완료
        var beforeResetTime = new DateTime(2026, 9, 24, 4, 0, 0);
        var recordBefore = _repo.GetOrCreateRecord(key, beforeResetTime);
        Assert.True(recordBefore.Items["daily_connect"].IsCompleted);

        // 리셋 후(2026-09-24 07:00) 확인 시 자동 미완료 초기화
        var afterResetTime = new DateTime(2026, 9, 24, 7, 0, 0);
        var recordAfter = _repo.GetOrCreateRecord(key, afterResetTime);
        Assert.False(recordAfter.Items["daily_connect"].IsCompleted);
    }

    [Fact]
    public void CategoryFilter_FiltersCorrectly()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        string key = "Aira_Striker";

        var bossItems = _service.GetViewItems(key, HomeworkCategory.FieldBoss, now);
        Assert.NotEmpty(bossItems);
        Assert.All(bossItems, item => Assert.Equal(HomeworkCategory.FieldBoss, item.Category));

        var raidItems = _service.GetViewItems(key, HomeworkCategory.Raid, now);
        Assert.NotEmpty(raidItems);
        Assert.All(raidItems, item => Assert.Equal(HomeworkCategory.Raid, item.Category));

        var shopItems = _service.GetViewItems(key, HomeworkCategory.Shop, now);
        Assert.NotEmpty(shopItems);
        Assert.All(shopItems, item => Assert.Equal(HomeworkCategory.Shop, item.Category));
    }

    [Fact]
    public void GetProgressStats_CalculatesAccurately()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        string key = "Aira_Striker";

        var stats1 = _service.GetProgressStats(key, now);
        Assert.Equal(0, stats1.dailyDone);
        Assert.True(stats1.dailyTotal > 0);

        // 1개 일일 숙제 수동 완료
        _service.ToggleManual("daily_connect", key, now);
        var stats2 = _service.GetProgressStats(key, now);
        Assert.Equal(1, stats2.dailyDone);

        // 1개 주간 숙제(필드 보스 - 공유 풀) 수동 완료
        _service.ToggleManual("fieldboss_peri", key, now);
        var stats3 = _service.GetProgressStats(key, now);
        Assert.Equal(1, stats3.weeklyDone);
    }

    [Fact]
    public void FieldBoss_SharedPool_OneClearMarksAllSixAsCompleted()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0);
        string key = "Aira_Striker";

        // 최초 상태: 6종 보스 모두 미완료
        var bossItemsBefore = _service.GetViewItems(key, HomeworkCategory.FieldBoss, now);
        Assert.Equal(6, bossItemsBefore.Count);
        Assert.All(bossItemsBefore, b => Assert.False(b.IsCompleted));

        // 크라마(krama) 1종만 수동 완료 토글
        _service.ToggleManual("fieldboss_krama", key, now);

        // 6종 보스 전체가 주간 1회 보상 풀 완료로 동기화되었는지 확인
        var bossItemsAfter = _service.GetViewItems(key, HomeworkCategory.FieldBoss, now);
        Assert.Equal(6, bossItemsAfter.Count);
        Assert.All(bossItemsAfter, b => Assert.True(b.IsCompleted));

        // 다시 토글 시 전체 미완료로 해제되는지 확인
        _service.ToggleManual("fieldboss_peri", key, now);
        var bossItemsReset = _service.GetViewItems(key, HomeworkCategory.FieldBoss, now);
        Assert.All(bossItemsReset, b => Assert.False(b.IsCompleted));
    }
}
