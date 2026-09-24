using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MobiMate;

public partial class MainWindow : Window
{
    private readonly GameCliService _cli = new();
    private readonly SnapshotManager _snapshotManager = new();
    private readonly AiEngineManager _aiManager = new();
    private InGameChatterService? _chatterService;

    private CharacterInfo? _lastCharInfo;
    private EnvironmentInfo? _lastEnvInfo;
    private ActivityInfo? _lastActivity;
    private List<CurrencyItem>? _lastCurrencies;
    private List<MissionItem>? _lastDailyMissions;
    private List<MissionItem>? _lastWeeklyMissions;
    private AlteringWorksResponse? _lastAlteringWorks;
    private List<QuestItem>? _lastQuests;

    private readonly HomeworkTrackerService _homeworkService = new();
    private HomeworkCategory _currentHomeworkCategory = HomeworkCategory.All;

    private readonly GoogleSheetSettingsManager _googleSheetSettingsManager = new();
    private readonly GoogleSheetSyncService _googleSheetSyncService = new();
    private readonly ExcelSettingsManager _excelSettingsManager = new();
    private readonly ExcelSheetSyncService _excelSyncService = new();
    private string _lastSyncedCharacterKey = "";

    private List<ItemData> _allItems = new();
    private string _currentItemLocationFilter = "All";
    private readonly Dictionary<string, int> _initialBagItemCounts = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasBagBaseline = false;

    private List<GatherableItem> _allGatherables = new();
    private List<AlteringWorkItem> _allAlteringWorks = new();
    private string _currentGatherCategory = "All";
    private int _targetGatherCount = 100;

    public ObservableCollection<ChatLogEntry> GameChatLogs { get; } = new();
    public ObservableCollection<AiMessageEntry> AiMessages { get; } = new();

    private CancellationTokenSource? _tabCts;
    private DispatcherTimer? _searchDebounceTimer;
    private DispatcherTimer? _toastTimer;
    private DispatcherTimer? _autoRefreshTimer;
    private readonly AdaptiveRefreshController _adaptiveRefresh = new();
    private bool _isAutoRefreshing = false;
    private bool _isWindowLoaded;

    public ICommand EmergencyStopCommand { get; }

    public MainWindow()
    {
        App.LogTrace("MainWindow.ctor enter");
        EmergencyStopCommand = new RelayCommand(async _ => await EmergencyStopInternalAsync());

        App.LogTrace("MainWindow.ctor calling InitializeComponent");
        InitializeComponent();
        App.LogTrace("MainWindow.ctor InitializeComponent finished");

        var asmVersion = typeof(MainWindow).Assembly.GetName().Version;
        if (TxtBuildVersion != null)
        {
            TxtBuildVersion.Text = asmVersion != null ? $"v{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}" : "v1.1.1";
        }

        DataContext = this;
        ListGameChatLogs.ItemsSource = GameChatLogs;
        ListAiMessages.ItemsSource = AiMessages;

        AiMessages.Add(new AiMessageEntry("AI 도우미", "✨ 모비노기 AI도우미가 준비되었습니다. 아래 완성형 가이드 카드를 선택하거나 자유롭게 질문해 보세요! (ESC: 긴급 정지)", false));

        // 아무말 대잔치(페르소나 혼잣말) 백그라운드 서비스 초기화
        _chatterService = new InGameChatterService(_cli, _aiManager, GetCurrentChatterContext);
        _chatterService.OnChatterEmitted += (personaTag, msg) =>
        {
            Dispatcher.Invoke(() =>
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                GameChatLogs.Add(new ChatLogEntry($"[{time}] {personaTag}", msg, true));
                ScrollGameChatLog.ScrollToBottom();
            });
        };
        _chatterService.OnToastRequested += (msg, success) => ShowToast(msg, success);
        _excelSyncService.SetTargetFilePath(_excelSettingsManager.CurrentSettings.FilePath);

        // 유저 인터랙션 감지 (키보드 입력, 마우스 클릭 시 자동 새로고침 주기 15초로 즉시 리셋)
        PreviewKeyDown += (s, e) =>
        {
            ResetRefreshIntervalOnUserActivity();
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (OverlayExcelManage.Visibility == Visibility.Visible)
                {
                    OverlayExcelManage.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
            }
        };
        PreviewMouseDown += (s, e) => ResetRefreshIntervalOnUserActivity();

        Loaded += async (s, e) =>
        {
            App.LogTrace("MainWindow.Loaded enter");
            _isWindowLoaded = true;
            RefreshHomeworkUi();
            RefreshDungeonCutoffUi();
            LoadCustomPersonasToUi();
            App.LogTrace("MainWindow.Loaded calling LoadAiEnginesAsync");
            await LoadAiEnginesAsync();
            App.LogTrace("MainWindow.Loaded calling RefreshHeaderOnlyAsync");
            await RefreshHeaderOnlyAsync();
            App.LogTrace("MainWindow.Loaded calling RefreshCurrentTabAsync");
            await RefreshCurrentTabAsync();

            InitAutoRefreshTimer();
            App.LogTrace("MainWindow.Loaded exit");
        };

        Closed += (s, e) =>
        {
            _autoRefreshTimer?.Stop();
        };
        App.LogTrace("MainWindow.ctor exit");
    }

    // ================= 0. 인앱 비동기 토스트 알림 (MessageBox 완전 대체) =================
    public void ShowToast(string message, bool isSuccess = true)
    {
        Dispatcher.Invoke(() =>
        {
            ToastBar.Visibility = Visibility.Visible;
            ToastText.Text = message;

            if (isSuccess)
            {
                ToastBar.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x38, 0x29));
                ToastIcon.Text = "✓";
                ToastIcon.Foreground = (Brush)FindResource("AccentGreen");
            }
            else
            {
                ToastBar.Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x1A, 0x1A));
                ToastIcon.Text = "⚠️";
                ToastIcon.Foreground = (Brush)FindResource("AccentRed");
            }

            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                ToastBar.Visibility = Visibility.Collapsed;
            };
            _toastTimer.Start();
        });
    }

    private void BtnCloseToast_Click(object sender, RoutedEventArgs e)
    {
        _toastTimer?.Stop();
        ToastBar.Visibility = Visibility.Collapsed;
    }

    // ================= 1. 상단 글로벌 제어 =================
    private void BtnTopmost_Click(object sender, RoutedEventArgs e)
    {
        Topmost = BtnTopmost.IsChecked == true;
    }

    private void InitAutoRefreshTimer()
    {
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AdaptiveRefreshController.BaseIntervalSec)
        };
        _autoRefreshTimer.Tick += async (s, e) => await AutoRefreshTimer_Tick();
        _autoRefreshTimer.Start();
    }

    private void ResetRefreshIntervalOnUserActivity()
    {
        _adaptiveRefresh.RecordUserActivity();
        if (_autoRefreshTimer != null && _autoRefreshTimer.Interval.TotalSeconds > AdaptiveRefreshController.BaseIntervalSec)
        {
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(AdaptiveRefreshController.BaseIntervalSec);
        }
    }

    private async Task AutoRefreshTimer_Tick()
    {
        if (!_isWindowLoaded || _isAutoRefreshing) return;

        _isAutoRefreshing = true;
        try
        {
            await RefreshHeaderOnlyAsync();
            await RefreshCurrentTabAsync();
        }
        catch
        {
            // 백그라운드 자동 갱신 예외 방어
        }
        finally
        {
            _isAutoRefreshing = false;
        }

        var nextInterval = _adaptiveRefresh.OnTick();
        if (_autoRefreshTimer != null)
        {
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(nextInterval);
        }
    }

    private async void BtnRefreshCurrentTab_Click(object sender, RoutedEventArgs e)
    {
        ResetRefreshIntervalOnUserActivity();
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer?.Start();
        await RefreshCurrentTabAsync();
    }

    private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl)
        {
            ResetRefreshIntervalOnUserActivity();
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer?.Start();
            await RefreshCurrentTabAsync();
        }
    }

    private async Task RefreshHeaderOnlyAsync()
    {
        var (statusOk, _, _) = await _cli.RunRawAsync("status", timeoutSeconds: 3);
        if (!statusOk)
        {
            ApplyDisconnected();
            return;
        }

        TxtStatusIcon.Text = "🟢";

        var (chOk, ch, _) = await _cli.RunJsonAsync<CharacterInfo>("get_my_info", timeoutSeconds: 3);
        var (actOk, act, _) = await _cli.RunJsonAsync<ActivityInfo>("get_activity", timeoutSeconds: 3);
        var (envOk, env, _) = await _cli.RunJsonAsync<EnvironmentInfo>("get_current_environment", timeoutSeconds: 3);

        UpdateHeaderAndStats(ch, act, env);
    }

    // 이전 탭 요청을 CancellationToken으로 즉시 취소하고 최신 탭만 안전하게 로딩
    public async Task RefreshCurrentTabAsync()
    {
        _tabCts?.Cancel();
        _tabCts = new CancellationTokenSource();
        var ct = _tabCts.Token;

        BtnRefreshCurrentTab.IsEnabled = false;
        BtnRefreshCurrentTab.Content = "⏳ 로딩 중...";

        try
        {
            var idx = MainTabControl.SelectedIndex;
            switch (idx)
            {
                case 0: // 캐릭터 & 스탯
                    var tChar = _cli.RunJsonAsync<CharacterInfo>("get_my_info", timeoutSeconds: 4, ct: ct);
                    var tAct = _cli.RunJsonAsync<ActivityInfo>("get_activity", timeoutSeconds: 4, ct: ct);
                    var tEnv = _cli.RunJsonAsync<EnvironmentInfo>("get_current_environment", timeoutSeconds: 4, ct: ct);
                    await Task.WhenAll(tChar, tAct, tEnv);
                    UpdateHeaderAndStats((await tChar).data, (await tAct).data, (await tEnv).data);
                    break;

                case 1: // 가방 & 소지품
                    var tInv = _cli.RunJsonAsync<CharacterInfo>("get_my_info", timeoutSeconds: 4, ct: ct);
                    var tItems = _cli.RunJsonAsync<List<ItemData>>("get_items", timeoutSeconds: 5, ct: ct);
                    await Task.WhenAll(tInv, tItems);
                    var ch = (await tInv).data;
                    if (ch?.Vitals != null) UpdateWeightUi(ch.Vitals);
                    UpdateItems((await tItems).data);
                    break;

                case 2: // 재화 & 화폐
                    var tCurr = await _cli.RunJsonAsync<List<CurrencyItem>>("get_currencies", timeoutSeconds: 4, ct: ct);
                    UpdateCurrencies(tCurr.data);
                    break;

                case 3: // 미션 & 퀘스트 & 숙제
                    var tDaily = _cli.RunJsonAsync<List<MissionItem>>("get_daily_missions", timeoutSeconds: 4, ct: ct);
                    var tWeekly = _cli.RunJsonAsync<List<MissionItem>>("get_weekly_missions", timeoutSeconds: 4, ct: ct);
                    var tQuests = _cli.RunJsonAsync<List<QuestItem>>("get_quests", timeoutSeconds: 4, ct: ct);
                    var tTab3Curr = _cli.RunJsonAsync<List<CurrencyItem>>("get_currencies", timeoutSeconds: 4, ct: ct);
                    await Task.WhenAll(tDaily, tWeekly, tQuests, tTab3Curr);
                    if ((await tTab3Curr).data is { } curr3) _lastCurrencies = curr3;
                    UpdateMissions((await tDaily).data, (await tWeekly).data, (await tQuests).data);
                    break;

                case 4: // 생활 & 생산
                    var tAlter = _cli.RunJsonAsync<AlteringWorksResponse>("get_altering_works", timeoutSeconds: 4, ct: ct);
                    var tGather = _cli.RunJsonAsync<GatherableResponse>("get_gatherable_items", timeoutSeconds: 4, ct: ct);
                    var tLifeItems = _cli.RunJsonAsync<List<ItemData>>("get_items", timeoutSeconds: 5, ct: ct);
                    await Task.WhenAll(tAlter, tGather, tLifeItems);
                    if ((await tLifeItems).data is { } lifeItems) UpdateItems(lifeItems);
                    UpdateLifeAndCraft((await tAlter).data, (await tGather).data);
                    break;

                case 5: // 주변 레이더
                    var tPcs = await _cli.RunJsonAsync<List<NearPcItem>>("get_near_pcs", timeoutSeconds: 4, ct: ct);
                    UpdateNearPcs(tPcs.data);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // 새 탭 전환에 따른 정상 취소
        }
        catch (Exception ex)
        {
            ShowToast($"데이터 로딩 오류: {ex.Message}", false);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                BtnRefreshCurrentTab.IsEnabled = true;
                BtnRefreshCurrentTab.Content = "🔄 탭 새로고침";
            }
        }
    }

    private void ApplyDisconnected()
    {
        TxtStatusIcon.Text = "🔴";
        TxtCharTitle.Text = "게임 미연결";
        BubbleNickGuide.Visibility = Visibility.Collapsed;
        TxtCombatScore.Text = "⚔️ 전투력 -";
        TxtActivity.Text = "연결 끊김";
        TxtLocation.Text = "게임을 실행하고 MM AI 에이전트 설정을 켜주세요.";
    }

    private void TxtCharTitle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_lastCharInfo == null) return;
        var realm = string.IsNullOrEmpty(_lastCharInfo.RealmName) ? "에린" : _lastCharInfo.RealmName;
        var job = string.IsNullOrEmpty(_lastCharInfo.JobName) ? "밀레시안" : _lastCharInfo.JobName;
        var profile = _snapshotManager.GetProfile(realm, job);

        TxtCustomNickInput.Text = profile?.CustomName ?? "";
        PopupNickName.IsOpen = true;
        TxtCustomNickInput.Focus();
        TxtCustomNickInput.SelectAll();
    }

    private void TxtCustomNickInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnSaveNick_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            PopupNickName.IsOpen = false;
        }
    }

    private void BtnCancelNick_Click(object sender, RoutedEventArgs e)
    {
        PopupNickName.IsOpen = false;
    }

    private void BtnSaveNick_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCharInfo == null)
        {
            PopupNickName.IsOpen = false;
            return;
        }

        var realm = string.IsNullOrEmpty(_lastCharInfo.RealmName) ? "에린" : _lastCharInfo.RealmName;
        var job = string.IsNullOrEmpty(_lastCharInfo.JobName) ? "밀레시안" : _lastCharInfo.JobName;
        var nick = TxtCustomNickInput.Text.Trim();

        _snapshotManager.SetCustomName(realm, job, nick);
        PopupNickName.IsOpen = false;

        if (!string.IsNullOrWhiteSpace(nick))
        {
            TxtCharTitle.Text = $"[{realm}] {nick} ({job} Lv.{_lastCharInfo.Level})";
            BubbleNickGuide.Visibility = Visibility.Collapsed;
            ShowToast($"🏷️ 캐릭터 별칭이 '{nick}'(으)로 저장되었습니다.", true);
        }
        else
        {
            TxtCharTitle.Text = $"[{realm}] {job} Lv.{_lastCharInfo.Level}";
            BubbleNickGuide.Visibility = Visibility.Visible;
            ShowToast("🏷️ 캐릭터 별칭이 기본값으로 초기화되었습니다.", true);
        }
    }

    public static string GetJobIcon(string? jobName)
    {
        if (string.IsNullOrWhiteSpace(jobName)) return "⭐";
        var j = jobName.ToLowerInvariant();
        if (j.Contains("전사") || j.Contains("대검") || j.Contains("검방") || j.Contains("기사") || j.Contains("검사") || j.Contains("워리어") || j.Contains("나이트"))
            return "⚔️";
        if (j.Contains("궁수") || j.Contains("장궁") || j.Contains("석궁") || j.Contains("아처") || j.Contains("헌터") || j.Contains("스나이퍼"))
            return "🏹";
        if (j.Contains("마법") || j.Contains("원소") || j.Contains("메이지") || j.Contains("위자드") || j.Contains("소서러") || j.Contains("술사"))
            return "🔮";
        if (j.Contains("힐러") || j.Contains("사제") || j.Contains("프리스트") || j.Contains("클레릭") || j.Contains("치유"))
            return "✝️";
        if (j.Contains("도적") || j.Contains("암살") || j.Contains("로그") || j.Contains("어쌔신") || j.Contains("시프") || j.Contains("격투"))
            return "🗡️";
        if (j.Contains("음유") || j.Contains("바드") || j.Contains("악사") || j.Contains("음악"))
            return "🎵";
        return "⭐";
    }

    // ================= 1. 캐릭터 & 스탯 탭 =================
    private void UpdateHeaderAndStats(CharacterInfo? ch, ActivityInfo? act, EnvironmentInfo? env)
    {
        if (ch != null)
        {
            _lastCharInfo = ch;
            UpdateDeltas();

            var realm = string.IsNullOrEmpty(ch.RealmName) ? "에린" : ch.RealmName;
            var job = string.IsNullOrEmpty(ch.JobName) ? "밀레시안" : ch.JobName;
            TxtJobIcon.Text = GetJobIcon(job);
            var profile = _snapshotManager.GetProfile(realm, job);
            if (profile != null && !string.IsNullOrWhiteSpace(profile.CustomName))
            {
                TxtCharTitle.Text = $"[{realm}] {profile.CustomName} ({job} Lv.{ch.Level})";
                BubbleNickGuide.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtCharTitle.Text = $"[{realm}] {job} Lv.{ch.Level}";
                BubbleNickGuide.Visibility = Visibility.Visible;
            }

            if (ch.CombatScore != null)
            {
                TxtCombatScore.Text = $"⚔️ 전투력 {ch.CombatScore.Value:N0}";
                TxtScoreCombat.Text = $"{ch.CombatScore.Value:N0}";
            }
            if (ch.ArcaneResistance != null)
            {
                TxtMdefScore.Text = $"🔮 마도저항 {ch.ArcaneResistance.Value:N0}";
                TxtScoreMdef.Text = $"{ch.ArcaneResistance.Value:N0}";
            }
            if (ch.LivingScore != null) TxtScoreLiving.Text = $"{ch.LivingScore.Value:N0}";
            if (ch.AttractivenessScore != null) TxtScoreAttract.Text = $"{ch.AttractivenessScore.Value:N0}";

            // 어비스 던전 & 주간 레이드 입장 컷 & 마도 압력 대시보드 실시간 갱신
            RefreshDungeonCutoffUi(ch);

            if (ch.Vitals != null) UpdateWeightUi(ch.Vitals);

            // 엑셀 스프레드시트 캐릭터 변경 시 자동 동기화 트리거
            CheckAndTriggerAutoGoogleSheetSync(realm, job);
        }

        if (act != null)
        {
            _lastActivity = act;
            if (act.IsInCombat)
            {
                TxtActivity.Text = "⚔️ 전투 중";
                BadgeActivity.Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x1A, 0x1A));
                TxtActivity.Foreground = (Brush)FindResource("AccentRed");
            }
            else if (act.IsAutoPlaying)
            {
                TxtActivity.Text = "🤖 자동사냥 중";
                BadgeActivity.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2E, 0x4A));
                TxtActivity.Foreground = (Brush)FindResource("AccentBlue");
            }
            else if (act.IsGathering)
            {
                TxtActivity.Text = "🌿 채집 중";
                BadgeActivity.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x38, 0x29));
                TxtActivity.Foreground = (Brush)FindResource("AccentGreen");
            }
            else
            {
                TxtActivity.Text = "대기 중 (Idle)";
                BadgeActivity.Background = new SolidColorBrush(Color.FromRgb(0x23, 0x24, 0x28));
                TxtActivity.Foreground = (Brush)FindResource("TextSecondary");
            }
        }

        if (env != null)
        {
            _lastEnvInfo = env;
            var loc = string.IsNullOrEmpty(env.ChannelName) ? "필드" : env.ChannelName;
            var weather = string.IsNullOrEmpty(env.Weather) ? "맑음" : env.Weather;
            var erinnFormatted = InGameChatterService.FormatErinnTime(env.ErinnNow);
            var erinn = string.IsNullOrEmpty(erinnFormatted) ? "" : $"  |  {erinnFormatted}";
            TxtLocation.Text = $"📍 {loc} ({weather} ☀️){erinn}";
        }
    }

    private void UpdateWeightUi(VitalsInfo vitals)
    {
        var curW = vitals.WeightCurrent;
        var maxW = vitals.WeightMax > 0 ? vitals.WeightMax : 1000.0;
        var pct = (curW / maxW) * 100.0;

        ProgWeightTab.Value = Math.Min(100, pct);
        TxtWeightSummary.Text = $"{curW:F1} / {maxW:F1} ({pct:F1}%)";

        if (pct >= 100.0)
        {
            ProgWeightTab.Foreground = (Brush)FindResource("AccentRed");
            TxtWeightSummary.Foreground = (Brush)FindResource("AccentRed");
            TxtWeightStatus.Text = "🚨 가방 무게 100% 초과 (과적 페널티 상태)! 비잠금 잡템 정리가 시급합니다.";
            TxtWeightStatus.Foreground = (Brush)FindResource("AccentRed");
        }
        else if (pct >= 95.0)
        {
            ProgWeightTab.Foreground = (Brush)FindResource("AccentYellow");
            TxtWeightSummary.Foreground = (Brush)FindResource("AccentYellow");
            TxtWeightStatus.Text = "⚠️ 가방 무게 95% 이상 (주의 필요). 무거운 잡템 다이어트를 권장합니다.";
            TxtWeightStatus.Foreground = (Brush)FindResource("AccentYellow");
        }
        else
        {
            ProgWeightTab.Foreground = (Brush)FindResource("AccentGreen");
            TxtWeightSummary.Foreground = (Brush)FindResource("AccentGreen");
            TxtWeightStatus.Text = "✓ 가방 무게 정상 (95% 미만). 활동에 지장이 없습니다.";
            TxtWeightStatus.Foreground = (Brush)FindResource("AccentGreen");
        }
    }

    private void UpdateDeltas()
    {
        if (_lastCharInfo == null) return;

        var delta = _snapshotManager.UpdateSnapshot(_lastCharInfo, _lastCurrencies, _lastDailyMissions);

        // 1. 전투력 변화량
        if (delta.CombatScoreDiff != 0)
        {
            TxtCombatDelta.Text = $" ({SnapshotManager.FormatDiff(delta.CombatScoreDiff)})";
            TxtCombatDelta.Foreground = delta.CombatScoreDiff > 0 ? (Brush)FindResource("AccentGreen") : (Brush)FindResource("AccentRed");
            TxtCombatDelta.Visibility = Visibility.Visible;
        }
        else
        {
            TxtCombatDelta.Visibility = Visibility.Collapsed;
        }

        // 1-1. 마도 저항 변화량
        if (delta.ArcaneResistanceDiff != 0)
        {
            TxtMdefDelta.Text = $" ({SnapshotManager.FormatDiff(delta.ArcaneResistanceDiff)})";
            TxtMdefDelta.Foreground = delta.ArcaneResistanceDiff > 0 ? (Brush)FindResource("AccentGreen") : (Brush)FindResource("AccentRed");
            TxtMdefDelta.Visibility = Visibility.Visible;
        }
        else
        {
            TxtMdefDelta.Visibility = Visibility.Collapsed;
        }

        // 2. 가방 무게 변화량
        if (Math.Abs(delta.WeightDiff) >= 0.05)
        {
            TxtWeightDelta.Text = $" ({SnapshotManager.FormatWeightDiff(delta.WeightDiff)})";
            TxtWeightDelta.Foreground = delta.WeightDiff < 0 ? (Brush)FindResource("AccentGreen") : (Brush)FindResource("AccentYellow");
            TxtWeightDelta.Visibility = Visibility.Visible;
        }
        else
        {
            TxtWeightDelta.Visibility = Visibility.Collapsed;
        }

        // 3. 재화 탭 세션 누적 요약 (증감에 따른 동적 색상 매핑)
        if (delta.GoldDiff > 0)
        {
            TxtGoldDeltaSummary.Text = $"골드 {SnapshotManager.FormatDiff(delta.GoldDiff, " G")}";
            TxtGoldDeltaSummary.Foreground = (Brush)FindResource("AccentGreen");
        }
        else if (delta.GoldDiff < 0)
        {
            TxtGoldDeltaSummary.Text = $"골드 {SnapshotManager.FormatDiff(delta.GoldDiff, " G")}";
            TxtGoldDeltaSummary.Foreground = (Brush)FindResource("AccentRed");
        }
        else
        {
            TxtGoldDeltaSummary.Text = "골드 +0 G";
            TxtGoldDeltaSummary.Foreground = (Brush)FindResource("AccentGold");
        }

        if (delta.WingsDiff > 0)
        {
            TxtWingsDeltaSummary.Text = $"날개 {SnapshotManager.FormatDiff(delta.WingsDiff, "개")}";
            TxtWingsDeltaSummary.Foreground = (Brush)FindResource("AccentGreen");
        }
        else if (delta.WingsDiff < 0)
        {
            TxtWingsDeltaSummary.Text = $"날개 {SnapshotManager.FormatDiff(delta.WingsDiff, "개")}";
            TxtWingsDeltaSummary.Foreground = (Brush)FindResource("AccentRed");
        }
        else
        {
            TxtWingsDeltaSummary.Text = "날개 +0개";
            TxtWingsDeltaSummary.Foreground = (Brush)FindResource("AccentCyan");
        }

        if (delta.NyangDiff > 0)
        {
            TxtNyangDeltaSummary.Text = $"냥토큰 {SnapshotManager.FormatDiff(delta.NyangDiff, "개")}";
            TxtNyangDeltaSummary.Foreground = (Brush)FindResource("AccentGreen");
        }
        else if (delta.NyangDiff < 0)
        {
            TxtNyangDeltaSummary.Text = $"냥토큰 {SnapshotManager.FormatDiff(delta.NyangDiff, "개")}";
            TxtNyangDeltaSummary.Foreground = (Brush)FindResource("AccentRed");
        }
        else
        {
            TxtNyangDeltaSummary.Text = "냥토큰 +0개";
            TxtNyangDeltaSummary.Foreground = (Brush)FindResource("TextSecondary");
        }

        // 4. 일일 미션 변화량
        if (delta.MissionDiff > 0)
        {
            TxtMissionDelta.Text = $" (+{delta.MissionDiff} 완료 ▲)";
            TxtMissionDelta.Visibility = Visibility.Visible;
        }
        else
        {
            TxtMissionDelta.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshDungeonCutoffUi(CharacterInfo? ch = null)
    {
        if (!_isWindowLoaded || ListContentCards == null) return;

        var charData = ch ?? _lastCharInfo;
        long combat = charData?.CombatScore?.Value ?? 0;
        long mdef = charData?.ArcaneResistance?.Value ?? 0;

        var contents = DungeonCutoffService.EvaluateAllContents(combat, mdef);
        ListContentCards.ItemsSource = contents;
    }

    private static bool IsBagLocation(string? loc) =>
        !string.IsNullOrEmpty(loc) && (loc.Equals("inventory", StringComparison.OrdinalIgnoreCase) || loc.Equals("bag", StringComparison.OrdinalIgnoreCase));

    private static bool IsAccountStorageLocation(string? loc) =>
        !string.IsNullOrEmpty(loc) && (loc.Equals("account_storage", StringComparison.OrdinalIgnoreCase) || loc.Equals("accountstorage", StringComparison.OrdinalIgnoreCase));

    private static bool IsCharacterStorageLocation(string? loc) =>
        !string.IsNullOrEmpty(loc) && (loc.Equals("character_storage", StringComparison.OrdinalIgnoreCase) || loc.Equals("characterstorage", StringComparison.OrdinalIgnoreCase));

    // ================= 2. 가방 & 아이템 탭 (디바운싱 지원) =================
    private void UpdateItems(List<ItemData>? items)
    {
        if (items == null) return;
        _allItems = items;

        // 세션 시작(로그인 최초 수신 시) 가방 아이템 수량을 베이스라인으로 1회 기록
        if (!_hasBagBaseline && _allItems.Count > 0)
        {
            _initialBagItemCounts.Clear();
            foreach (var item in _allItems.Where(i => IsBagLocation(i.Location)))
            {
                if (_initialBagItemCounts.TryGetValue(item.DisplayName, out var existing))
                {
                    _initialBagItemCounts[item.DisplayName] = existing + item.Count;
                }
                else
                {
                    _initialBagItemCounts[item.DisplayName] = item.Count;
                }
            }
            _hasBagBaseline = true;
        }

        FilterItems();
    }

    private void UpdateItemFilterButtons()
    {
        if (!_isWindowLoaded || _allItems == null || BtnFilterAll == null || BtnFilterBag == null ||
            BtnFilterAccount == null || BtnFilterChar == null || BtnFilterDiet == null) return;

        var allCount = _allItems.Count;
        var bagCount = _allItems.Count(i => IsBagLocation(i.Location));
        var accCount = _allItems.Count(i => IsAccountStorageLocation(i.Location));
        var charCount = _allItems.Count(i => IsCharacterStorageLocation(i.Location));

        BtnFilterAll.Content = $"전체 ({allCount})";
        BtnFilterBag.Content = $"🎒 가방 ({bagCount})";
        BtnFilterAccount.Content = $"🏛️ 계정 창고 ({accCount})";
        BtnFilterChar.Content = $"👤 캐릭터 창고 ({charCount})";

        // 활성 탭 하이라이트
        var activeBrush = (Brush)FindResource("AccentBlue");
        var normalBrush = (Brush)FindResource("BgCard");
        var activeDietBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A3A1A"));
        var normalDietBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A2818"));

        BtnFilterAll.Background = _currentItemLocationFilter == "All" ? activeBrush : normalBrush;
        BtnFilterBag.Background = _currentItemLocationFilter == "Bag" ? activeBrush : normalBrush;
        BtnFilterAccount.Background = _currentItemLocationFilter == "AccountStorage" ? activeBrush : normalBrush;
        BtnFilterChar.Background = _currentItemLocationFilter == "CharacterStorage" ? activeBrush : normalBrush;
        BtnFilterDiet.Background = _currentItemLocationFilter == "Diet" ? activeDietBg : normalDietBg;
    }

    private void FilterItems()
    {
        if (!_isWindowLoaded || _allItems == null || TxtItemSearch == null || ListItemView == null || TxtItemCountLabel == null) return;

        UpdateItemFilterButtons();

        var query = TxtItemSearch.Text?.Trim() ?? "";

        // 1. 뷰모델 변환 (세션 시작 대비 수량 델타 계산)
        var viewItems = _allItems.Select(i =>
        {
            _initialBagItemCounts.TryGetValue(i.DisplayName, out var initial);
            return new ItemViewItem
            {
                Location = i.Location,
                DisplayName = i.DisplayName,
                CategoryName = i.CategoryName,
                Count = i.Count,
                IsLocked = i.IsLocked,
                InitialCount = initial
            };
        });

        // 2. 위치 및 다이어트 필터링
        if (_currentItemLocationFilter == "Diet")
        {
            // 가방에 있으면서 잠금 해제된 아이템 중:
            // 1순위: 이번 접속 세션에서 수량이 급격히 늘어난 아이템 (DeltaCount > 0, 증가량 내림차순)
            // 2순위: 기존 대량 소지 잡템 (Count 내림차순)
            viewItems = viewItems
                .Where(i => IsBagLocation(i.Location) && !i.IsLocked)
                .OrderByDescending(i => i.DeltaCount > 0)
                .ThenByDescending(i => i.DeltaCount)
                .ThenByDescending(i => i.Count);
        }
        else if (_currentItemLocationFilter == "Bag")
        {
            viewItems = viewItems.Where(i => IsBagLocation(i.Location));
        }
        else if (_currentItemLocationFilter == "AccountStorage")
        {
            viewItems = viewItems.Where(i => IsAccountStorageLocation(i.Location));
        }
        else if (_currentItemLocationFilter == "CharacterStorage")
        {
            viewItems = viewItems.Where(i => IsCharacterStorageLocation(i.Location));
        }
        else if (_currentItemLocationFilter != "All")
        {
            viewItems = viewItems.Where(i => i.Location.Equals(_currentItemLocationFilter, StringComparison.OrdinalIgnoreCase));
        }

        // 3. 텍스트 검색 필터링
        if (!string.IsNullOrEmpty(query))
        {
            viewItems = viewItems.Where(i => i.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                             i.CategoryName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = viewItems.ToList();
        ListItemView.ItemsSource = list;
        var filterLabel = _currentItemLocationFilter == "Diet" ? "⚖️ 급증 다이어트: " : "표시: ";
        TxtItemCountLabel.Text = $"{filterLabel}{list.Count}개 / 전체: {_allItems.Count}개";
    }

    private void TxtItemSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchDebounceTimer == null)
        {
            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _searchDebounceTimer.Tick += (s, ev) =>
            {
                _searchDebounceTimer.Stop();
                FilterItems();
            };
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void BtnItemFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string loc)
        {
            _currentItemLocationFilter = loc;
            FilterItems();
        }
    }

    // ================= 3. 재화 & 화폐 탭 =================
    public static string ClassifyCurrencyCategory(string name)
    {
        if (name.Contains("골드") || name.Contains("캐시") || name.Contains("다이아") || name.Contains("마일리지") || name.Contains("돈"))
            return "💰 기본 통화";
        if (name.Contains("날개") || name.Contains("환생") || name.Contains("룬") || name.Contains("데카") || name.Contains("갱신") || name.Contains("승급") || name.Contains("강화") || name.Contains("성장") || name.Contains("추출"))
            return "⚔️ 성장 & 강화";
        if (name.Contains("토큰") || name.Contains("증표") || name.Contains("인장") || name.Contains("코인") || name.Contains("길드") || name.Contains("하트"))
            return "🎫 토큰 & 교환";
        return "📦 기타 재화";
    }

    private void UpdateCurrencies(List<CurrencyItem>? list)
    {
        if (list == null) return;
        _lastCurrencies = list;

        var groups = list.GroupBy(c => ClassifyCurrencyCategory(c.DisplayName))
            .OrderBy(g => g.Key switch
            {
                "💰 기본 통화" => 1,
                "⚔️ 성장 & 강화" => 2,
                "🎫 토큰 & 교환" => 3,
                _ => 4
            })
            .Select(g => new CurrencyCategoryGroup
            {
                CategoryName = g.Key,
                Items = g.ToList()
            })
            .ToList();

        ListCurrencyGroups.ItemsSource = groups;
        UpdateDeltas();
    }

    // ================= 4. 미션 & 퀘스트 & 숙제 탭 =================
    private void UpdateMissions(List<MissionItem>? daily, List<MissionItem>? weekly, List<QuestItem>? quests = null)
    {
        if (daily != null)
        {
            _lastDailyMissions = daily;
            var completed = daily.Count(d => d.IsCompleted || d.CurrentCount >= d.GoalCount);
            ProgDailyMissions.Value = daily.Count > 0 ? (double)completed / daily.Count * 100 : 0;

            // 완료된 미션(3/3, 2/2 등)은 완전 제외하고 남은 미션만 필터링
            var remainingDaily = daily.Where(d => !d.IsCompleted && d.CurrentCount < d.GoalCount).ToList();
            ListDailyMissionsFull.ItemsSource = remainingDaily;
            TxtDailyMissionHeader.Text = $"📅 오늘의 잔여 일일 미션 ({remainingDaily.Count}개)";

            var allDailyDone = remainingDaily.Count == 0 && daily.Count > 0;
            TxtDailyAllDoneBanner.Visibility = allDailyDone ? Visibility.Visible : Visibility.Collapsed;
            ScrollDailyMissions.Visibility = allDailyDone ? Visibility.Collapsed : Visibility.Visible;
        }

        if (weekly != null)
        {
            _lastWeeklyMissions = weekly;
            var completed = weekly.Count(w => w.IsCompleted || w.CurrentCount >= w.GoalCount);
            ProgWeeklyMissions.Value = weekly.Count > 0 ? (double)completed / weekly.Count * 100 : 0;

            // 완료된 미션은 완전 제외하고 남은 미션만 필터링
            var remainingWeekly = weekly.Where(w => !w.IsCompleted && w.CurrentCount < w.GoalCount).ToList();
            ListWeeklyMissionsFull.ItemsSource = remainingWeekly;
            TxtWeeklyMissionHeader.Text = $"📆 이번 주 잔여 주간 미션 ({remainingWeekly.Count}개)";

            var allWeeklyDone = remainingWeekly.Count == 0 && weekly.Count > 0;
            TxtWeeklyAllDoneBanner.Visibility = allWeeklyDone ? Visibility.Visible : Visibility.Collapsed;
            ScrollWeeklyMissions.Visibility = allWeeklyDone ? Visibility.Collapsed : Visibility.Visible;
        }

        if (quests != null)
        {
            _lastQuests = quests;
        }

        EvaluateHomeworkStatus();
        RefreshHomeworkUi();

        UpdateDeltas();
    }

    private void EvaluateHomeworkStatus()
    {
        var charKey = _lastCharInfo != null
            ? $"{_lastCharInfo.RealmName}_{_lastCharInfo.JobName}"
            : "Default_Player";

        var ctx = new HomeworkEvaluationContext
        {
            CharacterKey = charKey,
            Character = _lastCharInfo,
            Environment = _lastEnvInfo,
            Activity = _lastActivity,
            DailyMissions = _lastDailyMissions,
            WeeklyMissions = _lastWeeklyMissions,
            AlteringWorks = _lastAlteringWorks,
            Quests = _lastQuests,
            Currencies = _lastCurrencies
        };

        _homeworkService.EvaluateAndSync(ctx, DateTime.Now);
    }

    private void RefreshHomeworkUi()
    {
        if (!_isWindowLoaded || ListHomeworkCards == null || ProgHomeworkDaily == null || ProgHomeworkWeekly == null ||
            TxtHomeworkDailyStats == null || TxtHomeworkWeeklyStats == null || TxtHomeworkResetInfo == null)
            return;

        var charKey = _lastCharInfo != null
            ? $"{_lastCharInfo.RealmName}_{_lastCharInfo.JobName}"
            : "Default_Player";

        var now = DateTime.Now;
        var items = _homeworkService.GetViewItems(charKey, _currentHomeworkCategory, now);
        ListHomeworkCards.ItemsSource = items;

        var stats = _homeworkService.GetProgressStats(charKey, now);

        ProgHomeworkDaily.Value = stats.dailyTotal > 0 ? (double)stats.dailyDone / stats.dailyTotal * 100 : 0;
        int dailyPct = stats.dailyTotal > 0 ? (stats.dailyDone * 100 / stats.dailyTotal) : 0;
        TxtHomeworkDailyStats.Text = $"{stats.dailyDone}/{stats.dailyTotal} ({dailyPct}%)";

        ProgHomeworkWeekly.Value = stats.weeklyTotal > 0 ? (double)stats.weeklyDone / stats.weeklyTotal * 100 : 0;
        int weeklyPct = stats.weeklyTotal > 0 ? (stats.weeklyDone * 100 / stats.weeklyTotal) : 0;
        TxtHomeworkWeeklyStats.Text = $"{stats.weeklyDone}/{stats.weeklyTotal} ({weeklyPct}%)";

        // 리셋 카운트다운 타이머 계산
        var nextDaily = HomeworkRepository.GetNextDailyResetTime(now);
        var nextWeekly = HomeworkRepository.GetNextWeeklyResetTime(now);
        var dailyRemain = nextDaily - now;
        var weeklyRemain = nextWeekly - now;

        string dailyRemainText = $"{(int)dailyRemain.TotalHours:D2}:{dailyRemain.Minutes:D2}:{dailyRemain.Seconds:D2}";
        string weeklyRemainText = weeklyRemain.Days > 0
            ? $"{weeklyRemain.Days}일 {weeklyRemain.Hours:D2}:{weeklyRemain.Minutes:D2}:{weeklyRemain.Seconds:D2}"
            : $"{weeklyRemain.Hours:D2}:{weeklyRemain.Minutes:D2}:{weeklyRemain.Seconds:D2}";

        if (TxtHomeworkResetInfo != null)
        {
            TxtHomeworkResetInfo.Text = $"다음 일일 리셋: {dailyRemainText} | 주간 리셋 (월 06시): {weeklyRemainText}";
        }
    }

    private void BtnViewHomeworkTracker_Click(object sender, RoutedEventArgs e)
    {
        PanelHomeworkTracker.Visibility = Visibility.Visible;
        PanelInGameMissions.Visibility = Visibility.Collapsed;
        BtnViewHomeworkTracker.Background = (Brush)FindResource("AccentYellow");
        BtnViewHomeworkTracker.Foreground = (Brush)FindResource("BgPrimary");
        BtnViewInGameMissions.Background = (Brush)FindResource("BgCard");
        BtnViewInGameMissions.Foreground = (Brush)FindResource("TextSecondary");
        RefreshHomeworkUi();
    }

    private void BtnViewInGameMissions_Click(object sender, RoutedEventArgs e)
    {
        PanelHomeworkTracker.Visibility = Visibility.Collapsed;
        PanelInGameMissions.Visibility = Visibility.Visible;
        BtnViewInGameMissions.Background = (Brush)FindResource("AccentYellow");
        BtnViewInGameMissions.Foreground = (Brush)FindResource("BgPrimary");
        BtnViewHomeworkTracker.Background = (Brush)FindResource("BgCard");
        BtnViewHomeworkTracker.Foreground = (Brush)FindResource("TextSecondary");
    }

    private void RbHomeworkFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isWindowLoaded || sender is not RadioButton rb) return;

        _currentHomeworkCategory = rb.Name switch
        {
            "RbHwDaily" => HomeworkCategory.Daily,
            "RbHwWeekly" => HomeworkCategory.Weekly,
            "RbHwFieldBoss" => HomeworkCategory.FieldBoss,
            "RbHwAbyss" => HomeworkCategory.Abyss,
            "RbHwRaid" => HomeworkCategory.Raid,
            "RbHwShop" => HomeworkCategory.Shop,
            _ => HomeworkCategory.All
        };

        RefreshHomeworkUi();
    }

    private void BtnToggleHomework_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string homeworkId })
        {
            var charKey = _lastCharInfo != null
                ? $"{_lastCharInfo.RealmName}_{_lastCharInfo.JobName}"
                : "Default_Player";

            _homeworkService.ToggleManual(homeworkId, charKey, DateTime.Now);
            RefreshHomeworkUi();
            ShowToast("숙제 완료 상태를 수동 변경했습니다.", true);
        }
    }

    private void BtnResetHomeworkChecks_Click(object sender, RoutedEventArgs e)
    {
        var charKey = _lastCharInfo != null
            ? $"{_lastCharInfo.RealmName}_{_lastCharInfo.JobName}"
            : "Default_Player";

        _homeworkService.ResetAllManual(charKey, DateTime.Now);
        RefreshHomeworkUi();
        ShowToast("현재 캐릭터의 모든 숙제 체크를 초기화했습니다.", true);
    }

    // ================= 5. 생활 & 생산 탭 (150종 전체 스크롤 지원) =================
    private void UpdateLifeAndCraft(AlteringWorksResponse? alter, GatherableResponse? gather)
    {
        _lastAlteringWorks = alter;
        if (alter != null && alter.Works != null)
        {
            _allAlteringWorks = alter.Works.OrderBy(w => w.FacilityName).ThenBy(w => w.RemainingSeconds).ToList();
            ListAlteringWorks.ItemsSource = _allAlteringWorks;
        }

        if (gather != null && gather.Items != null)
        {
            _allGatherables = gather.Items;
            FilterGatherablesFull();
        }
    }

    private static string ClassifyGatherCategory(string name)
    {
        if (name.Contains("장작") || name.Contains("나무") || name.Contains("가지") || name.Contains("통나무"))
            return "벌목";
        if (name.Contains("광석") || name.Contains("철") || name.Contains("구리") || name.Contains("은") || name.Contains("금") || name.Contains("보석") || name.Contains("석영") || name.Contains("유황") || name.Contains("돌멩이"))
            return "채광";
        if (name.Contains("사과") || name.Contains("달걀") || name.Contains("양털") || name.Contains("우유") || name.Contains("감자") || name.Contains("옥수수") || name.Contains("보리") || name.Contains("밀"))
            return "농축산";
        if (name.Contains("초") || name.Contains("풀") || name.Contains("꽃") || name.Contains("버섯") || name.Contains("클로버"))
            return "약초";
        return "기타";
    }

    private void UpdateGatherCategoryButtons()
    {
        if (!_isWindowLoaded || BtnGatherCatAll == null || BtnGatherCatLogging == null || BtnGatherCatMining == null ||
            BtnGatherCatFarm == null || BtnGatherCatHerb == null || BtnGatherCatEtc == null) return;

        var activeBrush = (Brush)FindResource("AccentBlue");
        var normalBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x35));

        BtnGatherCatAll.Background = _currentGatherCategory == "All" ? activeBrush : normalBrush;
        BtnGatherCatLogging.Background = _currentGatherCategory == "벌목" ? activeBrush : normalBrush;
        BtnGatherCatMining.Background = _currentGatherCategory == "채광" ? activeBrush : normalBrush;
        BtnGatherCatFarm.Background = _currentGatherCategory == "농축산" ? activeBrush : normalBrush;
        BtnGatherCatHerb.Background = _currentGatherCategory == "약초" ? activeBrush : normalBrush;
        BtnGatherCatEtc.Background = _currentGatherCategory == "기타" ? activeBrush : normalBrush;
    }

    private void FilterGatherablesFull()
    {
        if (!_isWindowLoaded || _allGatherables == null || TxtGatherSearchFull == null || ListGatherablesView == null) return;

        UpdateGatherCategoryButtons();

        var q = TxtGatherSearchFull.Text?.Trim() ?? "";

        // 1. 카테고리 및 아이템 뷰모델 생성 (가방 내 전체 슬롯 합산 소지수 및 목표 대비 부족 수량 연산)
        var list = _allGatherables.Select(g =>
        {
            var cat = ClassifyGatherCategory(g.DisplayName);
            var currentBag = _allItems.Where(i => IsBagLocation(i.Location) &&
                                                  i.DisplayName.Equals(g.DisplayName, StringComparison.OrdinalIgnoreCase))
                                      .Sum(i => i.Count);

            return new GatherableDisplayItem
            {
                DisplayName = g.DisplayName,
                Category = cat,
                ToolOk = g.ToolOk,
                CurrentBagCount = currentBag,
                TargetCount = _targetGatherCount
            };
        });

        // 2. 카테고리 필터링
        if (_currentGatherCategory != "All")
        {
            list = list.Where(g => g.Category == _currentGatherCategory);
        }

        // 3. 검색어 필터링
        if (!string.IsNullOrEmpty(q))
        {
            list = list.Where(g => g.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        ListGatherablesView.ItemsSource = list.ToList();
    }

    private void BtnGatherCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cat)
        {
            _currentGatherCategory = cat;
            FilterGatherablesFull();
        }
    }

    private void BtnTargetPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var preset))
        {
            _targetGatherCount = Math.Max(1, preset);
            if (TxtTargetGatherCount != null)
            {
                TxtTargetGatherCount.Text = _targetGatherCount.ToString();
            }
            FilterGatherablesFull();
        }
    }

    private void BtnAdjustTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var delta))
        {
            _targetGatherCount = Math.Clamp(_targetGatherCount + delta, 1, 9999);
            if (TxtTargetGatherCount != null)
            {
                TxtTargetGatherCount.Text = _targetGatherCount.ToString();
            }
            FilterGatherablesFull();
        }
    }

    private void TxtTargetGatherCount_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isWindowLoaded || TxtTargetGatherCount == null) return;
        if (int.TryParse(TxtTargetGatherCount.Text, out var val) && val > 0)
        {
            _targetGatherCount = val;
            FilterGatherablesFull();
        }
    }

    private void TxtGatherSearchFull_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isWindowLoaded) return;
        FilterGatherablesFull();
    }

    private async void BtnStartGatherTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string itemName)
        {
            var current = _allItems.Where(i => IsBagLocation(i.Location) &&
                                               i.DisplayName.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                                   .Sum(i => i.Count);
            var needed = _targetGatherCount - current;

            if (needed <= 0)
            {
                ShowToast($"'{itemName}'은(는) 이미 목표 수량({_targetGatherCount}개) 이상 소지하고 있습니다! (현재: {current}개)", true);
                return;
            }

            await StartGatherAsync(itemName, needed);
        }
    }

    private async Task StartGatherAsync(string itemName, int neededCount = 0)
    {
        var countMsg = neededCount > 0 ? $" (목표 부족 {neededCount}개)" : "";
        ShowToast($"🌾 '{itemName}'{countMsg} 채집을 시작합니다... (정령의 날개 5개 소모)", true);

        // CLI 스펙: displayName 단일 필드 전송 (최대 100회 자동 채집)
        var body = $"{{\"displayName\":\"{itemName}\"}}";
        var (ok, stdout, err) = await _cli.RunRawAsync("execute_gathering", stdinJson: body, timeoutSeconds: 120);
        if (ok)
        {
            var msg = $"'{itemName}' 채집 명령이 전달되었습니다!";
            if (!string.IsNullOrEmpty(stdout))
            {
                try
                {
                    using var doc = JsonDocument.Parse(stdout);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("result", out var resElem))
                    {
                        var resStr = resElem.GetString();
                        var gained = root.TryGetProperty("gained", out var gElem) ? gElem.GetInt32() : 0;
                        msg = resStr switch
                        {
                            "completed" => $"'{itemName}' 목표 채집을 완료했습니다! (+{gained}개 획득)",
                            "started" => $"'{itemName}' 자동 채집/낚시가 시작되었습니다!",
                            "stopped" => $"'{itemName}' 채집이 종료되었습니다. (+{gained}개 획득)",
                            _ => $"'{itemName}' 채집: {resStr} (+{gained}개)"
                        };
                    }
                    else if (root.TryGetProperty("message", out var mElem))
                    {
                        msg = mElem.GetString() ?? msg;
                    }
                }
                catch { }
            }
            ShowToast(msg, true);
            await RefreshCurrentTabAsync();
        }
        else
        {
            ShowToast($"채집 실패: {err}", false);
        }
    }

    private void BtnActionDailyMissions_Click(object sender, RoutedEventArgs e)
    {
        TabMissions.IsSelected = true;
        if (_lastDailyMissions != null && _lastDailyMissions.Count > 0)
        {
            var pending = _lastDailyMissions.Where(m => !m.IsCompleted && m.CurrentCount < m.GoalCount).ToList();
            if (pending.Count > 0)
            {
                var summary = string.Join(", ", pending.Take(2).Select(m => m.Title));
                ShowToast($"📋 미완료 일일 숙제 {pending.Count}건 남음: {summary}...", true);
            }
            else
            {
                ShowToast("🎉 오늘의 모든 일일 숙제를 완료했습니다!", true);
            }
        }
        else
        {
            ShowToast("📋 일일 미션 탭으로 이동했습니다. 갱신을 확인하세요.", true);
        }
    }

    private void BtnActionDiet_Click(object sender, RoutedEventArgs e)
    {
        TabInventory.IsSelected = true;
        _currentItemLocationFilter = "Diet";
        FilterItems();
        ShowToast("⚖️ 무게 다이어트 필터 활성화: 가방 내 비잠금 잡템을 우선 정렬했습니다.", true);
    }

    private void BtnActionCollectWorks_Click(object sender, RoutedEventArgs e)
    {
        BtnCollectWorks_Click(sender, e);
    }

    private async void BtnActionStop_Click(object sender, RoutedEventArgs e)
    {
        ShowToast("🛑 긴급 행동 정지를 요청했습니다...", true);
        var (ok, _, err) = await _cli.RunRawAsync("stop_action", timeoutSeconds: 3);
        if (ok)
        {
            ShowToast("🛑 캐릭터의 모든 행동/채집을 즉시 중단했습니다.", true);
        }
        else
        {
            ShowToast($"행동 정지 실패: {err}", false);
        }
    }

    private async void BtnCollectWorks_Click(object sender, RoutedEventArgs e)
    {
        if (_allAlteringWorks.Count == 0)
        {
            ShowToast("현재 진행 중이거나 완료된 가공 작업이 없습니다.", false);
            return;
        }

        var completed = _allAlteringWorks.FirstOrDefault(w => w.IsCompleted || w.RemainingSeconds == 0);
        if (completed == null)
        {
            ShowToast("아직 완료된 가공 작업이 없습니다. 남은 시간을 확인해주세요.", false);
            return;
        }

        ShowToast($"⚗️ '{completed.DisplayName}' 수거 시설로 이동 중...", true);
        var body = JsonSerializer.Serialize(new { displayName = completed.DisplayName });
        var (ok, _, err) = await _cli.RunRawAsync("complete_altering_work", stdinJson: body, timeoutSeconds: 60);
        if (ok)
        {
            ShowToast($"'{completed.DisplayName}' 가공물을 성공적으로 수거했습니다!", true);
            await RefreshCurrentTabAsync();
        }
        else
        {
            ShowToast($"수거 실패: {err}", false);
        }
    }

    // ================= 6. 주변 레이더 탭 =================
    private void UpdateNearPcs(List<NearPcItem>? pcs)
    {
        if (pcs == null) return;
        var myCombatScore = _lastCharInfo?.CombatScore?.Value ?? 0L;

        // 우선순위: 1) 파티원(0), 2) 친구(1), 3) 같은 길드원(2), 4) 일반 유저(3)
        static int GetPriority(NearPcItem p)
        {
            if (p.IsInParty) return 0;
            if (p.IsFriend) return 1;
            if (p.IsSameGuild) return 2;
            return 3;
        }

        var viewItems = pcs
            .OrderBy(GetPriority)
            .ThenByDescending(p => p.CombatScore)
            .ThenBy(p => p.Distance)
            .Select(p => NearPcViewItem.FromRaw(p, GetJobIcon(p.JobName), myCombatScore > 0 && p.CombatScore > myCombatScore))
            .ToList();

        ListNearPcsView.ItemsSource = viewItems;
        if (TxtRadarHeader != null)
        {
            TxtRadarHeader.Text = $"📡 내 주변 플레이어 실시간 레이더 ({viewItems.Count}명)";
        }
    }

    // ================= 7. 하단 인게임 전체 채팅 (이모티콘 & 소셜 액션 연동) =================
    private void TxtGameChatInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var raw = TxtGameChatInput.Text;
        var len = raw.Length;
        TxtCharLimit.Text = $"{len} / 50자";
        TxtCharLimit.Foreground = len >= 45 ? (Brush)FindResource("AccentRed") : (Brush)FindResource("TextSecondary");

        var hint = ChatPlanService.GetPreviewHint(raw);
        TxtChatEmotePreview.Text = string.IsNullOrEmpty(hint) ? "" : $"[자동: {hint}]";
    }

    private async void TxtGameChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SendGameChatInternalAsync();
        }
    }

    private async void BtnSendGameChat_Click(object sender, RoutedEventArgs e)
    {
        await SendGameChatInternalAsync();
    }

    private async Task SendGameChatInternalAsync()
    {
        var rawText = TxtGameChatInput.Text.Trim();
        if (string.IsNullOrEmpty(rawText)) return;

        TxtGameChatInput.Clear();
        TxtChatEmotePreview.Text = "";

        var plan = ChatPlanService.BuildChatPlan(rawText);
        var time = DateTime.Now.ToString("HH:mm:ss");

        // 1차: 이모티콘이 안전하게 부착된 대사 전송
        var (ok, err) = await _cli.SendGameChatAsync(plan.FinalMessage);

        if (ok)
        {
            var logMsg = plan.BehaviourCommand != null
                ? $"{plan.FinalMessage} (행동: {plan.BehaviourCommand})"
                : plan.FinalMessage;

            GameChatLogs.Add(new ChatLogEntry($"[{time}] ✓ 전송 완료", logMsg, true));
            ShowToast($"채팅 전송 완료: \"{plan.FinalMessage}\"", true);

            // 2차: 소셜 액션(행동) 전송 (Fail-Safe: 1차 성공 시에만 0.15초 후 실행)
            if (!string.IsNullOrEmpty(plan.BehaviourCommand))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(150);
                    await _cli.SendGameChatAsync(plan.BehaviourCommand);
                });
            }
        }
        else
        {
            GameChatLogs.Add(new ChatLogEntry($"[{time}] ✗ 전송 실패 ({err})", plan.FinalMessage, false, err));
            ShowToast($"채팅 전송 실패: {err}", false);
        }

        ScrollGameChatLog.ScrollToBottom();
    }

    // ================= 7-1. 아무말 대잔치 (페르소나 혼잣말) 제어 =================
    private ChatterContext GetCurrentChatterContext()
    {
        var ch = _lastCharInfo;
        var realm = string.IsNullOrWhiteSpace(ch?.RealmName) ? "에린" : ch.RealmName;
        var job = string.IsNullOrWhiteSpace(ch?.JobName) ? "밀레시안" : ch.JobName;
        var level = ch?.Level ?? 1;
        var combatScore = ch?.CombatScore?.Value ?? 0;
        var activity = TxtActivity?.Text ?? "자유 활동";
        var location = TxtLocation?.Text ?? "티르코네일";
        var weightInfo = TxtWeightSummary?.Text ?? "정상";
        var goldItem = _lastCurrencies?.FirstOrDefault(c => c.DisplayName.Contains("골드"));
        var goldInfo = goldItem != null ? $"{goldItem.Amount:N0} 골드" : "100,000 골드";
        var erinnTime = _lastEnvInfo?.ErinnNow ?? "";

        return new ChatterContext(realm, job, level, combatScore, activity, location, weightInfo, goldInfo, erinnTime);
    }

    private List<CustomPersona> _customPersonas = new();

    private void LoadCustomPersonasToUi()
    {
        if (CmbPersona == null) return;

        _customPersonas = _snapshotManager.LoadCustomPersonas();

        // 기존 내장 5종을 제외한 커스텀 아이템 정리
        var builtInTags = new HashSet<string> { "Villainess", "Scrooge", "MorningSpirit", "GyeongsangAhjussi", "IdolDancer" };
        for (int i = CmbPersona.Items.Count - 1; i >= 0; i--)
        {
            if (CmbPersona.Items[i] is ComboBoxItem item && item.Tag is string tag && !builtInTags.Contains(tag))
            {
                CmbPersona.Items.RemoveAt(i);
            }
        }

        // 세이브 파일의 커스텀 페르소나들 추가
        foreach (var cp in _customPersonas)
        {
            var item = new ComboBoxItem
            {
                Content = cp.DisplayName,
                Tag = cp.Id,
                ToolTip = cp.SystemPrompt
            };
            CmbPersona.Items.Add(item);
        }
    }

    private void CmbPersona_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isWindowLoaded || _chatterService == null || CmbPersona == null) return;
        if (CmbPersona.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            var custom = _customPersonas.FirstOrDefault(p => p.Id == tag);
            if (custom != null)
            {
                _chatterService.CurrentPersona = ChatterPersona.Custom;
                _chatterService.CurrentCustomPersona = custom;
                ShowToast($"페르소나가 커스텀 '{custom.DisplayName}'(으)로 변경되었습니다.", true);
            }
            else
            {
                _chatterService.CurrentPersona = tag switch
                {
                    "Villainess" => ChatterPersona.Villainess,
                    "Scrooge" => ChatterPersona.Scrooge,
                    "MorningSpirit" => ChatterPersona.MorningSpirit,
                    "GyeongsangAhjussi" => ChatterPersona.GyeongsangAhjussi,
                    "IdolDancer" => ChatterPersona.IdolDancer,
                    _ => ChatterPersona.Villainess
                };
                _chatterService.CurrentCustomPersona = null;
                ShowToast($"페르소나가 '{item.Content}'(으)로 변경되었습니다.", true);
            }
        }
    }

    private void BtnAddCustomPersona_Click(object sender, RoutedEventArgs e)
    {
        PopupCustomPersona.IsOpen = true;
        TxtCustomPersonaName.Focus();
    }

    private void BtnCancelCustomPersona_Click(object sender, RoutedEventArgs e)
    {
        PopupCustomPersona.IsOpen = false;
    }

    private void BtnSaveCustomPersona_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtCustomPersonaName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowToast("페르소나 명칭을 입력해주세요.", false);
            return;
        }

        var emoji = string.IsNullOrWhiteSpace(TxtCustomPersonaEmoji.Text) ? "🎭" : TxtCustomPersonaEmoji.Text.Trim();
        var prompt = TxtCustomPersonaPrompt.Text.Trim();

        var cp = new CustomPersona
        {
            Name = name,
            TagEmoji = emoji,
            SystemPrompt = prompt
        };

        _snapshotManager.SaveCustomPersona(cp);
        LoadCustomPersonasToUi();

        // 새로 생성된 페르소나 선택
        for (int i = 0; i < CmbPersona.Items.Count; i++)
        {
            if (CmbPersona.Items[i] is ComboBoxItem item && (string)item.Tag == cp.Id)
            {
                CmbPersona.SelectedIndex = i;
                break;
            }
        }

        PopupCustomPersona.IsOpen = false;
        ShowToast($"새로운 페르소나 '{cp.DisplayName}'이(가) 세이브 파일에 저장되었습니다!", true);
    }

    private async void BtnTriggerChatterNow_Click(object sender, RoutedEventArgs e)
    {
        if (_chatterService == null) return;

        BtnTriggerChatterNow.IsEnabled = false;
        BtnTriggerChatterNow.Content = "⏳...";

        try
        {
            var line = await _chatterService.GenerateChatterLineAsync();
            if (!string.IsNullOrWhiteSpace(line))
            {
                TxtGameChatInput.Text = line;
                TxtGameChatInput.Focus();
                TxtGameChatInput.CaretIndex = TxtGameChatInput.Text.Length;
                ShowToast("💬 대사가 생성되어 입력창에 채워졌습니다. '게임 전송'을 누르면 발송됩니다.", true);
            }
        }
        catch (Exception ex)
        {
            ShowToast($"대사 생성 실패: {ex.Message}", false);
        }
        finally
        {
            BtnTriggerChatterNow.IsEnabled = true;
            BtnTriggerChatterNow.Content = "💬 한마디";
        }
    }

    // ================= 8. 하단 AI 코파일럿 대화 (다중 엔진 자동 감지 및 폴백) =================
    private async Task LoadAiEnginesAsync()
    {
        CmbAiEngine.Items.Clear();
        var engines = await _aiManager.DiscoverEnginesAsync();

        foreach (var engine in engines)
        {
            var displayName = engine.Info.Type == AiEngineType.BuiltInGuide
                ? "기본값 : AI 사용하지 않음"
                : engine.Info.DisplayName;

            CmbAiEngine.Items.Add(new ComboBoxItem
            {
                Content = displayName,
                Tag = engine.Info.Id,
                ToolTip = engine.Info.Description
            });
        }

        // 기본 엔진 자동 선택
        var current = _aiManager.CurrentEngine;
        if (current != null)
        {
            for (int i = 0; i < CmbAiEngine.Items.Count; i++)
            {
                if (CmbAiEngine.Items[i] is ComboBoxItem item && (string)item.Tag == current.Info.Id)
                {
                    CmbAiEngine.SelectedIndex = i;
                    break;
                }
            }
        }
        else if (CmbAiEngine.Items.Count > 0)
        {
            CmbAiEngine.SelectedIndex = 0;
        }

        // 내장 가이드 여부에 따라 가이드 쉘프 및 배너 초기화
        UpdateEngineBannerAndShelf(current);

        // 상태 안내 토스트
        var hasOllama = engines.Any(e => e.Info.Type == AiEngineType.Ollama);
        if (hasOllama)
        {
            ShowToast($"🤖 로컬 AI(Ollama) 감지 완료 (엔진 {engines.Count}개 준비)", true);
        }
    }

    private async void BtnReloadAiModels_Click(object sender, RoutedEventArgs e)
    {
        ShowToast("설치된 AI 엔진을 새로고침하는 중...", true);
        await LoadAiEnginesAsync();
    }

    private void CmbAiEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isWindowLoaded) return;

        if (CmbAiEngine?.SelectedItem is ComboBoxItem item && item.Tag is string engineId)
        {
            _aiManager.SetCurrentEngine(engineId);
            var engine = _aiManager.CurrentEngine;
            var engineName = engine?.Info.DisplayName ?? (item.Content as string ?? "AI");

            UpdateEngineBannerAndShelf(engine);

            if (engine?.Info.Type == AiEngineType.CliAgent)
            {
                ShowToast($"⚠️ CLI 에이전트 '{engine.Info.TargetModel}' 선택됨 (사용자 계정 정책 적용)", true);
            }
            else
            {
                ShowToast($"🤖 AI 엔진이 '{engineName}'(으)로 전환되었습니다.", true);
            }
        }
    }

    private void UpdateEngineBannerAndShelf(IAiEngine? engine)
    {
        var isBuiltIn = engine?.Info.Type == AiEngineType.BuiltInGuide;
        GuideShelfGrid.Visibility = isBuiltIn ? Visibility.Visible : Visibility.Collapsed;

        if (isBuiltIn)
        {
            TxtBannerEngineStatus.Text = "기본값 : AI 사용하지 않음 (로컬 AI 연동 시 자유 대화 가능)";
            TxtBannerEngineStatus.Foreground = (Brush)FindResource("AccentCyan");
            BannerEngineStatus.Background = new SolidColorBrush(Color.FromRgb(0x13, 0x1A, 0x26));
            BannerEngineStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x2D, 0x42));
        }
        else if (engine?.Info.Type == AiEngineType.Ollama)
        {
            TxtBannerEngineStatus.Text = $"🤖 로컬 LLM '{engine.Info.DisplayName}' 활성화됨 (자유 대화 가능, 100% 무료)";
            TxtBannerEngineStatus.Foreground = (Brush)FindResource("AccentGreen");
            BannerEngineStatus.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x29, 0x1E));
            BannerEngineStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x42, 0x2E));
        }
        else
        {
            TxtBannerEngineStatus.Text = $"⚡ CLI 에이전트 '{engine?.Info.DisplayName}' 활성화됨 (사용자 계정 정책 적용)";
            TxtBannerEngineStatus.Foreground = (Brush)FindResource("AccentYellow");
            BannerEngineStatus.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x26, 0x14));
            BannerEngineStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x3F, 0x1D));
        }

        // 아무말 대잔치: 페르소나 안내 뱃지 및 추가 버튼 전환
        if (BorderPersonaHintBadge != null)
        {
            BorderPersonaHintBadge.Visibility = isBuiltIn ? Visibility.Visible : Visibility.Collapsed;
        }
        if (BtnAddCustomPersona != null)
        {
            BtnAddCustomPersona.Visibility = isBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        }

        // 기본값(AI 미사용) 모드에서는 커스텀 페르소나를 지원하지 않으므로 기본 페르소나로 자동 롤백
        if (isBuiltIn && _chatterService?.CurrentPersona == ChatterPersona.Custom)
        {
            if (CmbPersona != null && CmbPersona.Items.Count > 0)
            {
                CmbPersona.SelectedIndex = 0;
                ShowToast("기본값(AI 미사용) 모드로 전환되어 기본 페르소나로 자동 변경되었습니다.", true);
            }
        }
    }

    private void BtnGuideSentence_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            var prompt = tag switch
            {
                "전투력" => "현재 전투력 기준 1티어 룬 세팅 및 무기 각인 공략 알려줘",
                "골드" => "일일 미션과 요일 던전 보상으로 골드 빠르게 모으는 법 알려줘",
                "채집" => "가공 시설 쿨타임 관리법과 철광석·약초·목재 채집 명당 알려줘",
                "던전" => "보스 브레이크 게이지 파훼법 및 장판 회피 요령 알려줘",
                "진단" => "현재 캐릭터 실시간 스탯, 전투력, 가방, 미션 정밀 진단해줘",
                "창고" => "가방 무게 초과 방지 및 계정 창고/캐릭터 창고 정리법 알려줘",
                _ => btn.Content?.ToString() ?? "공략 가이드"
            };

            ExecuteAiQueryDirect(prompt);
        }
    }

    private void ScrollGuideShelf_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scv)
        {
            scv.ScrollToHorizontalOffset(scv.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    // 기존 칩 버튼 클릭 및 휠 핸들러 하위 호환 유지
    private void BtnQuickChip_Click(object sender, RoutedEventArgs e) => BtnGuideSentence_Click(sender, e);
    private void ScrollQuickChips_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => ScrollGuideShelf_PreviewMouseWheel(sender, e);

    private void ExecuteAiQueryDirect(string query)
    {
        if (!BtnSendAi.IsEnabled) return;
        TxtAiInput.Clear();
        AiMessages.Add(new AiMessageEntry("나", query, true));
        _ = ProcessAiQueryAsync(query);
    }

    private void TxtAiInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendAiQueryInternal();
        }
    }

    private void BtnSendAi_Click(object sender, RoutedEventArgs e)
    {
        SendAiQueryInternal();
    }

    private void SendAiQueryInternal()
    {
        if (!BtnSendAi.IsEnabled) return;

        var query = TxtAiInput.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        TxtAiInput.Clear();
        AiMessages.Add(new AiMessageEntry("나", query, true));

        _ = ProcessAiQueryAsync(query);
    }

    private async Task ProcessAiQueryAsync(string query)
    {
        var currentEngine = _aiManager.CurrentEngine;
        var displayModelName = currentEngine?.Info.DisplayName ?? "AI 코파일럿";

        var gatherNames = _allGatherables?.Select(g => g.DisplayName).ToList();
        var intent = CommandIntentParser.Parse(query, gatherNames);

        // 1. 긴급 정지 (W-04 엄격한 단독 명령 매칭)
        if (intent.Kind == IntentKind.Stop)
        {
            _ = EmergencyStopInternalAsync();
            AiMessages.Add(new AiMessageEntry(displayModelName, "🛑 캐릭터의 진행 중인 행동(채집, 이동 등)을 즉시 긴급 정지시켰습니다!", false));
            ScrollAiFeed.ScrollToBottom();
            return;
        }

        // 2. AI 페르소나 자동 생성 및 영구 저장
        if (intent.Kind == IntentKind.CreatePersona)
        {
            await HandleCreatePersonaIntentAsync(query, intent.PersonaConcept ?? "맞춤 스타일", currentEngine, displayModelName);
            return;
        }

        // 3. 일일 숙제 점검 키워드 처리
        if (intent.Kind == IntentKind.CheckDailyMissions)
        {
            BtnActionDailyMissions_Click(this, new RoutedEventArgs());
            AiMessages.Add(new AiMessageEntry(displayModelName, "📋 미션 탭으로 이동하여 잔여 일일 숙제 목록을 점검했습니다.", false));
            ScrollAiFeed.ScrollToBottom();
            return;
        }

        // 4. 가방 다이어트 키워드 처리
        if (intent.Kind == IntentKind.InventoryDiet)
        {
            BtnActionDiet_Click(this, new RoutedEventArgs());
            AiMessages.Add(new AiMessageEntry(displayModelName, "⚖️ 가방 다이어트 필터를 활성화하여 이번 세션에 급증한 잡템 및 무거운 물품을 우선 정렬했습니다.", false));
            ScrollAiFeed.ScrollToBottom();
            return;
        }

        // 5. 가공 작업대 수거 키워드 처리
        if (intent.Kind == IntentKind.CollectWorks)
        {
            BtnActionCollectWorks_Click(this, new RoutedEventArgs());
            AiMessages.Add(new AiMessageEntry(displayModelName, "📥 완료된 가공 작업물 수거를 요청했습니다.", false));
            ScrollAiFeed.ScrollToBottom();
            return;
        }

        // 6. 채집/낚시 자연어 명령 즉시 디스패치
        if (intent.Kind == IntentKind.Gather && !string.IsNullOrWhiteSpace(intent.ItemName))
        {
            _ = StartGatherAsync(intent.ItemName, intent.Count ?? 0);
            var countSuffix = intent.Count > 0 ? $" (목표 {intent.Count}개)" : "";
            AiMessages.Add(new AiMessageEntry(displayModelName, $"🌾 '{intent.ItemName}' 채집을 즉시 시작합니다! 캐릭터가 채집 장소로 자동 이동합니다.{countSuffix} (정령의 날개 5개 소모)", false));
            ScrollAiFeed.ScrollToBottom();
            return;
        }

        // 현재 게임 상황을 정밀한 시스템 컨텍스트로 구성
        var ch = _lastCharInfo;
        var realm = string.IsNullOrWhiteSpace(ch?.RealmName) ? "에린" : ch.RealmName;
        var job = string.IsNullOrWhiteSpace(ch?.JobName) ? "밀레시안" : ch.JobName;
        var level = ch?.Level ?? 1;
        var combatScore = ch?.CombatScore?.Value ?? 0;
        var activity = TxtActivity.Text;
        var location = TxtLocation.Text;
        var weightInfo = TxtWeightSummary.Text;

        var systemContext =
            $"당신은 넥슨 '마비노기 모바일'의 든든한 플레이 동반자 AI 도우미(MobiMate)입니다.\n" +
            $"[현재 플레이어 실시간 게임 상태]\n" +
            $"- 서버: {realm} / 직업: {job} (Lv.{level})\n" +
            $"- 전투력: {combatScore:N0}점\n" +
            $"- 현재 상태: {activity} / 위치: {location}\n" +
            $"- 가방 무게 현황: {weightInfo}\n\n" +
            $"[MobiMate 내장 기능 안내]\n" +
            $"- 사용자가 새로운 페르소나 생성을 요청하면 MobiMate가 이를 자동으로 프롬프트화하여 세이브 파일(custom_personas.json)에 영구 저장하고 즉시 등록합니다. 가짜 메뉴나 수동 조작 경로를 안내하지 마세요.\n\n" +
            $"플레이어의 질문에 대해 마비노기 모바일 게임 공략과 현재 캐릭터 상태에 맞추어 친절하고 간결하게 2~3문장 이내의 한국어로 답변해주세요.";

        BtnSendAi.IsEnabled = false;
        BtnSendAi.Content = "생각 중... 💭";

        try
        {
            var res = await _aiManager.AskCurrentEngineAsync(query, systemContext);
            if (res.Success)
            {
                AiMessages.Add(new AiMessageEntry(displayModelName, res.Reply, false));
            }
            else
            {
                AiMessages.Add(new AiMessageEntry(displayModelName, $"⚠️ {res.ErrorMessage}", false));
            }
        }
        catch (Exception ex)
        {
            AiMessages.Add(new AiMessageEntry(displayModelName, $"⚠️ 오류 발생: {ex.Message}", false));
        }
        finally
        {
            BtnSendAi.IsEnabled = true;
            BtnSendAi.Content = "AI 질문 ↵";
            ScrollAiFeed.ScrollToBottom();
        }
    }

    private async Task HandleCreatePersonaIntentAsync(string query, string concept, IAiEngine? currentEngine, string displayModelName)
    {
        BtnSendAi.IsEnabled = false;
        BtnSendAi.Content = "페르소나 구상 중... 🎭";

        try
        {
            var generator = new PersonaGeneratorService(currentEngine);
            var persona = await generator.GeneratePersonaAsync(query, concept);

            // 세이브 파일(custom_personas.json)에 영구 저장
            _snapshotManager.SaveCustomPersona(persona);

            // UI 즉시 동기화 및 콤보박스 선택
            LoadCustomPersonasToUi();

            for (int i = 0; i < CmbPersona.Items.Count; i++)
            {
                if (CmbPersona.Items[i] is ComboBoxItem item && (string)item.Tag == persona.Id)
                {
                    CmbPersona.SelectedIndex = i;
                    break;
                }
            }

            if (_chatterService != null)
            {
                _chatterService.CurrentPersona = ChatterPersona.Custom;
                _chatterService.CurrentCustomPersona = persona;
            }

            var replyMsg =
                $"✨ 새로운 페르소나 '{persona.DisplayName}' 생성이 완료되었습니다!\n\n" +
                $"📋 [성격 및 말투 프롬프트]\n\"{persona.SystemPrompt}\"\n\n" +
                $"💾 세이브 파일(custom_personas.json)에 프롬프트가 영구 저장되었습니다.\n" +
                $"좌측 '🗣️ 아무말 대잔치'에 즉시 적용되었으며, 앱을 다시 시작해도 언제든 선택하여 즐기실 수 있습니다!";

            AiMessages.Add(new AiMessageEntry(displayModelName, replyMsg, false));
            ShowToast($"'{persona.DisplayName}' 페르소나가 저장 및 활성화되었습니다!", true);
        }
        catch (Exception ex)
        {
            AiMessages.Add(new AiMessageEntry(displayModelName, $"⚠️ 페르소나 생성 실패: {ex.Message}", false));
        }
        finally
        {
            BtnSendAi.IsEnabled = true;
            BtnSendAi.Content = "AI 질문 ↵";
            ScrollAiFeed.ScrollToBottom();
        }
    }

    // ================= 9. 긴급 정지 (ESC 단축키 및 버튼 공통) =================
    private async void BtnEmergencyStop_Click(object sender, RoutedEventArgs e)
    {
        await EmergencyStopInternalAsync();
    }

    private async Task EmergencyStopInternalAsync()
    {
        var (ok, _, err) = await _cli.RunRawAsync("stop_action", timeoutSeconds: 3);
        if (ok)
        {
            ShowToast("🛑 캐릭터의 진행 중인 행동(채집/이동 등)을 즉시 정지했습니다!", true);
        }
        else
        {
            ShowToast($"정지 실패: {err}", false);
        }
    }

    // ================= 10. 전자동 엑셀 (.xlsx) 관리 및 동기화 =================
    private void BtnOpenExcelSheet_Click(object sender, RoutedEventArgs e)
    {
        TxtExcelFilePath.Text = _excelSettingsManager.CurrentSettings.FilePath;
        ChkExcelAutoSyncOnCharChange.IsChecked = _excelSettingsManager.CurrentSettings.AutoSyncOnCharChange;
        UpdateExcelStatusUi();
        OverlayExcelManage.Visibility = Visibility.Visible;
    }

    private void BtnCloseExcelManage_Click(object sender, RoutedEventArgs e)
    {
        OverlayExcelManage.Visibility = Visibility.Collapsed;
    }

    private void BtnChangeExcelPath_Click(object sender, RoutedEventArgs e)
    {
        var currentPath = TxtExcelFilePath.Text.Trim();
        var initialDir = "";
        try
        {
            var dir = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                initialDir = dir;
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(initialDir))
        {
            initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "마비노기 모바일 엑셀 저장 위치 지정",
            Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx|모든 파일 (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(Path.GetFileName(currentPath)) ? "마비노기_모바일_캐릭터육성.xlsx" : Path.GetFileName(currentPath),
            InitialDirectory = initialDir
        };

        if (dlg.ShowDialog() == true)
        {
            TxtExcelFilePath.Text = dlg.FileName;
            _excelSettingsManager.CurrentSettings.FilePath = dlg.FileName;
            _excelSettingsManager.SaveSettings();
            _excelSyncService.SetTargetFilePath(dlg.FileName);
            ShowToast($"📁 엑셀 저장 위치가 변경되었습니다: {Path.GetFileName(dlg.FileName)}", true);
            UpdateExcelStatusUi();
        }
    }

    private void BtnResetExcelPathDefault_Click(object sender, RoutedEventArgs e)
    {
        var defaultPath = ExcelSheetSyncService.ResolveDefaultSavePath();
        TxtExcelFilePath.Text = defaultPath;
        _excelSettingsManager.CurrentSettings.FilePath = defaultPath;
        _excelSettingsManager.SaveSettings();
        _excelSyncService.SetTargetFilePath(defaultPath);
        ShowToast("📁 엑셀 저장 위치가 구글 드라이브 기본값으로 복원되었습니다.", true);
        UpdateExcelStatusUi();
    }

    private void BtnOpenExcelFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var filePath = TxtExcelFilePath.Text.Trim();
            if (File.Exists(filePath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
                }
                else
                {
                    ShowToast("저장 폴더 경로를 찾을 수 없습니다.", false);
                }
            }
        }
        catch (Exception ex)
        {
            ShowToast($"폴더 열기 오류: {ex.Message}", false);
        }
    }

    private void BtnSaveExcelSettings_Click(object sender, RoutedEventArgs e)
    {
        var newPath = TxtExcelFilePath.Text.Trim();
        if (string.IsNullOrWhiteSpace(newPath))
        {
            newPath = ExcelSheetSyncService.ResolveDefaultSavePath();
            TxtExcelFilePath.Text = newPath;
        }

        _excelSettingsManager.CurrentSettings.FilePath = newPath;
        _excelSettingsManager.CurrentSettings.AutoSyncOnCharChange = ChkExcelAutoSyncOnCharChange.IsChecked == true;
        _excelSettingsManager.SaveSettings();
        _excelSyncService.SetTargetFilePath(newPath);

        ShowToast("💾 엑셀 동기화 설정이 안전하게 저장되었습니다.", true);
    }

    private async void BtnSyncAndOpenExcel_Click(object sender, RoutedEventArgs e)
    {
        BtnSyncAndOpenExcel.IsEnabled = false;
        BtnSyncAndOpenExcel.Content = "⏳ 동기화 중...";
        TxtExcelStatus.Text = "현재 캐릭터 정보를 엑셀 파일에 갱신하는 중...";

        try
        {
            BtnSaveExcelSettings_Click(sender, e);
            var (ok, msg) = await ExecuteExcelSyncAsync();
            UpdateExcelStatusUi();

            if (ok)
            {
                ShowToast($"✓ {msg}", true);

                // 엑셀 프로그램 실행
                var filePath = _excelSettingsManager.CurrentSettings.FilePath;
                if (File.Exists(filePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                ShowToast($"⚠️ {msg}", false);
            }
        }
        finally
        {
            BtnSyncAndOpenExcel.IsEnabled = true;
            BtnSyncAndOpenExcel.Content = "⚡ 동기화 & 엑셀 열기";
        }
    }

    private async Task<(bool success, string message)> ExecuteExcelSyncAsync()
    {
        // 1. 최신 캐릭터 정보, 주간 미션, 재화, 퀘스트 병렬 조회
        try
        {
            var tInfo = _cli.RunJsonAsync<CharacterInfo>("get_my_info", timeoutSeconds: 5);
            var tWeekly = _cli.RunJsonAsync<List<MissionItem>>("get_weekly_missions", timeoutSeconds: 5);
            var tCurr = _cli.RunJsonAsync<List<CurrencyItem>>("get_currencies", timeoutSeconds: 5);
            var tQuests = _cli.RunJsonAsync<List<QuestItem>>("get_quests", timeoutSeconds: 5);
            await Task.WhenAll(tInfo, tWeekly, tCurr, tQuests);

            if ((await tInfo).data is { } ch) _lastCharInfo = ch;
            if ((await tWeekly).data is { } weekly) _lastWeeklyMissions = weekly;
            if ((await tCurr).data is { } curr) _lastCurrencies = curr;
            if ((await tQuests).data is { } quests) _lastQuests = quests;

            // 숙제 상태 최신 평가
            EvaluateHomeworkStatus();
            RefreshHomeworkUi();
        }
        catch { }

        var charInfo = _lastCharInfo;
        if (charInfo == null)
        {
            return (false, "게임이 실행 중이지 않거나 캐릭터 정보를 가져올 수 없습니다.");
        }

        string targetName = "빅클라우드";
        var realm = string.IsNullOrEmpty(charInfo.RealmName) ? "에린" : charInfo.RealmName;
        var job = string.IsNullOrEmpty(charInfo.JobName) ? "밀레시안" : charInfo.JobName;
        var profile = _snapshotManager.GetProfile(realm, job);
        if (profile != null && !string.IsNullOrWhiteSpace(profile.CustomName))
        {
            targetName = profile.CustomName;
        }
        else if (job.Contains("전사") || job.Contains("대검"))
        {
            targetName = "빅클라우드";
        }
        else if (job.Contains("듀얼") || job.Contains("블레이드"))
        {
            targetName = "빅콜라";
        }

        var (success, msg, row) = await Task.Run(() =>
            _excelSyncService.SyncCharacter(targetName, charInfo, _homeworkService.Repository));

        var settings = _excelSettingsManager.CurrentSettings;
        settings.LastSyncTime = DateTime.Now;
        settings.LastSyncCharacter = targetName;
        settings.LastSyncStatus = success ? $"[{targetName}] {row}행 갱신 완료" : msg;
        _excelSettingsManager.SaveSettings();

        return (success, success ? $"[{targetName}] 엑셀 동기화 완료! ({row}행)" : msg);
    }

    private void UpdateExcelStatusUi()
    {
        var settings = _excelSettingsManager.CurrentSettings;
        if (settings.LastSyncTime.HasValue)
        {
            var timeStr = settings.LastSyncTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
            var charName = settings.LastSyncCharacter ?? "미지정";
            var status = settings.LastSyncStatus ?? "상태 없음";
            TxtExcelStatus.Text = $"🕒 마지막 동기화: {timeStr} | 대상: {charName}\n상태: {status}\n파일: {settings.FilePath}";
        }
        else
        {
            TxtExcelStatus.Text = $"동기화 대기 중... (저장 위치: {settings.FilePath})\n[⚡ 동기화 & 엑셀 열기] 버튼을 누르면 즉시 동기화 후 엑셀이 열립니다.";
        }
    }

    private void CheckAndTriggerAutoGoogleSheetSync(string realm, string job)
    {
        var charKey = $"{realm}_{job}";
        if (_lastSyncedCharacterKey == charKey) return;
        _lastSyncedCharacterKey = charKey;

        var charInfo = _lastCharInfo;
        if (charInfo == null) return;

        var profile = _snapshotManager.GetProfile(realm, job);
        var targetName = profile?.CustomName;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            if (job.Contains("전사") || job.Contains("대검")) targetName = "빅클라우드";
            else if (job.Contains("듀얼") || job.Contains("블레이드")) targetName = "빅콜라";
            else targetName = _excelSettingsManager.CurrentSettings.LastSyncCharacter ?? "빅클라우드";
        }

        // 1. 전자동 엑셀 (구글 드라이브 동기화 폴더) 직접 갱신
        if (_excelSettingsManager.CurrentSettings.AutoSyncOnCharChange)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var (ok, msg) = await ExecuteExcelSyncAsync();
                    if (ok)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ShowToast($"📊 {msg} (구글 드라이브 반영)", true);
                        });
                    }
                }
                catch (Exception ex)
                {
                    App.LogTrace($"Auto excel sync error: {ex.Message}");
                }
            });
        }
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}