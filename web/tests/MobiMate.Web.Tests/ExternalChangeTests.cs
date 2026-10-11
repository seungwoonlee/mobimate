using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MobiMate.Web.Hosting;

namespace MobiMate.Web.Tests;

/// <summary>
/// 공유 저장 패치: 같은 저장 폴더를 쓰는 다른 프로그램(숙제 앱)이 공유 파일을 바꾸면 웹앱이 감지해
/// 화면이 이미 듣는 SSE 이벤트(homework.changed / state.changed)로 알린다. 자기 쓰기는 알리지 않는다.
/// </summary>
public class ExternalChangeTests
{
    private static string ManualId(HomeworkCatalog c) => c.Items.First(d => d.Active && d.Share == HomeworkShare.Character).Id;

    [Fact]
    public async Task ExternalHomeworkWrite_IsDetected_AndBroadcastAsHomeworkChanged()
    {
        using var host = new TestHost { ExternalWatch = true };
        var c = await host.LocalAsync();
        var sse = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await sse.Content.ReadAsStreamAsync());

        var catalog = host.Services.GetRequiredService<HomeworkCatalog>();
        var web = host.Services.GetRequiredService<HomeworkService>();
        web.Set("아이라_격투가", ManualId(catalog), completed: true);   // 웹앱 자신의 쓰기: 알리지 않는다

        // 숙제 앱 역할: 같은 폴더를 가리키는 별개의 서비스 객체가 쓴다
        var app = new HomeworkService(catalog, new HomeworkStore(host.StorageDir));
        app.Set("아이라_석궁사수", ManualId(catalog), completed: true);

        var e = await ReadEvent(reader, "homework.changed", TimeSpan.FromSeconds(15));
        Assert.Equal("external", e.GetProperty("reason").GetString());   // 첫 이벤트가 외부 변경이어야 한다(자기 쓰기 이벤트는 없다)
        Assert.True(web.GetBoard("아이라_석궁사수").Cards.Single(x => x.Id == ManualId(catalog)).IsDone);   // 웹앱이 따라잡았다
    }

    [Fact]
    public async Task ExternalAccountWrite_IsBroadcastAsStateChangedAccounts()
    {
        using var host = new TestHost { ExternalWatch = true };
        var c = await host.LocalAsync();
        var sse = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await sse.Content.ReadAsStreamAsync());

        var web = host.Services.GetRequiredService<AccountStore>();
        Assert.True(web.Update(d => { d.Assign["a_b"] = new AccountAssignment { Account = "x", Manual = true }; return true; }));   // 자기 쓰기

        var other = new AccountStore(host.StorageDir);
        Assert.True(other.Update(d => { d.Assign["c_d"] = new AccountAssignment { Account = "y", Manual = true }; return true; }));

        var e = await ReadEvent(reader, "state.changed", TimeSpan.FromSeconds(15));
        Assert.Contains("accounts", e.GetProperty("keys").EnumerateArray().Select(k => k.GetString()));
        var assign = web.Snapshot().Assign;
        Assert.True(assign.ContainsKey("a_b") && assign.ContainsKey("c_d"), "두 쪽의 지정이 모두 남아야 한다");
    }

    [Fact]
    public void ExternalWatchOff_ReadsStillSeeTheChange()
    {
        using var host = new TestHost();   // 감지 끔(기본)
        var web = host.Services.GetRequiredService<AccountStore>();
        new AccountStore(host.StorageDir).Update(d => { d.Assign["c_d"] = new AccountAssignment { Account = "y" }; return true; });
        Assert.True(web.Snapshot().Assign.ContainsKey("c_d"));   // 조회 경로가 바뀐 파일을 다시 읽는다
    }

    private static async Task<JsonElement> ReadEvent(StreamReader reader, string name, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        string? evt = null;
        while (!cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line == null) break;
            if (line.StartsWith("event: ")) evt = line[7..];
            else if (line.StartsWith("data: ") && evt == name) return JsonDocument.Parse(line[6..]).RootElement.Clone();
        }
        throw new TimeoutException($"SSE 이벤트 '{name}'를 받지 못했습니다.");
    }
}
