using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MobiMate.Web.Tests;

/// <summary>
/// 캐릭터 전환 감시 (v1.6): 화면이 아무것도 요청하지 않아도 서버가 캐릭터가 바뀐 것을 몇 초 안에 알아채고
/// SSE "header"로 밀어 준다. 게임 연결이 끊겼다 돌아온 순간(캐릭터 선택창을 거칠 때)도 바로 새로 읽는다.
/// </summary>
public class CharacterWatcherTests
{
    private static string SamplesDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "tools", "FakeCli", "Samples"))) d = d.Parent;
        return Path.Combine(d!.FullName, "tools", "FakeCli", "Samples");
    }

    private static async Task<JsonElement> NextHeader(StreamReader reader, Func<JsonElement, bool> match, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        using var cts = new CancellationTokenSource(timeout);
        string? evt = null;
        var seen = new List<string>();
        try
        {
        while (DateTime.UtcNow < end)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line == null) break;
            if (line.StartsWith("event: ")) evt = line[7..];
            else if (line.StartsWith("data: ") && evt == "header")
            {
                var e = JsonDocument.Parse(line[6..]).RootElement.Clone();
                seen.Add($"{e.GetProperty("characterKey").GetString()}/sel={e.GetProperty("selecting").GetBoolean()}");
                if (match(e)) return e;
            }
        }
        }
        catch (OperationCanceledException) { }
        throw new TimeoutException("조건에 맞는 SSE header를 받지 못했습니다. 받은 header: " + string.Join(", ", seen));
    }

    [Fact]
    public async Task CharacterSwitch_IsPushedWithinSeconds_WithoutAnyClientRequest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var cmd in new[] { "status", "get_my_info", "get_activity", "get_current_environment", "get_currencies", "get_daily_missions", "get_quests" })
        {
            var src = Path.Combine(SamplesDir(), cmd + ".json");
            if (File.Exists(src)) File.Copy(src, Path.Combine(dir, cmd + ".json"));
        }
        Environment.SetEnvironmentVariable("FAKECLI_SAMPLES_DIR", dir);
        Environment.SetEnvironmentVariable("MobiMate__QueryCacheTtl", "00:00:00");
        try
        {
            using var host = new TestHost { WatchInterval = "00:00:00.200" };
            var c = await host.LocalAsync();
            var first = await TestHost.Data(await c.GetAsync("/api/header"));
            Assert.Equal("아이라_격투가", first.GetProperty("characterKey").GetString());

            var sse = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
            using var reader = new StreamReader(await sse.Content.ReadAsStreamAsync());

            // 게임에서 직업이 댄서로 바뀐 상황: 이 뒤로 화면은 아무것도 요청하지 않는다 (누구로 볼지는 CharacterIdentity가 정하고, 여기서는 서버가 알아채 밀어 주는지를 본다)
            var p = Path.Combine(dir, "get_my_info.json");
            File.WriteAllText(p, Regex.Replace(File.ReadAllText(p), "\"EnabledCombatJobDisplayName\"\\s*:\\s*\"[^\"]*\"", "\"EnabledCombatJobDisplayName\": \"댄서\""));

            var manual = await TestHost.Data(await c.GetAsync("/api/header"));
            var pushed = await NextHeader(reader, h => h.GetProperty("character").GetProperty("job").GetString() == "댄서", TimeSpan.FromSeconds(10));
            Assert.False(pushed.GetProperty("selecting").GetBoolean());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_SAMPLES_DIR", null);
            Environment.SetEnvironmentVariable("MobiMate__QueryCacheTtl", null);
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
