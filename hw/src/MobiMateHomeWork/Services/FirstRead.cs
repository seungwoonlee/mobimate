namespace MobiMateHomeWork.Services;

/// <summary>
/// 시작 직후 첫 읽기. <c>Characters()</c>는 첫 <c>RecordNowAsync</c> 전에는 접속 중(IsCurrent) 표시가 모두 거짓이라,
/// 화면은 이 읽기가 끝날 때까지(성공이든 실패든) "읽는 중" 자리 표시를 보여 준다.
/// 게임에 연결돼 있지 않으면 읽을 것이 없으므로 곧바로 끝나고, 화면은 기록된 값을 바로 보여 준다.
/// 서비스 주기(기록 60초)를 기다리지 않으려고 앱이 한 번 직접 부른다 — AppServices에 넣지 않는다(테스트 하니스는 읽기 시점을 직접 정한다).
/// </summary>
public sealed class FirstRead(StatusMonitor status, CharacterSession session, ILogger<FirstRead>? log = null, TimeSpan? timeout = null)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private int _started;

    /// <summary>끝났는가(성공·실패·시간 초과 모두).</summary>
    public bool Done { get; private set; }

    public event Action? Completed;

    /// <summary>상태를 확인하고, 연결돼 있으면 접속 중 캐릭터를 한 번 읽어 기록한다. 두 번 불러도 한 번만 한다.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? DefaultTimeout);
            if (await status.CheckNowAsync(cts.Token) == GameConnection.Connected)
                await session.RecordNowAsync(cts.Token);
        }
        catch (OperationCanceledException) { log?.LogInformation("첫 읽기가 제한 시간 안에 끝나지 않아 기록된 값으로 먼저 보여 줍니다."); }
        catch (Exception ex) { log?.LogWarning(ex, "첫 읽기 실패"); }
        finally
        {
            Done = true;
            try { Completed?.Invoke(); } catch (Exception ex) { log?.LogDebug(ex, "첫 읽기 완료 알림 오류"); }
        }
    }
}
