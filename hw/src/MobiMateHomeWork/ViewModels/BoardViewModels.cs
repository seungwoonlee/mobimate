using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MobiMateHomeWork.Services;

namespace MobiMateHomeWork.ViewModels;

/// <summary>계정 탭 하나. Remaining = 그 계정의 남은 숙제 칸 수(모든 캐릭터의 완료가 아닌 칸), 0이면 배지는 ✓.</summary>
public sealed partial class AccountTab : ObservableObject
{
    public string Id { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _remaining;
    [ObservableProperty] private int _memberCount;
    [ObservableProperty] private bool _isCurrent;

    public AccountTab(string id) => Id = id;

    public bool AllDone => Remaining == 0;
    public string Badge => Remaining == 0 ? "✓" : Remaining.ToString(CultureInfo.InvariantCulture);
    public string Tooltip => $"{Name} 계정 · 캐릭터 {MemberCount}명 · " + (Remaining == 0 ? "숙제를 모두 마쳤습니다" : $"남은 숙제 칸 {Remaining}개") + (IsCurrent ? " · 접속 중" : "");

    partial void OnRemainingChanged(int value) { OnPropertyChanged(nameof(AllDone)); OnPropertyChanged(nameof(Badge)); OnPropertyChanged(nameof(Tooltip)); }
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Tooltip));
    partial void OnMemberCountChanged(int value) => OnPropertyChanged(nameof(Tooltip));
    partial void OnIsCurrentChanged(bool value) => OnPropertyChanged(nameof(Tooltip));
}

/// <summary>숙제 표의 칸 하나. 갱신 때 새로 만들지 않고 값만 고친다(진행 중 맥동이 끊기지 않게).</summary>
public sealed partial class CellViewModel : ObservableObject
{
    public CellDef Def { get; }
    public string CharacterKey { get; }

    public CellViewModel(CellDef def, string characterKey)
    {
        Def = def;
        CharacterKey = characterKey;
    }

    public string Id => Def.Id;
    public string Label => Def.Label;
    public string Line1 => Def.Line1;
    public string Line2 => Def.Line2;
    public bool IsBoss => Def.IsBoss;

    [ObservableProperty] private CellState _state = CellState.Unknown;
    [ObservableProperty] private int? _left;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private bool _inProgress;
    [ObservableProperty] private bool _suggested;
    [ObservableProperty] private string _tooltip = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _automationName = "";

    public bool HasLeft => Left != null;
    public string LeftText => Left is { } n ? $"{n}회" : "";
    public bool IsDone => State == CellState.Done;
    public bool IsTodo => State == CellState.Todo;
    public bool IsUnknown => State == CellState.Unknown;
    public bool HasLine2 => Line2.Length > 0;

    partial void OnStateChanged(CellState value) { OnPropertyChanged(nameof(IsDone)); OnPropertyChanged(nameof(IsTodo)); OnPropertyChanged(nameof(IsUnknown)); }
    partial void OnLeftChanged(int? value) { OnPropertyChanged(nameof(HasLeft)); OnPropertyChanged(nameof(LeftText)); }

    public void Apply(CellData d)
    {
        State = d.State;
        Left = d.Left;
        Note = d.Note;
        InProgress = d.InProgress;
        Suggested = d.Suggestion != null;
        Tooltip = HomeworkBoardLayout.TooltipOf(d);
        // 넓은 단계의 상태 줄: 필드 보스는 완료하면 넘긴 보스 이름
        StatusText = d.Def.IsBoss && d.State == CellState.Done && !string.IsNullOrWhiteSpace(d.Note) ? d.Note : HomeworkBoardLayout.StatusText(d);
        AutomationName = $"{d.Def.Label} {HomeworkBoardLayout.StatusText(d)}" + (d.Def.IsBoss && !string.IsNullOrWhiteSpace(d.Note) ? $" ({d.Note})" : "")
            + $" — 눌러서 {(d.State == CellState.Done ? "미완료로" : d.Def.IsBoss ? "넘긴 보스 고르기" : "완료로")} 바꾸기";
    }
}

/// <summary>숙제 표의 묶음(머리 + 그 아래 칸들). 줄마다 같은 모양이라 머리와 줄의 가로 위치가 맞는다.</summary>
public sealed class CellGroupViewModel(GroupDef def, IReadOnlyList<CellViewModel> cells)
{
    public GroupDef Def { get; } = def;
    public string Title => Def.Title;
    public IReadOnlyList<CellViewModel> Cells { get; } = cells;
}

/// <summary>숙제 표의 캐릭터 한 줄.</summary>
public sealed partial class CharacterRowViewModel : ObservableObject
{
    public string Key { get; }

    public CharacterRowViewModel(string key)
    {
        Key = key;
        var groups = new List<CellGroupViewModel>();
        var all = new List<CellViewModel>();
        foreach (var g in HomeworkBoardLayout.Groups)
        {
            var cells = g.Cells.Select(d => new CellViewModel(d, key)).ToList();
            all.AddRange(cells);
            groups.Add(new CellGroupViewModel(g, cells));
        }
        Cells = all;
        Groups = groups;
    }

    public IReadOnlyList<CellViewModel> Cells { get; }
    public IReadOnlyList<CellGroupViewModel> Groups { get; }

    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string? _nickname;
    [ObservableProperty] private string _job = "";
    [ObservableProperty] private string _jobLetter = "?";
    [ObservableProperty] private string _jobColor = JobPalette.Fallback[0];
    [ObservableProperty] private int _level;
    [ObservableProperty] private long _combat;
    [ObservableProperty] private int _doneCount;
    [ObservableProperty] private bool _allDone;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _jobLevelCombat = "";
    [ObservableProperty] private string _lastSeenText = "";
    [ObservableProperty] private string _tooltip = "";
    [ObservableProperty] private string _automationName = "";
    [ObservableProperty] private MeterModel? _silver;
    [ObservableProperty] private MeterModel? _tribute;

    public int Total => Cells.Count;
    public string DoneText => $"{DoneCount}/{Total}";
    public int RemainingCells => Total - DoneCount;

    partial void OnDoneCountChanged(int value) { OnPropertyChanged(nameof(DoneText)); OnPropertyChanged(nameof(RemainingCells)); }

    /// <summary>닉네임, 없으면 "서버 · 직업".</summary>
    public static string NameOf(CharacterView c) => string.IsNullOrWhiteSpace(c.Nickname) ? $"{c.Realm} · {c.Job}" : c.Nickname!;

    public void Apply(CharacterView c, DateTimeOffset now)
    {
        DisplayName = NameOf(c);
        Nickname = string.IsNullOrWhiteSpace(c.Nickname) ? null : c.Nickname;
        Job = c.Job;
        JobLetter = JobPalette.LetterOf(c.Job);
        JobColor = JobPalette.ColorOf(c.Job);
        Level = c.Level;
        Combat = c.Combat;
        IsCurrent = c.IsCurrent;
        var data = HomeworkBoardLayout.CellsOf(c);
        for (var i = 0; i < Cells.Count; i++) Cells[i].Apply(data[i]);
        DoneCount = data.Count(x => x.State == CellState.Done);
        AllDone = DoneCount == Total;
        JobLevelCombat = $"{c.Job} {c.Level} · {CardFormat.CompactCombat(c.Combat)}";
        LastSeenText = CardFormat.Ago(c.LastSeen, now, c.IsCurrent);
        Silver = CardFormat.Coin(MeterKind.Silver, c.Silver, c.IsCurrent, c.LastSeen, now);
        Tribute = CardFormat.Coin(MeterKind.Tribute, c.Tribute, c.IsCurrent, c.LastSeen, now);
        AutomationName = HomeworkBoardLayout.RowAutomationName(DisplayName, DoneCount, Total);
        Tooltip = $"{DisplayName}\n{c.Job} Lv.{c.Level} · 전투력 {CardFormat.Thousands(c.Combat)}\n마지막 접속 {LastSeenText}"
            + (c.Stale ? "\n(다른 캐릭터로 접속한 뒤 재화가 동기화되지 않았습니다)" : "");
    }
}

/// <summary>2×2 타일 하나 (전투력·마도 저항·생활력·매력).</summary>
public sealed record StatTile(string Label, string Value);

/// <summary>선택한 캐릭터의 카드(정보 12항목). 줄이 바뀌어도 이 객체는 그대로 두고 값만 고친다.</summary>
public sealed partial class CharacterCardViewModel : ObservableObject
{
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _nickname;
    [ObservableProperty] private string _jobLetter = "?";
    [ObservableProperty] private string _jobColor = JobPalette.Fallback[0];
    [ObservableProperty] private string _classLevel = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private bool _hasTitle;
    [ObservableProperty] private string _mapText = "—";
    [ObservableProperty] private bool _isLive;
    [ObservableProperty] private string _lastSeenText = "";
    [ObservableProperty] private string _lastSeenTooltip = "";
    [ObservableProperty] private MeterModel? _silver;
    [ObservableProperty] private MeterModel? _tribute;
    [ObservableProperty] private MeterModel? _weight;

    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameText = "";
    [ObservableProperty] private string? _renameError;

    public bool HasRenameError => !string.IsNullOrEmpty(RenameError);
    partial void OnRenameErrorChanged(string? value) => OnPropertyChanged(nameof(HasRenameError));

    public ObservableCollection<StatTile> Tiles { get; } = [];

    public void Apply(CharacterView c, DateTimeOffset now)
    {
        if (Key != c.Key) CancelRename();
        Key = c.Key;
        Name = CharacterRowViewModel.NameOf(c);
        Nickname = string.IsNullOrWhiteSpace(c.Nickname) ? null : c.Nickname;
        JobLetter = JobPalette.LetterOf(c.Job);
        JobColor = JobPalette.ColorOf(c.Job);
        ClassLevel = $"{c.Job} Lv.{c.Level}";
        HasTitle = !string.IsNullOrWhiteSpace(c.Title);
        Title = HasTitle ? $"〈{c.Title.Trim()}〉" : "칭호 없음";
        IsLive = c.IsCurrent;
        MapText = MapOf(c);
        LastSeenText = c.IsCurrent ? "접속 중" : "마지막 접속 " + CardFormat.Ago(c.LastSeen, now);
        LastSeenTooltip = c.LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Silver = CardFormat.Coin(MeterKind.Silver, c.Silver, c.IsCurrent, c.LastSeen, now);
        Tribute = CardFormat.Coin(MeterKind.Tribute, c.Tribute, c.IsCurrent, c.LastSeen, now);
        Weight = CardFormat.Weight(c.Weight);

        var values = new (string Label, long Value)[] { ("전투력", c.Combat), ("마도 저항", c.Mdef), ("생활력", c.Living), ("매력", c.Attract) };
        for (var i = 0; i < values.Length; i++)
        {
            var tile = new StatTile(values[i].Label, CardFormat.Thousands(values[i].Value));
            if (i < Tiles.Count) { if (Tiles[i] != tile) Tiles[i] = tile; }
            else Tiles.Add(tile);
        }
    }

    /// <summary>접속 중이면 실시간 맵(없으면 "접속 중"), 아니면 "마지막 위치: …" 또는 —.</summary>
    public static string MapOf(CharacterView c) =>
        c.IsCurrent ? (string.IsNullOrWhiteSpace(c.Map) ? "접속 중" : c.Map!.Trim())
        : string.IsNullOrWhiteSpace(c.Map) ? "—" : "마지막 위치: " + c.Map!.Trim();

    public void CancelRename()
    {
        IsRenaming = false;
        RenameError = null;
        RenameText = "";
    }
}

/// <summary>필드 보스 고르기 팝업의 상태 (창 안 겹침 화면). 닫혀 있으면 보드의 BossPicker가 null이다.</summary>
public sealed record BossPickerViewModel(string CharacterKey, string CharacterName, IReadOnlyList<BossDef> Bosses)
{
    public string Title => $"필드 보스 (주 1회, 택1) — {CharacterName}";
    public string Hint => "이번 주에 넘긴 보스를 고르세요. 여섯 중 하나만 넘기면 됩니다.";
}
