using System.Windows.Controls;
using System.Windows.Input;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.Wpf.Controls;

namespace TimetableScheduler.Wpf.Views;

public partial class ManualEditView : UserControl
{
    public ManualEditView()
    {
        InitializeComponent();
        GridControl.EnableCrossHover = true;
        GridControl.EnableSwapHover = true;
        GridControl.CellClicked += OnCellClicked;
        GridControl.CrossHoverEvaluator = EvaluateCrossHover;
        GridControl.SwapHoverEvaluator = EvaluateSwapHover;
        GridControl.CrossAddRequested += OnCrossAddRequested;
        GridControl.SwapRequested += OnSwapRequested;
        Loaded += (_, _) => Focus();
    }

    private void OnCellClicked(object? sender, UnifiedTimetableControl.CellClickedEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            vm.HandleCellClick(e.Day, e.Period, e.Grade, e.SubColumnIdx, e.Assignment);
    }

    private CrossHoverState EvaluateCrossHover(UnifiedTimetableControl.CellClickedEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            return vm.EvaluateCrossHover(e.Day, e.Period, e.Grade, e.SubColumnIdx, e.Assignment);
        return CrossHoverState.Hidden();
    }

    private SwapHoverState EvaluateSwapHover(UnifiedTimetableControl.CellClickedEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            return vm.EvaluateSwapHover(e.Day, e.Period, e.Grade, e.SubColumnIdx, e.Assignment);
        return SwapHoverState.Hidden();
    }

    private void OnCrossAddRequested(object? sender, UnifiedTimetableControl.CellClickedEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm && e.Assignment != null)
            vm.HandleCrossAddRequested(e.Day, e.Period, e.Grade, e.SubColumnIdx, e.Assignment);
    }

    private void OnSwapRequested(object? sender, UnifiedTimetableControl.CellClickedEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm && e.Assignment != null)
            vm.HandleSwapRequested(e.Day, e.Period, e.Grade, e.SubColumnIdx, e.Assignment);
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
