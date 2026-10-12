using System.Net;

namespace MobiMateHomeWork.Images;

/// <summary>내려받을 이미지 하나: 이미지 id와 고정 원격 주소. 파일 이름은 <c>id.png</c>.</summary>
public sealed record RemoteImage(string Id, string Url)
{
    public string FileName => $"{Id}.png";
}

/// <summary>
/// 공개 이미지(직업 일러스트·공식 홈페이지 배경 일러스트)를 <b>첫 실행 때 이 PC에만</b> 내려받아 공유 저장 폴더의 하위 폴더
/// (<c>class-images</c>, <c>backgrounds</c>)에 둔다. 배포 파일(exe·zip)에는 넣지 않는다(저작권) — 웹앱의 직업 이미지(ClassImages)와 같은 방식이다.
/// 주소는 코드에 고정한 목록만 쓰고, 요청으로 받은 값으로 주소를 만들지 않는다. 없는 파일만 받는다(PNG 검사, 4MB 제한).
/// 웹앱과 동시에 받아도 안전하다: 고유한 <c>.part</c>에 쓰고 목적지가 이미 있으면 버리며, 이동 실패는 무시한다.
/// </summary>
public sealed class ImageDownloader
{
    public const long MaxBytes = 4 * 1024 * 1024;

    private readonly string _dir;
    private readonly IReadOnlyList<RemoteImage> _known;
    private readonly Func<HttpClient> _http;
    private readonly ILogger? _log;

    public ImageDownloader(string dir, IReadOnlyList<RemoteImage> known, Func<HttpClient>? http = null, ILogger? log = null)
    {
        _dir = dir;
        _known = known;
        _http = http ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        _log = log;
    }

    public string Folder => _dir;
    public IReadOnlyList<RemoteImage> Known => _known;

    public RemoteImage? Find(string? id) => _known.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    /// <summary>목록에 있는 id의 파일 경로(없는 id는 null — 경로 조작 방지).</summary>
    public string? PathOf(string? id) => Find(id) is { } c ? Path.Combine(_dir, c.FileName) : null;

    /// <summary>저장된 파일이 있으면 그 경로, 없으면 null.</summary>
    public string? Existing(string? id) => PathOf(id) is { } p && File.Exists(p) ? p : null;

    public int MissingCount => _known.Count(c => !File.Exists(Path.Combine(_dir, c.FileName)));

    /// <summary>없는 이미지만 내려받는다. 하나가 실패해도 나머지는 계속한다. 내려받은 개수를 돌려준다.</summary>
    public async Task<int> EnsureDownloadedAsync(CancellationToken ct = default)
    {
        if (MissingCount == 0) return 0;
        var done = 0;
        using var http = _http();
        foreach (var c in _known)
        {
            var path = Path.Combine(_dir, c.FileName);
            if (File.Exists(path)) continue;
            string? tmp = null;
            try
            {
                Directory.CreateDirectory(_dir);
                using var res = await http.GetAsync(c.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (res.StatusCode != HttpStatusCode.OK) { _log?.LogInformation("이미지 {Id}: 내려받지 못했습니다 ({Status})", c.Id, (int)res.StatusCode); continue; }
                if (res.Content.Headers.ContentLength is > MaxBytes) { _log?.LogInformation("이미지 {Id}: 너무 큽니다", c.Id); continue; }
                var bytes = await ReadLimitedAsync(res.Content, ct);
                if (bytes == null || !IsPng(bytes)) { _log?.LogInformation("이미지 {Id}: PNG가 아니거나 너무 큽니다", c.Id); continue; }
                tmp = $"{path}.{Environment.ProcessId}-{Guid.NewGuid():N}.part";
                await File.WriteAllBytesAsync(tmp, bytes, ct);
                if (File.Exists(path)) continue;   // 그 사이 웹앱이 받았다: 건너뜀 (finally에서 임시 파일 정리)
                try { File.Move(tmp, path, overwrite: false); tmp = null; done++; }
                catch (IOException) { /* 동시에 받은 쪽이 먼저 옮겼다 */ }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
            {
                _log?.LogInformation("이미지 {Id}: 내려받지 못했습니다 ({Message})", c.Id, ex.Message);
            }
            finally
            {
                if (tmp != null) try { File.Delete(tmp); } catch { }
            }
        }
        return done;
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var s = await content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > MaxBytes) return null;
        }
        return ms.ToArray();
    }

    private static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
}
