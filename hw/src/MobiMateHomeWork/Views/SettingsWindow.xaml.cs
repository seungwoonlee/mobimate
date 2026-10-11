using System.Windows;
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
        ShowCliState();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

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
