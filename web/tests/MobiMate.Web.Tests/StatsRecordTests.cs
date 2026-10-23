using System.Collections.Generic;
using System.IO;
using System.Linq;
using MobiMate;
using Xunit;

namespace MobiMate.Web.Tests;

/// <summary>스탯 탭이 접속하지 않은 캐릭터의 스탯도 보여주려고 get_my_info 스탯을 기록에 남긴다.</summary>
public class StatsRecordTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm_stats_" + System.Guid.NewGuid().ToString("N"));
    public StatsRecordTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static ScoreVal S(string n, long v) => new(n, v);
    private static CharacterInfo Info(long str, long attack, long combat = 90000) =>
        new(null, "아이라", 100, "대검전사", S("전투력", combat), S("생활력", 20000), S("매력", 20000), S("데코 점수", 3705), S("최대 체력", 81909),
            S("공격력", attack), S("방어력", 20307), S("마도 저항", 3756), S("힘", str), S("솜씨", 5606), S("지력", 5629), S("행운", 6080), S("의지", 5164), null, null);

    [Fact]
    public void StatsAreRecorded_AndNewRecordIsAddedWhenOneChanges()
    {
        var snap = new SnapshotManager(_dir);
        snap.UpdateSnapshot(Info(6305, 31803), new List<CurrencyItem>(), null);
        var p = snap.GetAllProfiles().Single();
        var r = p.History.Last();
        Assert.Equal((6305, 5606, 5629, 5164, 6080), (r.Str, r.Dex, r.Int, r.Will, r.Luck));
        Assert.Equal((31803, 20307, 81909, 3705), (r.AttackPower, r.DefencePower, r.HealthMax, r.DecorScore));

        var n = p.History.Count;
        snap.UpdateSnapshot(Info(6305, 31803), new List<CurrencyItem>(), null);
        Assert.Equal(n, snap.GetAllProfiles().Single().History.Count);   // 같은 값이면 늘지 않는다
        snap.UpdateSnapshot(Info(6400, 31803), new List<CurrencyItem>(), null);
        Assert.Equal(6400, snap.GetAllProfiles().Single().History.Last().Str);   // 힘만 바뀌어도 기록한다
    }
}
