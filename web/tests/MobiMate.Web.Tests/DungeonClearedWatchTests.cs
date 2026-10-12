using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace MobiMate.Web.Tests;

/// <summary>
/// 요일 던전 클리어 즉시 반영: 감시(4초)가 던전 안이었거나 요일 던전 지역일 때만 get_activity도 읽어 Cleared 전환을 놓치지 않는다.
/// 던전 밖 평상시에는 활동을 더 읽지 않는다(맵은 마지막 헤더 값을 쓰므로 맵 조회도 늘지 않는다).
/// </summary>
public class DungeonClearedWatchTests
{
    private static string SamplesDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "tools", "FakeCli", "Samples"))) d = d.Parent;
        return Path.Combine(d!.FullName, "tools", "FakeCli", "Samples");
    }

    private static string MakeSamples(string map, string dungeonState)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-dungeon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(SamplesDir(), "*.json")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        Replace(Path.Combine(dir, "get_current_environment.json"), @"""GameSpaceDisplayName""\s*:\s*""[^""]*""", $"\"GameSpaceDisplayName\": \"{map}\"");
        SetState(dir, dungeonState);
        return dir;
    }

    private static void SetState(string dir, string state) =>
        Replace(Path.Combine(dir, "get_activity.json"), @"""State""\s*:\s*""[^""]*""", $"\"State\": \"{state}\"");

    private static void Replace(string path, string pattern, string with)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Regex.Replace(File.ReadAllText(path), pattern, with));
        File.Move(tmp, path, overwrite: true);
    }

    private static int ActivityCalls(TestHost host) => host.CliCalls().Count(c => c.Contains("get_activity"));

    private static async Task UntilAsync(Func<bool> cond, string what, int seconds = 15)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!cond())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException(what);
            await Task.Delay(30);
        }
    }

    private static async Task<T> WithSamples<T>(string dir, Func<Task<T>> body)
    {
        Environment.SetEnvironmentVariable("FAKECLI_SAMPLES_DIR", dir);
        Environment.SetEnvironmentVariable("MobiMate__QueryCacheTtl", "00:00:00");
        try { return await body(); }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_SAMPLES_DIR", null);
            Environment.SetEnvironmentVariable("MobiMate__QueryCacheTtl", null);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task OutsideDungeon_TheWatcherDoesNotReadActivity()
    {
        var dir = MakeSamples("창백한 산", "NotInDungeon");   // 사냥터는 던전이 아니다
        await WithSamples(dir, async () =>
        {
            using var host = new TestHost { WatchInterval = "00:00:00.200" };
            var c = await host.LocalAsync();
            await TestHost.Data(await c.GetAsync("/api/header"));   // 헤더가 맵·활동을 읽는다
            await UntilAsync(() => host.CliCalls().Count(x => x.Contains("get_my_info")) >= 4, "감시가 읽기 시작");
            var before = ActivityCalls(host);
            var infoBefore = host.CliCalls().Count(x => x.Contains("get_my_info"));
            await UntilAsync(() => host.CliCalls().Count(x => x.Contains("get_my_info")) >= infoBefore + 8, "감시가 더 읽음");
            Assert.Equal(before, ActivityCalls(host));
            return 0;
        });
    }

    [Fact]
    public async Task InDayDungeonMap_ClearedIsCaught_AndCompletesHomeworkRightAway()
    {
        var dir = MakeSamples("찬란한 유적 V", "InProgress");
        await WithSamples(dir, async () =>
        {
            using var host = new TestHost { WatchInterval = "00:00:00.200" };
            var c = await host.LocalAsync();
            await TestHost.Data(await c.GetAsync("/api/header"));   // 마지막 헤더가 요일 던전 지역을 알려 준다
            var service = host.Services.GetRequiredService<HomeworkService>();
            const string key = "아이라_격투가";
            await UntilAsync(() => ActivityCalls(host) >= 3, "던전 지역에서는 활동도 읽는다");
            Assert.NotEqual(HomeworkCardStatus.AutoDone, service.GetBoard(key).Cards.Single(x => x.Id == "daily_day_dungeon").Status);

            SetState(dir, "Cleared");   // 보스 처치
            await UntilAsync(() => service.GetBoard(key).Cards.Single(x => x.Id == "daily_day_dungeon").Status == HomeworkCardStatus.AutoDone, "클리어 즉시 완료");
            Assert.Equal("던전 클리어(게임 상태)", service.GetBoard(key).Cards.Single(x => x.Id == "daily_day_dungeon").Evidence);
            return 0;
        });
    }
}
