using Microsoft.Extensions.Logging.Abstractions;
using MobiMate.Web.Hosting;
using MobiMate.Web.Services;

namespace MobiMate.Web.Tests;

/// <summary>게임 CLI 업데이트 점검: 기준 저장 → 같으면 조용 → 바뀌면 기록하고 기준 갱신</summary>
public class CliChangeWatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-cli-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class StubCli(Func<string> output) : IGameCli
    {
        public bool IsAvailable => true;
        public string CliPath => "stub";
        public Task<CliResult> RunAsync(CliCommand cmd, CancellationToken ct = default)
            => Task.FromResult(new CliResult(true, output(), null, TimeSpan.Zero, TimeSpan.Zero));
    }

    private static string Caps(params string[] outputs) =>
        System.Text.Json.JsonSerializer.Serialize(new { commands = outputs.Select((o, i) => new { Command = $"get_{i}", Description = "d", BodyExample = "", OutputExample = o, Note = (string?)null }) });

    private CliChangeWatcher Make(Func<string> output) =>
        new(new StubCli(output), null!, new MobiMateOptions { StorageDir = _dir }, NullLogger<CliChangeWatcher>.Instance);

    [Fact]
    public async Task FirstCheck_SavesTheBaseline_AndSameAgainIsQuiet_AndAChangeIsRecorded()
    {
        var current = Caps("{ A, B }", "{ C }");
        var w = Make(() => current);
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

        var first = await w.CheckAsync(now, default);
        Assert.True(first!.FirstBaseline);
        Assert.Equal(2, first.Commands);
        Assert.True(File.Exists(Path.Combine(_dir, "cli_capabilities.json")));

        var same = await w.CheckAsync(now.AddHours(6), default);
        Assert.False(same!.FirstBaseline);
        Assert.Null(same.Summary);                                   // 바뀐 것이 없다
        Assert.False(File.Exists(Path.Combine(_dir, "cli_changes.txt")));

        current = Caps("{ A, B, NewField }", "{ C }", "{ D }");      // 업데이트: 필드 1개·명령 1개 추가
        var changed = await w.CheckAsync(now.AddHours(12), default);
        Assert.NotNull(changed!.ChangedAt);
        Assert.Contains("새 명령 1개: get_2", changed.Summary);
        Assert.Contains("get_0: 새 필드 NewField", changed.Summary);
        Assert.Contains("get_2", File.ReadAllText(Path.Combine(_dir, "cli_changes.txt")));

        var after = await w.CheckAsync(now.AddHours(18), default);   // 기준이 새로 저장돼 다시 알리지 않는다
        Assert.Equal(changed.Summary, after!.Summary);               // 마지막 변경 내용은 기억한다
        Assert.Equal(changed.ChangedAt, after.ChangedAt);
        Assert.Equal(1, File.ReadAllText(Path.Combine(_dir, "cli_changes.txt")).Split("[2026-").Length - 1);
    }

    [Fact]
    public async Task UnreadableOutput_LeavesTheBaselineAlone()
    {
        var good = Caps("{ A }");
        var output = good;
        var w = Make(() => output);
        await w.CheckAsync(DateTimeOffset.UtcNow, default);
        var before = File.ReadAllText(Path.Combine(_dir, "cli_capabilities.json"));
        output = "게임이 꺼져 있습니다";                                // JSON이 아니다
        Assert.Null(await w.CheckAsync(DateTimeOffset.UtcNow, default));
        Assert.Equal(before, File.ReadAllText(Path.Combine(_dir, "cli_capabilities.json")));
    }

    [Fact]
    public async Task Api_ReportsNothingBeforeTheFirstCheck()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var d = await TestHost.Data(await c.GetAsync("/api/cli/changes"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, d.GetProperty("checkedAt").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, d.GetProperty("summary").ValueKind);
    }
}
