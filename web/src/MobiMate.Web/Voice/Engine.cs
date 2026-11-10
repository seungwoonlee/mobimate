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

    /// <summary>Qwen3-ASR 0.6B int8 (sherpa-onnx 변환본). 이름의 "/"는 하위 폴더. 저장소 커밋을 고정한다.</summary>
    public static readonly IReadOnlyList<SttModelFile> Qwen3Files = new[]
    {
        new SttModelFile("conv_frontend.onnx", 44148281L, "d22dc4423e0940e49884e903d2ea2f7e5567c14fc1aed97e4e26d6b8f208ef9e"),
        new SttModelFile("encoder.int8.onnx", 182491662L, "60748d3e6744a57c9c91e1b17424a6c2990567e8adceb0783940c03ed98fa9d9"),
        new SttModelFile("decoder.int8.onnx", 755914231L, "4f6885be5959ae26af3089d38ee7972c5fafbeeb1cf8d5e76eab6d8b61ca5771"),
        new SttModelFile("tokenizer/merges.txt", 1671853L, "8831e4f1a044471340f7c0a83d7bd71306a5b867e95fd870f74d0c5308a904d5"),
        new SttModelFile("tokenizer/tokenizer_config.json", 12487L, "4942d005604266809309cabc9f4e9cb89ce855d59b14681fdc0e1cc62ea26c4c"),
        new SttModelFile("tokenizer/vocab.json", 2776833L, "ca10d7e9fb3ed18575dd1e277a2579c16d108e32f27439684afa0e10b1440910"),
    };

    /// <param name="subDir">stt 폴더 아래 모델 폴더 이름</param>
    /// <param name="envOverride">개발·시험용 환경변수 MOBIMATE_STT_MODEL_DIR을 따를지 (기본 모델만)</param>
    public SttModelLocator(string storageDir, IReadOnlyList<SttModelFile>? files = null, string subDir = "model", bool envOverride = true)
    {
        Files = files ?? DefaultFiles;
        var over = envOverride ? Environment.GetEnvironmentVariable("MOBIMATE_STT_MODEL_DIR") : null;
        Dir = !string.IsNullOrWhiteSpace(over) ? over : Path.Combine(storageDir, "stt", subDir);
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

    public SherpaSttEngine(SttModelLocator model, int threads, string? hotwordsFile = null, int endSilenceMs = 500)
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
        c.Rule2MinTrailingSilence = Math.Clamp(endSilenceMs, 300, 2000) / 1000f;   // 말한 뒤 이만큼 조용하면 문장 끝 (설정, 기본 0.5초. P0 실측 때는 0.8초)
        c.Rule3MinUtteranceLength = 20f;
        if (hotwordsFile != null)
        {
            c.HotwordsFile = hotwordsFile;
            c.HotwordsScore = 2.0f;   // P0: 2에서는 해가 없었고 6부터 문장이 깨졌다
            c.ModelConfig.ModelingUnit = "cjkchar";
        }
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
public sealed class SherpaEngineProvider(SttModelLocator model, WebSettingsStore settings, VoiceVocabulary vocab, ILogger<SherpaEngineProvider> log, SttModelLocator? qwen3 = null) : ISttEngineProvider, IDisposable
{
    private readonly object _lock = new();
    private SherpaSttEngine? _engine;
    private Qwen3SttEngine? _qwen;
    private long _zipHandedAt, _qwenHandedAt;   // 각 엔진을 마지막으로 내어 준 시각 (Environment.TickCount64)

    /// <summary>쓰지 않는 엔진을 내리기 전에 기다리는 시간: 그 엔진으로 듣던 세션(최대 60초)이 끝날 여유</summary>
    internal const long ReleaseAfterMs = 90_000;

    /// <summary>쓰지 않는 엔진을 지금 해제해도 되는가: 내어 준 지 충분히 지났을 때만 (듣는 중인 세션이 쓰고 있을 수 있다)</summary>
    internal static bool CanRelease(long handedAtMs, long nowMs) => nowMs - handedAtMs >= ReleaseAfterMs;

    private bool UseQwen => qwen3 != null && settings.Current.SttEngine == "qwen3";

    public bool IsInstalled => UseQwen ? qwen3!.IsInstalled : model.IsInstalled;

    public ISttEngine GetEngine()
    {
        lock (_lock)
        {
            if (UseQwen)
            {
                ReleaseUnused(zip: true);
                _qwenHandedAt = Environment.TickCount64;
                if (_qwen != null) return _qwen;
                if (!qwen3!.IsInstalled) throw new InvalidOperationException("Qwen3 음성 인식 모델이 설치되어 있지 않습니다.");
                var qclock = System.Diagnostics.Stopwatch.StartNew();
                var words = settings.Current.SttHotwords ? vocab.HotwordCandidates(Qwen3SttEngine.MaxHotwords).ToList() : new List<string>();
                var qe = new Qwen3SttEngine(qwen3, Math.Max(settings.Current.SttThreads, 4), words, () => settings.Current.SttEndSilenceMs);
                log.LogInformation("Qwen3 음성 인식기를 올렸습니다 ({Ms}ms)", qclock.ElapsedMilliseconds);
                return _qwen = qe;
            }
            ReleaseUnused(zip: false);
            _zipHandedAt = Environment.TickCount64;
            if (_engine != null) return _engine;
            if (!model.IsInstalled) throw new InvalidOperationException("음성 인식 모델이 설치되어 있지 않습니다.");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var hotwords = settings.Current.SttHotwords ? HotwordFile.Write(model, vocab, log) : null;
            var engine = new SherpaSttEngine(model, settings.Current.SttThreads, hotwords, settings.Current.SttEndSilenceMs);
            log.LogInformation("음성 인식기를 올렸습니다 ({Ms}ms, {Threads}스레드)", clock.ElapsedMilliseconds, settings.Current.SttThreads);
            return _engine = engine;
        }
    }

    /// <summary>고르지 않은 쪽 엔진의 모델을 메모리에서 내린다 (Qwen3는 약 1GB, 게임을 하는 PC라 아껴 둔다). 호출하는 쪽이 _lock을 쥔다.</summary>
    private void ReleaseUnused(bool zip)
    {
        var now = Environment.TickCount64;
        if (zip && _engine != null && CanRelease(_zipHandedAt, now)) { _engine.Dispose(); _engine = null; log.LogInformation("쓰지 않는 기본 인식기를 메모리에서 내렸습니다"); }
        if (!zip && _qwen != null && CanRelease(_qwenHandedAt, now)) { _qwen.Dispose(); _qwen = null; log.LogInformation("쓰지 않는 Qwen3 인식기를 메모리에서 내렸습니다"); }
    }

    public Task WarmUpAsync() => !IsInstalled ? Task.CompletedTask : Task.Run(() =>
    {
        try { GetEngine(); }
        catch (Exception ex) { log.LogWarning(ex, "음성 인식기를 미리 올리지 못했습니다"); }
    });

    public void Dispose()
    {
        lock (_lock) { _engine?.Dispose(); _engine = null; _qwen?.Dispose(); _qwen = null; }
    }
}

/// <summary>
/// 핫워드 파일 만들기 (실험, 기본 끔). 모델 글자표(tokens.txt)에 없는 음절이 든 낱말은 뺀다 — 모르는 글자가 있으면 인식기가 파일을 읽지 못할 수 있다.
/// 형식은 음절 사이를 띄운 한 줄 한 낱말("힐 러").
/// </summary>
public static class HotwordFile
{
    public const int MaxWords = 30;

    /// <summary>글자표에 있는 음절로만 된 낱말을 "힐 러" 형식으로</summary>
    public static IReadOnlyList<string> BuildLines(IEnumerable<string> words, ISet<string> tokens, int max = MaxWords) =>
        words.Where(w => w.Length > 0 && w.All(ch => tokens.Contains(ch.ToString())))
            .Take(max)
            .Select(w => string.Join(" ", w.Select(ch => ch.ToString())))
            .ToList();

    /// <summary>파일을 쓰고 경로를 돌려준다. 쓸 낱말이 없거나 실패하면 null(핫워드 없이 간다).</summary>
    public static string? Write(SttModelLocator model, VoiceVocabulary vocab, ILogger log)
    {
        try
        {
            var tokens = File.ReadLines(model.PathOf(model.Files[3].Name))
                .Select(l => l.Split(' ', 2)[0]).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
            var lines = BuildLines(vocab.HotwordCandidates(MaxWords * 2), tokens);
            if (lines.Count == 0) return null;
            var path = Path.Combine(Path.GetDirectoryName(model.Dir.TrimEnd('\\', '/')) ?? model.Dir, "hotwords.txt");
            File.WriteAllLines(path, lines);
            log.LogInformation("핫워드 {Count}개를 넣습니다", lines.Count);
            return path;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "핫워드 파일을 만들지 못했습니다. 핫워드 없이 시작합니다");
            return null;
        }
    }
}
