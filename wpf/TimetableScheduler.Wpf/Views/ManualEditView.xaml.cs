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
        GridControl.EnableDragDropMove = true;
        GridControl.CellClicked += OnCellClicked;
        GridControl.CrossHoverEvaluator = EvaluateCrossHover;
        GridControl.SwapHoverEvaluator = EvaluateSwapHover;
        GridControl.CrossDropHoverEvaluator = EvaluateCrossDropHover;
        GridControl.SwapDropHoverEvaluator = EvaluateSwapDropHover;
        GridControl.DropMoveEvaluator = EvaluateDropMove;
        GridControl.CrossAddRequested += OnCrossAddRequested;
        GridControl.SwapRequested += OnSwapRequested;
        GridControl.CrossDropRequested += OnCrossDropRequested;
        GridControl.SwapDropRequested += OnSwapDropRequested;
        GridControl.DropMoveRequested += OnDropMoveRequested;
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

    private CrossHoverState EvaluateCrossDropHover(UnifiedTimetableControl.CellDropMoveEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            return vm.EvaluateCrossDropHover(
                e.Source.Day,
                e.Source.Period,
                e.Source.Grade,
                e.Source.SubColumnIdx,
                e.Source.Assignment,
                e.Target.Day,
                e.Target.Period,
                e.Target.Grade,
                e.Target.SubColumnIdx,
                e.Target.Assignment);
        return CrossHoverState.Hidden();
    }

    private SwapHoverState EvaluateSwapDropHover(UnifiedTimetableControl.CellDropMoveEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            return vm.EvaluateSwapDropHover(
                e.Source.Day,
                e.Source.Period,
                e.Source.Grade,
                e.Source.SubColumnIdx,
                e.Source.Assignment,
                e.Target.Day,
                e.Target.Period,
                e.Target.Grade,
                e.Target.SubColumnIdx,
                e.Target.Assignment);
        return SwapHoverState.Hidden();
    }

    private bool EvaluateDropMove(UnifiedTimetableControl.CellDropMoveEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            return vm.CanDropMove(
                e.Source.Day,
                e.Source.Period,
                e.Source.Grade,
                e.Source.SubColumnIdx,
                e.Source.Assignment,
                e.Target.Day,
                e.Target.Period,
                e.Target.Grade,
                e.Target.SubColumnIdx);
        return false;
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

    private void OnCrossDropRequested(object? sender, UnifiedTimetableControl.CellDropMoveEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            vm.HandleCrossDrop(
                e.Source.Day,
                e.Source.Period,
                e.Source.Grade,
                e.Source.SubColumnIdx,
                e.Source.Assignment,
                e.Target.Day,
                e.Target.Period,
                e.Target.Grade,
                e.Target.SubColumnIdx,
                e.Target.Assignment);
    }

    private void OnSwapDropRequested(object? sender, UnifiedTimetableControl.CellDropMoveEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            vm.HandleSwapDrop(
                e.Source.Day,
                e.Source.Period,
                e.Source.Grade,
                e.Source.SubColumnIdx,
                e.Source.Assignment,
                e.Target.Day,
                e.Target.Period,
                e.Target.Grade,
                e.Target.SubColumnIdx,
                e.Target.Assignment);
    }

    private void OnDropMoveRequested(object? sender, UnifiedTimetableControl.CellDropMoveEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            vm.HandleDropMove(
                e.Source.Day,
                e.Source.Period,
                e.Source.Grade,
                e.Source.SubColumnIdx,
                e.Source.Assignment,
                e.Target.Day,
                e.Target.Period,
                e.Target.Grade,
                e.Target.SubColumnIdx);
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
