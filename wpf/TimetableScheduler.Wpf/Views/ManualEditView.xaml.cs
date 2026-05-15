using System.Windows.Controls;
using System.Windows.Input;
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
            vm.HandleCellClick(e.Day, e.Period, e.Assignment);
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
