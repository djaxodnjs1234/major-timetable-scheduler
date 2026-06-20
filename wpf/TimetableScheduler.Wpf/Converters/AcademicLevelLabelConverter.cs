using System.Globalization;
using System.Windows.Data;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Wpf.Converters;

public sealed class AcademicLevelLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int grade)
            return AcademicLevels.DisplayName(grade);

        if (value is string text && AcademicLevels.TryParse(text, out grade))
            return AcademicLevels.DisplayName(grade);

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
