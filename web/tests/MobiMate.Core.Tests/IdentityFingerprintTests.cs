namespace MobiMate.Tests;

/// <summary>캐릭터 지문 (식별 개선 1단계): 같은 캐릭터라면 지켜지는 것(스킬은 줄지 않는다)과 창고 겹침</summary>
public class IdentityFingerprintTests
{
    private static IdentityFingerprint Fp(string key, Dictionary<string, long[]>? skills = null, string[]? charStore = null, string[]? accStore = null) =>
        new() { Key = key, Realm = "몰리", Job = "힐러", Skills = skills ?? new(), CharStorage = (charStore ?? []).ToList(), AccountStorage = (accStore ?? []).ToList() };

    [Fact]
    public void SameCharacterLater_SkillsOnlyGrow_IsCompatible()
    {
        var known = Fp("a", new() { ["낚시"] = [23, 7000], ["약초 채집"] = [32, 7000] });
        var now = Fp("x", new() { ["낚시"] = [23, 7400], ["약초 채집"] = [33, 100] });   // 경험치가 늘고, 레벨이 올라 경험치가 처음부터
        var m = FingerprintComparer.Compare(known, now);
        Assert.True(m.Compatible);
        Assert.Equal(2, m.SkillsCompared);
        Assert.Equal(1.0, m.SkillProgress);
    }

    [Fact]
    public void ADecreasedSkill_MeansADifferentCharacter()
    {
        var known = Fp("a", new() { ["낚시"] = [23, 7000], ["약초 채집"] = [32, 7000] });
        var now = Fp("x", new() { ["낚시"] = [20, 100], ["약초 채집"] = [32, 7000] });
        var m = FingerprintComparer.Compare(known, now);
        Assert.False(m.Compatible);
        Assert.Equal(1, m.SkillViolations);
    }

    [Fact]
    public void NoSkillsRead_IsNotTreatedAsIncompatible_ButNoted()
    {
        var m = FingerprintComparer.Compare(Fp("a"), Fp("x", new() { ["낚시"] = [1, 0] }));
        Assert.True(m.Compatible);
        Assert.Equal(0, m.SkillsCompared);
        Assert.NotNull(m.Note);
    }

    [Fact]
    public void Jaccard_OfStorages()
    {
        Assert.Equal(1.0, FingerprintComparer.Jaccard(["a", "b"], ["a", "b"]));
        Assert.Equal(0.5, FingerprintComparer.Jaccard(["a", "b"], ["b", "c", "a", "d"]), 3);   // 겹침 2 / 합집합 4
        Assert.Equal(-1, FingerprintComparer.Jaccard([], []));                                  // 둘 다 비면 판단할 수 없다
    }

    [Fact]
    public void From_ReadsSkillsAndStorages_NotTheBag()
    {
        var ch = System.Text.Json.JsonSerializer.Deserialize<CharacterInfo>(
            "{\"RealmName\":\"몰리\",\"Level\":85,\"EnabledCombatJobDisplayName\":\"힐러\",\"CombatScore\":{\"DisplayName\":\"전투력\",\"Value\":26950},\"LivingScore\":{\"DisplayName\":\"생활력\",\"Value\":3050},"
            + "\"LivingSkills\":[{\"DisplayName\":\"낚시\",\"Level\":23,\"Exp\":7429,\"MaxExp\":12670}]}")!;
        var items = new[]
        {
            new ItemData("inventory", "저주 해제 물약", "기타", 118, false),
            new ItemData("character_storage", "정령의 날개", "기타", 5, false),
            new ItemData("account_storage", "낡은 열쇠", "기타", 1, false),
            new ItemData("account_storage", "낡은 열쇠", "기타", 2, false),
        };
        var fp = IdentityFingerprint.From("몰리_힐러", ch, items, [new CurrencyItem("데카", 3202), new CurrencyItem("M캐시", 2109)], new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new long[] { 23, 7429 }, fp.Skills["낚시"]);
        Assert.Equal(["정령의 날개"], fp.CharStorage);
        Assert.Equal(["낡은 열쇠"], fp.AccountStorage);   // 가방은 넣지 않고, 같은 이름은 한 번만
        Assert.Equal(3202, fp.Deca);
        Assert.Equal(2109, fp.MCash);
    }
}
