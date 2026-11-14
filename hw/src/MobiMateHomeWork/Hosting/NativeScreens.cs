using System.Runtime.InteropServices;
using MobiMateHomeWork.Layout;
using Forms = System.Windows.Forms;

namespace MobiMateHomeWork.Hosting;

/// <summary>모니터·창 위치에 관한 Win32 호출 모음. 계산은 <see cref="WindowPlacement"/>가 하고, 여기서는 값을 읽고 쓰기만 한다.</summary>
internal static class NativeScreens
{
    private const int MdtEffectiveDpi = 0;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 2;

    /// <summary>연결된 모든 모니터의 작업 영역(물리 픽셀)과 배율.</summary>
    public static IReadOnlyList<MonitorArea> All() => Forms.Screen.AllScreens.Select(Of).ToList();

    /// <summary>마우스 커서가 있는 모니터(없으면 주 모니터). 사용자가 지금 보고 있는 화면이라고 본다.</summary>
    public static MonitorArea Current()
    {
        try { return Of(Forms.Screen.FromPoint(Forms.Cursor.Position)); }
        catch (Exception) { return Of(Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]); }
    }

    private static MonitorArea Of(Forms.Screen s)
    {
        var a = s.WorkingArea;
        return new MonitorArea(new PxRect(a.Left, a.Top, a.Width, a.Height), ScaleOf(a));
    }

    private static double ScaleOf(System.Drawing.Rectangle area)
    {
        try
        {
            var pt = new Point { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 };
            var monitor = MonitorFromPoint(pt, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dx, out _) == 0 && dx > 0)
                return dx / 96.0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return 1.0;
    }

    /// <summary>창의 현재 바깥 사각형(물리 픽셀).</summary>
    public static PxRect? BoundsOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
        return new PxRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>창을 물리 픽셀 사각형으로 옮긴다(활성화·순서 변경 없음).</summary>
    public static void Place(IntPtr hwnd, PxRect r) =>
        SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SwpNoZOrder | SwpNoActivate);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point pt, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
