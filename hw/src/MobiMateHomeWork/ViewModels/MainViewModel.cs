using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MobiMateHomeWork.Hosting;
using MobiMateHomeWork.Layout;
using MobiMateHomeWork.Services;

namespace MobiMateHomeWork.ViewModels;

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
    private readonly Func<CharactersSnapshot> _source;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Action> _post;
    private readonly TimeSpan _refreshDelay;
    private readonly HashSet<AppWarningKind> _dismissed = [];
    private int _refreshScheduled;
    private int _refreshing;
    private int _disposed;

    /// <param name="actions">숙제 칸 누르기·이름 바꾸기 같은 쓰기 동작. 없으면 쓰기를 못 하는 화면(읽기 전용)이다.</param>
    /// <param name="source">캐릭터 현황을 읽는 함수(기본: 서비스의 Characters()). 테스트가 가짜 현황을 넣을 때 바꾼다.</param>
    public MainViewModel(AppEvents events, StatusMonitor status, GameCli cli, CharacterSession session, AppSettingsStore settings, FirstRead firstRead,
        IHomeworkActions? actions = null, Func<CharactersSnapshot>? source = null,
        Action<Action>? post = null, Func<DateTimeOffset>? clock = null, TimeSpan? refreshDelay = null, TimeSpan? noticeDuration = null)
    {
        _events = events;
        _status = status;
        _cli = cli;
        _session = session;
        _settings = settings;
        _firstRead = firstRead;
        _source = source ?? session.Characters;
        _post = post ?? (a => a());
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _refreshDelay = refreshDelay ?? TimeSpan.FromMilliseconds(200);
        Board = new HomeworkBoardViewModel(actions ?? new ReadOnlyActions(), RefreshAsync, _post, _clock, noticeDuration);

        var s = settings.Current;
        _alwaysOnTop = s.AlwaysOnTop;
        _closeToTray = s.CloseToTray;
        _backgroundMode = s.BackgroundMode;
        _backgroundId = s.BackgroundId;
        _backgroundImagePath = s.BackgroundImagePath;
        _backgroundOpacity = s.BackgroundOpacity;
        _connection = status.State;
        _countdown = ResetCountdown.Format(_clock());
        _isFirstReadPending = !firstRead.Done;
        UpdateBanner();

        events.StatusChanged += OnStatus;
        events.CharactersChanged += OnChanged;
        events.HeaderChanged += OnHeader;
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

    /// <summary>
    /// 30초마다 부른다: 리셋 카운트다운·마지막 접속·예상 보유량을 다시 계산하고 연결 점 설명(CLI 경로)을 새로 한다.
    /// 일일(06:00 KST)·주간(월요일 06:00) 리셋을 지났으면 기록을 다시 읽어 보드를 새로 만든다.
    /// </summary>
    public void Tick()
    {
        Countdown = ResetCountdown.Format(_clock());
        OnPropertyChanged(nameof(ConnectionTooltip));
        var crossed = Board.ResetCrossed();
        Board.Rebuild();
        if (crossed) ScheduleRefresh();
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

    /// <summary>숙제 표·계정 탭·캐릭터 카드.</summary>
    public HomeworkBoardViewModel Board { get; }

    public string PendingText => "게임에서 캐릭터를 읽는 중…";
    public string EmptyText => "기록된 캐릭터가 없습니다. 게임에 접속하면 여기에 나타납니다.";

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

    /// <summary>캐릭터 현황을 다시 읽어 표·탭·카드에 반영한다. Characters()는 디스크를 읽을 수 있어 UI 스레드 밖에서 부른다.</summary>
    public async Task RefreshAsync()
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) { ScheduleRefresh(); return; }
        try
        {
            var readAt = _clock();   // 읽기 직전 시각: 리셋 경계 판정의 기준
            var snap = await Task.Run(_source);
            _post(() => Board.Apply(snap, readAt));
        }
        catch (Exception) { /* 다음 이벤트에 다시 */ }
        finally { Interlocked.Exchange(ref _refreshing, 0); }
    }

    private void OnChanged() => ScheduleRefresh();
    private void OnHeader(HeaderView _) => ScheduleRefresh();
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

    private void OnStatus(StatusChange c)
    {
        _post(() => Connection = c.State);
        ScheduleRefresh();
    }
    private void OnWarning(AppWarning _) => _post(UpdateBanner);

    private void OnWarningCleared(AppWarningKind kind) => _post(() =>
    {
        _dismissed.Remove(kind);   // 같은 경고가 다시 생기면 다시 보인다
        UpdateBanner();
    });

    // ── 설정 ──────────────────────────────────────────────────

    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _closeToTray;

    // ── 배경 그림 (공식 일러스트 / 내 그림 / 끄기, 투명도 5~40%) ──
    [ObservableProperty] private string _backgroundMode = BackgroundSettings.Official;
    [ObservableProperty] private string _backgroundId = Images.BackgroundCatalog.DefaultId;
    [ObservableProperty] private string? _backgroundImagePath;
    [ObservableProperty] private double _backgroundOpacity = BackgroundSettings.DefaultOpacity;

    /// <summary>배경 그림을 고른다(저장 포함). 투명도는 그대로.</summary>
    public void SetBackground(string mode, string? id = null, string? customPath = null)
    {
        BackgroundMode = mode;
        if (id != null) BackgroundId = id;
        if (customPath != null) BackgroundImagePath = customPath;
        _settings.Update(d =>
        {
            d.BackgroundMode = mode;
            if (id != null) d.BackgroundId = id;
            if (customPath != null) d.BackgroundImagePath = customPath;
        });
    }

    /// <summary>투명도(0.05~0.5로 보정). 슬라이더를 끄는 동안은 save=false로 화면에만 적용하고, 놓을 때 저장한다.</summary>
    public void SetBackgroundOpacity(double value, bool save = true)
    {
        var v = BackgroundSettings.ClampOpacity(value);
        BackgroundOpacity = v;
        if (save) _settings.Update(d => d.BackgroundOpacity = v);
    }

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
        _events.HeaderChanged -= OnHeader;
        _events.HomeworkChanged -= OnHomework;
        _events.WarningRaised -= OnWarning;
        _events.WarningCleared -= OnWarningCleared;
    }
}

/// <summary>쓰기 동작을 받지 못한 화면(테스트 등)의 자리 채움: 모두 거절한다.</summary>
internal sealed class ReadOnlyActions : IHomeworkActions
{
    public HomeworkCommandResult SetHomework(string id, bool? completed, int? count, string? characterKey = null) =>
        HomeworkCommandResult.Fail(CommandError.Validation, "이 화면에서는 숙제를 바꿀 수 없습니다.");

    public CommandResult RenameCharacter(string characterKey, string? nickname) =>
        new(CommandError.Validation, "이 화면에서는 이름을 바꿀 수 없습니다.");
}
