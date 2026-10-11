namespace MobiMateHomeWork.Tests;

/// <summary>
/// 캐릭터 선택창 (웹앱 CharacterSelectTests를 서비스 호출로 다시 씀) + 진입 시 재화 섞임·직업 전환(H2a가 지적한 위험 지점 a).
/// 서버·직업이 비고 레벨이 0인 정보는 캐릭터가 아니다 — 가짜 캐릭터를 기록하지 않고 마지막 실제 캐릭터를 흐리게 보여 준다.
/// </summary>
public class CharacterSelectTests
{
    private static string SeedRogue()
    {
        var dir = AppHarness.NewStorageDir();
        var seen = DateTime.Now.AddDays(-1);
        Seed.Write(dir, Seed.Profile("바람", "도적", seen, Seed.Rec(seen, 100, 99000, 777, 888, 100, 4, gold: 3_000_000, living: 24000)));
        return dir;
    }

    [Fact]
    public async Task CharacterSelectScreen_KeepsTheLastRealCharacter_AndMakesNoFakeOne()
    {
        await using var h = await AppHarness.StartAsync();

        var real = await h.Session.HeaderAsync(CancellationToken.None);
        Assert.True(real.Ok);
        Assert.False(real.Value!.Selecting);
        Assert.Equal("아이라_격투가", real.Value.CharacterKey);

        h.Game.ShowCharacterSelect();
        var held = await h.Session.HeaderAsync(CancellationToken.None);
        Assert.True(held.Ok);
        Assert.True(held.Value!.Selecting);                                   // 선택 중 표시
        Assert.Equal("아이라_격투가", held.Value.CharacterKey);                  // 마지막 실제 캐릭터를 그대로
        Assert.Equal(88737, held.Value.Combat);                               // 전투력 0으로 덮지 않는다
        Assert.True(h.Log.Headers[^1].Selecting);                             // 화면에도 선택 중으로 알린다

        await h.Session.CurrenciesAsync(CancellationToken.None);
        var cards = h.Session.Characters().Cards();
        var only = Assert.Single(cards);                                      // "에린_밀레시안" 같은 가짜 캐릭터가 없다
        Assert.Equal("아이라_격투가", only.Key);
        Assert.DoesNotContain(h.Snapshots.GetAllProfiles(), p => p.CharacterKey == "에린_밀레시안");
    }

    [Fact]
    public async Task CharacterSelectScreen_WithNoRealCharacterYet_ExplainsInsteadOfInventing()
    {
        await using var h = await AppHarness.StartAsync();
        h.Game.ShowCharacterSelect();

        var r = await h.Session.HeaderAsync(CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("캐릭터를 선택하는 중", r.Failure!.Message);
        Assert.Empty(h.Log.Headers);

        await h.Session.CurrenciesAsync(CancellationToken.None);   // 재화만 읽혀도 가짜 캐릭터를 만들지 않는다
        await h.Session.RecordNowAsync(CancellationToken.None);
        Assert.Empty(h.Session.Characters().Cards());
        Assert.Empty(h.Snapshots.GetAllProfiles());
        Assert.Null(h.Session.CurrentKey);
    }

    [Fact]
    public async Task OldFakeProfile_IsHiddenFromTheCharacterList()
    {
        var dir = AppHarness.NewStorageDir();
        try
        {
            // 이전 버전이 남긴 가짜 기록 (에린_밀레시안, 레벨 1)
            var at = new DateTime(2026, 9, 1, 10, 0, 0);
            Seed.Write(dir, Seed.Profile("에린", "밀레시안", at, Seed.Rec(at, 1, 0, 0, 0, 0, 0, gold: 100)));
            await using var h = await AppHarness.StartAsync(dir);
            await h.RecordAsync();
            var keys = h.Session.Characters().Cards().Select(c => c.Key).ToList();
            Assert.DoesNotContain("에린_밀레시안", keys);      // 옛 가짜 기록은 숨긴다
            Assert.Equal(new[] { "아이라_격투가" }, keys);      // 지금 접속한 실제 캐릭터만 남는다
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── 진입 시 재화 섞임 (H2a 위험 지점 a) ──

    [Fact]
    public async Task WalletArrivingBeforeTheCharacterInfo_IsNotMixedIntoThePreviousCharacter()
    {
        var dir = SeedRogue();
        try
        {
            await using var h = await AppHarness.StartAsync(dir);

            await h.RecordAsync();                         // 아이라 접속
            Assert.Equal("아이라_격투가", h.Session.CurrentKey);

            // 캐릭터를 도적으로 바꾸는 순간: 재화는 벌써 도적 것이고 캐릭터 정보는 아직 아이라다
            h.Game.SetWallet(Chars.Rogue);
            var r = await h.Session.CurrenciesAsync(CancellationToken.None);
            Assert.True(r.Ok);

            var aira = h.Snapshots.GetProfileByKey("아이라_격투가")!;
            Assert.DoesNotContain(aira.History, x => x.Gold == 3_000_000 || x.Deca == 777);   // 아이라 기록에 도적 재화가 섞이지 않는다
            Assert.Equal(11_144_590, h.State.Currencies!.First(c => c.DisplayName == "골드").Amount);   // 현재 캐릭터의 재화 캐시도 그대로

            // 캐릭터 정보가 따라오면 도적으로 이어진다
            h.Game.SetCharacterInfo(Chars.Rogue);
            await h.RecordAsync();
            await h.RecordAsync();
            Assert.Equal("바람_도적", h.Session.CurrentKey);
            Assert.DoesNotContain(h.Snapshots.GetProfileByKey("아이라_격투가")!.History, x => x.Gold == 3_000_000);
            Assert.Equal(3_000_000, h.Snapshots.GetProfileByKey("바람_도적")!.History[^1].Gold);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task MixedWalletBeforeTheFirstHeader_HoldsTheRecordUntilTheReadingsAgree()
    {
        var dir = SeedRogue();
        try
        {
            await using var h = await AppHarness.StartAsync(dir);

            // 앱을 켠 순간: 재화는 도적 것(이미 아는 캐릭터)인데 캐릭터 정보는 아직 아이라 샘플이다
            h.Game.SetWallet(Chars.Rogue);
            await h.Session.CurrenciesAsync(CancellationToken.None);   // 아직 접속 캐릭터를 모르니 재화 캐시로 들어간다
            Assert.NotNull(h.State.Currencies);

            var held = await h.Session.HeaderAsync(CancellationToken.None);
            Assert.True(held.Ok);
            Assert.Equal(0, held.Value!.CombatDelta);                  // 세션 변화량은 비운 채 보여 준다
            Assert.Null(h.State.Currencies);                           // 섞인 재화 캐시를 비웠다
            Assert.Null(h.Snapshots.GetProfileByKey("아이라_격투가"));   // 아이라 기록을 도적 재화로 만들지 않았다

            // 읽기가 일치하면(재화도 아이라 것) 그때 기록한다
            h.Game.SetWallet(Chars.Aira);
            await h.RecordAsync();
            var aira = h.Snapshots.GetProfileByKey("아이라_격투가")!;
            Assert.Equal(11_144_590, aira.History[^1].Gold);
            Assert.Equal(10285, aira.History[^1].Deca);
            Assert.Equal(3_000_000, h.Snapshots.GetProfileByKey("바람_도적")!.History[^1].Gold);   // 도적 기록은 건드리지 않았다
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── 직업 전환 (주↔부직업) ──

    [Fact]
    public async Task JobSwap_WithTheSameWallet_StaysTheSameCharacter()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();

        h.Game.SetJob("댄서");                   // 무기를 바꿔 들어 직업만 바뀜 (골드·데카·M캐시는 그대로)
        await h.RecordAsync();

        Assert.Equal("아이라_격투가", h.Session.CurrentKey);
        var profile = Assert.Single(h.Snapshots.GetAllProfiles());            // "아이라_댄서" 기록이 생기지 않는다
        Assert.Equal("격투가", profile.JobName);
        var last = h.Log.Headers[^1];
        Assert.Equal("댄서", last.Job);                                        // 화면에는 지금 직업을 보여 준다
        Assert.Equal("아이라_격투가", last.CharacterKey);
        Assert.Single(h.Session.Characters().Cards());
    }

    [Fact]
    public async Task SwitchingToAnotherCharacter_ClearsPerCharacterState_AndFollowsTheNewKey()
    {
        var dir = SeedRogue();
        try
        {
            await using var h = await AppHarness.StartAsync(dir);
            await h.RecordAsync();
            Assert.NotNull(h.State.Currencies);

            h.Game.SetCharacter(Chars.Rogue);
            var r = await h.Session.HeaderAsync(CancellationToken.None);

            Assert.Equal("바람_도적", r.Value!.CharacterKey);
            Assert.Equal("바람_도적", h.Session.CurrentKey);
            Assert.Null(h.State.Currencies);                                  // 이전 캐릭터의 재화 캐시를 새 캐릭터 기준값으로 쓰지 않는다
            Assert.True(h.Session.IsCurrentCharacter("바람_도적"));
            Assert.False(h.Session.IsCurrentCharacter("아이라_격투가"));
            Assert.Equal(0, r.Value.CombatDelta);                             // 전환 직후 세션 변화량은 0 (이전 캐릭터와 비교하지 않는다)
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
