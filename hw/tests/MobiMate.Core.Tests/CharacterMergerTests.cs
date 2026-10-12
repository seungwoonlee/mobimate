namespace MobiMate.Core.Tests;

/// <summary>둘로 갈라진 같은 캐릭터를 합친다 (2026-10-12 아이라_전사 / 아이라_전사#2)</summary>
public class CharacterMergerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-merge-" + Guid.NewGuid().ToString("N"));
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private readonly JsonFileStore _json = new();
    private const string Orig = "아이라_전사", Dup = "아이라_전사#2";
    // 2026-10-12(월) 10:05 KST: 이날 06:00에 일일·주간 리셋이 모두 지났다
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 12, 10, 5, 0, TimeSpan.FromHours(9)).ToUniversalTime();
    private static readonly DateTimeOffset Yesterday = Now.AddDays(-1);

    public CharacterMergerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private HomeworkDefinition Daily => _catalog.Items.First(d => d.Active && d.Period == HomeworkPeriod.Daily && d.Share == HomeworkShare.Character);
    private HomeworkDefinition Weekly => _catalog.Items.First(d => d.Active && d.Period == HomeworkPeriod.Weekly && d.Share == HomeworkShare.Character);

    private static CharacterHistoryRecord Rec(DateTime t, long combat, long living = 18939) =>
        new() { Timestamp = t, Level = 100, CombatScore = combat, LivingScore = living, Deca = 13682, MCash = 910, Gold = 31864299, Title = "전사" };

    private static CharacterProfile Profile(string key, string custom, DateTime first, DateTime last, params CharacterHistoryRecord[] recs) =>
        new() { CharacterKey = key, RealmName = "아이라", JobName = "전사", CustomName = custom, FirstSeen = first, LastSeen = last, History = recs.ToList() };

    private static HomeworkItemState Done(DateTimeOffset at, bool manual = true) =>
        new() { Completed = true, Count = 1, CompletedAtUtc = at, ManualOverride = manual, ManualAtUtc = manual ? at : null };

    private void Seed()
    {
        var t0 = new DateTime(2026, 10, 11, 4, 45, 0);
        var t1 = new DateTime(2026, 10, 12, 10, 5, 4);
        _json.Save(Path.Combine(_dir, "character_history_db.json"), new List<CharacterProfile>
        {
            Profile(Orig, "빅클라우드", new DateTime(2026, 9, 1), t0, Rec(new DateTime(2026, 10, 10, 1, 0, 0), 104000), Rec(t0, 105479)),
            Profile(Dup, "", t1, t1.AddMinutes(30), Rec(t1, 102926, 18733), Rec(t1.AddSeconds(9), 105562, 18733), Rec(t0, 105479)),   // t0는 양쪽에 있는 같은 시각
        });
        _json.Save(Path.Combine(_dir, "character_snapshots.json"), new List<CharacterSnapshot>
        {
            new() { RealmName = "아이라", JobName = "전사", Level = 100, CombatScore = 105479, Timestamp = new DateTime(2026, 10, 11, 4, 45, 0) },
            new() { RealmName = "아이라", JobName = "전사", KeySuffix = "#2", Level = 100, CombatScore = 105562, Timestamp = new DateTime(2026, 10, 12, 10, 5, 13) },
        });

        var hw = new HomeworkStore(_dir);
        var file = new HomeworkFile { LastObservedCharacter = Dup };
        // 원래 키: 어제 주기 기록(일일 리셋 전), 주간은 지난주 값
        var old = new HomeworkLedger { LastDailyResetUtc = KstClock.LastDailyReset(Yesterday), LastWeeklyResetUtc = KstClock.LastWeeklyReset(Yesterday.AddDays(-7)) };
        old.Items[Daily.Id] = Done(Yesterday);
        old.Items[Weekly.Id] = Done(Yesterday.AddDays(-5));
        // #2: 오늘 체크
        var today = new HomeworkLedger { LastDailyResetUtc = KstClock.LastDailyReset(Now), LastWeeklyResetUtc = KstClock.LastWeeklyReset(Now) };
        today.Items[Daily.Id] = Done(Now.AddMinutes(-3));
        today.QuestSightings = new() { ["q"] = Now.AddMinutes(-5) };
        file.Characters[Orig] = old;
        file.Characters[Dup] = today;
        Assert.True(hw.Save(file));

        var accounts = new AccountData();
        accounts.Accounts["acc-1"] = new AccountInfo { MembershipExpiresAtUtc = Now.UtcDateTime.AddDays(10) };
        accounts.Assign[Orig] = new AccountAssignment { Account = "acc-1", Manual = true };
        accounts.Assign[Dup] = new AccountAssignment { Account = "acc-2" };
        accounts.Order = ["acc-1", "acc-2"];
        _json.Save(Path.Combine(_dir, "accounts.json"), accounts);
    }

    private CharacterMerger Merger() => new(_dir, _catalog, () => Now);

    private Dictionary<string, string> Files() => Directory.GetFiles(_dir, "*.json").ToDictionary(f => f, File.ReadAllText);

    [Fact]
    public void Merge_TodayChecksOnDuplicateSurviveOnTheOriginalKey_AndNothingOfTheDuplicateRemains()
    {
        Seed();
        var otherInstance = new SnapshotManager(_dir);   // 합치기 전에 열려 있던 다른 인스턴스(웹앱 역할)
        Assert.NotNull(otherInstance.GetProfileByKey(Dup));

        var r = Merger().Merge(Dup, Orig);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(3, r.HistoryRecords);

        // 기록 DB: 시각순 합침, 같은 시각 하나, 닉네임 유지, 처음/마지막 본 시각 (열려 있던 인스턴스도 바로 일관되게 읽는다)
        foreach (var snaps in new[] { otherInstance, new SnapshotManager(_dir) })
        {
            Assert.Null(snaps.GetProfileByKey(Dup));
            var p = snaps.GetProfileByKey(Orig)!;
            Assert.Equal("빅클라우드", p.CustomName);
            Assert.Equal(4, p.History.Count);   // 2 + 3 - 같은 시각 1
            Assert.Equal(p.History.OrderBy(h => h.Timestamp).Select(h => h.Timestamp), p.History.Select(h => h.Timestamp));
            Assert.Equal(new DateTime(2026, 9, 1), p.FirstSeen);
            Assert.Equal(new DateTime(2026, 10, 12, 10, 35, 4), p.LastSeen);
            Assert.DoesNotContain(snaps.GetAllProfiles(), x => x.CharacterKey == Dup);
        }

        // 스냅숏: 더 최근 것이 원래 키로 남는다
        var snapList = _json.Load<List<CharacterSnapshot>>(Path.Combine(_dir, "character_snapshots.json"))!;
        var snap = Assert.Single(snapList);
        Assert.Equal(Orig, snap.GetKey());
        Assert.Equal(105562, snap.CombatScore);

        // 숙제: 오늘 체크가 원래 키에 남고(어제 일일 기록은 리셋), 지난주 주간 기록은 사라진다
        var svc = new HomeworkService(_catalog, new HomeworkStore(_dir), () => Now);
        var board = svc.GetBoard(Orig);
        Assert.Equal(HomeworkCardStatus.ManualDone, board.Cards.Single(c => c.Id == Daily.Id).Status);
        Assert.NotEqual(HomeworkCardStatus.ManualDone, board.Cards.Single(c => c.Id == Weekly.Id).Status);
        var file = new HomeworkStore(_dir).Load();
        Assert.False(file.Characters.ContainsKey(Dup));
        Assert.Equal(Orig, file.LastObservedCharacter);
        Assert.True(file.Characters[Orig].QuestSightings!.ContainsKey("q"));

        // 계정: 원래 키의 지정(수동)이 유지되고 #2의 흔적은 없다
        var acc = new AccountStore(_dir).Snapshot();
        Assert.Equal("acc-1", acc.Assign[Orig].Account);
        Assert.True(acc.Assign[Orig].Manual);
        Assert.False(acc.Assign.ContainsKey(Dup));
        Assert.NotNull(acc.Accounts["acc-1"].MembershipExpiresAtUtc);
        Assert.DoesNotContain(Dup, string.Join("\n", Files().Values));   // 어디에도 #2 키가 없다
    }

    [Fact]
    public void Merge_AssignmentMovesOnlyWhenTheTargetHasNone()
    {
        Seed();
        var acc = new AccountStore(_dir);
        Assert.True(acc.Update(d => d.Assign.Remove(Orig)));
        Assert.True(Merger().Merge(Dup, Orig).Ok);
        var a = new AccountStore(_dir).Snapshot();
        Assert.Equal("acc-2", a.Assign[Orig].Account);
        Assert.False(a.Assign.ContainsKey(Dup));
    }

    [Fact]
    public void Merge_BothLedgersValid_CompleteWins_AndEarlierCompletionIsKept()
    {
        var from = new HomeworkLedger { LastDailyResetUtc = KstClock.LastDailyReset(Now), LastWeeklyResetUtc = KstClock.LastWeeklyReset(Now) };
        var into = new HomeworkLedger { LastDailyResetUtc = from.LastDailyResetUtc, LastWeeklyResetUtc = from.LastWeeklyResetUtc };
        var d = Daily.Id; var w = Weekly.Id;
        from.Items[d] = Done(Now.AddHours(-2));                                  // 완료(이른)
        into.Items[d] = Done(Now.AddHours(-1));                                  // 완료(늦은)
        from.Items[w] = new HomeworkItemState { Completed = false, Count = 0, ManualOverride = true, ManualAtUtc = Now.AddHours(-1) };   // 수동 미완료
        into.Items[w] = Done(Now.AddHours(-3), manual: false);                   // 완료
        from.QuestContinuityBroken = new HashSet<string> { "x" };
        CharacterMerger.MergeLedgers(from, into, _catalog, Now);
        Assert.Equal(Now.AddHours(-2), into.Items[d].CompletedAtUtc);
        Assert.True(into.Items[w].Completed);                                    // 완료가 이긴다
        Assert.Contains("x", into.QuestContinuityBroken!);
    }

    [Fact]
    public void Merge_Twice_IsHarmless_AndMissingTargetFails_WithoutChanges()
    {
        Seed();
        Assert.True(Merger().Merge(Dup, Orig).Ok);
        var snapshot = Files();
        var again = Merger().Merge(Dup, Orig);
        Assert.True(again.Ok);
        Assert.Contains("이미 없습니다", again.Message);
        Assert.Equal(snapshot, Files());   // 파일이 그대로

        Seed();
        var seeded = Files();
        var bad = Merger().Merge(Dup, "아이라_없음");
        Assert.False(bad.Ok);
        Assert.Equal(seeded, Files());   // 아무것도 바뀌지 않았다
        Assert.False(Merger().Merge(Orig, Orig).Ok);
    }

    [Fact]
    public void Merge_WhenAFileIsLocked_ChangesNothing()
    {
        Seed();
        var before = Files();
        using (new FileStream(Path.Combine(_dir, "homework_records.json"), FileMode.Open, FileAccess.Read, FileShare.None))   // 다른 프로그램이 배타적으로 열어 둠
        {
            var r = Merger().Merge(Dup, Orig);
            Assert.False(r.Ok);
        }
        Assert.Equal(before, Files());
        Assert.True(Merger().Merge(Dup, Orig).Ok);   // 풀린 뒤 다시 시도하면 된다
    }

    [Fact]
    public void Preview_CountsWhatWillBeMerged()
    {
        Seed();
        var p = Merger().Preview(Dup, Orig);
        Assert.True(p.FromExists && p.IntoExists && p.HasSnapshot && p.HasAssignment);
        Assert.Equal(3, p.HistoryRecords);
        Assert.Equal(1, p.HomeworkItems);
    }
}
