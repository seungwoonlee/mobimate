using System.Net;
using MobiMateHomeWork.Images;

namespace MobiMateHomeWork.Tests;

/// <summary>직업 이미지·배경 이미지 내려받기 (H6 1차 피드백 3·4): 실제 인터넷에는 접속하지 않고 가짜 HttpClient만 쓴다.</summary>
public class ImageTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Urls) Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mm-img-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ClassImageCatalog_Has27Images_AndEveryJobNameMapsToAKnownImage()
    {
        Assert.Equal(27, ClassImageCatalog.Known.Count);
        Assert.All(ClassImageCatalog.Known, c => Assert.StartsWith("https://lwi.nexon.com/m_mabinogim/brand/info/class/", c.Url));
        Assert.Equal("warrior_1", ClassImageCatalog.IdOf("견습 전사"));
        Assert.Equal("warrior_1", ClassImageCatalog.IdOf("견습전사"));
        Assert.Equal("healer_5", ClassImageCatalog.IdOf("수도사"));
        Assert.Equal("thief_4", ClassImageCatalog.IdOf(" 듀얼 블레이드 "));
        Assert.Null(ClassImageCatalog.IdOf("연금술사"));
        Assert.Null(ClassImageCatalog.IdOf(""));
        // 직업 색 표의 직업은 모두 이미지가 있다
        foreach (var job in new[] { "기사", "전사", "대검전사", "검술사", "궁수", "석궁사수", "장궁병", "도적", "격투가", "마법사", "전격술사", "화염술사", "빙결술사", "음유시인", "댄서", "악사", "힐러", "사제", "암흑술사" })
            Assert.NotNull(ClassImageCatalog.IdOf(job));
    }

    [Fact]
    public async Task Downloader_SavesOnlyMissingPngs_FromTheFixedList_WithoutLeavingPartFiles()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "warrior_1.png"), [9, 9, 9]);       // 이미 있는 파일은 건너뛴다(덮어쓰지 않는다)
            var handler = new FakeHandler(_ => Ok(Png));
            var d = new ImageDownloader(dir, ClassImageCatalog.Known, () => new HttpClient(handler));

            var n = await d.EnsureDownloadedAsync();

            Assert.Equal(26, n);
            Assert.Equal(26, handler.Urls.Count);
            Assert.All(handler.Urls, u => Assert.Contains(ClassImageCatalog.Known, c => c.Url == u));
            Assert.Equal([9, 9, 9], File.ReadAllBytes(Path.Combine(dir, "warrior_1.png")));
            Assert.Equal(27, Directory.GetFiles(dir, "*.png").Length);
            Assert.Empty(Directory.GetFiles(dir, "*.part"));
            Assert.Equal(0, d.MissingCount);
            Assert.Equal(0, await d.EnsureDownloadedAsync());                         // 다 있으면 요청하지 않는다
            Assert.Equal(26, handler.Urls.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Downloader_RejectsNonPng_TooBig_AndHttpErrors()
    {
        var dir = TempDir();
        try
        {
            var list = ClassImageCatalog.Known.Take(4).ToList();
            var handler = new FakeHandler(req => req.RequestUri!.ToString() switch
            {
                var u when u == list[0].Url => Ok([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),                      // PNG가 아님
                var u when u == list[1].Url => Ok(new byte[ImageDownloader.MaxBytes + 10].Select((_, i) => i < 4 ? Png[i] : (byte)0).ToArray()),   // 4MB 초과
                var u when u == list[2].Url => new HttpResponseMessage(HttpStatusCode.NotFound),
                _ => Ok(Png),
            });
            var d = new ImageDownloader(dir, list, () => new HttpClient(handler));

            Assert.Equal(1, await d.EnsureDownloadedAsync());
            Assert.Equal([list[3].FileName], Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Downloader_TwoDownloadersAtOnce_ArePeacefulAndLeaveValidFiles()
    {
        var dir = TempDir();
        try
        {
            var a = new ImageDownloader(dir, ClassImageCatalog.Known, () => new HttpClient(new FakeHandler(_ => Ok(Png))));
            var b = new ImageDownloader(dir, ClassImageCatalog.Known, () => new HttpClient(new FakeHandler(_ => Ok(Png))));
            var total = await Task.WhenAll(a.EnsureDownloadedAsync(), b.EnsureDownloadedAsync());
            Assert.Equal(27, total.Sum());                  // 어느 한쪽만 옮긴다(이미 있으면 건너뜀)
            Assert.Equal(27, Directory.GetFiles(dir, "*.png").Length);
            Assert.Empty(Directory.GetFiles(dir, "*.part"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Downloader_PathOf_AcceptsOnlyKnownIds()
    {
        var d = new ImageDownloader(TempDir(), ClassImageCatalog.Known);
        Assert.NotNull(d.PathOf("mage_3"));
        Assert.Null(d.PathOf("../evil"));
        Assert.Null(d.PathOf(null));
    }

    [Fact]
    public async Task Board_RowAndCard_CarryTheJobImageId()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var snap = h.Session.Characters();
        var card = new MobiMateHomeWork.ViewModels.CharacterCardViewModel();
        card.Apply(snap.Card(FakeGame.SampleKey), DateTimeOffset.UtcNow);
        Assert.Equal("thief_3", card.JobImageId);           // 격투가
    }
}
