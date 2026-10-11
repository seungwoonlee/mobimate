namespace MobiMateHomeWork.Tests;

/// <summary>
/// 전투력 재확인 (FR-DT-10): 로딩 순간에 낮게 읽힌 전투력은 이전 값을 유지하다가 다시 읽어 확인한다.
/// 웹앱 CombatRecheckTests를 서비스 호출로 다시 씀. 재확인은 화면을 갱신하려고 HeaderChanged를 한 번 더 올리므로 그 알림을 기다린다(고정 대기 없음).
/// 앱 종료(ApplicationStopping)에서 재확인이 멈추는지(H2a 위험 지점 b)도 본다.
/// </summary>
public class CombatRecheckTests
{
    private static Task<AppHarness> Start(double recheckDelaySeconds) =>
        AppHarness.StartAsync(configure: o => o.CombatRecheckDelaySeconds = recheckDelaySeconds);

    private static async Task<long> Combat(AppHarness h) => (await h.Session.HeaderAsync(CancellationToken.None)).Value!.Combat;

    [Fact]
    public async Task TransientDip_KeepsPreviousValue_AndNeverShowsTheLowOne()
    {
        await using var h = await Start(0.4);
        h.Game.SetCombat(100000);
        Assert.Equal(100000, await Combat(h));
        var before = h.Log.Headers.Count;

        h.Game.SetCombat(97000);                              // 로딩 순간: 3,000 낮게 읽힘
        Assert.Equal(100000, await Combat(h));                // 바로는 믿지 않고 이전 값을 보여 준다
        Assert.Equal(100000, h.State.Character!.CombatScore!.Value);   // 스탯 화면(원본 캐릭터 정보)도 같다
        h.Game.SetCombat(100000);                             // 다시 읽을 때는 정상

        // 재확인이 끝나면 화면 갱신용 알림이 한 번 더 온다(직접 부른 헤더 알림 1 + 재확인 알림 1)
        await Wait.UntilAsync(() => h.Log.Headers.Count >= before + 2, "재확인 뒤 헤더 갱신");
        Assert.All(h.Log.Headers, x => Assert.Equal(100000, x.Combat));   // 낮은 값은 화면에 한 번도 나오지 않았다
        Assert.Equal(100000, await Combat(h));
    }

    [Fact]
    public async Task RealDrop_IsAcceptedAfterTheRecheckStaysLow()
    {
        await using var h = await Start(0.1);
        h.Game.SetCombat(100000);
        Assert.Equal(100000, await Combat(h));

        h.Game.SetCombat(96000);                              // 장비를 바꿔 실제로 낮아짐
        Assert.Equal(100000, await Combat(h));                // 처음에는 이전 값

        // 다시 읽어도 같아 실제 값으로 받아들임 (재확인이 알린 헤더에 새 값이 실린다)
        await Wait.UntilAsync(() => h.Log.Headers.Any(x => x.Combat == 96000), "실제 하락이 받아들여짐", TimeSpan.FromSeconds(10));
        Assert.Equal(96000, await Combat(h));                 // 이후 읽기도 새 값을 유지
    }

    [Fact]
    public async Task HigherValue_IsAcceptedImmediately()
    {
        await using var h = await Start(0.1);
        h.Game.SetCombat(100000);
        Assert.Equal(100000, await Combat(h));
        h.Game.SetCombat(105000);
        Assert.Equal(105000, await Combat(h));
    }

    [Fact]
    public async Task Recheck_StopsAtApplicationStopping()
    {
        await using var h = await Start(60);                  // 재확인이 60초 뒤에 읽도록: 멈추지 않으면 오래 매달린다
        var guard = h.Services.GetRequiredService<CombatScoreGuard>();
        h.Game.SetCombat(100000);
        Assert.Equal(100000, await Combat(h));
        h.Game.SetCombat(97000);
        Assert.Equal(100000, await Combat(h));                // 의심 값 → 재확인이 시작되어 기다리는 중

        Assert.False(guard.TryBeginRecheck("아이라_격투가"), "재확인이 진행 중이어야 한다");
        var headers = h.Log.Headers.Count;

        // 앱 종료: ApplicationStopping이 올라오면 재확인이 즉시 멈추고 정리된다(60초를 기다리지 않는다)
        var stopped = h.Host.StopAsync();
        await Wait.UntilAsync(() => guard.TryBeginRecheck("아이라_격투가"), "재확인 정리(EndRecheck)", TimeSpan.FromSeconds(5));
        await stopped;
        Assert.Equal(headers, h.Log.Headers.Count);           // 멈춘 재확인은 헤더를 다시 읽어 알리지 않는다
    }
}
