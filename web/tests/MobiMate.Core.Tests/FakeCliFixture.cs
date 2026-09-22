using System.Globalization;

namespace MobiMate.Tests;

/// <summary>
/// 빌드된 가짜 CLI(tools/FakeCli → MabinogiMobile_CLI.exe)를 찾고, 테스트마다 독립된 로그·정지 파일 경로를 준다.
/// 가짜 CLI는 환경변수로 제어하므로, 이 클래스를 쓰는 테스트는 같은 컬렉션에서 순차 실행한다.
/// </summary>
public sealed class FakeCliEnv : IDisposable
{
    private static readonly string[] Vars =
    {
        "FAKECLI_LOG", "FAKECLI_STOPFILE", "FAKECLI_STATE", "FAKECLI_SAMPLES_DIR",
        "FAKECLI_DELAY_EXECUTE_GATHERING", "FAKECLI_DELAY_GET_MY_INFO", "FAKECLI_DELAY_STOP_ACTION",
        "FAKECLI_FAIL_GET_CURRENCIES", "FAKECLI_BADJSON_GET_CURRENCIES", "FAKECLI_DELAY_GET_CURRENCIES", "FAKECLI_DELAY_GET_ACTIVITY",
    };

    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "mm-fakecli-" + Guid.NewGuid().ToString("N"));
    public string LogPath => Path.Combine(Dir, "calls.log");
    public string StopFile => Path.Combine(Dir, "stop.flag");
    public string ExePath { get; } = Locate();

    // 빌드 직후 첫 실행은 .NET 콜드 스타트로 수백 ms~1초가 더 걸려 시간 단언(정지 1초 이내 등)이 흔들린다.
    // 테스트 전에 한 번 띄워 디스크 캐시·JIT를 데운다.
    private static readonly Lazy<bool> WarmUp = new(() =>
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Locate(), "status")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        p.WaitForExit(10_000);
        return true;
    });

    public FakeCliEnv()
    {
        _ = WarmUp.Value;
        Directory.CreateDirectory(Dir);
        foreach (var v in Vars) Environment.SetEnvironmentVariable(v, null);
        Set("FAKECLI_LOG", LogPath);
        Set("FAKECLI_STOPFILE", StopFile);
    }

    public void Set(string name, string? value) => Environment.SetEnvironmentVariable(name, value);

    public GameCli CreateCli() => new(ExePath);

    public sealed record Call(DateTime At, int Pid, string Phase, string Command, string Args);

    public IReadOnlyList<Call> ReadCalls()
    {
        if (!File.Exists(LogPath)) return Array.Empty<Call>();
        return File.ReadAllLines(LogPath)
            .Where(l => l.Length > 0)
            .Select(l => l.Split('\t'))
            .Select(p => new Call(DateTime.Parse(p[0], null, DateTimeStyles.RoundtripKind), int.Parse(p[1]), p[2], p[3], p.Length > 4 ? p[4] : ""))
            .ToList();
    }

    public void Dispose()
    {
        foreach (var v in Vars) Environment.SetEnvironmentVariable(v, null);
        try { Directory.Delete(Dir, recursive: true); } catch { }
    }

    private static string Locate()
    {
        // tests/MobiMate.Core.Tests/bin/<Config>/net8.0 → web/tools/FakeCli/bin/<Config>/net8.0
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var tfm = Path.GetFileName(baseDir);
        var config = Path.GetFileName(Path.GetDirectoryName(baseDir)!);
        var dir = new DirectoryInfo(baseDir);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "FakeCli"))) dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("web/tools/FakeCli 폴더를 찾지 못했습니다.");
        var exe = Path.Combine(dir.FullName, "tools", "FakeCli", "bin", config, tfm, "MabinogiMobile_CLI.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("가짜 CLI가 빌드되지 않았습니다. 솔루션 전체를 빌드하세요.", exe);
        return exe;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FakeCliCollection
{
    public const string Name = "FakeCli";
}

/// <summary>전역 정적 상태(ChatText.Mode 등)를 바꾸는 테스트. 다른 테스트와 겹치지 않게 따로 돈다.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalStateCollection
{
    public const string Name = "GlobalState";
}
