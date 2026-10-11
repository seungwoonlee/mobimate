using MobiMateHomeWork.ViewModels;
using static MobiMateHomeWork.Tests.BoardKit;

namespace MobiMateHomeWork.Tests;

/// <summary>
/// H4 화면 모델 + 실제 서비스(가짜 CLI + 임시 폴더): 칸을 누르면 기록 파일이 바뀌고 화면이 다시 그려지는지,
/// 알림이 몰려도 한 번에 읽는지, 리셋 경계에서 다시 읽는지.
/// </summary>
public class BoardIntegrationTests
{
    private static async Task<(AppHarness H, MainViewModel Vm)> StartAsync(FakeGame? game = null, DateTimeOffset? clock = null, Func<CharactersSnapshot>? source = null)
    {
        var h = await AppHarness.StartAsync(game: game);
        Seed.WriteAccountsSeed(h.StorageDir, out _);
        var first = new FirstRead(h.Status, h.Session);
        var now = clock ?? DateTimeOffset.UtcNow;
        var vm = new MainViewModel(h.Events, h.Status, h.Services.GetRequiredService<GameCli>(), h.Session, h.Settings, first, actions: h.Commands, source: source,
            refreshDelay: TimeSpan.FromMilliseconds(20), clock: clock == null ? null : () => now);
        await vm.StartAsync();
        return (h, vm);
    }

    private static CellViewModel CellOf(MainViewModel vm, string key, string id) => vm.Board.Rows.Single(r => r.Key == key).Cells.Single(c => c.Id == id);

    [Fact]
    public async Task ClickingACell_WritesTheRecordFile_AndRedrawsDone()
    {
        var (h, vm) = await StartAsync();
        await using var _ = h;
        using var __ = vm;
        var cell = CellOf(vm, FakeGame.SampleKey, "raid_airel");
        Assert.False(cell.IsDone);

        await vm.Board.ToggleCellAsync(cell);                      // 완료로

        await Wait.UntilAsync(() => CellOf(vm, FakeGame.SampleKey, "raid_airel").IsDone, "다시 그려져 완료로 보임");
        var file = File.ReadAllText(Path.Combine(h.StorageDir, "homework_records.json"));
        Assert.Contains("raid_airel", file);                        // 기록 파일에 반영
        Assert.Equal(HomeworkCardStatus.ManualDone, h.Homework.GetBoard(FakeGame.SampleKey).Cards.Single(c => c.Id == "raid_airel").Status);

        await vm.Board.ToggleCellAsync(CellOf(vm, FakeGame.SampleKey, "raid_airel"));   // 다시 누르면 미완료로
        await Wait.UntilAsync(() => !CellOf(vm, FakeGame.SampleKey, "raid_airel").IsDone, "다시 그려져 미완료로 보임");
    }

    [Fact]
    public async Task ClickingACellOfAnotherCharacter_ChecksThatCharacter_NotTheOneInTheGame()
    {
        var (h, vm) = await StartAsync();
        await using var _ = h;
        using var __ = vm;
        await vm.Board.ToggleCellAsync(CellOf(vm, "바람_도적", "raid_cavrak"));
        Assert.Equal(HomeworkCardStatus.ManualDone, h.Homework.GetBoard("바람_도적").Cards.Single(c => c.Id == "raid_cavrak").Status);
        Assert.NotEqual(HomeworkCardStatus.ManualDone, h.Homework.GetBoard(FakeGame.SampleKey).Cards.Single(c => c.Id == "raid_cavrak").Status);
        await Wait.UntilAsync(() => CellOf(vm, "바람_도적", "raid_cavrak").IsDone, "도적의 칸이 완료로 보임");
        Assert.False(CellOf(vm, FakeGame.SampleKey, "raid_cavrak").IsDone);
    }

    [Fact]
    public async Task FieldBoss_ChooseThenClear_RoundTripsThroughTheRecordFile()
    {
        var (h, vm) = await StartAsync();
        await using var _ = h;
        using var __ = vm;
        var boss = CellOf(vm, "바람_도적", "field_boss");
        await vm.Board.ToggleCellAsync(boss);                       // 팝업
        Assert.NotNull(vm.Board.BossPicker);
        await vm.Board.ChooseBossAsync(HomeworkBoardLayout.Bosses.Single(b => b.Title == "앙그르바한"));

        await Wait.UntilAsync(() => CellOf(vm, "바람_도적", "field_boss").IsDone, "필드 보스 칸이 완료로 보임");
        var done = CellOf(vm, "바람_도적", "field_boss");
        Assert.Equal("앙그르바한", done.Note);                      // 넘긴 보스 이름
        Assert.Contains("넘긴 보스: 앙그르바한", done.Tooltip);

        await vm.Board.ToggleCellAsync(done);                       // 해제: 넘긴 보스만 풀린다
        await Wait.UntilAsync(() => !CellOf(vm, "바람_도적", "field_boss").IsDone, "필드 보스 칸이 미완료로 보임");
        Assert.Equal("todo", h.Homework.GetFieldBossStatuses(["바람_도적"])["바람_도적"].State);
    }

    [Fact]
    public async Task Rename_ThroughTheRealCommand_ChangesTheNameEverywhere()
    {
        var (h, vm) = await StartAsync();
        await using var _ = h;
        using var __ = vm;
        vm.Board.SelectRow(vm.Board.Rows.Single(r => r.Key == "바람_도적"));
        vm.Board.BeginRename();
        vm.Board.Card.RenameText = "그림자 도적";
        await vm.Board.CommitRenameAsync();

        await Wait.UntilAsync(() => vm.Board.Rows.Single(r => r.Key == "바람_도적").DisplayName == "그림자 도적", "표의 이름이 바뀜");
        Assert.Equal("그림자 도적", vm.Board.Card.Name);
        Assert.Equal("그림자 도적", h.Snapshots.GetProfileByKey("바람_도적")!.CustomName);
        Assert.Equal("그림자 도적", new SnapshotManager(h.StorageDir).GetProfileByKey("바람_도적")!.CustomName);   // 파일에 남았다
    }

    [Fact]
    public async Task CurrentCharacter_GetsTheLiveCardValues_FromTheGame()
    {
        var (h, vm) = await StartAsync();
        await using var _ = h;
        using var __ = vm;
        var card = vm.Board.Card;
        Assert.Equal(FakeGame.SampleKey, card.Key);
        Assert.True(card.IsLive);
        Assert.Equal("접속 중", card.LastSeenText);
        Assert.Contains("격투가", card.ClassLevel);
        Assert.Equal("전투력", card.Tiles[0].Label);
        Assert.Contains(",", card.Tiles[0].Value);                  // 천 단위 구분
        Assert.True(card.Weight?.HasBar);                           // 가짜 CLI 샘플은 무게 최대를 준다
    }

    [Fact]
    public async Task Events_AreCoalesced_AndEachKindTriggersARefresh()
    {
        var reads = 0;
        var (h, vm) = await StartAsync(source: null);
        await using var _ = h;
        using var __ = vm;
        // 새 화면 모델에 읽기 횟수를 세는 원천을 달아서 본다
        var counted = new MainViewModel(h.Events, h.Status, h.Services.GetRequiredService<GameCli>(), h.Session, h.Settings, new FirstRead(h.Status, h.Session),
            actions: h.Commands, source: () => { Interlocked.Increment(ref reads); return Snap(Acct("A", Char("a_1"))); }, refreshDelay: TimeSpan.FromMilliseconds(20));
        using var ___ = counted;

        for (var i = 0; i < 5; i++) h.Events.RaiseCharactersChanged();   // 몰려 오는 알림
        await Wait.UntilAsync(() => Volatile.Read(ref reads) >= 1, "첫 읽기");
        await Task.Delay(120);
        var burst = Volatile.Read(ref reads);
        Assert.InRange(burst, 1, 2);                                     // 5번 울려도 1~2번만 읽는다

        h.Events.RaiseHomeworkChanged(new HomeworkChange("a_1", ["raid_cavrak"], HomeworkChangeReason.Manual));
        await Wait.UntilAsync(() => Volatile.Read(ref reads) > burst, "숙제 변경으로 다시 읽음");
        var afterHomework = Volatile.Read(ref reads);
        await Task.Delay(80);

        h.Events.RaiseHeaderChanged(new HeaderView("a_1", false, "에린", "도적", 1, null, null, 0, 0, 0, 0, 0, 0, null, null, null));
        await Wait.UntilAsync(() => Volatile.Read(ref reads) > afterHomework, "헤더 변경으로 다시 읽음");
        var afterHeader = Volatile.Read(ref reads);
        await Task.Delay(80);

        h.Events.RaiseStatusChanged(new StatusChange(GameConnection.Disconnected, DateTimeOffset.UtcNow));
        await Wait.UntilAsync(() => Volatile.Read(ref reads) > afterHeader, "연결 상태 변경으로 다시 읽음");
        Assert.Equal(GameConnection.Disconnected, counted.Connection);
    }

    [Fact]
    public async Task Tick_AfterTheResetBoundary_ReadsTheRecordsAgain_ButNotBefore()
    {
        var reads = 0;
        var clock = new DateTimeOffset(2026, 10, 14, 5, 59, 0, TimeSpan.FromHours(9));
        await using var h = await AppHarness.StartAsync();
        using var vm = new MainViewModel(h.Events, h.Status, h.Services.GetRequiredService<GameCli>(), h.Session, h.Settings, new FirstRead(h.Status, h.Session),
            actions: h.Commands, source: () => { Interlocked.Increment(ref reads); return Snap(Acct("A", Char("a_1", current: true))); },
            clock: () => clock, refreshDelay: TimeSpan.FromMilliseconds(20));
        await vm.RefreshAsync();
        Assert.Equal(1, reads);

        clock = clock.AddSeconds(30);              // 05:59:30 — 아직 리셋 전
        vm.Tick();
        await Task.Delay(150);
        Assert.Equal(1, reads);

        clock = new DateTimeOffset(2026, 10, 14, 6, 0, 5, TimeSpan.FromHours(9));   // 06:00 리셋을 지남
        vm.Tick();
        await Wait.UntilAsync(() => Volatile.Read(ref reads) == 2, "리셋 경계에서 보드를 다시 만듦");
        await Task.Delay(100);
        vm.Tick();                                  // 새 주기로 다시 읽었으니 더 읽지 않는다
        await Task.Delay(100);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task Tick_UpdatesTheCardsTimeBasedValues()
    {
        var clock = BoardKit.Now;
        await using var h = await AppHarness.StartAsync();
        using var vm = new MainViewModel(h.Events, h.Status, h.Services.GetRequiredService<GameCli>(), h.Session, h.Settings, new FirstRead(h.Status, h.Session),
            actions: h.Commands, source: () => Snap(Acct("A", Char("바람_도적", "도적", "바람", silver: 40))), clock: () => clock, refreshDelay: TimeSpan.FromMilliseconds(20));
        await vm.RefreshAsync();
        Assert.Equal("46/100 예상", vm.Board.Card.Silver?.ValueText);
        clock = clock.AddHours(1);
        vm.Tick();
        Assert.Equal("48/100 예상", vm.Board.Card.Silver?.ValueText);
    }

    [Fact]
    public async Task WithoutActions_TheBoardIsReadOnly_AndSaysSo()
    {
        await using var h = await AppHarness.StartAsync();
        using var vm = new MainViewModel(h.Events, h.Status, h.Services.GetRequiredService<GameCli>(), h.Session, h.Settings, new FirstRead(h.Status, h.Session),
            source: () => Snap(Acct("A", Char("a_1", current: true))), refreshDelay: TimeSpan.FromMilliseconds(20));
        await vm.RefreshAsync();
        await vm.Board.ToggleCellAsync(CellOf(vm, "a_1", "raid_cavrak"));
        Assert.True(vm.Board.NoticeVisible);
    }
}
