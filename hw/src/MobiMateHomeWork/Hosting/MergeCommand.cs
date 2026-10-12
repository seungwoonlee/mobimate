using Microsoft.Extensions.Logging.Abstractions;

namespace MobiMateHomeWork.Hosting;

/// <summary>
/// 일회 실행 모드 <c>--merge-character &lt;from&gt; &lt;into&gt; [--storage &lt;폴더&gt;]</c>: 창 없이 저장소만 열어 from 기록을 into로 합치고
/// 결과를 로그(&lt;저장 폴더&gt;\logs\homework-*.log)와 종료 코드로 알리고 끝난다. 단일 인스턴스 검사는 건너뛴다.
/// 같은 저장 폴더를 쓰는 프로그램(숙제 앱·웹앱)이 떠 있으면 합치지 않고 거부한다("먼저 끄세요") — 메모리에 든 옛 기록이 합친 결과를 덮어쓰지 않게.
/// 기본 저장 폴더는 실제 폴더(%APPDATA%\MobiMateWeb)다. 시험은 반드시 <c>--storage</c>로 임시 폴더를 준다.
/// </summary>
public static class MergeCommand
{
    public const string Switch = "--merge-character";

    public const int Ok = 0, Failed = 1, Usage = 2, AppRunning = 3, MergeFailed = 4;

    public static bool IsRequested(IEnumerable<string> args) => args.Any(a => a.Equals(Switch, StringComparison.OrdinalIgnoreCase));

    public sealed record Parsed(string From, string Into, string StorageDir);

    /// <summary>실행 인자를 읽는다. 맞지 않으면 null과 이유.</summary>
    public static Parsed? Parse(string[] args, out string? error)
    {
        error = null;
        var i = Array.FindIndex(args, a => a.Equals(Switch, StringComparison.OrdinalIgnoreCase));
        static bool IsValue(string[] a, int k) => k < a.Length && !a[k].StartsWith("--", StringComparison.Ordinal);
        if (i < 0 || !IsValue(args, i + 1) || !IsValue(args, i + 2))
        {
            error = $"사용법: {Switch} <합칠 캐릭터 키(사라짐)> <남길 캐릭터 키> [--storage <폴더>]  (예: {Switch} 아이라_전사#2 아이라_전사)";
            return null;
        }
        string? storage = null;
        for (var k = 0; k < args.Length; k++)
        {
            if (args[k].Equals("--storage", StringComparison.OrdinalIgnoreCase) && IsValue(args, k + 1)) storage = args[k + 1];
            else if (args[k].StartsWith("--storage=", StringComparison.OrdinalIgnoreCase)) storage = args[k]["--storage=".Length..];
        }
        var dir = Path.GetFullPath(string.IsNullOrWhiteSpace(storage) ? SnapshotManager.DefaultStorageDirectory : storage);
        return new Parsed(args[i + 1], args[i + 2], dir);
    }

    /// <summary>같은 저장 폴더를 쓰는 프로그램이 떠 있는가: 폴더의 공유 뮤텍스(웹앱·숙제 앱이 저장소를 열면 만든다)가 이미 있으면 떠 있는 것이다.</summary>
    public static bool IsStorageInUse(string storageDir)
    {
        try
        {
            if (Mutex.TryOpenExisting(SharedStorageLock.MutexNameFor(storageDir), out var m)) { m.Dispose(); return true; }
            // 실제 저장 폴더라면 숙제 앱의 단일 인스턴스 뮤텍스도 본다(공유 뮤텍스를 만들지 못한 예외 상황 대비)
            if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(storageDir)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(SnapshotManager.DefaultStorageDirectory)), StringComparison.OrdinalIgnoreCase)
                && Mutex.TryOpenExisting(SingleInstance.MutexName, out var s)) { s.Dispose(); return true; }
        }
        catch (UnauthorizedAccessException) { return true; }   // 열 수 없는 뮤텍스가 있다 = 다른 프로그램이 쥐고 있다
        catch (Exception ex) when (ex is IOException or WaitHandleCannotBeOpenedException) { }
        return false;
    }

    /// <summary>합치기를 실행하고 종료 코드를 돌려준다 (0 성공 또는 합칠 것 없음, 2 사용법, 3 프로그램 실행 중, 4 합치기 실패, 1 예상 밖 오류).</summary>
    public static int Run(string[] args, Action<string>? output = null)
    {
        void Say(string m) { try { output?.Invoke(m); } catch { } }
        var parsed = Parse(args, out var error);
        if (parsed == null) { Say(error!); return Usage; }

        FileLoggerProvider? logs = null;
        ILogger log = NullLogger.Instance;
        if (Directory.Exists(parsed.StorageDir))
        {
            logs = new FileLoggerProvider(Path.Combine(parsed.StorageDir, "logs"));
            log = logs.CreateLogger("MobiMateHomeWork.Merge");
        }
        try
        {
            log.LogInformation("캐릭터 합치기 요청(일회 실행): {From} → {Into} (저장 폴더 {Dir})", parsed.From, parsed.Into, parsed.StorageDir);
            if (!Directory.Exists(parsed.StorageDir))
            {
                var m = $"저장 폴더가 없습니다: {parsed.StorageDir}";
                log.LogError("{Message}", m); Say(m);
                return MergeFailed;
            }
            if (IsStorageInUse(parsed.StorageDir))
            {
                const string m = "같은 저장 폴더를 쓰는 숙제 앱 또는 웹앱이 실행 중입니다. 먼저 끄세요. (실행 중에 합치면 메모리의 옛 기록이 결과를 덮어쓸 수 있습니다.)";
                log.LogWarning("{Message}", m); Say(m);
                return AppRunning;
            }

            var result = new CharacterMerger(parsed.StorageDir).Merge(parsed.From, parsed.Into);
            if (result.Ok)
            {
                // 숙제 앱 전용 부가 정보(마지막 맵·가방 최대 무게)도 따라 옮긴다 (실패해도 합치기 결과는 그대로)
                try { new AppSettingsStore(parsed.StorageDir).MoveCharacter(parsed.From, parsed.Into); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.LogDebug(ex, "앱 설정의 캐릭터 부가 정보를 옮기지 못했습니다"); }
                log.LogInformation("캐릭터 합치기 완료: {Message}", result.Message);
                Say("완료: " + result.Message);
                return Ok;
            }
            log.LogError("캐릭터 합치기 실패: {Message}", result.Message);
            Say("실패: " + result.Message);
            return MergeFailed;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "캐릭터 합치기 중 예상 밖 오류");
            Say("오류: " + ex.Message);
            return Failed;
        }
    }
}
