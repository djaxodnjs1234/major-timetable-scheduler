using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TimetableScheduler.Wpf.Converters;

public sealed class GradeToBrushConverter : IValueConverter
{
    private static readonly Dictionary<int, Brush> _brushes;

    static GradeToBrushConverter()
    {
        _brushes = new Dictionary<int, Brush>
        {
            [1] = MakeBrush(0xFF, 0xF9, 0xC4),
            [2] = MakeBrush(0xDC, 0xED, 0xC8),
            [3] = MakeBrush(0xBB, 0xDE, 0xFB),
            [4] = MakeBrush(0xFF, 0xCD, 0xD2),
        };
    }

    public static Brush BrushFor(int grade) =>
        _brushes.TryGetValue(grade, out var b) ? b : Brushes.White;

    private static Brush MakeBrush(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => TryNormalizeGrade(value, out var grade) ? BrushFor(grade) : (object)Brushes.White;

    private static bool TryNormalizeGrade(object value, out int grade)
    {
        switch (value)
        {
            case int g:
                grade = g;
                return true;
            case string text:
                var digits = new string(text.Where(char.IsDigit).ToArray());
                return int.TryParse(digits, out grade);
            default:
                grade = 0;
                return false;
        }
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
