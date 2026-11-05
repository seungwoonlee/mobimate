using System.Collections.Concurrent;
using System.Diagnostics;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Services;

namespace MobiMate.Web.Voice;

public enum SttState { Idle, Starting, Listening, Finishing }

/// <summary><see cref="Cancelled"/>: 시작하는 도중에 중지가 와서 듣지 않고 끝났다(오류 아님)</summary>
public enum SttStartStatus { Started, Cancelled, Busy, ModelMissing, MicFailed, EngineFailed }

public sealed record SttStartResult(SttStartStatus Status, string? SessionId = null, AudioFailure? Failure = null, string? Message = null);

/// <summary>확정된 문장. <see cref="Original"/>은 후처리 전 인식 글자(학습 비교용)이다.</summary>
public sealed record SttFinal(string SessionId, string Text, string Original, IReadOnlyList<string> Applied, DateTimeOffset At, bool AutoSent = false);

public sealed record SttSnapshot(SttState State, string? SessionId, string? ByDevice, string? DeviceName);

/// <summary>마이크 시험 결과 (Peak·Rms는 0~1)</summary>
public sealed record MicTestResult(string Device, float Peak, float Rms, bool Fallback);

public sealed record MicTestOutcome(bool Busy, MicTestResult? Result, AudioFailure? Failure);

/// <summary>
/// 음성 입력 서비스 (FR-VC, 상세설계 §3.9). 한 번에 한 세션:
/// Idle → Starting(모델·마이크 열기) → Listening(100ms 단위로 변환·인식, 글자가 바뀔 때 stt.partial) → Finishing(남은 글자 확정) → Idle.
/// 인식은 낮은 우선순위 전용 스레드에서 돌려 게임에 영향을 주지 않는다 (FR-VC-06).
/// </summary>
public sealed class SttService(
    IAudioSource audio, ISttEngineProvider engines, IVoicePostProcessor post, ISttCue cue, ChatService chat,
    WebSettingsStore settings, SseHub hub, ILogger<SttService> log) : IHostedService
{
    /// <summary>마지막 확정 결과를 보관하는 시간 (연결이 끊겼다 돌아온 기기용, FR-VC-12)</summary>
    public static readonly TimeSpan LastKeep = TimeSpan.FromSeconds(60);

    /// <summary>인식 대기 오디오 상한(100ms 블록 50개 ≈ 5초). 넘치면 오래된 것부터 버린다 (FR-VC-06)</summary>
    private const int QueueCapacity = 50;

    /// <summary>이만큼 소리가 없으면(또는 블록이 안 오면) 마이크 문제를 알린다</summary>
    private static readonly TimeSpan NoSignalAfter = TimeSpan.FromSeconds(3);

    /// <summary>자동 전송은 한 번에 하나씩 (CLI 호출과 행동 명령이 섞이지 않게)</summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private readonly object _gate = new();
    private int _testing;   // 마이크 시험 중에는 듣기를 시작하지 않는다
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
            if (_session != null || Volatile.Read(ref _testing) == 1) return new(SttStartStatus.Busy);
            if (!engines.IsInstalled) return new(SttStartStatus.ModelMissing);
            s = new Session(Guid.NewGuid().ToString("N")[..12], byDevice, stopAtEndpoint) { State = SttState.Starting };
            _session = s;
        }
        PublishState(s);

        try
        {
            s.Stream = engines.GetEngine().CreateStream();
            if (Stopped(s)) return Cancel(s);
            // 시작 신호음은 마이크를 열기 전에 끝까지 재생한다: 신호음이 인식에 섞이지 않게 (FR-VC-02)
            if (settings.Current.SttCue) cue.Play(SttCueKind.Start);
            if (Stopped(s)) return Cancel(s);
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
            if (Stopped(s)) return Cancel(s);   // 열고 있는 동안 중지가 왔다
            s.Capture.Start();
        }
        catch (AudioException ex)
        {
            if (Stopped(s)) return Cancel(s);   // 중지가 마이크를 닫아 시작이 실패한 것: 오류가 아니다
            s.Failure = ex.Failure;
            Abort(s);
            return new(SttStartStatus.MicFailed, Failure: ex.Failure, Message: ex.Message);
        }
        catch (Exception ex)
        {
            if (Stopped(s)) return Cancel(s);
            log.LogWarning(ex, "음성 인식 엔진을 시작하지 못했습니다");
            s.Failure = new AudioFailure(AudioFailureKind.Other, "음성 인식을 시작하지 못했습니다: " + ex.Message);
            Abort(s);
            return new(SttStartStatus.EngineFailed, Message: ex.Message);
        }

        // Listening으로 바꾸는 것과 중지 확인을 한 락에서 한다. 상태를 알리는 일은 작업 스레드를 띄우기 전에 끝내
        // 작업 스레드가 내는 Idle보다 늦게 Listening이 나가지 않게 한다.
        bool cancelled;
        lock (_gate)
        {
            cancelled = Stopped(s);
            if (!cancelled) s.State = SttState.Listening;
        }
        if (cancelled) return Cancel(s);
        PublishState(s);
        s.Worker = new Thread(() => Run(s)) { IsBackground = true, Name = "stt-" + s.Id, Priority = ThreadPriority.BelowNormal };
        s.Worker.Start();
        return new(SttStartStatus.Started, s.Id);
    }

    private static bool Stopped(Session s) => Volatile.Read(ref s.StopFlag) == 1;

    private SttStartResult Cancel(Session s)
    {
        Abort(s);
        return new(SttStartStatus.Cancelled, s.Id);
    }

    /// <summary>시작하지 못한 이유를 사용자에게 보일 문장으로 (단축키 시작 실패 토스트용)</summary>
    public static string DescribeStartFailure(SttStartResult r) => r.Status switch
    {
        SttStartStatus.ModelMissing => "음성 인식 모델이 설치되어 있지 않습니다. 설정에서 설치해 주세요.",
        SttStartStatus.MicFailed => r.Failure?.Message ?? "마이크를 열지 못했습니다.",
        SttStartStatus.Busy => "이미 듣고 있습니다.",
        _ => "음성 인식을 시작하지 못했습니다.",
    };

    /// <summary>
    /// 마이크를 잠깐 열어 입력 크기를 잰다 (설정 화면의 마이크 테스트). 듣는 중이거나 이미 시험 중이면 Busy.
    /// 시험하는 동안 듣기 시작은 막힌다.
    /// </summary>
    public async Task<MicTestOutcome> TestMicAsync(TimeSpan duration, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_session != null) return new(true, null, null);
            if (Interlocked.CompareExchange(ref _testing, 1, 0) != 0) return new(true, null, null);
        }
        try
        {
            IAudioCapture cap;
            try { cap = audio.Open(settings.Current.SttDeviceId); }
            catch (AudioException ex) { return new(false, null, ex.Failure); }

            float peak = 0;
            double sum = 0;
            long count = 0;
            var sync = new object();
            cap.Samples += block =>
            {
                lock (sync)
                {
                    foreach (var v in block)
                    {
                        var a = Math.Abs(v);
                        if (a > peak) peak = a;
                        sum += v * v;
                    }
                    count += block.Length;
                }
            };
            try
            {
                cap.Start();
                await Task.Delay(duration, ct);
            }
            catch (AudioException ex) { return new(false, null, ex.Failure); }
            finally { cap.Dispose(); }

            lock (sync) return new(false, new MicTestResult(cap.Device.Name, peak, count > 0 ? (float)Math.Sqrt(sum / count) : 0f, cap.UsedDefaultFallback), null);
        }
        finally
        {
            Interlocked.Exchange(ref _testing, 0);
        }
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
        var silentSamples = 0;
        var warned = false;
        var sinceBlock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                if (s.StopFlag == 0 && clock.Elapsed > TimeSpan.FromSeconds(Math.Clamp(settings.Current.SttMaxSeconds, 1, 60))) RequestStop(s);
                if (!s.Queue.TryTake(out var block, 100))
                {
                    if (s.Queue.IsCompleted) break;
                    // 블록이 3초 넘게 오지 않는다: 마이크 콜백이 멈췄다 (FR-VC-05 ⑥)
                    if (!warned && sinceBlock.Elapsed > NoSignalAfter) { warned = true; PublishState(s, warn: "NO_SIGNAL"); }
                    continue;
                }
                sinceBlock.Restart();

                var level = s.Gain.Apply(block);
                // 소리가 정확히 0만 3초 어치 이어지면 마이크 접근이 막혔을 수 있다 (블록 길이와 무관하게 샘플 수로 센다)
                silentSamples = level.Peak == 0 ? silentSamples + block.Length : 0;
                if (silentSamples >= NoSignalAfter.TotalSeconds * AudioConverter.TargetRate && !warned) { warned = true; PublishState(s, warn: "NO_SIGNAL"); }

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
            try { PublishState(s); }
            catch (Exception ex) { log.LogDebug(ex, "상태 알림 실패"); }
            finally { s.Done.Set(); }
        }
    }

    private void EmitFinal(Session s, string raw)
    {
        var r = post.Process(raw);
        if (r.Text.Length == 0) return;   // 빈 결과는 초안을 바꾸지 않는다 (FR-VC-03)
        // 자동 전송이 켜져 있어도 한도를 넘으면 보내지 않고 화면 초안에 남긴다 (FR-VC-04). 화면은 autoSent가 false일 때만 초안에 붙인다.
        var cur = settings.Current;
        var autoSent = cur.SttAutoSend && FitsInChat(r.Text, cur.AutoEmoteDefault);
        var fin = new SttFinal(s.Id, r.Text, raw, r.Applied, DateTimeOffset.UtcNow, autoSent);
        lock (_gate) _last = fin;
        hub.Broadcast("stt.final", new { sessionId = fin.SessionId, text = fin.Text, original = fin.Original, applied = fin.Applied, autoSent });
        if (cur.SttCue) _ = Task.Run(() => cue.Play(SttCueKind.Done));   // 전체 화면 게임에서도 받았음을 알 수 있게
        if (autoSent) _ = Task.Run(() => AutoSendAsync(fin));
        else if (cur.SttAutoSend) hub.Broadcast("toast", new { level = "warn", message = "음성 문장이 길어서 보내지 않았습니다. 입력창에서 줄여 주세요" });
    }

    /// <summary>
    /// 본문이 잘리지 않고 한 번에 들어가는가. 자동 이모티콘이 켜져 있으면 실제 채팅 계획(이모지 길이는 문장마다 다르다)을 만들어
    /// 최종 문장이 원문으로 시작하는지로 판정한다 — 본문이 잘리면 시작 부분이 달라진다.
    /// </summary>
    internal static bool FitsInChat(string text, bool autoEmote)
    {
        var clean = ChatText.Sanitize(text);
        if (clean.Length == 0) return true;
        if (!autoEmote) return ChatText.Count(clean) <= ChatText.MaxLength;
        return ChatPlanService.BuildChatPlan(clean).FinalMessage.StartsWith(clean, StringComparison.Ordinal);
    }

    /// <summary>자동 전송 (FR-VC-13): 중복 방지(1초)와 로그는 ChatService가 처리한다. 실패는 모든 기기에 토스트로 알린다.</summary>
    private async Task AutoSendAsync(SttFinal fin)
    {
        await _sendGate.WaitAsync();
        try
        {
            var autoEmote = settings.Current.AutoEmoteDefault;
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
        finally
        {
            _sendGate.Release();
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
        WaitIdle(TimeSpan.FromSeconds(5));   // 마이크를 닫고 끝낸다. 엔진을 먼저 해제하지 않도록 충분히 기다린다 (FR-VC-12)
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
