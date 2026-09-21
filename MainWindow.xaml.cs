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

namespace MabiMate;

public partial class MainWindow : Window
{
    private readonly GameCliService _cli = new();

    private List<ItemData> _allItems = new();
    private string _currentItemLocationFilter = "All";

    private List<GatherableItem> _allGatherables = new();
    private List<AlteringWorkItem> _allAlteringWorks = new();

    public ObservableCollection<ChatLogEntry> GameChatLogs { get; } = new();
    public ObservableCollection<AiMessageEntry> AiMessages { get; } = new();

    private CancellationTokenSource? _tabCts;
    private DispatcherTimer? _searchDebounceTimer;
    private DispatcherTimer? _toastTimer;

    public ICommand EmergencyStopCommand { get; }

    public MainWindow()
    {
        EmergencyStopCommand = new RelayCommand(async _ => await EmergencyStopInternalAsync());

        InitializeComponent();

        DataContext = this;
        ListGameChatLogs.ItemsSource = GameChatLogs;
        ListAiMessages.ItemsSource = AiMessages;

        AiMessages.Add(new AiMessageEntry("AI 코파일럿", "안녕하세요, 밀레시안님! ✨\n상단 6대 정보 탭과 하단 인게임 채팅/AI 대화창이 준비되었습니다.\nESC 키를 누르면 언제든 즉시 캐릭터의 행동을 긴급 정지할 수 있습니다.", false));

        Loaded += async (s, e) =>
        {
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
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchDebounceTimer.Tick += (s, ev) =>
        {
            _searchDebounceTimer.Stop();
            FilterItems();
        };
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
        ListAllCurrencies.ItemsSource = list;
    }

    // ================= 4. 미션 & 퀘스트 탭 =================
    private void UpdateMissions(List<MissionItem>? daily, List<MissionItem>? weekly)
    {
        if (daily != null)
        {
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

    // ================= 7. 하단 인게임 전체 채팅 =================
    private void TxtGameChatInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var len = TxtGameChatInput.Text.Length;
        TxtCharLimit.Text = $"{len} / 50자";
        TxtCharLimit.Foreground = len >= 45 ? (Brush)FindResource("AccentRed") : (Brush)FindResource("TextSecondary");
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
        var text = TxtGameChatInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        TxtGameChatInput.Clear();

        var time = DateTime.Now.ToString("HH:mm:ss");
        var (ok, err) = await _cli.SendGameChatAsync(text);

        if (ok)
        {
            GameChatLogs.Add(new ChatLogEntry($"[{time}] ✓ 전송 완료", text, true));
            ShowToast($"채팅 전송 완료: \"{text}\"", true);
        }
        else
        {
            GameChatLogs.Add(new ChatLogEntry($"[{time}] ✗ 전송 실패 ({err})", text, false, err));
            ShowToast($"채팅 전송 실패: {err}", false);
        }

        ScrollGameChatLog.ScrollToBottom();
    }

    // ================= 8. 하단 AI 코파일럿 대화 =================
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
        var query = TxtAiInput.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        TxtAiInput.Clear();
        AiMessages.Add(new AiMessageEntry("나", query, true));

        string reply;
        var q = query.ToLower();

        if (q.Contains("가방") || q.Contains("아이템") || q.Contains("무게"))
            reply = "가방 및 소지품 600여 종은 상단의 [🎒 가방 & 소지품] 탭에서 위치별(가방/창고)로 검색하실 수 있습니다!";
        else if (q.Contains("돈") || q.Contains("골드") || q.Contains("재화"))
            reply = "골드와 정령의 날개 등 28종 전체 재화는 상단의 [💰 재화 & 화폐] 탭에 상세히 정리되어 있습니다.";
        else if (q.Contains("미션") || q.Contains("일퀘"))
            reply = "일일 미션과 주간 미션 진행 현황은 상단의 [📜 미션 & 퀘스트] 탭에서 확인하세요.";
        else if (q.Contains("채집") || q.Contains("가공") || q.Contains("수거"))
            reply = "작업대 남은 시간과 150종 채집 목록은 상단의 [🌿 생활 & 생산] 탭에서 바로 조작하실 수 있습니다.";
        else if (q.Contains("주변") || q.Contains("길드원") || q.Contains("유저"))
            reply = "근처 22명 플레이어와 길드원 위치는 상단의 [👥 주변 레이더] 탭에서 거리순으로 확인하실 수 있습니다.";
        else if (q.Contains("정지") || q.Contains("멈춰"))
        {
            _ = EmergencyStopInternalAsync();
            reply = "캐릭터의 진행 중인 행동(채집, 이동 등)을 즉시 정지시켰습니다!";
        }
        else
            reply = $"'{query}'에 대해 알려드릴게요!\n상단 6개 정보 탭과 좌측 인게임 채팅을 자유롭게 활용해 보세요.";

        AiMessages.Add(new AiMessageEntry("AI 코파일럿", reply, false));
        ScrollAiFeed.ScrollToBottom();
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