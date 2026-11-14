using Xunit;

namespace MobiMate.Tests;

/// <summary>전투력 재확인 규칙 (FR-DT-10)</summary>
public class CombatScoreGuardTests
{
    [Fact]
    public void FirstReadingIsAccepted()
    {
        var g = new CombatScoreGuard();
        Assert.Equal(new CombatScoreGuard.Verdict(88000, false), g.Evaluate("아이라_격투가", 88000));
    }

    [Fact]
    public void LowerThanKnown_IsSuspect_AndKeepsTheKnownValue()
    {
        var g = new CombatScoreGuard();
        g.Evaluate("k", 100000);
        var v = g.Evaluate("k", 97000);
        Assert.True(v.Suspect);
        Assert.Equal(100000, v.Value);
        Assert.Equal(100000, g.Evaluate("k", 97000).Value);   // 확인 전에는 몇 번을 읽어도 이전 값
    }

    [Fact]
    public void HigherOrEqual_IsAcceptedAndRaisesTheKnownValue()
    {
        var g = new CombatScoreGuard();
        g.Evaluate("k", 100000);
        Assert.False(g.Evaluate("k", 100000).Suspect);
        Assert.False(g.Evaluate("k", 105000).Suspect);
        Assert.True(g.Evaluate("k", 100000).Suspect);          // 기준이 105000으로 올랐다
    }

    [Fact]
    public void ConfirmedLow_IsAcceptedAsTheNewKnownValue()
    {
        var g = new CombatScoreGuard();
        g.Evaluate("k", 100000);
        Assert.True(g.Evaluate("k", 96000).Suspect);
        g.ConfirmLow("k", 96000);
        var v = g.Evaluate("k", 96000);
        Assert.False(v.Suspect);
        Assert.Equal(96000, v.Value);
        Assert.False(g.Evaluate("k", 96000).Suspect);          // 이제 96000이 기준
        Assert.True(g.Evaluate("k", 95000).Suspect);           // 더 낮은 값은 다시 의심
    }

    [Fact]
    public void ConfirmedLow_DoesNotAcceptADifferentLowValue()
    {
        var g = new CombatScoreGuard();
        g.Evaluate("k", 100000);
        g.ConfirmLow("k", 96000);
        Assert.True(g.Evaluate("k", 97000).Suspect);
    }

    [Fact]
    public void SavedValue_IsTheBaselineAfterARestart()
    {
        var g = new CombatScoreGuard();
        var v = g.Evaluate("k", 97000, saved: 100000);
        Assert.True(v.Suspect);
        Assert.Equal(100000, v.Value);
    }

    [Fact]
    public void CharactersAreIndependent()
    {
        var g = new CombatScoreGuard();
        g.Evaluate("a", 100000);
        Assert.False(g.Evaluate("b", 50000).Suspect);
    }

    [Fact]
    public void RecheckRunsOncePerCharacterAtATime()
    {
        var g = new CombatScoreGuard();
        Assert.True(g.TryBeginRecheck("k"));
        Assert.False(g.TryBeginRecheck("k"));
        Assert.True(g.TryBeginRecheck("other"));
        g.EndRecheck("k");
        Assert.True(g.TryBeginRecheck("k"));
    }
}
