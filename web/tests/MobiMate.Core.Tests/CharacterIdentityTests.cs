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

    // 2026-10-26 실제 사례: 울랄라힐러(아이라 서버 힐러). 한동안 접속하지 않은 사이 레벨이 85→91로 오르고 데카·M캐시도 달라졌는데
    // 레벨이 +2를 넘어 새 캐릭터("아이라_힐러#3")로 갈라졌다. 전투력 38,209→41,926(+9.7%)이라 같은 캐릭터다.
    private const string Healer = "아이라_힐러";

    [Fact]
    public void LevelUpWithChangedDeca_OnTheSameCharacter_IsNotASplit() =>
        Assert.Equal(Healer, CharacterIdentity.Resolve(Healer, new[] { Cand(Healer, 85, 1200, 38209, 5000, 3000), Cand(Healer + "#2", 100, 9000, 115297, 90000, 12000) }, Read(91, 1210, 41926, 5200, 3100)));

    [Fact]
    public void LevelUp_WithAHugeCombatJump_IsStillAnotherCharacter() =>
        Assert.Equal(Healer + "#2", CharacterIdentity.Resolve(Healer, new[] { Cand(Healer, 85, 1200, 38209, 5000, 3000) }, Read(91, 1210, 120000, 5200, 3100)));   // 전투력이 3배면 다른 캐릭터

    [Fact]
    public void TooManyLevelsApart_IsAnotherCharacter() =>
        Assert.Equal(Healer + "#2", CharacterIdentity.Resolve(Healer, new[] { Cand(Healer, 80, 1200, 38209, 5000, 3000) }, Read(95, 1210, 52000, 5200, 3100)));   // 15레벨 차이는 넘는다

    // 2026-10-12 실제 사례: 아이라 서버 전사. 생활력이 1.1% 줄고 데카가 달라졌지만 같은 캐릭터였다
    private const string Warrior = "아이라_전사";

    [Fact]
    public void RealCase_LivingDroppedSlightly_AndDecaDiffers_StillTheSameCharacter() =>
        Assert.Equal(Warrior, CharacterIdentity.Resolve(Warrior, new[] { Cand(Warrior, 100, 18939, 105479, 13682, 910) },
            Read(100, 18733, 102926, 13982, 910)));   // 전투력 97.6%, 생활력 98.9%

    [Fact]
    public void LivingDropWithinTenPercent_IsTolerated_ButBeyondIsAnotherCharacter()
    {
        var cands = new[] { Cand(Base, 100, 5000, 90000, 10285, 5619) };
        Assert.Equal(Base, CharacterIdentity.Resolve(Base, cands, Read(100, 4500, 90000, 10285, 5619)));       // 정확히 10% 하락까지
        Assert.Equal(Base + "#2", CharacterIdentity.Resolve(Base, cands, Read(100, 4499, 90000, 10285, 5619)));   // 10% 초과 하락
    }

    [Fact]
    public void ReallyDifferentCharacter_SameJob_IsStillANewOne() =>
        Assert.Equal(Warrior + "#2", CharacterIdentity.Resolve(Warrior, new[] { Cand(Warrior, 100, 18939, 105479, 13682, 910) },
            Read(100, 9000, 60000, 777, 888)));   // 생활력·전투력이 크게 다르고 데카·M캐시도 다르다

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

    private static readonly Wallet Main = new(7231, 2239, 51085371);

    [Fact]
    public void JobSwap_SameGoldDecaMCash_IsTheSameCharacter()
    {
        Assert.True(CharacterIdentity.IsJobSwap("아이라", "검술사", "아이라", "화염술사", Main, Main));
        Assert.True(CharacterIdentity.IsJobSwap("아이라", "검술사", "아이라", "화염술사", Main, Main with { Gold = 51085371 - 1000 }));   // 골드 0.1% 이내 변동
    }

    [Fact]
    public void JobSwap_NotWhenWalletDiffers_OrJobSame_OrNoWallet()
    {
        Assert.False(CharacterIdentity.IsJobSwap("아이라", "검술사", "아이라", "화염술사", Main, Main with { Gold = 14263039 }));   // 같은 계정의 다른 캐릭터: 골드가 다르다
        Assert.False(CharacterIdentity.IsJobSwap("아이라", "검술사", "아이라", "화염술사", Main, new Wallet(18944, 30, 14263039)));   // 다른 계정
        Assert.False(CharacterIdentity.IsJobSwap("아이라", "검술사", "아이라", "검술사", Main, Main));
        Assert.False(CharacterIdentity.IsJobSwap("아이라", "검술사", "바람", "화염술사", Main, Main));
        Assert.False(CharacterIdentity.IsJobSwap("아이라", "검술사", "아이라", "화염술사", null, Main));
        Assert.False(CharacterIdentity.IsJobSwap("아이라", "검술사", "아이라", "화염술사", new Wallet(0, 0, 0), new Wallet(0, 0, 0)));
    }

    [Fact]
    public void KeepTitle_BlankReadKeepsTheKnownTitle_UntilANewOneIsConfirmed()
    {
        CharacterInfo Ch(string? title) => new(title, "아이라", 100, "사제", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        Assert.Equal("이거밖에 안 되심?", CharacterIdentity.KeepTitle(Ch(""), "이거밖에 안 되심?").Title);
        Assert.Equal("이거밖에 안 되심?", CharacterIdentity.KeepTitle(Ch(null), "이거밖에 안 되심?").Title);
        Assert.Equal("  ", CharacterIdentity.KeepTitle(Ch("  "), null).Title);                        // 알던 칭호가 없으면 그대로
        Assert.Equal("새 칭호", CharacterIdentity.KeepTitle(Ch("새 칭호"), "이거밖에 안 되심?").Title);   // 바뀐 것이 확인되면 바꾼다
    }
}
