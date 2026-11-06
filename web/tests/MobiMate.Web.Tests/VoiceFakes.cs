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

    /// <summary>마이크를 여는 도중에 실행된다(그 사이에 중지가 오는 경우를 흉내 낸다)</summary>
    public Action? OnOpen { get; set; }

    /// <summary>캡처를 시작하는 순간에 실행된다</summary>
    public Action? OnCaptureStart { get; set; }

    public IReadOnlyList<AudioDeviceInfo> Devices() => DeviceList;

    public IAudioCapture Open(string? deviceId)
    {
        LastRequestedId = deviceId;
        OpenCount++;
        OnOpen?.Invoke();
        if (OpenFailure != null) throw new AudioException(OpenFailure);
        return Current = new FakeCapture { OnStart = OnCaptureStart };
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
    public Action? OnStart { get; set; }

    public void Start()
    {
        Started = true;
        OnStart?.Invoke();
    }

    public void Push(float[] block) => Samples?.Invoke(block);

    /// <summary>소리가 있는 100ms 블록 (정확히 0이 아니어서 "신호 없음" 경고가 나오지 않는다)</summary>
    public void PushVoice(int blocks = 1)
    {
        for (var i = 0; i < blocks; i++) Push(Enumerable.Repeat(0.1f, 1600).ToArray());
    }

    /// <summary>정확히 0인 100ms 블록 (마이크 접근이 막힌 것처럼)</summary>
    public void PushSilence(int blocks = 1)
    {
        for (var i = 0; i < blocks; i++) Push(new float[1600]);
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

    /// <summary>엔진을 가져오는 도중에 실행된다(시작 중에 중지가 오는 경우)</summary>
    public Action? OnGetEngine { get; set; }

    /// <summary>엔진을 만들 때 실패한다(모델 파일 손상 등)</summary>
    public bool ThrowOnGetEngine { get; set; }

    /// <summary>소리를 받아 인식하는 중에 실패한다</summary>
    public bool ThrowOnAccept { get; set; }

    public bool IsInstalled => Installed;

    public Task WarmUpAsync() => Task.CompletedTask;

    public ISttEngine GetEngine()
    {
        OnGetEngine?.Invoke();
        if (ThrowOnGetEngine) throw new InvalidOperationException("가짜 엔진 생성 실패");
        return new Engine(this);
    }

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

        public void Accept(float[] samples)
        {
            if (cfg.ThrowOnAccept) throw new InvalidOperationException("가짜 인식 실패");
            _count += samples.Length;
        }

        public SttPartial Poll() =>
            _index >= cfg.Utterances.Length ? new("", false) : new(Prefix(), _count >= cfg.SamplesPerUtterance + cfg.TrailingSamples);

        public void Reset() { _index++; _count = 0; }

        public string Finish() => Prefix();

        public void Dispose() { }
    }
}

/// <summary>가짜 전역 단축키: 등록 결과를 정하고 <see cref="Fire"/>로 누름을 흉내 낸다</summary>
public sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
{
    public event Action? Pressed;
    public HotkeySpec? Current { get; private set; }
    public string? FailWith { get; set; }
    public int RegisterCount { get; private set; }

    /// <summary>등록하는 도중에 실행된다(그 사이 단축키가 눌리는 경우를 흉내 낸다)</summary>
    public Action? OnRegister { get; set; }

    public bool Register(HotkeySpec spec, out string? error)
    {
        RegisterCount++;
        OnRegister?.Invoke();
        if (FailWith != null) { error = FailWith; return false; }
        Current = spec;
        error = null;
        return true;
    }

    public void Unregister() => Current = null;

    public void Fire() => Pressed?.Invoke();

    public void Dispose() { }
}

public sealed class FakeKeyState : IKeyState
{
    private readonly HashSet<uint> _down = new();
    public void Down(uint vk) { lock (_down) _down.Add(vk); }
    public void Up(uint vk) { lock (_down) _down.Remove(vk); }
    public bool IsDown(uint virtualKey) { lock (_down) return _down.Contains(virtualKey); }
}

/// <summary>재생한 신호음을 기록한다. <see cref="Probe"/>가 있으면 재생하는 순간의 값도 남긴다(예: 그때 마이크가 닫혀 있었는지)</summary>
public sealed class RecordingCue : ISttCue
{
    private readonly List<SttCueKind> _played = new();
    private readonly List<bool> _probes = new();
    public Func<bool>? Probe { get; set; }
    public IReadOnlyList<SttCueKind> Played { get { lock (_played) return _played.ToList(); } }
    public IReadOnlyList<bool> ProbeValues { get { lock (_played) return _probes.ToList(); } }
    public void Play(SttCueKind kind)
    {
        var probe = Probe?.Invoke() ?? false;
        lock (_played) { _played.Add(kind); _probes.Add(probe); }
    }
}

/// <summary>가짜 AI 다듬기: 기본은 꺼짐. 시험이 Reply를 정하면 그대로 돌려주고, DelayMs만큼 기다린다.</summary>
public sealed class FakeRefiner : IVoiceRefiner
{
    public bool Enabled { get; set; }
    public Func<string, string?> Reply { get; set; } = _ => null;
    public int DelayMs { get; set; }
    public List<string> Inputs { get; } = new();
    public int WarmUps;
    public RefinerStatus Status => new(Enabled, "fake", false, null, null, false);

    public async Task<string?> RefineAsync(string text, CancellationToken ct)
    {
        lock (Inputs) Inputs.Add(text);
        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
        return RefineGuard.Accept(text, Reply(text));
    }

    public Task WarmUpAsync() { Interlocked.Increment(ref WarmUps); return Task.CompletedTask; }
}

/// <summary>가짜 채팅 꾸미기: 시험이 Reply로 결과를 정한다. 기본은 아무것도 하지 않는다.</summary>
public sealed class FakeStylist : MobiMate.Web.Services.IChatStylist
{
    public Func<string, MobiMate.Web.Services.ChatStyleRequest, MobiMate.Web.Services.ChatStyleResult> Reply { get; set; } = (_, _) => MobiMate.Web.Services.ChatStyleResult.Nothing;
    public bool Throw { get; set; }
    public List<(string Text, MobiMate.Web.Services.ChatStyleRequest Req)> Calls { get; } = new();

    public Task<MobiMate.Web.Services.ChatStyleResult> StyleAsync(string text, MobiMate.Web.Services.ChatStyleRequest req, CancellationToken ct)
    {
        lock (Calls) Calls.Add((text, req));
        if (Throw) throw new InvalidOperationException("가짜 오류");
        return Task.FromResult(Reply(text, req));
    }
}
