using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimetableScheduler.Wpf.Converters;

public sealed class StringNullOrWhiteSpaceToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
