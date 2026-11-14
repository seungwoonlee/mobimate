using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MobiMateHomeWork.Views;

/// <summary>참이면 숨김, 거짓이면 보임 (BooleanToVisibilityConverter의 반대).</summary>
public sealed class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>이름의 마지막 낱말 첫 글자: "은동전" → "은", "마족 공물" → "공". 좁은 줄의 막대 이름표용.</summary>
public sealed class LastWordInitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var words = (value as string ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? "" : words[^1][..1];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
