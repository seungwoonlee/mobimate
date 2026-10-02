using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    [Fact]
    public void SameServerAndJob_WithDifferentLevelAndCurrencies_GetsSeparateProfiles_AndSurvivesARestart()
    {
        var m = new SnapshotManager(_dir);
        var accountA = new List<CurrencyItem> { new("데카", 10285), new("M캐시", 5619) };
        var accountB = new List<CurrencyItem> { new("데카", 777), new("M캐시", 888) };
        m.UpdateSnapshot(Info("아이라", "사제", 100, 90000), accountA, null);
        m.UpdateSnapshot(Info("아이라", "사제", 80, 50000), accountB, null);            // 레벨이 낮고 계정이 다르다 = 다른 캐릭터
        m.UpdateSnapshot(Info("아이라", "사제", 100, 91000), accountA, null);
        m.UpdateSnapshot(Info("아이라", "사제", 80, 51000), accountB, null);
        var keys = m.GetAllProfiles().Select(p => p.CharacterKey).OrderBy(k => k).ToList();
        Assert.Equal(new[] { "아이라_사제", "아이라_사제#2" }, keys);
        Assert.Equal(91000, m.GetSavedCombat("아이라_사제"));
        Assert.Equal(51000, m.GetSavedCombat("아이라_사제#2"));
        m.SaveSnapshotsNow();

        var again = new SnapshotManager(_dir);                                          // 다시 켜도 저장된 두 캐릭터가 그대로다
        Assert.Equal(91000, again.GetSavedCombat("아이라_사제"));
        Assert.Equal(51000, again.GetSavedCombat("아이라_사제#2"));
    }

    [Fact]
    public void SplitLatest_MovesTheNewestRecordToANewCharacter()
    {
        var m = new SnapshotManager(_dir);
        var cur = new List<CurrencyItem> { new("데카", 10285), new("M캐시", 5619) };
        m.UpdateSnapshot(Info("아이라", "사제", 100, 90000), cur, null);
        m.UpdateSnapshot(Info("아이라", "사제", 100, 99000), cur, null);                // 전투력이 달라져 기록이 하나 더 쌓인다
        Assert.Null(m.SplitLatest("없는_키"));
        var created = m.SplitLatest("아이라_사제");
        Assert.Equal("아이라_사제#2", created);
        Assert.Equal(99000, m.GetProfileByKey("아이라_사제#2")!.History.Last().CombatScore);
        Assert.Equal(90000, m.GetProfileByKey("아이라_사제")!.History.Last().CombatScore);
        Assert.Null(m.SplitLatest("아이라_사제#2"));                                    // 기록이 하나뿐이면 나눌 것이 없다
    }

    private static List<CurrencyItem> Wallet(long deca, long mcash, long gold) => new() { new("데카", deca), new("M캐시", mcash), new("골드", gold) };

    [Fact]
    public void SubJobSwap_IsNotAnotherAccountsCharacter_AndLeavesTheMainRecordUntouched()
    {
        // 사고 재현 (2026-10-02): GALAXYZ(검술사)가 화염술사로 잠시 바꾸자 다른 계정의 윈클라우드(화염술사)로 오인했다
        var m = new SnapshotManager(_dir);
        var galaxy = Wallet(7231, 2239, 51085371);
        m.UpdateSnapshot(Info("에린", "검술사", 100, 112131), galaxy, null, "에린_검술사");
        m.UpdateSnapshot(Info("에린", "화염술사", 100, 101718), Wallet(18944, 30, 14262936), null, "에린_화염술사");   // 윈클라우드

        var sub = Info("에린", "화염술사", 100, 105849);   // GALAXYZ가 화염술사로 전환
        Assert.Equal("에린_검술사", m.DetectJobSwap("에린_검술사", sub, galaxy, galaxy));
        Assert.True(m.IsSubJob("에린_검술사", sub));

        var before = m.GetProfileByKey("에린_검술사")!.History.Count;
        var d = m.UpdateSnapshot(sub, galaxy, null, "에린_검술사");
        Assert.Equal(0, d.CombatScoreDiff);
        var main = m.GetProfileByKey("에린_검술사")!;
        Assert.Equal(before, main.History.Count);
        Assert.Equal(112131, main.History[^1].CombatScore);
        Assert.Equal("검술사", main.JobName);
        Assert.Equal(14262936, m.GetProfileByKey("에린_화염술사")!.History[^1].Gold);   // 윈클라우드 기록도 그대로
    }

    [Fact]
    public void RealLoginAsAnotherCharacter_IsNotAJobSwap()
    {
        var m = new SnapshotManager(_dir);
        m.UpdateSnapshot(Info("에린", "검술사", 100, 112131), Wallet(7231, 2239, 51085371), null, "에린_검술사");
        var other = Info("에린", "화염술사", 100, 101718);
        Assert.Null(m.DetectJobSwap("에린_검술사", other, Wallet(18944, 30, 14262936), Wallet(7231, 2239, 51085371)));   // 다른 계정
        Assert.Null(m.DetectJobSwap("에린_검술사", other, Wallet(7231, 2239, 12000000), Wallet(7231, 2239, 51085371)));   // 같은 계정의 다른 캐릭터: 골드가 다르다
        Assert.False(m.IsSubJob("에린_검술사", Info("에린", "검술사", 100)));
    }
}
