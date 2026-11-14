using System.Reflection;
using System.Text.RegularExpressions;
using MobiMateHomeWork.Hosting;

namespace MobiMateHomeWork.Tests;

/// <summary>배포 관련(H5): 버전 형식, 임베디드 app.ico(트레이 아이콘)가 실제 아이콘으로 읽히는지.</summary>
public class ReleaseTests
{
    [Fact]
    public void Version_has_build_stamp_format()
    {
        Assert.Matches(new Regex(@"^1\.0\.\d{4}\.\d{4}$"), AppInfo.Version);
    }

    [Fact]
    public void Embedded_icon_contains_all_sizes()
    {
        using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream("app.ico");
        Assert.NotNull(stream);
        using var br = new BinaryReader(stream!);
        Assert.Equal(0, br.ReadInt16());
        Assert.Equal(1, br.ReadInt16());
        var count = br.ReadInt16();
        var sizes = new List<int>();
        for (int i = 0; i < count; i++)
        {
            var w = br.ReadByte(); br.ReadBytes(15);
            sizes.Add(w == 0 ? 256 : w);
        }
        Assert.Equal(new[] { 16, 20, 24, 32, 48, 64, 256 }, sizes);
    }

    [Fact]
    public void Tray_icon_loads_from_embedded_resource()
    {
        using var icon = TrayIcon.Load();
        Assert.True(icon.Width > 0 && icon.Height > 0);
    }
}
