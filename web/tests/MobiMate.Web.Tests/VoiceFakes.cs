using MobiMate.Web.Voice;

namespace MobiMate.Web.Tests;

/// <summary>마이크 없이 음성 입력 흐름을 시험하기 위한 가짜 오디오 소스 (FR-VC 테스트 ②)</summary>
public sealed class FakeAudioSource : IAudioSource
{
    public List<AudioDeviceInfo> DeviceList { get; } = new() { new("fake-1", "가짜 마이크", 48000, 2, true) };
    public AudioFailure? OpenFailure { get; set; }
    public FakeCapture? Current { get; private set; }
    public string? LastRequestedId { get; private set; }
    public int OpenCount { get; private set; }

    public IReadOnlyList<AudioDeviceInfo> Devices() => DeviceList;

    public IAudioCapture Open(string? deviceId)
    {
        LastRequestedId = deviceId;
        OpenCount++;
        if (OpenFailure != null) throw new AudioException(OpenFailure);
        return Current = new FakeCapture();
    }
}

public sealed class FakeCapture : IAudioCapture
{
    public event Action<float[]>? Samples;
    public event Action<AudioFailure>? Failed;
    public AudioDeviceInfo Device { get; } = new("fake-1", "가짜 마이크", 16000, 1, true);
    public bool UsedDefaultFallback => false;
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }

    public void Start() => Started = true;

    public void Push(float[] block) => Samples?.Invoke(block);

    /// <summary>소리가 있는 100ms 블록 (정확히 0이 아니어서 "신호 없음" 경고가 나오지 않는다)</summary>
    public void PushVoice(int blocks = 1)
    {
        for (var i = 0; i < blocks; i++) Push(Enumerable.Repeat(0.1f, 1600).ToArray());
    }

    public void Fail(AudioFailure f) => Failed?.Invoke(f);

    public void Dispose() => Disposed = true;
}

/// <summary>
/// 가짜 인식 엔진: 정해 둔 문장을 들어온 샘플 수에 비례해 한 글자씩 드러낸다.
/// 문장이 끝나고 조금 더 소리가 들어오면(<see cref="TrailingSamples"/>) 끝점(문장 끝)으로 본다.
/// </summary>
public sealed class FakeEngineProvider : ISttEngineProvider
{
    public bool Installed { get; set; } = true;
    public string[] Utterances { get; set; } = { "힐러한분만더구합니다" };
    public int SamplesPerUtterance { get; set; } = 3200;
    public int TrailingSamples { get; set; } = 1600;

    public bool IsInstalled => Installed;

    public ISttEngine GetEngine() => new Engine(this);

    private sealed class Engine(FakeEngineProvider cfg) : ISttEngine
    {
        public ISttStream CreateStream() => new Stream(cfg);
        public void Dispose() { }
    }

    private sealed class Stream(FakeEngineProvider cfg) : ISttStream
    {
        private int _index;
        private int _count;

        private string Prefix()
        {
            if (_index >= cfg.Utterances.Length || _count == 0) return "";
            var u = cfg.Utterances[_index];
            var n = (int)Math.Ceiling(u.Length * Math.Min(1.0, (double)_count / cfg.SamplesPerUtterance));
            return u[..n];
        }

        public void Accept(float[] samples) => _count += samples.Length;

        public SttPartial Poll() =>
            _index >= cfg.Utterances.Length ? new("", false) : new(Prefix(), _count >= cfg.SamplesPerUtterance + cfg.TrailingSamples);

        public void Reset() { _index++; _count = 0; }

        public string Finish() => Prefix();

        public void Dispose() { }
    }
}
