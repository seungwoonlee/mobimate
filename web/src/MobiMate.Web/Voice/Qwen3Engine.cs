using SherpaOnnx;

namespace MobiMate.Web.Voice;

/// <summary>
/// Qwen3-ASR 0.6B (sherpa-onnx 오프라인 인식기). 스트리밍이 아니라서 말이 끝난 뒤 한 문장을 한 번에 인식한다.
/// 말이 끝났는지는 소리 크기로 정한다(<see cref="UtteranceStream"/>). 띄어쓰기·문장부호가 붙어서 나오고 잡음에 강하다 (실측: 약한 잡음에서 글자 오류 17.5% vs 29.4%).
/// </summary>
public sealed class Qwen3SttEngine : ISttEngine
{
    public const int MaxHotwords = 30;

    private readonly OfflineRecognizer _recognizer;
    private readonly Func<int> _endSilenceMs;

    private readonly Func<float[], string>? _koreanFallback;

    /// <param name="koreanFallback">Qwen3가 한국어가 아닌 글자(중국어)로 받아 적었을 때 같은 소리를 한국어 전용 엔진으로 다시 받아 적는 함수. 없으면 그 문장은 버린다.</param>
    public Qwen3SttEngine(SttModelLocator model, int threads, IReadOnlyList<string> hotwords, Func<int> endSilenceMs, Func<float[], string>? koreanFallback = null)
    {
        _koreanFallback = koreanFallback;
        _endSilenceMs = endSilenceMs;
        var c = new OfflineRecognizerConfig();
        // 구조체라 복사본에 쓰면 반영되지 않는다: 반드시 config 안의 값에 직접 쓴다 (틀리면 인식기가 조용히 비어 만들어진다)
        c.ModelConfig.Qwen3Asr.ConvFrontend = model.PathOf("conv_frontend.onnx");
        c.ModelConfig.Qwen3Asr.Encoder = model.PathOf("encoder.int8.onnx");
        c.ModelConfig.Qwen3Asr.Decoder = model.PathOf("decoder.int8.onnx");
        c.ModelConfig.Qwen3Asr.Tokenizer = model.PathOf("tokenizer");
        if (hotwords.Count > 0) c.ModelConfig.Qwen3Asr.Hotwords = string.Join(",", hotwords.Where(w => w.Length > 0 && !w.Contains(',')).Take(MaxHotwords));
        c.ModelConfig.NumThreads = Math.Clamp(threads, 1, 8);
        c.ModelConfig.Provider = "cpu";
        _recognizer = new OfflineRecognizer(c);
    }

    public ISttStream CreateStream() => new UtteranceStream(Decode, _endSilenceMs);

    private string Decode(float[] samples)
    {
        using var s = _recognizer.CreateStream();
        s.AcceptWaveform(AudioConverter.TargetRate, samples);
        _recognizer.Decode(s);
        var text = Clean(s.Result.Text);
        // Qwen3는 여러 나라말을 아는 모델이라 웃음소리·짧은 감탄("어허허허")은 어느 말인지 가늠하다가 중국어로 받아 적곤 한다(2026-11-15 실측: "哦吼吼吼").
        // 모델에 언어를 고정하는 옵션이 없어서(sherpa-onnx), 한자 위주 결과는 한국어 전용 엔진으로 다시 받아 적고, 그것도 안 되면 버린다.
        if (!LooksChinese(text)) return text;
        try { return _koreanFallback == null ? "" : Clean(_koreanFallback(samples)); }
        catch { return ""; }
    }

    /// <summary>한자(중국어)가 한글보다 많거나 같은 결과인가. 한국어로 말했는데 한자뿐이면 잘못 받아 적은 것이다.</summary>
    internal static bool LooksChinese(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int han = 0, hangul = 0;
        foreach (var ch in text)
        {
            if (ch is >= '一' and <= '鿿' or >= '㐀' and <= '䶿') han++;
            else if (ch is >= '가' and <= '힣' or >= 'ㄱ' and <= 'ㆎ') hangul++;
        }
        return han > 0 && han >= hangul;
    }

    /// <summary>끝의 마침표는 채팅에 어울리지 않아 뗀다("?"·"!"는 둔다)</summary>
    internal static string Clean(string? text)
    {
        var t = (text ?? "").Trim();
        while (t.EndsWith('.') || t.EndsWith('。')) t = t[..^1].TrimEnd();
        return t;
    }

    public void Dispose() => _recognizer.Dispose();
}

/// <summary>
/// 소리 크기로 말의 시작과 끝을 가려 한 문장씩 <paramref name="decode"/>에 넘기는 스트림.
/// 말이 시작된 뒤 <c>endSilenceMs</c> 이상 조용하면 문장 끝. 인식은 <see cref="Poll"/>에서 한다(서비스 작업 스레드).
/// </summary>
public sealed class UtteranceStream(Func<float[], string> decode, Func<int> endSilenceMs) : ISttStream
{
    /// <summary>글자가 없다고 보는 가장 작은 소리 (AutoGain을 거친 뒤 기준)</summary>
    internal const float MinSpeechRms = 0.012f;

    /// <summary>주변 소음의 이 배수를 넘어야 말로 본다</summary>
    internal const float NoiseMargin = 2.5f;

    /// <summary>말 앞뒤로 남겨 두는 여유 (말끝 자음이 잘리지 않게)</summary>
    private const double PadSeconds = 0.3;

    /// <summary>이보다 길면 끊고 인식한다 (모델이 길이에 한계가 있고, 지연도 길어진다)</summary>
    private const double MaxSeconds = 25;

    /// <summary>문장 끝으로 보는 최소 무음 (설정이 이보다 짧아도 이만큼은 기다린다: 이 방식은 한 번 끊으면 되돌릴 수 없다)</summary>
    internal const int MinEndSilenceMs = 500;

    /// <summary>말하는 중에는 말소리 평균 크기의 이 비율을 넘어야 계속 말하는 것으로 본다 (음악·팬 소리가 깔려 있어도 말이 끝나면 문장이 끝난다)</summary>
    internal const float SpeechRelative = 0.2f;

    /// <summary>말이 이만큼 이어진 뒤부터 말소리 크기 대비 판정을 쓴다 (말 첫머리의 큰 소리 하나로 기준이 쏠리지 않게)</summary>
    private const double RelativeAfterSeconds = 0.6;

    private readonly List<float> _buf = new();
    private float _floor = -1;          // 주변 소음 추정 (rms)
    private float _speechLevel;         // 말소리 평균 크기 (rms, 이동 평균)
    private bool _speech;               // 말이 시작됨
    private int _lastVoice;             // 마지막으로 소리가 있던 샘플 위치 (_buf 기준)
    private int _firstVoice;            // 처음 소리가 있던 샘플 위치
    private bool _ended;

    public void Accept(float[] samples)
    {
        if (samples.Length == 0) return;
        var rms = Rms(samples);
        if (_floor < 0) _floor = Math.Min(rms, MinSpeechRms);   // 첫 블록부터 말이어도 소음으로 잡지 않는다
        var threshold = Math.Max(MinSpeechRms, _floor * NoiseMargin);
        if (_speech && _lastVoice - _firstVoice >= RelativeAfterSeconds * AudioConverter.TargetRate) threshold = Math.Max(threshold, _speechLevel * SpeechRelative);
        var voiced = rms > threshold;
        if (voiced) _speechLevel = _speechLevel <= 0 ? rms : _speechLevel * 0.9f + rms * 0.1f;
        if (!voiced) _floor = _floor * 0.9f + rms * 0.1f;   // 소음 추정은 조용할 때만 갱신한다
        else if (!_speech) _floor *= 1.02f;                 // 말이 시작되기 전에 계속 시끄러우면 그건 소음이다: 천천히 받아들인다

        var start = _buf.Count;
        _buf.AddRange(samples);
        if (voiced)
        {
            if (!_speech) { _speech = true; _firstVoice = start; }
            _lastVoice = start + samples.Length;
        }
        else if (!_speech)
        {
            // 아직 말하기 전: 앞쪽 소음은 쌓지 않는다 (여유분만 남긴다)
            var keep = (int)(PadSeconds * AudioConverter.TargetRate);
            if (_buf.Count > keep) { _buf.RemoveRange(0, _buf.Count - keep); }
            _lastVoice = 0;
        }

        if (_speech && !_ended)
        {
            var silent = _buf.Count - _lastVoice;
            var need = (int)(Math.Max(endSilenceMs(), MinEndSilenceMs) / 1000.0 * AudioConverter.TargetRate);
            if (silent >= need || _buf.Count >= MaxSeconds * AudioConverter.TargetRate) _ended = true;
        }
    }

    public SttPartial Poll()
    {
        if (!_ended) return new("", false);
        return new(Decode(), true);
    }

    public void Reset()
    {
        _buf.Clear();
        _speech = false;
        _ended = false;
        _speechLevel = 0;
        _lastVoice = _firstVoice = 0;
    }

    public string Finish() => _speech ? Decode() : "";

    public void Dispose() { }

    /// <summary>쌓인 소리를 여유만 남기고 잘라 인식한다. 인식하고 나면 비운다.</summary>
    private string Decode()
    {
        var pad = (int)(PadSeconds * AudioConverter.TargetRate);
        var voiced = _lastVoice - _firstVoice;
        var from = Math.Max(0, _firstVoice - pad);
        var to = Math.Min(_buf.Count, _lastVoice + pad);
        var seg = to > from ? _buf.GetRange(from, to - from).ToArray() : Array.Empty<float>();
        _buf.Clear();
        _speech = false;
        _ended = false;
        _speechLevel = 0;
        _lastVoice = _firstVoice = 0;
        if (voiced < AudioConverter.TargetRate / 5) return "";   // 소리가 0.2초도 안 되면 책상 치는 소리 같은 것이다
        return decode(seg);
    }

    private static float Rms(float[] s)
    {
        double sum = 0;
        foreach (var v in s) sum += v * v;
        return (float)Math.Sqrt(sum / s.Length);
    }
}
