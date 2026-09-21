using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    private CharacterInfo? _lastCharInfo;
    private List<CurrencyItem>? _lastCurrencies;
    private List<MissionItem>? _lastDailyMissions;

    private List<ItemData> _allItems = new();
    private string _currentItemLocationFilter = "All";

    private List<GatherableItem> _allGatherables = new();
    private List<AlteringWorkItem> _allAlteringWorks = new();

    public ObservableCollection<ChatLogEntry> GameChatLogs { get; } = new();
    public ObservableCollection<AiMessageEntry> AiMessages { get; } = new();

    private CancellationTokenSource? _tabCts;
    private DispatcherTimer? _searchDebounceTimer;
    private DispatcherTimer? _toastTimer;
    private bool _isWindowLoaded;

    public ICommand EmergencyStopCommand { get; }

    public MainWindow()
    {
        EmergencyStopCommand = new RelayCommand(async _ => await EmergencyStopInternalAsync());

        InitializeComponent();

        DataContext = this;
        ListGameChatLogs.ItemsSource = GameChatLogs;
        ListAiMessages.ItemsSource = AiMessages;

        AiMessages.Add(new AiMessageEntry("AI 도우미", "✨ 모비노기 AI도우미가 준비되었습니다. (ESC: 긴급 정지)", false));

        Loaded += async (s, e) =>
        {
            _isWindowLoaded = true;
            await LoadAiEnginesAsync();
            await RefreshHeaderOnlyAsync();
            await RefreshCurrentTabAsync();
        };
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

    private async void BtnRefreshCurrentTab_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCurrentTabAsync();
    }

    private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl)
        {
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

                case 3: // 미션 & 퀘스트
                    var tDaily = _cli.RunJsonAsync<List<MissionItem>>("get_daily_missions", timeoutSeconds: 4, ct: ct);
                    var tWeekly = _cli.RunJsonAsync<List<MissionItem>>("get_weekly_missions", timeoutSeconds: 4, ct: ct);
                    await Task.WhenAll(tDaily, tWeekly);
                    UpdateMissions((await tDaily).data, (await tWeekly).data);
                    break;

                case 4: // 생활 & 생산
                    var tAlter = _cli.RunJsonAsync<AlteringWorksResponse>("get_altering_works", timeoutSeconds: 4, ct: ct);
                    var tGather = _cli.RunJsonAsync<GatherableResponse>("get_gatherable_items", timeoutSeconds: 4, ct: ct);
                    await Task.WhenAll(tAlter, tGather);
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
        TxtCombatScore.Text = "⚔️ 전투력 -";
        TxtActivity.Text = "연결 끊김";
        TxtLocation.Text = "게임을 실행하고 MM AI 에이전트 설정을 켜주세요.";
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
            TxtCharTitle.Text = $"[{realm}] {job} Lv.{ch.Level}";

            if (ch.CombatScore != null)
            {
                TxtCombatScore.Text = $"⚔️ 전투력 {ch.CombatScore.Value:N0}";
                TxtScoreCombat.Text = $"{ch.CombatScore.Value:N0}";
            }
            if (ch.LivingScore != null) TxtScoreLiving.Text = $"{ch.LivingScore.Value:N0}";
            if (ch.AttractivenessScore != null) TxtScoreAttract.Text = $"{ch.AttractivenessScore.Value:N0}";
            if (ch.DecorScore != null) TxtScoreDecor.Text = $"{ch.DecorScore.Value:N0}";

            if (ch.HealthMax != null) TxtStatHp.Text = $"❤️ 최대 체력: {ch.HealthMax.Value:N0}";
            if (ch.AttackPower != null) TxtStatAtk.Text = $"⚔️ 공격력: {ch.AttackPower.Value:N0}";
            if (ch.DefencePower != null) TxtStatDef.Text = $"🛡️ 방어력: {ch.DefencePower.Value:N0}";
            if (ch.ArcaneResistance != null) TxtStatMdef.Text = $"🔮 마도 저항: {ch.ArcaneResistance.Value:N0}";
            if (ch.STR != null) TxtStatStr.Text = $"💪 힘 (STR): {ch.STR.Value:N0}";
            if (ch.DEX != null) TxtStatDex.Text = $"🎯 솜씨 (DEX): {ch.DEX.Value:N0}";
            if (ch.INT != null) TxtStatInt.Text = $"🧠 지력 (INT): {ch.INT.Value:N0}";
            if (ch.LUCK != null) TxtStatLuck.Text = $"🍀 행운 (LUCK): {ch.LUCK.Value:N0}";
            if (ch.WILL != null) TxtStatWill.Text = $"🔥 의지 (WILL): {ch.WILL.Value:N0}";

            if (ch.PaladinStats != null)
            {
                var p = ch.PaladinStats;
                if (p.PaladinAttackPower != null) TxtPalAtk.Text = $"신성력: {p.PaladinAttackPower.Value:N0}";
                if (p.PaladinDefencePower != null) TxtPalDef.Text = $"항마력: {p.PaladinDefencePower.Value:N0}";
                if (p.JusticePower != null) TxtPalJustice.Text = $"정의: {p.JusticePower.Value:N0}";
                if (p.JudgementPower != null) TxtPalJudge.Text = $"심판: {p.JudgementPower.Value:N0}";
                if (p.OrderPower != null) TxtPalOrder.Text = $"질서: {p.OrderPower.Value:N0}";
                if (p.BlessingPower != null) TxtPalBless.Text = $"가호: {p.BlessingPower.Value:N0}";
            }

            if (ch.Vitals != null) UpdateWeightUi(ch.Vitals);
        }

        if (act != null)
        {
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
            var loc = string.IsNullOrEmpty(env.ChannelName) ? "필드" : env.ChannelName;
            var weather = string.IsNullOrEmpty(env.Weather) ? "맑음" : env.Weather;
            var erinn = string.IsNullOrEmpty(env.ErinnNow) ? "" : $"  |  에린 시간 {env.ErinnNow}";
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

        if (pct >= 90)
        {
            ProgWeightTab.Foreground = (Brush)FindResource("AccentRed");
            TxtWeightStatus.Text = "🚨 가방이 거의 꽉 찼습니다! 아이템 정리가 시급합니다.";
            TxtWeightStatus.Foreground = (Brush)FindResource("AccentRed");
        }
        else if (pct >= 80)
        {
            ProgWeightTab.Foreground = (Brush)FindResource("AccentYellow");
            TxtWeightStatus.Text = "⚠️ 여유 공간이 부족합니다.";
            TxtWeightStatus.Foreground = (Brush)FindResource("AccentYellow");
        }
        else
        {
            ProgWeightTab.Foreground = (Brush)FindResource("AccentGreen");
            TxtWeightStatus.Text = "✓ 여유 공간이 충분합니다.";
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

    // ================= 2. 가방 & 아이템 탭 (디바운싱 지원) =================
    private void UpdateItems(List<ItemData>? items)
    {
        if (items == null) return;
        _allItems = items;
        FilterItems();
    }

    private void FilterItems()
    {
        if (_allItems == null) return;

        var query = TxtItemSearch.Text.Trim();
        IEnumerable<ItemData> filtered = _allItems;

        if (_currentItemLocationFilter != "All")
        {
            filtered = filtered.Where(i => i.Location.Equals(_currentItemLocationFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(i => i.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           i.CategoryName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        ListItemView.ItemsSource = list;
        TxtItemCountLabel.Text = $"표시: {list.Count}개 / 전체: {_allItems.Count}개";
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
    private void UpdateCurrencies(List<CurrencyItem>? list)
    {
        if (list == null) return;
        _lastCurrencies = list;
        ListAllCurrencies.ItemsSource = list;
        UpdateDeltas();
    }

    // ================= 4. 미션 & 퀘스트 탭 =================
    private void UpdateMissions(List<MissionItem>? daily, List<MissionItem>? weekly)
    {
        if (daily != null)
        {
            _lastDailyMissions = daily;
            var completed = daily.Count(d => d.IsCompleted);
            ProgDailyMissions.Value = daily.Count > 0 ? (double)completed / daily.Count * 100 : 0;
            ListDailyMissionsFull.ItemsSource = daily.OrderBy(d => d.IsCompleted).ToList();
        }

        if (weekly != null)
        {
            var completed = weekly.Count(w => w.IsCompleted);
            ProgWeeklyMissions.Value = weekly.Count > 0 ? (double)completed / weekly.Count * 100 : 0;
            ListWeeklyMissionsFull.ItemsSource = weekly.OrderBy(w => w.IsCompleted).ToList();
        }

        UpdateDeltas();
    }

    // ================= 5. 생활 & 생산 탭 (150종 전체 스크롤 지원) =================
    private void UpdateLifeAndCraft(AlteringWorksResponse? alter, GatherableResponse? gather)
    {
        if (alter != null && alter.Works != null)
        {
            _allAlteringWorks = alter.Works;
            ListAlteringWorks.ItemsSource = _allAlteringWorks;
        }

        if (gather != null && gather.Items != null)
        {
            _allGatherables = gather.Items;
            FilterGatherablesFull();
        }
    }

    private void FilterGatherablesFull()
    {
        if (_allGatherables == null) return;
        var q = TxtGatherSearchFull.Text.Trim();

        IEnumerable<GatherableItem> filtered = _allGatherables;
        if (!string.IsNullOrEmpty(q))
        {
            filtered = filtered.Where(g => g.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // 전체 150종 스크롤 브라우징 (가상화 리스트뷰)
        ListGatherablesView.ItemsSource = filtered.ToList();
    }

    private void TxtGatherSearchFull_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterGatherablesFull();
    }

    private async void ChipGather_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string name)
        {
            await StartGatherAsync(name);
        }
    }

    private async void BtnStartGather_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string name)
        {
            await StartGatherAsync(name);
        }
    }

    private async Task StartGatherAsync(string itemName)
    {
        ShowToast($"'{itemName}' 채집 이동 및 작업을 요청했습니다...", true);

        var body = $"{{\"displayName\":\"{itemName}\",\"count\":5}}";
        var (ok, _, err) = await _cli.RunRawAsync("execute_gathering", stdinJson: body, timeoutSeconds: 6);
        if (ok)
        {
            ShowToast($"'{itemName}' 채집을 성공적으로 시작했습니다!", true);
        }
        else
        {
            ShowToast($"채집 요청 실패: {err}", false);
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

        var body = $"{{\"displayName\":\"{completed.DisplayName}\"}}";
        var (ok, _, err) = await _cli.RunRawAsync("complete_altering_work", stdinJson: body, timeoutSeconds: 6);
        if (ok)
        {
            ShowToast($"'{completed.DisplayName}' 가공물을 수거했습니다!", true);
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
        ListNearPcsView.ItemsSource = pcs.OrderBy(p => p.Distance).ToList();
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

    // ================= 8. 하단 AI 코파일럿 대화 (다중 엔진 자동 감지 및 폴백) =================
    private async Task LoadAiEnginesAsync()
    {
        CmbAiEngine.Items.Clear();
        var engines = await _aiManager.DiscoverEnginesAsync();

        foreach (var engine in engines)
        {
            CmbAiEngine.Items.Add(new ComboBoxItem
            {
                Content = engine.Info.DisplayName,
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

        // 내장 가이드 여부에 따라 퀵 칩 바 가시성 초기화
        var isBuiltIn = current?.Info.Type == AiEngineType.BuiltInGuide;
        ScrollQuickChips.Visibility = isBuiltIn ? Visibility.Visible : Visibility.Collapsed;

        // 상태 안내 토스트
        var hasOllama = engines.Any(e => e.Info.Type == AiEngineType.Ollama);
        if (hasOllama)
        {
            ShowToast($"💡 무료 로컬 AI(Ollama) 감지 완료 (엔진 {engines.Count}개 준비)", true);
        }
        else
        {
            ShowToast("💡 AI 미설치 PC 감지: 완전 무료 내장 마비 가이드로 자동 동작합니다. (0원)", true);
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

            // 내장 가이드일 때만 5대 퀵 공략 칩 바 노출
            var isBuiltIn = engine?.Info.Type == AiEngineType.BuiltInGuide;
            ScrollQuickChips.Visibility = isBuiltIn ? Visibility.Visible : Visibility.Collapsed;

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

    private void BtnQuickChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            var prompt = tag switch
            {
                "전투력" => "전투력 및 룬 장착 공략 알려줘",
                "골드" => "골드 및 재화 파밍 팁 알려줘",
                "채집" => "주요 채집물 위치와 가공 시설 공략 알려줘",
                "던전" => "던전 보스 브레이크 및 회피 공략 알려줘",
                "진단" => "현재 내 캐릭터 스펙과 상태 진단해줘",
                _ => btn.Content?.ToString() ?? "공략 가이드"
            };

            ExecuteAiQueryDirect(prompt);
        }
    }

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

        // 긴급 정지 키워드 즉각 처리
        if (query.Contains("정지") || query.Contains("멈춰"))
        {
            _ = EmergencyStopInternalAsync();
            AiMessages.Add(new AiMessageEntry(displayModelName, "🛑 캐릭터의 진행 중인 행동(채집, 이동 등)을 즉시 긴급 정지시켰습니다!", false));
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