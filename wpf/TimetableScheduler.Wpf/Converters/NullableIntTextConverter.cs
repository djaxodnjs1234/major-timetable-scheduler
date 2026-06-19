using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimetableScheduler.Wpf.Converters;

public sealed class NullableIntTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return Binding.DoNothing;

        return int.TryParse(text, NumberStyles.Integer, culture, out var parsed)
            ? parsed
            : Binding.DoNothing;
    }
}
