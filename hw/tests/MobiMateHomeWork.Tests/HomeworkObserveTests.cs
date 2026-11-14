namespace MobiMateHomeWork.Tests;

/// <summary>숙제 관찰 트리거 (H6 1차 피드백 2): 로딩 복귀·맵 변경 직후 예약 관찰, 겹친 트리거 합침, 20초 주기 분리.</summary>
public class HomeworkObserveTests
{
    [Fact]
    public void MapChangeDetector_TriggersOnLoadingReturn_AndMapChange_Only()
    {
        var d = new MapChangeDetector();
        Assert.False(d.Observe(true, "콜헨"));          // 첫 관찰은 기준만 잡는다
        Assert.False(d.Observe(true, "콜헨"));
        Assert.False(d.Observe(true, null));            // 맵이 비어 읽혀도 마지막 맵 유지
        Assert.True(d.Observe(true, "던바튼"));         // 맵 변경
        Assert.False(d.Observe(false, ""));             // 로딩
        Assert.False(d.Observe(false, ""));
        Assert.True(d.Observe(true, "던바튼"));         // 로딩 → 복귀
        Assert.False(d.Observe(true, "던바튼"));
        d.Reset();
        Assert.False(d.Observe(true, "탈틴"));
    }

    /// <summary>가짜 시계: 대기를 테스트가 직접 풀어 준다.</summary>
    private sealed class FakeDelays
    {
        public readonly List<(TimeSpan Span, TaskCompletionSource Gate)> Waits = [];
        public Task Delay(TimeSpan span, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            lock (Waits) Waits.Add((span, tcs));
            return tcs.Task;
        }
        public int Count { get { lock (Waits) return Waits.Count; } }
        public TaskCompletionSource Gate(int i) { lock (Waits) return Waits[i].Gate; }
    }

    [Fact]
    public async Task DelayedBurst_RunsAtEachOffset_AndOverlappingRequestsMergeIntoOne()
    {
        var clock = new FakeDelays();
        var runs = 0;
        using var burst = new DelayedBurst([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)], _ => { Interlocked.Increment(ref runs); return Task.CompletedTask; }, clock.Delay);

        burst.Request();
        await Wait.UntilAsync(() => clock.Count == 1, "첫 대기");
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Waits[0].Span);

        burst.Request();                                  // 첫 대기 중 새 트리거: 앞의 것은 버리고 새로 시작
        await Wait.UntilAsync(() => clock.Count == 2, "합쳐진 새 요청의 첫 대기");
        Assert.True(clock.Gate(0).Task.IsCanceled);
        Assert.Equal(0, Volatile.Read(ref runs));

        clock.Gate(1).SetResult();                        // +2초
        await Wait.UntilAsync(() => Volatile.Read(ref runs) == 1, "첫 실행");
        await Wait.UntilAsync(() => clock.Count == 3, "둘째 대기");
        Assert.Equal(TimeSpan.FromSeconds(6), clock.Waits[2].Span);   // 8초 시점까지 남은 6초
        clock.Gate(2).SetResult();                        // +8초
        await Wait.UntilAsync(() => Volatile.Read(ref runs) == 2, "둘째 실행");
        Assert.Equal(2, burst.Requests);
        await Wait.StaysFalseAsync(() => Volatile.Read(ref runs) > 2, TimeSpan.FromMilliseconds(200), "끝난 요청이 더 실행됨");
    }

    private static Task<AppHarness> Start(FakeGame game, double periodic = 0) => AppHarness.StartAsync(game: game, configure: o =>
    {
        o.WatchIntervalSeconds = 0.15;
        o.HomeworkObserveIntervalSeconds = periodic;
        o.HomeworkTriggerFirstDelaySeconds = 0.1;
        o.HomeworkTriggerSecondDelaySeconds = 0.4;
    });

    [Fact]
    public async Task LoadingReturn_And_MapChange_ScheduleTwoObservationsEach_WithoutThePeriodicTimer()
    {
        using var game = new FakeGame();
        await using var h = await Start(game);
        var obs = h.Services.GetRequiredService<HomeworkObserver>();
        await h.Status.CheckNowAsync();
        await Wait.UntilAsync(() => h.Log.Headers.Any(x => !x.Selecting), "감시 시작");
        Assert.Equal(0, obs.Runs);                         // 주기 0 + 트리거 없음 → 관찰 없음

        // 로딩(캐릭터 선택창과 같은 빈 정보) → 복귀
        game.ShowCharacterSelect();
        await Task.Delay(500);                             // 감시가 로딩을 한 번 이상 읽게 둔다(관찰 창)
        Assert.Equal(0, obs.Runs);
        game.SetCharacter(Chars.Aira);
        await Wait.UntilAsync(() => obs.Runs >= 2, "로딩 복귀 뒤 +2초·+8초 관찰");

        // 같은 캐릭터의 맵 변경
        await Wait.UntilAsync(() => obs.BurstRequests == 1, "요청 1회");
        var before = obs.Runs;
        game.SetMap("던바튼");
        await Wait.UntilAsync(() => obs.BurstRequests == 2, "맵 변경 요청");
        await Wait.UntilAsync(() => obs.Runs >= before + 2, "맵 변경 뒤 관찰 두 번");
    }

    [Fact]
    public async Task PeriodicObservation_RunsOnItsOwnInterval_NotWithTheRecorder()
    {
        using var game = new FakeGame();
        await using var h = await AppHarness.StartAsync(game: game, configure: o => { o.HomeworkObserveIntervalSeconds = 0.15; o.RecordIntervalSeconds = 0; });
        var obs = h.Services.GetRequiredService<HomeworkObserver>();
        await h.Status.CheckNowAsync();
        await Wait.UntilAsync(() => obs.Runs >= 3, "숙제 전용 주기 관찰");
    }
}
