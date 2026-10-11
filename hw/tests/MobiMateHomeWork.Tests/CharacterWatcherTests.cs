namespace MobiMateHomeWork.Tests;

/// <summary>
/// 캐릭터 전환 감시 (웹앱 CharacterWatcherTests): 화면이 아무것도 요청하지 않아도 감시가 캐릭터가 바뀐 것을 몇 초 안에 알아채고
/// AppEvents.HeaderChanged로 밀어 준다. 웹앱은 SSE "header"였다. 게임 연결이 끊겼다 돌아온 순간도 바로 새로 읽는다.
/// 감시 주기만 짧게 켜고(0.2초), 상태는 StatusMonitor.CheckNowAsync로 직접 확인한다.
/// </summary>
public class CharacterWatcherTests
{
    private static Task<AppHarness> StartWatching(FakeGame? game = null) =>
        AppHarness.StartAsync(game: game, configure: o => o.WatchIntervalSeconds = 0.2);

    [Fact]
    public async Task CharacterSwitch_IsPushedWithinSeconds_WithoutAnyClientRequest()
    {
        await using var h = await StartWatching();
        Assert.Equal(GameConnection.Connected, await h.Status.CheckNowAsync());

        // 감시가 처음 읽을 때(감시 시작) 곧바로 헤더를 새로 읽어 알린다
        await Wait.UntilAsync(() => h.Log.Headers.Any(x => x.CharacterKey == "아이라_격투가" && !x.Selecting), "감시 시작 헤더");
        Assert.DoesNotContain(h.Log.Headers, x => x.Job == "댄서");

        // 게임에서 직업이 댄서로 바뀐 상황: 이 뒤로 호출하는 쪽은 아무것도 요청하지 않는다
        // (누구로 볼지는 CharacterIdentity가 정하고, 여기서는 감시가 알아채 밀어 주는지를 본다)
        h.Game.SetJob("댄서");

        await Wait.UntilAsync(() => h.Log.Headers.Any(x => x.Job == "댄서"), "직업 전환이 헤더로 전달됨");
        var pushed = h.Log.Headers.First(x => x.Job == "댄서");
        Assert.False(pushed.Selecting);
    }

    [Fact]
    public async Task ReconnectAfterCharacterSelect_ReadsAgainRightAway()
    {
        using var game = new FakeGame();
        await using var h = await StartWatching(game);
        await h.Status.CheckNowAsync();
        await Wait.UntilAsync(() => h.Log.Headers.Any(x => !x.Selecting), "감시 시작 헤더");

        // 게임이 끊겼다가(캐릭터 선택창을 거쳐) 다시 연결: 연결 복구 순간 바로 새로 읽는다
        game.Disconnect();
        Assert.Equal(GameConnection.Disconnected, await h.Status.CheckNowAsync());
        game.SetCharacter(Chars.Rogue);
        game.Reconnect();
        var before = h.Log.Headers.Count;
        Assert.Equal(GameConnection.Connected, await h.Status.CheckNowAsync());

        await Wait.UntilAsync(() => h.Log.Headers.Skip(before).Any(x => x.Job == "도적"), "연결 복구 뒤 새 캐릭터 헤더");
    }

    [Fact]
    public async Task WhileDisconnected_TheWatcherDoesNotReadTheGame()
    {
        using var game = new FakeGame();
        game.Disconnect();
        await using var h = await StartWatching(game);
        Assert.Equal(GameConnection.Disconnected, await h.Status.CheckNowAsync());

        // 연결이 없는 동안에는 읽지 않는다(헤더 알림 없음). 관찰 창 동안 계속 비어 있어야 한다.
        await Wait.StaysFalseAsync(() => h.Log.Headers.Count > 0, TimeSpan.FromSeconds(1), "연결이 없는데 헤더가 올라옴");
    }
}
