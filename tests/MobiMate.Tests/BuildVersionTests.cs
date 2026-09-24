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
        Assert.StartsWith("v1.2.", version);

        // v1.2.MMdd.HHmm 형식 검증
        Assert.Matches(@"^v1\.2\.\d+\.\d+$", version);

        // Git SHA (+...)가 붙지 않았는지 검증
        Assert.DoesNotContain("+", version);
    }
}
