using MobiMateHomeWork.Layout;
using MobiMateHomeWork.ViewModels;

namespace MobiMateHomeWork.Tests;

/// <summary>화면 모델을 만드는 도우미: 앱과 같은 서비스 등록(AppHarness) 위에 창 없이 MainViewModel을 올린다.</summary>
internal static class ViewModelFixture
{
    public static MainViewModel Make(AppHarness h, out FirstRead firstRead)
    {
        firstRead = new FirstRead(h.Status, h.Session);
        return new MainViewModel(h.Events, h.Status, h.Services.GetRequiredService<GameCli>(), h.Session, h.Settings, firstRead,
            refreshDelay: TimeSpan.FromMilliseconds(20));
    }
}

public class MainViewModelTests
{
    /// <summary>가짜 CLI 호출 기록(탭 구분: 시각·pid·phase·명령·인자)에서 해당 명령의 시작 횟수.</summary>
    private static List<string> CliCalls(FakeGame game, string command)
    {
        if (!File.Exists(game.LogPath)) return [];
        using var fs = new FileStream(game.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs);
        var calls = new List<string>();
        while (r.ReadLine() is { } line)
        {
            var f = line.Split((char)9);   // 탭 구분: 시각·pid·phase·명령·인자
            if (f.Length > 3 && f[2] == "start" && f[3] == command) calls.Add(f[3]);
        }
        return calls;
    }

    [Fact]
    public async Task BeforeTheFirstRead_ShowsThePlaceholder_ThenFillsFromTheGame()
    {
        await using var h = await AppHarness.StartAsync();
        Seed.WriteAccountsSeed(h.StorageDir, out _);   // 시작 뒤에 쓰므로 저장소가 읽는 시점은 첫 Characters() 호출
        using var vm = ViewModelFixture.Make(h, out var first);

        Assert.True(vm.IsFirstReadPending);
        Assert.False(first.Done);
        Assert.Empty(vm.Accounts);

        await vm.StartAsync();

        Assert.True(first.Done);
        Assert.False(vm.IsFirstReadPending);
        // 게임이 연결돼 있으므로 접속 중 캐릭터(아이라_격투가)가 읽혀 IsCurrent가 켜진다
        var account = Assert.Single(vm.Accounts, a => a.IsCurrent);
        Assert.Equal(account, vm.SelectedAccount);
        Assert.Contains(vm.Characters, c => c.IsCurrent && c.Key == FakeGame.SampleKey);
        Assert.Equal(FakeGame.SampleKey, vm.SelectedCharacter?.Key);   // 기본 선택 = 접속 중 캐릭터
        Assert.True(vm.Accounts.Count >= 2);                           // 도적과 같은 계정, 궁수는 다른 계정
    }

    [Fact]
    public async Task WhenTheGameIsNotConnected_TheRecordedValuesShowRightAway()
    {
        using var game = new FakeGame();
        game.Disconnect();
        await using var h = await AppHarness.StartAsync(game: game);
        Seed.WriteAccountsSeed(h.StorageDir, out _);
        using var vm = ViewModelFixture.Make(h, out var first);

        await vm.StartAsync();

        Assert.True(first.Done);
        Assert.False(vm.IsFirstReadPending);
        Assert.Equal(GameConnection.Disconnected, h.Status.State);
        Assert.Equal(2, vm.Accounts.Count);
        Assert.DoesNotContain(vm.Accounts, a => a.IsCurrent);          // 접속 중인 캐릭터가 없다
        Assert.NotEmpty(vm.Characters);                                // 기록된 값은 그대로 보인다
        // 게임을 읽은 적이 없으므로 카드에 쓸 접속 호출은 없었다
        Assert.Empty(CliCalls(game, "get_my_info"));
    }

    [Fact]
    public async Task WhenTheFirstReadFails_ThePlaceholderStillEnds_WithRecordedValues()
    {
        using var game = new FakeGame();
        game.Set("FAKECLI_FAIL_GET_MY_INFO", "1");
        await using var h = await AppHarness.StartAsync(game: game);
        Seed.WriteAccountsSeed(h.StorageDir, out _);
        using var vm = ViewModelFixture.Make(h, out var first);

        await vm.StartAsync();

        Assert.True(first.Done);
        Assert.False(vm.IsFirstReadPending);
        Assert.NotEmpty(vm.Accounts);
    }

    [Fact]
    public async Task FirstRead_RunsOnlyOnce()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game);
        var first = new FirstRead(h.Status, h.Session);
        var completed = 0;
        first.Completed += () => completed++;

        await first.RunAsync();
        var calls = CliCalls(game, "get_my_info");
        await first.RunAsync();

        Assert.Equal(1, completed);
        Assert.Equal(calls, CliCalls(game, "get_my_info"));
    }

    // ── 경고 띠 ──

    [Fact]
    public async Task Banner_ShowsWarnings_CanBeDismissed_AndDisappearsWhenTheWarningClears()
    {
        await using var h = await AppHarness.StartAsync();
        using var vm = ViewModelFixture.Make(h, out _);
        Assert.False(vm.BannerVisible);

        h.Events.RaiseWarning(new AppWarning(AppWarningKind.LegacyWebApp, "옛 웹앱 경고"));
        Assert.True(vm.BannerVisible);
        Assert.Equal("옛 웹앱 경고", vm.BannerText);

        vm.DismissBannerCommand.Execute(null);
        Assert.False(vm.BannerVisible);                                 // 닫으면 사라진다(경고 자체는 남아 있다)
        Assert.Single(h.Events.Warnings);

        h.Events.ClearWarning(AppWarningKind.LegacyWebApp);
        Assert.False(vm.BannerVisible);

        h.Events.RaiseWarning(new AppWarning(AppWarningKind.LegacyWebApp, "옛 웹앱 경고"));
        Assert.True(vm.BannerVisible);                                  // 해소됐다가 다시 생기면 다시 보인다
    }

    [Fact]
    public async Task Banner_ClearedWarning_HidesWithoutDismissing()
    {
        await using var h = await AppHarness.StartAsync();
        using var vm = ViewModelFixture.Make(h, out _);
        h.Events.RaiseWarning(new AppWarning(AppWarningKind.LegacyWebApp, "A"));
        h.Events.RaiseWarning(new AppWarning(AppWarningKind.StorageLockDegraded, "B"));
        Assert.Equal("A", vm.BannerText);
        vm.DismissBannerCommand.Execute(null);
        Assert.Equal("B", vm.BannerText);                               // 하나를 닫으면 다음 경고
        h.Events.ClearWarning(AppWarningKind.StorageLockDegraded);
        Assert.False(vm.BannerVisible);
    }

    [Fact]
    public async Task BannerExistingBeforeTheWindowOpens_IsShown()
    {
        await using var h = await AppHarness.StartAsync();
        h.Events.RaiseWarning(new AppWarning(AppWarningKind.LegacyWebApp, "먼저 생긴 경고"));
        using var vm = ViewModelFixture.Make(h, out _);
        Assert.True(vm.BannerVisible);
        Assert.Equal("먼저 생긴 경고", vm.BannerText);
    }

    // ── 연결 점 ──

    [Fact]
    public async Task ConnectionDot_FollowsTheStatus_WithATooltipPerState()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game);
        using var vm = ViewModelFixture.Make(h, out _);
        Assert.Equal(GameConnection.Unknown, vm.Connection);

        await h.Status.CheckNowAsync();
        Assert.Equal(GameConnection.Connected, vm.Connection);
        Assert.Equal("연결됨", vm.ConnectionLabel);

        game.Disconnect();
        await h.Status.CheckNowAsync();
        Assert.Equal(GameConnection.Disconnected, vm.Connection);
        Assert.Contains("연결되지 않음", vm.ConnectionTooltip);

        Assert.Contains("CLI", ConnectionInfo.Tooltip(GameConnection.CliMissing, @"C:\x\cli.exe"));
        Assert.Contains(@"C:\x\cli.exe", ConnectionInfo.Tooltip(GameConnection.CliMissing, @"C:\x\cli.exe"));
        Assert.Equal(4, new[] { GameConnection.Connected, GameConnection.Disconnected, GameConnection.CliMissing, GameConnection.Unknown }
            .Select(ConnectionInfo.Label).Distinct().Count());
    }

    [Fact]
    public async Task ChangedRecords_RefreshTheAccountsOnTheEvent()
    {
        await using var h = await AppHarness.StartAsync();
        using var vm = ViewModelFixture.Make(h, out _);
        await vm.StartAsync();
        var before = vm.Accounts.Count;

        Seed.Write(h.StorageDir,
            Seed.Profile("바람", "도적", DateTime.Now, Seed.Rec(DateTime.Now, 100, 99000, 10285, 5619, 90, 3)),
            Seed.Profile("류트", "검사", DateTime.Now, Seed.Rec(DateTime.Now, 80, 50000, 1, 2, 10, 1)));
        h.Events.RaiseCharactersChanged();   // 다른 프로그램의 변경도 이 길로 온다

        await Wait.UntilAsync(() => vm.Accounts.SelectMany(_ => vm.Characters).Any() && vm.Accounts.Count != before || vm.Accounts.Count >= 2, "계정 목록 갱신");
        Assert.True(vm.Accounts.Count >= 1);
    }

    // ── 설정 저장 ──

    [Fact]
    public async Task AlwaysOnTop_AndCloseToTray_ArePersisted()
    {
        await using var h = await AppHarness.StartAsync();
        using var vm = ViewModelFixture.Make(h, out _);
        Assert.False(vm.AlwaysOnTop);
        Assert.True(vm.CloseToTray);                                    // 기본: 닫으면 트레이로

        vm.SetAlwaysOnTop(true);
        vm.SetCloseToTray(false);

        var reread = new AppSettingsStore(h.StorageDir);               // 새로 읽어도 남아 있다
        Assert.True(reread.Current.AlwaysOnTop);
        Assert.False(reread.Current.CloseToTray);

        using var vm2 = ViewModelFixture.Make(h, out _);                // 다음 실행의 화면 모델이 저장값으로 시작한다
        Assert.True(vm2.AlwaysOnTop);
        Assert.False(vm2.CloseToTray);
    }

    [Fact]
    public async Task TrayHint_IsShownOnlyOnce()
    {
        await using var h = await AppHarness.StartAsync();
        using var vm = ViewModelFixture.Make(h, out _);
        Assert.False(vm.TrayHintShown);
        vm.MarkTrayHintShown();
        Assert.True(vm.TrayHintShown);
        Assert.True(new AppSettingsStore(h.StorageDir).Current.TrayHintShown);
    }

    [Fact]
    public async Task WindowBounds_AreSavedAndComeBackThroughTheSettingsFile()
    {
        await using var h = await AppHarness.StartAsync();
        using var vm = ViewModelFixture.Make(h, out _);

        vm.SaveWindow(new PxRect(960, 0, 960, 520), maximized: true);

        var s = new AppSettingsStore(h.StorageDir).Current;
        Assert.Equal((960d, 0d, 960d, 520d, true), (s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight, s.WindowMaximized));
        // 같은 파일에서 저장된 값으로 복원 계산이 되는지
        var restored = WindowPlacement.Restore(new SavedPlacement((int)s.WindowLeft!, (int)s.WindowTop!, (int)s.WindowWidth!, (int)s.WindowHeight!, s.WindowMaximized),
            [new MonitorArea(new PxRect(0, 0, 1920, 1040), 1.0)]);
        Assert.Equal(new PxRect(960, 0, 960, 520), restored);
    }

    [Fact]
    public async Task CliPath_IsSavedAppliedAndReflectedInTheConnectionDot()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game);
        using var vm = ViewModelFixture.Make(h, out _);
        await h.Status.CheckNowAsync();
        Assert.Equal(GameConnection.Connected, vm.Connection);

        await vm.SetCliPathAsync(Path.Combine(game.Dir, "없는-cli.exe"));
        Assert.Equal(GameConnection.CliMissing, vm.Connection);         // 없는 경로로 바꾸면 CLI 없음
        Assert.Contains("없는-cli.exe", vm.ConnectionTooltip);
        Assert.Equal(Path.Combine(game.Dir, "없는-cli.exe"), new AppSettingsStore(h.StorageDir).Current.CliPath);

        await vm.SetCliPathAsync(game.ExePath);                         // 올바른 경로로 되돌리면 연결
        Assert.Equal(GameConnection.Connected, vm.Connection);
        Assert.Equal(game.ExePath, vm.EffectiveCliPath);
    }

    [Fact]
    public async Task Tick_RecomputesTheCountdown()
    {
        await using var h = await AppHarness.StartAsync();
        var now = new DateTimeOffset(2026, 10, 13, 15, 48, 0, TimeSpan.Zero);
        var first = new FirstRead(h.Status, h.Session);
        using var vm = new MainViewModel(h.Events, h.Status, h.Services.GetRequiredService<GameCli>(), h.Session, h.Settings, first, clock: () => now);
        Assert.Equal("일 5:12 · 주 5일 5h", vm.Countdown);
        now = now.AddMinutes(30);
        vm.Tick();
        Assert.Equal("일 4:42 · 주 5일 4h", vm.Countdown);
    }
}
