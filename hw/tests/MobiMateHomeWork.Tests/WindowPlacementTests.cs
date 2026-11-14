using MobiMateHomeWork.Layout;

namespace MobiMateHomeWork.Tests;

/// <summary>첫 실행 위치(작업 영역·DPI 입력)와 저장 위치 복원/대체. 단위는 물리 픽셀, 최소 크기는 배율을 곱해 환산한다.</summary>
public class WindowPlacementTests
{
    private static MonitorArea Mon(int l, int t, int w, int h, double scale = 1.0) => new(new PxRect(l, t, w, h), scale);

    // ── 첫 실행 = 작업 영역 4분할, 오른쪽 위 ──

    [Theory]
    // 모니터·배율(계획서 §6 표): 작업 영역(px) → 4분할 크기(px) = DIP × 배율
    [InlineData(1920, 1040, 1.25, 960, 520)]     // FHD 125% → 768×416 DIP
    [InlineData(2560, 1392, 1.50, 1280, 696)]    // QHD 150% → 853×464 DIP
    [InlineData(1920, 1032, 1.00, 960, 516)]     // FHD 100% → 960×516 DIP
    [InlineData(2560, 1392, 1.25, 1280, 696)]    // QHD 125% → 1024×557 DIP
    [InlineData(2560, 1392, 1.00, 1280, 696)]    // QHD 100% → 1280×696 DIP
    public void FirstRun_IsAQuarterOfTheWorkArea_AtTheTopRight(int w, int h, double scale, int expectedW, int expectedH)
    {
        var r = WindowPlacement.FirstRun(Mon(0, 0, w, h, scale));
        Assert.Equal(new PxRect(w - expectedW, 0, expectedW, expectedH), r);
        // DIP로 환산하면 계획서 표의 4분할 크기 범위 안
        Assert.InRange(r.Width / scale, 760, 1281);
        Assert.InRange(r.Height / scale, 380, 697);
    }

    [Fact]
    public void FirstRun_OnASecondaryMonitor_StaysInsideThatMonitor()
    {
        // 왼쪽 모니터(음수 좌표) 위쪽에 작업 표시줄(작업 영역이 y=40부터)
        var r = WindowPlacement.FirstRun(Mon(-1920, 40, 1920, 1000, 1.0));
        Assert.Equal(new PxRect(-960, 40, 960, 500), r);
    }

    [Fact]
    public void FirstRun_OnASmallMonitor_NeverGoesBelowTheMinimumSize()
    {
        // 1366×728 100%: 절반은 683×364지만 최소 760×380을 지킨다
        var r = WindowPlacement.FirstRun(Mon(0, 0, 1366, 728, 1.0));
        Assert.Equal(new PxRect(1366 - 760, 0, 760, 380), r);
    }

    [Fact]
    public void FirstRun_MinimumScalesWithDpi()
    {
        // 1280×680 150%: 절반 640×340 px는 최소(760×380 DIP = 1140×570 px)보다 작고, 작업 영역 높이(680)에는 들어간다
        var r = WindowPlacement.FirstRun(Mon(0, 0, 1280, 680, 1.5));
        Assert.Equal(new PxRect(1280 - 1140, 0, 1140, 570), r);
    }

    [Fact]
    public void FirstRun_WhenTheWorkAreaIsSmallerThanTheMinimum_UsesTheWorkArea()
    {
        var r = WindowPlacement.FirstRun(Mon(0, 0, 700, 360, 1.0));
        Assert.Equal(new PxRect(0, 0, 700, 360), r);
    }

    // ── 저장 위치 복원 ──

    private static readonly MonitorArea Main = Mon(0, 0, 1920, 1040, 1.0);

    [Fact]
    public void Restore_KeepsAVisibleSavedRect()
    {
        var r = WindowPlacement.Restore(new SavedPlacement(300, 200, 1000, 600, false), [Main]);
        Assert.Equal(new PxRect(300, 200, 1000, 600), r);
    }

    [Fact]
    public void Restore_OnAMonitorThatIsGone_FallsBackToNull_SoTheFirstRunRuleApplies()
    {
        // 저장 위치가 오른쪽 보조 모니터(1920~3840)였는데 지금은 모니터가 하나
        Assert.Null(WindowPlacement.Restore(new SavedPlacement(2500, 100, 960, 520, false), [Main]));
        // 왼쪽 위쪽으로 완전히 벗어남
        Assert.Null(WindowPlacement.Restore(new SavedPlacement(-3000, -2000, 960, 520, false), [Main]));
    }

    [Fact]
    public void Restore_PicksTheMonitorTheWindowMostlyCovers()
    {
        var left = Mon(-1920, 0, 1920, 1040, 1.0);
        var r = WindowPlacement.Restore(new SavedPlacement(-1500, 100, 1000, 600, false), [Main, left]);
        Assert.Equal(new PxRect(-1500, 100, 1000, 600), r);
    }

    [Fact]
    public void Restore_PartlyOffTheEdge_IsPulledInside_WhenTheTitleBarIsStillReachable()
    {
        var r = WindowPlacement.Restore(new SavedPlacement(1500, 100, 960, 520, false), [Main]);
        Assert.Equal(new PxRect(1920 - 960, 100, 960, 520), r);
    }

    [Fact]
    public void Restore_WhenTheTitleBarIsOffTheBottom_FallsBackToNull()
    {
        // 제목줄이 작업 영역 아래로 완전히 나가면 잡을 수 없다
        Assert.Null(WindowPlacement.Restore(new SavedPlacement(100, 1040, 960, 520, false), [Main]));
    }

    [Fact]
    public void Restore_WhenOnlyAFewPixelsOfTheTitleBarShow_FallsBackToNull()
    {
        // 오른쪽 끝에 40px만 걸침(최소 120 DIP 필요)
        Assert.Null(WindowPlacement.Restore(new SavedPlacement(1880, 100, 960, 520, false), [Main]));
    }

    [Fact]
    public void Restore_ClampsTheSizeToMinimumAndWorkArea()
    {
        Assert.Equal(new PxRect(100, 100, 760, 380), WindowPlacement.Restore(new SavedPlacement(100, 100, 300, 200, false), [Main]));
        Assert.Equal(new PxRect(0, 0, 1920, 1040), WindowPlacement.Restore(new SavedPlacement(0, 0, 5000, 3000, false), [Main]));
    }

    [Fact]
    public void Restore_MinimumUsesTheMonitorsScale()
    {
        var hi = Mon(0, 0, 2560, 1392, 1.5);
        var r = WindowPlacement.Restore(new SavedPlacement(100, 100, 500, 300, false), [hi]);
        Assert.Equal(new PxRect(100, 100, 1140, 570), r);   // 760×380 DIP × 1.5
    }

    [Fact]
    public void Restore_RejectsEmptyOrBrokenInput()
    {
        Assert.Null(WindowPlacement.Restore(new SavedPlacement(0, 0, 0, 0, false), [Main]));
        Assert.Null(WindowPlacement.Restore(new SavedPlacement(0, 0, 960, 520, false), []));
    }
}
