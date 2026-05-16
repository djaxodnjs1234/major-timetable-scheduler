using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.Wpf.Controls;

namespace TimetableScheduler.Wpf.Views;

public partial class ManualEditView : UserControl
{
    public ManualEditView()
    {
        InitializeComponent();
        GridControl.CellClicked += OnCellClicked;
        Loaded += (_, _) => Focus();
    }

    private void OnCellClicked(object? sender, UnifiedTimetableControl.CellClickedEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            vm.HandleCellClick(e.Day, e.Period, e.Grade, e.SubColumnIdx, e.Assignment);
    }

    private void OnExportXlsxClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ManualEditViewModel vm) return;
        var dlg = new SaveFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "수동 편집 시간표 xlsx 저장",
            FileName = "수동편집결과.xlsx",
        };
        if (dlg.ShowDialog() == true)
            vm.ExportXlsxCommand.Execute(dlg.FileName);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is ManualEditViewModel vm)
        {
            vm.ClearSelectionCommand.Execute(null);
            e.Handled = true;
        }
    }
}
