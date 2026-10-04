using System.Text.Json;

namespace MobiMate.Tests;

/// <summary>TST-09 (숙제 판정 회귀 H-1~H-10, KST 리셋) · TST-10 (카탈로그 검증).</summary>
public class HomeworkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hw-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private DateTimeOffset _now = Kst(2026, 9, 23, 12, 0);   // 수요일 정오 KST
    private const string Main = "아이라_격투가";
    private const string Alt = "아이라_석궁사수";

    public HomeworkTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static DateTimeOffset Kst(int y, int mo, int d, int h, int mi) => new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromHours(9)).ToUniversalTime();
    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);
    private static HomeworkCard Card(HomeworkBoard b, string id) => b.Cards.Single(c => c.Id == id);
    private static MissionItem M(string title, int cur, int goal) => new(title, "", cur, goal, cur >= goal, cur >= goal);
    private static QuestItem Q(string title, params bool[] objectives) =>
        new(title, "Weekly", "주간", objectives.Select((o, i) => new QuestObjective($"목표 {i + 1}", o)).ToList());
    private static ActivityInfo Act(bool combat, bool autoPlay = false, string? target = null) =>
        new(autoPlay, false, combat, false, false, false, false, false, false, combat ? "Combat" : "Idle", false, false, target);

    // ── TST-10 카탈로그 ──

    [Fact]
    public void Catalog_Has34Items_AndPassesValidation()
    {
        Assert.Equal(34, _catalog.Items.Count);
        Assert.Empty(_catalog.Validate());
        Assert.Equal(6, _catalog.Items.Count(i => i.Pool == "field_boss_weekly"));
    }

    [Fact]
    public void Catalog_Validation_RejectsGenericKeys_UnknownPool_AndDuplicates()
    {
        var bad = HomeworkCatalog.Parse("""
        { "version": 1, "sharedPools": {},
          "items": [
            { "id": "a", "category": "abyss", "period": "weekly", "share": "character", "mode": "manual", "title": "A", "spaceNames": ["동굴"] },
            { "id": "a", "category": "daily", "period": "daily", "share": "character", "mode": "directMission", "title": "B" },
            { "id": "c", "category": "fieldBoss", "period": "weekly", "share": "character", "mode": "manual", "title": "C", "pool": "nope" }
          ] }
        """);
        var errors = bad.Validate();
        Assert.Contains(errors, e => e.Contains("ID 중복"));
        Assert.Contains(errors, e => e.Contains("일반어 단독 매칭 키"));
        Assert.Contains(errors, e => e.Contains("1:1 미션 제목 없음"));
        Assert.Contains(errors, e => e.Contains("정의되지 않은 공유 풀"));
    }

    [Fact]
    public void Catalog_MissionTitles_MatchApiSpecSamples()
    {
        // TST-10: API_SPEC_SAMPLES.md에 있는 일일 미션 제목과 정확히 일치해야 한다
        var sampleTitles = new[] { "에린에 돌아왔습니다", "오늘도 던전 한 바퀴", "자급자족의 삶" };
        foreach (var def in _catalog.Items.Where(i => i.EffectiveMode == HomeworkMode.DirectMission))
            Assert.All(def.MissionTitles, t => Assert.Contains(t, sampleTitles));
    }

    [Fact]
    public void Catalog_ItemsNeedingMeasurement_BehaveAsManual()
    {
        var blackHole = _catalog.Find("daily_black_hole")!;
        Assert.NotNull(blackHole.NeedsMeasurement);
        Assert.Equal(HomeworkMode.Manual, blackHole.EffectiveMode);
    }

    // ── TST-09 판정 회귀 ──

    [Fact]
    public void K01_AccountChallengeMissions_DoNotCompleteRaidOrBarrier()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(WeeklyMissions: new[]
        {
            M("선장님, 출정합니다!", 1, 1), M("안 돼, 다시 돌아가", 1, 1)
        }));
        var b = svc.GetBoard(Main);
        foreach (var id in new[] { "raid_cavrak", "abyss_madness_cave", "weekly_barrier_1_7" })
            Assert.Equal(HomeworkCardStatus.Pending, Card(b, id).Status);
    }

    [Fact]
    public void H1_BossCombat_IsProgressSignalOnly_NeverCompletion()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(Activity: Act(combat: true, target: "흑룡 카브락")));
        Assert.Equal(HomeworkCardStatus.InProgress, Card(svc.GetBoard(Main), "raid_cavrak").Status);

        // 신호는 10분이 지나면 사라지고, 완료로 바뀌지 않는다
        _now = _now.AddMinutes(11);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "raid_cavrak").Status);
    }

    [Fact]
    public void H1_GenericSpaceWords_DoNotSignalAbyss()
    {
        // "동굴"만 같은 다른 던전에서는 광기의 동굴 신호가 나지 않는다 (정확히 일치)
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(Activity: Act(true), Environment: new EnvironmentInfo("미지의 동굴", "Sunny", "", "미지의 동굴")));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "abyss_madness_cave").Status);

        svc.Evaluate(Main, new HomeworkObservation(Activity: Act(true), Environment: new EnvironmentInfo("ch", "Sunny", "", "광기의  동굴")));
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main), "abyss_madness_cave").Status);   // 지역 진입이 곧 완료 근거다
    }

    [Fact]
    public void H2_QuestWithPartialObjectives_IsNotCompletion()
    {
        var catalog = CatalogWithVerifiedVanguard();
        var svc = new HomeworkService(catalog, new HomeworkStore(_dir), () => _now);

        svc.Evaluate(Main, new HomeworkObservation(Quests: new[] { Q("<color=orange>[긴급 의뢰]</color> 뱅가드  브리치", true, false) }));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "weekly_vanguard_breach").Status);

        svc.Evaluate(Main, new HomeworkObservation(Quests: new[] { Q("[긴급 의뢰] 뱅가드 브리치") }));   // 목표 0개도 근거 아님
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "weekly_vanguard_breach").Status);

        svc.Evaluate(Main, new HomeworkObservation(Quests: new[] { Q("<color=orange>[긴급 의뢰]</color>뱅가드 브리치", true, true) }));
        var card = Card(svc.GetBoard(Main), "weekly_vanguard_breach");
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Contains("목표 2개 모두 완료", card.Evidence);
    }

    [Fact]
    public void H3_DirectMission_RequiresExactTitle_NotKeywordContainment()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(DailyMissions: new[] { M("오늘도 던전 한 바퀴", 3, 3) }));
        var b = svc.GetBoard(Main);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(b, "daily_dungeon_3").Status);
        Assert.Equal(HomeworkCardStatus.Pending, Card(b, "daily_day_dungeon").Status);   // "던전" 포함만으로 요일 던전 완료 안 됨
        Assert.Contains("오늘도 던전 한 바퀴", Card(b, "daily_dungeon_3").Evidence);
    }

    [Fact]
    public void H4_AlteringReadyToCollect_IsNotCollected_UntilCountDrops()
    {
        var svc = NewService();
        AlteringWorksResponse Works(int doneLeather) => new(doneLeather,
            Enumerable.Range(0, doneLeather).Select(_ => new AlteringWorkItem("질긴 가죽", "가죽 작업대", "Completed", true, 0)).ToList());

        svc.Evaluate(Main, new HomeworkObservation(AlteringWorks: Works(2)));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_altering").Status);

        svc.Evaluate(Main, new HomeworkObservation(AlteringWorks: null));   // 조회 실패는 비교하지 않는다
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_altering").Status);

        _now = _now.AddMinutes(1);
        svc.Evaluate(Main, new HomeworkObservation(AlteringWorks: Works(0)));   // 빈 응답(로딩 중 등)은 근거 아님
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_altering").Status);

        svc.Evaluate(Main, new HomeworkObservation(AlteringWorks: Works(1)));   // 2 → 1: 수거 관찰
        var card = Card(svc.GetBoard(Main), "daily_altering");
        Assert.Equal(HomeworkCardStatus.AutoDone, card.Status);
        Assert.Contains("질긴 가죽", card.Evidence);
    }

    [Fact]
    public void H4_CharacterSwitch_FirstObservationIsNotCompared()
    {
        var svc = NewService();
        AlteringWorksResponse W(int n) => new(n, Enumerable.Range(0, n).Select(_ => new AlteringWorkItem("실크", "베틀", "Completed", true, 0))
            .Append(new AlteringWorkItem("철괴", "제련로", "InProgress", false, 900)).ToList());

        svc.Evaluate(Main, new HomeworkObservation(AlteringWorks: W(2)));
        svc.Evaluate(Alt, new HomeworkObservation(AlteringWorks: W(0)));        // 다른 캐릭터 목록
        svc.Evaluate(Main, new HomeworkObservation(AlteringWorks: W(1)));       // 전환 직후 첫 조회: 비교하지 않음
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_altering").Status);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Alt), "daily_altering").Status);

        svc.Evaluate(Main, new HomeworkObservation(AlteringWorks: W(0)));       // 같은 캐릭터 연속 조회: 1 → 0
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main), "daily_altering").Status);
    }

    [Fact]
    public void H4_StaleObservation_AfterLongGap_IsNotCompared()
    {
        var svc = NewService();
        var running = new AlteringWorkItem("철괴", "제련로", "InProgress", false, 900);
        svc.Evaluate(Main, new HomeworkObservation(AlteringWorks: new(1, new() { new("실크", "베틀", "Completed", true, 0), running })));
        _now = _now.AddMinutes(30);   // 오래 비어 있던 관찰은 비교에 쓰지 않는다
        svc.Evaluate(Main, new HomeworkObservation(AlteringWorks: new(0, new() { running })));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_altering").Status);
    }

    [Fact]
    public void H4_CollectedByApp_Completes()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(CollectedByApp: true));
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(svc.GetBoard(Main), "daily_altering").Status);
    }

    [Fact]
    public void H5_Reset_ClearsManualOverrideAndPartialProgress_SoAutoDetectionResumes()
    {
        var svc = NewService();
        svc.Set(Main, "daily_dungeon_3", completed: false);            // 사용자가 해제 → 수동 우선
        svc.Evaluate(Main, new HomeworkObservation(DailyMissions: new[] { M("오늘도 던전 한 바퀴", 3, 3) }));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_dungeon_3").Status);   // 이번 주기엔 덮어쓰지 않음

        svc.Set(Main, "weekly_barrier_1_7", count: 3);                  // 부분 진행

        _now = Kst(2026, 9, 24, 6, 0);                                   // 다음날 06:00 일일 리셋
        svc.Evaluate(Main, new HomeworkObservation(DailyMissions: new[] { M("오늘도 던전 한 바퀴", 3, 3) }));
        var b = svc.GetBoard(Main);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(b, "daily_dungeon_3").Status);   // 자동 감지 재개
        Assert.Equal(3, Card(b, "weekly_barrier_1_7").Count);                            // 주간은 아직 유지

        _now = Kst(2026, 9, 28, 6, 0);                                   // 월요일 06:00 주간 리셋
        Assert.Equal(0, Card(svc.GetBoard(Main), "weekly_barrier_1_7").Count);
    }

    [Theory]
    [InlineData(2026, 9, 28, 5, 59, false)]   // 월요일 05:59 → 아직 리셋 전
    [InlineData(2026, 9, 28, 6, 0, true)]     // 월요일 06:00 → 리셋
    [InlineData(2026, 9, 27, 23, 59, false)]  // 일요일 23:59
    public void H6_WeeklyReset_UsesKstMondaySixAm(int y, int mo, int d, int h, int mi, bool reset)
    {
        var svc = NewService();
        _now = Kst(2026, 9, 25, 20, 0);   // 금요일
        svc.Set(Main, "raid_cavrak", completed: true);
        _now = Kst(y, mo, d, h, mi);
        Assert.Equal(reset ? HomeworkCardStatus.Pending : HomeworkCardStatus.ManualDone, Card(svc.GetBoard(Main), "raid_cavrak").Status);
    }

    [Fact]
    public void H6_KstClock_IsIndependentOfLocalTimeZone()
    {
        // UTC 21:00 = KST 다음날 06:00
        var utc = new DateTimeOffset(2026, 9, 27, 21, 0, 0, TimeSpan.Zero);
        Assert.Equal(utc, KstClock.LastWeeklyReset(utc));
        Assert.Equal(utc, KstClock.LastDailyReset(utc));
        Assert.Equal(utc.AddDays(-1), KstClock.LastDailyReset(utc.AddSeconds(-1)));
    }

    [Fact]
    public void Reset_SkippedWhileServerWasOff_IsAppliedOnNextLoad()
    {
        var svc = NewService();
        svc.Set(Main, "daily_tower", completed: true);
        _now = _now.AddDays(3);   // 3일 동안 꺼져 있었다
        Assert.Equal(HomeworkCardStatus.Pending, Card(NewService().GetBoard(Main), "daily_tower").Status);
    }

    [Fact]
    public void H7_H10_FieldBossPool_ComputedNotCopied_AndUnsetRestoresPool()
    {
        var svc = NewService();
        svc.Set(Main, "fieldboss_krama", completed: true);
        var b = svc.GetBoard(Main);
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(b, "fieldboss_krama").Status);
        Assert.All(b.Cards.Where(c => c.Pool != null && c.Id != "fieldboss_krama"), c => Assert.Equal(HomeworkCardStatus.PoolDone, c.Status));

        svc.Set(Main, "fieldboss_krama", completed: false);
        Assert.All(svc.GetBoard(Main).Cards.Where(c => c.Pool != null), c => Assert.Equal(HomeworkCardStatus.Pending, c.Status));
    }

    [Fact]
    public void H8_AccountItems_AreSharedAcrossCharacters_CharacterItemsAreNot()
    {
        var svc = NewService();
        svc.Set(Main, "weekly_heart_holy_water", completed: true);
        svc.Set(Main, "abyss_madness_cave", completed: true);
        svc.Evaluate(Main, new HomeworkObservation(DailyMissions: new[] { M("에린에 돌아왔습니다", 1, 1) }));

        var alt = svc.GetBoard(Alt);
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(alt, "weekly_heart_holy_water").Status);
        Assert.Equal(HomeworkCardStatus.AutoDone, Card(alt, "daily_connect").Status);   // 계정 미션 근거 → 계정 공통
        Assert.Equal(HomeworkCardStatus.Pending, Card(alt, "abyss_madness_cave").Status);
    }

    [Fact]
    public void H10_ManualOnlyItems_NeverAutoComplete_EvenWithMatchingText()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(
            WeeklyMissions: new[] { M("하트 토큰 성수 교환", 1, 1) },
            Quests: new[] { Q("성수 교환", true), Q("검은 구멍", true) },
            Activity: Act(combat: false, autoPlay: true, target: "카브락")));
        var b = svc.GetBoard(Main);
        Assert.Equal(HomeworkCardStatus.Pending, Card(b, "weekly_heart_holy_water").Status);
        Assert.Equal(HomeworkCardStatus.Pending, Card(b, "daily_black_hole").Status);
        Assert.Equal(HomeworkCardStatus.Pending, Card(b, "weekly_black_hole_1_7").Status);
        Assert.Equal(HomeworkCardStatus.Pending, Card(b, "raid_cavrak").Status);   // 자동 사냥만으로는 신호도 아님
    }

    [Fact]
    public void ManualOverride_BlocksAuto_BothDirections()
    {
        var svc = NewService();
        svc.Set(Main, "daily_connect", completed: true);
        svc.Evaluate(Main, new HomeworkObservation(DailyMissions: new[] { M("에린에 돌아왔습니다", 0, 1) }));
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(svc.GetBoard(Main), "daily_connect").Status);
    }

    [Fact]
    public void Set_IsIdempotent_AndStepCounterClamps()
    {
        var svc = NewService();
        svc.Set(Main, "raid_cavrak", completed: true);
        svc.Set(Main, "raid_cavrak", completed: true);
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(svc.GetBoard(Main), "raid_cavrak").Status);

        svc.Set(Main, "weekly_barrier_1_7", count: 99);
        var c = Card(svc.GetBoard(Main), "weekly_barrier_1_7");
        Assert.Equal(7, c.Count);
        Assert.Equal(HomeworkCardStatus.ManualDone, c.Status);
        Assert.Throws<KeyNotFoundException>(() => svc.Set(Main, "nope", completed: true));
    }

    [Fact]
    public void MissionTotal_CompletesOnlyWhenAllMissionsDone()
    {
        var svc = NewService();
        var missions = Enumerable.Range(0, 11).Select(i => M($"m{i}", i < 10 ? 1 : 0, 1)).ToList();
        svc.Evaluate(Main, new HomeworkObservation(DailyMissions: missions));
        var c = Card(svc.GetBoard(Main), "daily_missions_all");
        Assert.Equal(HomeworkCardStatus.Pending, c.Status);
        Assert.Equal(10, c.Count);
    }

    [Fact]
    public void Progress_ExcludesShop_AndCountsPoolOnce()
    {
        var svc = NewService();
        var before = svc.GetBoard(Main);
        Assert.Equal(_catalog.Items.Count(i => i.Active && i.Period == HomeworkPeriod.Daily && i.Category != "shop"), before.Daily.Total);
        Assert.Equal(_catalog.Items.Count(i => i.Active && i.Period == HomeworkPeriod.Weekly && i.Category != "shop" && i.Pool == null) + 1, before.Weekly.Total);

        svc.Set(Main, "fieldboss_peri", completed: true);
        Assert.Equal(1, svc.GetBoard(Main).Weekly.Done);
        Assert.Equal(Kst(2026, 9, 24, 6, 0), before.NextDailyResetUtc);
        Assert.Equal(Kst(2026, 9, 28, 6, 0), before.NextWeeklyResetUtc);
    }

    [Fact]
    public void ResetAll_ClearsCharacter_AndOptionallyAccount()
    {
        var svc = NewService();
        svc.Set(Main, "raid_cavrak", completed: true);
        svc.Set(Main, "weekly_heart_holy_water", completed: true);

        svc.ResetAll(Main, includeAccount: false);
        var b = svc.GetBoard(Main);
        Assert.Equal(HomeworkCardStatus.Pending, Card(b, "raid_cavrak").Status);
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(b, "weekly_heart_holy_water").Status);

        svc.ResetAll(Main, includeAccount: true);
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "weekly_heart_holy_water").Status);
    }

    [Fact]
    public void ImportFromWpf_KeepsOnlyManualCompletionsOfCurrentPeriod_AndDropsDefaultPlayer()
    {
        var wpfDir = Path.Combine(_dir, "wpf");
        Directory.CreateDirectory(wpfDir);
        var nowLocal = _now.LocalDateTime;
        var wpf = new Dictionary<string, object>
        {
            [Main] = new { Items = new Dictionary<string, object>
            {
                ["raid_cavrak"] = new { IsCompleted = true, CurrentCount = 1, CompletedAt = nowLocal, IsAutoDetected = false },
                ["abyss_madness_cave"] = new { IsCompleted = true, CurrentCount = 1, CompletedAt = nowLocal, IsAutoDetected = true },           // 자동 → 버림
                ["daily_tower"] = new { IsCompleted = true, CurrentCount = 1, CompletedAt = nowLocal.AddDays(-2), IsAutoDetected = false }, // 지난 주기 → 버림
                ["weekly_heart_holy_water"] = new { IsCompleted = true, CurrentCount = 1, CompletedAt = nowLocal, IsAutoDetected = false }, // 계정 공통
            } },
            ["Default_Player"] = new { Items = new Dictionary<string, object>
            {
                ["raid_cavrak"] = new { IsCompleted = true, CurrentCount = 1, CompletedAt = nowLocal, IsAutoDetected = false },
            } },
        };
        File.WriteAllText(Path.Combine(wpfDir, HomeworkStore.FileName), JsonSerializer.Serialize(wpf));
        var before = File.ReadAllText(Path.Combine(wpfDir, HomeworkStore.FileName));

        var store = new HomeworkStore(_dir);
        Assert.Equal(1, store.ImportFromWpfIfMissing(wpfDir, _catalog, _now));
        Assert.Equal(0, store.ImportFromWpfIfMissing(wpfDir, _catalog, _now));   // 이미 있으면 다시 안 가져옴

        var svc = new HomeworkService(_catalog, store, () => _now);
        var b = svc.GetBoard(Main);
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(b, "raid_cavrak").Status);
        Assert.Equal(HomeworkCardStatus.Pending, Card(b, "abyss_madness_cave").Status);
        Assert.Equal(HomeworkCardStatus.Pending, Card(b, "daily_tower").Status);
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(svc.GetBoard(Alt), "weekly_heart_holy_water").Status);
        Assert.DoesNotContain("Default_Player", store.Load().Characters.Keys);
        Assert.Equal(before, File.ReadAllText(Path.Combine(wpfDir, HomeworkStore.FileName)));   // 원본 불변
    }

    [Fact]
    public void ReadFailure_DoesNotOverwriteRecords_AndManualChangesFail()
    {
        var svc = NewService();
        svc.Set(Main, "raid_cavrak", completed: true);
        var path = Path.Combine(_dir, HomeworkStore.FileName);
        var before = File.ReadAllText(path);

        var fresh = NewService();   // 캐시 없는 새 인스턴스 (서버 재시작 상황)
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(fresh.Evaluate(Main, new HomeworkObservation(DailyMissions: new[] { M("에린에 돌아왔습니다", 1, 1) })));
            Assert.Throws<IOException>(() => fresh.Set(Main, "abyss_madness_cave", completed: true));
            Assert.Throws<IOException>(() => fresh.ResetAll(Main, includeAccount: true));
            Assert.All(fresh.GetBoard(Main).Cards, c => Assert.Equal(HomeworkCardStatus.Pending, c.Status));   // 화면은 빈 기준으로라도 보여 줌
        }
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(fresh.GetBoard(Main), "raid_cavrak").Status);   // 잠금이 풀리면 다시 읽는다
    }

    [Fact]
    public void Evaluate_WritesOnlyChangedItems_AndSkipsSaveWhenNothingChanged()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(DailyMissions: new[] { M("에린에 돌아왔습니다", 1, 1) }));
        var file = new HomeworkStore(_dir).Load();
        // 빈 상태를 미리 만들지 않는다: 실제로 바뀐 두 항목(접속 1/1, 미션 목록 1개 전부 완료)만 들어간다
        Assert.Equal(new[] { "daily_connect", "daily_missions_all" }, file.Account.Items.Keys.OrderBy(k => k));
        Assert.All(file.Account.Items.Values, v => Assert.True(v.Completed));
        Assert.Empty(file.Characters[Main].Items);

        var path = Path.Combine(_dir, HomeworkStore.FileName);
        var stamp = File.GetLastWriteTimeUtc(path);
        Thread.Sleep(30);
        Assert.Null(svc.Evaluate(Main, new HomeworkObservation(DailyMissions: new[] { M("에린에 돌아왔습니다", 1, 1) })));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Reload_KeepsCaseInsensitiveKeys()
    {
        NewService().Set(Main, "RAID_CAVRAK", completed: true);
        var file = new HomeworkStore(_dir).Load();
        Assert.True(file.Characters.ContainsKey(Main.ToUpperInvariant()) || file.Characters.ContainsKey(Main));
        Assert.True(file.Characters[Main].Items.ContainsKey("Raid_Cavrak"));
    }

    [Fact]
    public void DirectMission_WithZeroGoal_TrustsOnlyCompletedFlag()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(DailyMissions: new[] { new MissionItem("오늘도 던전 한 바퀴", "", 5, 0, false, false) }));
        Assert.Equal(HomeworkCardStatus.Pending, Card(svc.GetBoard(Main), "daily_dungeon_3").Status);
    }

    [Fact]
    public void MissionTotal_CardShowsActualGoal()
    {
        var svc = NewService();
        svc.Evaluate(Main, new HomeworkObservation(WeeklyMissions: Enumerable.Range(0, 12).Select(i => M($"w{i}", 1, 1)).ToList()));
        var c = Card(svc.GetBoard(Main), "weekly_missions_all");
        Assert.Equal((12, 12, HomeworkCardStatus.AutoDone), (c.Count, c.Goal, c.Status));
    }

    [Fact]
    public void Normalize_StripsUnityTagsAndSpaces()
    {
        Assert.Equal("[긴급의뢰]뱅가드브리치", HomeworkText.Normalize("<color=orange>[긴급 의뢰]</color>  뱅가드 브리치"));
        Assert.Equal("[긴급 의뢰] 뱅가드 브리치", HomeworkText.StripTags("<color=orange>[긴급 의뢰]</color> 뱅가드 브리치"));
    }

    private HomeworkCatalog CatalogWithVerifiedVanguard()
    {
        // 실측이 끝났다고 가정한 카탈로그 (뱅가드 브리치 퀘스트 판정 경로 검증용)
        using var s = typeof(HomeworkCatalog).Assembly.GetManifestResourceStream("MobiMate.Homework.homework_catalog.json")!;
        var node = System.Text.Json.Nodes.JsonNode.Parse(s)!;
        foreach (var item in node["items"]!.AsArray())
            if ((string?)item!["id"] == "weekly_vanguard_breach")
            {
                // 이 경로(모든 목표 완료 퀘스트)는 지금 카탈로그에 쓰는 항목이 없어, 뱅가드 항목을 그 방식으로 바꿔 검증한다
                var o = item.AsObject();
                o["mode"] = "questAllObjectives";
                o["questTitles"] = new System.Text.Json.Nodes.JsonArray("[긴급 의뢰] 뱅가드 브리치");
                o.Remove("titleContains"); o.Remove("continuous"); o.Remove("needsMeasurement");
            }
        return HomeworkCatalog.Parse(node.ToJsonString());
    }
}

public class DisplayAndRankingTests
{
    [Theory]
    [InlineData(10995, "3시간 3분 15초")]
    [InlineData(145, "2분 25초")]
    [InlineData(9, "9초")]
    [InlineData(0, "수거 대기")]
    public void RemainingTime_Formats(int seconds, string expected) => Assert.Equal(expected, DisplayFormat.RemainingTime(seconds));

    [Fact]
    public void Nearby_RanksByRelation_ThenCombat_ThenDistance()
    {
        NearPcItem P(string n, long cp, double d, bool party = false, bool friend = false, bool guild = false, bool hasGuild = true) =>
            new("아이라", n, d, 100, "전사", cp, guild, party, friend, false, hasGuild);
        var ranked = NearbyRanker.Rank(new[]
        {
            P("강자", 120_000, 30), P("길드원", 50_000, 5, guild: true), P("친구", 40_000, 9, friend: true),
            P("파티원", 10_000, 50, party: true), P("무소속", 90_000, 2, hasGuild: false), P("무소속2", 90_000, 1, hasGuild: false),
        }, myCombatScore: 88_737);

        Assert.Equal(new[] { "친구", "길드원", "강자", "무소속2", "무소속", "파티원" }, ranked.Select(r => r.Pc.Title));   // IsInParty는 무시: 파티원은 그냥 전투력 순에 들어간다
        Assert.Equal(NearbyRelation.Friend, NearbyRanker.RelationOf(new NearPcItem("아이라", "둘 다", 1, 100, "전사", 1, false, true, true, false, true)));   // 친구이면서 파티원이면 친구
        Assert.Equal(NearbyRelation.Other, NearbyRanker.RelationOf(new NearPcItem("아이라", "파티만", 1, 100, "전사", 1, false, true, false, false, false)));   // IsInParty만으로는 아무 관계도 아니다
        Assert.Equal(NearbyRelation.Guild, NearbyRanker.RelationOf(new NearPcItem("아이라", "파티+길드", 1, 100, "전사", 1, true, true, false, false, true)));
        Assert.True(ranked.Single(r => r.Pc.Title == "강자").IsStronger);
        Assert.Equal("길드 없음", ranked.Single(r => r.Pc.Title == "무소속").RelationLabel);
        Assert.Equal("", ranked.Single(r => r.Pc.Title == "강자").RelationLabel);
        Assert.False(NearbyRanker.Rank(new[] { P("x", 1, 1) }, 0)[0].IsStronger);   // 내 전투력을 모르면 표시 안 함
    }
}

public class PersonaGeneratorTests
{
    [Fact]
    public void TryParse_ReadsFencedOrBareJson_AndRejectsIncomplete()
    {
        var fenced = PersonaGenerator.TryParse("좋아요!\n```json\n{\"name\":\"공주기사 스타일\",\"emoji\":\"🛡️\",\"prompt\":\"기품 있게 말한다.\"}\n```");
        Assert.Equal("공주기사 스타일", fenced!.Name);
        Assert.True(fenced.FromAi);

        Assert.NotNull(PersonaGenerator.TryParse("{\"name\":\"a\",\"prompt\":\"b\"}"));
        Assert.Null(PersonaGenerator.TryParse("{\"name\":\"a\"}"));
        Assert.Null(PersonaGenerator.TryParse("그냥 문장"));
        Assert.Equal("🎭", PersonaGenerator.TryParse("{\"name\":\"a\",\"prompt\":\"b\"}")!.Emoji);
    }

    [Fact]
    public async Task BuiltInEngine_UsesRuleBasedFallback()
    {
        var draft = await PersonaGenerator.GenerateAsync("공주기사 느낌", new BuiltInGuideEngine());
        Assert.False(draft.FromAi);
        Assert.Equal("공주기사 느낌 스타일", draft.Name);
        Assert.Equal("🛡️", draft.Emoji);
        Assert.Contains("기품", draft.Prompt);
    }

    [Fact]
    public async Task FailingAiEngine_FallsBack()
    {
        var draft = await PersonaGenerator.GenerateAsync("츤데레 메이드", new CliAgentEngine("claude", @"Z:\없는\claude.exe", "Claude"));
        Assert.False(draft.FromAi);
        Assert.Equal("🎀", draft.Emoji);
    }
}
