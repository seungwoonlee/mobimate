using System.Globalization;
using System.Xml.Linq;
using MobiMateHomeWork.Layout;

namespace MobiMateHomeWork.Tests;

/// <summary>H3 순수 계산: 배치 단계(창 폭 → 단계), 단계 리소스 파일, 테마 색 대비, 리셋 카운트다운 글.</summary>
public class LayoutTests
{
    // ── 배치 단계 ──

    [Theory]
    [InlineData(0, LayoutTier.Narrow)]
    [InlineData(760, LayoutTier.Narrow)]          // 최소 폭
    [InlineData(899.99, LayoutTier.Narrow)]
    [InlineData(900, LayoutTier.Normal)]          // 경계는 위 단계에 속한다
    [InlineData(1279.99, LayoutTier.Normal)]
    [InlineData(1280, LayoutTier.Wide)]
    [InlineData(2560, LayoutTier.Wide)]
    [InlineData(double.NaN, LayoutTier.Narrow)]
    [InlineData(-5, LayoutTier.Narrow)]
    public void LayoutTier_Boundaries(double width, LayoutTier expected) => Assert.Equal(expected, LayoutTiers.For(width));

    [Theory]
    [InlineData(768, LayoutTier.Narrow)]          // FHD 125% 4분할
    [InlineData(853, LayoutTier.Narrow)]          // QHD 150%
    [InlineData(960, LayoutTier.Normal)]          // FHD 100%
    [InlineData(1024, LayoutTier.Normal)]         // QHD 125%
    [InlineData(1280, LayoutTier.Wide)]           // QHD 100%
    public void LayoutTier_TheFiveSnapSizes(double width, LayoutTier expected) => Assert.Equal(expected, LayoutTiers.For(width));

    [Fact]
    public async Task ViewModelTier_FollowsTheWidth_AndOnlyChangesAtTheBoundaries()
    {
        await using var h = await AppHarness.StartAsync();
        using var vm = ViewModelFixture.Make(h, out _);
        var changes = new List<LayoutTier>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Tier)) changes.Add(vm.Tier); };
        vm.SetWindowWidth(768);
        vm.SetWindowWidth(853);     // 같은 단계: 알림 없음
        vm.SetWindowWidth(960);
        vm.SetWindowWidth(1024);
        vm.SetWindowWidth(1280);
        vm.SetWindowWidth(800);
        Assert.Equal([LayoutTier.Normal, LayoutTier.Wide, LayoutTier.Narrow], changes);
    }

    // ── 단계 리소스 ──

    private static string ThemeDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "MobiMateHomeWork", "Themes"))) d = d.Parent;
        return d == null ? throw new InvalidOperationException("Themes 폴더를 찾지 못했습니다.") : Path.Combine(d.FullName, "src", "MobiMateHomeWork", "Themes");
    }

    private static Dictionary<string, string> Keys(string file)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(Path.Combine(ThemeDir(), file)).Root!.Elements()
            .Where(e => e.Attribute(x + "Key") != null)
            .ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Value.Trim());
    }

    [Fact]
    public void TierResources_HaveTheSameKeys_AndTheDocumentedValues()
    {
        var narrow = Keys(LayoutTiers.ResourceFile(LayoutTier.Narrow));
        var normal = Keys(LayoutTiers.ResourceFile(LayoutTier.Normal));
        var wide = Keys(LayoutTiers.ResourceFile(LayoutTier.Wide));
        Assert.Equal(narrow.Keys.Order(), normal.Keys.Order());
        Assert.Equal(narrow.Keys.Order(), wide.Keys.Order());

        // 기본 글자 12/13/14, 최소 11, 카드 폭 232/270/320
        Assert.Equal(["12", "13", "14"], new[] { narrow, normal, wide }.Select(d => d["Font.Base"]));
        Assert.All(new[] { narrow, normal, wide }, d => Assert.True(double.Parse(d["Font.Small"], CultureInfo.InvariantCulture) >= 11));
        Assert.Equal(["210", "250", "320"], new[] { narrow, normal, wide }.Select(d => d["Metrics.CardWidth"]));
    }

    // ── H4: 숙제 표 치수 ──

    private static double Num(Dictionary<string, string> d, string key) => double.Parse(d[key], CultureInfo.InvariantCulture);
    private static double Left(Dictionary<string, string> d, string key) => double.Parse(d[key].Split(',')[0], CultureInfo.InvariantCulture);

    [Fact]
    public void TierResources_TableMetrics_AreTheDocumentedValues_AndVisibilityIsPerTier()
    {
        var t = new[] { LayoutTier.Narrow, LayoutTier.Normal, LayoutTier.Wide }.Select(x => Keys(LayoutTiers.ResourceFile(x))).ToArray();
        Assert.Equal(["44", "60", "76"], t.Select(d => d["Metrics.RowHeight"]));
        Assert.Equal(["32", "39", "60"], t.Select(d => d["Metrics.CellWidth"]));
        Assert.Equal(["32", "44", "56"], t.Select(d => d["Metrics.CellHeight"]));
        // 좁게 = 기호만 / 보통 = 기호 + 남은 횟수 / 넓게 = 칸 안 두 줄 이름
        Assert.Equal(["Visible", "Visible", "Collapsed"], t.Select(d => d["Show.CellSymbol"]));
        Assert.Equal(["Collapsed", "Collapsed", "Visible"], t.Select(d => d["Show.CellName"]));
        Assert.Equal(["Collapsed", "Visible", "Collapsed"], t.Select(d => d["Show.CellLeft"]));
        Assert.Equal(["Collapsed", "Visible", "Visible"], t.Select(d => d["Show.RowSub"]));        // 보통 이상: 마지막 접속
        Assert.Equal(["Collapsed", "Collapsed", "Visible"], t.Select(d => d["Show.RowMeters"]));   // 넓게: 작은 막대
        Assert.All(t, d => Assert.Contains(d["Show.GroupShort"], new[] { "Visible", "Hidden" }));   // 넓게는 자리만 차지(묶음 폭을 맞춘다)
        Assert.All(t, d => Assert.True(Num(d, "Metrics.CellWidth") >= 32 && Num(d, "Metrics.CellHeight") >= 32));   // 누를 수 있는 최소 크기
    }

    // 4분할 크기마다: 6줄이 스크롤 없이 들어가는 높이 + 표가 넘치지 않는 폭 (바깥 테두리 2, 표 여백 8, 카드 여백 8, 스크롤바 자리 10, 제목 32, 계정 탭 28, 본문 위아래 여백 16)
    [Theory]
    [InlineData(768, 405)]
    [InlineData(853, 458)]
    [InlineData(960, 510)]
    [InlineData(1024, 552)]
    [InlineData(1280, 690)]
    [InlineData(1536, 840)]
    public void SixRows_FitWithoutScrolling_AtEveryTargetSize(int width, int height)
    {
        var tier = LayoutTiers.For(width);
        var d = Keys(LayoutTiers.ResourceFile(tier));
        var scale = LayoutTiers.WideScale(tier, width);
        double Scaled(string key) => Math.Round(Num(d, key) * scale);
        var rowHeight = Scaled("Metrics.RowHeight");
        var cardWidth = Math.Round(Num(d, "Metrics.CardWidth") * scale);
        var infoMin = Scaled("Metrics.CharInfoWidth");
        var cellWidth = Scaled("Metrics.CellWidth");

        var needHeight = 32 + 28 + 16 + Num(d, "Metrics.GroupHeaderHeight") + 6 * rowHeight;
        Assert.True(needHeight <= height - 2, $"높이 {needHeight} > {height - 2}");

        var cellGap = 2.0;   // CellMargin 1,1
        var needWidth = infoMin + 3 + 11 * (cellWidth + cellGap) + 4 * Left(d, "Metrics.GroupMargin");
        var available = width - 2 - cardWidth - 16 - 10;
        Assert.True(needWidth <= available, $"폭 {needWidth} > {available} ({tier})");
    }

    // ── 색 대비 (WCAG AA) ──

    private static (double R, double G, double B) Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];   // 알파는 무시: 겹침색은 따로 판단한다
        double C(int i) => Convert.ToInt32(h.Substring(i, 2), 16) / 255.0;
        return (C(0), C(2), C(4));
    }

    private static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double L(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return 0.2126 * L(r) + 0.7152 * L(g) + 0.0722 * L(b);
    }

    public static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static string Color(string name) => Keys("Colors.xaml")["Color." + name];

    [Theory]
    [InlineData("Text")]
    [InlineData("TextMuted")]
    [InlineData("TextFaint")]
    [InlineData("Accent")]
    [InlineData("Done")]
    [InlineData("Warn")]
    [InlineData("Danger")]
    public void TextColors_MeetAA_OnEverySurface(string name)
    {
        foreach (var surface in new[] { "Bg", "Surface", "Surface2" })
            Assert.True(Contrast(Color(name), Color(surface)) >= 4.5, $"{name} on {surface}: {Contrast(Color(name), Color(surface)):0.00}");
    }

    [Fact]
    public void ColoredPairs_MeetAA()
    {
        Assert.True(Contrast(Color("OnAccent"), Color("Accent")) >= 4.5);      // 강조 버튼 글자
        Assert.True(Contrast(Color("Done"), Color("DoneFill")) >= 4.5);        // 완료 칸의 ✓
        Assert.True(Contrast(Color("Text"), Color("DoneFill")) >= 4.5);
    }

    [Fact]
    public void BarsAndOutlines_MeetTheGraphicContrast_OnSurface()
    {
        // 그래픽 요소(막대·외곽선)는 3:1 이상
        foreach (var name in new[] { "BarSilver", "BarTribute", "Done", "Warn", "Danger", "Accent" })
            Assert.True(Contrast(Color(name), Color("Surface")) >= 3, $"{name}: {Contrast(Color(name), Color("Surface")):0.00}");
    }

    // ── 리셋 카운트다운 ──

    [Fact]
    public void ResetCountdown_FormatsDailyAsHoursMinutes_AndWeeklyAsDaysHours()
    {
        // 2026-10-14(수) 00:48 KST = 10-13(화) 15:48 UTC → 일일 06:00까지 5:12, 주간(10-19 월 06:00)까지 5일 5시간 12분
        var now = new DateTimeOffset(2026, 10, 13, 15, 48, 0, TimeSpan.Zero);
        Assert.Equal("일 5:12 · 주 5일 5h", ResetCountdown.Format(now));
    }

    [Fact]
    public void ResetCountdown_LessThanADay_OmitsDays()
    {
        // 2026-10-19(월) 03:00 KST = 10-18(일) 18:00 UTC → 일일 3:00, 주간 3시간
        var now = new DateTimeOffset(2026, 10, 18, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal("일 3:00 · 주 3h", ResetCountdown.Format(now));
    }

    [Fact]
    public void ResetCountdown_AtTheResetInstant_StartsAFullPeriod()
    {
        // 월요일 06:00 KST 정각 = 일요일 21:00 UTC → 다음 일일 24:00, 주간 7일
        var now = new DateTimeOffset(2026, 10, 18, 21, 0, 0, TimeSpan.Zero);
        Assert.Equal("일 24:00 · 주 7일 0h", ResetCountdown.Format(now));
    }
}
