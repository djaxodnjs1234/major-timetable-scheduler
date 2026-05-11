using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TimetableScheduler.Wpf.Converters;

public sealed class OccupiedToBgConverter : IValueConverter
{
    private static readonly Brush Occupied = new SolidColorBrush(Color.FromRgb(220, 235, 255));
    private static readonly Brush Empty = Brushes.White;

    static OccupiedToBgConverter()
    {
        Occupied.Freeze();
    }

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Occupied : Empty;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
