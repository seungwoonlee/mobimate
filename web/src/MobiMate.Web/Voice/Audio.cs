using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MobiMate.Web.Voice;

public sealed record AudioDeviceInfo(string Id, string Name, int SampleRate, int Channels, bool IsDefault);

public enum AudioFailureKind { NoDevice, Denied, InUse, Removed, Other }

public sealed record AudioFailure(AudioFailureKind Kind, string Message);

/// <summary>마이크를 열지 못했을 때 (FR-VC-12)</summary>
public sealed class AudioException(AudioFailure failure) : Exception(failure.Message)
{
    public AudioFailure Failure { get; } = failure;
}

/// <summary>열린 마이크. 16kHz 모노 샘플을 캡처 스레드에서 <see cref="Samples"/>로 흘린다(핸들러는 오래 걸리면 안 된다).</summary>
public interface IAudioCapture : IDisposable
{
    event Action<float[]> Samples;
    event Action<AudioFailure> Failed;
    AudioDeviceInfo Device { get; }

    /// <summary>요청한 장치를 찾지 못해 시스템 기본 마이크로 열었다</summary>
    bool UsedDefaultFallback { get; }

    /// <summary>소리 받기를 시작한다. 구독을 마친 뒤에 부른다(그래야 첫 소리를 놓치지 않는다). 실패하면 <see cref="AudioException"/>.</summary>
    void Start();
}

/// <summary>마이크 장치 목록과 열기. 테스트에서는 가짜로 바꾼다.</summary>
public interface IAudioSource
{
    IReadOnlyList<AudioDeviceInfo> Devices();

    /// <summary>
    /// 장치를 연다(아직 받기 시작하지 않는다). <paramref name="deviceId"/>가 없거나 못 찾으면 시스템 기본 마이크.
    /// 실패하면 <see cref="AudioException"/>.
    /// </summary>
    IAudioCapture Open(string? deviceId);
}

/// <summary>
/// 실제 마이크 (FR-VC-01): WASAPI 공유 모드. 디스코드·게임이 같은 마이크를 써도 충돌하지 않는다.
/// 장치는 고유 ID로 찾는다(이름·순서는 바뀐다).
/// </summary>
public sealed class WasapiAudioSource : IAudioSource
{
    private const int ElementNotFound = unchecked((int)0x80070490);
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int DeviceInUse = unchecked((int)0x8889000A);

    public IReadOnlyList<AudioDeviceInfo> Devices()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            var defaultId = TryDefault(en)?.ID;
            var list = new List<AudioDeviceInfo>();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (d) list.Add(Describe(d, d.ID == defaultId));
            }
            return list;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return Array.Empty<AudioDeviceInfo>();
        }
    }

    public IAudioCapture Open(string? deviceId)
    {
        try
        {
            var en = new MMDeviceEnumerator();
            MMDevice? dev = null;
            var fallback = false;
            if (!string.IsNullOrEmpty(deviceId))
            {
                try
                {
                    var d = en.GetDevice(deviceId);
                    if (d.State == DeviceState.Active) dev = d;
                }
                catch (Exception ex) when (ex is COMException or ArgumentException) { }
                fallback = dev == null;
            }
            dev ??= TryDefault(en) ?? throw new AudioException(new AudioFailure(AudioFailureKind.NoDevice, "사용할 수 있는 마이크가 없습니다."));
            return new WasapiCaptureSession(en, dev, fallback);
        }
        catch (AudioException) { throw; }
        catch (Exception ex) { throw new AudioException(Map(ex)); }
    }

    private static MMDevice? TryDefault(MMDeviceEnumerator en)
    {
        try { return en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console); }
        catch (COMException) { return null; }
    }

    private static AudioDeviceInfo Describe(MMDevice d, bool isDefault)
    {
        var rate = 0; var ch = 0;
        try { var f = d.AudioClient.MixFormat; rate = f.SampleRate; ch = f.Channels; } catch { }
        return new AudioDeviceInfo(d.ID, d.FriendlyName, rate, ch, isDefault);
    }

    internal static AudioFailure Map(Exception ex) => ex switch
    {
        UnauthorizedAccessException => new(AudioFailureKind.Denied, "Windows 개인정보 설정에서 마이크 접근이 막혀 있습니다."),
        COMException c when c.HResult == AccessDenied => new(AudioFailureKind.Denied, "Windows 개인정보 설정에서 마이크 접근이 막혀 있습니다."),
        COMException c when c.HResult == DeviceInUse => new(AudioFailureKind.InUse, "다른 프로그램이 마이크를 독점해서 열 수 없습니다."),
        COMException c when c.HResult == ElementNotFound => new(AudioFailureKind.NoDevice, "사용할 수 있는 마이크가 없습니다."),
        COMException => new(AudioFailureKind.Removed, "마이크 연결이 끊겼습니다."),
        _ => new(AudioFailureKind.Other, "마이크를 열지 못했습니다: " + ex.Message),
    };

    private sealed class WasapiCaptureSession : IAudioCapture
    {
        private readonly MMDeviceEnumerator _enumerator;
        private readonly MMDevice _device;
        private readonly WasapiCapture _capture;
        private readonly AudioConverter _converter;
        private bool _disposed;

        public event Action<float[]>? SamplesRaised;
        public event Action<AudioFailure>? FailedRaised;
        event Action<float[]> IAudioCapture.Samples { add => SamplesRaised += value; remove => SamplesRaised -= value; }
        event Action<AudioFailure> IAudioCapture.Failed { add => FailedRaised += value; remove => FailedRaised -= value; }

        public AudioDeviceInfo Device { get; }
        public bool UsedDefaultFallback { get; }

        public WasapiCaptureSession(MMDeviceEnumerator enumerator, MMDevice device, bool fallback)
        {
            _enumerator = enumerator;
            _device = device;
            UsedDefaultFallback = fallback;
            _capture = new WasapiCapture(device) { ShareMode = AudioClientShareMode.Shared };
            _converter = new AudioConverter(_capture.WaveFormat);
            Device = new AudioDeviceInfo(device.ID, device.FriendlyName, _capture.WaveFormat.SampleRate, _capture.WaveFormat.Channels, false);
            _capture.DataAvailable += (_, e) =>
            {
                if (_disposed || e.BytesRecorded == 0) return;
                var block = _converter.Push(e.Buffer, e.BytesRecorded);
                if (block.Length > 0) SamplesRaised?.Invoke(block);
            };
            _capture.RecordingStopped += (_, e) =>
            {
                if (!_disposed && e.Exception != null) FailedRaised?.Invoke(Map(e.Exception));
            };
        }

        public void Start()
        {
            try { _capture.StartRecording(); }
            catch (Exception ex) { throw new AudioException(Map(ex)); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _device.Dispose();
            _enumerator.Dispose();
        }
    }
}
