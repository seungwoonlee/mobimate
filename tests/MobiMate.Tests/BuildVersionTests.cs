using System;
using System.Text.RegularExpressions;
using Xunit;

namespace MobiMate.Tests;

public class BuildVersionTests
{
    [Fact]
    public void GetBuildVersionString_ReturnsFormattedTimestampVersion()
    {
        string version = MainWindow.GetBuildVersionString();

        Assert.NotNull(version);
        Assert.StartsWith("v1.1.", version);

        // v1.1.MMdd.HHmm 형식 검증 (정규식: ^v1\.1\.\d{3,4}\.\d{3,4}$)
        // 또는 v1.1.MMdd.HHmm
        Assert.Matches(@"^v1\.1\.\d+\.\d+$", version);

        // Git SHA (+...)가 붙지 않았는지 검증
        Assert.DoesNotContain("+", version);
    }
}
