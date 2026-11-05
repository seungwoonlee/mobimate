using System.Collections.Concurrent;
using System.Diagnostics;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Services;

namespace MobiMate.Web.Voice;

public enum SttState { Idle, Starting, Listening, Finishing }

public enum SttStartStatus { Started, Busy, ModelMissing, MicFailed, EngineFailed }

public sealed record SttStartResult(SttStartStatus Status, string? SessionId = null, AudioFailure? Failure = null, string? Message = null);

/// <summary>확정된 문장. <see cref="Original"/>은 후처리 전 인식 글자(학습 비교용)이다.</summary>
public sealed record SttFinal(string SessionId, string Text, string Original, IReadOnlyList<string> Applied, DateTimeOffset At);

public sealed record SttSnapshot(SttState State, string? SessionId, string? ByDevice, string? DeviceName);

/// <summary>
/// 음성 입력 서비스 (FR-VC, 상세설계 §3.9). 한 번에 한 세션:
/// Idle → Starting(모델·마이크 열기) → Listening(100ms 단위로 변환·인식, 글자가 바뀔 때 stt.partial) → Finishing(남은 글자 확정) → Idle.
/// 인식은 낮은 우선순위 전용 스레드에서 돌려 게임에 영향을 주지 않는다 (FR-VC-06).
/// </summary>
public sealed class SttService(
    IAudioSource audio, ISttEngineProvider engines, IVoicePostProcessor post, ChatService chat,
    WebSettingsStore settings, SseHub hub, ILogger<SttService> log) : IHostedService
{
    /// <summary>마지막 확정 결과를 보관하는 시간 (연결이 끊겼다 돌아온 기기용, FR-VC-12)</summary>
    public static readonly TimeSpan LastKeep = TimeSpan.FromSeconds(60);

    /// <summary>인식 대기 오디오 상한(100ms 블록 50개 ≈ 5초). 넘치면 오래된 것부터 버린다 (FR-VC-06)</summary>
    private const int QueueCapacity = 50;

    private readonly object _gate = new();
    private Session? _session;
    private SttFinal? _last;

    public SttSnapshot Snapshot()
    {
        lock (_gate)
        {
            var s = _session;
            return s == null ? new(SttState.Idle, null, null, null) : new(s.State, s.Id, s.ByDevice, s.Capture?.Device.Name);
        }
    }

    public SttFinal? Last()
    {
        lock (_gate) return _last != null && DateTimeOffset.UtcNow - _last.At <= LastKeep ? _last : null;
    }

    public static string ErrorCode(AudioFailureKind kind) => kind switch
    {
        AudioFailureKind.NoDevice => "STT_MIC_NONE",
        AudioFailureKind.Denied => "STT_MIC_DENIED",
        AudioFailureKind.InUse => "STT_MIC_BUSY",
        AudioFailureKind.Removed => "STT_MIC_REMOVED",
        _ => "STT_MIC_FAILED",
    };

    /// <param name="stopAtEndpoint">첫 문장이 확정되면 세션을 끝낸다(버튼·토글). 누르는 동안 듣기는 false로 키를 뗄 때까지 이어 간다.</param>
    public SttStartResult Start(string byDevice, bool stopAtEndpoint = true)
    {
        Session s;
        lock (_gate)
        {
            if (_session != null) return new(SttStartStatus.Busy);
            if (!engines.IsInstalled) return new(SttStartStatus.ModelMissing);
            s = new Session(Guid.NewGuid().ToString("N")[..12], byDevice, stopAtEndpoint) { State = SttState.Starting };
            _session = s;
        }
        PublishState(s);

        try
        {
            s.Stream = engines.GetEngine().CreateStream();
            s.Capture = audio.Open(settings.Current.SttDeviceId);
            s.Capture.Samples += block =>
            {
                try
                {
                    // 가득 차면 가장 오래된 블록을 버린다 (지연이 쌓이는 것보다 낫다)
                    while (!s.Queue.TryAdd(block))
                    {
                        if (s.Queue.TryTake(out _)) Interlocked.Increment(ref s.Drops);
                    }
                }
                catch (InvalidOperationException) { }   // 이미 닫힘
            };
            s.Capture.Failed += f => { s.Failure = f; RequestStop(s); };
            s.Capture.Start();
        }
        catch (AudioException ex)
        {
            s.Failure = ex.Failure;
            Abort(s);
            return new(SttStartStatus.MicFailed, Failure: ex.Failure, Message: ex.Message);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "음성 인식 엔진을 시작하지 못했습니다");
            s.Failure = new AudioFailure(AudioFailureKind.Other, "음성 인식을 시작하지 못했습니다: " + ex.Message);
            Abort(s);
            return new(SttStartStatus.EngineFailed, Message: ex.Message);
        }

        lock (_gate) s.State = SttState.Listening;
        s.Worker = new Thread(() => Run(s)) { IsBackground = true, Name = "stt-" + s.Id, Priority = ThreadPriority.BelowNormal };
        s.Worker.Start();
        PublishState(s);
        return new(SttStartStatus.Started, s.Id);
    }

    /// <summary>듣기를 끝낸다. 지금까지 들은 글자는 확정된다. 듣는 중이 아니면 false.</summary>
    public bool Stop()
    {
        Session? s;
        lock (_gate) s = _session;
        if (s == null) return false;
        RequestStop(s);
        return true;
    }

    /// <summary>세션이 끝날 때까지 기다린다(테스트·종료용).</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        Session? s;
        lock (_gate) s = _session;
        return s == null || s.Done.Wait(timeout);
    }

    private void RequestStop(Session s)
    {
        if (Interlocked.Exchange(ref s.StopFlag, 1) == 1) return;
        try { s.Capture?.Dispose(); } catch (Exception ex) { log.LogDebug(ex, "마이크 닫기 오류"); }
        try { s.Queue.CompleteAdding(); } catch (ObjectDisposedException) { }
    }

    private void Abort(Session s)
    {
        try { s.Capture?.Dispose(); } catch { }
        try { s.Stream?.Dispose(); } catch { }
        lock (_gate)
        {
            if (_session == s) _session = null;
            s.State = SttState.Idle;
        }
        PublishState(s);
        s.Done.Set();
    }

    private void Run(Session s)
    {
        var clock = Stopwatch.StartNew();
        var skipFinish = false;
        var silent = 0;
        var warned = false;
        try
        {
            while (true)
            {
                if (s.StopFlag == 0 && clock.Elapsed > TimeSpan.FromSeconds(Math.Clamp(settings.Current.SttMaxSeconds, 1, 60))) RequestStop(s);
                if (!s.Queue.TryTake(out var block, 100))
                {
                    if (s.Queue.IsCompleted) break;
                    continue;
                }

                var level = s.Gain.Apply(block);
                // 소리가 정확히 0만 3초 이어지면 마이크 접근이 막혔을 수 있다 (FR-VC-05 ⑥)
                silent = level.Peak == 0 ? silent + 1 : 0;
                if (silent >= 30 && !warned) { warned = true; PublishState(s, warn: "NO_SIGNAL"); }

                s.Stream!.Accept(block);
                var p = s.Stream.Poll();
                if (p.IsEndpoint)
                {
                    s.Stream.Reset();
                    s.LastPartial = "";
                    if (p.Text.Length > 0)
                    {
                        EmitFinal(s, p.Text);
                        if (s.StopAtEndpoint) { skipFinish = true; RequestStop(s); break; }
                    }
                }
                else if (p.Text.Length > 0 && p.Text != s.LastPartial)
                {
                    s.LastPartial = p.Text;
                    hub.Broadcast("stt.partial", new { sessionId = s.Id, text = post.Process(p.Text).Text });
                }
            }

            lock (_gate) s.State = SttState.Finishing;
            PublishState(s);
            if (!skipFinish)
            {
                var tail = s.Stream!.Finish();
                if (tail.Length > 0) EmitFinal(s, tail);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "음성 인식 중 오류");
            s.Failure ??= new AudioFailure(AudioFailureKind.Other, "음성 인식 중 오류가 났습니다: " + ex.Message);
        }
        finally
        {
            try { s.Capture?.Dispose(); } catch { }
            try { s.Stream?.Dispose(); } catch { }
            if (s.Drops > 0) log.LogInformation("음성 입력: 인식이 느려 오디오 {Count}블록을 버렸습니다", s.Drops);
            lock (_gate)
            {
                if (_session == s) _session = null;
                s.State = SttState.Idle;
            }
            PublishState(s);
            s.Done.Set();
        }
    }

    private void EmitFinal(Session s, string raw)
    {
        var r = post.Process(raw);
        if (r.Text.Length == 0) return;   // 빈 결과는 초안을 바꾸지 않는다 (FR-VC-03)
        var fin = new SttFinal(s.Id, r.Text, raw, r.Applied, DateTimeOffset.UtcNow);
        lock (_gate) _last = fin;
        hub.Broadcast("stt.final", new { sessionId = fin.SessionId, text = fin.Text, original = fin.Original, applied = fin.Applied });
        if (settings.Current.SttAutoSend) _ = Task.Run(() => AutoSendAsync(fin));
    }

    /// <summary>자동 전송 (FR-VC-13): 중복 방지(1초)와 로그는 ChatService가 처리한다. 실패는 모든 기기에 토스트로 알린다.</summary>
    private async Task AutoSendAsync(SttFinal fin)
    {
        try
        {
            var autoEmote = settings.Current.AutoEmoteDefault;
            if (ChatText.Count(fin.Text) > ChatText.MaxLength - (autoEmote ? 2 : 0))
            {
                hub.Broadcast("toast", new { level = "warn", message = "음성 문장이 길어서 보내지 않았습니다. 입력창에서 줄여 주세요" });
                return;
            }
            var (status, entry) = await chat.SendAsync(fin.Text, autoEmote, "음성", "stt", CancellationToken.None);
            switch (status)
            {
                case ChatSendStatus.Duplicate:
                    hub.Broadcast("toast", new { level = "warn", message = "같은 문장을 방금 보냈습니다" });
                    break;
                case ChatSendStatus.Failed:
                    hub.Broadcast("toast", new { level = "warn", message = $"음성 채팅 전송 실패: {entry?.Error}" });
                    break;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "음성 자동 전송 오류");
        }
    }

    private void PublishState(Session s, string? warn = null)
    {
        SttState state;
        lock (_gate) state = s.State;
        var f = s.Failure;
        hub.Broadcast("stt.state", new
        {
            sessionId = s.Id,
            state = state.ToString().ToLowerInvariant(),
            byDevice = s.ByDevice,
            error = f == null ? null : ErrorCode(f.Kind),
            message = f?.Message,
            warn,
            device = s.Capture?.Device.Name,
            fallback = s.Capture?.UsedDefaultFallback ?? false,
        });
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        WaitIdle(TimeSpan.FromSeconds(2));   // 마이크를 닫고 끝낸다 (FR-VC-12)
        return Task.CompletedTask;
    }

    private sealed class Session(string id, string byDevice, bool stopAtEndpoint)
    {
        public string Id { get; } = id;
        public string ByDevice { get; } = byDevice;
        public bool StopAtEndpoint { get; } = stopAtEndpoint;
        public SttState State { get; set; }
        public ISttStream? Stream { get; set; }
        public IAudioCapture? Capture { get; set; }
        public Thread? Worker { get; set; }
        public AudioFailure? Failure { get; set; }
        public AutoGain Gain { get; } = new();
        public BlockingCollection<float[]> Queue { get; } = new(new ConcurrentQueue<float[]>(), QueueCapacity);
        public ManualResetEventSlim Done { get; } = new(false);
        public string LastPartial { get; set; } = "";
        public int StopFlag;
        public int Drops;
    }
}
