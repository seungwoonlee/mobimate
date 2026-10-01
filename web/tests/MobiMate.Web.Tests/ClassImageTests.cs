using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MobiMate.Web.Services;

namespace MobiMate.Web.Tests;

/// <summary>클래스 이미지 (v1.5 요청 4): 첫 실행 때 PC에만 내려받고, 못 받으면 SVG 아이콘으로 대신한다</summary>
public class ClassImageTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string> Requested { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Requested.Add(req.RequestUri!.ToString());
            return Task.FromResult(reply(req));
        }
    }

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mm-cls-" + Guid.NewGuid().ToString("N"));

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    [Fact]
    public void Known_IsEveryClassOnTheNexonImageServerOnly()
    {
        Assert.Equal(27, ClassImages.Known.Count);                                          // 6개 계열 × 4~5단계 직업
        Assert.Equal(ClassImages.Known.Count, ClassImages.Known.Select(c => c.Id).Distinct().Count());
        Assert.Equal(ClassImages.Known.Count, ClassImages.Known.Select(c => c.Url).Distinct().Count());
        Assert.All(ClassImages.Known, c => Assert.StartsWith("https://lwi.nexon.com/m_mabinogim/brand/info/class/", c.Url));
        Assert.All(ClassImages.Known, c => Assert.Matches(@"^(warrior|archer|thief|mage|bard|healer)_[1-5]$", c.Id));
    }

    [Fact]
    public async Task FirstRun_DownloadsEveryImageIntoTheStorageFolder_AndNextRunsSkipThem()
    {
        var dir = TempDir();
        try
        {
            var handler = new Fake(_ => Ok(Png));
            var svc = new ClassImages(dir, NullLogger<ClassImages>.Instance, () => new HttpClient(handler));
            Assert.Equal(ClassImages.Known.Count, await svc.EnsureDownloadedAsync());
            Assert.All(ClassImages.Known, c => Assert.True(File.Exists(Path.Combine(dir, "class-images", c.FileName))));
            Assert.Equal(ClassImages.Known.Count, handler.Requested.Count);

            Assert.Equal(0, await svc.EnsureDownloadedAsync());      // 이미 있으면 다시 받지 않는다
            Assert.Equal(ClassImages.Known.Count, handler.Requested.Count);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task FailedDownloads_LeaveNoFile_AndTheRestContinue()
    {
        var dir = TempDir();
        try
        {
            var handler = new Fake(req => req.RequestUri!.AbsoluteUri.Contains("mage_line_2") ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : req.RequestUri.AbsoluteUri.Contains("bard_line_2") ? Ok(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })   // PNG가 아니다
                : Ok(Png));
            var svc = new ClassImages(dir, NullLogger<ClassImages>.Instance, () => new HttpClient(handler));
            Assert.Equal(ClassImages.Known.Count - 2, await svc.EnsureDownloadedAsync());
            Assert.Null(svc.Existing("mage_3"));      // 마법사: 404
            Assert.Null(svc.Existing("bard_2"));      // 음유시인: PNG가 아님
            Assert.NotNull(svc.Existing("warrior_4"));
            Assert.Empty(Directory.GetFiles(Path.Combine(dir, "class-images"), "*.part"));   // 임시 파일이 남지 않는다
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task NetworkErrors_AreSwallowed()
    {
        var dir = TempDir();
        try
        {
            var svc = new ClassImages(dir, NullLogger<ClassImages>.Instance, () => new HttpClient(new Fake(_ => throw new HttpRequestException("offline"))));
            Assert.Equal(0, await svc.EnsureDownloadedAsync());
            Assert.All(svc.Status(), s => Assert.False(s.Available));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Theory]
    [InlineData("../../secrets")]
    [InlineData("warrior_4.png")]
    [InlineData("WARRIOR_4")]
    [InlineData("warrior")]   // 예전 계열 단위 id는 없다
    [InlineData("")]
    public void OnlyListedIdsAreAccepted(string id)
    {
        Assert.Null(ClassImages.Find(id));
        Assert.Null(new ClassImages(TempDir(), NullLogger<ClassImages>.Instance).Existing(id));
    }

    [Fact]
    public async Task Endpoint_ServesADownloadedImage_AndReturns404WhenMissing()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "class-images"));
        File.WriteAllBytes(Path.Combine(dir, "class-images", "warrior_4.png"), Png);
        try
        {
            using var host = new TestHost(dir) { KeepStorage = true };
            var c = await host.LocalAsync();
            var ok = await c.GetAsync("/api/class-image/warrior_4");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("image/png", ok.Content.Headers.ContentType!.MediaType);
            Assert.Contains("max-age", ok.Headers.CacheControl!.ToString());
            Assert.Equal(Png, await ok.Content.ReadAsByteArrayAsync());

            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/class-image/mage_3")).StatusCode);       // 아직 못 받음
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/class-image/..%2f..%2fweb_settings")).StatusCode);
            var status = await TestHost.Data(await c.GetAsync("/api/class-images"));
            Assert.True(status.EnumerateArray().First(s => s.GetProperty("id").GetString() == "warrior_4").GetProperty("available").GetBoolean());
            Assert.False(status.EnumerateArray().First(s => s.GetProperty("id").GetString() == "mage_3").GetProperty("available").GetBoolean());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
