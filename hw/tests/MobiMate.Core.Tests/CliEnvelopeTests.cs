namespace MobiMate.Tests;

/// <summary>CLI 오류 응답 알아보기 (2026-10-22 CLI 점검): 오류 객체를 값으로 읽지 않는다</summary>
public class CliEnvelopeTests
{
    [Theory]
    [InlineData("""{"error":"unknown_command","message":"Command 'x' is not recognized by the game client."}""", "unknown_command")]
    [InlineData("""{"pipe":"disconnected","reason":"game_off","message":"Mabinogi Mobile client is not running."}""", "게임이 꺼져 있")]
    [InlineData("""{"pipe":"disconnected","reason":"other","message":"연결이 끊겼습니다"}""", "연결이 끊겼습니다")]
    [InlineData("""{"error":"not_in_game"}""", "not_in_game")]
    [InlineData("  \r\n{\"error\":\"x\",\"message\":\"y\"}\r\n", "x")]
    public void ErrorBodies_AreRecognised(string stdout, string expectedPart)
    {
        Assert.True(CliEnvelope.TryReadError(stdout, out var msg));
        Assert.Contains(expectedPart, msg);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"DisplayName":"a","Count":1}]""")]
    [InlineData("""{"pipe":"connected"}""")]                                  // status 정상
    [InlineData("""{"Title":"x","RealmName":"아이라","Level":100}""")]          // get_my_info 같은 정상 객체
    [InlineData("""{"items":[{"DisplayName":"통나무","ToolOk":true}]}""")]      // get_gatherable_items
    [InlineData("""{"completedCount":0,"works":[]}""")]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ 깨진")]
    public void NormalValues_AreNotErrors(string stdout) => Assert.False(CliEnvelope.TryReadError(stdout, out _));

    [Theory]
    [InlineData("""{"pipe":"connected"}""", true)]
    [InlineData("""{"pipe":"disconnected","reason":"game_off","message":"m"}""", false)]
    [InlineData("""{"Title":"x"}""", null)]
    [InlineData("[]", null)]
    [InlineData("", null)]
    [InlineData("not json", null)]
    public void PipeConnected_ReadsTheStatusBody(string stdout, bool? expected) => Assert.Equal(expected, CliEnvelope.PipeConnected(stdout));

    [Fact]
    public void QueryTimeouts_AreLongerThanTheFiveSecondsTheCliWaitsWhenTheGameIsDisconnected()
    {
        // 2026-10-22 실측: 게임과 연결이 끊긴 CLI는 5.03초 뒤에 응답한다. 조회 타임아웃이 이보다 짧으면 그때마다 "응답 시간 초과"가 된다.
        foreach (var cmd in new[] { "get_my_info", "get_activity", "get_near_pcs", "get_quests", "get_currencies", "get_daily_missions", "get_items", "status" })
            Assert.True(CliLanes.DefaultTimeout(cmd) > TimeSpan.FromSeconds(6), cmd);
    }

    [Fact]
    public async Task RunJson_TurnsAnErrorBodyIntoAFailure_InsteadOfEmptyData()
    {
        var cli = new StubCli("""{"pipe":"disconnected","reason":"game_off","message":"off"}""");
        var (ok, data, error) = await cli.RunJsonAsync<List<ItemData>>("get_items");
        Assert.False(ok);
        Assert.Null(data);
        Assert.Contains("게임이 꺼져 있", error);

        var obj = await new StubCli("""{"error":"unknown_command","message":"m"}""").RunJsonAsync<CharacterInfo>("get_my_info");
        Assert.False(obj.Ok);                                                    // 예전에는 빈 CharacterInfo가 "성공"이었다
    }

    private sealed class StubCli(string output) : IGameCli
    {
        public bool IsAvailable => true;
        public string CliPath => "stub";
        public Task<CliResult> RunAsync(CliCommand cmd, CancellationToken ct = default) => Task.FromResult(new CliResult(true, output, null, TimeSpan.Zero, TimeSpan.Zero));
    }
}
