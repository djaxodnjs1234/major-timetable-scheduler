using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using TimetableScheduler.Wpf.Controls;
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

    [Fact]
    public void TimetableClassCards_ConstrainOverflowingFieldsWithEllipsis()
    {
        var source = File.ReadAllText(FindUnifiedTimetableControlSource());

        Assert.Contains("FontSize = 11,", source);
        Assert.Equal(2, CountOccurrences(source, "FontSize = 7.5,"));
        Assert.Contains("MaxCardTitleLinesPerBlock = 2", source);
        Assert.Contains("MaxCardProfessorLinesPerBlock = 2", source);
        Assert.Contains("MaxCardRoomLinesPerBlock = 1", source);
        Assert.Contains("MaxHeight = CardTitleLineHeight * titleLines", source);
        Assert.Contains("MaxHeight = CardMetaLineHeight * professorLines", source);
        Assert.Contains("MaxHeight = CardMetaLineHeight * roomLines", source);
        Assert.Contains("LineHeight = CardTitleLineHeight", source);
        Assert.Contains("TextWrapping = roomLines == 1 ? TextWrapping.NoWrap : TextWrapping.Wrap", source);
        Assert.True(CountOccurrences(source, "TextTrimming = TextTrimming.CharacterEllipsis") >= 3);
        Assert.Contains("Text = nameText,", source);
        Assert.Contains("Text = a.ProfessorLine,", source);
        Assert.Contains("Text = FormatRoomsForCard(a.RoomsLabel),", source);
        Assert.DoesNotContain("a.RoomsLabel.Replace(\"\\n\", \", \")", source);
    }

    [Fact]
    public void TimetableClassCards_ScaleTextBudgetsWithRowSpan()
    {
        Assert.Equal(2, UnifiedTimetableControl.CardTitleLinesFor(1));
        Assert.Equal(2, UnifiedTimetableControl.CardProfessorLinesFor(1));
        Assert.Equal(1, UnifiedTimetableControl.CardRoomLinesFor(1));

        Assert.Equal(4, UnifiedTimetableControl.CardTitleLinesFor(2));
        Assert.Equal(4, UnifiedTimetableControl.CardProfessorLinesFor(2));
        Assert.Equal(2, UnifiedTimetableControl.CardRoomLinesFor(2));
    }

    [Fact]
    public void TimetableClassCards_JoinRoomLinesWithCommas()
    {
        Assert.Equal(
            "R1, R2",
            UnifiedTimetableControl.FormatRoomsForCard("R1\nR2"));
        Assert.Equal(
            "R1, R2, R3",
            UnifiedTimetableControl.FormatRoomsForCard("R1\nR2\nR3"));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindUnifiedTimetableControlSource(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var startDirs = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(sourceFile) ?? ""
        };

        foreach (var startDir in startDirs)
        {
            var dir = startDir;
            while (!string.IsNullOrEmpty(dir))
            {
                var path = Path.Combine(dir, "wpf", "TimetableScheduler.Wpf", "Controls", "UnifiedTimetableControl.xaml.cs");
                if (File.Exists(path)) return path;

                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        throw new InvalidOperationException("UnifiedTimetableControl.xaml.cs not found");
    }
}
