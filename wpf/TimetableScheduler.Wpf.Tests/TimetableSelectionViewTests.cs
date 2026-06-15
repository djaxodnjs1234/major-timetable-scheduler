using TimetableScheduler.Wpf.Views;

namespace TimetableScheduler.Wpf.Tests;

public class TimetableSelectionViewTests
{
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
}
