using MobiMate.Web.Hosting;
using MobiMate.Web.Voice;
using NAudio.Wave;

namespace MobiMate.Web.Tests;

/// <summary>
/// 실제 모델이 있을 때만 도는 시험. 환경변수 MOBIMATE_STT_MODEL_DIR(모델 폴더)과 MOBIMATE_STT_TEST_WAV(한국어 음성 wav)가 둘 다 있어야 한다.
/// 없으면 "건너뜀"으로 표시된다(통과로 가장하지 않는다). 모델은 앱의 첫 사용 설치 대상이라 저장소·CI에 넣지 않는다.
/// </summary>
public sealed class RequiresSttModelFactAttribute : FactAttribute
{
    public RequiresSttModelFactAttribute()
    {
        var dir = Environment.GetEnvironmentVariable("MOBIMATE_STT_MODEL_DIR");
        var wav = Environment.GetEnvironmentVariable("MOBIMATE_STT_TEST_WAV");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || string.IsNullOrEmpty(wav) || !File.Exists(wav))
            Skip = "MOBIMATE_STT_MODEL_DIR·MOBIMATE_STT_TEST_WAV가 없어 건너뜀 (실제 모델 시험)";
    }
}

public class SherpaEngineIntegrationTests
{
    /// <summary>시험용 wav가 읽어 주는 문장 (P0에서 만든 합성 음성, 띄어쓰기 없이 비교)</summary>
    private static readonly string[] Expected =
    {
        "지금레이드들어갑니다준비해주세요", "힐러한분만더구합니다", "저는잠깐자리비우겠습니다", "보스패턴조심하세요바닥장판나옵니다",
        "수고하셨습니다다음에또같이해요", "파티초대부탁드립니다", "포션이부족해서마을에다녀올게요", "다음층으로이동하겠습니다",
    };

    [RequiresSttModelFact]
    public void RealEngine_RecognizesKoreanSpeech_ThroughTheDeviceFormatConverter()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-stt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var locator = new SttModelLocator(dir);   // MOBIMATE_STT_MODEL_DIR이 우선한다
            Assert.True(locator.IsInstalled, "모델 파일 누락: " + string.Join(", ", locator.Missing()));
            using var provider = new SherpaEngineProvider(locator, new WebSettingsStore(dir));
            var engine = provider.GetEngine();
            using var stream = engine.CreateStream();

            using var reader = new WaveFileReader(Environment.GetEnvironmentVariable("MOBIMATE_STT_TEST_WAV")!);
            var converter = new AudioConverter(reader.WaveFormat);   // 파일 형식(예: 48kHz 스테레오)에서 16kHz 모노로
            var finals = new List<string>();
            var buf = new byte[reader.WaveFormat.AverageBytesPerSecond / 10 / reader.WaveFormat.BlockAlign * reader.WaveFormat.BlockAlign];
            int n;
            while ((n = reader.Read(buf, 0, buf.Length)) > 0)
            {
                var block = converter.Push(buf, n);
                if (block.Length == 0) continue;
                stream.Accept(block);
                var p = stream.Poll();
                if (p.IsEndpoint)
                {
                    stream.Reset();
                    if (p.Text.Length > 0) finals.Add(p.Text);
                }
            }
            var tail = stream.Finish();
            if (tail.Length > 0) finals.Add(tail);

            Assert.Equal(Expected, finals);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
