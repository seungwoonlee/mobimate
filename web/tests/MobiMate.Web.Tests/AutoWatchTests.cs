using MobiMate.Web.Services;

namespace MobiMate.Web.Tests;

/// <summary>오토 의심 서비스: 맵이 바뀌면 이전 방의 파티원이 "안 보임"으로 남지 않는다 (2026-10-23, 어비스는 나 포함 4명)</summary>
public class AutoWatchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    private static NearPcItem Pc(string job, double dist, long score) =>
        new("아이라", null, dist, 100, job, score, false, false, false, true);

    private static List<NearPcItem> Party(int s, params (string Job, long Score)[] people) =>
        people.Select((p, i) => Pc(p.Job, 4 + i * 2 + (s % 2) * 0.6, p.Score)).ToList();

    [Fact]
    public void ChangingTheMap_StartsOver_SoThePreviousRoomsPartyIsNotMissing()
    {
        var w = new AutoWatch();
        var old = new[] { ("전사", 100000L), ("궁수", 110000L), ("힐러", 90000L) };
        for (var s = 0; s <= 40; s++) { w.SetInstance(true, T0.AddSeconds(s), "허상의 정박지"); w.Observe(T0.AddSeconds(s), true, Party(s, old)); }

        // 다른 어비스로 옮겼고, 새 방에는 나 말고 한 명만 보인다(전부 새 얼굴이 2명 미만이라 파티 교체 규칙은 아직 작동하지 않는다)
        var solo = new[] { ("마법사", 95000L) };
        for (var s = 41; s <= 200; s++) { w.SetInstance(true, T0.AddSeconds(s), "광기의 동굴"); w.Observe(T0.AddSeconds(s), true, Party(s, solo)); }
        Assert.DoesNotContain(w.Suspects(), x => x.Reason is AutoSuspicionReason.Missing or AutoSuspicionReason.Deserter);
    }

    [Fact]
    public void WithoutAMapChange_AMemberWhoDisappears_IsStillMissing()
    {
        var w = new AutoWatch();
        var all = new[] { ("전사", 100000L), ("궁수", 110000L), ("힐러", 90000L) };
        for (var s = 0; s <= 40; s++) { w.SetInstance(true, T0.AddSeconds(s), "허상의 정박지"); w.Observe(T0.AddSeconds(s), true, Party(s, all)); }
        for (var s = 41; s <= 160; s++) { w.SetInstance(true, T0.AddSeconds(s), "허상의 정박지"); w.Observe(T0.AddSeconds(s), true, Party(s, all.Take(2).ToArray())); }
        Assert.Contains(w.Suspects(), x => x.Reason is AutoSuspicionReason.Missing or AutoSuspicionReason.Deserter && x.Player.Job == "힐러");
    }

    [Fact]
    public void AnEmptyMapName_WhileLoading_IsNotAChange()
    {
        var w = new AutoWatch();
        var all = new[] { ("전사", 100000L), ("궁수", 110000L), ("힐러", 90000L) };
        for (var s = 0; s <= 40; s++) { w.SetInstance(true, T0.AddSeconds(s), "허상의 정박지"); w.Observe(T0.AddSeconds(s), true, Party(s, all)); }
        for (var s = 41; s <= 160; s++)
        {
            w.SetInstance(true, T0.AddSeconds(s), s % 5 == 0 ? "" : "허상의 정박지");   // 가끔 로딩으로 이름이 빈다
            w.Observe(T0.AddSeconds(s), true, Party(s, all.Take(2).ToArray()));
        }
        Assert.Contains(w.Suspects(), x => x.Reason is AutoSuspicionReason.Missing or AutoSuspicionReason.Deserter && x.Player.Job == "힐러");   // 초기화되지 않았다
    }
}
