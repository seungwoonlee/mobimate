using System.Net;
using System.Text;
using System.Text.Json;
using MobiMate.Web.Voice;
using Xunit;

namespace MobiMate.Web.Tests;

public class Qwen3Tests
{
    private const int Rate = AudioConverter.TargetRate;

    private static HttpRequestMessage Req(HttpMethod m, string url, object body)
    {
        var r = new HttpRequestMessage(m, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        r.Headers.Add("X-MobiMate-Req", "1");
        return r;
    }

    private static float[] Tone(double sec, float amp) => Enumerable.Range(0, (int)(sec * Rate)).Select(i => (float)(amp * Math.Sin(i * 0.05))).ToArray();
    private static float[] Quiet(double sec) => new float[(int)(sec * Rate)];

    /// <summary>100ms 단위로 넣는다 (실제 마이크 블록 크기)</summary>
    private static void Feed(ISttStream s, float[] samples)
    {
        for (var i = 0; i < samples.Length; i += Rate / 10)
            s.Accept(samples.AsSpan(i, Math.Min(Rate / 10, samples.Length - i)).ToArray());
    }

    [Fact]
    public void Utterance_SilenceAfterSpeech_EndsAndDecodesOnce()
    {
        var decoded = new List<int>();
        using var s = new UtteranceStream(x => { decoded.Add(x.Length); return "안녕하세요"; }, () => 500);
        Feed(s, Quiet(0.5));
        Assert.False(s.Poll().IsEndpoint);
        Feed(s, Tone(1.0, 0.2f));
        Assert.False(s.Poll().IsEndpoint);          // 말하는 중
        Feed(s, Quiet(0.3));
        Assert.False(s.Poll().IsEndpoint);          // 아직 짧게 쉬었을 뿐
        Feed(s, Quiet(0.3));
        var p = s.Poll();
        Assert.True(p.IsEndpoint);
        Assert.Equal("안녕하세요", p.Text);
        Assert.Single(decoded);
        // 앞뒤 0.3초 여유 + 말 1.0초 안팎만 넘긴다 (조용한 앞부분 0.5초는 버린다)
        Assert.InRange(decoded[0], (int)(1.4 * Rate), (int)(1.8 * Rate));
        Assert.False(s.Poll().IsEndpoint);          // 한 번 끝낸 뒤에는 다시 나오지 않는다
    }

    [Fact]
    public void Utterance_ShortEndSilenceSetting_StillWaitsHalfSecond()
    {
        using var s = new UtteranceStream(_ => "가", () => 300);
        Feed(s, Tone(0.8, 0.2f));
        Feed(s, Quiet(0.4));
        Assert.False(s.Poll().IsEndpoint);          // 설정이 0.3초여도 0.5초는 기다린다
        Feed(s, Quiet(0.2));
        Assert.True(s.Poll().IsEndpoint);
    }

    [Fact]
    public void Utterance_OnlyNoise_NeverEnds()
    {
        var called = false;
        using var s = new UtteranceStream(_ => { called = true; return "x"; }, () => 500);
        Feed(s, Tone(3.0, 0.004f));                 // 아주 작은 소리: 말이 아니다
        Assert.False(s.Poll().IsEndpoint);
        Assert.Equal("", s.Finish());
        Assert.False(called);
    }

    [Fact]
    public void Utterance_Finish_DecodesWhatIsSpokenSoFar()
    {
        using var s = new UtteranceStream(_ => "중간에 뗐어요", () => 500);
        Feed(s, Tone(1.0, 0.2f));
        Assert.Equal("중간에 뗐어요", s.Finish());   // 키를 뗀 순간까지의 말
    }

    [Fact]
    public void Utterance_VeryShortClick_IsIgnored()
    {
        var called = false;
        using var s = new UtteranceStream(_ => { called = true; return "x"; }, () => 500);
        Feed(s, Tone(0.1, 0.3f));
        Feed(s, Quiet(1.0));
        var p = s.Poll();
        Assert.True(p.IsEndpoint);
        Assert.Equal("", p.Text);
        Assert.False(called);
    }

    [Theory]
    [InlineData("오늘 레이드 같이 가요.", "오늘 레이드 같이 가요")]
    [InlineData("같이 갈래요?", "같이 갈래요?")]
    [InlineData("  좋아요 .. ", "좋아요")]
    [InlineData(null, "")]
    public void Clean_StripsTrailingPeriod(string? raw, string expected) => Assert.Equal(expected, Qwen3SttEngine.Clean(raw));

    [Fact]
    public void Manifest_IsPinnedAndComplete()
    {
        Assert.Equal(40, SttModelInstaller.Qwen3Revision.Length);
        Assert.Contains(SttModelLocator.Qwen3Files, f => f.Name == "tokenizer/vocab.json");
        Assert.All(SttModelLocator.Qwen3Files, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256));
        Assert.InRange(SttModelLocator.Qwen3Files.Sum(f => f.Size), 900_000_000L, 1_000_000_000L);
        Assert.True(SttModelInstaller.AllowedUri(new Uri(SttModelInstaller.Qwen3BaseUri, "encoder.int8.onnx")));
    }

    [Fact]
    public void Locator_Qwen3_UsesOwnFolderAndIgnoresEnvOverride()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-q3-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MOBIMATE_STT_MODEL_DIR", @"X:\other");
        try
        {
            var q = new SttModelLocator(dir, SttModelLocator.Qwen3Files, "qwen3", envOverride: false);
            Assert.Equal(Path.Combine(dir, "stt", "qwen3"), q.Dir);
            Assert.False(q.IsInstalled);
        }
        finally { Environment.SetEnvironmentVariable("MOBIMATE_STT_MODEL_DIR", null); }
    }

    [Fact]
    public async Task Settings_Engine_Validates()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { engine = "whisper" }))).StatusCode);
        var r = await TestHost.Data(await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { engine = "qwen3" })));
        Assert.Equal("qwen3", r.GetProperty("settings").GetProperty("engine").GetString());
        Assert.False(r.GetProperty("restartRequired").GetBoolean());   // 다음 듣기부터 바로 적용
    }
}

public sealed class RequiresQwen3FactAttribute : FactAttribute
{
    public RequiresQwen3FactAttribute()
    {
        var dir = Environment.GetEnvironmentVariable("MOBIMATE_QWEN3_DIR");
        var wavs = Environment.GetEnvironmentVariable("MOBIMATE_QWEN3_WAVS");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || string.IsNullOrEmpty(wavs) || !Directory.Exists(wavs))
            Skip = "MOBIMATE_QWEN3_DIR·MOBIMATE_QWEN3_WAVS가 없어 건너뜀 (실제 Qwen3 모델 시험)";
    }
}

public class Qwen3IntegrationTests
{
    [RequiresQwen3Fact]
    public void RealEngine_SplitsSentencesBySilence_AndRecognizesKorean()
    {
        // MOBIMATE_QWEN3_DIR = 앱 저장 폴더(%APPDATA%\MobiMateWeb). 모델은 그 아래 stt\qwen3에 있다.
        var locator = new MobiMate.Web.Voice.SttModelLocator(Environment.GetEnvironmentVariable("MOBIMATE_QWEN3_DIR")!, MobiMate.Web.Voice.SttModelLocator.Qwen3Files, "qwen3", envOverride: false);
        Assert.True(locator.IsInstalled, string.Join(", ", locator.Missing()));
        using var engine = new MobiMate.Web.Voice.Qwen3SttEngine(locator, 4, Array.Empty<string>(), () => 500);
        using var stream = engine.CreateStream();
        var files = Directory.GetFiles(Environment.GetEnvironmentVariable("MOBIMATE_QWEN3_WAVS")!, "k?.wav").OrderBy(f => f).Take(3).ToArray();
        var finals = new List<string>();
        foreach (var f in files)
        {
            using var r = new NAudio.Wave.WaveFileReader(f);
            var conv = new MobiMate.Web.Voice.AudioConverter(r.WaveFormat);
            var buf = new byte[r.WaveFormat.AverageBytesPerSecond / 10 / r.WaveFormat.BlockAlign * r.WaveFormat.BlockAlign];
            int n;
            while ((n = r.Read(buf, 0, buf.Length)) > 0)
            {
                var block = conv.Push(buf, n);
                if (block.Length == 0) continue;
                stream.Accept(block);
                var p = stream.Poll();
                if (p.IsEndpoint) { stream.Reset(); if (p.Text.Length > 0) finals.Add(p.Text); }
            }
            for (var i = 0; i < 12; i++)   // 문장 사이 1.2초 무음
            {
                stream.Accept(new float[1600]);
                var p = stream.Poll();
                if (p.IsEndpoint) { stream.Reset(); if (p.Text.Length > 0) finals.Add(p.Text); }
            }
        }
        var all = string.Join(" | ", finals);
        Assert.True(finals.Count == 3, "문장 수 " + finals.Count + ": " + all);
        Assert.True(finals[0].Contains("레이드") && finals[2].Contains("클리어"), all);
    }
}

public class EngineReleaseTests
{
    [Fact]
    public void UnusedEngine_IsReleasedOnlyAfterItHadTimeToFinishASession()
    {
        Assert.False(MobiMate.Web.Voice.SherpaEngineProvider.CanRelease(handedAtMs: 1_000, nowMs: 1_000 + 89_999));   // 방금까지 듣던 세션이 있을 수 있다
        Assert.True(MobiMate.Web.Voice.SherpaEngineProvider.CanRelease(handedAtMs: 1_000, nowMs: 1_000 + 90_000));
        Assert.True(MobiMate.Web.Voice.SherpaEngineProvider.CanRelease(handedAtMs: 0, nowMs: 5_000_000));              // 한 번도 내어 준 적 없다
        Assert.True(MobiMate.Web.Voice.SherpaEngineProvider.ReleaseAfterMs > 60_000);                                  // 듣기 최대 시간(60초)보다 길어야 한다
    }
}
