using MobiMateHomeWork.ViewModels;
using static MobiMateHomeWork.Tests.BoardKit;

namespace MobiMateHomeWork.Tests;

/// <summary>
/// 캐릭터 합치기 (2026-10-12): 일회 실행 모드 --merge-character, 접속 중 캐릭터 키 바꾸기(CharacterSession), 화면 모델의 후보·확인·실행.
/// 저장소 합치기 규칙 자체는 Core의 CharacterMergerTests가 검증한다.
/// </summary>
public class MergeCharacterTests
{
    private const string Orig = "아이라_전사", Dup = "아이라_전사#2";

    private static void SeedTwo(string dir)
    {
        var t = DateTime.Now.AddHours(-2);
        var a = Seed.Profile("아이라", "전사", t, Seed.Rec(t.AddDays(-1), 100, 105479, 13682, 910, 10, 1), Seed.Rec(t.AddHours(-1), 100, 105600, 13682, 910, 10, 1));
        a.CustomName = "빅클라우드";
        var b = Seed.Profile("아이라", "전사", t, Seed.Rec(t, 100, 102926, 13982, 910, 10, 1));
        b.CharacterKey = Dup;
        Seed.Write(dir, a, b);
    }

    // ── 실행 인자 ──

    [Fact]
    public void Parse_ReadsFromIntoAndStorage_InAnyForm()
    {
        var p = MergeCommand.Parse([MergeCommand.Switch, Dup, Orig, "--storage", @"C:\temp\x"], out var err)!;
        Assert.Null(err);
        Assert.Equal((Dup, Orig), (p.From, p.Into));
        Assert.Equal(Path.GetFullPath(@"C:\temp\x"), p.StorageDir);

        var q = MergeCommand.Parse(["--storage=D:/y", "--MERGE-CHARACTER", Dup, Orig], out _)!;
        Assert.Equal(Path.GetFullPath("D:/y"), q.StorageDir);

        var d = MergeCommand.Parse([MergeCommand.Switch, Dup, Orig], out _)!;
        Assert.Equal(Path.GetFullPath(SnapshotManager.DefaultStorageDirectory), d.StorageDir);   // 기본은 실제 폴더
    }

    [Fact]
    public void Parse_NeedsTwoKeys()
    {
        Assert.Null(MergeCommand.Parse([MergeCommand.Switch, Dup], out var e1));
        Assert.Contains("사용법", e1);
        Assert.Null(MergeCommand.Parse([MergeCommand.Switch, Dup, "--storage", "x"], out _));
        Assert.True(MergeCommand.IsRequested(["--x", MergeCommand.Switch]));
        Assert.False(MergeCommand.IsRequested(["--storage", "x"]));
    }

    [Fact]
    public void Run_BadUsage_ReturnsUsageCode_WithoutTouchingAnything()
    {
        var said = new List<string>();
        Assert.Equal(MergeCommand.Usage, MergeCommand.Run([MergeCommand.Switch, Dup], said.Add));
        Assert.Single(said);
    }

    // ── 일회 실행 ──

    [Fact]
    public void Run_MergesInTheGivenFolder_AndLogsTheResult()
    {
        var dir = AppHarness.NewStorageDir();
        try
        {
            SeedTwo(dir);
            var said = new List<string>();
            Assert.Equal(MergeCommand.Ok, MergeCommand.Run([MergeCommand.Switch, Dup, Orig, "--storage", dir], said.Add));
            Assert.Contains(said, m => m.StartsWith("완료"));

            var profiles = new JsonFileStore().Load<List<CharacterProfile>>(Path.Combine(dir, "character_history_db.json"))!;
            var only = Assert.Single(profiles);
            Assert.Equal(Orig, only.CharacterKey);
            Assert.Equal("빅클라우드", only.CustomName);
            Assert.Equal(3, only.History.Count);

            var log = string.Concat(Directory.GetFiles(Path.Combine(dir, "logs"), "homework-*.log").Select(File.ReadAllText));
            Assert.Contains("캐릭터 합치기 완료", log);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Run_MissingStorageFolder_FailsWithoutCreatingIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-nope-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(MergeCommand.MergeFailed, MergeCommand.Run([MergeCommand.Switch, Dup, Orig, "--storage", dir]));
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task Run_RefusesWhileAnAppUsesTheSameFolder_AndChangesNothing()
    {
        var dir = AppHarness.NewStorageDir();
        SeedTwo(dir);
        await using (await AppHarness.StartAsync(dir))   // 앱이 이 폴더의 저장소를 열면 폴더 공유 뮤텍스가 생긴다
        {
            var before = Directory.GetFiles(dir, "*.json").ToDictionary(f => f, File.ReadAllText);
            var said = new List<string>();
            Assert.Equal(MergeCommand.AppRunning, MergeCommand.Run([MergeCommand.Switch, Dup, Orig, "--storage", dir], said.Add));
            Assert.Contains("먼저 끄세요", said.Single());
            Assert.Equal(before, Directory.GetFiles(dir, "*.json").ToDictionary(f => f, File.ReadAllText));
        }
        try { Directory.Delete(dir, true); } catch { }
    }

    // ── 접속 중인 캐릭터가 from ──

    [Fact]
    public async Task Session_WhenTheCurrentCharacterIsMerged_CurrentKeyFollowsAndNoDuplicateReappears()
    {
        var dir = AppHarness.NewStorageDir();
        try
        {
            // 접속 중 캐릭터(아이라_격투가)와 레벨이 크게 다른 기록(#2)을 넣어 둔다: 접속 캐릭터는 기본 이름으로 기록된다
            var t = DateTime.Now.AddHours(-3);
            var other = Seed.Profile("아이라", "격투가", t, Seed.Rec(t, 40, 20000, 1, 1, 1, 1));
            other.CharacterKey = FakeGame.SampleKey + "#2";
            Seed.Write(dir, other);
            await using var h = await AppHarness.StartAsync(dir);
            await h.RecordAsync();
            Assert.Equal(FakeGame.SampleKey, h.Session.CurrentKey);

            var result = h.Commands.MergeCharacters(FakeGame.SampleKey, other.CharacterKey);
            Assert.True(result.Ok, result.Message);
            Assert.Equal(other.CharacterKey, h.Session.CurrentKey);              // 접속 중 키도 대상으로 바뀐다
            Assert.Null(h.Snapshots.GetProfileByKey(FakeGame.SampleKey));         // 원래 기록은 사라졌다 (메모리도 다시 읽음)

            await h.RecordAsync();                                                // 다시 기록해도 #2가 되살아나지 않는다
            Assert.Null(h.Snapshots.GetProfileByKey(FakeGame.SampleKey));
            Assert.Equal(other.CharacterKey, h.Session.CurrentKey);
            Assert.True(h.Snapshots.GetProfileByKey(other.CharacterKey)!.History.Count >= 2);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Session_FailedMerge_KeepsTheCurrentKey()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var result = h.Commands.MergeCharacters(FakeGame.SampleKey, "아이라_없음");   // 대상 기록이 없다
        Assert.False(result.Ok);
        Assert.Equal(FakeGame.SampleKey, h.Session.CurrentKey);
        Assert.NotNull(h.Snapshots.GetProfileByKey(FakeGame.SampleKey));
    }

    // ── 화면 모델 ──

    private sealed class FakeMerge : IMergeActions
    {
        public List<(string From, string Into)> Merges { get; } = [];
        public Func<MergeResult> OnMerge { get; set; } = () => new MergeResult(true, "합침", 3, 2);
        public MergePreview PreviewMerge(string fromKey, string intoKey) => new(true, true, 3, 2, true, true);
        public MergeResult MergeCharacters(string fromKey, string intoKey) { Merges.Add((fromKey, intoKey)); return OnMerge(); }
    }

    private static (HomeworkBoardViewModel Board, FakeMerge Merge) NewBoard(params AccountView[] accounts)
    {
        var merge = new FakeMerge();
        var board = new HomeworkBoardViewModel(new FakeActions(), post: a => a(), clock: () => Now, noticeDuration: TimeSpan.FromMinutes(5), merge: merge);
        board.Apply(Snap(accounts));
        return (board, merge);
    }

    [Fact]
    public void Board_Candidates_AreSameRealm_SameJobFirst_AndExcludeItself()
    {
        var dup = Char(Dup, "전사", combat: 102_000, current: true) with { Variant = 2 };
        var orig = Char(Orig, "전사", combat: 105_000, nick: "빅클라우드");
        var other = Char("아이라_격투가", "격투가", combat: 99_000);
        var elsewhere = Char("바람_전사", "전사", "바람", 90_000);
        var (board, _) = NewBoard(Acct("A", dup), Acct("B", other, orig), Acct("C", elsewhere));
        MergeRequest? req = null;
        board.MergeRequested += r => req = r;

        board.BeginMerge(board.Rows.Single(r => r.Key == Dup));

        Assert.NotNull(req);
        Assert.Equal(Dup, req!.FromKey);
        Assert.Equal([Orig, "아이라_격투가"], req.Candidates.Select(c => c.Key));   // 같은 직업(전사)이 앞, 다른 서버(바람)는 없음
        Assert.True(req.Candidates[0].SameJob);
        Assert.False(req.Candidates[1].SameJob);
    }

    [Fact]
    public void Board_NoCandidates_ShowsANotice_AndReadOnlyBoardCannotMerge()
    {
        var (board, _) = NewBoard(Acct("A", Char(Dup, "전사", current: true)));
        MergeRequest? req = null;
        board.MergeRequested += r => req = r;
        board.BeginMerge(board.Rows.Single());
        Assert.Null(req);
        Assert.True(board.NoticeVisible);
        Assert.True(board.CanMerge);

        var readOnly = new HomeworkBoardViewModel(new FakeActions(), post: a => a());
        Assert.False(readOnly.CanMerge);
    }

    [Fact]
    public async Task Board_MergeAsync_RunsTheMerge_FollowsTheSelection_AndRefreshes()
    {
        var dup = Char(Dup, "전사", current: true);
        var orig = Char(Orig, "전사");
        var merge = new FakeMerge();
        var refreshes = 0;
        var board = new HomeworkBoardViewModel(new FakeActions(), refresh: () => { refreshes++; return Task.CompletedTask; }, post: a => a(), clock: () => Now,
            noticeDuration: TimeSpan.FromMinutes(5), merge: merge);
        board.Apply(Snap(Acct("A", dup, orig)));
        board.SelectRow(board.Rows.Single(r => r.Key == Dup));

        await board.MergeAsync(Dup, Orig);

        Assert.Equal([(Dup, Orig)], merge.Merges);
        Assert.Equal(1, refreshes);
        Assert.Contains("합침", board.NoticeText);
    }

    [Fact]
    public async Task Board_MergeAsync_Failure_ShowsTheReason()
    {
        var (board, merge) = NewBoard(Acct("A", Char(Dup, "전사", current: true), Char(Orig, "전사")));
        merge.OnMerge = () => new MergeResult(false, "저장소 잠금을 얻지 못했습니다");
        await board.MergeAsync(Dup, Orig);
        Assert.Contains("합치지 못했습니다", board.NoticeText);
        Assert.Contains("잠금", board.NoticeText);
    }

    [Fact]
    public void ConfirmText_SaysItCannotBeUndone_AndShowsTheAmount()
    {
        var text = HomeworkBoardViewModel.ConfirmText("전사 #2", "빅클라우드", new MergePreview(true, true, 1234, 5, true, true));
        Assert.Contains("되돌릴 수 없습니다", text);
        Assert.Contains("1,234건", text);
        Assert.Contains("5개", text);
        Assert.Contains("빅클라우드", text);
    }
}
