using MobiMateHomeWork.ViewModels;
using static MobiMateHomeWork.Tests.BoardKit;

namespace MobiMateHomeWork.Tests;

/// <summary>H4 화면 모델(창 없음): 계정 탭·배지, 줄 정렬·모두 완료, 선택 기본값·갱신 후 유지, 칸 클릭 → 명령(필드 보스 규칙), 리셋 경계, 이름 바꾸기.</summary>
public class BoardViewModelTests
{
    private sealed class Fixture
    {
        public FakeActions Actions { get; } = new();
        public int Refreshes;
        public DateTimeOffset Clock = Now;
        public HomeworkBoardViewModel Board { get; }

        public Fixture(TimeSpan? notice = null)
        {
            Board = new HomeworkBoardViewModel(Actions, refresh: () => { Interlocked.Increment(ref Refreshes); return Task.CompletedTask; },
                post: a => a(), clock: () => Clock, noticeDuration: notice ?? TimeSpan.FromMinutes(5));
        }
    }

    private static (CharacterView Aira, CharacterView Rogue, CharacterView Archer, CharacterView Bard) Sample() => (
        Char("아이라_격투가", "격투가", "아이라", 88_000, current: true, hw: [St("raid_cavrak", "done", evidence: "원정의 증거 +20")]),
        Char("바람_도적", "도적", "바람", 99_000, nick: "그림자", hw: AllNineDone(), boss: new HomeworkPoolStatus("done", "크라마")),
        Char("바람_궁수", "궁수", "바람", 84_000),
        Char("류트_음유시인", "음유시인", "류트", 80_000));

    // ── 계정 탭 ──

    [Fact]
    public void Tabs_AccountsOrderedCurrentFirst_ThenByTopCombat()
    {
        var (aira, rogue, archer, bard) = Sample();
        var f = new Fixture();
        // 도적(최고 99k)이 있는 계정이 격투가(88k)의 계정보다 전투력은 높지만, 접속 중 계정이 맨 앞이다
        f.Board.Apply(Snap(Acct("B", rogue, archer), Acct("A", aira), Acct("C", bard)));
        Assert.Equal(["A", "B", "C"], f.Board.Accounts.Select(a => a.Id));
        Assert.True(f.Board.Accounts[0].IsCurrent);
        Assert.Equal("A", f.Board.SelectedAccount?.Id);   // 기본 선택 = 접속 중 계정
    }

    [Fact]
    public void Tabs_NoCurrentAccount_SelectsTheFirstOne()
    {
        var (_, rogue, archer, bard) = Sample();
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("C", bard), Acct("B", rogue, archer)));
        Assert.Equal(["B", "C"], f.Board.Accounts.Select(a => a.Id));   // 접속 중이 없으면 최고 전투력 순
        Assert.Equal("B", f.Board.SelectedAccount?.Id);
        Assert.DoesNotContain(f.Board.Accounts, a => a.IsCurrent);
    }

    [Fact]
    public void Tabs_ManualOrder_AppliesAfterTheCurrentAccount_UnsetAccountsGoLast()
    {
        var a = Char("a_1", combat: 10, current: true);
        var b = Char("b_1", combat: 50);
        var c = Char("c_1", combat: 30);
        var d = Char("d_1", combat: 40);
        var order = HomeworkBoardViewModel.OrderAccounts([Acct("B", 1, b), Acct("C", 0, c), Acct("D", d), Acct("A", a)]);
        Assert.Equal(["A", "C", "B", "D"], order.Select(x => x.Id));   // 접속 중 → 내 순서(0, 1) → 정하지 않은 계정
        // 순서를 정한 계정이 하나도 없으면 최고 전투력 순
        Assert.Equal(["A", "B", "D", "C"], HomeworkBoardViewModel.OrderAccounts([Acct("C", c), Acct("D", d), Acct("B", b), Acct("A", a)]).Select(x => x.Id));
    }

    [Fact]
    public void Badge_IsTheNumberOfRemainingCells_AndACheckWhenEverythingIsDone()
    {
        var (aira, rogue, archer, _) = Sample();
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", aira), Acct("B", rogue, archer)));
        var a = f.Board.Accounts.Single(x => x.Id == "A");
        Assert.Equal(9, a.Remaining);                  // 열 칸 중 카브락만 완료
        Assert.Equal("9", a.Badge);
        Assert.False(a.AllDone);
        var b = f.Board.Accounts.Single(x => x.Id == "B");
        Assert.Equal(0 + 10, b.Remaining);             // 도적은 열 칸 완료(0), 궁수는 열 칸 모두 남음(10)
        Assert.Contains("남은 숙제 칸 10개", b.Tooltip);

        f.Board.Apply(Snap(Acct("A", aira), Acct("B", rogue)));
        var done = f.Board.Accounts.Single(x => x.Id == "B");
        Assert.True(done.AllDone);
        Assert.Equal("✓", done.Badge);                  // 모두 완료면 ✓
        Assert.Contains("모두 마쳤습니다", done.Tooltip);
    }

    // ── 줄 ──

    [Fact]
    public void Rows_AreSortedByCombatDescending_AndDimWhenAllDone()
    {
        var (aira, rogue, archer, _) = Sample();
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", archer, aira, rogue)));   // 일부러 뒤섞인 순서
        Assert.Equal(["바람_도적", "아이라_격투가", "바람_궁수"], f.Board.Rows.Select(r => r.Key));   // 99k, 88k, 84k
        var rogueRow = f.Board.Rows[0];
        Assert.True(rogueRow.AllDone);                       // 흐리게 보인다
        Assert.Equal("10/10", rogueRow.DoneText);
        Assert.Equal("그림자", rogueRow.DisplayName);
        Assert.False(f.Board.Rows[1].AllDone);
        Assert.Equal("1/10", f.Board.Rows[1].DoneText);
    }

    [Fact]
    public void Row_Info_NicknameOrRealmJob_JobBadge_AndCompactNumbers()
    {
        var (aira, rogue, _, _) = Sample();
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", aira, rogue)));
        var airaRow = f.Board.Rows.Single(r => r.Key == "아이라_격투가");
        Assert.Equal("아이라 · 격투가", airaRow.DisplayName);       // 닉네임이 없으면 "서버 · 직업"
        Assert.Equal("격", airaRow.JobLetter);
        Assert.Equal(JobPalette.Thief, airaRow.JobColor);
        Assert.Equal("격투가 100 · 8.8만", airaRow.JobLevelCombat);
        Assert.Equal("접속 중", airaRow.LastSeenText);
        Assert.True(airaRow.IsCurrent);
        var rogueRow = f.Board.Rows.Single(r => r.Key == "바람_도적");
        Assert.Equal("3시간 전", rogueRow.LastSeenText);
        Assert.Contains("전투력 99,000", rogueRow.Tooltip);
        Assert.Equal(10, airaRow.Cells.Count);
        Assert.Equal([3, 3, 3, 1], airaRow.Groups.Select(g => g.Cells.Count));
    }

    [Fact]
    public void Cells_CarryStateLeftSuggestionAndInProgress()
    {
        var sug = new HomeworkSuggestion("raidTokenIncreased", "원정의 증거: 카브락 레이드", 3, 4);
        var c = Char("아이라_격투가", current: true, inProgress: ["abyss_madness_cave"], hw:
        [
            St("weekly_vanguard_breach", "todo", remaining: 2), St("raid_cavrak", "todo", suggestion: sug), St("daily_day_dungeon", "done", evidence: "퀘스트가 사라짐"),
        ]);
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", c)));
        var cells = f.Board.Rows[0].Cells.ToDictionary(x => x.Id);
        Assert.True(cells["daily_day_dungeon"].IsDone);
        Assert.True(cells["weekly_vanguard_breach"].IsTodo);
        Assert.Equal(("2회", true), (cells["weekly_vanguard_breach"].LeftText, cells["weekly_vanguard_breach"].HasLeft));
        Assert.Equal("남은 2회", cells["weekly_vanguard_breach"].StatusText);
        Assert.True(cells["raid_cavrak"].Suggested);
        Assert.True(cells["abyss_madness_cave"].InProgress);
        Assert.False(cells["abyss_illusory_anchorage"].InProgress);
        Assert.True(cells["raid_white_succubus"].IsUnknown);
        Assert.Contains("눌러서 미완료로 바꾸기", cells["daily_day_dungeon"].AutomationName);
    }

    // ── 선택 ──

    [Fact]
    public void Selection_DefaultsToTheCurrentCharacter_ElseTheFirstRow()
    {
        var (aira, rogue, archer, _) = Sample();
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", rogue, aira)));
        Assert.Equal("아이라_격투가", f.Board.SelectedRow?.Key);   // 접속 중 캐릭터(전투력은 둘째)
        Assert.Equal("아이라_격투가", f.Board.Card.Key);
        Assert.True(f.Board.HasCard);

        var g = new Fixture();
        g.Board.Apply(Snap(Acct("B", archer, rogue)));
        Assert.Equal("바람_도적", g.Board.SelectedRow?.Key);       // 접속 중이 없으면 첫 줄(전투력 1위)
    }

    [Fact]
    public void Selection_SurvivesRefresh_WhenTheUserPickedARow()
    {
        var (aira, rogue, archer, _) = Sample();
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", aira, rogue, archer)));
        f.Board.SelectRow(f.Board.Rows.Single(r => r.Key == "바람_궁수"));
        Assert.Equal("바람_궁수", f.Board.SelectedRow?.Key);
        Assert.Equal("바람_궁수", f.Board.Card.Key);
        Assert.True(f.Board.Rows.Single(r => r.Key == "바람_궁수").IsSelected);
        Assert.False(f.Board.Rows.Single(r => r.Key == "아이라_격투가").IsSelected);

        // 게임에서 캐릭터를 바꿔 접속 중이 도적으로 옮겨 가도, 사용자가 고른 줄은 그대로다
        f.Board.Apply(Snap(Acct("A", Char("아이라_격투가", combat: 88_000), Char("바람_도적", "도적", "바람", 99_000, current: true), archer)));
        Assert.Equal("바람_궁수", f.Board.SelectedRow?.Key);
        Assert.Equal("바람_궁수", f.Board.Card.Key);
    }

    [Fact]
    public void Selection_WithoutAUserPick_FollowsTheCurrentCharacter()
    {
        var (aira, rogue, _, _) = Sample();
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", aira, rogue)));
        Assert.Equal("아이라_격투가", f.Board.SelectedRow?.Key);
        f.Board.Apply(Snap(Acct("A", Char("아이라_격투가", combat: 88_000), Char("바람_도적", "도적", "바람", 99_000, current: true))));
        Assert.Equal("바람_도적", f.Board.SelectedRow?.Key);           // 고른 적이 없으면 접속 중을 따라간다
    }

    [Fact]
    public void Selection_WhenThePickedRowDisappears_FallsBackToTheDefault()
    {
        var (aira, rogue, archer, _) = Sample();
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", aira, rogue, archer)));
        f.Board.SelectRow(f.Board.Rows.Single(r => r.Key == "바람_궁수"));
        f.Board.Apply(Snap(Acct("A", aira, rogue)));
        Assert.Equal("아이라_격투가", f.Board.SelectedRow?.Key);
    }

    [Fact]
    public void SelectingAnotherAccount_PicksThatAccountsDefault_AndStaysOnItAcrossRefreshes()
    {
        var (aira, rogue, archer, bard) = Sample();
        var f = new Fixture();
        var accountChanged = 0;
        f.Board.AccountPicked += () => accountChanged++;
        f.Board.Apply(Snap(Acct("A", aira), Acct("B", rogue, archer), Acct("C", bard)));
        f.Board.SelectRow(f.Board.Rows[0]);   // 같은 줄을 다시 눌러도 아무 일 없음

        f.Board.SelectedAccount = f.Board.Accounts.Single(a => a.Id == "B");   // 탭을 누름
        Assert.Equal(1, accountChanged);
        Assert.Equal(["바람_도적", "바람_궁수"], f.Board.Rows.Select(r => r.Key));
        Assert.Equal("바람_도적", f.Board.SelectedRow?.Key);                   // 그 계정의 기본 = 접속 중이 없으니 첫 줄
        Assert.Equal("바람_도적", f.Board.Card.Key);

        f.Board.Apply(Snap(Acct("A", aira), Acct("B", rogue, archer), Acct("C", bard)));
        Assert.Equal("B", f.Board.SelectedAccount?.Id);                         // 갱신 뒤에도 고른 계정 유지
        Assert.Equal(["바람_도적", "바람_궁수"], f.Board.Rows.Select(r => r.Key));

        f.Board.SelectedAccount = f.Board.Accounts.Single(a => a.Id == "A");   // 접속 중 계정으로 돌아오면 접속 중 캐릭터
        Assert.Equal("아이라_격투가", f.Board.SelectedRow?.Key);
    }

    [Fact]
    public void Refresh_UpdatesInPlace_KeepingTheSameObjects_AndMovesRowsWhenTheOrderChanges()
    {
        var (aira, rogue, archer, _) = Sample();
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", aira, rogue, archer)));
        var airaRow = f.Board.Rows.Single(r => r.Key == "아이라_격투가");
        var cavrak = airaRow.Cells.Single(c => c.Id == "raid_cavrak");
        var tab = f.Board.Accounts[0];
        Assert.True(cavrak.IsDone);

        // 격투가의 전투력이 도적을 앞지르고 카브락이 미완료로 바뀐 새 현황
        var airaNew = Char("아이라_격투가", "격투가", "아이라", 120_000, current: true, hw: [St("raid_cavrak", "todo")]);
        f.Board.Apply(Snap(Acct("A", airaNew, rogue, archer)));

        Assert.Same(airaRow, f.Board.Rows.Single(r => r.Key == "아이라_격투가"));      // 같은 줄 객체
        Assert.Same(cavrak, airaRow.Cells.Single(c => c.Id == "raid_cavrak"));         // 같은 칸 객체 (맥동이 끊기지 않는다)
        Assert.Same(tab, f.Board.Accounts[0]);
        Assert.True(cavrak.IsTodo);
        Assert.Equal(["아이라_격투가", "바람_도적", "바람_궁수"], f.Board.Rows.Select(r => r.Key));   // 순서만 바뀜
    }

    [Fact]
    public void NoAccounts_MeansNoCard()
    {
        var f = new Fixture();
        f.Board.Apply(Snap());
        Assert.False(f.Board.HasAccounts);
        Assert.False(f.Board.HasCard);
        Assert.Empty(f.Board.Rows);
        Assert.Null(f.Board.SelectedAccount);
    }

    // ── 카드 ──

    [Fact]
    public void Card_ShowsTheTwelveItems_ForTheSelectedCharacter()
    {
        var c = Char("아이라_격투가", current: true, combat: 88_737, title: "이거밖에 안 되심?", map: "페카 고분 심층 2층 3구역", weight: WeightView.Of(854.5651, 1030), nick: "아이라");
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", c)));
        var card = f.Board.Card;
        Assert.Equal("아이라", card.Name);
        Assert.Equal("격투가 Lv.100", card.ClassLevel);
        Assert.Equal("〈이거밖에 안 되심?〉", card.Title);
        Assert.Equal("페카 고분 심층 2층 3구역", card.MapText);
        Assert.True(card.IsLive);
        Assert.Equal("접속 중", card.LastSeenText);
        Assert.Equal([("전투력", "88,737"), ("마도 저항", "3,000"), ("생활력", "1,200"), ("매력", "5,000")], card.Tiles.Select(t => (t.Label, t.Value)));
        Assert.Equal("은동전", card.Silver?.Label);
        Assert.Equal("마족 공물", card.Tribute?.Label);
        Assert.Equal("854.6/1,030 kg", card.Weight?.ValueText);
    }

    [Fact]
    public void Card_NotLive_ShowsLastPlaceLastSeenAndEstimates()
    {
        var withMap = Char("바람_도적", "도적", "바람", map: "콜헨", silver: 40);
        var noMap = Char("바람_궁수", "궁수", "바람", combat: 500, map: null, title: "");
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", withMap, noMap)));
        Assert.Equal("마지막 위치: 콜헨", f.Board.Card.MapText);
        Assert.False(f.Board.Card.IsLive);
        Assert.Equal("마지막 접속 3시간 전", f.Board.Card.LastSeenText);
        Assert.Equal("46/100 예상", f.Board.Card.Silver?.ValueText);       // 3시간 × 2개
        f.Board.SelectRow(f.Board.Rows.Single(r => r.Key == "바람_궁수"));
        Assert.Equal("—", f.Board.Card.MapText);                           // 본 적 없는 맵
        Assert.Equal("칭호 없음", f.Board.Card.Title);
        Assert.False(f.Board.Card.HasTitle);
        Assert.Equal("—", f.Board.Card.Weight?.ValueText);                 // 무게 기록 없음
    }

    [Fact]
    public void Rebuild_RecomputesTimeBasedValues_WithoutNewData()
    {
        var c = Char("바람_도적", "도적", "바람", silver: 40);   // 3시간 전, 40개 → 46개 예상
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", c)));
        Assert.Equal("46/100 예상", f.Board.Card.Silver?.ValueText);
        Assert.Equal("3시간 전", f.Board.Rows[0].LastSeenText);

        f.Clock = Now.AddHours(2);   // 30초 타이머가 흐른 시간만큼 다시 계산한다
        f.Board.Rebuild();
        Assert.Equal("50/100 예상", f.Board.Card.Silver?.ValueText);
        Assert.Equal("5시간 전", f.Board.Rows[0].LastSeenText);
        Assert.Equal("마지막 접속 5시간 전", f.Board.Card.LastSeenText);
    }

    // ── 리셋 경계 ──

    [Fact]
    public void ResetCrossed_AfterTheDailyOrWeeklyReset_UntilTheNextApply()
    {
        var f = new Fixture();
        Assert.False(f.Board.ResetCrossed());                         // 읽은 적이 없으면 아니다
        // 수요일 05:50 KST
        f.Clock = new DateTimeOffset(2026, 10, 14, 5, 50, 0, TimeSpan.FromHours(9));
        f.Board.Apply(Snap(Acct("A", Char("a_1"))));
        f.Clock = new DateTimeOffset(2026, 10, 14, 5, 59, 59, TimeSpan.FromHours(9));
        Assert.False(f.Board.ResetCrossed());
        f.Clock = new DateTimeOffset(2026, 10, 14, 6, 0, 1, TimeSpan.FromHours(9));   // 일일 리셋을 지남
        Assert.True(f.Board.ResetCrossed());
        f.Board.Rebuild();
        Assert.True(f.Board.ResetCrossed());                          // 시간만 흐른 갱신은 다시 읽은 것이 아니다
        f.Board.Apply(Snap(Acct("A", Char("a_1"))));
        Assert.False(f.Board.ResetCrossed());                         // 다시 읽으면 새 주기

        // 월요일 06:00 KST 주간 리셋
        f.Clock = new DateTimeOffset(2026, 10, 19, 5, 0, 0, TimeSpan.FromHours(9));
        f.Board.Apply(Snap(Acct("A", Char("a_1"))));
        f.Clock = new DateTimeOffset(2026, 10, 19, 6, 0, 0, TimeSpan.FromHours(9));
        Assert.True(f.Board.ResetCrossed());
    }

    // ── 칸 클릭 → 명령 ──

    private static Fixture WithRow(Action<Fixture>? configure = null, params CharacterView[] extra)
    {
        var f = new Fixture();
        configure?.Invoke(f);
        var aira = Char("아이라_격투가", current: true, hw: [St("raid_cavrak", "done", evidence: "원정의 증거 +20"), St("weekly_vanguard_breach", "todo", remaining: 2)]);
        f.Board.Apply(Snap(Acct("A", [aira, .. extra])));
        return f;
    }

    private static CellViewModel Cell(Fixture f, string id, string key = "아이라_격투가") => f.Board.Rows.Single(r => r.Key == key).Cells.Single(c => c.Id == id);

    [Fact]
    public async Task Click_NotDone_SetsCompleted_AndDone_SetsNotCompleted_ForTheRowsCharacter()
    {
        var f = WithRow();
        await f.Board.ToggleCellAsync(Cell(f, "weekly_vanguard_breach"));    // 미완료 → 완료
        await f.Board.ToggleCellAsync(Cell(f, "raid_cavrak"));               // 완료 → 미완료
        await f.Board.ToggleCellAsync(Cell(f, "raid_airel"));                // 미확인 → 완료
        Assert.Equal(
            [("weekly_vanguard_breach", (bool?)true, (int?)null, "아이라_격투가"), ("raid_cavrak", false, null, "아이라_격투가"), ("raid_airel", true, null, "아이라_격투가")],
            f.Actions.Sets);
        Assert.Equal(3, f.Refreshes);            // 끝나면 다시 읽는다
        Assert.False(f.Board.NoticeVisible);
    }

    [Fact]
    public async Task Click_OnAnotherCharacter_UsesThatRowsKey_NotTheCurrentOne()
    {
        var f = WithRow(null, Char("바람_도적", "도적", "바람", 99_000));
        await f.Board.ToggleCellAsync(Cell(f, "daily_day_dungeon", "바람_도적"));
        Assert.Equal(("daily_day_dungeon", (bool?)true, (int?)null, "바람_도적"), f.Actions.Sets.Single());
    }

    [Fact]
    public async Task Click_NullCell_DoesNothing()
    {
        var f = WithRow();
        await f.Board.ToggleCellAsync(null);
        Assert.Empty(f.Actions.Sets);
    }

    [Fact]
    public async Task Click_FailedCommand_ShowsANotice_AndStillRefreshes()
    {
        var f = WithRow(x => x.Actions.OnSet = _ => HomeworkCommandResult.Fail(CommandError.UnknownCharacter, "알 수 없는 캐릭터입니다."));
        await f.Board.ToggleCellAsync(Cell(f, "raid_airel"));
        Assert.True(f.Board.NoticeVisible);
        Assert.Equal("알 수 없는 캐릭터입니다.", f.Board.NoticeText);
        Assert.Equal(1, f.Refreshes);

        f.Actions.OnSet = _ => HomeworkCommandResult.Fail(CommandError.StorageUnavailable, "IOException 상세");
        await f.Board.ToggleCellAsync(Cell(f, "raid_airel"));
        Assert.Contains("저장하지 못했습니다", f.Board.NoticeText);   // 잠김 같은 실패는 쉬운 말로
        Assert.DoesNotContain("IOException", f.Board.NoticeText);

        f.Board.DismissNotice();
        Assert.False(f.Board.NoticeVisible);
    }

    [Fact]
    public async Task Click_CommandThrows_ShowsANotice_InsteadOfCrashing()
    {
        var f = WithRow(x => x.Actions.OnSet = _ => throw new IOException("잠김"));
        await f.Board.ToggleCellAsync(Cell(f, "raid_airel"));
        Assert.True(f.Board.NoticeVisible);
        Assert.Equal(1, f.Refreshes);
    }

    [Fact]
    public async Task Notice_HidesItselfAfterTheDuration()
    {
        var f = new Fixture(notice: TimeSpan.FromMilliseconds(60));
        f.Board.Apply(Snap(Acct("A", Char("아이라_격투가", current: true))));
        f.Actions.OnSet = _ => HomeworkCommandResult.Fail(CommandError.Validation, "실패");
        await f.Board.ToggleCellAsync(Cell(f, "raid_airel"));
        Assert.True(f.Board.NoticeVisible);
        await Wait.UntilAsync(() => !f.Board.NoticeVisible, "알림이 저절로 사라짐");
    }

    // ── 필드 보스 ──

    [Fact]
    public async Task FieldBoss_ClickWhenNotDone_OpensThePicker_WithoutCallingTheCommand()
    {
        var f = WithRow();
        await f.Board.ToggleCellAsync(Cell(f, "field_boss"));
        Assert.Empty(f.Actions.Sets);
        var picker = f.Board.BossPicker;
        Assert.NotNull(picker);
        Assert.Equal("아이라_격투가", picker!.CharacterKey);
        Assert.Equal(["페리", "크라브바흐", "크라마", "드로흐에넴", "토르모그", "앙그르바한"], picker.Bosses.Select(b => b.Title));
        Assert.Contains("아이라 · 격투가", picker.Title);
        f.Board.CloseBossPicker();
        Assert.Null(f.Board.BossPicker);
    }

    [Fact]
    public async Task FieldBoss_ChoosingABoss_CompletesThatBoss_ForTheRowsCharacter_AndClosesThePicker()
    {
        var f = WithRow(null, Char("바람_도적", "도적", "바람", 99_000));
        await f.Board.ToggleCellAsync(Cell(f, "field_boss", "바람_도적"));
        await f.Board.ChooseBossAsync(HomeworkBoardLayout.Bosses.Single(b => b.Title == "토르모그"));
        Assert.Equal(("fieldboss_tormog", (bool?)true, (int?)null, "바람_도적"), f.Actions.Sets.Single());
        Assert.Null(f.Board.BossPicker);
        Assert.Equal(1, f.Refreshes);
        await f.Board.ChooseBossAsync(HomeworkBoardLayout.Bosses[0]);   // 팝업이 닫힌 뒤의 선택은 무시
        Assert.Single(f.Actions.Sets);
    }

    [Fact]
    public async Task FieldBoss_ClickWhenDone_ClearsOnlyTheBossThatWasCleared()
    {
        var f = WithRow(null, Char("바람_도적", "도적", "바람", 99_000, boss: new HomeworkPoolStatus("done", "크라마")));
        await f.Board.ToggleCellAsync(Cell(f, "field_boss", "바람_도적"));
        Assert.Equal(("fieldboss_krama", (bool?)false, (int?)null, "바람_도적"), f.Actions.Sets.Single());
        Assert.Null(f.Board.BossPicker);
    }

    [Fact]
    public async Task FieldBoss_ClickWhenDoneAndTheBossIsUnknown_ClearsAllSix()
    {
        var f = WithRow(null, Char("바람_도적", "도적", "바람", 99_000, boss: new HomeworkPoolStatus("done", null)));
        await f.Board.ToggleCellAsync(Cell(f, "field_boss", "바람_도적"));
        Assert.Equal(HomeworkBoardLayout.Bosses.Select(b => b.Id), f.Actions.Sets.Select(s => s.Id));
        Assert.All(f.Actions.Sets, s => Assert.Equal((false, "바람_도적"), (s.Completed ?? true, s.Key)));
    }

    [Fact]
    public async Task FieldBoss_UnknownNameNotInTheSix_ClearsAllSix()
    {
        var f = WithRow(null, Char("바람_도적", "도적", "바람", 99_000, boss: new HomeworkPoolStatus("done", "모르는 보스")));
        await f.Board.ToggleCellAsync(Cell(f, "field_boss", "바람_도적"));
        Assert.Equal(6, f.Actions.Sets.Count);
    }

    // ── 이름 바꾸기 ──

    [Theory]
    [InlineData("", null)]
    [InlineData("그림자", null)]
    [InlineData("12345678901234567890", null)]            // 20자까지
    [InlineData("  양옆 공백  ", null)]
    public void NicknameValidation_Accepts(string text, string? error) => Assert.Equal(error, HomeworkBoardViewModel.ValidateNickname(text));

    [Theory]
    [InlineData("123456789012345678901")]                 // 21자
    [InlineData("줄\n바꿈")]
    public void NicknameValidation_Rejects(string text) => Assert.NotNull(HomeworkBoardViewModel.ValidateNickname(text));

    [Fact]
    public async Task Rename_BeginStartsFromTheCurrentNickname_AndEnterSavesTheTrimmedName()
    {
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", Char("바람_도적", "도적", "바람", 99_000, nick: "그림자"))));
        f.Board.BeginRename();
        Assert.True(f.Board.Card.IsRenaming);
        Assert.Equal("그림자", f.Board.Card.RenameText);

        f.Board.Card.RenameText = "  새 이름  ";
        await f.Board.CommitRenameAsync();
        Assert.False(f.Board.Card.IsRenaming);
        Assert.Equal(("바람_도적", (string?)"새 이름"), f.Actions.Renames.Single());
        Assert.Equal(1, f.Refreshes);
    }

    [Fact]
    public async Task Rename_EmptyClearsTheNickname_UnchangedDoesNotSave_EscapeCancels()
    {
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", Char("바람_도적", "도적", "바람", 99_000, nick: "그림자"))));

        f.Board.BeginRename();
        f.Board.Card.RenameText = "그림자";                // 바뀐 게 없다
        await f.Board.CommitRenameAsync();
        Assert.Empty(f.Actions.Renames);
        Assert.False(f.Board.Card.IsRenaming);

        f.Board.BeginRename();
        f.Board.Card.RenameText = "   ";                  // 비우면 닉네임을 지운다
        await f.Board.CommitRenameAsync();
        Assert.Equal(("바람_도적", (string?)""), f.Actions.Renames.Single());

        f.Board.BeginRename();
        f.Board.Card.RenameText = "저장 안 함";
        f.Board.CancelRename();                           // Esc
        Assert.False(f.Board.Card.IsRenaming);
        Assert.Single(f.Actions.Renames);
    }

    [Fact]
    public async Task Rename_TooLong_StaysInEditing_WithAReason_AndDoesNotCallTheCommand()
    {
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", Char("바람_도적", "도적", "바람", 99_000))));
        f.Board.BeginRename();
        f.Board.Card.RenameText = new string('가', 21);
        await f.Board.CommitRenameAsync();
        Assert.True(f.Board.Card.IsRenaming);
        Assert.True(f.Board.Card.HasRenameError);
        Assert.Contains("20자", f.Board.Card.RenameError);
        Assert.Empty(f.Actions.Renames);
        f.Board.Card.RenameText = new string('가', 20);
        await f.Board.CommitRenameAsync();
        Assert.Single(f.Actions.Renames);
        Assert.False(f.Board.Card.IsRenaming);
        Assert.False(f.Board.Card.HasRenameError);
    }

    [Fact]
    public async Task Rename_Failure_ShowsANotice()
    {
        var f = new Fixture();
        f.Actions.OnRename = () => new CommandResult(CommandError.UnknownCharacter, "알 수 없는 캐릭터입니다.");
        f.Board.Apply(Snap(Acct("A", Char("바람_도적", "도적", "바람", 99_000))));
        f.Board.BeginRename();
        f.Board.Card.RenameText = "새 이름";
        await f.Board.CommitRenameAsync();
        Assert.Equal("알 수 없는 캐릭터입니다.", f.Board.NoticeText);
    }

    [Fact]
    public void Rename_RefreshKeepsTheInputWhileEditing_ButSelectingAnotherRowCancelsIt()
    {
        var rogue = Char("바람_도적", "도적", "바람", 99_000);
        var archer = Char("바람_궁수", "궁수", "바람", 84_000);
        var f = new Fixture();
        f.Board.Apply(Snap(Acct("A", rogue, archer)));
        f.Board.BeginRename();
        f.Board.Card.RenameText = "입력 중";

        f.Board.Apply(Snap(Acct("A", rogue, archer)));            // 갱신이 와도 입력은 그대로
        Assert.True(f.Board.Card.IsRenaming);
        Assert.Equal("입력 중", f.Board.Card.RenameText);

        f.Board.SelectRow(f.Board.Rows.Single(r => r.Key == "바람_궁수"));   // 다른 줄을 고르면 취소
        Assert.False(f.Board.Card.IsRenaming);
        Assert.Equal("바람_궁수", f.Board.Card.Key);
    }

    [Fact]
    public void Rename_WithoutACharacter_DoesNothing()
    {
        var f = new Fixture();
        f.Board.Apply(Snap());
        f.Board.BeginRename();
        Assert.False(f.Board.Card.IsRenaming);
    }
}
