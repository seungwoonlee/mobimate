namespace MobiMate.Tests;

/// <summary>북마크릿이 조회할 계정: 지금 로그인한 계정 하나 (2026-10-23)</summary>
public class ActiveAccountTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 1, 0, 0, DateTimeKind.Utc);

    private static AccountData Data() => new()
    {
        Assign = new()
        {
            ["아이라_전사"] = new AccountAssignment { Account = "acc-4" },
            ["아이라_듀얼블레이드"] = new AccountAssignment { Account = "acc-4" },
            ["아이라_힐러#2"] = new AccountAssignment { Account = "acc-1" },
            ["아이라_댄서"] = new AccountAssignment { Account = "acc-1" },
        },
    };

    private static readonly (string, DateTime)[] Chars =
    {
        ("아이라_전사", Now.AddMinutes(-5)), ("아이라_듀얼블레이드", Now.AddHours(-2)),
        ("아이라_힐러#2", Now.AddHours(-9)), ("아이라_댄서", Now.AddDays(-3)),
    };

    [Fact]
    public void ConnectedCharacter_DecidesTheAccount()
    {
        var keys = ActiveAccount.KeysOf(Data(), Chars, "아이라_댄서");
        Assert.Equal(new[] { "아이라_댄서", "아이라_힐러#2" }, keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void WithoutAConnectedCharacter_TheMostRecentlySeenOnesAccountIsUsed()
    {
        var keys = ActiveAccount.KeysOf(Data(), Chars, null);
        Assert.Equal(new[] { "아이라_듀얼블레이드", "아이라_전사" }, keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void AnUnknownCurrentKey_FallsBackToTheMostRecent_AndAnEmptyListGivesNothing()
    {
        Assert.Contains("아이라_전사", ActiveAccount.KeysOf(Data(), Chars, "없는_캐릭터"));
        Assert.Empty(ActiveAccount.KeysOf(Data(), Array.Empty<(string, DateTime)>(), null));
    }

    [Fact]
    public void AUnassignedCharacter_IsItsOwnAccount()
    {
        var keys = ActiveAccount.KeysOf(Data(), new[] { ("아이라_도적", Now), ("아이라_전사", Now.AddDays(-1)) }, "아이라_도적");
        Assert.Equal(new[] { "아이라_도적" }, keys);
    }
}
