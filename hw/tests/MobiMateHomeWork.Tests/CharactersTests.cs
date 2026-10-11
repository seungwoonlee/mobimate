namespace MobiMateHomeWork.Tests;

/// <summary>
/// 전체 캐릭터 현황 CharacterSession.Characters() (웹앱 GET /api/characters): 계정 묶기, 은동전·마족 공물 예상(CoinForecast)과 멤버십,
/// 가방 무게(기록 0 이하·최대 모름 → null/최대 없음), 현재 맵(접속 중 실시간 / 아니면 homework_app.json), 숙제 현황·필드 보스.
/// 웹앱 ViewsTests의 Characters_*와 AccountsTests의 묶기·멤버십·예상 보유량 시나리오를 서비스 호출로 다시 쓴 것이다.
/// 웹앱과 달리 Characters()는 게임을 읽지 않으므로(읽는 일은 RecordAsync/주기 서비스) 먼저 RecordAsync로 접속 캐릭터를 기록한다.
/// </summary>
public class CharactersTests
{
    private const string Aira = "아이라_격투가";

    private static async Task<(AppHarness H, string Dir)> StartSeeded(Action<string> seed)
    {
        var dir = AppHarness.NewStorageDir();
        seed(dir);
        var h = await AppHarness.StartAsync(dir);
        await h.RecordAsync();
        return (h, dir);
    }

    private static void Cleanup(string dir) { try { Directory.Delete(dir, true); } catch { } }

    // ── ViewsTests ──

    [Fact]
    public async Task Characters_ListsTheCurrentOneWithLiveValuesFirst()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();      // 프로필·기록이 생긴다

        var me = h.Session.Characters().Cards()[0];
        Assert.True(me.IsCurrent);
        Assert.Equal(Aira, me.Key);
        Assert.Equal(88737, me.Combat);
        Assert.Equal(19745, me.Attract);
        Assert.Equal(23011, me.Living);
        var last = h.Snapshots.GetProfileByKey(Aira)!.History[^1];   // 골드·데카는 카드가 아니라 기록에 남는다
        Assert.True(last.Gold > 0);
        Assert.True(last.Deca > 0);
    }

    [Fact]
    public async Task Characters_RememberOtherCharactersAfterTheyLeave()
    {
        var dir = AppHarness.NewStorageDir();
        using var game = new FakeGame();
        try
        {
            await using (var first = await AppHarness.StartAsync(dir, game))
                await first.RecordAsync();

            await using var second = await AppHarness.StartAsync(dir, game);   // 앱을 다시 켠 뒤에도 같은 캐릭터 기록이 이어진다
            var me = Assert.Single(second.Session.Characters().Cards());
            Assert.Equal(88737, me.Combat);
            Assert.Equal(19745, me.Attract);
            Assert.False(me.IsCurrent);                                         // 아직 게임을 읽기 전: 마지막으로 본 값
            Assert.True(second.Snapshots.GetProfileByKey(Aira)!.History[^1].Deca > 0);

            await second.RecordAsync();
            Assert.True(second.Session.Characters().Cards().Single().IsCurrent);
        }
        finally { Cleanup(dir); }
    }

    // ── AccountsTests ──

    [Fact]
    public async Task SameDecaAndMCash_GroupsCharactersIntoOneAccount_AndTheyStayGroupedAfterTheyDrift()
    {
        var (h, dir) = await StartSeeded(d => Seed.WriteAccountsSeed(d, out _));
        await using var disposeH = h;
        try
        {
            var snap = h.Session.Characters();
            Assert.Equal(2, snap.Accounts.Count);                                         // 아이라+도적 / 궁수
            var main = snap.Accounts[0];                                                  // 지금 접속한 캐릭터의 계정이 맨 위
            Assert.Equal(snap.CurrentAccountId, main.Id);
            Assert.True(main.HasCurrent);
            Assert.Equal(new[] { "바람_도적", "아이라_격투가" }, main.Members.Select(m => m.Key).OrderBy(x => x, StringComparer.Ordinal));

            // 계정 안에서는 전투력이 높은 순 (도적 99,000 > 격투가 88,737)
            Assert.Equal("바람_도적", main.Members[0].Key);
            Assert.Equal(99000, main.TopCombat);
            Assert.True(snap.Accounts[1].Solo || snap.Accounts[1].Members.Count == 1);

            // 값이 어긋난 도적은 "동기화 안 됨", 지금 캐릭터는 아니다
            Assert.True(snap.Card("바람_도적").Stale);
            Assert.False(snap.Card(Aira).Stale);
            Assert.False(snap.Card("바람_궁수").Stale);

            // 묶음은 저장되어 데카·M캐시가 더 어긋나도 유지된다
            h.Game.SetWallet(Chars.Aira with { Deca = 9000 });
            await h.RecordAsync();
            Assert.Equal(2, h.Session.Characters().Accounts.Count);
            Assert.Equal(2, h.Session.Characters().AccountOf(Aira).Members.Count);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Coins_AreForecastFromTheLastSeenAmount_WithMembershipDecidingTheCap()
    {
        var (h, dir) = await StartSeeded(d => Seed.WriteAccountsSeed(d, out _));
        await using var disposeH = h;
        try
        {
            var snap = h.Session.Characters();
            var rogue = snap.Card("바람_도적");
            // 미가입(상한 100 / 10): 3일 전 은동전 100 → 이미 가득(충전 멈춤), 마족 공물 4 + 6 = 10 → 가득
            Assert.Equal(100, rogue.Silver.Cap);
            Assert.Equal(CoinLevel.Full, rogue.Silver.Level);
            Assert.Equal(10, rogue.Tribute.Expected);
            Assert.Equal(CoinLevel.Full, rogue.Tribute.Level);
            // 지금 접속 중인 캐릭터는 읽은 값 그대로 (은동전 120은 미가입 상한 100을 넘어 가득, 마족 공물 9/10은 곧 가득)
            var me = snap.Card(Aira);
            Assert.Equal(120, me.Silver.Expected);
            Assert.Equal(9, me.Tribute.Expected);
            Assert.Equal(CoinLevel.Near, me.Tribute.Level);
            Assert.False(snap.AccountOf(Aira).Membership.Active);

            // 멤버십 등록(남은 27일): 상한이 150 / 15로 늘어 충전이 이어진다
            // 남은 일수만 입력한다: 만료 = (오늘 + 27일 − 1일) 다음 새벽 6시
            var expires = MembershipClock.ExpiryLocal(DateTime.Now, 27).ToUniversalTime();
            Assert.True(h.Accounts.Update(d => { AccountGrouper.SetMembership(d, Aira, expires); return true; }));
            snap = h.Session.Characters();
            var main = snap.AccountOf(Aira);
            Assert.True(main.Membership.Active);
            Assert.InRange((main.Membership.ExpiresAt!.Value.UtcDateTime - expires).TotalMinutes, -1, 1);
            Assert.Equal(150, main.Caps.Silver);
            Assert.Equal(15, main.Caps.Tribute);
            rogue = snap.Card("바람_도적");
            Assert.Equal(150, rogue.Silver.Expected);                                   // 100 + 3일×48 = 상한 150
            Assert.Equal(CoinLevel.Full, rogue.Silver.Level);
            Assert.Equal(10, rogue.Tribute.Expected);                                   // 4 + 6 = 10 (상한 15 아래)
            Assert.Equal(CoinLevel.Ok, rogue.Tribute.Level);                            // 10/15 = 67%

            // 해제
            Assert.True(h.Accounts.Update(d => { AccountGrouper.SetMembership(d, Aira, null); return true; }));
            Assert.False(h.Session.Characters().AccountOf(Aira).Membership.Active);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task ManualAssignment_MovesACharacterToAnotherAccount_AndSurvivesARestart()
    {
        var dir = AppHarness.NewStorageDir();
        Seed.WriteAccountsSeed(dir, out _);
        using var game = new FakeGame();
        try
        {
            string archerAccount;
            await using (var h = await AppHarness.StartAsync(dir, game))
            {
                await h.RecordAsync();
                var snap = h.Session.Characters();
                var mainId = snap.AccountOf(Aira).Id;
                archerAccount = snap.AccountOf("바람_궁수").Id;

                // 궁수를 아이라의 계정으로 직접 묶는다
                Assert.True(h.Accounts.Update(d => { AccountGrouper.AssignManually(d, "바람_궁수", mainId); return true; }));
                snap = h.Session.Characters();
                Assert.Single(snap.Accounts);
                Assert.Equal(3, snap.Accounts[0].Members.Count);

                // 도적을 직접 "새 계정"으로 뺀다: 이후 자동 묶기가 다시 붙이지 않는다
                Assert.True(h.Accounts.Update(d => { AccountGrouper.AssignManually(d, "바람_도적", "new"); return true; }));
                snap = h.Session.Characters();
                Assert.Equal(2, snap.Accounts.Count);
                await h.RecordAsync();
                Assert.Equal(2, h.Session.Characters().Accounts.Count);
            }
            await using (var h = await AppHarness.StartAsync(dir, game))
            {
                await h.RecordAsync();
                var rogueAccount = h.Session.Characters().AccountOf("바람_도적");
                Assert.Single(rogueAccount.Members);                                   // 그대로 따로 있다
                Assert.NotEqual(archerAccount, rogueAccount.Id);
            }
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task AccountOrder_IsSavedAndReturnedAsManualRank()
    {
        var (h, dir) = await StartSeeded(d => Seed.WriteAccountsSeed(d, out _));
        await using var disposeH = h;
        try
        {
            var accounts = h.Session.Characters().Accounts;
            Assert.All(accounts, a => Assert.Equal(-1, a.ManualRank));                 // 정하기 전
            var ids = accounts.Select(a => a.Id).ToList();
            Assert.Equal(2, ids.Count);

            Assert.True(h.Accounts.Update(d => { AccountGrouper.SetOrder(d, [ids[1], ids[0]]); return true; }));
            var after = h.Session.Characters().Accounts;
            Assert.Equal(1, after.First(a => a.Id == ids[0]).ManualRank);
            Assert.Equal(0, after.First(a => a.Id == ids[1]).ManualRank);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task SameServerAndJobOnAnotherAccount_BecomesASeparateCharacter_NotMergedIntoTheExistingOne()
    {
        // 접속 중: 아이라_격투가 Lv.100 · 생활력 23,011 · 데카 10,285 · M캐시 5,619. 기록에는 다른 계정의 아이라_격투가(Lv.100, 데카·M캐시가 다르다)가 있다
        var (h, dir) = await StartSeeded(d =>
        {
            var t = DateTime.Now.AddDays(-1);
            Seed.Write(d, Seed.Profile("아이라", "격투가", t, Seed.Rec(t, 100, 40000, 777, 888, 10, 1, gold: 2_000_000)));
        });
        await using var disposeH = h;
        try
        {
            var snap = h.Session.Characters();
            Assert.Equal(new[] { "아이라_격투가", "아이라_격투가#2" }, snap.Cards().Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(2, snap.Accounts.Count);
            Assert.True(snap.Card("아이라_격투가#2").IsCurrent);
            Assert.Equal(2, snap.Card("아이라_격투가#2").Variant);
            Assert.False(snap.Card(Aira).IsCurrent);
            Assert.Equal("아이라_격투가#2", h.Session.CurrentKey);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task SameServerAndJobAndSameAccountValues_StaysOneCharacter()
    {
        var (h, dir) = await StartSeeded(d =>
        {
            var t = DateTime.Now.AddDays(-1);
            Seed.Write(d, Seed.Profile("아이라", "격투가", t, Seed.Rec(t, 99, 85000, 10285, 5619, 10, 1, gold: 11_000_000)));
        });
        await using var disposeH = h;
        try
        {
            Assert.Equal(Aira, h.Session.Characters().Cards().Single().Key);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Recorder_RecordsTheCurrentCharacterCoinsAndCurrencies()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var me = h.Session.Characters().Cards().Single();
        Assert.Equal(120, me.Silver.Held);
        Assert.Equal(9, me.Tribute.Held);
        var last = h.Snapshots.GetProfileByKey(Aira)!.History[^1];
        Assert.Equal(10285, last.Deca);
        Assert.Equal(5619, last.MCash);
    }

    // ── 이 앱에서 새로 확인하는 것 ──

    [Fact]
    public async Task CurrentAccountComesFirst_EvenWhenAnotherAccountHasHigherCombat()
    {
        var (h, dir) = await StartSeeded(d =>
        {
            var t = DateTime.Now.AddDays(-1);
            Seed.Write(d, Seed.Profile("바람", "궁수", t, Seed.Rec(t, 100, 500000, 777, 888, 10, 1, gold: 1_000_000)));
        });
        await using var disposeH = h;
        try
        {
            var snap = h.Session.Characters();
            Assert.Equal("바람_궁수", snap.Accounts.OrderByDescending(a => a.TopCombat).First().Members[0].Key);
            Assert.True(snap.Accounts[0].HasCurrent);                                  // 전투력이 낮아도 접속 중 계정이 맨 위
            Assert.Equal(snap.Accounts[0].Id, snap.CurrentAccountId);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Weight_IsNullWhenTheLastRecordIsZeroOrBelow_AndHasNoMaxWhenTheMaxIsUnknown()
    {
        var (h, dir) = await StartSeeded(d =>
        {
            var t = DateTime.Now.AddDays(-1);
            Seed.Write(d,
                Seed.Profile("바람", "무기록", t, Seed.Rec(t, 90, 50000, 11, 22, 10, 1, weight: 0)),         // 0 = 모름
                Seed.Profile("바람", "음수", t, Seed.Rec(t, 90, 50000, 33, 44, 10, 1, weight: -3)),          // 0 이하 = 모름
                Seed.Profile("바람", "최대모름", t, Seed.Rec(t, 90, 50000, 55, 66, 10, 1, weight: 500)),      // 현재 무게만 알고 최대는 모름
                Seed.Profile("바람", "주의", t, Seed.Rec(t, 90, 50000, 77, 88, 10, 1, weight: 960)),
                Seed.Profile("바람", "위험", t, Seed.Rec(t, 90, 50000, 99, 111, 10, 1, weight: 1010)),
                Seed.Profile("바람", "여유", t, Seed.Rec(t, 90, 50000, 122, 133, 10, 1, weight: 500)));
        });
        await using var disposeH = h;
        try
        {
            h.Settings.RememberCharacter("바람_주의", null, 1000);       // 저장된 최대 무게
            h.Settings.RememberCharacter("바람_위험", null, 1000);
            h.Settings.RememberCharacter("바람_여유", null, 1000);
            var snap = h.Session.Characters();

            Assert.Null(snap.Card("바람_무기록").Weight);
            Assert.Null(snap.Card("바람_음수").Weight);
            var unknownMax = snap.Card("바람_최대모름").Weight!;
            Assert.Equal(500, unknownMax.Current);
            Assert.Null(unknownMax.Max);                                   // 최대를 모르면 백분율·단계도 없다
            Assert.Null(unknownMax.Percent);
            Assert.Null(unknownMax.Level);

            Assert.Equal((96.0, WeightLevel.Warn), (snap.Card("바람_주의").Weight!.Percent!.Value, snap.Card("바람_주의").Weight!.Level!.Value));
            Assert.Equal((101.0, WeightLevel.Danger), (snap.Card("바람_위험").Weight!.Percent!.Value, snap.Card("바람_위험").Weight!.Level!.Value));
            Assert.Equal((50.0, WeightLevel.Ok), (snap.Card("바람_여유").Weight!.Percent!.Value, snap.Card("바람_여유").Weight!.Level!.Value));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Weight_ForTheConnectedCharacter_IsLive_AndFallsBackToTheSavedMaxWhenTheGameDoesNotSayIt()
    {
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var live = h.Session.Characters().Card(Aira).Weight!;
        Assert.Equal(854.6, live.Current);                                 // 소수 첫째 자리 반올림
        Assert.Equal(1030, live.Max);
        Assert.Equal(83.0, live.Percent);
        Assert.Equal(WeightLevel.Ok, live.Level);
        Assert.Equal(1030, h.Settings.CharacterInfo(Aira)!.WeightMax);      // 최대는 숙제 앱 설정에 남는다

        // 게임이 최대 무게를 0으로 알려 주는 순간(읽기 실패): 저장된 최대로 계산한다
        h.Game.SetVitals(1030, 0);
        await h.RecordAsync();
        var fallback = h.Session.Characters().Card(Aira).Weight!;
        Assert.Equal(1030, fallback.Max);
        Assert.Equal(100.0, fallback.Percent);
        Assert.Equal(WeightLevel.Danger, fallback.Level);
        Assert.Equal(1030, h.Settings.CharacterInfo(Aira)!.WeightMax);      // 0으로 지우지 않았다
        Assert.Equal(WeightLevel.Danger, h.Log.Headers[^1].Weight!.Level);   // 헤더도 같다
    }

    [Fact]
    public async Task Weight_ForTheConnectedCharacter_WithNoSavedMax_ShowsOnlyTheCurrentWeight()
    {
        await using var h = await AppHarness.StartAsync();
        h.Game.SetVitals(500, 0);
        await h.RecordAsync();
        var w = h.Session.Characters().Card(Aira).Weight!;
        Assert.Equal(500, w.Current);
        Assert.Null(w.Max);
        Assert.Null(h.Settings.CharacterInfo(Aira)!.WeightMax);             // 모르는 최대를 0으로 기록하지 않는다
    }

    [Fact]
    public async Task Map_IsLiveForTheConnectedCharacter_AndComesFromTheAppSettingsForTheOthers()
    {
        var (h, dir) = await StartSeeded(d =>
        {
            var t = DateTime.Now.AddDays(-1);
            Seed.Write(d,
                Seed.Profile("바람", "도적", t, Seed.Rec(t, 100, 99000, 777, 888, 10, 1, gold: 3_000_000)),
                Seed.Profile("바람", "궁수", t, Seed.Rec(t, 90, 70000, 55, 66, 10, 1, gold: 1_000_000)));
        });
        await using var disposeH = h;
        try
        {
            h.Settings.RememberCharacter("바람_도적", "던바튼", null);       // 이 캐릭터가 마지막으로 있던 곳
            var snap = h.Session.Characters();
            Assert.Equal("페카 고분 심층 2층 3구역", snap.Card(Aira).Map);       // 접속 중: 실시간
            Assert.Equal("던바튼", snap.Card("바람_도적").Map);                // 아니면 기억해 둔 마지막 맵
            Assert.Null(snap.Card("바람_궁수").Map);                           // 한 번도 못 봤으면 없음

            // 접속 중인 캐릭터가 이동하면 실시간 값이 따라가고, 마지막 맵으로도 남는다
            h.Game.SetMap("콜헨");
            await h.RecordAsync();
            Assert.Equal("콜헨", h.Session.Characters().Card(Aira).Map);
            Assert.Equal("콜헨", h.Settings.CharacterInfo(Aira)!.LastMap);

            // 지역 이름이 비어 읽히는 순간(로딩)에는 마지막 맵을 보여 주고 기억한 값을 지우지 않는다
            h.Game.SetMap("");
            await h.RecordAsync();
            Assert.Equal("콜헨", h.Session.Characters().Card(Aira).Map);
            Assert.Equal("콜헨", h.Log.Headers[^1].Map);
            Assert.Equal("콜헨", h.Settings.CharacterInfo(Aira)!.LastMap);
            Assert.Equal("던바튼", h.Session.Characters().Card("바람_도적").Map);   // 다른 캐릭터 값은 그대로
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Remember_SkipsTheFirstReadAfterACharacterSwitch_AndSavesFromTheNextRead()
    {
        // 전환 직후 읽기는 맵·무게가 이전 캐릭터 것일 수 있다: 그 읽기에서는 새 캐릭터 몫으로 저장하지 않고, 다음 읽기에서 저장한다 (H2a 위험 지점 c)
        await using var h = await AppHarness.StartAsync();
        await h.RecordAsync();
        var aira = h.Settings.CharacterInfo(Aira)!;
        Assert.Equal("페카 고분 심층 2층 3구역", aira.LastMap);
        Assert.Equal(1030, aira.WeightMax);

        h.Game.SetCharacter(Chars.Rogue);
        var switching = await h.Session.HeaderAsync(CancellationToken.None);
        Assert.Equal("바람_도적", switching.Value!.CharacterKey);
        Assert.Null(h.Settings.CharacterInfo("바람_도적"));                       // 전환 직후 읽기: 저장 건너뜀
        Assert.Equal("페카 고분 심층 2층 3구역", h.Settings.CharacterInfo(Aira)!.LastMap);   // 이전 캐릭터 값은 새 값으로 덮이지 않는다
        Assert.Equal(1030, h.Settings.CharacterInfo(Aira)!.WeightMax);

        await h.Session.HeaderAsync(CancellationToken.None);                        // 다음 읽기: 캐릭터가 확정되었으니 저장
        var rogue = h.Settings.CharacterInfo("바람_도적")!;
        Assert.Equal("콜헨", rogue.LastMap);
        Assert.Equal(2000, rogue.WeightMax);
        Assert.Equal("페카 고분 심층 2층 3구역", h.Settings.CharacterInfo(Aira)!.LastMap);
    }

    [Fact]
    public async Task HomeworkStatus_ShowsAutoCheckedHomework_PerCharacter()
    {
        var (h, dir) = await StartSeeded(d =>
        {
            var t = DateTime.Now.AddDays(-1);
            Seed.Write(d, Seed.Profile("바람", "도적", t, Seed.Rec(t, 100, 99000, 777, 888, 10, 1, gold: 3_000_000)));
        });
        await using var disposeH = h;
        try
        {
            var before = h.Session.Characters();
            Assert.All(before.Card(Aira).Homework, s => Assert.Equal("unknown", s.State));   // 이번 주기에 아직 확인 못 함
            Assert.Equal("unknown", before.Card(Aira).FieldBoss.State);
            var ids = before.Card(Aira).Homework.Select(s => s.Id).ToList();
            Assert.Contains("raid_cavrak", ids);                                           // 자동 판정되는 숙제만 나온다
            Assert.DoesNotContain("daily_black_hole", ids);                                // 수동 전용 숙제는 나오지 않는다
            Assert.DoesNotContain("raid_glasgivnen", ids);                                 // 열려 있지 않은 레이드도 나오지 않는다

            Assert.True(h.Commands.SetHomework("raid_cavrak", true, null).Ok);
            var after = h.Session.Characters();
            Assert.Equal("done", after.Card(Aira).Homework.Single(s => s.Id == "raid_cavrak").State);
            Assert.Equal("unknown", after.Card(Aira).Homework.Single(s => s.Id == "raid_airel").State);
            Assert.All(after.Card("바람_도적").Homework, s => Assert.Equal("unknown", s.State));   // 다른 캐릭터는 그대로
            Assert.Equal("unknown", after.Card("바람_도적").FieldBoss.State);
        }
        finally { Cleanup(dir); }
    }
}
