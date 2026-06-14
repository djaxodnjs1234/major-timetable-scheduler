using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TimetableScheduler.Wpf.Converters;

public sealed class DensityToBrushConverter : IValueConverter
{
    // Low density: #EFE7FF (clean pastel lavender) -> High density: #8D63D8 (clear violet)
    private static readonly (byte R, byte G, byte B) Low  = (0xEF, 0xE7, 0xFF);
    private static readonly (byte R, byte G, byte B) High = (0x8D, 0x63, 0xD8);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var t = value is double d ? Math.Clamp(d, 0.0, 1.0) : 0.0;
        var r = (byte)(Low.R + (High.R - Low.R) * t);
        var g = (byte)(Low.G + (High.G - Low.G) * t);
        var b = (byte)(Low.B + (High.B - Low.B) * t);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
