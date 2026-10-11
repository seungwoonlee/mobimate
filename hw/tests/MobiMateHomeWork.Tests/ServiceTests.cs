namespace MobiMateHomeWork.Tests;

/// <summary>숙제 관찰 연결부(HomeworkWatcher)와 게임 연결 상태(StatusMonitor).</summary>
public class ServiceTests
{
    // ── HomeworkWatcher ──

    [Fact]
    public async Task Observe_ReadsTheCharacterFirst_AndNamesTheObservation()
    {
        await using var h = await AppHarness.StartAsync();
        var key = await h.Watcher.ObserveAsync(CancellationToken.None);
        Assert.Equal("아이라_격투가", key);
        Assert.Equal("아이라_격투가", h.Homework.LastObservedCharacter);
    }

    [Fact]
    public async Task Observe_OnTheCharacterSelectScreen_JudgesNothing()
    {
        await using var h = await AppHarness.StartAsync();
        h.Game.ShowCharacterSelect();
        Assert.Null(await h.Watcher.ObserveAsync(CancellationToken.None));   // 캐릭터를 확인하지 못하면 판정하지 않는다
        Assert.Null(h.Homework.LastObservedCharacter);
        Assert.Empty(h.Log.Homework);
    }

    [Fact]
    public async Task Observe_WhenTheCliFails_ReturnsNullInsteadOfThrowing()
    {
        await using var h = await AppHarness.StartAsync();
        h.Game.Set("FAKECLI_FAIL_GET_MY_INFO", "3");
        Assert.Null(await h.Watcher.ObserveAsync(CancellationToken.None));
        h.Game.Set("FAKECLI_FAIL_GET_MY_INFO", null);
        Assert.NotNull(await h.Watcher.ObserveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Observe_FollowsTheCharacterSwitch_UsingTheNewReading()
    {
        await using var h = await AppHarness.StartAsync();
        Assert.Equal("아이라_격투가", await h.Watcher.ObserveAsync(CancellationToken.None));
        h.Game.SetCharacter(Chars.Rogue);
        Assert.Equal("바람_도적", await h.Watcher.ObserveAsync(CancellationToken.None));
        Assert.Equal("바람_도적", h.Homework.LastObservedCharacter);
    }

    // ── StatusMonitor ──

    [Fact]
    public async Task Status_RaisesTheNotificationOnlyWhenTheStateChanges()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game);
        Assert.Equal(GameConnection.Unknown, h.Status.State);

        Assert.Equal(GameConnection.Connected, await h.Status.CheckNowAsync());
        Assert.Equal(GameConnection.Connected, await h.Status.CheckNowAsync());   // 같은 상태: 알리지 않는다
        Assert.Equal(new[] { GameConnection.Connected }, h.Log.Status.Select(s => s.State));

        game.Disconnect();
        Assert.Equal(GameConnection.Disconnected, await h.Status.CheckNowAsync());
        game.Reconnect();
        Assert.Equal(GameConnection.Connected, await h.Status.CheckNowAsync());
        Assert.Equal(new[] { GameConnection.Connected, GameConnection.Disconnected, GameConnection.Connected }, h.Log.Status.Select(s => s.State));
    }

    [Fact]
    public async Task Status_WithoutTheCliFile_IsCliMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "mm-no-such-cli-" + Guid.NewGuid().ToString("N"), "MabinogiMobile_CLI.exe");
        await using var h = await AppHarness.StartAsync(configure: o => o.CliPath = missing);
        Assert.Equal(GameConnection.CliMissing, await h.Status.CheckNowAsync());
        var header = await h.Session.HeaderAsync(CancellationToken.None);
        Assert.False(header.Ok);
        Assert.Equal(CliFailureKind.Missing, header.Failure!.Kind);
    }

    [Fact]
    public async Task Status_DisconnectedGame_MakesReadsFailInsteadOfInventingValues()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game);
        await h.RecordAsync();
        game.Set("FAKECLI_FAIL_GET_MY_INFO", "3");
        var header = await h.Session.HeaderAsync(CancellationToken.None);
        Assert.False(header.Ok);
        Assert.Equal(CliFailureKind.Failed, header.Failure!.Kind);
        Assert.Equal("아이라_격투가", h.Session.CurrentKey);               // 마지막으로 알던 캐릭터는 그대로
        Assert.Single(h.Session.Characters().Cards());
    }

    // ── WPF판 기록 가져오기 (StartupTasks) ──

    [Fact]
    public async Task Startup_ImportsMissingWpfFiles_AndNeverWritesToTheWpfFolder()
    {
        var wpf = Path.Combine(Path.GetTempPath(), "mm-hwwpf-" + Guid.NewGuid().ToString("N"));
        try
        {
            var seen = new DateTime(2026, 9, 1, 10, 0, 0);
            Seed.Write(wpf, Seed.Profile("바람", "도적", seen, Seed.Rec(seen, 100, 99000, 777, 888, 10, 1, gold: 3_000_000)));
            var original = File.ReadAllBytes(Path.Combine(wpf, "character_history_db.json"));
            var filesBefore = Directory.GetFiles(wpf).Length;

            await using var h = await AppHarness.StartAsync(configure: o => o.WpfStorageDir = wpf);
            Assert.NotNull(h.Snapshots.GetProfileByKey("바람_도적"));      // 저장소에 없던 기록을 가져왔다
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(wpf, "character_history_db.json")));
            Assert.Equal(filesBefore, Directory.GetFiles(wpf).Length);    // 원본 폴더에는 아무것도 쓰지 않았다
        }
        finally { try { Directory.Delete(wpf, true); } catch { } }
    }
}
