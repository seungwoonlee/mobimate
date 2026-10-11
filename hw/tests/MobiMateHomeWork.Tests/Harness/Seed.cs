using System.Text.Json;

namespace MobiMateHomeWork.Tests;

/// <summary>저장 폴더에 캐릭터 기록(character_history_db.json)을 미리 넣는다. 형식은 앱이 쓰는 Core 데이터 클래스 그대로다.</summary>
public static class Seed
{
    public static CharacterHistoryRecord Rec(DateTime at, int level, long combat, long deca, long mcash, long silver, long tribute,
        long gold = 500_000, double weight = 0, long living = 1000, long attract = 5000) => new()
    {
        Timestamp = at, Level = level, Title = "", CombatScore = combat, ArcaneResistance = 3000, LivingScore = living, AttractivenessScore = attract,
        Gold = gold, Deca = deca, MCash = mcash, SilverCoin = silver, DemonTribute = tribute, WeightCurrent = weight,
    };

    public static CharacterProfile Profile(string realm, string job, DateTime lastSeen, params CharacterHistoryRecord[] records) => new()
    {
        CharacterKey = $"{realm}_{job}", RealmName = realm, JobName = job, CustomName = "",
        FirstSeen = lastSeen.AddDays(-30), LastSeen = lastSeen, History = [.. records],
    };

    public static void Write(string dir, params CharacterProfile[] profiles)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "character_history_db.json"), JsonSerializer.Serialize(profiles.ToList()));
    }

    /// <summary>
    /// AccountsTests와 같은 기록: 아이라_격투가(샘플의 접속 캐릭터)와 같은 계정으로 보이는 도적
    /// (5일 전엔 같은 데카·M캐시, 3일 전 값이 어긋남)과, 전혀 다른 계정의 궁수.
    /// </summary>
    public static void WriteAccountsSeed(string dir, out DateTime threeDaysAgo)
    {
        var t3 = threeDaysAgo = DateTime.Now.AddDays(-3);
        var t5 = DateTime.Now.AddDays(-5);
        Write(dir,
            Profile("바람", "도적", t3,
                Rec(t5, 100, 99000, 10285, 5619, 90, 3, gold: 3_000_000),
                Rec(t3, 100, 99000, 10000, 5619, 100, 4, gold: 3_000_000)),
            Profile("바람", "궁수", t3, Rec(t3, 90, 70000, 777, 888, 10, 1, gold: 1_000_000)));
    }
}
