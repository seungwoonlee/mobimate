using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MobiMateHomeWork.Hosting;
using MobiMateHomeWork.Layout;
using MobiMateHomeWork.Services;
using MobiMateHomeWork.ViewModels;

namespace MobiMateHomeWork;

/// <summary>
/// 메인 창: 직접 그린 제목줄(WindowChrome), 계정 탭 줄, 본문(숙제 표 영역 + 캐릭터 카드 영역).
/// 화면 상태는 <see cref="MainViewModel"/>이 갖고, 이 클래스는 창에 묶인 일만 한다 —
/// 위치·크기 복원과 저장, 최대화 테두리 보정, 닫기 = 트레이 숨김, 배치 단계 리소스 교체.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private readonly MainViewModel _vm;
    private readonly bool _preview;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _saveTimer;
    private PxRect? _normal;          // 마지막으로 본 일반(최대화 아닌) 상태의 위치·크기 (물리 픽셀)
    private bool _wantMaximized;
    private bool _tracking;           // 첫 배치가 끝난 뒤에만 위치 변화를 기억한다
    private PxRect? _target;          // 시작 배치의 목표 사각형. 배치가 안정될 때까지 DPI 변경 처리가 크기를 바꾸면 되돌린다
    private bool _settling;
    private readonly ILogger? _log;

    /// <summary>시작 읽기(첫 읽기 + 화면 채우기)가 끝나면 완료되는 작업. 미리보기 렌더러가 기다린다.</summary>
    public Task Started { get; private set; } = Task.CompletedTask;

    public MainViewModel ViewModel => _vm;

    /// <param name="preview">스냅숏 렌더용: 위치·크기를 복원·저장하지 않고, 닫아도 앱을 끝내지 않는다.</param>
    public MainWindow(IServiceProvider sp, bool preview = false)
    {
        InitializeComponent();
        _preview = preview;
        _log = sp.GetService<ILoggerFactory>()?.CreateLogger("MobiMateHomeWork.Window");

        var status = sp.GetRequiredService<StatusMonitor>();
        var firstRead = new FirstRead(status, sp.GetRequiredService<CharacterSession>(), sp.GetService<ILoggerFactory>()?.CreateLogger<FirstRead>());
        _vm = new MainViewModel(sp.GetRequiredService<AppEvents>(), status, sp.GetRequiredService<GameCli>(), sp.GetRequiredService<CharacterSession>(),
            sp.GetRequiredService<AppSettingsStore>(), firstRead, actions: sp.GetRequiredService<HomeworkCommands>(), post: a => Dispatcher.BeginInvoke(a));
        DataContext = _vm;
        _vm.Board.AccountPicked += () => Table.ScrollToTop();   // 다른 계정 탭을 누르면 표를 맨 위로
        PreviewKeyDown += OnWindowKeyDown;

        _vm.PropertyChanged += OnViewModelChanged;
        SizeChanged += OnSizeChanged;
        StateChanged += OnStateChanged;
        LocationChanged += (_, _) => ScheduleSave();
        Loaded += OnLoaded;
        Closed += OnClosed;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _tick.Tick += (_, _) => _vm.Tick();
        _tick.Start();

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SavePlacement(); };
    }

    // ── 시작·배치 ─────────────────────────────────────────────

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_preview) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var saved = _vm.SavedWindow;
        PxRect? restored = null;
        if (saved is { WindowLeft: { } l, WindowTop: { } t, WindowWidth: { } w, WindowHeight: { } h })
            restored = WindowPlacement.Restore(new SavedPlacement((int)Math.Round(l), (int)Math.Round(t), (int)Math.Round(w), (int)Math.Round(h), saved.WindowMaximized), NativeScreens.All());
        var rect = restored ?? WindowPlacement.FirstRun(NativeScreens.Current());
        _wantMaximized = restored != null && saved.WindowMaximized;
        _normal = rect;
        _target = rect;
        _settling = true;
        NativeScreens.Place(hwnd, rect);
        _log?.LogInformation("창 시작 배치: {Kind} {Rect} (모니터 작업 영역 {Area})", restored != null ? "저장된 위치 복원" : "첫 실행 4분할", rect, NativeScreens.Current().WorkArea);
    }

    /// <summary>다른 배율의 모니터에 놓이면 시스템이 DPI 변경 처리로 창 크기를 다시 계산한다. 시작 배치가 안정될 때까지는 목표 사각형으로 되돌린다.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_settling) Dispatcher.BeginInvoke(Reapply, DispatcherPriority.Normal);
    }

    private void Reapply()
    {
        if (!_settling || _target is not { } t) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (NativeScreens.BoundsOf(hwnd) != t) NativeScreens.Place(hwnd, t);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_preview || !_settling) return;
        Reapply();
        // 화면이 한가해진 뒤 한 번 더 확인하고 배치를 확정한다
        Dispatcher.BeginInvoke(() =>
        {
            Reapply();
            _settling = false;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (NativeScreens.BoundsOf(hwnd) is { } b) _normal = b;
            _log?.LogInformation("창 배치 확정: {Rect} 배율 {Scale:0.##} 안쪽 폭 {Width:0.#} DIP → {Tier}", _normal, VisualTreeHelper.GetDpi(this).DpiScaleX, Frame.ActualWidth, _vm.Tier);
            if (_wantMaximized) WindowState = WindowState.Maximized;
            _tracking = true;
        }, DispatcherPriority.ApplicationIdle);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Reapply();
        _vm.SetWindowWidth(Frame.ActualWidth);
        Started = _vm.StartAsync();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 최대화 중 테두리 보정 여백을 뺀 안쪽 폭으로 단계를 정한다
        _vm.SetWindowWidth(Math.Max(0, e.NewSize.Width - Frame.Margin.Left - Frame.Margin.Right));
        ScheduleSave();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Tier)) TierResources.Apply(Application.Current, _vm.Tier, _vm.WindowWidth);
        else if (e.PropertyName == nameof(MainViewModel.WindowWidth)) TierResources.ApplyScale(Application.Current, _vm.Tier, _vm.WindowWidth);
    }

    // ── 필드 보스 팝업 ────────────────────────────────────────

    private void OnBossScrimPressed(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _vm.Board.CloseBossPicker();
        e.Handled = true;
    }

    /// <summary>팝업 상자 안을 눌러도 바깥(어두운 면)을 누른 것으로 치지 않는다.</summary>
    private void OnBossDialogPressed(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;

    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && _vm.Board.BossPicker != null)
        {
            _vm.Board.CloseBossPicker();
            e.Handled = true;
        }
    }

    // ── 최대화·테두리 ─────────────────────────────────────────

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var max = WindowState == WindowState.Maximized;
        MaxButton.Content = max ? "" : "";
        MaxButton.ToolTip = max ? "이전 크기로" : "최대화";
        System.Windows.Automation.AutomationProperties.SetName(MaxButton, max ? "이전 크기로" : "최대화");
        if (max) Dispatcher.BeginInvoke(FitMaximizedFrame, DispatcherPriority.Loaded);
        else Frame.Margin = new Thickness(0);
        ScheduleSave();
    }

    /// <summary>
    /// WindowChrome 창은 최대화하면 크기 조절 테두리만큼 화면 밖으로 넘친다. 실제로 넘친 만큼을 재서 안쪽 여백으로 되돌린다
    /// (배율·작업 표시줄 위치와 상관없이 맞는다).
    /// </summary>
    private void FitMaximizedFrame()
    {
        if (WindowState != WindowState.Maximized) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (NativeScreens.BoundsOf(hwnd) is not { } b) return;
        var center = new PxRect(b.Left + b.Width / 2, b.Top + b.Height / 2, 1, 1);
        var monitor = NativeScreens.All().FirstOrDefault(m => PxRect.Intersect(center, m.WorkArea).Area > 0);
        if (monitor == null) return;
        var a = monitor.WorkArea;
        var s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Frame.Margin = new Thickness(Math.Max(0, a.Left - b.Left) / s, Math.Max(0, a.Top - b.Top) / s, Math.Max(0, b.Right - a.Right) / s, Math.Max(0, b.Bottom - a.Bottom) / s);
        _log?.LogInformation("최대화 테두리 보정: 창 {B} 작업 영역 {A} → 여백 {M}", b, a, Frame.Margin);
    }

    // ── 위치 기억 ─────────────────────────────────────────────

    private void ScheduleSave()
    {
        if (!_tracking) return;
        _saveTimer.Stop();   // 끌거나 크기를 바꾸는 동안에는 저장하지 않고, 멈춘 뒤 한 번만 저장한다
        _saveTimer.Start();
    }

    /// <summary>지금 상태를 바로 저장한다(숨기기·닫기 직전).</summary>
    public void SavePlacement()
    {
        if (!_tracking) return;
        // 위치는 저장하는 순간에 잰다: 최대화 직후에는 이벤트 시점에 WindowState가 아직 이전 값일 수 있어, 이벤트마다 재면 최대화 크기를 일반 크기로 오인한다
        if (WindowState == WindowState.Normal && IsVisible && NativeScreens.BoundsOf(new WindowInteropHelper(this).Handle) is { } b) _normal = b;
        if (_normal is not { } n) return;
        _vm.SaveWindow(n, WindowState == WindowState.Maximized);
    }

    // ── 닫기 = 트레이 숨김 ────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        var app = (App)Application.Current;
        if (!_preview && !app.IsQuitting && _vm.CloseToTray)
        {
            e.Cancel = true;
            _saveTimer.Stop();
            SavePlacement();
            Hide();
            app.NotifyHiddenToTray();
            return;
        }
        base.OnClosing(e);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _tick.Stop();
        _saveTimer.Stop();
        if (!_preview) SavePlacement();
        _vm.Dispose();
        if (!_preview) ((App)Application.Current).Quit();
    }

    // ── 제목줄 버튼 ───────────────────────────────────────────

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_vm) { Owner = this };
        dialog.ShowDialog();
    }
}
