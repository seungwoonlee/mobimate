using Xunit;

namespace MobiMate.Tests;

/// <summary>캐릭터 전환 직후 0으로 읽힌 생활력·매력은 마지막 기록을 쓴다</summary>
public class ScoreReadingTests
{
    [Theory]
    [InlineData(1500L, 1200L, 1500L)]   // 정상 값은 그대로
    [InlineData(0L, 1200L, 1200L)]      // 0은 못 읽은 값
    [InlineData(null, 1200L, 1200L)]    // 빈 값
    [InlineData(0L, null, 0L)]          // 기록도 없으면 0
    [InlineData(null, null, 0L)]
    public void ZeroOrMissingFallsBackToLastRecord(long? read, long? last, long expected) =>
        Assert.Equal(expected, ScoreReading.OrLast(read, last));
}
