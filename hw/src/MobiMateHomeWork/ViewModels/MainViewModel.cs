using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MobiMateHomeWork.Hosting;
using MobiMateHomeWork.Layout;
using MobiMateHomeWork.Services;

namespace MobiMateHomeWork.ViewModels;

/// <summary>계정 탭 하나. H3에서는 배지에 캐릭터 수를 보인다(남은 숙제 수는 H4).</summary>
public sealed record AccountTab(string Id, string Name, int Badge, bool IsCurrent);

/// <summary>왼쪽 영역의 임시 캐릭터 줄(H4에서 숙제 표로 바뀐다).</summary>
public sealed record CharacterRow(string Key, string Name, string Detail, bool IsCurrent)
{
    public static CharacterRow From(CharacterView c) => new(c.Key, c.Nickname ?? $"{c.Realm} · {c.Job}",
        $"{c.Job} Lv.{c.Level}  전투력 {c.Combat:N0}", c.IsCurrent);
}

/// <summary>
/// 창 전체의 화면 모델. UI 스레드·창 없이 단위 테스트할 수 있게 <c>post</c>(UI 스레드로 넘기기)와 시계를 주입받는다.
/// 서비스 이벤트는 서비스 스레드에서 올라오므로 화면 상태를 바꾸는 코드는 모두 post를 거친다.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppEvents _events;
    private readonly StatusMonitor _status;
    private readonly GameCli _cli;
    private readonly CharacterSession _session;
    private readonly AppSettingsStore _settings;
    private readonly FirstRead _firstRead;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Action> _post;
    private readonly TimeSpan _refreshDelay;
    private readonly HashSet<AppWarningKind> _dismissed = [];
    private int _refreshScheduled;
    private int _refreshing;
    private int _disposed;

    public MainViewModel(AppEvents events, StatusMonitor status, GameCli cli, CharacterSession session, AppSettingsStore settings, FirstRead firstRead,
        Action<Action>? post = null, Func<DateTimeOffset>? clock = null, TimeSpan? refreshDelay = null)
    {
        _events = events;
        _status = status;
        _cli = cli;
        _session = session;
        _settings = settings;
        _firstRead = firstRead;
        _post = post ?? (a => a());
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _refreshDelay = refreshDelay ?? TimeSpan.FromMilliseconds(200);

        var s = settings.Current;
        _alwaysOnTop = s.AlwaysOnTop;
        _closeToTray = s.CloseToTray;
        _connection = status.State;
        _countdown = ResetCountdown.Format(_clock());
        _isFirstReadPending = !firstRead.Done;
        UpdateBanner();

        events.StatusChanged += OnStatus;
        events.CharactersChanged += OnChanged;
        events.HomeworkChanged += OnHomework;
        events.WarningRaised += OnWarning;
        events.WarningCleared += OnWarningCleared;
    }

    // ── 배치 단계 ──────────────────────────────────────────────

    [ObservableProperty] private LayoutTier _tier = LayoutTier.Narrow;
    [ObservableProperty] private double _windowWidth;

    /// <summary>창 폭(DIP)이 바뀌었다. 단계가 달라질 때만 <see cref="Tier"/>가 바뀐다.</summary>
    public void SetWindowWidth(double widthDip)
    {
        WindowWidth = widthDip;
        Tier = LayoutTiers.For(widthDip);
    }

    // ── 제목줄 ────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionLabel), nameof(ConnectionTooltip))]
    private GameConnection _connection;

    [ObservableProperty] private string _countdown;

    public string ConnectionLabel => ConnectionInfo.Label(Connection);
    public string ConnectionTooltip => ConnectionInfo.Tooltip(Connection, _cli.CliPath);

    /// <summary>30초마다 부른다: 리셋 카운트다운을 다시 계산하고 연결 점 설명(CLI 경로)을 새로 한다.</summary>
    public void Tick()
    {
        Countdown = ResetCountdown.Format(_clock());
        OnPropertyChanged(nameof(ConnectionTooltip));
    }

    // ── 경고 띠 ───────────────────────────────────────────────

    [ObservableProperty] private string _bannerText = "";
    [ObservableProperty] private bool _bannerVisible;

    [RelayCommand]
    private void DismissBanner()
    {
        var w = _events.Warnings.FirstOrDefault(x => !_dismissed.Contains(x.Kind));
        if (w != null) _dismissed.Add(w.Kind);
        UpdateBanner();
    }

    private void UpdateBanner()
    {
        var w = _events.Warnings.FirstOrDefault(x => !_dismissed.Contains(x.Kind));
        BannerText = w?.Message ?? "";
        BannerVisible = w != null;
    }

    // ── 본문 ──────────────────────────────────────────────────

    /// <summary>첫 읽기가 끝나기 전. 이때는 본문에 "읽는 중" 자리 표시를 보인다.</summary>
    [ObservableProperty] private bool _isFirstReadPending;

    public ObservableCollection<AccountTab> Accounts { get; } = [];
    public ObservableCollection<CharacterRow> Characters { get; } = [];

    [ObservableProperty] private AccountTab? _selectedAccount;
    [ObservableProperty] private CharacterRow? _selectedCharacter;
    [ObservableProperty] private bool _hasAccounts;

    public string PendingText => "게임에서 캐릭터를 읽는 중…";
    public string EmptyText => "기록된 캐릭터가 없습니다. 게임에 접속하면 여기에 나타납니다.";

    private CharactersSnapshot? _last;

    partial void OnSelectedAccountChanged(AccountTab? value) => FillCharacters();

    /// <summary>시작: 첫 읽기를 한 번 하고(끝날 때까지 자리 표시), 그 결과로 화면을 채운다.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        try { await _firstRead.RunAsync(ct); }
        finally
        {
            await RefreshAsync();
            _post(() => IsFirstReadPending = false);
        }
    }

    /// <summary>캐릭터 현황을 다시 읽어 탭·목록에 반영한다. Characters()는 디스크를 읽을 수 있어 UI 스레드 밖에서 부른다.</summary>
    public async Task RefreshAsync()
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) { ScheduleRefresh(); return; }
        try
        {
            var snap = await Task.Run(() => _session.Characters());
            _post(() => Apply(snap));
        }
        catch (Exception) { /* 다음 이벤트에 다시 */ }
        finally { Interlocked.Exchange(ref _refreshing, 0); }
    }

    private void Apply(CharactersSnapshot snap)
    {
        _last = snap;
        var keep = SelectedAccount?.Id;
        Accounts.Clear();
        foreach (var a in snap.Accounts)
            Accounts.Add(new AccountTab(a.Id, a.Name, a.Members.Count, a.HasCurrent));
        HasAccounts = Accounts.Count > 0;
        // 선택 유지 → 없으면 접속 중 계정 → 첫 계정
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == keep) ?? Accounts.FirstOrDefault(a => a.Id == snap.CurrentAccountId) ?? Accounts.FirstOrDefault();
        FillCharacters();
    }

    private void FillCharacters()
    {
        var keep = SelectedCharacter?.Key;
        Characters.Clear();
        var account = _last?.Accounts.FirstOrDefault(a => a.Id == SelectedAccount?.Id);
        if (account != null)
            foreach (var c in account.Members) Characters.Add(CharacterRow.From(c));
        SelectedCharacter = Characters.FirstOrDefault(c => c.Key == keep) ?? Characters.FirstOrDefault(c => c.IsCurrent) ?? Characters.FirstOrDefault();
    }

    private void OnChanged() => ScheduleRefresh();
    private void OnHomework(HomeworkChange _) => ScheduleRefresh();

    /// <summary>알림이 몰려 와도 한 번만 다시 읽는다(200ms 묶음).</summary>
    private void ScheduleRefresh()
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        if (Interlocked.Exchange(ref _refreshScheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(_refreshDelay); }
            finally { Interlocked.Exchange(ref _refreshScheduled, 0); }
            await RefreshAsync();
        });
    }

    private void OnStatus(StatusChange c) => _post(() => Connection = c.State);
    private void OnWarning(AppWarning _) => _post(UpdateBanner);

    private void OnWarningCleared(AppWarningKind kind) => _post(() =>
    {
        _dismissed.Remove(kind);   // 같은 경고가 다시 생기면 다시 보인다
        UpdateBanner();
    });

    // ── 설정 ──────────────────────────────────────────────────

    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _closeToTray;

    /// <summary>실제로 쓰는 CLI 경로(저장 값이 없으면 자동 탐색 결과).</summary>
    public string EffectiveCliPath => _cli.CliPath;

    /// <summary>항상 위를 바꾸고 저장한다(트레이·설정 어느 쪽에서 눌러도 같다). 저장에 실패해도 이번 실행에서는 적용된다.</summary>
    public void SetAlwaysOnTop(bool value)
    {
        AlwaysOnTop = value;
        _settings.Update(d => d.AlwaysOnTop = value);
    }

    public void SetCloseToTray(bool value)
    {
        CloseToTray = value;
        _settings.Update(d => d.CloseToTray = value);
    }

    /// <summary>CLI 경로를 저장하고 바로 적용한 뒤 연결 상태를 다시 확인한다. 빈 값은 "자동"(저장된 경로를 지우고 웹앱 설정 → 기본 경로 순으로 되돌린다).</summary>
    public async Task SetCliPathAsync(string? path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        _settings.Update(d => d.CliPath = value);
        _cli.CliPath = value ?? AppSettingsStore.ResolveCliPath(new AppOptions(), _settings) ?? GameCli.DefaultCliPath;
        OnPropertyChanged(nameof(EffectiveCliPath));
        try { await _status.CheckNowAsync(); } catch (OperationCanceledException) { }
        _post(() => { Connection = _status.State; OnPropertyChanged(nameof(ConnectionTooltip)); });
    }

    /// <summary>"닫으면 트레이로" 안내 풍선을 이미 보여 줬는가. 처음 한 번만 보이도록 저장한다.</summary>
    public bool TrayHintShown => _settings.Current.TrayHintShown;
    public void MarkTrayHintShown() => _settings.Update(d => d.TrayHintShown = true);

    // ── 창 위치 ───────────────────────────────────────────────

    public AppSettingsData SavedWindow => _settings.Current;

    public void SaveWindow(PxRect normalBounds, bool maximized) => _settings.Update(d =>
    {
        d.WindowLeft = normalBounds.Left;
        d.WindowTop = normalBounds.Top;
        d.WindowWidth = normalBounds.Width;
        d.WindowHeight = normalBounds.Height;
        d.WindowMaximized = maximized;
    });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _events.StatusChanged -= OnStatus;
        _events.CharactersChanged -= OnChanged;
        _events.HomeworkChanged -= OnHomework;
        _events.WarningRaised -= OnWarning;
        _events.WarningCleared -= OnWarningCleared;
    }
}
