using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using MobiMateHomeWork.Hosting;
using MobiMateHomeWork.Services;

namespace MobiMateHomeWork;

/// <summary>
/// H2a 임시 창: 서비스가 도는지 확인하는 용도다(게임 연결 상태, 계정·캐릭터 수, 마지막 이벤트 몇 줄).
/// 화면은 H3에서 다시 만든다. 지금은 이 창을 닫으면 앱이 끝난다(트레이 숨김은 H3).
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxLines = 30;

    private readonly IServiceProvider _sp;
    private readonly AppEvents _events;
    private readonly StatusMonitor _status;
    private readonly CharacterSession _session;
    private readonly ObservableCollection<string> _lines = [];
    private readonly DispatcherTimer _timer;
    private int _refreshing;

    public MainWindow(IServiceProvider sp)
    {
        InitializeComponent();
        _sp = sp;
        _events = sp.GetRequiredService<AppEvents>();
        _status = sp.GetRequiredService<StatusMonitor>();
        _session = sp.GetRequiredService<CharacterSession>();
        EventList.ItemsSource = _lines;

        _events.StatusChanged += OnStatus;
        _events.HeaderChanged += OnHeader;
        _events.CharactersChanged += OnCharacters;
        _events.HomeworkChanged += OnHomework;
        _events.WarningRaised += OnWarning;

        var options = sp.GetRequiredService<AppOptions>();
        StorageText.Text = $"저장 폴더: {options.StorageDir}";
        ShowStatus();
        ShowWarnings();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => { ShowStatus(); _ = RefreshCountsAsync(); };
        _timer.Start();
        _ = RefreshCountsAsync();
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _events.StatusChanged -= OnStatus;
        _events.HeaderChanged -= OnHeader;
        _events.CharactersChanged -= OnCharacters;
        _events.HomeworkChanged -= OnHomework;
        _events.WarningRaised -= OnWarning;
        ((App)Application.Current).Quit();
    }

    private void OnQuit(object sender, RoutedEventArgs e) => Close();

    // 이벤트는 서비스 스레드에서 올라온다: UI 스레드로 넘긴다
    private void OnStatus(StatusChange c) => Post($"연결 상태: {c.State}");
    private void OnHeader(HeaderView h) => Post($"헤더: {h.Realm}·{h.Job} Lv.{h.Level} 전투력 {h.Combat:N0}{(h.Selecting ? " (캐릭터 선택창)" : "")} 맵 {h.Map ?? "-"}");
    private void OnCharacters() => Post("캐릭터 기록 갱신", refreshCounts: true);
    private void OnHomework(HomeworkChange c) => Post($"숙제 변경 [{c.Reason}] {(c.CharacterKey.Length == 0 ? "(전체)" : c.CharacterKey)} {string.Join(",", c.Ids)}", refreshCounts: true);
    private void OnWarning(AppWarning w) => Post($"경고: {w.Message}", showWarnings: true);

    private void Post(string line, bool refreshCounts = false, bool showWarnings = false)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _lines.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
            while (_lines.Count > MaxLines) _lines.RemoveAt(_lines.Count - 1);
            if (showWarnings) ShowWarnings();
            if (refreshCounts) _ = RefreshCountsAsync();
        });
    }

    private void ShowStatus()
    {
        var cli = _sp.GetRequiredService<IGameCli>();
        StatusText.Text = $"게임 연결: {_status.State switch
        {
            GameConnection.Connected => "연결됨",
            GameConnection.Disconnected => "연결 안 됨",
            GameConnection.CliMissing => "CLI 없음",
            _ => "확인 중",
        }}";
        CliText.Text = $"CLI: {cli.CliPath}{(cli.IsAvailable ? "" : " (없음)")}";
    }

    private void ShowWarnings()
    {
        var w = _events.Warnings;
        WarningText.Visibility = w.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = string.Join(Environment.NewLine, w.Select(x => "⚠ " + x.Message));
    }

    /// <summary>계정·캐릭터 수. Characters()는 디스크를 읽고 공유 잠금을 얻을 수 있으므로 UI 스레드 밖에서 부른다.</summary>
    private async Task RefreshCountsAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        try
        {
            var snap = await Task.Run(() => _session.Characters());
            var current = snap.Accounts.SelectMany(a => a.Members).FirstOrDefault(m => m.IsCurrent);
            CountText.Text = $"계정 {snap.Accounts.Count} · 캐릭터 {snap.Accounts.Sum(a => a.Members.Count)}";
            CurrentText.Text = current == null ? "접속 중: -" : $"접속 중: {current.Nickname ?? current.Realm} ({current.Realm}·{current.Job} Lv.{current.Level}) 맵 {current.Map ?? "-"}";
        }
        catch (Exception) { /* 다음 주기에 다시 */ }
        finally { Interlocked.Exchange(ref _refreshing, 0); }
    }
}
