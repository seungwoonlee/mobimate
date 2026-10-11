namespace MobiMateHomeWork.Layout;

/// <summary>화면 좌표(물리 픽셀) 사각형. 가상 화면 좌표라 모니터가 왼쪽·위에 있으면 음수일 수 있다.</summary>
public readonly record struct PxRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public static PxRect Intersect(PxRect a, PxRect b)
    {
        var l = Math.Max(a.Left, b.Left);
        var t = Math.Max(a.Top, b.Top);
        var r = Math.Min(a.Right, b.Right);
        var bt = Math.Min(a.Bottom, b.Bottom);
        return r <= l || bt <= t ? new PxRect(l, t, 0, 0) : new PxRect(l, t, r - l, bt - t);
    }

    public long Area => (long)Width * Height;

    public override string ToString() => $"({Left},{Top}) {Width}x{Height}";
}

/// <summary>모니터 하나: 작업 영역(작업 표시줄 제외, 물리 픽셀)과 배율(1.0 = 96dpi, 1.25 = 125%).</summary>
public sealed record MonitorArea(PxRect WorkArea, double Scale);

/// <summary>저장된 창 위치·크기(물리 픽셀).</summary>
public sealed record SavedPlacement(int Left, int Top, int Width, int Height, bool Maximized);

/// <summary>
/// 창 위치·크기 계산 (화면·DPI 입력만 받는 순수 계산). 단위는 물리 픽셀이고, 최소 크기·제목줄 높이는 모니터 배율을 곱해 환산한다.
/// 첫 실행 = 작업 영역의 4분할 크기로 오른쪽 위. 저장된 위치가 어느 모니터에도 안 보이면 첫 실행 규칙으로 되돌린다.
/// </summary>
public static class WindowPlacement
{
    public const double MinWidthDip = 760;
    public const double MinHeightDip = 380;
    public const double TitleBarDip = 32;

    /// <summary>제목줄이 화면에 이만큼(DIP) 이상 보여야 끌어서 옮길 수 있다고 본다.</summary>
    public const double MinVisibleTitleWidthDip = 120;

    /// <summary>첫 실행: 작업 영역 폭·높이의 절반(4분할 한 칸), 오른쪽 위. 최소 크기보다 작아지지 않고 작업 영역보다 커지지 않는다.</summary>
    public static PxRect FirstRun(MonitorArea monitor)
    {
        var area = monitor.WorkArea;
        var w = FitSize(area.Width / 2, MinPx(MinWidthDip, monitor.Scale), area.Width);
        var h = FitSize(area.Height / 2, MinPx(MinHeightDip, monitor.Scale), area.Height);
        return new PxRect(area.Right - w, area.Top, w, h);
    }

    /// <summary>
    /// 저장된 위치를 복원한다. 저장 사각형이 가장 많이 걸친 모니터를 기준으로 크기를 최소~작업 영역 사이로 다듬고,
    /// 제목줄이 그 모니터 안에 충분히 보이도록 위치를 보정한다. 어느 모니터와도 만나지 않거나 제목줄을 잡을 수 없으면 null(첫 실행 규칙).
    /// </summary>
    public static PxRect? Restore(SavedPlacement saved, IReadOnlyList<MonitorArea> monitors)
    {
        if (saved.Width <= 0 || saved.Height <= 0 || monitors.Count == 0) return null;
        var rect = new PxRect(saved.Left, saved.Top, saved.Width, saved.Height);

        MonitorArea? best = null;
        long bestArea = 0;
        foreach (var m in monitors)
        {
            var a = PxRect.Intersect(rect, m.WorkArea).Area;
            if (a > bestArea) (best, bestArea) = (m, a);
        }
        if (best == null) return null;

        var area = best.WorkArea;
        var w = FitSize(rect.Width, MinPx(MinWidthDip, best.Scale), area.Width);
        var h = FitSize(rect.Height, MinPx(MinHeightDip, best.Scale), area.Height);

        // 제목줄(맨 위 32 DIP 띠)이 작업 영역에 충분히 걸쳐야 한다
        var title = new PxRect(rect.Left, rect.Top, w, MinPx(TitleBarDip, best.Scale));
        var visible = PxRect.Intersect(title, area);
        if (visible.Width < Math.Min(MinPx(MinVisibleTitleWidthDip, best.Scale), w) || visible.Height <= 0) return null;

        // 크기를 줄였거나 가장자리에 걸쳐 있으면 작업 영역 안으로 당긴다
        var left = Math.Clamp(rect.Left, area.Left, area.Right - w);
        var top = Math.Clamp(rect.Top, area.Top, area.Bottom - h);
        return new PxRect(left, top, w, h);
    }

    private static int MinPx(double dip, double scale) => (int)Math.Ceiling(dip * (scale > 0 ? scale : 1));

    /// <summary>값을 [min, max]로 맞춘다. 작업 영역이 최소 크기보다 작은 모니터면 작업 영역이 이긴다.</summary>
    private static int FitSize(int value, int min, int max) => Math.Min(Math.Max(value, min), max);
}
