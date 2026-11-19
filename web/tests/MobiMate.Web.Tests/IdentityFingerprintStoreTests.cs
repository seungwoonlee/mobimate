using MobiMate.Web.Hosting;
using Xunit;

namespace MobiMate.Web.Tests;

public class IdentityFingerprintStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-fp-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    public IdentityFingerprintStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private IdentityFingerprintStore Make() => new(_dir, () => _now);
    private static IdentityFingerprint Fp(string key, long exp, bool skills = true) =>
        new() { Key = key, Realm = "몰리", Job = "힐러", Level = 85, Skills = skills ? new() { ["낚시"] = [23, exp] } : new() };

    [Fact]
    public void FirstObservation_IsSaved_AndSurvivesARestart()
    {
        Make().Observe(Fp("몰리_힐러", 7000));
        var again = Make();
        Assert.Equal(7000, again.Get("몰리_힐러")!.Skills["낚시"][1]);
    }

    [Fact]
    public void ReadWithoutSkills_DoesNotEraseTheKnownSkills()
    {
        var s = Make();
        s.Observe(Fp("몰리_힐러", 7000));
        _now += TimeSpan.FromMinutes(5);
        s.Observe(Fp("몰리_힐러", 0, skills: false));
        Assert.Equal(7000, s.Get("몰리_힐러")!.Skills["낚시"][1]);
    }

    [Fact]
    public void NewRecord_WritesAShadowLine_ComparingWithTheOthersOfTheSameRealm()
    {
        var s = Make();
        s.Observe(Fp("몰리_힐러", 7000));
        _now += TimeSpan.FromMinutes(5);
        s.Observe(Fp("몰리_힐러#2", 100));   // 낚시 경험치가 줄었다: 다른 캐릭터
        var lines = File.ReadAllLines(Path.Combine(_dir, IdentityFingerprintStore.ShadowFileName));
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"newRecord\":true", lines[1]);
        Assert.Contains("\"compatible\":false", lines[1]);
    }

    [Fact]
    public void SameKeyAgain_DoesNotWriteMoreShadowLines()
    {
        var s = Make();
        s.Observe(Fp("몰리_힐러", 7000));
        s.Observe(Fp("몰리_힐러", 7100));
        Assert.Single(File.ReadAllLines(Path.Combine(_dir, IdentityFingerprintStore.ShadowFileName)));
    }
}
