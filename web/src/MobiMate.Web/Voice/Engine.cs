using MobiMate.Web.Hosting;
using SherpaOnnx;

namespace MobiMate.Web.Voice;

/// <summary>인식 중 결과. <see cref="IsEndpoint"/>는 말이 멈춰 문장이 끝났다는 뜻이다.</summary>
public readonly record struct SttPartial(string Text, bool IsEndpoint);

/// <summary>한 세션의 인식 스트림. 모든 호출은 같은 스레드(서비스 작업 스레드)에서 한다.</summary>
public interface ISttStream : IDisposable
{
    /// <summary>16kHz 모노 샘플을 넣고 디코딩한다.</summary>
    void Accept(float[] samples);

    SttPartial Poll();

    /// <summary>문장이 확정된 뒤 다음 문장을 위해 비운다.</summary>
    void Reset();

    /// <summary>입력을 끝내고 남은 글자를 돌려준다. 이 뒤에는 쓰지 않는다.</summary>
    string Finish();
}

public interface ISttEngine : IDisposable
{
    ISttStream CreateStream();
}

/// <summary>엔진을 만드는 곳. 모델 로딩이 무거워서 처음 쓸 때 한 번 만들고 계속 쓴다.</summary>
public interface ISttEngineProvider
{
    bool IsInstalled { get; }
    ISttEngine GetEngine();

    /// <summary>
    /// 인식기를 미리 올려 둔다. 처음 단축키를 눌렀을 때 모델을 올리느라 첫 문장을 놓치지 않게 한다.
    /// 모델이 없거나 올리지 못해도 예외 없이 끝난다.
    /// </summary>
    Task WarmUpAsync();
}

/// <summary>모델 파일 하나: 이름, 크기, SHA-256 (저장소 커밋 고정본의 SHA256SUMS 값)</summary>
public sealed record SttModelFile(string Name, long Size, string Sha256);

/// <summary>
/// 모델 파일 위치와 설치 확인 (FR-VC-07). 정해진 파일이 정해진 크기로 모두 있어야 설치된 것으로 본다.
/// 해시 검증은 설치할 때 한다. 로드 전에는 크기만 확인한다(네이티브 라이브러리는 손상 파일에서 죽을 수 있다).
/// 개발·시험용으로 환경변수 MOBIMATE_STT_MODEL_DIR이 있으면 그 폴더를 쓴다.
/// </summary>
public sealed class SttModelLocator
{
    public const int Chunk = 16;

    /// <summary>순서: 인코더, 디코더, 조인너, 글자표</summary>
    public static readonly IReadOnlyList<SttModelFile> DefaultFiles = new[]
    {
        new SttModelFile($"encoder-epoch-99-avg-1-chunk-{Chunk}-left-128.int8.onnx", 156840518L, "e595e2e37f46078387868ffa656b775de787fa511cf851cf8e1892d7dbdabe54"),
        new SttModelFile($"decoder-epoch-99-avg-1-chunk-{Chunk}-left-128.int8.onnx", 1544210L, "f5dfa6c8609b29da86c739d4904889475c33b62e28e70c619952a0ee6c31416f"),
        new SttModelFile($"joiner-epoch-99-avg-1-chunk-{Chunk}-left-128.int8.onnx", 1270777L, "64efd9aeb71fb2278c713b3d5eb5ec45edf6b2ccd8716d3709044f17723c13fd"),
        new SttModelFile("tokens.txt", 20844L, "435dfb9e0a2b6a79124f1a4d8f0f33a951b25384726e2e0d854f081533e6ec9d"),
    };

    public IReadOnlyList<SttModelFile> Files { get; }

    public string Dir { get; }

    public SttModelLocator(string storageDir, IReadOnlyList<SttModelFile>? files = null)
    {
        Files = files ?? DefaultFiles;
        var over = Environment.GetEnvironmentVariable("MOBIMATE_STT_MODEL_DIR");
        Dir = !string.IsNullOrWhiteSpace(over) ? over : Path.Combine(storageDir, "stt", "model");
    }

    public string PathOf(string name) => Path.Combine(Dir, name);

    public IReadOnlyList<string> Missing() => Files
        .Where(f => !File.Exists(PathOf(f.Name)) || new FileInfo(PathOf(f.Name)).Length != f.Size)
        .Select(f => f.Name)
        .ToList();

    public bool IsInstalled => Missing().Count == 0;
}

/// <summary>sherpa-onnx 한국어 스트리밍 Zipformer (상세설계 §3.9). 기본 1스레드(FR-VC-06)</summary>
public sealed class SherpaSttEngine : ISttEngine
{
    private readonly OnlineRecognizer _recognizer;

    public SherpaSttEngine(SttModelLocator model, int threads)
    {
        var c = new OnlineRecognizerConfig();
        c.FeatConfig.SampleRate = AudioConverter.TargetRate;
        c.FeatConfig.FeatureDim = 80;
        c.ModelConfig.Transducer.Encoder = model.PathOf(model.Files[0].Name);
        c.ModelConfig.Transducer.Decoder = model.PathOf(model.Files[1].Name);
        c.ModelConfig.Transducer.Joiner = model.PathOf(model.Files[2].Name);
        c.ModelConfig.Tokens = model.PathOf(model.Files[3].Name);
        c.ModelConfig.NumThreads = Math.Clamp(threads, 1, 4);
        c.ModelConfig.Provider = "cpu";
        c.DecodingMethod = "modified_beam_search";
        c.MaxActivePaths = 8;
        c.EnableEndpoint = 1;
        c.Rule1MinTrailingSilence = 2.4f;   // 아직 말하지 않았을 때
        c.Rule2MinTrailingSilence = 0.8f;   // 말한 뒤 이만큼 조용하면 문장 끝 (P0 실측)
        c.Rule3MinUtteranceLength = 20f;
        _recognizer = new OnlineRecognizer(c);
    }

    public ISttStream CreateStream() => new Stream(_recognizer, _recognizer.CreateStream());

    public void Dispose() => _recognizer.Dispose();

    private sealed class Stream(OnlineRecognizer recognizer, OnlineStream stream) : ISttStream
    {
        public void Accept(float[] samples)
        {
            stream.AcceptWaveform(AudioConverter.TargetRate, samples);
            while (recognizer.IsReady(stream)) recognizer.Decode(stream);
        }

        public SttPartial Poll() => new(recognizer.GetResult(stream).Text.Trim(), recognizer.IsEndpoint(stream));

        public void Reset() => recognizer.Reset(stream);

        public string Finish()
        {
            stream.AcceptWaveform(AudioConverter.TargetRate, new float[AudioConverter.TargetRate]);   // 꼬리 무음 1초: 마지막 조각까지 디코딩
            stream.InputFinished();
            while (recognizer.IsReady(stream)) recognizer.Decode(stream);
            return recognizer.GetResult(stream).Text.Trim();
        }

        public void Dispose() => stream.Dispose();
    }
}

/// <summary>실제 엔진 공급자: 처음 요청할 때 모델을 올리고 이후 재사용한다.</summary>
public sealed class SherpaEngineProvider(SttModelLocator model, WebSettingsStore settings, ILogger<SherpaEngineProvider> log) : ISttEngineProvider, IDisposable
{
    private readonly object _lock = new();
    private SherpaSttEngine? _engine;

    public bool IsInstalled => model.IsInstalled;

    public ISttEngine GetEngine()
    {
        lock (_lock)
        {
            if (_engine != null) return _engine;
            if (!model.IsInstalled) throw new InvalidOperationException("음성 인식 모델이 설치되어 있지 않습니다.");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var engine = new SherpaSttEngine(model, settings.Current.SttThreads);
            log.LogInformation("음성 인식기를 올렸습니다 ({Ms}ms, {Threads}스레드)", clock.ElapsedMilliseconds, settings.Current.SttThreads);
            return _engine = engine;
        }
    }

    public Task WarmUpAsync() => !model.IsInstalled ? Task.CompletedTask : Task.Run(() =>
    {
        try { GetEngine(); }
        catch (Exception ex) { log.LogWarning(ex, "음성 인식기를 미리 올리지 못했습니다"); }
    });

    public void Dispose()
    {
        lock (_lock) { _engine?.Dispose(); _engine = null; }
    }
}
