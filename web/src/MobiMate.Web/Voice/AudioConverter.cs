using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MobiMate.Web.Voice;

/// <summary>입력 소리 크기 (0~1). 이득 보정 전 값이다.</summary>
public readonly record struct AudioLevel(float Peak, float Rms)
{
    public double PeakDb => 20 * Math.Log10(Math.Max(Peak, 1e-9));
    public double RmsDb => 20 * Math.Log10(Math.Max(Rms, 1e-9));
}

/// <summary>
/// 마이크 장치 형식(샘플레이트·채널·비트 수 무엇이든) → 16kHz 모노 float (FR-VC-05 ③).
/// 스테레오는 평균, 3채널 이상도 평균, 리샘플은 NAudio Wdl. 장치마다 형식이 달라서 16kHz를 가정하지 않는다.
/// </summary>
public sealed class AudioConverter
{
    public const int TargetRate = 16000;

    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _output;
    private readonly float[] _scratch = new float[8000];

    public WaveFormat Input { get; }

    public AudioConverter(WaveFormat input)
    {
        Input = input;
        _buffer = new BufferedWaveProvider(input)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(10),
            ReadFully = false,
        };
        ISampleProvider sp = _buffer.ToSampleProvider();
        if (sp.WaveFormat.Channels == 2) sp = new StereoToMonoSampleProvider(sp) { LeftVolume = 0.5f, RightVolume = 0.5f };
        else if (sp.WaveFormat.Channels > 2) sp = new AverageDownmix(sp);
        if (sp.WaveFormat.SampleRate != TargetRate) sp = new WdlResamplingSampleProvider(sp, TargetRate);
        _output = sp;
    }

    /// <summary>장치에서 받은 바이트를 넣고, 변환된 16kHz 모노 샘플을 돌려준다(없으면 빈 배열).</summary>
    public float[] Push(byte[] data, int count)
    {
        _buffer.AddSamples(data, 0, count);
        List<float[]>? parts = null;
        var total = 0;
        int n;
        while ((n = _output.Read(_scratch, 0, _scratch.Length)) > 0)
        {
            (parts ??= new()).Add(_scratch.AsSpan(0, n).ToArray());
            total += n;
        }
        if (parts == null) return Array.Empty<float>();
        if (parts.Count == 1) return parts[0];
        var all = new float[total];
        var at = 0;
        foreach (var p in parts) { p.CopyTo(all, at); at += p.Length; }
        return all;
    }

    /// <summary>3채널 이상을 평균으로 모노화</summary>
    private sealed class AverageDownmix(ISampleProvider source) : ISampleProvider
    {
        private float[] _tmp = Array.Empty<float>();
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var ch = source.WaveFormat.Channels;
            if (_tmp.Length < count * ch) _tmp = new float[count * ch];
            var frames = source.Read(_tmp, 0, count * ch) / ch;
            for (var i = 0; i < frames; i++)
            {
                float sum = 0;
                for (var c = 0; c < ch; c++) sum += _tmp[i * ch + c];
                buffer[offset + i] = sum / ch;
            }
            return frames;
        }
    }
}

/// <summary>
/// 자동 이득 보정 (FR-VC-05 ④). 입력이 작은 마이크(노트북 내장 등)의 **목소리**를 끌어올린다.
/// - 잡음 바닥: 최근 10초 블록 소리 크기의 하위 20% 값. 이 바닥보다 약 9.5dB 이상 크고 절대 크기도 -48dBFS 이상이어야 목소리로 본다.
///   (그래서 일정한 방 잡음·선풍기 소리는 키우지 않는다. 방 잡음을 키우면 모델이 없는 말을 지어낼 수 있다.)
/// - 목소리 블록에서만 피크 포락선을 따라가고 목표 피크(0.5)에 맞춘다. 최대 8배(18dB).
/// - 커지는 소리에는 즉시 낮추고, 목소리가 없으면 이득을 서서히 1로 되돌린다.
/// </summary>
public sealed class AutoGain
{
    public const float TargetPeak = 0.5f;
    public const float MaxGain = 8f;
    public const float MinVoiceRms = 0.004f;
    private const float VoiceOverNoise = 3f;
    private const float EnvelopeRelease = 0.97f;
    private const float GainReturn = 0.96f;   // 목소리가 없으면 블록마다 4%씩 1로 (말 사이 쉬는 동안 잡음이 커지지 않게)
    private const int NoiseWindow = 100;    // 블록 100개 ≈ 10초 (블록은 100ms 안팎)
    private const float NoisePercentile = 0.2f;

    private readonly float[] _recent = new float[NoiseWindow];
    private int _count;
    private int _next;
    private float _envelope;
    private float _gain = 1f;

    public float Gain => _gain;

    /// <summary>블록을 제자리에서 보정하고, 보정 전 소리 크기를 돌려준다.</summary>
    public AudioLevel Apply(float[] samples)
    {
        if (samples.Length == 0) return default;
        float peak = 0, sum = 0;
        foreach (var v in samples)
        {
            var a = Math.Abs(v);
            if (a > peak) peak = a;
            sum += v * v;
        }
        var rms = MathF.Sqrt(sum / samples.Length);
        var level = new AudioLevel(peak, rms);

        _recent[_next] = rms;
        _next = (_next + 1) % NoiseWindow;
        if (_count < NoiseWindow) _count++;
        var noise = NoiseFloor();

        var voice = rms > Math.Max(noise * VoiceOverNoise, MinVoiceRms);
        if (voice)
        {
            _envelope = Math.Max(peak, _envelope * EnvelopeRelease);
            var desired = Math.Clamp(TargetPeak / _envelope, 1f, MaxGain);
            _gain = desired < _gain ? desired : _gain + (desired - _gain) * 0.2f;
        }
        else
        {
            _envelope *= EnvelopeRelease;
            _gain = 1f + (_gain - 1f) * GainReturn;
        }
        if (peak * _gain > 1f) _gain = 1f / peak;   // 찌그러지지 않게 즉시 낮춘다

        if (_gain > 1.0001f)
            for (var i = 0; i < samples.Length; i++) samples[i] = Math.Clamp(samples[i] * _gain, -1f, 1f);
        return level;
    }

    private float NoiseFloor()
    {
        var copy = new float[_count];
        Array.Copy(_recent, copy, _count);
        Array.Sort(copy);
        return copy[(int)((_count - 1) * NoisePercentile)];
    }
}
