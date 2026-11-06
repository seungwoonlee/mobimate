using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MobiMate.Web.Voice;

/// <summary>
/// 이 앱과 게임의 권한(관리자 여부). 게임이 **관리자 권한(상승)** 으로 실행 중이고 이 앱이 일반 권한이면, Windows 보안 규칙(UIPI) 때문에
/// 게임 화면이 앞에 있는 동안에는 전역 단축키가 전달되지 않는다 — 앱 화면이 앞에 있을 때만 동작하는 것처럼 보인다 (2026-11-06 실측).
/// 이 앱도 관리자 권한으로 실행하면 해결된다.
/// </summary>
public sealed record ElevationInfo(bool App, bool? Game)
{
    /// <summary>게임은 관리자 권한인데 이 앱은 아니다: 게임 화면에서 단축키가 먹지 않는다</summary>
    public bool Mismatch => Game == true && !App;
}

public static class ElevationProbe
{
    /// <summary>게임 프로세스 이름 (MabinogiMobile.exe)</summary>
    public const string GameProcessName = "MabinogiMobile";

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public static bool IsAppElevated()
    {
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    /// <summary>게임이 실행 중이 아니거나 알 수 없으면 Game=null</summary>
    public static ElevationInfo Detect()
    {
        bool? game = null;
        try
        {
            var p = Process.GetProcessesByName(GameProcessName).FirstOrDefault();
            if (p != null) game = IsProcessElevated(p.Id);
        }
        catch { }
        return new ElevationInfo(IsAppElevated(), game);
    }

    /// <summary>프로세스 토큰의 상승(TokenElevation) 여부. 열 수 없으면 null</summary>
    public static bool? IsProcessElevated(int pid)
    {
        var process = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, 0x8, out var token)) return null;   // TOKEN_QUERY
            try { return GetTokenInformation(token, 20, out var elevated, sizeof(int), out _) ? elevated != 0 : null; }   // TokenElevation
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    /// <summary>지금 앞에 있는 창의 프로그램 이름 (진단 로그용). 알 수 없으면 null</summary>
    public static string? ForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out var pid);
            return pid == 0 ? null : Process.GetProcessById((int)pid).ProcessName;
        }
        catch { return null; }
    }
}
