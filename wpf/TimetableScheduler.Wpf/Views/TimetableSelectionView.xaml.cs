using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TimetableScheduler.Data;
using TimetableScheduler.ViewModel.Pages;

[assembly: InternalsVisibleTo("TimetableScheduler.Wpf.Tests")]

namespace TimetableScheduler.Wpf.Views;

public partial class TimetableSelectionView : UserControl
{
    internal static Action<string> OpenExportedFile { get; set; } = OpenExportedFileCore;
    internal static Action<Exception> ShowExportFailureMessage { get; set; } = ShowExportFailureMessageCore;
    internal static Action ShowAutoOpenFailureMessage { get; set; } = ShowAutoOpenFailureMessageCore;

    public TimetableSelectionView() => InitializeComponent();

    private TimetableSelectionViewModel? Vm => DataContext as TimetableSelectionViewModel;

    private void OnExportXlsxClick(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "시간표 xlsx 저장",
            FileName = $"{Vm.SelectedTimetable?.Name ?? "시간표"}.xlsx",
        };

        if (dlg.ShowDialog() != true)
            return;

        TryExportAndOpen(() => Vm.ExportXlsxCommand.Execute(dlg.FileName), dlg.FileName);
    }

    internal static bool TryExportAndOpen(Action export, string filePath)
    {
        try
        {
            export();
        }
        catch (Exception ex)
        {
            ShowExportFailureMessage(ex);
            return false;
        }

        return TryOpenExportedFile(filePath);
    }

    internal static bool TryOpenExportedFile(string filePath)
    {
        try
        {
            OpenExportedFile(filePath);
            return true;
        }
        catch
        {
            ShowAutoOpenFailureMessage();
            return false;
        }
    }

    internal static void ResetExportOpenHandlersForTests()
    {
        OpenExportedFile = OpenExportedFileCore;
        ShowExportFailureMessage = ShowExportFailureMessageCore;
        ShowAutoOpenFailureMessage = ShowAutoOpenFailureMessageCore;
    }

    private static void OpenExportedFileCore(string filePath) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true,
        });

    private static void ShowExportFailureMessageCore(Exception ex) =>
        MessageBox.Show(
            $"Excel 파일을 저장할 수 없습니다.\n{ex.Message}",
            "Excel 내보내기 실패",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

    private static void ShowAutoOpenFailureMessageCore() =>
        MessageBox.Show(
            "Excel 파일은 저장되었지만 자동으로 열 수 없습니다. 저장 위치에서 직접 열어 주세요.",
            "Excel 파일 열기 실패",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not SavedTimetableRecord record || Vm == null) return;
        var result = MessageBox.Show(
            $"'{record.Name}' 시간표를 삭제하시겠습니까?",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            Vm.DeleteTimetableCommand.Execute(record);
    }

}
