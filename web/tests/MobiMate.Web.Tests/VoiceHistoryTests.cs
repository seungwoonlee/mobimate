using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MobiMate.Web.Voice;
using Xunit;

namespace MobiMate.Web.Tests;

/// <summary>지난 음성 기록 보정 (FR-VC-16)</summary>
public class VoiceHistoryTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mm-hist-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void History_KeepsNewestFirst_CapsAt100_AndDropsAfterSevenDays()
    {
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var h = new VoiceHistory(TempDir(), () => now);
        for (var i = 0; i < 105; i++) { now = now.AddMinutes(1); h.Add("원문" + i, "문장" + i, true); }
        var list = h.List();
        Assert.Equal(VoiceHistory.MaxEntries, list.Count);
        Assert.Equal("문장104", list[0].Text);                    // 최근 것이 앞
        Assert.Equal("문장5", list[^1].Text);                     // 오래된 5개는 밀려남
        now = now.AddDays(8);
        Assert.Empty(h.List());                                   // 7일이 지나면 사라진다
    }

    [Fact]
    public void History_SurvivesRestart_AndClearRemovesEverything()
    {
        var dir = TempDir();
        new VoiceHistory(dir).Add("힐러한분만더구해요", "힐러 한 분만 더 구해요", false);
        var again = new VoiceHistory(dir);
        Assert.Single(again.List());
        again.Clear();
        Assert.Empty(new VoiceHistory(dir).List());
    }

    [Fact]
    public void LearnCorrection_ActivatesTheSwapAtOnce_UnlikeNormalLearning()
    {
        var v = new VoiceVocabulary(TempDir());
        v.SetLearning(true);
        Assert.True(v.LearnCorrection("힐러 한 분만 더 구해요", "일러한분만더구해요") > 0);
        var (text, applied) = v.Apply("일러한분만더구해요");
        Assert.Equal("힐러한분만더구해요", text);                  // 한 번 고쳤을 뿐인데 바로 적용된다
        Assert.NotEmpty(applied);
    }

    [Fact]
    public void LearnCorrection_ReturnsMinusOne_WhenLearningIsOff()
    {
        var v = new VoiceVocabulary(TempDir());
        v.SetLearning(false);
        Assert.Equal(-1, v.LearnCorrection("힐러 구해요", "일러구해요"));
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string url, object body)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        r.Headers.Add("X-MobiMate-Req", "1");
        return await c.SendAsync(r);
    }

    [Fact]
    public async Task Api_ListCorrectLearn_AndDelete()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var history = host.Services.GetRequiredService<VoiceHistory>();
        var e = history.Add("일러한분만더구해요", "일러 한 분만 더 구해요", true);

        var list = await TestHost.Data(await c.GetAsync("/api/stt/history"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("일러 한 분만 더 구해요", list[0].GetProperty("text").GetString());

        var learn = await Post(c, "/api/stt/history/correct", new { id = e.Id, text = "힐러 한 분만 더 구해요" });
        Assert.Equal(HttpStatusCode.OK, learn.StatusCode);
        var body = await TestHost.Data(learn);
        Assert.True(body.GetProperty("learned").GetInt32() > 0);
        Assert.Equal("힐러 한 분만 더 구해요", body.GetProperty("entry").GetProperty("corrected").GetString());

        var vocab = await TestHost.Data(await c.GetAsync("/api/stt/vocab"));
        Assert.Contains(vocab.GetProperty("pairs").EnumerateArray(), p => p.GetProperty("active").GetBoolean());   // 바로 적용되는 쌍

        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, "/api/stt/history/correct", new { id = "없는id", text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, "/api/stt/history/correct", new { id = e.Id, text = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(c, "/api/stt/history/delete", new { id = e.Id })).StatusCode);
        Assert.Equal(0, (await TestHost.Data(await c.GetAsync("/api/stt/history"))).GetArrayLength());
    }

    [Fact]
    public async Task Api_CorrectionIsRefused_WhenLearningIsOff()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        var e = host.Services.GetRequiredService<VoiceHistory>().Add("일러구해요", "일러 구해요", true);
        await c.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/stt/vocab") { Content = JsonContent.Create(new { learningOn = false }), Headers = { { "X-MobiMate-Req", "1" } } });
        Assert.Equal(HttpStatusCode.Conflict, (await Post(c, "/api/stt/history/correct", new { id = e.Id, text = "힐러 구해요" })).StatusCode);
    }

    [Fact]
    public async Task Api_IsRefusedFromAPhone()
    {
        using var host = new TestHost();
        var local = await host.LocalAsync();
        var phone = await host.LanAsync(local);
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.GetAsync("/api/stt/history")).StatusCode);
    }
}
