using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace MobiMate.Core.Tests;

/// <summary>같은 서버·같은 직업이지만 다른 계정인 캐릭터를 구분한다 (승운 확인 2026-10-02)</summary>
public class CharacterIdentityTests
{
    private const string Base = "아이라_사제";
    private static readonly DateTime T = new(2026, 10, 2, 12, 0, 0);

    private static IdentityCandidate Cand(string key, int level, long living, long combat, long deca, long mcash, int ageH = 1) =>
        new(key, level, living, combat, deca, mcash, T.AddHours(-ageH));

    private static IdentityReading Read(int level, long living, long combat, long? deca, long? mcash) => new(level, living, combat, deca, mcash);

    [Fact]
    public void NoRecord_UsesTheBaseKey() =>
        Assert.Equal(Base, CharacterIdentity.Resolve(Base, new List<IdentityCandidate>(), Read(80, 1000, 50000, 777, 888)));

    [Fact]
    public void SameAccountValues_AreTheSameCharacter() =>
        Assert.Equal(Base, CharacterIdentity.Resolve(Base, new[] { Cand(Base, 100, 5000, 90000, 10285, 5619) }, Read(100, 5100, 91000, 10285, 5619)));

    [Fact]
    public void DifferentDecaAndMCash_MeansAnotherAccount_SoANewCharacter() =>
        Assert.Equal(Base + "#2", CharacterIdentity.Resolve(Base, new[] { Cand(Base, 100, 5000, 90000, 10285, 5619) }, Read(100, 5200, 60000, 777, 888)));   // 레벨·생활력은 문제없지만 데카·M캐시가 달라 다른 계정

    [Fact]
    public void LowerLevelThanTheRecord_IsAnotherCharacter_EvenWithoutCurrencies() =>
        Assert.Equal(Base + "#2", CharacterIdentity.Resolve(Base, new[] { Cand(Base, 100, 5000, 90000, 10285, 5619) }, Read(80, 1000, 50000, null, null)));

    [Fact]
    public void SpendingDeca_OnTheSameCharacter_IsNotASplit() =>
        Assert.Equal(Base, CharacterIdentity.Resolve(Base, new[] { Cand(Base, 100, 5000, 90000, 10285, 5619) }, Read(100, 5000, 91000, 8000, 5619)));   // 데카를 썼다: 레벨·전투력은 그대로

    [Fact]
    public void WithTwoCharacters_EachReadingFollowsItsOwnRecord()
    {
        var cands = new[]
        {
            Cand(Base, 100, 5000, 90000, 10285, 5619, ageH: 5),
            Cand(Base + "#2", 80, 1000, 50000, 777, 888, ageH: 1),
        };
        Assert.Equal(Base, CharacterIdentity.Resolve(Base, cands, Read(100, 5000, 90500, 10285, 5619)));
        Assert.Equal(Base + "#2", CharacterIdentity.Resolve(Base, cands, Read(80, 1000, 50500, 777, 888)));
        Assert.Equal(Base + "#2", CharacterIdentity.Resolve(Base, cands, Read(81, 1010, 51000, null, null)));   // 재화를 못 읽어도 레벨이 가까운 쪽
        Assert.Equal(Base, CharacterIdentity.Resolve(Base, cands, Read(101, 5050, 92000, null, null)));
    }

    [Fact]
    public void ThirdCharacter_GetsTheNextNumber()
    {
        var cands = new[] { Cand(Base, 100, 5000, 90000, 10285, 5619), Cand(Base + "#2", 80, 1000, 50000, 777, 888) };
        Assert.Equal(Base + "#3", CharacterIdentity.Resolve(Base, cands, Read(60, 500, 30000, 5000, 4000)));
    }

    [Fact]
    public void OtherServerOrJob_IsNeverMixed() =>
        Assert.Equal("바람_사제", CharacterIdentity.Resolve("바람_사제", new[] { Cand(Base, 100, 5000, 90000, 10285, 5619) }, Read(100, 5000, 90000, 10285, 5619)));

    [Fact]
    public void SameReading_NeedsSameLevelAndCloseScores()
    {
        CharacterInfo Ch(int lv, long combat, long living) => new(null, "아이라", lv, "사제", new ScoreVal(null, combat), new ScoreVal(null, living), null, null, null, null, null, null, null, null, null, null, null, null, null);
        Assert.True(CharacterIdentity.SameReading(Ch(100, 90000, 5000), Ch(100, 91000, 5020)));
        Assert.False(CharacterIdentity.SameReading(Ch(100, 90000, 5000), Ch(101, 90000, 5000)));
        Assert.False(CharacterIdentity.SameReading(Ch(100, 90000, 5000), Ch(100, 60000, 3000)));
        Assert.False(CharacterIdentity.SameReading(null, Ch(100, 90000, 5000)));
    }
}
