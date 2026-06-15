using System.Globalization;
using System.Reflection;
using System.Windows;
using TimetableScheduler.Solver;
using TimetableScheduler.Wpf.Converters;
using TimetableScheduler.Wpf.Services;

namespace TimetableScheduler.Wpf.Tests;

public class UserDisplayFormattingTests
{
    [Fact]
    public void SaveNamePlaceholderVisibility_UsesActualTextContent()
    {
        var converter = new StringNullOrWhiteSpaceToVisibilityConverter();

        Assert.Equal(Visibility.Visible, converter.Convert("", typeof(Visibility), string.Empty, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, converter.Convert(" ", typeof(Visibility), string.Empty, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, converter.Convert("A", typeof(Visibility), string.Empty, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, converter.Convert("가", typeof(Visibility), string.Empty, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConflictDialogLabels_DoNotExposeHcCodes()
    {
        var method = typeof(MessageBoxConflictDialogService)
            .GetMethod("KoreanLabel", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var label = Assert.IsType<string>(method!.Invoke(null, new object[] { ConflictType.FixedRoomViolation }));

        Assert.Equal("고정 강의실 위반", label);
        Assert.DoesNotContain("HC-", label);
    }
}
