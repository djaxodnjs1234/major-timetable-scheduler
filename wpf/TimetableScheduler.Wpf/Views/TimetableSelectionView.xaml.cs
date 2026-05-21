using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TimetableScheduler.Data;
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
