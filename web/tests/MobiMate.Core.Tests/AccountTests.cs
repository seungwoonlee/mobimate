using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace MobiMate.Tests;

/// <summary>은동전·마족 공물 예상 보유량 (v1.5 요청 5)</summary>
public class CoinForecastTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Silver_GainsOnePer30Minutes()
    {
        var s = CoinForecast.Silver(10, Now.AddMinutes(-95), Now, member: true);   // 95분 = 3개 (남은 5분)
        Assert.Equal(13, s.Expected);
        Assert.Equal(150, s.Cap);
        Assert.Equal(CoinLevel.Ok, s.Level);
        Assert.Equal((150 - 13) * 30 - 5, s.MinutesToFull, 3);
    }

    [Fact]
    public void Tribute_GainsOnePer12Hours()
    {
        var t = CoinForecast.Tribute(3, Now.AddHours(-36), Now, member: true);     // 36시간 = 3개
        Assert.Equal(6, t.Expected);
        Assert.Equal(15, t.Cap);
    }

    [Theory]
    [InlineData(true, 150, 15)]
    [InlineData(false, 100, 10)]
    public void MembershipDecidesTheCaps(bool member, long silverCap, long tributeCap)
    {
        Assert.Equal(silverCap, CoinForecast.SilverCap(member));
        Assert.Equal(tributeCap, CoinForecast.TributeCap(member));
    }

    [Fact]
    public void ChargingStopsAtTheCap_AndAlreadyFullStaysFull()
    {
        var long_ago = Now.AddDays(-30);
        var s = CoinForecast.Silver(140, long_ago, Now, member: true);
        Assert.Equal(150, s.Expected);                       // 상한까지만
        Assert.Equal(CoinLevel.Full, s.Level);
        Assert.Equal(0, s.MinutesToFull);

        var over = CoinForecast.Silver(170, long_ago, Now, member: true);   // 이미 상한 이상: 늘지도 줄지도 않는다
        Assert.Equal(170, over.Expected);
        Assert.Equal(CoinLevel.Full, over.Level);
        Assert.Equal(1.0, over.Percent);

        var free = CoinForecast.Silver(100, Now.AddHours(-1), Now, member: false);
        Assert.Equal(100, free.Expected);                    // 미가입: 100에서 멈춘다
        Assert.Equal(CoinLevel.Full, free.Level);
    }

    [Theory]
    [InlineData(120, CoinLevel.Ok)]      // 150의 80% = 경고 없음
    [InlineData(121, CoinLevel.Ok)]      // 80.67%
    [InlineData(122, CoinLevel.Near)]    // 81.33% 부터 경고
    [InlineData(149, CoinLevel.Near)]
    [InlineData(150, CoinLevel.Full)]
    public void NearStartsAt81PercentOfTheCap(long held, CoinLevel level) =>
        Assert.Equal(level, CoinForecast.Silver(held, Now, Now, member: true).Level);

    [Theory]
    [InlineData(12, 15, CoinLevel.Ok)]     // 80% = 경고 없음
    [InlineData(13, 15, CoinLevel.Near)]   // 86.7%
    [InlineData(8, 10, CoinLevel.Ok)]      // 미가입 10의 80% = 경고 없음
    [InlineData(9, 10, CoinLevel.Near)]    // 90%
    [InlineData(80, 100, CoinLevel.Ok)]
    [InlineData(81, 100, CoinLevel.Near)]
    public void TributeThresholds(long held, long cap, CoinLevel level)
    {
        var s = CoinForecast.Forecast(held, Now, Now, CoinForecast.TributeStep, cap);
        Assert.Equal(level, s.Level);
    }

    [Fact]
    public void AClockThatMovedBackwardsGainsNothing()
    {
        Assert.Equal(10, CoinForecast.Silver(10, Now.AddHours(1), Now, member: true).Expected);
    }

    [Fact]
    public void ReachingTheCapDuringTheWait_IsFullWithNoWaitLeft()
    {
        var s = CoinForecast.Silver(148, Now.AddMinutes(-61), Now, member: true);  // 2개 충전 → 150
        Assert.Equal(150, s.Expected);
        Assert.Equal(CoinLevel.Full, s.Level);
        Assert.Equal(0, s.MinutesToFull);
    }
}

/// <summary>계정 묶기 (v1.5 요청 3): 데카·M캐시가 같았던 캐릭터끼리 같은 계정</summary>
public class AccountGroupingTests
{
    private static CharacterCurrencyHistory H(string key, params (long, long)[] pairs) => new(key, pairs);

    [Fact]
    public void SamePair_GroupsCharacters()
    {
        var d = new AccountData();
        Assert.True(AccountGrouper.Reconcile(d, new[] { H("a", (1000, 500)), H("b", (1000, 500)), H("c", (777, 888)) }));
        Assert.Equal(AccountGrouper.AccountOf(d, "a"), AccountGrouper.AccountOf(d, "b"));
        Assert.StartsWith("solo:", AccountGrouper.AccountOf(d, "c"));   // 묶이지 않은 캐릭터는 혼자
        Assert.False(AccountGrouper.Reconcile(d, new[] { H("a", (1000, 500)), H("b", (1000, 500)), H("c", (777, 888)) }));   // 다시 해도 변화 없음
    }

    [Fact]
    public void OnceGrouped_StaysGroupedEvenWhenTheValuesDriftApart()
    {
        var d = new AccountData();
        AccountGrouper.Reconcile(d, new[] { H("a", (1000, 500)), H("b", (1000, 500)) });
        var acc = AccountGrouper.AccountOf(d, "a");
        // 그 뒤 a만 접속해 값이 달라졌다: b는 아직 동기화 전이지만 같은 계정이다
        AccountGrouper.Reconcile(d, new[] { H("a", (1000, 500), (1200, 520)), H("b", (1000, 500)) });
        Assert.Equal(acc, AccountGrouper.AccountOf(d, "a"));
        Assert.Equal(acc, AccountGrouper.AccountOf(d, "b"));
    }

    [Fact]
    public void LinkedThroughAnEarlierMoment_EvenIfTheLatestValuesDiffer()
    {
        var d = new AccountData();
        AccountGrouper.Reconcile(d, new[] { H("a", (1200, 520), (1000, 500)), H("b", (900, 480), (1000, 500)) });   // 과거 기록에서 같은 값이 있었다
        Assert.Equal(AccountGrouper.AccountOf(d, "a"), AccountGrouper.AccountOf(d, "b"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]      // 값이 작고 한쪽만 있으면 우연히 같을 수 있다
    [InlineData(0, 50)]
    [InlineData(40, 40)]     // 둘 다 있으면 근거가 된다 → 아래 테스트에서 묶임
    public void WeakValuesAreNotEvidenceUnlessBothArePresent(long deca, long mcash)
    {
        var d = new AccountData();
        AccountGrouper.Reconcile(d, new[] { H("a", (deca, mcash)), H("b", (deca, mcash)) });
        var grouped = AccountGrouper.AccountOf(d, "a") == AccountGrouper.AccountOf(d, "b") && !AccountGrouper.AccountOf(d, "a").StartsWith("solo:");
        Assert.Equal(deca > 0 && mcash > 0, grouped);
    }

    [Fact]
    public void LargeSingleValueCountsAsEvidence()
    {
        var d = new AccountData();
        AccountGrouper.Reconcile(d, new[] { H("a", (5000, 0)), H("b", (5000, 0)) });   // 합이 100 이상
        Assert.Equal(AccountGrouper.AccountOf(d, "a"), AccountGrouper.AccountOf(d, "b"));
    }

    [Fact]
    public void ThreeCharacters_ChainedByDifferentMoments_EndUpInOneAccount()
    {
        var d = new AccountData();
        AccountGrouper.Reconcile(d, new[] { H("a", (1000, 500)), H("b", (1000, 500), (2000, 600)), H("c", (2000, 600)) });
        var acc = AccountGrouper.AccountOf(d, "a");
        Assert.Equal(acc, AccountGrouper.AccountOf(d, "b"));
        Assert.Equal(acc, AccountGrouper.AccountOf(d, "c"));
    }

    [Fact]
    public void ManualAssignment_IsNeverUndoneByAutoGrouping()
    {
        var d = new AccountData();
        AccountGrouper.Reconcile(d, new[] { H("a", (1000, 500)), H("b", (1000, 500)) });
        var main = AccountGrouper.AccountOf(d, "a");
        var split = AccountGrouper.AssignManually(d, "b", "new");           // 사용자가 b를 따로 뺐다
        Assert.NotEqual(main, split);
        Assert.False(AccountGrouper.Reconcile(d, new[] { H("a", (1000, 500)), H("b", (1000, 500)) }));
        Assert.Equal(main, AccountGrouper.AccountOf(d, "a"));
        Assert.Equal(split, AccountGrouper.AccountOf(d, "b"));
        Assert.True(d.Assign["b"].Manual);
    }

    [Fact]
    public void ManualJoin_IsRespected_AndNewMatchesJoinThatAccount()
    {
        var d = new AccountData();
        var acc = AccountGrouper.AssignManually(d, "a", "new");
        AccountGrouper.AssignManually(d, "b", acc);
        Assert.Equal(AccountGrouper.AccountOf(d, "a"), AccountGrouper.AccountOf(d, "b"));
        // 새 캐릭터 c가 a와 같은 값을 가졌다 → 직접 정한 그 계정에 합류
        AccountGrouper.Reconcile(d, new[] { H("a", (3000, 700)), H("b", (1, 1)), H("c", (3000, 700)) });
        Assert.Equal(acc, AccountGrouper.AccountOf(d, "c"));
    }

    [Fact]
    public void TwoAutoGroupsThatLaterMatch_Merge_AndKeepTheLongerMembership()
    {
        var d = new AccountData();
        AccountGrouper.Reconcile(d, new[] { H("a", (1000, 500)), H("b", (1000, 500)), H("c", (2000, 600)), H("d", (2000, 600)) });
        var x = AccountGrouper.AccountOf(d, "a");
        var y = AccountGrouper.AccountOf(d, "c");
        Assert.NotEqual(x, y);
        AccountGrouper.SetMembership(d, "a", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        AccountGrouper.SetMembership(d, "c", new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        // b와 c가 같은 값을 가졌던 순간이 드러났다 → 두 계정이 합쳐진다
        AccountGrouper.Reconcile(d, new[] { H("a", (1000, 500)), H("b", (1000, 500), (2000, 600)), H("c", (2000, 600)), H("d", (2000, 600)) });
        var merged = AccountGrouper.AccountOf(d, "a");
        Assert.All(new[] { "b", "c", "d" }, k => Assert.Equal(merged, AccountGrouper.AccountOf(d, k)));
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), d.Accounts[merged].MembershipExpiresAtUtc);
    }

    [Fact]
    public void Membership_IsActiveUntilItExpires_AndClearable()
    {
        var d = new AccountData();
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var acc = AccountGrouper.SetMembership(d, "a", now.AddDays(5));
        Assert.True(AccountGrouper.IsMember(d, acc, now));
        Assert.False(AccountGrouper.IsMember(d, acc, now.AddDays(6)));
        AccountGrouper.SetMembership(d, "a", null);
        Assert.False(AccountGrouper.IsMember(d, acc, now));
        Assert.False(AccountGrouper.IsMember(d, "solo:zzz", now));
    }
}

/// <summary>멤버십 만료 계산 (승운 확정): (오늘 + 남은 일수 − 1일) 다음 새벽 6시</summary>
public class MembershipClockTests
{
    [Fact]
    public void OneDayLeft_EndsAtTheNext6Am()
    {
        var now = new DateTime(2026, 10, 2, 14, 30, 0);                       // 낮 2시 반
        Assert.Equal(new DateTime(2026, 10, 3, 6, 0, 0), MembershipClock.ExpiryLocal(now, 1));
    }

    [Fact]
    public void Days_AddUpFromTheGameDate()
    {
        var now = new DateTime(2026, 10, 2, 14, 30, 0);
        Assert.Equal(new DateTime(2026, 10, 29, 6, 0, 0), MembershipClock.ExpiryLocal(now, 27));   // 10/2 + 27일 = 10/29 새벽 6시
        Assert.Equal(new DateTime(2026, 10, 5, 6, 0, 0), MembershipClock.ExpiryLocal(now, 3));
    }

    [Fact]
    public void Before6Am_StillCountsAsYesterday()
    {
        var now = new DateTime(2026, 10, 2, 5, 0, 0);                         // 새벽 5시: 게임은 아직 10/1
        Assert.Equal(new DateTime(2026, 10, 2, 6, 0, 0), MembershipClock.ExpiryLocal(now, 1));       // 한 시간 뒤
        Assert.Equal(new DateTime(2026, 10, 3, 6, 0, 0), MembershipClock.ExpiryLocal(now, 2));
    }

    [Fact]
    public void Exactly6Am_IsTheNewDay()
    {
        var now = new DateTime(2026, 10, 2, 6, 0, 0);
        Assert.Equal(new DateTime(2026, 10, 3, 6, 0, 0), MembershipClock.ExpiryLocal(now, 1));
    }

    [Fact]
    public void ResultIsAlwaysAt6Am()
    {
        foreach (var d in new[] { 1, 2, 30, 365 })
            Assert.Equal(TimeSpan.FromHours(6), MembershipClock.ExpiryLocal(new DateTime(2026, 3, 8, 23, 59, 0), d).TimeOfDay);
    }
}
