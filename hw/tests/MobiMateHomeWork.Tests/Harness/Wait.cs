namespace MobiMateHomeWork.Tests;

/// <summary>조건 대기: 고정 Sleep 대신 조건이 참이 될 때까지 짧게 확인하고, 제한 시간을 넘기면 실패한다.</summary>
public static class Wait
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(15);

    public static async Task UntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        var end = DateTime.UtcNow + (timeout ?? Default);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException($"조건을 기다리다 시간이 지났습니다: {what}");
            await Task.Delay(20);
        }
    }

    /// <summary>"일어나지 않아야 하는 일"을 확인할 때: 일정 시간 동안 조건이 계속 거짓인지 본다(그 시간은 판정이 아니라 관찰 창이다).</summary>
    public static async Task StaysFalseAsync(Func<bool> condition, TimeSpan window, string what)
    {
        var end = DateTime.UtcNow + window;
        while (DateTime.UtcNow < end)
        {
            Assert.False(condition(), what);
            await Task.Delay(20);
        }
    }
}
