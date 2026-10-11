using System.Diagnostics;

namespace MobiMateHomeWork.Tests;

/// <summary>
/// 공유 저장 패치: 같은 저장 폴더를 쓰는 다른 프로그램(웹앱)이 공유 파일을 바꾸면 감지해 화면이 듣는 알림으로 알린다. 자기 쓰기는 알리지 않는다.
/// 웹앱 ExternalChangeTests의 두 시나리오(SSE homework.changed / state.changed)를 AppEvents.HomeworkChanged(External) / CharactersChanged로 다시 썼다.
/// 감시 신호는 ExternalChangeMonitor.Check()를 직접 불러 대신한다(감시기 연결 자체는 마지막 테스트에서 따로 본다).
/// 다른 프로그램은 같은 폴더를 가리키는 별개의 저장소 객체, 또는 실제 별개 프로세스(SharedStorageHarness)로 흉내 낸다.
/// </summary>
public class ExternalChangeTests
{
    private static string ManualId(HomeworkCatalog c) => c.Items.First(d => d.Active && d.Share == HomeworkShare.Character).Id;

    [Fact]
    public async Task ExternalHomeworkWrite_IsDetected_AndBroadcastAsHomeworkChanged()
    {
        await using var h = await AppHarness.StartAsync();
        var id = ManualId(h.Catalog);
        h.Homework.Set("아이라_격투가", id, completed: true);       // 이 앱 자신의 쓰기: 알리지 않는다
        h.External.Check();
        Assert.Empty(h.Log.Homework);

        // 웹앱 역할: 같은 폴더를 가리키는 별개의 서비스 객체가 쓴다
        var web = new HomeworkService(h.Catalog, new HomeworkStore(h.StorageDir));
        web.Set("아이라_석궁사수", id, completed: true);

        h.External.Check();
        var e = Assert.Single(h.Log.Homework);
        Assert.Equal(HomeworkChangeReason.External, e.Reason);          // 첫 이벤트가 외부 변경이어야 한다(자기 쓰기 이벤트는 없다)
        Assert.True(h.Homework.GetBoard("아이라_석궁사수").Cards.Single(x => x.Id == id).IsDone);   // 이 앱이 따라잡았다
        Assert.True(h.Homework.GetBoard("아이라_격투가").Cards.Single(x => x.Id == id).IsDone);     // 자기 쓰기도 잃지 않았다

        h.External.Check();                                             // 같은 변경을 두 번 알리지 않는다
        h.External.Check();
        Assert.Single(h.Log.Homework);
    }

    [Fact]
    public async Task ExternalWatchOff_ReadsStillSeeTheChange()
    {
        await using var h = await AppHarness.StartAsync();   // 감시 끔(하니스 기본)
        var mine = h.Accounts;
        new AccountStore(h.StorageDir).Update(d => { d.Assign["c_d"] = new AccountAssignment { Account = "y" }; return true; });
        Assert.True(mine.Snapshot().Assign.ContainsKey("c_d"));   // 조회 경로가 바뀐 파일을 다시 읽는다
        Assert.Empty(h.Log.Homework);                              // 감시가 꺼져 있으니 알림은 없다
    }

    [Fact]
    public async Task OwnCommandWrite_RaisesOnlyItsManualNotification_NeverAnExternalOne()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        Assert.True(h.Commands.SetHomework("raid_cavrak", true, null).Ok);
        h.External.Check();
        var e = Assert.Single(h.Log.Homework);
        Assert.Equal(HomeworkChangeReason.Manual, e.Reason);
    }

    [Fact]
    public async Task ExternalAccountWrite_IsBroadcastAsCharactersChanged()
    {
        await using var h = await AppHarness.StartAsync();
        Assert.True(h.Accounts.Update(d => { d.Assign["a_b"] = new AccountAssignment { Account = "x", Manual = true }; return true; }));   // 자기 쓰기
        h.External.Check();
        var before = h.Log.CharactersChanged;                            // 자기 쓰기는 알림을 만들지 않는다
        Assert.Equal(0, before);

        var other = new AccountStore(h.StorageDir);
        Assert.True(other.Update(d => { d.Assign["c_d"] = new AccountAssignment { Account = "y", Manual = true }; return true; }));

        h.External.Check();
        Assert.Equal(before + 1, h.Log.CharactersChanged);
        var assign = h.Accounts.Snapshot().Assign;
        Assert.True(assign.ContainsKey("a_b") && assign.ContainsKey("c_d"), "두 쪽의 지정이 모두 남아야 한다");

        h.External.Check();
        Assert.Equal(before + 1, h.Log.CharactersChanged);               // 다시 알리지 않는다
    }

    [Fact]
    public async Task ExternalCharacterRecordWrite_IsBroadcastAsCharactersChanged_AndVisibleInTheList()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        Assert.True(h.Commands.RenameCharacter("아이라_격투가", "내 별칭").Ok);   // 자기 쓰기
        var before = h.Log.CharactersChanged;
        h.External.Check();
        Assert.Equal(before, h.Log.CharactersChanged);

        var web = new SnapshotManager(h.StorageDir);
        web.SetCustomName("바람_도적", "바람", "도적", "웹에서 만든 기록");
        web.UpdateSnapshot(new CharacterInfo("", "바람", 100, "도적", new ScoreVal("전투력", 99000), new ScoreVal("생활력", 1000), null, null, null, null, null,
            null, null, null, null, null, null, null, null), [new CurrencyItem("골드", 3_000_000)], null, "바람_도적");

        h.External.Check();
        Assert.Equal(before + 1, h.Log.CharactersChanged);
        Assert.NotNull(h.Snapshots.GetProfileByKey("바람_도적"));
        Assert.Equal("내 별칭", h.Snapshots.GetProfileByKey("아이라_격투가")!.CustomName);   // 이쪽 기록도 그대로
    }

    [Fact]
    public async Task NothingChanged_MeansNoNotification()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var chars = h.Log.CharactersChanged;
        for (var i = 0; i < 3; i++) h.External.Check();
        Assert.Empty(h.Log.Homework);
        Assert.Equal(chars, h.Log.CharactersChanged);
    }

    [Fact]
    public async Task ChecksFromAnotherProcess_AreSeenAsOneExternalHomeworkChange_WithNothingLost()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        Assert.True(h.Commands.SetHomework("raid_cavrak", true, null).Ok);

        // 실제 별개 프로세스가 같은 폴더에 숙제·캐릭터 기록·계정 지정을 번갈아 쓴다
        var (exit, output) = RunHarness(h.StorageDir, "A", 9);
        Assert.True(exit == 0, output);

        var before = h.Log.Homework.Count;
        var chars = h.Log.CharactersChanged;
        h.External.Check();
        Assert.Equal(before + 1, h.Log.Homework.Count);
        Assert.Equal(HomeworkChangeReason.External, h.Log.Homework[^1].Reason);
        Assert.Equal(chars + 1, h.Log.CharactersChanged);

        Assert.Equal(HomeworkCardStatus.ManualDone, h.Homework.GetBoard("아이라_격투가").Cards.Single(c => c.Id == "raid_cavrak").Status);   // 이쪽 체크가 남았다
        Assert.Contains(h.Homework.GetBoard("shared_char").Cards, c => c.IsDone);                                                        // 저쪽 체크가 보인다
        Assert.NotNull(h.Snapshots.GetProfileByKey("류트_A1"));                                                                             // 저쪽 캐릭터 기록
        Assert.True(h.Accounts.Snapshot().Assign.ContainsKey("acct_A_2"));                                                                 // 저쪽 계정 지정
    }

    [Fact]
    public async Task TheWatcher_DeliversAnExternalWriteWithoutAnyoneCallingCheck()
    {
        // 감시기 연결 확인: 감시(짧은 묶음 시간) + 폴링을 켜고, 다른 저장소 객체가 쓰면 알림이 온다. 도착을 조건으로 기다린다.
        await using var h = await AppHarness.StartAsync(configure: o =>
        {
            o.ExternalWatch = true;
            o.ExternalWatchDebounceSeconds = 0.05;
            o.ExternalWatchPollSeconds = 0.5;
        });
        var id = ManualId(h.Catalog);
        h.Homework.Set("아이라_격투가", id, completed: true);           // 자기 쓰기

        var web = new HomeworkService(h.Catalog, new HomeworkStore(h.StorageDir));
        web.Set("아이라_석궁사수", id, completed: true);

        await Wait.UntilAsync(() => h.Log.Homework.Any(x => x.Reason == HomeworkChangeReason.External), "감시기가 외부 변경을 전달");
        Assert.True(h.Homework.GetBoard("아이라_석궁사수").Cards.Single(x => x.Id == id).IsDone);
        Assert.DoesNotContain(h.Log.Homework, x => x.Reason != HomeworkChangeReason.External);   // 자기 쓰기는 한 번도 알리지 않았다
    }

    private static (int Exit, string Output) RunHarness(string dir, string role, int count)
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var config = Path.GetFileName(Path.GetDirectoryName(baseDir)!);
        var exe = Path.Combine(FakeGame.FindToolsDir("SharedStorageHarness"), "bin", config, "net8.0", "SharedStorageHarness.exe");
        Assert.True(File.Exists(exe), "SharedStorageHarness가 빌드되지 않았습니다: " + exe);
        using var p = Process.Start(new ProcessStartInfo(exe, $"\"{dir}\" {role} {count}")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(60_000), "하네스가 끝나지 않았습니다.");
        return (p.ExitCode, text);
    }
}
