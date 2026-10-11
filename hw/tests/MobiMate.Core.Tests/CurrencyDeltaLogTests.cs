using Xunit;

namespace MobiMate.Tests;

/// <summary>레이드 보상 실측용 재화 변화 기록</summary>
public class CurrencyDeltaLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cdl-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "c.jsonl");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static List<CurrencyItem> L(long a, long b) => new() { new("원정의 증거: 카브락 레이드", a), new("골드", b) };

    [Fact]
    public void FirstObservationOnlySetsBaseline()
    {
        var log = new CurrencyDeltaLog(File_);
        Assert.Null(log.Observe("k", L(10, 5)));
        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void RecordsOnlyChangedCurrencies()
    {
        var log = new CurrencyDeltaLog(File_);
        log.Observe("k", L(10, 5));
        var ch = log.Observe("k", L(28, 5), "카브락 레이드");
        var c = Assert.Single(ch!);
        Assert.Equal(new CurrencyChange("원정의 증거: 카브락 레이드", 10, 28), c);
        Assert.Contains("\"diff\":18", File.ReadAllText(File_));
        Assert.Null(log.Observe("k", L(28, 5)));   // 변화 없음
    }

    [Fact]
    public void CharacterSwitchResetsBaselineAndEmptyListIsIgnored()
    {
        var log = new CurrencyDeltaLog(File_);
        log.Observe("a", L(10, 5));
        Assert.Null(log.Observe("b", L(99, 99)));   // 다른 캐릭터: 비교하지 않는다
        Assert.Null(log.Observe("b", new List<CurrencyItem>()));   // 빈 목록은 기준을 지우지 않는다
        Assert.Single(log.Observe("b", L(99, 100))!);
    }
}
