using System.Globalization;
using System.Windows.Data;

namespace TimetableScheduler.Wpf.Converters;

public sealed class IntListToCsvConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is IEnumerable<int> xs ? string.Join(",", xs) : "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return new List<int>();
        var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<int>();
        foreach (var p in parts)
            if (int.TryParse(p.Trim(), out int n)) result.Add(n);
        return result;
    }
}
