namespace MobiMateHomeWork.Hosting;

/// <summary>
/// 기동 작업: WPF판(%APPDATA%\MobiMate) 기록 가져오기 — 웹앱과 같은 규칙이다.
/// 저장소에 없는 파일만 복사하고(SnapshotManager.ImportMissingFrom), 숙제 기록은 형식 변환이 필요해 따로 가져온다
/// (HomeworkStore.ImportFromWpfIfMissing: 숙제 기록 파일이 아직 없을 때만). 원본 폴더에는 쓰지 않는다.
/// 웹앱 폴더에서 복사하는 코드는 없다 — 같은 폴더를 공유하므로 이미 거기 있다.
/// </summary>
public sealed class StartupTasks(IServiceProvider sp, AppOptions options, ILogger<StartupTasks> log) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.WpfStorageDir) && Directory.Exists(options.WpfStorageDir))
        {
            var copied = sp.GetRequiredService<SnapshotManager>().ImportMissingFrom(options.WpfStorageDir);
            var hw = sp.GetRequiredService<HomeworkStore>().ImportFromWpfIfMissing(options.WpfStorageDir, sp.GetRequiredService<HomeworkCatalog>(), DateTimeOffset.UtcNow);
            if (copied > 0 || hw > 0) log.LogInformation("WPF판 기록 가져옴: 파일 {Files}개, 숙제 캐릭터 {Chars}개", copied, hw);
        }
        log.LogInformation("MobiMate Homework 시작 {Version}: 저장 폴더 {Dir}", AppInfo.Version, options.StorageDir);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

public static class AppInfo
{
    /// <summary>빌드 버전 원문 "1.0.MMdd.HHmm".</summary>
    public static string Version { get; } =
        (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AppInfo).Assembly)
            ?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
