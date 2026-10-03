using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MobiMate.Web.Lan;

/// <summary>
/// 폰 접속용 방화벽 상태. 윈도우 방화벽은 접속을 조용히 버려서 폰에서는 "응답 시간이 너무 오래 걸립니다"로만 보인다 (2026-10-03 실측).
/// 앱 경로 기준 허용 규칙은 이 PC처럼 적용되지 않는 경우가 있어 "포트 기준 허용 규칙"이 있을 때만 Ok로 본다.
/// Ok = 포트 허용 규칙 있음 / Missing = 포트 허용 규칙 없음(앱 경로 규칙만 있어도 폰이 안 열리면 allow-lan-firewall.bat) / Blocked = 이 포트·앱을 막는 규칙이 있음 / Unknown = 읽지 못함.
/// 규칙은 관리자 권한 없이 읽을 수 있다.
/// </summary>
public enum FirewallState { Ok, Missing, Blocked, Unknown }

public interface IFirewallCheck
{
    FirewallState Check(int port);
}

public sealed class WindowsFirewallCheck(Func<DateTimeOffset>? now = null) : IFirewallCheck
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private readonly object _lock = new();
    private (int Port, FirewallState State, DateTimeOffset At)? _cache;

    public FirewallState Check(int port)
    {
        lock (_lock)
        {
            if (_cache is { } c && c.Port == port && _now() - c.At < Ttl) return c.State;
            var s = Read(port);
            _cache = (port, s, _now());
            return s;
        }
    }

    private static FirewallState Read(int port)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return FirewallState.Unknown;
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type == null) return FirewallState.Unknown;
            dynamic policy = Activator.CreateInstance(type)!;
            var exe = Environment.ProcessPath;
            var rules = new List<FirewallRule>();
            foreach (dynamic r in policy.Rules)
            {
                try
                {
                    if (!(bool)r.Enabled || (int)r.Direction != 1) continue;   // 인바운드, 켜진 규칙만
                    rules.Add(new FirewallRule((int)r.Action == 1, (int)r.Profiles, (int)r.Protocol, (string?)r.LocalPorts, (string?)r.ApplicationName));
                }
                catch (COMException) { /* 읽을 수 없는 규칙은 건너뛴다 */ }
            }
            return Evaluate(rules, port, exe);
        }
        catch (Exception)
        {
            return FirewallState.Unknown;
        }
    }

    public sealed record FirewallRule(bool Allow, int Profiles, int Protocol, string? LocalPorts, string? Application);

    private const int Private = 2;

    /// <summary>규칙 목록으로 상태를 정한다 (테스트용으로 분리). Profiles는 비트(도메인 1·개인 2·공용 4), Protocol은 TCP 6·전체 256.</summary>
    public static FirewallState Evaluate(IEnumerable<FirewallRule> rules, int port, string? exePath)
    {
        var allowByPort = false;
        foreach (var r in rules)
        {
            if ((r.Profiles & Private) == 0) continue;               // 개인 네트워크에 적용되는 규칙만
            if (r.Protocol != 6 && r.Protocol != 256) continue;      // TCP 또는 전체
            var noApp = string.IsNullOrWhiteSpace(r.Application) || r.Application == "*";
            var appIsUs = !noApp && exePath != null && string.Equals(r.Application, exePath, StringComparison.OrdinalIgnoreCase);
            var covers = PortCovers(r.LocalPorts, port);
            if (!r.Allow && ((noApp && covers) || (appIsUs && covers))) return FirewallState.Blocked;
            if (r.Allow && noApp && covers) allowByPort = true;
        }
        return allowByPort ? FirewallState.Ok : FirewallState.Missing;
    }

    /// <summary>"*"·비어 있음 = 모든 포트. "17800", "80,443", "1000-2000" 형식.</summary>
    public static bool PortCovers(string? spec, int port)
    {
        if (string.IsNullOrWhiteSpace(spec) || spec == "*") return true;
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ends = part.Split('-', 2);
            if (int.TryParse(ends[0], out var lo))
            {
                var hi = ends.Length == 2 && int.TryParse(ends[1], out var h) ? h : lo;
                if (port >= lo && port <= hi) return true;
            }
        }
        return false;
    }
}

/// <summary>배포 폴더(exe 옆)의 방화벽 허용 배치 파일.</summary>
public static class FirewallScript
{
    public const string FileName = "allow-lan-firewall.bat";

    public static string? Find()
    {
        var p = Path.Combine(AppContext.BaseDirectory, FileName);
        return File.Exists(p) ? p : null;
    }

    /// <summary>관리자 승인 창(UAC)을 띄워 배치 파일을 실행한다. 시작하면 true.</summary>
    public static bool Run(string path, int port)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path, port.ToString()) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(path)! });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;   // 승인 창에서 취소한 경우 등
        }
    }
}
