using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.Wpf.Views;

public partial class TimetableSelectionView : UserControl
{
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
        if (dlg.ShowDialog() == true)
            Vm.ExportXlsxCommand.Execute(dlg.FileName);
    }
}
