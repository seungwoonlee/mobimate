using MobiMate.Web.Lan;
using Xunit;
using static MobiMate.Web.Lan.WindowsFirewallCheck;

namespace MobiMate.Web.Tests;

/// <summary>폰 접속 방화벽 상태 판정 (포트 허용 규칙 기준)</summary>
public class FirewallCheckTests
{
    private const string Exe = @"V:\workspace\mobimate\webapp\web\dist\mobimateweb.exe";
    private static FirewallRule R(bool allow, string? ports, int profiles = 2, int proto = 6, string? app = null) => new(allow, profiles, proto, ports, app);

    [Fact]
    public void NoPortRule_IsMissing_EvenWithAppPathRule() =>
        Assert.Equal(FirewallState.Missing, Evaluate(new[] { R(true, "*", app: Exe) }, 17800, Exe));   // 앱 경로 규칙은 이 PC에서 적용되지 않았다

    [Fact]
    public void PortAllowRule_IsOk()
    {
        Assert.Equal(FirewallState.Ok, Evaluate(new[] { R(true, "17800") }, 17800, Exe));
        Assert.Equal(FirewallState.Ok, Evaluate(new[] { R(true, "80,17000-18000") }, 17800, Exe));
        Assert.Equal(FirewallState.Ok, Evaluate(new[] { R(true, "17800", proto: 256) }, 17800, Exe));
    }

    [Fact]
    public void RuleThatDoesNotApply_IsIgnored()
    {
        Assert.Equal(FirewallState.Missing, Evaluate(new[] { R(true, "17801") }, 17800, Exe));                 // 다른 포트
        Assert.Equal(FirewallState.Missing, Evaluate(new[] { R(true, "17800", profiles: 4) }, 17800, Exe));    // 공용에만 적용
        Assert.Equal(FirewallState.Missing, Evaluate(new[] { R(true, "17800", proto: 17) }, 17800, Exe));      // UDP
        Assert.Equal(FirewallState.Missing, Evaluate(new[] { R(true, "17800", app: @"C:\other.exe") }, 17800, Exe));   // 다른 앱 전용
    }

    [Fact]
    public void BlockRule_ForPortOrThisApp_IsBlocked_ButOtherPortsDoNotCount()
    {
        Assert.Equal(FirewallState.Blocked, Evaluate(new[] { R(true, "17800"), R(false, "17800") }, 17800, Exe));
        Assert.Equal(FirewallState.Blocked, Evaluate(new[] { R(false, "*", app: Exe) }, 17800, Exe));
        Assert.Equal(FirewallState.Missing, Evaluate(new[] { R(false, "445") }, 17800, Exe));   // Tailscale SMB 차단 같은 무관한 규칙
    }

    [Theory]
    [InlineData(null, 17800, true)]
    [InlineData("*", 17800, true)]
    [InlineData("17800", 17800, true)]
    [InlineData("1-100,17800", 17800, true)]
    [InlineData("17000-17799", 17800, false)]
    [InlineData("abc", 17800, false)]
    public void PortSpec(string? spec, int port, bool expected) => Assert.Equal(expected, PortCovers(spec, port));
}
