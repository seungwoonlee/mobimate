namespace MobiMateHomeWork.Tests;

/// <summary>
/// 화면이 부르는 쓰기 동작 (웹앱 PUT /homework/{id}, PUT /profile/nickname 처리): 숙제 수동 설정 키 규칙·필드 보스·단계 카운터와 닉네임 바꾸기.
/// 웹앱 ApiTests의 Homework_SetForAnotherCharacter_RequiresKnownCharacterKey·Homework_ListSetAndReset 중 수동 체크 부분을 포함한다.
/// </summary>
public class HomeworkCommandsTests
{
    private const string Aira = "아이라_격투가";

    private static string SeedRogue()
    {
        var dir = AppHarness.NewStorageDir();
        var seen = DateTime.Now.AddDays(-1);
        Seed.Write(dir, Seed.Profile("바람", "도적", seen, Seed.Rec(seen, 100, 99000, 777, 888, 100, 4, gold: 3_000_000, living: 24000)));
        return dir;
    }

    private static HomeworkCard Card(AppHarness h, string key, string id) => h.Homework.GetBoard(key).Cards.Single(c => c.Id == id);

    // ── SetHomework: 대상 캐릭터 규칙 ──

    [Fact]
    public async Task Set_NeedsCompletedOrCount()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var r = h.Commands.SetHomework("raid_cavrak", null, null);
        Assert.Equal(CommandError.Validation, r.Error);
        Assert.Equal(CommandError.Validation, h.Commands.SetHomework("raid_cavrak", null, null, "없는서버_없는직업").Error);   // 키가 있어도 값이 없으면 검증이 먼저
        Assert.Empty(h.Log.Homework);
    }

    [Fact]
    public async Task Set_WithoutKey_BeforeAnyCharacterIsKnown_Fails()
    {
        await using var h = await AppHarness.StartAsync();   // 아직 한 번도 읽지 않음
        var r = h.Commands.SetHomework("raid_cavrak", true, null);
        Assert.Equal(CommandError.NoCharacter, r.Error);
        Assert.False(r.Ok);
        Assert.Empty(h.Log.Homework);
        Assert.Empty(h.Snapshots.GetAllProfiles());
    }

    [Fact]
    public async Task Set_WithoutKey_UsesTheCurrentCharacter_IsIdempotent_AndNotifies()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        Assert.Equal(HomeworkCardStatus.Pending, Card(h, Aira, "raid_cavrak").Status);

        var r = h.Commands.SetHomework("raid_cavrak", true, null);
        Assert.True(r.Ok);
        Assert.Equal(Aira, r.Change!.CharacterKey);
        Assert.Equal(HomeworkChangeReason.Manual, r.Change.Reason);
        Assert.Equal(new[] { "raid_cavrak" }, r.Change.Ids);
        var evt = Assert.Single(h.Log.Homework);                         // 화면 알림이 한 번 올라갔다
        Assert.Equal(Aira, evt.CharacterKey);

        Assert.True(h.Commands.SetHomework("raid_cavrak", true, null).Ok);   // 멱등: 목표 상태를 지정하므로 다시 눌러도 같다
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(h, Aira, "raid_cavrak").Status);

        Assert.True(h.Commands.SetHomework("raid_cavrak", false, null).Ok);  // 해제
        Assert.Equal(HomeworkCardStatus.Pending, Card(h, Aira, "raid_cavrak").Status);
    }

    [Fact]
    public async Task Set_WithAKey_RejectsAnUnknownCharacter_AndCreatesNothing()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var r = h.Commands.SetHomework("raid_cavrak", true, null, "없는서버_없는직업");
        Assert.Equal(CommandError.UnknownCharacter, r.Error);
        Assert.Null(r.Change);
        Assert.Empty(h.Log.Homework);
        Assert.DoesNotContain(h.Homework.GetAutoStatuses(["없는서버_없는직업"])["없는서버_없는직업"], x => x.State == "done");
        Assert.Equal(CommandError.UnknownCharacter, h.Commands.SetHomework("raid_cavrak", true, null, "아이라_ 격투가").Error);   // 비슷한 키도 안 된다
    }

    [Fact]
    public async Task Set_WithAKey_AcceptsAKnownCharacterWhoIsNotConnected_WithoutTouchingTheCurrentOne()
    {
        var dir = SeedRogue();
        try
        {
            await using var h = await AppHarness.StartAsync(dir);
            await h.RecordAsync();
            var r = h.Commands.SetHomework("raid_cavrak", true, null, "바람_도적");
            Assert.True(r.Ok);
            Assert.Equal("바람_도적", r.Change!.CharacterKey);
            Assert.Equal(HomeworkCardStatus.ManualDone, Card(h, "바람_도적", "raid_cavrak").Status);
            Assert.Equal(HomeworkCardStatus.Pending, Card(h, Aira, "raid_cavrak").Status);   // 접속 중 캐릭터의 숙제는 그대로
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Set_WithAKey_AcceptsTheCurrentCharacter_EvenWithoutAProfile()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        Assert.True(h.Snapshots.RemoveCharacter(Aira));                  // 기록이 없는 상태(지웠거나 아직 기록 전)
        Assert.False(h.Session.IsKnownCharacter(Aira));
        Assert.True(h.Session.IsCurrentCharacter(Aira));

        var r = h.Commands.SetHomework("raid_cavrak", true, null, Aira);
        Assert.True(r.Ok);                                               // 접속 중인 캐릭터면 프로필이 없어도 허용
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(h, Aira, "raid_cavrak").Status);
    }

    [Fact]
    public async Task Set_UnknownHomework_IsRejected()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var r = h.Commands.SetHomework("nope", true, null);
        Assert.Equal(CommandError.UnknownHomework, r.Error);
        Assert.Contains("nope", r.Message);
        Assert.Empty(h.Log.Homework);
    }

    // ── 필드 보스(택1)·단계 카운터 ──

    [Fact]
    public async Task FieldBoss_OnePerWeek_ShowsWhichBossWasCleared_AndTheOthersAsPoolDone()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        Assert.Equal("unknown", h.Session.Characters().Card(Aira).FieldBoss.State);

        Assert.True(h.Commands.SetHomework("fieldboss_peri", true, null).Ok);
        var title = h.Catalog.Find("fieldboss_peri")!.Title;
        var boss = h.Session.Characters().Card(Aira).FieldBoss;
        Assert.Equal("done", boss.State);
        Assert.Equal(title, boss.Item);

        // 같은 풀(택1)의 다른 보스 칸은 완료로 보인다
        Assert.Equal(HomeworkCardStatus.ManualDone, Card(h, Aira, "fieldboss_peri").Status);
        Assert.Equal(HomeworkCardStatus.PoolDone, Card(h, Aira, "fieldboss_krama").Status);

        Assert.True(h.Commands.SetHomework("fieldboss_peri", false, null).Ok);
        Assert.Equal("todo", h.Session.Characters().Card(Aira).FieldBoss.State);   // 이번 주기 기록은 있는데 넘긴 보스가 없음
        Assert.Null(h.Session.Characters().Card(Aira).FieldBoss.Item);
    }

    [Fact]
    public async Task StepCounter_SetsCountWithinZeroAndGoal()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        const string id = "weekly_barrier_1_7";
        var goal = h.Catalog.Find(id)!.Goal;
        Assert.Equal(7, goal);

        Assert.True(h.Commands.SetHomework(id, null, 3).Ok);
        Assert.Equal((3, false), (Card(h, Aira, id).Count, Card(h, Aira, id).IsDone));

        Assert.True(h.Commands.SetHomework(id, null, goal).Ok);
        Assert.Equal((goal, true), (Card(h, Aira, id).Count, Card(h, Aira, id).IsDone));

        Assert.True(h.Commands.SetHomework(id, null, 99).Ok);            // 넘치면 목표로 맞춘다
        Assert.Equal(goal, Card(h, Aira, id).Count);

        Assert.True(h.Commands.SetHomework(id, null, -4).Ok);            // 모자라면 0
        Assert.Equal((0, false), (Card(h, Aira, id).Count, Card(h, Aira, id).IsDone));

        Assert.True(h.Commands.SetHomework(id, true, null).Ok);          // completed=true는 목표 횟수 완료
        Assert.Equal((goal, true), (Card(h, Aira, id).Count, Card(h, Aira, id).IsDone));
    }

    [Fact]
    public async Task Set_IsSavedInTheSharedFile_SoAnotherInstanceSeesIt()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        h.Commands.SetHomework("raid_cavrak", true, null);

        var other = new HomeworkService(HomeworkCatalog.LoadEmbedded(), new HomeworkStore(h.StorageDir));   // 같은 폴더를 보는 별개 객체(예: 웹앱)
        Assert.Equal(HomeworkCardStatus.ManualDone, other.GetBoard(Aira).Cards.Single(c => c.Id == "raid_cavrak").Status);
    }

    [Fact]
    public async Task Set_WhenTheSharedFileCannotBeWritten_ReportsStorageUnavailable_AndKeepsTheOldRecord()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        Assert.True(h.Commands.SetHomework("raid_cavrak", true, null).Ok);
        var path = Path.Combine(h.StorageDir, HomeworkStore.FileName);
        var before = File.ReadAllBytes(path);

        CommandResult? result;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))    // 다른 프로그램이 쥐고 있다
            result = ToResult(h.Commands.SetHomework("raid_airel", true, null));
        Assert.Equal(CommandError.StorageUnavailable, result.Error);
        Assert.Single(h.Log.Homework);                                   // 실패는 알리지 않는다

        Assert.Equal(before, File.ReadAllBytes(path));                   // 파일은 그대로
        Assert.True(h.Commands.SetHomework("raid_airel", true, null).Ok);   // 풀리면 정상
    }

    private static CommandResult ToResult(HomeworkCommandResult r) => new(r.Error, r.Message);

    // ── 닉네임 ──

    [Fact]
    public async Task Rename_SetsTheNickname_ShowsOnCardAndHeader_AndIsSavedInTheSharedFile()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var changed = h.Log.CharactersChanged;

        var r = h.Commands.RenameCharacter(Aira, "  주력 캐릭터 ");
        Assert.True(r.Ok);
        Assert.Equal(changed + 1, h.Log.CharactersChanged);               // 화면 알림
        Assert.Equal("주력 캐릭터", h.Session.Characters().Card(Aira).Nickname);   // 앞뒤 공백 제거

        await h.Session.HeaderAsync(CancellationToken.None);
        Assert.Equal("주력 캐릭터", h.Log.Headers[^1].Nickname);

        var other = new SnapshotManager(h.StorageDir);                    // 공유 파일에서 다시 읽어도 남아 있다
        Assert.Equal("주력 캐릭터", other.GetProfileByKey(Aira)!.CustomName);
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public async Task Rename_LimitsTheLengthToTwentyCharacters(int length, bool ok)
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var changed = h.Log.CharactersChanged;
        var r = h.Commands.RenameCharacter(Aira, new string('가', length));
        Assert.Equal(ok, r.Ok);
        if (!ok)
        {
            Assert.Equal(CommandError.Validation, r.Error);
            Assert.Contains("20", r.Message);
            Assert.Equal(changed, h.Log.CharactersChanged);               // 실패는 알리지 않는다
            Assert.Null(h.Session.Characters().Card(Aira).Nickname);
        }
        else Assert.Equal(new string('가', 20), h.Session.Characters().Card(Aira).Nickname);
    }

    [Fact]
    public async Task Rename_CountsLengthAfterTrimming()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        Assert.True(h.Commands.RenameCharacter(Aira, "  " + new string('나', 20) + "  ").Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Rename_BlankClearsTheNickname(string? blank)
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        h.Commands.RenameCharacter(Aira, "별칭");
        Assert.Equal("별칭", h.Session.Characters().Card(Aira).Nickname);

        Assert.True(h.Commands.RenameCharacter(Aira, blank).Ok);
        Assert.Null(h.Session.Characters().Card(Aira).Nickname);
        Assert.Equal("", new SnapshotManager(h.StorageDir).GetProfileByKey(Aira)!.CustomName);
    }

    [Fact]
    public async Task Rename_RejectsABlankKey_AndAnUnknownCharacter_WithoutCreatingARecord()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var changed = h.Log.CharactersChanged;

        Assert.Equal(CommandError.Validation, h.Commands.RenameCharacter("  ", "별칭").Error);
        Assert.Equal(CommandError.Validation, h.Commands.RenameCharacter("", "별칭").Error);
        var unknown = h.Commands.RenameCharacter("없는서버_없는직업", "별칭");
        Assert.Equal(CommandError.UnknownCharacter, unknown.Error);

        Assert.Equal(changed, h.Log.CharactersChanged);
        Assert.Null(h.Snapshots.GetProfileByKey("없는서버_없는직업"));
        Assert.Single(h.Snapshots.GetAllProfiles());
    }

    [Fact]
    public async Task Rename_CanNameAnyKnownCharacter_NotOnlyTheConnectedOne()
    {
        var dir = SeedRogue();
        try
        {
            await using var h = await AppHarness.StartAsync(dir);
            await h.RecordAsync();
            Assert.True(h.Commands.RenameCharacter("바람_도적", "부캐").Ok);
            var card = h.Session.Characters().Card("바람_도적");
            Assert.Equal("부캐", card.Nickname);
            Assert.False(card.IsCurrent);
            Assert.Null(h.Session.Characters().Card(Aira).Nickname);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Rename_TheConnectedCharacterWithoutAProfile_CreatesItFromTheLiveReading()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        h.Snapshots.RemoveCharacter(Aira);

        Assert.True(h.Commands.RenameCharacter(Aira, "별칭").Ok);
        var p = h.Snapshots.GetProfileByKey(Aira)!;
        Assert.Equal(("아이라", "격투가", "별칭"), (p.RealmName, p.JobName, p.CustomName));
    }
}
