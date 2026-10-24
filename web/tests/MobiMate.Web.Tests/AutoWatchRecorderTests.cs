using System.Text.Json;
using MobiMate.Web.Services;

namespace MobiMate.Web.Tests;

/// <summary>오토 감시 기록(2026-10-25): 판 안의 관찰을 사람이 기록기를 켜지 않아도 남긴다.</summary>
public class AutoWatchRecorderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-watchrec-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 3, 0, 0, TimeSpan.Zero);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static NearPcItem Pc(string job, double dist, long score, bool combat = true) => new("아이라", null, dist, 100, job, score, false, false, false, combat);

    private string[] Lines(AutoWatchRecorder r) => File.ReadAllLines(r.FileFor(T0));

    [Fact]
    public void InsideAnInstance_EveryObservationIsOneJsonLineWithPlayersAndSuspects()
    {
        var rec = new AutoWatchRecorder(_dir);
        var w = new AutoWatch(rec);
        w.SetInstance(true, T0, "레이드 에이렐");
        w.Observe(T0, true, new[] { Pc("전사", 5, 100000), Pc("수도사", 8, 95000, combat: false) }, new WorldPos(1.5, 2.5));
        w.Observe(T0.AddSeconds(1), true, new[] { Pc("전사", 5.2, 100000), Pc("수도사", 8, 95000, combat: false) });

        var lines = Lines(rec);
        Assert.Equal(2, lines.Length);
        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("레이드 에이렐", doc.RootElement.GetProperty("space").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("p").GetArrayLength());
        Assert.Equal("수도사", doc.RootElement.GetProperty("p")[1].GetProperty("job").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("p")[1].GetProperty("c").GetInt32());
        Assert.Equal(1.5, doc.RootElement.GetProperty("me").GetProperty("X").GetDouble());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("s").ValueKind);
    }

    [Fact]
    public void OutsideAnInstance_NothingIsWritten()
    {
        var rec = new AutoWatchRecorder(_dir);
        var w = new AutoWatch(rec);
        w.Observe(T0, false, new[] { Pc("전사", 5, 100000) });
        Assert.False(File.Exists(rec.FileFor(T0)));
    }

    [Fact]
    public void AWriteFailure_DoesNotStopTheWatch_AndIsReportedOnce()
    {
        var blocked = Path.Combine(_dir, "file-not-dir");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocked, "x");   // 폴더 자리에 파일이 있어 만들 수 없다
        var errors = new List<string>();
        var w = new AutoWatch(new AutoWatchRecorder(blocked, (m, _) => errors.Add(m)));
        w.SetInstance(true, T0, "레이드 에이렐");
        for (var s = 0; s < 3; s++) w.Observe(T0.AddSeconds(s), true, new[] { Pc("전사", 5, 100000) });
        Assert.Single(errors);
        Assert.True(w.InInstance(T0.AddSeconds(3)));   // 감시는 계속된다
    }

    [Fact]
    public void FilesOlderThanTwoWeeks_AreDeleted()
    {
        Directory.CreateDirectory(_dir);
        var old = Path.Combine(_dir, "watch-20260101.jsonl");
        File.WriteAllText(old, "{}\n");
        File.SetLastWriteTimeUtc(old, T0.UtcDateTime - TimeSpan.FromDays(20));
        var keep = Path.Combine(_dir, "watch-20261024.jsonl");
        File.WriteAllText(keep, "{}\n");
        File.SetLastWriteTimeUtc(keep, T0.UtcDateTime - TimeSpan.FromDays(1));
        var w = new AutoWatch(new AutoWatchRecorder(_dir));
        w.SetInstance(true, T0, "레이드 에이렐");
        w.Observe(T0, true, new[] { Pc("전사", 5, 100000) });
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(keep));
    }
}
