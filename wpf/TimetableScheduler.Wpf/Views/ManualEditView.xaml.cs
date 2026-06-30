using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.Wpf.Controls;

[assembly: InternalsVisibleTo("TimetableScheduler.Wpf.Tests")]

namespace TimetableScheduler.Wpf.Views;

public partial class ManualEditView : UserControl
{
    private enum ExportUnsavedChoice
    {
        SaveThenExport,
        ExportWithoutSaving,
        Cancel,
    }

    internal static Action<string> OpenExportedFile { get; set; } = OpenExportedFileCore;
    internal static Action<Exception> ShowExportFailureMessage { get; set; } = ShowExportFailureMessageCore;
    internal static Action ShowAutoOpenFailureMessage { get; set; } = ShowAutoOpenFailureMessageCore;

    public TimetableZoom Zoom { get; } = new();

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
        GridControl.DragMovePreviewStarted += OnDragMovePreviewStarted;
        GridControl.DragMovePreviewEnded += OnDragMovePreviewEnded;
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => Focus();
    }

    private ManualEditViewModel? _subscribedViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel != null)
            _subscribedViewModel.ConflictFocusRequested -= OnConflictFocusRequested;

        _subscribedViewModel = e.NewValue as ManualEditViewModel;
        if (_subscribedViewModel != null)
            _subscribedViewModel.ConflictFocusRequested += OnConflictFocusRequested;
    }

    private void OnConflictFocusRequested(object? sender, ConflictFocusRequestedEventArgs e)
    {
        GridControl.Dispatcher.BeginInvoke(new Action(() =>
        {
            var target = FindTaggedCell(GridControl, e.Day, e.Period, e.Grade, e.SubColumnIdx);
            if (target == null)
                return;

            target.BringIntoView();
            FlashElement(target, 0.45);
            foreach (var related in e.RelatedTargets)
            {
                var relatedTarget = FindTaggedCell(
                    GridControl,
                    related.Day,
                    related.Period,
                    related.Grade,
                    related.SubColumnIdx);
                if (relatedTarget != null && !ReferenceEquals(relatedTarget, target))
                    FlashElement(relatedTarget, 0.7);
            }
        }));
    }

    private static void FlashElement(FrameworkElement element, double highlightOpacity)
    {
        var originalOpacity = element.Opacity;
        element.Opacity = highlightOpacity;
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            element.Opacity = originalOpacity;
        };
        timer.Start();
    }

    private void OnCellClicked(object? sender, UnifiedTimetableControl.CellClickedEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            vm.HandleCellClick(e.Day, e.Period, e.Grade, e.SubColumnIdx, e.Assignment);
    }

    private void OnZoomOutClick(object sender, System.Windows.RoutedEventArgs e) => Zoom.ZoomOut();

    private void OnZoomResetClick(object sender, System.Windows.RoutedEventArgs e) => Zoom.Reset();

    private void OnZoomInClick(object sender, System.Windows.RoutedEventArgs e) => Zoom.ZoomIn();

    private void OnExportXlsxClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ManualEditViewModel vm)
            return;

        if (!vm.ValidateBeforeExport())
            return;

        if (vm.HasUnsavedChanges)
        {
            var choice = ShowUnsavedExportDialog();
            if (choice == ExportUnsavedChoice.Cancel)
                return;

            if (choice == ExportUnsavedChoice.SaveThenExport && !vm.SaveCurrentForExport())
                return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "시간표 xlsx 저장",
            FileName = $"{vm.ExportFileNameBase}.xlsx",
        };

        if (dlg.ShowDialog() != true)
            return;

        TryExportAndOpen(() => vm.ExportXlsxCommand.Execute(dlg.FileName), dlg.FileName);
    }

    private ExportUnsavedChoice ShowUnsavedExportDialog()
    {
        var dialog = new Window
        {
            Title = "Excel 내보내기",
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(16),
            MinWidth = 360,
        };
        panel.Children.Add(new TextBlock
        {
            Text = "저장되지 않은 수정사항이 있습니다.",
            Margin = new Thickness(0, 0, 0, 14),
            TextWrapping = TextWrapping.Wrap,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var result = ExportUnsavedChoice.Cancel;
        AddButton("저장 후 내보내기", ExportUnsavedChoice.SaveThenExport);
        AddButton("저장하지 않고 내보내기", ExportUnsavedChoice.ExportWithoutSaving);
        AddButton("취소", ExportUnsavedChoice.Cancel);

        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.ShowDialog();
        return result;

        void AddButton(string content, ExportUnsavedChoice choice)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 80,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 4, 10, 4),
            };
            button.Click += (_, _) =>
            {
                result = choice;
                dialog.DialogResult = true;
            };
            buttons.Children.Add(button);
        }
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

    private CrossHoverState EvaluateCrossHover(UnifiedTimetableControl.CellClickedEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
        {
            return vm.EvaluateCrossHover(e.Day, e.Period, e.Grade, e.SubColumnIdx, e.Assignment);
        }
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
        {
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
        }
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

    private void OnDragMovePreviewStarted(object? sender, UnifiedTimetableControl.CellClickedEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            vm.BeginDragMovePreview(e.Day, e.Period, e.Grade, e.SubColumnIdx, e.Assignment);
    }

    private void OnDragMovePreviewEnded(object? sender, EventArgs e)
    {
        if (DataContext is ManualEditViewModel vm)
            vm.EndDragMovePreview();
    }

    private static FrameworkElement? FindTaggedCell(DependencyObject root, int day, int period, int grade, int subColumnIdx)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element
                && element.Tag is ValueTuple<int, int, int, int, CellAssignment> tag
                && tag.Item1 == day
                && tag.Item2 == period
                && tag.Item3 == grade
                && tag.Item4 == subColumnIdx)
            {
                return element;
            }

            var descendant = FindTaggedCell(child, day, period, grade, subColumnIdx);
            if (descendant != null)
                return descendant;
        }

        return null;
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
