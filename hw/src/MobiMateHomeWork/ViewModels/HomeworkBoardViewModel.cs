using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MobiMateHomeWork.Services;

namespace MobiMateHomeWork.ViewModels;

/// <summary>
/// 숙제 표 + 캐릭터 카드의 화면 모델 (웹앱 homework.tsx의 HW_GROUPS·cellsOf·필드 보스 해제 규칙과 같다).
/// 창·서비스 없이 단위 테스트할 수 있게 쓰기 동작(<see cref="IHomeworkActions"/>)·다시 읽기·UI 스레드 넘기기·시계를 주입받는다.
/// 값 갱신은 새로 만들지 않고 줄·칸·탭을 그 자리에서 고친다 — 선택·스크롤·맥동이 갱신에 끊기지 않는다.
/// </summary>
public sealed partial class HomeworkBoardViewModel : ObservableObject
{
    public static readonly TimeSpan DefaultNoticeDuration = TimeSpan.FromSeconds(7);

    private readonly IHomeworkActions _actions;
    private readonly Func<Task> _refresh;
    private readonly Action<Action> _post;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _noticeDuration;
    private CharactersSnapshot? _last;
    private bool _applying;                 // 코드가 선택을 바꾸는 중(사용자 선택과 구분)
    private string? _pickedAccountId;       // 사용자가 직접 고른 계정·캐릭터. 없으면 접속 중 기준 기본값을 따라간다
    private string? _pickedKey;
    private DateTimeOffset _cycleDaily, _cycleWeekly;
    private int _noticeSeq;

    public HomeworkBoardViewModel(IHomeworkActions actions, Func<Task>? refresh = null, Action<Action>? post = null, Func<DateTimeOffset>? clock = null, TimeSpan? noticeDuration = null)
    {
        _actions = actions;
        _refresh = refresh ?? (() => Task.CompletedTask);
        _post = post ?? (a => a());
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _noticeDuration = noticeDuration ?? DefaultNoticeDuration;
    }

    public ObservableCollection<AccountTab> Accounts { get; } = [];
    public ObservableCollection<CharacterRowViewModel> Rows { get; } = [];
    public CharacterCardViewModel Card { get; } = new();

    [ObservableProperty] private AccountTab? _selectedAccount;
    [ObservableProperty] private CharacterRowViewModel? _selectedRow;
    [ObservableProperty] private bool _hasAccounts;
    [ObservableProperty] private bool _hasCard;
    [ObservableProperty] private BossPickerViewModel? _bossPicker;
    [ObservableProperty] private string _noticeText = "";
    [ObservableProperty] private bool _noticeVisible;

    public IReadOnlyList<GroupDef> Groups => HomeworkBoardLayout.Groups;

    // ── 화면 만들기 ───────────────────────────────────────────

    /// <summary>
    /// 접속 중 계정을 맨 앞에, 그다음은 사용자가 정한 순서(있을 때, 정하지 않은 계정은 뒤로 — 웹앱 규칙)와 계정 최고 전투력 순.
    /// </summary>
    public static IReadOnlyList<AccountView> OrderAccounts(IEnumerable<AccountView> accounts)
    {
        var list = accounts.ToList();
        var manual = list.Any(a => a.ManualRank >= 0);
        return [.. list
            .OrderByDescending(a => a.HasCurrent)
            .ThenBy(a => manual ? (a.ManualRank < 0 ? int.MaxValue : a.ManualRank) : 0)
            .ThenByDescending(a => a.TopCombat)];
    }

    /// <summary>캐릭터 현황을 받아 표·탭·카드를 고친다 (UI 스레드).</summary>
    public void Apply(CharactersSnapshot snap)
    {
        _last = snap;
        var now = _clock();
        _cycleDaily = KstClock.LastDailyReset(now);
        _cycleWeekly = KstClock.LastWeeklyReset(now);
        Redraw();
    }

    /// <summary>시간만 흐른 갱신(30초 타이머): 같은 현황으로 마지막 접속·예상 보유량·막대를 다시 계산한다.</summary>
    public void Rebuild()
    {
        if (_last != null) Redraw();
    }

    /// <summary>마지막으로 읽은 뒤 일일·주간 리셋 시각을 지났는가. 지났으면 다시 읽어 보드를 새로 만들어야 한다.</summary>
    public bool ResetCrossed()
    {
        if (_last == null) return false;
        var now = _clock();
        return KstClock.LastDailyReset(now) != _cycleDaily || KstClock.LastWeeklyReset(now) != _cycleWeekly;
    }

    private void Redraw()
    {
        var snap = _last;
        var now = _clock();
        _applying = true;
        try
        {
            var accounts = snap == null ? [] : OrderAccounts(snap.Accounts);
            Reconcile(Accounts, accounts, a => a.Id, t => t.Id, id => new AccountTab(id), (tab, a) =>
            {
                tab.Name = a.Name;
                tab.MemberCount = a.Members.Count;
                tab.IsCurrent = a.HasCurrent;
                tab.Remaining = a.Members.Sum(m => HomeworkBoardLayout.CellsOf(m).Count(c => c.State != CellState.Done));
            });
            HasAccounts = Accounts.Count > 0;
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == _pickedAccountId)
                ?? Accounts.FirstOrDefault(a => a.IsCurrent) ?? Accounts.FirstOrDefault();
            FillRows(now);
        }
        finally { _applying = false; }
    }

    partial void OnSelectedAccountChanged(AccountTab? value)
    {
        if (_applying) return;
        // 사용자가 탭을 눌렀다: 그 계정의 기본 캐릭터를 고르고, 갱신이 와도 이 계정을 유지한다
        _pickedAccountId = value?.Id;
        _pickedKey = null;
        _applying = true;
        try { FillRows(_clock()); }
        finally { _applying = false; }
        AccountPicked?.Invoke();
    }

    /// <summary>사용자가 다른 계정 탭을 눌렀다 (창이 표를 맨 위로 되돌리는 데 쓴다).</summary>
    public event Action? AccountPicked;

    private void FillRows(DateTimeOffset now)
    {
        var account = _last?.Accounts.FirstOrDefault(a => a.Id == SelectedAccount?.Id);
        var members = account?.Members.OrderByDescending(m => m.Combat).ToList() ?? [];   // 전투력 내림차순
        Reconcile(Rows, members, m => m.Key, r => r.Key, k => new CharacterRowViewModel(k), (row, m) => row.Apply(m, now));

        var key = members.Any(m => m.Key == _pickedKey) ? _pickedKey
            : members.FirstOrDefault(m => m.IsCurrent)?.Key ?? members.FirstOrDefault()?.Key;
        SelectedRow = Rows.FirstOrDefault(r => r.Key == key);
        foreach (var r in Rows) r.IsSelected = ReferenceEquals(r, SelectedRow);
        ShowCard(members.FirstOrDefault(m => m.Key == key), now);
    }

    private void ShowCard(CharacterView? view, DateTimeOffset now)
    {
        if (view == null)
        {
            HasCard = false;
            Card.CancelRename();
            Card.Key = "";
            return;
        }
        Card.Apply(view, now);
        HasCard = true;
    }

    /// <summary>줄을 눌러 선택한다 (그 줄의 칸을 눌러도 선택된다).</summary>
    public void SelectRow(CharacterRowViewModel? row)
    {
        if (row == null || ReferenceEquals(row, SelectedRow)) return;
        _pickedKey = row.Key;
        _applying = true;
        try { FillRows(_clock()); }
        finally { _applying = false; }
    }

    /// <summary>목표 목록과 같은 순서·같은 키가 되게 컬렉션을 그 자리에서 고친다 (없어진 것은 지우고, 새것은 끼우고, 순서가 다르면 옮긴다).</summary>
    private static void Reconcile<TItem, TSrc>(ObservableCollection<TItem> target, IReadOnlyList<TSrc> source, Func<TSrc, string> keyOf,
        Func<TItem, string> targetKey, Func<string, TItem> create, Action<TItem, TSrc> update)
    {
        var wanted = source.GroupBy(keyOf).Select(g => g.First()).ToList();
        var keys = wanted.Select(keyOf).ToHashSet();
        for (var i = target.Count - 1; i >= 0; i--)
            if (!keys.Contains(targetKey(target[i]))) target.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            var key = keyOf(wanted[i]);
            var at = -1;
            for (var j = i; j < target.Count; j++) if (targetKey(target[j]) == key) { at = j; break; }
            if (at < 0) target.Insert(i, create(key));
            else if (at != i) target.Move(at, i);
            update(target[i], wanted[i]);
        }
    }

    // ── 칸 누르기 ─────────────────────────────────────────────

    /// <summary>
    /// 칸 클릭 = 완료 ↔ 미완료 수동 전환. 필드 보스는 완료로 바꿀 때 보스를 고르는 팝업을 열고,
    /// 완료 → 미완료는 넘긴 보스만 해제한다(이름을 모르면 여섯 모두). 명령은 백그라운드에서 부르고 끝나면 다시 읽는다.
    /// 대상 캐릭터는 줄의 키를 항상 명시한다 — 그사이 게임에서 캐릭터를 바꿔도 다른 캐릭터에 체크되지 않는다.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ToggleCellAsync(CellViewModel? cell)
    {
        if (cell == null) return;
        var key = cell.CharacterKey;
        if (!cell.IsBoss)
        {
            var target = cell.State != CellState.Done;
            var id = cell.Id;
            await RunAsync(() => [_actions.SetHomework(id, target, null, key)]);
            return;
        }
        if (cell.State != CellState.Done)
        {
            OpenBossPicker(key);
            return;
        }
        var note = cell.Note;
        var named = HomeworkBoardLayout.Bosses.Where(b => b.Title == note).ToList();
        var ids = (named.Count > 0 ? named : [.. HomeworkBoardLayout.Bosses]).Select(b => b.Id).ToList();
        await RunAsync(() => [.. ids.Select(id => _actions.SetHomework(id, false, null, key))]);
    }

    /// <summary>필드 보스 고르기 팝업을 연다.</summary>
    public void OpenBossPicker(string characterKey)
    {
        var row = Rows.FirstOrDefault(r => r.Key == characterKey);
        BossPicker = new BossPickerViewModel(characterKey, row?.DisplayName ?? characterKey, HomeworkBoardLayout.Bosses);
    }

    [RelayCommand]
    public void CloseBossPicker() => BossPicker = null;

    /// <summary>팝업에서 보스를 골랐다: 그 보스를 완료로 체크하고 팝업을 닫는다.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ChooseBossAsync(BossDef? boss)
    {
        var picker = BossPicker;
        BossPicker = null;
        if (boss == null || picker == null) return;
        await RunAsync(() => [_actions.SetHomework(boss.Id, true, null, picker.CharacterKey)]);
    }

    private async Task RunAsync(Func<IReadOnlyList<HomeworkCommandResult>> work)
    {
        IReadOnlyList<HomeworkCommandResult> results;
        try { results = await Task.Run(work); }   // 파일 저장이 UI 스레드를 막지 않게
        catch (Exception ex) { results = [HomeworkCommandResult.Fail(CommandError.StorageUnavailable, ex.Message)]; }
        if (results.FirstOrDefault(r => !r.Ok) is { } bad) _post(() => ShowNotice(Describe(bad.Error, bad.Message)));
        try { await _refresh(); }
        catch (Exception) { /* 다음 알림에 다시 */ }
    }

    private static string Describe(CommandError error, string? message) => error switch
    {
        CommandError.StorageUnavailable => "숙제 기록을 저장하지 못했습니다. 다른 프로그램이 파일을 쓰는 중일 수 있습니다 — 잠시 뒤 다시 눌러 주세요.",
        _ => string.IsNullOrWhiteSpace(message) ? "바꾸지 못했습니다." : message,
    };

    // ── 알림 띠(토스트) ───────────────────────────────────────

    public void ShowNotice(string text)
    {
        NoticeText = text;
        NoticeVisible = true;
        var seq = Interlocked.Increment(ref _noticeSeq);
        _ = Task.Delay(_noticeDuration).ContinueWith(_ => _post(() => { if (seq == Volatile.Read(ref _noticeSeq)) NoticeVisible = false; }), TaskScheduler.Default);
    }

    [RelayCommand]
    public void DismissNotice()
    {
        Interlocked.Increment(ref _noticeSeq);
        NoticeVisible = false;
    }

    // ── 이름 바꾸기 ───────────────────────────────────────────

    /// <summary>닉네임 검증. 문제가 없으면 null, 있으면 사용자에게 보일 글.</summary>
    public static string? ValidateNickname(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length > HomeworkCommands.MaxNicknameLength) return $"이름은 {HomeworkCommands.MaxNicknameLength}자 이하로 써 주세요.";
        if (t.Any(char.IsControl)) return "이름에 쓸 수 없는 글자가 있습니다.";
        return null;
    }

    /// <summary>카드의 이름을 눌렀다: 그 자리에서 바꾸는 입력 상태로. 지금 닉네임(없으면 빈칸)에서 시작한다.</summary>
    [RelayCommand]
    public void BeginRename()
    {
        if (SelectedRow == null || !HasCard) return;
        Card.RenameText = Card.Nickname ?? "";
        Card.RenameError = null;
        Card.IsRenaming = true;
    }

    [RelayCommand]
    public void CancelRename() => Card.CancelRename();

    /// <summary>Enter: 저장. 검증에 걸리면 입력 상태로 남아 이유를 보인다. 비우면 닉네임을 지운다. 바뀐 게 없으면 저장하지 않는다.</summary>
    [RelayCommand]
    public async Task CommitRenameAsync()
    {
        if (!Card.IsRenaming) return;
        var key = Card.Key;
        var text = (Card.RenameText ?? "").Trim();
        if (ValidateNickname(text) is { } error) { Card.RenameError = error; return; }
        if (text == (Card.Nickname ?? ""))
        {
            Card.CancelRename();
            return;
        }
        Card.CancelRename();   // 입력 칸은 바로 닫고, 저장은 뒤에서 한다
        CommandResult result;
        try { result = await Task.Run(() => _actions.RenameCharacter(key, text)); }
        catch (Exception ex) { result = new CommandResult(CommandError.StorageUnavailable, ex.Message); }
        if (!result.Ok) _post(() => ShowNotice(Describe(result.Error, result.Message)));
        try { await _refresh(); }
        catch (Exception) { }
    }
}
