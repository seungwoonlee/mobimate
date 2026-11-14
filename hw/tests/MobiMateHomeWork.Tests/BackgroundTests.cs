using MobiMateHomeWork.Images;
using MobiMateHomeWork.Views;

namespace MobiMateHomeWork.Tests;

/// <summary>배경 그림 (H6 1차 피드백 4): 공식 일러스트 목록·설정 보정·그림이 없을 때의 대체·반투명 면의 글자 대비.</summary>
public class BackgroundTests
{
    private const double AppSettingsMax = MobiMateHomeWork.Hosting.BackgroundSettings.MaxOpacity;

    [Fact]
    public void Catalog_HasTheSixOfficialIllustrations_WithTheNightSkyAsDefault()
    {
        Assert.Equal(6, BackgroundCatalog.Known.Count);
        Assert.Equal("night_wish", BackgroundCatalog.DefaultId);
        Assert.All(BackgroundCatalog.Known, b => Assert.StartsWith("https://dszw1qtcnsa5e.cloudfront.net/community/", b.Url));
        Assert.Equal(6, BackgroundCatalog.Known.Select(b => b.Id).Distinct().Count());
        Assert.NotNull(BackgroundCatalog.Find("white_snow"));
        Assert.Null(BackgroundCatalog.Find("../x"));
    }

    [Fact]
    public async Task Downloader_ForBackgrounds_UsesOnlyTheFixedUrls()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-bg-" + Guid.NewGuid().ToString("N"));
        try
        {
            var urls = new List<string>();
            var handler = new Handler(r => { urls.Add(r.RequestUri!.AbsoluteUri); return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 1]) }; });
            var d = new ImageDownloader(dir, BackgroundCatalog.Remotes, () => new HttpClient(handler));
            Assert.Equal(6, await d.EnsureDownloadedAsync());
            Assert.All(urls, u => Assert.Contains(BackgroundCatalog.Known, b => b.Url == u));
            Assert.True(File.Exists(Path.Combine(dir, "night_wish.png")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(f(r));
    }

    [Fact]
    public void Settings_DefaultsAndClamps_AndSurvivesGarbage()
    {
        var dir = AppHarness.NewStorageDir();
        Directory.CreateDirectory(dir);
        try
        {
            var s = new AppSettingsStore(dir);
            Assert.Equal("official", s.Current.BackgroundMode);
            Assert.Equal("night_wish", s.Current.BackgroundId);
            Assert.Equal(0.35, s.Current.BackgroundOpacity);

            s.Update(d => { d.BackgroundMode = "custom"; d.BackgroundImagePath = @"C:\x\a.png"; d.BackgroundOpacity = 0.9; d.BackgroundId = "nope"; });
            var again = new AppSettingsStore(dir).Current;
            Assert.Equal("custom", again.BackgroundMode);
            Assert.Equal(@"C:\x\a.png", again.BackgroundImagePath);
            Assert.Equal(AppSettingsMax, again.BackgroundOpacity);        // 상한 보정
            Assert.Equal("night_wish", again.BackgroundId);      // 모르는 id는 기본값
            s.Update(d => { d.BackgroundOpacity = 0.01; d.BackgroundMode = "weird"; });
            Assert.Equal(0.05, s.Current.BackgroundOpacity);     // 하한 보정
            Assert.Equal("official", s.Current.BackgroundMode);
            Assert.Equal(0.35, BackgroundSettings.ClampOpacity(double.NaN));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResolvePath_FallsBackToTheDefaultOfficialPicture_WhenTheCustomFileIsMissing()
    {
        ImageFiles.Initialize(@"C:\store");
        var missing = Backdrop.ResolvePath("custom", "sunny_outing", @"C:\no\such.png");
        Assert.Equal(@"C:\store\backgrounds\night_wish.png", missing.Path);
        Assert.True(missing.Blur);
        Assert.Equal(@"C:\store\backgrounds\sunny_outing.png", Backdrop.ResolvePath("official", "sunny_outing", null).Path);
        Assert.Equal(@"C:\store\backgrounds\night_wish.png", Backdrop.ResolvePath("official", "bad id", null).Path);
        var real = Path.GetTempFileName();
        try { Assert.Equal((real, false), Backdrop.ResolvePath("custom", null, real)); }   // 내 그림은 블러 없이
        finally { File.Delete(real); }
    }

    [Fact]
    public void BrokenOrMissingImages_LoadAsNull_NotAsExceptions()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var bad = Path.Combine(dir, "bad.png");
            File.WriteAllBytes(bad, [1, 2, 3, 4, 5]);
            Assert.Null(ImageFiles.Load(bad, 100));
            Assert.Null(ImageFiles.Load(Path.Combine(dir, "none.png"), 100));
            Assert.Null(ImageFiles.Load("", 100));
            Assert.Null(ImageFiles.Load(null, 100));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── 반투명 면 위 글자 대비: 가장 밝은 그림(흰색)을 최대 투명도 50%로 깔아도 면은 AA ──

    private static (double R, double G, double B) Parse(string hex, out double alpha)
    {
        var h = hex.TrimStart('#');
        alpha = h.Length == 8 ? Convert.ToInt32(h[..2], 16) / 255.0 : 1;
        if (h.Length == 8) h = h[2..];
        return (Convert.ToInt32(h[..2], 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
    }

    private static string Hex((double R, double G, double B) c) => $"#{(int)Math.Round(c.R):X2}{(int)Math.Round(c.G):X2}{(int)Math.Round(c.B):X2}";

    private static (double R, double G, double B) Over((double R, double G, double B) top, double a, (double R, double G, double B) under) =>
        (top.R * a + under.R * (1 - a), top.G * a + under.G * (1 - a), top.B * a + under.B * (1 - a));

    [Theory]
    [InlineData("Panel", AppSettingsMax)]
    [InlineData("PanelBg", AppSettingsMax)]
    [InlineData("Panel", 0.25)]
    [InlineData("PanelBg", 0.25)]
    public void TextColors_MeetAA_OnTranslucentPanels_OverTheBrightestPicture(string panel, double opacity)
    {
        var colors = ColorTokens();
        var bg = Parse(colors["Bg"], out _);
        var white = (255.0, 255.0, 255.0);
        var picture = Over(white, opacity, bg);                      // 배경 그림(가장 밝은 경우) 위에 얹힌 모습
        var p = Parse(colors[panel], out var alpha);
        var face = Hex(Over(p, alpha, picture));
        foreach (var name in new[] { "Text", "TextMuted", "TextFaint", "Accent", "Done", "Warn", "Danger" })
            Assert.True(LayoutTests.Contrast(colors[name], face) >= 4.5, $"{name} on {panel}@{opacity}: {LayoutTests.Contrast(colors[name], face):0.00}");
    }

    private static Dictionary<string, string> ColorTokens()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "MobiMateHomeWork"))) dir = dir.Parent;
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "MobiMateHomeWork", "Themes", "Colors.xaml"));
        return System.Text.RegularExpressions.Regex.Matches(xaml, "<Color x:Key=\"Color\\.(\\w+)\">(#[0-9A-Fa-f]+)</Color>")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    }
}
