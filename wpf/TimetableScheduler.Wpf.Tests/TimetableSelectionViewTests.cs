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
        Assert.Contains("LunchRowHeight=\"18\"", xaml);
        Assert.Equal(4, CountOccurrences(xaml, "NightSeparatorThickness=\"3\""));
        Assert.DoesNotContain("NightRowHeight", xaml);
        Assert.Contains("DayColumnWidth=\"68\"", xaml);
        Assert.Equal(3, CountOccurrences(xaml, "LunchRowMinHeight=\"24\""));
        Assert.DoesNotContain("NightRowMinHeight", xaml);
        Assert.Equal(3, CountOccurrences(xaml, "DayColumnMinWidth=\"66\""));
    }

    [Fact]
    public void TryExportAndOpen_OpensFileAfterSuccessfulExport()
    {
        var exported = false;
        var openedPath = "";
        var exportFailures = 0;
        var openFailures = 0;
        Configure(
            open: path => openedPath = path,
            exportFailure: _ => exportFailures++,
            openFailure: () => openFailures++);

        try
        {
            var result = TimetableSelectionView.TryExportAndOpen(
                () => exported = true,
                "C:\\temp\\timetable.xlsx");

            Assert.True(result);
            Assert.True(exported);
            Assert.Equal("C:\\temp\\timetable.xlsx", openedPath);
            Assert.Equal(0, exportFailures);
            Assert.Equal(0, openFailures);
        }
        finally
        {
            Reset();
        }
    }

    [Fact]
    public void TryExportAndOpen_DoesNotOpenFileWhenExportFails()
    {
        var opened = false;
        var exportFailures = 0;
        Configure(
            open: _ => opened = true,
            exportFailure: _ => exportFailures++,
            openFailure: () => { });

        try
        {
            var result = TimetableSelectionView.TryExportAndOpen(
                () => throw new InvalidOperationException("export failed"),
                "C:\\temp\\timetable.xlsx");

            Assert.False(result);
            Assert.False(opened);
            Assert.Equal(1, exportFailures);
        }
        finally
        {
            Reset();
        }
    }

    [Fact]
    public void TryOpenExportedFile_ShowsMessageAndDoesNotThrowWhenOpenFails()
    {
        var messageCount = 0;
        Configure(
            open: _ => throw new InvalidOperationException("open failed"),
            exportFailure: _ => { },
            openFailure: () => messageCount++);

        try
        {
            var result = TimetableSelectionView.TryOpenExportedFile("C:\\temp\\timetable.xlsx");

            Assert.False(result);
            Assert.Equal(1, messageCount);
        }
        finally
        {
            Reset();
        }
    }

    private static void Configure(Action<string> open, Action<Exception> exportFailure, Action openFailure)
    {
        TimetableSelectionView.OpenExportedFile = open;
        TimetableSelectionView.ShowExportFailureMessage = exportFailure;
        TimetableSelectionView.ShowAutoOpenFailureMessage = openFailure;
    }

    private static void Reset()
    {
        TimetableSelectionView.ResetExportOpenHandlersForTests();
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
