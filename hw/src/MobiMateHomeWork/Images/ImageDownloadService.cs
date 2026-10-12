using MobiMateHomeWork.Hosting;

namespace MobiMateHomeWork.Images;

/// <summary>
/// 시작할 때 없는 이미지(직업 일러스트 27개)만 백그라운드로 내려받는다. 이미 다 있으면(웹앱이 받아 둠) 아무것도 하지 않는다.
/// 받을 때마다 <see cref="ImageFiles.Refresh"/>로 화면에 알린다. 실패해도 앱 동작에는 영향이 없다(직업 색 원+첫 글자로 대신 보인다).
/// </summary>
public sealed class ImageDownloadService : BackgroundService
{
    private readonly AppOptions _options;
    private readonly ILogger<ImageDownloadService> _log;
    private readonly IReadOnlyList<ImageDownloader> _downloaders;

    public ImageDownloadService(AppOptions options, ILogger<ImageDownloadService> log, Func<HttpClient>? http = null)
    {
        _options = options;
        _log = log;
        _downloaders =
        [
            new ImageDownloader(Path.Combine(options.StorageDir, ClassImageCatalog.FolderName), ClassImageCatalog.Known, http, log),
        ];
    }

    /// <summary>내려받기를 한 번 한다(없는 파일만). 내려받은 개수.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var total = 0;
        foreach (var d in _downloaders)
        {
            if (d.MissingCount == 0) continue;
            var n = await d.EnsureDownloadedAsync(ct);
            total += n;
            if (n > 0)
            {
                _log.LogInformation("이미지 {Count}개를 내려받았습니다: {Dir}", n, d.Folder);
                ImageFiles.Refresh();
            }
        }
        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_options.DownloadImages) return;
        try { await Task.Run(() => RunOnceAsync(ct), ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogInformation(ex, "이미지 내려받기 실패"); }
    }
}
