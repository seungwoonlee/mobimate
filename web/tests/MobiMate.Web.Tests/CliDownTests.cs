using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Services;

namespace MobiMate.Web.Tests;

/// <summary>게임과 연결이 끊긴 CLI(호출마다 5초 기다림)가 쌓이지 않게: 끊김 응답을 읽으면 잠시 즉시 실패, 상태 판정은 응답 내용으로 (2026-10-22)</summary>
public class CliDownTests
{
    private const string Down = """{"pipe":"disconnected","reason":"game_off","message":"Mabinogi Mobile client is not running."}""";

    private sealed class CountingCli(string output) : IGameCli
    {
        public int Calls;
        public bool IsAvailable => true;
        public string CliPath => "stub";
        public Task<CliResult> RunAsync(CliCommand cmd, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new CliResult(true, output, null, TimeSpan.Zero, TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task AfterADisconnectedAnswer_FurtherQueriesFailAtOnce_WithoutCallingTheCli()
    {
        var cli = new CountingCli(Down);
        var q = new GameQueries(cli, new QueryCache(), new MobiMateOptions());
        var first = await q.Get<List<NearPcItem>>("get_near_pcs");
        Assert.False(first.Ok);
        Assert.Equal(1, cli.Calls);
        foreach (var cmd in new[] { "get_near_pcs", "get_my_info", "get_activity", "get_items", "get_quests" })
            Assert.False((await q.Get<object>(cmd)).Ok);
        Assert.Equal(1, cli.Calls);                                   // 끊긴 동안은 CLI를 더 부르지 않는다
    }

    [Fact]
    public async Task NormalAnswers_AreNotAffected()
    {
        var cli = new CountingCli("[]");
        var q = new GameQueries(cli, new QueryCache(), new MobiMateOptions());
        Assert.True((await q.Get<List<NearPcItem>>("get_near_pcs")).Ok);
        Assert.True((await q.Get<List<ItemData>>("get_items")).Ok);
        Assert.Equal(2, cli.Calls);
    }

    [Theory]
    [InlineData("""{"pipe":"connected"}""", true, GameConnection.Connected)]
    [InlineData(Down, true, GameConnection.Disconnected)]            // 프로세스는 정상 종료하지만 연결은 끊겼다
    [InlineData("", false, GameConnection.Disconnected)]             // 실행 실패·타임아웃
    [InlineData("""{"unexpected":1}""", true, GameConnection.Connected)]   // 모르는 응답은 응답이 왔다는 사실을 따른다(기존 동작)
    public void Status_IsJudgedByTheAnswer_NotByWhetherTheProcessReplied(string stdout, bool ok, GameConnection expected)
        => Assert.Equal(expected, StatusMonitor.Judge(new CliResult(ok, stdout, null, TimeSpan.Zero, TimeSpan.Zero)));
}
