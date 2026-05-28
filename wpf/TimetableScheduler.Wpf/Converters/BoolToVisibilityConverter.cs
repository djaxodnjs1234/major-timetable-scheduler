using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimetableScheduler.Wpf.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool inverse = string.Equals(parameter as string, "Inverse", StringComparison.Ordinal);
        if (value is bool b)
            return (b ^ inverse) ? Visibility.Visible : Visibility.Collapsed;
        if (value is int i && string.Equals(parameter as string, "Zero", StringComparison.Ordinal))
            return i == 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}
