using System;
using System.IO;
using Xunit;

namespace MobiMate.Tests;

/// <summary>캐릭터 선택창의 빈 정보는 기록하지 않는다 (v1.5 요청 1)</summary>
public class RealCharacterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-real-" + Guid.NewGuid().ToString("N"));
    public RealCharacterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static CharacterInfo Info(string? realm, string? job, int level, long combat = 1000) =>
        new(null, realm, level, job, new ScoreVal("전투력", combat), null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    [Theory]
    [InlineData("아이라", "격투가", 100, true)]
    [InlineData("", "", 0, false)]
    [InlineData(null, null, 0, false)]
    [InlineData("아이라", "격투가", 0, false)]
    [InlineData("아이라", "", 100, false)]
    [InlineData("  ", "격투가", 100, false)]
    public void IsRealCharacter(string? realm, string? job, int level, bool expected) =>
        Assert.Equal(expected, SnapshotManager.IsRealCharacter(Info(realm, job, level)));

    [Fact]
    public void NullCharacter_IsNotRealEither() => Assert.False(SnapshotManager.IsRealCharacter(null));

    [Fact]
    public void SelectScreenInfo_CreatesNoProfileAndNoHistory()
    {
        var m = new SnapshotManager(_dir);
        var d = m.UpdateSnapshot(Info("", "", 0, 0), new List<CurrencyItem> { new("골드", 123) }, null);
        Assert.Equal(0, d.CombatScoreDiff);
        Assert.Empty(m.GetAllProfiles());
        Assert.Null(m.GetProfile("에린", "밀레시안"));
        m.UpdateSnapshot(null, null, null);
        Assert.Empty(m.GetAllProfiles());
    }

    [Fact]
    public void RealCharacter_StillGetsAProfile()
    {
        var m = new SnapshotManager(_dir);
        m.UpdateSnapshot(Info("아이라", "격투가", 100, 88000), null, null);
        Assert.Single(m.GetAllProfiles());
    }
}
