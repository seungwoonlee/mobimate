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

    private static List<CurrencyItem> WalletW(long gold, long wings, long nyang) => new() { new("데카", 11954), new("M캐시", 610), new("골드", gold), new("정령의 날개", wings), new("냥 토큰", nyang) };

    [Fact]
    public void OwnRecordIsNotMistakenForAnother_WhenGoldIsAlmostTheSame_ButWingsMatchOwn()
    {
        // 사고 재현 (2026-11-21): 듀얼블레이드(기록 골드 21,213,787)가 사냥으로 21,300,464가 됐는데 전사(21,308,096)와 0.04% 차이라
        // 다른 캐릭터 것으로 오인되어 재화 갱신이 막혔고, 은동전·마족 공물이 몇 시간째 옛 값으로 남았다.
        var m = new SnapshotManager(_dir);
        m.UpdateSnapshot(Info("아이라", "전사", 100, 90000), WalletW(21308096, 41000, 900000), null, "아이라_전사");
        m.UpdateSnapshot(Info("아이라", "듀얼블레이드", 100, 96000), WalletW(21213787, 59135, 1129000), null, "아이라_듀얼블레이드");
        Assert.False(m.WalletBelongsToOther("아이라_듀얼블레이드", WalletW(21300464, 59185, 1129117)));   // 날개·냥 토큰이 자기 것과 가깝다
        Assert.True(m.WalletBelongsToOther("아이라_듀얼블레이드", WalletW(21308096, 41010, 900100)));      // 전사 것과 같은 값이 읽히면 여전히 전사로 본다
    }

    [Fact]
    public void StaleCharacterInfo_WithNewCharactersGold_IsNotAJobSwapNorWrittenToTheOldRecord()
    {
        // 사고 재현 (2026-10-02): GalaxyZFlip(댄서) → GalaxyEdge(검술사)로 바꿨는데 재화만 먼저 바뀌어 Flip 기록에 Edge의 골드가 섞였다
        var m = new SnapshotManager(_dir);
        m.UpdateSnapshot(Info("에린", "댄서", 100, 98990), Wallet(7171, 2239, 31362959), null, "에린_댄서");
        m.UpdateSnapshot(Info("에린", "검술사", 100, 95116), Wallet(7171, 2239, 37568706), null, "에린_검술사");
        var edgeGold = Wallet(7238, 2239, 37568706);

        Assert.True(m.WalletBelongsToOther("에린_댄서", edgeGold));     // Flip 기록에 쓰면 안 된다
        Assert.False(m.WalletBelongsToOther("에린_검술사", edgeGold));
        Assert.False(m.WalletBelongsToOther("에린_댄서", Wallet(7238, 2239, 31362959)));   // Flip 자신의 골드

        // 이미 섞인 Flip 기록이 있어도 Edge의 골드는 직업 전환으로 보지 않는다
        m.UpdateSnapshot(Info("에린", "댄서", 100, 98990), Wallet(7238, 2239, 37568706), null, "에린_댄서");
        Assert.Null(m.DetectJobSwap("에린_댄서", Info("에린", "검술사", 100, 95116), edgeGold, edgeGold));
    }

    [Fact]
    public void PreviousReadAlreadyHoldingTheNewCharactersWallet_IsNotAJobSwap()
    {
        // 접속 인식 지연 재현 (2026-10-10): 재화가 캐릭터 정보보다 먼저 바뀌어 앞선 읽기(before)가 새 캐릭터 재화가 된 뒤
        // 직업이 읽히면 "같은 재화 + 다른 직업"이라 부직업 전환으로 오인해 새 캐릭터를 알아채지 못했다
        var m = new SnapshotManager(_dir);
        m.UpdateSnapshot(Info("에린", "격투가", 100, 88737), Wallet(10285, 5619, 11144590), null, "에린_격투가");
        var dancerWallet = Wallet(10285, 5619, 2310450);
        Assert.Null(m.DetectJobSwap("에린_격투가", Info("에린", "댄서", 100, 99961), dancerWallet, dancerWallet));
        // 진짜 부직업 전환은 그대로 알아본다 (재화가 기록과 같다)
        var same = Wallet(10285, 5619, 11144590);
        Assert.Equal("에린_격투가", m.DetectJobSwap("에린_격투가", Info("에린", "댄서", 100, 90000), same, same));
    }

    [Fact]
    public void SmallGold_IsNeverTreatedAsAnotherCharacters()
    {
        var m = new SnapshotManager(_dir);
        m.UpdateSnapshot(Info("에린", "댄서", 100), Wallet(1, 1, 500), null, "에린_댄서");
        m.UpdateSnapshot(Info("에린", "검술사", 100), Wallet(1, 1, 500), null, "에린_검술사");
        Assert.False(m.WalletBelongsToOther("에린_댄서", 500));
    }

    private static CharacterInfo WithWeight(double current, double max) =>
        new(null, "아이라", 100, "사제", new ScoreVal("전투력", 1000), null, null, null, null, null, null, null, null, null, null, null, null, null,
            new VitalsInfo(100, 100, current, max, 0, 0, 0));

    [Theory]
    [InlineData(869.4, 1080.0, true)]
    [InlineData(0.0, 3.40282347E+38, false)]   // 로딩·전환 중 게임이 주는 터무니없는 최대치
    [InlineData(10.0, 0.0, false)]
    [InlineData(-1.0, 1080.0, false)]
    [InlineData(double.NaN, 1080.0, false)]
    public void IsWeightSane(double current, double max, bool expected) =>
        Assert.Equal(expected, new VitalsInfo(100, 100, current, max, 0, 0, 0).IsWeightSane());

    [Fact]
    public void WeightSummary_HidesAbsurdReadings() =>
        Assert.Equal("알 수 없음", GameStateCache.WeightSummary(new VitalsInfo(100, 100, 0, 3.40282347E+38, 0, 0, 0)));

    [Fact]
    public void AbsurdWeightReading_DoesNotCorruptTheBaselineOrDelta()
    {
        var m = new SnapshotManager(_dir);
        m.UpdateSnapshot(WithWeight(0, 3.40282347E+38), null, null);                  // 첫 읽기가 비정상: 기준값 0으로 굳으면 안 된다
        var d = m.UpdateSnapshot(WithWeight(869.4, 1080), null, null);                // 정상 값이 들어와도 변화량이 +869로 튀지 않는다
        Assert.True(Math.Abs(d.WeightDiff) < 1, $"변화량이 튐: {d.WeightDiff}");
        var d2 = m.UpdateSnapshot(WithWeight(0, 3.40282347E+38), null, null);         // 중간의 비정상 읽기는 값을 0으로 덮지 않는다
        Assert.True(Math.Abs(d2.WeightDiff) < 1, $"변화량이 튐: {d2.WeightDiff}");
        Assert.Equal(869.4, d2.Current.WeightCurrent, 1);
    }
}
