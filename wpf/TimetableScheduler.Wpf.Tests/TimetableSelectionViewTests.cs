using System.IO;
using TimetableScheduler.Wpf.Views;

namespace TimetableScheduler.Wpf.Tests;

public class TimetableSelectionViewTests
{
    [Fact]
    public void SavedTimetableList_TrimsLongNamesWithTooltipAndCompactRows()
    {
        var xaml = File.ReadAllText(FindTimetableSelectionViewXaml());
        var nameTextIndex = xaml.IndexOf("<TextBlock Text=\"{Binding Name}\" FontWeight=\"SemiBold\"", StringComparison.Ordinal);

        Assert.True(nameTextIndex >= 0);
        var nameTextSnippet = xaml[nameTextIndex..Math.Min(nameTextIndex + 420, xaml.Length)];

        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", xaml);
        Assert.Contains("<Border Padding=\"8,6\" BorderBrush=\"#EEE\"", xaml);
        Assert.Contains("<ColumnDefinition Width=\"*\" MinWidth=\"0\" />", xaml);
        Assert.Contains("<StackPanel Grid.Column=\"0\" MinWidth=\"0\"", xaml);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", nameTextSnippet);
        Assert.Contains("TextWrapping=\"NoWrap\"", nameTextSnippet);
        Assert.Contains("ToolTip=\"{Binding Name}\"", nameTextSnippet);
        Assert.Contains("FontSize=\"10\" Foreground=\"#888\" Margin=\"0,1,0,0\"", xaml);
        Assert.Contains("Padding=\"6,2\" Margin=\"0\"", xaml);
        Assert.Contains("Padding=\"5,2\" Margin=\"3,0,0,0\"", xaml);
        Assert.Equal(4, CountOccurrences(xaml, "PeriodRowMinHeight=\"52\""));
        Assert.DoesNotContain("LunchRowHeight", xaml);
        Assert.Equal(4, CountOccurrences(xaml, "NightSeparatorThickness=\"3\""));
        Assert.DoesNotContain("NightRowHeight", xaml);
        Assert.Contains("DayColumnWidth=\"68\"", xaml);
        Assert.DoesNotContain("LunchRowMinHeight", xaml);
        Assert.DoesNotContain("NightRowMinHeight", xaml);
        Assert.Equal(3, CountOccurrences(xaml, "DayColumnMinWidth=\"66\""));
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

    private static string FindTimetableSelectionViewXaml(
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
                var path = Path.Combine(dir, "wpf", "TimetableScheduler.Wpf", "Views", "TimetableSelectionView.xaml");
                if (File.Exists(path)) return path;

                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        throw new InvalidOperationException("TimetableSelectionView.xaml not found");
    }
}
