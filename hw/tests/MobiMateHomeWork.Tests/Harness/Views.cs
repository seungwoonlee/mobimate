namespace MobiMateHomeWork.Tests;

/// <summary>Characters() 결과를 테스트에서 읽기 쉽게 펴 주는 도우미.</summary>
public static class ViewExtensions
{
    public static List<CharacterView> Cards(this CharactersSnapshot s) => s.Accounts.SelectMany(a => a.Members).ToList();

    public static CharacterView Card(this CharactersSnapshot s, string key) => s.Cards().First(c => c.Key == key);

    public static AccountView AccountOf(this CharactersSnapshot s, string characterKey) =>
        s.Accounts.First(a => a.Members.Any(m => m.Key == characterKey));
}

/// <summary>시나리오에서 자주 쓰는 캐릭터.</summary>
public static class Chars
{
    /// <summary>가짜 CLI 샘플 그대로의 접속 캐릭터.</summary>
    public static readonly GameChar Aira = new("아이라", "격투가", 100, 88737, 23011, "이거밖에 안 되심?", 11_144_590, 10285, 5619, "페카 고분 심층 2층 3구역",
        WeightCurrent: 854.5651, WeightMax: 1030, Silver: 120, Tribute: 9);

    /// <summary>다른 계정의 도적. Seed.RogueProfile과 짝이다(같은 데카·M캐시·골드).</summary>
    public static readonly GameChar Rogue = new("바람", "도적", 100, 99000, 24000, "", 3_000_000, 777, 888, "콜헨",
        WeightCurrent: 300, WeightMax: 2000, Silver: 100, Tribute: 4);
}
