using System.Security.Cryptography;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Voice;

public enum SttInstallPhase { Idle, Downloading, Done, Failed }

/// <summary>모델 내려받기 진행 상황 (SSE `stt.install`로도 나간다)</summary>
public sealed record SttInstallState(SttInstallPhase Phase, long DoneBytes, long TotalBytes, string? File, string? Error);

public enum SttInstallStart { Started, AlreadyRunning, AlreadyInstalled }

/// <summary>
/// 음성 인식 모델을 첫 사용 때 이 PC에만 내려받는다 (FR-VC-07). 배포 파일에는 넣지 않는다.
/// - 저장소 **커밋을 고정**해서 상류가 바뀌어도 같은 파일을 받는다. 파일마다 크기와 SHA-256을 코드에 고정해 검증한다.
/// - 따라가는 주소는 https이고 huggingface.co / hf.co(와 그 하위 도메인)뿐이다. 리다이렉트는 직접 따라가며 매번 검사한다.
/// - 받는 중에는 `.part` 파일에 쓰고, 크기와 해시가 맞을 때만 제 이름으로 바꾼다. 실패하면 `.part`를 지운다. 이어받기는 하지 않는다.
/// - 동시에 하나만 설치한다.
/// 학습 자료(KsponSpeech·AIHub)의 약관은 확인하지 못했다 — 개인 PC에서 쓰는 범위로 한정한다.
/// </summary>
public sealed class SttModelInstaller(SttModelLocator model, SseHub hub, ILogger<SttModelInstaller> log, IHostApplicationLifetime life, Func<HttpMessageHandler>? handler = null)
{
    public const string Repo = "kangkyu/icefall-asr-ko-streaming-zipformer-174m";
    public const string Revision = "f0e73b1653c3ea75898c6d949dd71c690c9121da";
    public static readonly Uri BaseUri = new($"https://huggingface.co/{Repo}/resolve/{Revision}/");
    private const int MaxRedirects = 5;

    /// <summary>이만큼 데이터가 오지 않으면 실패로 본다 (전체 시간이 아니라 무활동 기준 — 느린 회선에서도 끝까지 받는다)</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>내려받기 전에 확보해야 하는 여유 공간(모델 크기 + 임시 파일 여유)</summary>
    private const long SpareBytes = 100_000_000;

    private readonly object _lock = new();
    private SttInstallState _state = new(SttInstallPhase.Idle, 0, 0, null, null);
    private Task? _task;
    private long _lastPublish;

    public SttInstallState State { get { lock (_lock) return _state; } }

    /// <summary>허용된 호스트인가 (https 전용)</summary>
    public static bool AllowedUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        var h = uri.Host.ToLowerInvariant();
        return h == "huggingface.co" || h.EndsWith(".huggingface.co") || h == "hf.co" || h.EndsWith(".hf.co");
    }

    public SttInstallStart Start()
    {
        lock (_lock)
        {
            if (_task is { IsCompleted: false }) return SttInstallStart.AlreadyRunning;
            if (model.IsInstalled)
            {
                _state = new(SttInstallPhase.Done, 0, 0, null, null);
                return SttInstallStart.AlreadyInstalled;
            }
            _state = new(SttInstallPhase.Downloading, 0, model.Files.Sum(f => f.Size), null, null);
            _task = Task.Run(() => RunAsync(life.ApplicationStopping));
            return SttInstallStart.Started;
        }
    }

    /// <summary>끝날 때까지 기다린다(시험용)</summary>
    public Task WaitAsync() => _task ?? Task.CompletedTask;

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(model.Dir);
            foreach (var stale in Directory.GetFiles(model.Dir, "*.part")) TryDelete(stale);

            var total = model.Files.Sum(f => f.Size);
            var need = model.Files.Where(f => !File.Exists(model.PathOf(f.Name)) || new FileInfo(model.PathOf(f.Name)).Length != f.Size).Sum(f => f.Size);
            CheckFreeSpace(need);
            long done = 0;
            using var http = new HttpClient(handler?.Invoke() ?? new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            foreach (var f in model.Files)
            {
                var dest = model.PathOf(f.Name);
                if (File.Exists(dest) && new FileInfo(dest).Length == f.Size) { done += f.Size; continue; }   // 이미 받은 파일
                await DownloadAsync(http, f, dest, done, total, ct);
                done += f.Size;
            }
            Set(new(SttInstallPhase.Done, total, total, null, null), force: true);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "음성 인식 모델 설치 실패");
            var msg = ex is OperationCanceledException ? "설치가 중단되었습니다." : ex.Message;
            Set(new(SttInstallPhase.Failed, State.DoneBytes, State.TotalBytes, State.File, msg), force: true);
        }
    }

    private async Task DownloadAsync(HttpClient http, SttModelFile f, string dest, long doneBefore, long total, CancellationToken ct)
    {
        var part = dest + ".part";
        try
        {
            using var head = CancellationTokenSource.CreateLinkedTokenSource(ct);
            head.CancelAfter(IdleTimeout);   // 응답 머리까지
            using var resp = await GetFollowingAsync(http, new Uri(BaseUri, f.Name), head.Token);
            if (resp.Content.Headers.ContentLength is { } len && len != f.Size)
                throw new InvalidDataException($"{f.Name}: 크기가 다릅니다 ({len} ≠ {f.Size}).");

            using var sha = SHA256.Create();
            long got = 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buf = new byte[81920];
                int n;
                while (true)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(IdleTimeout);   // 읽을 때마다 다시 잰다
                    n = await src.ReadAsync(buf, idle.Token);
                    if (n <= 0) break;
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    sha.TransformBlock(buf, 0, n, null, 0);
                    got += n;
                    if (got > f.Size) throw new InvalidDataException($"{f.Name}: 예상보다 큽니다.");
                    Set(new(SttInstallPhase.Downloading, doneBefore + got, total, f.Name, null));
                }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            if (got != f.Size) throw new InvalidDataException($"{f.Name}: 크기가 맞지 않습니다 ({got} ≠ {f.Size}).");
            var hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            if (!hash.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{f.Name}: 파일 검증(SHA-256)에 실패했습니다.");

            File.Move(part, dest, overwrite: true);
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    private void CheckFreeSpace(long needBytes)
    {
        if (needBytes <= 0) return;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(model.Dir));
            if (string.IsNullOrEmpty(root)) return;
            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free < needBytes + SpareBytes)
                throw new IOException($"디스크 여유 공간이 부족합니다. {(needBytes + SpareBytes) / 1_000_000}MB 이상 필요하고 지금은 {free / 1_000_000}MB입니다.");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            // 네트워크 경로 등 여유 공간을 알 수 없는 곳은 그냥 진행한다
        }
    }

    /// <summary>리다이렉트를 직접 따라간다. 매 단계마다 https + 허용 호스트인지 확인한다.</summary>
    private static async Task<HttpResponseMessage> GetFollowingAsync(HttpClient http, Uri uri, CancellationToken ct)
    {
        for (var i = 0; i <= MaxRedirects; i++)
        {
            if (!AllowedUri(uri)) throw new InvalidOperationException($"허용되지 않은 주소입니다: {uri.Host}");
            var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)resp.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var loc = resp.Headers.Location ?? throw new InvalidOperationException("리다이렉트 주소가 없습니다.");
                resp.Dispose();
                uri = loc.IsAbsoluteUri ? loc : new Uri(uri, loc);
                continue;
            }
            if (!resp.IsSuccessStatusCode)
            {
                var code = (int)resp.StatusCode;
                resp.Dispose();
                throw new HttpRequestException($"내려받기 실패 (HTTP {code})");
            }
            return resp;
        }
        throw new InvalidOperationException("리다이렉트가 너무 많습니다.");
    }

    private void Set(SttInstallState next, bool force = false)
    {
        bool publish;
        lock (_lock)
        {
            _state = next;
            var now = Environment.TickCount64;
            publish = force || next.Phase != SttInstallPhase.Downloading || now - _lastPublish >= 250;   // 진행률은 0.25초마다만
            if (publish) _lastPublish = now;
        }
        if (publish) hub.BroadcastLocal("stt.install", Dto(next));
    }

    public static object Dto(SttInstallState s) => new
    {
        phase = s.Phase.ToString().ToLowerInvariant(),
        done = s.DoneBytes,
        total = s.TotalBytes,
        file = s.File,
        error = s.Error,
    };

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
