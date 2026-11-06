using System.Media;

namespace MobiMate.Web.Voice;

public enum SttCueKind { Start, Done }

/// <summary>
/// 듣기 시작·문장 확정 신호음 (FR-VC-02). 게임이 전체 화면이면 화면 표시를 볼 수 없어서 소리로 알린다.
/// 시작음은 마이크를 열기 *전에* 끝까지 재생해 인식에 섞이지 않게 한다.
/// </summary>
public interface ISttCue
{
    void Play(SttCueKind kind);
}

public sealed class NoCue : ISttCue
{
    public void Play(SttCueKind kind) { }
}

/// <summary>짧은 사인파 소리를 메모리에서 만들어 시스템 기본 출력으로 재생한다. 실패해도 음성 입력은 계속된다.</summary>
public sealed class SoundCue : ISttCue
{
    private readonly Lazy<byte[]> _start = new(() => Tone(880, 90));    // 높은 짧은 소리: 말하세요
    private readonly Lazy<byte[]> _done = new(() => Tone(587, 70));     // 낮은 소리: 받았습니다

    public void Play(SttCueKind kind)
    {
        try
        {
            using var ms = new MemoryStream(kind == SttCueKind.Start ? _start.Value : _done.Value);
            using var player = new SoundPlayer(ms);
            player.PlaySync();
        }
        catch { /* 소리가 안 나도 기능은 동작한다 */ }
    }

    /// <summary>16비트 모노 44.1kHz 사인파 WAV. 앞뒤 5ms를 부드럽게 줄여 딱 소리를 막는다.</summary>
    internal static byte[] Tone(double hz, int millis)
    {
        const int rate = 44100;
        var samples = rate * millis / 1000;
        var ramp = rate * 5 / 1000;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + samples * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            var env = Math.Min(1.0, Math.Min(i, samples - 1 - i) / (double)ramp);
            w.Write((short)(Math.Sin(2 * Math.PI * hz * i / rate) * 0.35 * 32767 * env));
        }
        return ms.ToArray();
    }
}
