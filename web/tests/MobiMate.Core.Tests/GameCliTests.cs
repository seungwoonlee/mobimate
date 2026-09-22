using System.Diagnostics;

namespace MobiMate.Tests;

[Collection(FakeCliCollection.Name)]
public class GameCliTests
{
    [Fact]
    public async Task RunJsonAsync_ParsesSampleResponse()
    {
        using var env = new FakeCliEnv();
        var cli = env.CreateCli();

        var (ok, info, err) = await cli.RunJsonAsync<CharacterInfo>("get_my_info");

        Assert.True(ok, err);
        Assert.Equal("아이라", info!.RealmName);
        Assert.Equal(88737, info.CombatScore!.Value);
        Assert.Equal(1030.0, info.Vitals!.WeightMax);
    }

    [Fact]
    public async Task MissingExecutable_ReturnsErrorWithoutThrowing()
    {
        var cli = new GameCli(@"Z:\없는\경로\MabinogiMobile_CLI.exe");
        var r = await cli.RunAsync(new CliCommand("status"));
        Assert.False(cli.IsAvailable);
        Assert.False(r.Ok);
        Assert.Contains("찾을 수 없습니다", r.Error);
    }

    [Fact]
    public async Task Timeout_KillsProcessAndReportsTimedOut()
    {
        using var env = new FakeCliEnv();
        env.Set("FAKECLI_DELAY_GET_MY_INFO", "3000");
        var cli = env.CreateCli();

        var sw = Stopwatch.StartNew();
        var r = await cli.RunAsync(new CliCommand("get_my_info", Timeout: TimeSpan.FromMilliseconds(400)));

        Assert.False(r.Ok);
        Assert.True(r.TimedOut);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2.5), $"타임아웃이 늦게 걸렸다: {sw.Elapsed}");
    }

    [Fact]
    public async Task NonZeroExit_And_BadJson_AreReportedAsErrors()
    {
        using var env = new FakeCliEnv();
        var cli = env.CreateCli();

        env.Set("FAKECLI_FAIL_GET_CURRENCIES", "3");
        var fail = await cli.RunJsonAsync<List<CurrencyItem>>("get_currencies");
        Assert.False(fail.Ok);
        Assert.Contains("fake failure", fail.Error);

        env.Set("FAKECLI_FAIL_GET_CURRENCIES", null);
        env.Set("FAKECLI_BADJSON_GET_CURRENCIES", "1");
        var bad = await cli.RunJsonAsync<List<CurrencyItem>>("get_currencies");
        Assert.False(bad.Ok);
        Assert.Contains("JSON 파싱 오류", bad.Error);
    }

    [Fact]
    public async Task Body_IsSerializedAsJson_EvenWithQuotesInName()
    {
        // D-02: 아이템명에 따옴표가 있어도 JSON이 깨지지 않는다
        using var env = new FakeCliEnv();
        var cli = env.CreateCli();

        var r = await cli.RunAsync(new CliCommand("complete_altering_work", Body: new { displayName = "\"특제\" 가죽" }));

        Assert.True(r.Ok, r.Error);
        var call = env.ReadCalls().First(c => c.Phase == "start" && c.Command == "complete_altering_work");
        Assert.Equal("{\"displayName\":\"\\\"특제\\\" 가죽\"}", call.Args);
    }

    [Fact]
    public async Task Gather_PassesCount_AndStopActionReachesCliWithinOneSecond()
    {
        // NFR-06 / TST-03: 채집(장시간 레인)이 도는 동안 정지(우선 경로)가 1초 안에 CLI에 닿고, 채집이 "stopped"로 끝난다
        using var env = new FakeCliEnv();
        env.Set("FAKECLI_DELAY_EXECUTE_GATHERING", "8000");
        var cli = env.CreateCli();

        var gather = cli.RunAsync(new CliCommand("execute_gathering", Body: new { displayName = "사과", count = 12 }));
        await WaitForAsync(() => env.ReadCalls().Any(c => c.Command == "execute_gathering" && c.Phase == "start"));

        // 일반 레인 조회는 채집에 막히지 않는다
        var info = await cli.RunAsync(new CliCommand("get_currencies"));
        Assert.True(info.Ok, info.Error);

        // 일반 레인이 '진행 중'인 상태에서도 정지는 기다리지 않는다
        env.Set("FAKECLI_DELAY_GET_ACTIVITY", "2000");
        var busyGeneral = cli.RunAsync(new CliCommand("get_activity"));
        await WaitForAsync(() => env.ReadCalls().Any(c => c.Command == "get_activity" && c.Phase == "start"));

        var requestedAt = DateTime.UtcNow;
        var stop = await cli.RunAsync(new CliCommand("stop_action"));
        Assert.True(stop.Ok, stop.Error);
        Assert.False(busyGeneral.IsCompleted, "정지가 일반 레인 작업이 끝나길 기다렸다");
        await busyGeneral;

        var stopStart = env.ReadCalls().First(c => c.Command == "stop_action" && c.Phase == "start");
        Assert.True(stopStart.At - requestedAt < TimeSpan.FromSeconds(1), $"정지 전달 지연: {stopStart.At - requestedAt}");
        Assert.True(stop.QueueWait < TimeSpan.FromMilliseconds(50), $"정지가 큐에서 기다렸다: {stop.QueueWait}");

        var g = await gather.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(g.Ok, g.Error);
        Assert.Contains("\"stopped\"", g.Stdout);

        var gatherCall = env.ReadCalls().First(c => c.Command == "execute_gathering" && c.Phase == "start");
        Assert.Contains("\"count\":12", gatherCall.Args); // D-06: 부족 수량을 count로 전달
    }

    [Fact]
    public async Task KillLongRunning_EndsGather_AndMarksResultKilled()
    {
        using var env = new FakeCliEnv();
        env.Set("FAKECLI_DELAY_EXECUTE_GATHERING", "8000");
        env.Set("FAKECLI_STOPFILE", null); // 정지 신호 없이 프로세스 종료만으로 끝나는지 본다
        var cli = env.CreateCli();

        var gather = cli.RunAsync(new CliCommand("execute_gathering", Body: new { displayName = "사과", count = 3 }));
        await WaitForAsync(() => env.ReadCalls().Any(c => c.Command == "execute_gathering" && c.Phase == "start"));

        Assert.Equal(1, cli.KillLongRunning());
        var r = await gather.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(r.Ok);
        Assert.True(r.Killed);
        Assert.Equal(0, cli.KillLongRunning());
    }

    [Fact]
    public async Task GeneralLane_SerializesConcurrentCalls()
    {
        using var env = new FakeCliEnv();
        env.Set("FAKECLI_DELAY_GET_MY_INFO", "300");
        var cli = env.CreateCli();

        var a = cli.RunAsync(new CliCommand("get_my_info"));
        var b = cli.RunAsync(new CliCommand("get_my_info"));
        var results = await Task.WhenAll(a, b);

        Assert.All(results, r => Assert.True(r.Ok, r.Error));
        var calls = env.ReadCalls().Where(c => c.Command == "get_my_info").OrderBy(c => c.At).ToList();
        Assert.Equal(new[] { "start", "end", "start", "end" }, calls.Select(c => c.Phase));
        Assert.True(results.Max(r => r.QueueWait) >= TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task SendGameChat_UsesWriteChatArgv_AndRejectsEmpty()
    {
        using var env = new FakeCliEnv();
        var cli = env.CreateCli();

        var empty = await cli.SendGameChatAsync("  \n ");
        Assert.False(empty.Ok);

        var ok = await cli.SendGameChatAsync("안녕하세요\n반가워요 😊");
        Assert.True(ok.Ok, ok.Error);

        var call = env.ReadCalls().Single(c => c.Command == "write_chat" && c.Phase == "start");
        Assert.Equal("안녕하세요 반가워요 😊", call.Args);
    }

    [Theory]
    [InlineData("execute_gathering", CliLane.LongRunning)]
    [InlineData("stop_action", CliLane.Priority)]
    [InlineData("stand_up", CliLane.Priority)]
    [InlineData("get_items", CliLane.General)]
    [InlineData("write_chat", CliLane.General)]
    public void Lanes_AreDecidedByCommandName(string command, CliLane lane)
    {
        Assert.Equal(lane, CliLanes.For(command));
        Assert.Equal(lane, new CliCommand(command).Lane);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("조건을 기다리다 시간이 초과됐습니다.");
            await Task.Delay(25);
        }
    }
}
