using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MobiMateHomeWork.ViewModels;

namespace MobiMateHomeWork;

/// <summary>설정 팝업(⚙): CLI 경로, 항상 위, 닫으면 트레이로. 값은 바로 적용·저장한다(확인 단추 없음).</summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private string _applied;

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _applied = vm.EffectiveCliPath;
        CliBox.Text = _applied;
        TopBox.IsChecked = vm.AlwaysOnTop;
        TrayBox.IsChecked = vm.CloseToTray;
        VersionText.Text = $"MobiMate Homework {Hosting.AppInfo.Version}";
        ShowCliState();
        BgOpacity.Value = Math.Round(vm.BackgroundOpacity * 100);
        BgOpacityText.Text = $"{BgOpacity.Value:0}%";
        BuildBackgroundThumbs();
        Images.ImageFiles.Changed += OnImagesChanged;
        Closed += (_, _) => Images.ImageFiles.Changed -= OnImagesChanged;
    }

    // ── 배경 그림 ─────────────────────────────────────────────

    private void OnImagesChanged() => Dispatcher.BeginInvoke(new Action(BuildBackgroundThumbs));   // 받는 중이던 그림이 도착하면 미리보기를 채운다

    /// <summary>공식 일러스트 6개의 작은 미리보기 단추. 고른 것은 강조 테두리. 아직 받지 못한 그림은 "받는 중".</summary>
    private void BuildBackgroundThumbs()
    {
        BgThumbs.Children.Clear();
        foreach (var bg in Images.BackgroundCatalog.Known)
        {
            var selected = _vm.BackgroundMode == Hosting.BackgroundSettings.Official && _vm.BackgroundId == bg.Id;
            var preview = Images.ImageFiles.Load(Images.ImageFiles.BackgroundPath(bg.Id), decodePixelWidth: 192);
            UIElement face = preview != null
                ? new System.Windows.Controls.Image { Source = preview, Stretch = System.Windows.Media.Stretch.UniformToFill, Width = 96, Height = 54 }
                : new TextBlock { Text = "받는 중", Style = (Style)FindResource("Text.Small"), Width = 96, Height = 54, TextAlignment = TextAlignment.Center, Padding = new Thickness(0, 18, 0, 0) };
            var button = new System.Windows.Controls.Button
            {
                Content = face, Padding = new Thickness(0), MinHeight = 0, Margin = new Thickness(0, 0, 6, 6),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = (System.Windows.Media.Brush)FindResource(selected ? "Brush.Accent" : "Brush.Line"),
                ToolTip = bg.Title,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, "배경 그림 " + bg.Title + (selected ? " (선택됨)" : ""));
            var id = bg.Id;
            button.Click += (_, _) => { _vm.SetBackground(Hosting.BackgroundSettings.Official, id); BuildBackgroundThumbs(); };
            BgThumbs.Children.Add(button);
        }
        ShowBackgroundState();
    }

    private void ShowBackgroundState()
    {
        var warn = (System.Windows.Media.Brush)FindResource("Brush.Warn");
        var muted = (System.Windows.Media.Brush)FindResource("Brush.TextMuted");
        string text;
        var useWarn = false;
        switch (_vm.BackgroundMode)
        {
            case Hosting.BackgroundSettings.Off:
                text = "배경 그림을 끄고 있습니다.";
                break;
            case Hosting.BackgroundSettings.Custom:
                var path = _vm.BackgroundImagePath;
                if (!string.IsNullOrWhiteSpace(path) && Images.ImageFiles.Load(path, decodePixelWidth: 64) != null) text = "내 그림: " + System.IO.Path.GetFileName(path);
                else { text = "내 그림을 읽지 못해 공식 기본 그림을 씁니다."; useWarn = true; }
                break;
            default:
                var file = Images.ImageFiles.BackgroundPath(_vm.BackgroundId);
                if (System.IO.File.Exists(file)) text = Images.BackgroundCatalog.Find(_vm.BackgroundId)?.Title ?? "";
                else { text = "이 그림을 받는 중입니다(인터넷 연결 필요). 받으면 바로 보입니다."; useWarn = true; }
                break;
        }
        BgState.Text = text;
        BgState.Foreground = useWarn ? warn : muted;
    }

    private void OnPickBackground(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "배경으로 쓸 그림 선택",
            Filter = "그림 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        _vm.SetBackground(Hosting.BackgroundSettings.Custom, customPath: dialog.FileName);
        BuildBackgroundThumbs();
    }

    private void OnBackgroundOff(object sender, RoutedEventArgs e)
    {
        _vm.SetBackground(Hosting.BackgroundSettings.Off);
        BuildBackgroundThumbs();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BgOpacityText == null) return;   // 초기화 중
        BgOpacityText.Text = $"{e.NewValue:0}%";
        _vm.SetBackgroundOpacity(e.NewValue / 100, save: false);   // 끄는 동안은 화면에만 적용하고 놓을 때 저장한다
    }

    private void OnOpacityCommit(object sender, EventArgs e) => _vm.SetBackgroundOpacity(BgOpacity.Value / 100);

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _vm.SetBackgroundOpacity(BgOpacity.Value / 100);   // 키보드·휠로 바꾼 값도 닫을 때 저장된다
        Close();
    }

    private void OnTopClick(object sender, RoutedEventArgs e) => _vm.SetAlwaysOnTop(TopBox.IsChecked == true);
    private void OnTrayClick(object sender, RoutedEventArgs e) => _vm.SetCloseToTray(TrayBox.IsChecked == true);

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "게임 CLI 실행 파일 선택",
            Filter = "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*",
            CheckFileExists = true,
        };
        try { if (!string.IsNullOrWhiteSpace(CliBox.Text)) dialog.InitialDirectory = System.IO.Path.GetDirectoryName(CliBox.Text); }
        catch (ArgumentException) { }
        if (dialog.ShowDialog(this) != true) return;
        CliBox.Text = dialog.FileName;
        _ = ApplyCliAsync();
    }

    private void OnCliKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = ApplyCliAsync();
    }

    private void OnCliLostFocus(object sender, RoutedEventArgs e) => _ = ApplyCliAsync();

    private async Task ApplyCliAsync()
    {
        var text = CliBox.Text.Trim();
        if (text == _applied) return;
        await _vm.SetCliPathAsync(text);
        _applied = _vm.EffectiveCliPath;
        if (string.IsNullOrWhiteSpace(text)) CliBox.Text = _applied;
        ShowCliState();
    }

    private void ShowCliState()
    {
        var exists = System.IO.File.Exists(_applied);
        CliState.Text = exists ? "" : "이 경로에 파일이 없습니다. 게임이 설치된 폴더의 MabinogiMobile_CLI.exe를 지정하세요.";
        CliState.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Warn");
    }
}
