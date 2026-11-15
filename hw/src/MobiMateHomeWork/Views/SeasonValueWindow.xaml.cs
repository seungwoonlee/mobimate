using System.Globalization;
using System.Windows;
using MobiMateHomeWork.ViewModels;

namespace MobiMateHomeWork.Views;

public partial class SeasonValueWindow : Window
{
    private readonly int _max;
    public int Value { get; private set; }
    public SeasonValueWindow(CellViewModel cell)
    {
        InitializeComponent();
        _max = cell.Id == "weekly_nightmare_drill" ? 30 : 99999;
        TitleText.Text = cell.Label;
        HintText.Text = $"0~{_max:N0}의 숫자를 입력하세요. 0이나 빈칸은 기록을 지웁니다.";
        ValueInput.Text = cell.Value is > 0 ? cell.Value.Value.ToString(CultureInfo.InvariantCulture) : "";
        Loaded += (_, _) => { ValueInput.Focus(); ValueInput.SelectAll(); };
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        var input = ValueInput.Text.Trim();
        if (input.Length == 0) Value = 0;
        else if (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || value > _max)
        {
            ErrorText.Text = $"0부터 {_max:N0}까지의 정수를 입력해 주세요.";
            return;
        }
        else Value = value;
        DialogResult = true;
    }
}
