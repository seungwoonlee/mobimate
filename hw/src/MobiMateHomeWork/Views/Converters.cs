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
