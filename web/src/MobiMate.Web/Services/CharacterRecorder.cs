using MobiMate.Web.Hosting;

namespace MobiMate.Web.Services;

/// <summary>
/// 접속 중인 캐릭터를 일정 간격으로 읽어 기록한다 (v1.5). 화면을 열어 두지 않아도 "마지막으로 본 시각"과
/// 보유량(은동전·마족 공물·데카·M캐시)이 최신으로 남아야 로그오프 뒤의 예상 보유량을 정확히 계산할 수 있다.
/// 게임에 접속 중일 때만 읽고, 간격이 0이면 끈다(테스트).
/// </summary>
public sealed class CharacterRecorder(GameViews views, StatusMonitor status, MobiMateOptions options, ILogger<CharacterRecorder> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.RecordInterval <= TimeSpan.Zero) return;
        using var timer = new PeriodicTimer(options.RecordInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (status.State != GameConnection.Connected) continue;
                try
                {
                    await views.RecordNowAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    log.LogDebug(ex, "캐릭터 기록 실패");
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
