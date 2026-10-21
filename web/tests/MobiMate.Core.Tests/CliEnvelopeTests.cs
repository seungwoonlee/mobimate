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
