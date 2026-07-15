using System.IO;
using System.Runtime.CompilerServices;
using TimetableScheduler.Wpf.Controls;

namespace TimetableScheduler.Wpf.Tests;

public class TimetableZoomTests
{
    [Fact]
    public void Zoom_ChangesByTenPercentAndRespectsBounds()
    {
        var zoom = new TimetableZoom();

        zoom.ZoomOut();
        Assert.Equal(0.9, zoom.Scale);
        Assert.Equal("90%", zoom.DisplayPercent);

        for (var i = 0; i < 10; i++) zoom.ZoomOut();
        Assert.Equal(TimetableZoom.MinimumScale, zoom.Scale);

        for (var i = 0; i < 20; i++) zoom.ZoomIn();
        Assert.Equal(TimetableZoom.MaximumScale, zoom.Scale);

        zoom.Reset();
        Assert.Equal(1.0, zoom.Scale);
        Assert.Equal("100%", zoom.DisplayPercent);
    }

    [Fact]
    public void SharedZoom_UsesOneInstanceAcrossScreens()
    {
        TimetableZoom.Shared.Reset();

        var firstScreenZoom = TimetableZoom.Shared;
        var secondScreenZoom = TimetableZoom.Shared;

        firstScreenZoom.ZoomIn();

        Assert.Same(firstScreenZoom, secondScreenZoom);
        Assert.Equal(1.1, secondScreenZoom.Scale);

        TimetableZoom.Shared.Reset();
    }

    [Fact]
    public void ManualEditUnifiedTimetable_BindsConnectorScaleToZoom()
    {
        var xaml = File.ReadAllText(FindWpfSource("Views", "ManualEditView.xaml"));

        Assert.Contains("ConnectorScale=\"{Binding Zoom.Scale", xaml);
    }

    [Fact]
    public void ConflictConnectors_RedrawAndCompensateStrokeForZoom()
    {
        var source = File.ReadAllText(FindWpfSource("Controls", "UnifiedTimetableControl.xaml.cs"));

        Assert.Contains("ConnectorScaleProperty", source);
        Assert.Contains("QueueConflictConnectorRedraw(force: true)", source);
        Assert.Contains("1.25 / Math.Clamp(ConnectorScale", source);
        Assert.Contains("StrokeStartLineCap = PenLineCap.Round", source);
        Assert.Contains("LayoutUpdated += OnConflictConnectorLayoutUpdated", source);
    }

    [Fact]
    public void ManualEditBlockHours_UsesDropdown()
    {
        var xaml = File.ReadAllText(FindWpfSource("Views", "ManualEditView.xaml"));
        var bindingIndex = xaml.IndexOf("SelectedItem=\"{Binding NewBlockRowSpan", StringComparison.Ordinal);

        Assert.True(bindingIndex >= 0);

        var snippetStart = Math.Max(0, bindingIndex - 180);
        var snippetLength = Math.Min(360, xaml.Length - snippetStart);
        var snippet = xaml.Substring(snippetStart, snippetLength);
        Assert.Contains("<ComboBox ItemsSource=\"{Binding ManualBlockWeeklyHourOptions}\"", snippet);
        Assert.DoesNotContain("<TextBox Text=\"{Binding NewBlockRowSpan", snippet);
    }

    [Fact]
    public void ManualEditConstraintPanel_DoesNotUseWarningYellow()
    {
        var xaml = File.ReadAllText(FindWpfSource("Views", "ManualEditView.xaml"));
        var panelStart = xaml.IndexOf("ItemsSource=\"{Binding ConflictGroups}\"", StringComparison.Ordinal);

        Assert.True(panelStart >= 0);

        var panelEnd = xaml.IndexOf("Visibility=\"{Binding HasStagedBlocks", panelStart, StringComparison.Ordinal);
        Assert.True(panelEnd > panelStart);

        var panelXaml = xaml[panelStart..panelEnd];
        Assert.DoesNotContain("Value=\"Warning\"", panelXaml);
        Assert.DoesNotContain("#B7791F", panelXaml);
        Assert.DoesNotContain("#FFF8E1", panelXaml);
    }

    [Fact]
    public void ManualEditConstraintPanel_UsesNeutralCourseTextColors()
    {
        var xaml = File.ReadAllText(FindWpfSource("Views", "ManualEditView.xaml"));
        var panelStart = xaml.IndexOf("ItemsSource=\"{Binding ConflictGroups}\"", StringComparison.Ordinal);

        Assert.True(panelStart >= 0);

        var panelEnd = xaml.IndexOf("Visibility=\"{Binding HasStagedBlocks", panelStart, StringComparison.Ordinal);
        Assert.True(panelEnd > panelStart);

        var panelXaml = xaml[panelStart..panelEnd];
        Assert.DoesNotContain("Foreground=\"{Binding Grade, Converter={StaticResource GradeToBrush}}\"", panelXaml);
        Assert.Contains("Value=\"#111827\"", panelXaml);
        Assert.Contains("Value=\"관련 수업:\"", panelXaml);
        Assert.Contains("Value=\"#6B7280\"", panelXaml);
    }

    [Fact]
    public void ValidationCheckRows_BindTooltip()
    {
        var source = File.ReadAllText(FindWpfSource("Services", "MessageBoxConflictDialogService.cs"));

        Assert.Contains("ToolTip = string.IsNullOrWhiteSpace(check.Tooltip) ? null : check.Tooltip", source);
    }

    [Fact]
    public void ValidationCheckRows_ShowInlineCountAndLocationFirstDetails()
    {
        var source = File.ReadAllText(FindWpfSource("Services", "MessageBoxConflictDialogService.cs"));

        Assert.Contains("Text = check.CountText", source);
        Assert.Contains("BuildConflictBlock(conflict, null, locationFirst: true)", source);
        Assert.Contains("AddLine(\"위치:\"", source);
        Assert.Contains("AddLine(\"상세:\"", source);
        Assert.DoesNotContain("AddCourseLine(\"충돌 항목:\"", source);
    }

    [Fact]
    public void ValidationCheckRows_FixedTimeReasonShowsOriginalPosition()
    {
        var source = File.ReadAllText(FindWpfSource("Services", "MessageBoxConflictDialogService.cs"));

        Assert.Contains("AddLine(\"사유:\", BuildReasonLabel(conflict)", source);
        Assert.Contains("원래 고정위치:", source);
        Assert.Contains("FormatFixedPositionPeriodRange", source);
        Assert.Contains("$\"{startPeriod}시\"", source);
    }

    private static string FindWpfSource(
        string folder,
        string fileName,
        [CallerFilePath] string sourceFile = "")
    {
        var startDirs = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(sourceFile) ?? "",
        };

        foreach (var startDir in startDirs)
        {
            var dir = startDir;
            while (!string.IsNullOrEmpty(dir))
            {
                var path = Path.Combine(dir, "wpf", "TimetableScheduler.Wpf", folder, fileName);
                if (File.Exists(path)) return path;

                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        throw new InvalidOperationException($"{fileName} not found");
    }
}
