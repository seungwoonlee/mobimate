using MobiMate.Web.Voice;
using NAudio.Wave;

namespace MobiMate.Web.Tests;

/// <summary>마이크 형식 변환·이득 보정 (FR-VC-05). 마이크 없이 합성 음원으로 시험한다.</summary>
public class AudioConverterTests
{
    private const double Hz = 440;

    /// <summary>진폭 0.5의 사인파를 장치 형식(PCM 16/24비트 또는 float)의 바이트로 만든다</summary>
    private static byte[] Sine(WaveFormat f, double seconds)
    {
        var frames = (int)(f.SampleRate * seconds);
        var bytesPerSample = f.BitsPerSample / 8;
        var data = new byte[frames * f.Channels * bytesPerSample];
        var at = 0;
        for (var i = 0; i < frames; i++)
        {
            var v = 0.5 * Math.Sin(2 * Math.PI * Hz * i / f.SampleRate);
            for (var c = 0; c < f.Channels; c++)
            {
                switch (f.Encoding, f.BitsPerSample)
                {
                    case (WaveFormatEncoding.IeeeFloat, 32):
                        BitConverter.TryWriteBytes(data.AsSpan(at), (float)v);
                        break;
                    case (WaveFormatEncoding.Pcm, 16):
                        BitConverter.TryWriteBytes(data.AsSpan(at), (short)(v * 32767));
                        break;
                    case (WaveFormatEncoding.Pcm, 24):
                        var s = (int)(v * 8388607);
                        data[at] = (byte)s; data[at + 1] = (byte)(s >> 8); data[at + 2] = (byte)(s >> 16);
                        break;
                    default:
                        throw new NotSupportedException();
                }
                at += bytesPerSample;
            }
        }
        return data;
    }

    private static float[] Convert(WaveFormat f, double seconds)
    {
        var conv = new AudioConverter(f);
        var data = Sine(f, seconds);
        var chunk = f.AverageBytesPerSecond / 10 / f.BlockAlign * f.BlockAlign;   // 100ms
        var all = new List<float>();
        for (var i = 0; i < data.Length; i += chunk)
        {
            var n = Math.Min(chunk, data.Length - i);
            all.AddRange(conv.Push(data.AsSpan(i, n).ToArray(), n));
        }
        return all.ToArray();
    }

    public static IEnumerable<object[]> Formats() => new[]
    {
        new object[] { 16000, 1, 32, true },     // 이미 목표 형식
        new object[] { 44100, 2, 16, false },    // CD 형식
        new object[] { 48000, 2, 32, true },     // Yeti X·Kraken의 공유 모드 형식 (P0 실측)
        new object[] { 8000, 1, 16, false },     // 좁은 대역 (블루투스 통화 모드)
        new object[] { 48000, 6, 32, true },     // 다채널 장치
        new object[] { 22050, 1, 24, false },    // 24비트
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void Convert_AnyFormat_To16kMono_KeepsLengthLevelAndPitch(int rate, int channels, int bits, bool isFloat)
    {
        var f = isFloat ? WaveFormat.CreateIeeeFloatWaveFormat(rate, channels) : new WaveFormat(rate, bits, channels);
        var seconds = 1.0;
        var o = Convert(f, seconds);

        // 길이: 리샘플러가 끝을 조금 남길 수 있어 3% 안
        Assert.InRange(o.Length, (int)(16000 * seconds * 0.97), (int)(16000 * seconds * 1.03));

        // 가운데 0.2~0.8초 구간 (앞뒤 과도 응답 제외)
        var mid = o.Skip(3200).Take(9600).ToArray();
        var rms = Math.Sqrt(mid.Sum(v => (double)v * v) / mid.Length);
        Assert.InRange(rms, 0.5 / Math.Sqrt(2) * 0.85, 0.5 / Math.Sqrt(2) * 1.15);   // 진폭 0.5 사인의 RMS ≈ 0.354

        var crossings = 0;
        for (var i = 1; i < mid.Length; i++) if (mid[i - 1] < 0 != mid[i] < 0) crossings++;
        var expected = Hz * 2 * (mid.Length / 16000.0);
        Assert.InRange(crossings, expected * 0.9, expected * 1.1);   // 음높이가 그대로
    }

    [Fact]
    public void Convert_SmallChunks_AccumulateAcrossCalls()
    {
        // 장치가 아주 작은 조각으로 줄 때도 샘플이 사라지지 않는다
        var f = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var conv = new AudioConverter(f);
        var data = Sine(f, 0.5);
        var total = 0;
        for (var i = 0; i < data.Length; i += 960 * 8) total += conv.Push(data.AsSpan(i, Math.Min(960 * 8, data.Length - i)).ToArray(), Math.Min(960 * 8, data.Length - i)).Length;
        Assert.InRange(total, 8000 * 0.95, 8000 * 1.05);
    }
}

public class AutoGainTests
{
    private static float[] Block(float amp) => Enumerable.Repeat(amp, 1600).ToArray();

    /// <summary>잡음만 있던 마이크에 목소리가 들어온 상황 (잡음 50블록 + 목소리 60블록)</summary>
    private static AutoGain NoiseThenQuietVoice(float noise = 0.002f, float voice = 0.05f)
    {
        var g = new AutoGain();
        for (var i = 0; i < 50; i++) g.Apply(Block(noise));
        for (var i = 0; i < 60; i++) g.Apply(Block(voice));
        return g;
    }

    [Fact]
    public void QuietVoice_AfterRoomNoise_IsRaisedTowardTargetPeak()
    {
        var g = NoiseThenQuietVoice();
        Assert.InRange(g.Gain, 6f, AutoGain.MaxGain + 0.01f);
        var b = Block(0.05f);
        g.Apply(b);
        Assert.InRange(b.Max(), 0.3f, 0.5f);
    }

    [Fact]
    public void RoomNoise_IsNeverAmplified()
    {
        // 방 잡음을 키우면 인식 모델이 없는 말을 지어낼 수 있다 (리뷰 지적)
        var g = new AutoGain();
        for (var i = 0; i < 200; i++) g.Apply(Block(0.002f));
        Assert.Equal(1f, g.Gain, 3);
    }

    [Fact]
    public void SteadyLevelWithoutQuietReference_IsTreatedAsNoise_AndNotAmplified()
    {
        // 계속 일정한 소리(선풍기 등)는 목소리가 아니라 잡음 바닥으로 본다
        var g = new AutoGain();
        for (var i = 0; i < 150; i++) g.Apply(Block(0.05f));
        Assert.Equal(1f, g.Gain, 2);
    }

    [Fact]
    public void LoudVoice_StaysUnchanged()
    {
        var g = new AutoGain();
        for (var i = 0; i < 50; i++) g.Apply(Block(0.002f));
        var b = Block(0.9f);
        g.Apply(b);
        Assert.All(b, v => Assert.Equal(0.9f, v, 3));
    }

    [Fact]
    public void SuddenLoudSound_DropsGainAtOnce_AndNeverClips()
    {
        var g = NoiseThenQuietVoice();
        Assert.True(g.Gain > 6f);
        var loud = Block(0.8f);
        g.Apply(loud);
        Assert.Equal(1f, g.Gain, 3);
        Assert.All(loud, v => Assert.InRange(v, -1f, 1f));
    }

    [Fact]
    public void WhenVoiceStops_GainReturnsToOne()
    {
        var g = NoiseThenQuietVoice();
        Assert.True(g.Gain > 6f);
        for (var i = 0; i < 200; i++) g.Apply(Block(0.002f));
        Assert.InRange(g.Gain, 0.99f, 1.05f);
    }

    [Fact]
    public void Level_IsMeasuredBeforeGain()
    {
        var g = NoiseThenQuietVoice();
        var level = g.Apply(Block(0.05f));
        Assert.Equal(0.05f, level.Peak, 3);
        Assert.InRange(level.PeakDb, -27, -25);
    }
}
