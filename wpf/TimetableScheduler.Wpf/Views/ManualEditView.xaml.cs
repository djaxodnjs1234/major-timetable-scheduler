using System.Diagnostics;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.Wpf.Controls;
using TimetableScheduler.Wpf.Converters;

[assembly: InternalsVisibleTo("TimetableScheduler.Wpf.Tests")]

namespace TimetableScheduler.Wpf.Views;

public partial class ManualEditView : UserControl
{
    private const int StagedBlocksPerRow = 4;
    private const double StagedBlockPeriodHeight = 42.0;

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
        AllowDrop = true;
        AddHandler(DragDrop.DragOverEvent, new DragEventHandler(OnManualEditDragOverForPreview), true);
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => Focus();
    }

    private ManualEditViewModel? _subscribedViewModel;
    private Point? _stagedDragStartPoint;
    private ManualEditViewModel.StagedBlockItem? _stagedDragItem;
    private Point _stagedDragClickOffset;
    private bool _suppressNextStagedClick;
    private FrameworkElement? _stagedDragSourceCard;
    private double _stagedDragSourceOriginalOpacity = 1.0;
    private DragPreviewAdorner? _stagedDragPreviewAdorner;
    private AdornerLayer? _stagedDragPreviewLayer;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ConflictFocusRequested -= OnConflictFocusRequested;
            _subscribedViewModel.StagedBlocks.CollectionChanged -= OnStagedBlocksChanged;
            ((INotifyPropertyChanged)_subscribedViewModel).PropertyChanged -= OnManualEditPropertyChanged;
        }

        _subscribedViewModel = e.NewValue as ManualEditViewModel;
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ConflictFocusRequested += OnConflictFocusRequested;
            _subscribedViewModel.StagedBlocks.CollectionChanged += OnStagedBlocksChanged;
            ((INotifyPropertyChanged)_subscribedViewModel).PropertyChanged += OnManualEditPropertyChanged;
        }

        RebuildStagedBlockCards();
    }

    private void OnStagedBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(RebuildStagedBlockCards), DispatcherPriority.Background);

    private void OnManualEditPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ManualEditViewModel.SelectedStagedBlock)
            or nameof(ManualEditViewModel.HasStagedBlocks)
            or nameof(ManualEditViewModel.StagedBlockCount))
        {
            Dispatcher.BeginInvoke(new Action(RebuildStagedBlockCards), DispatcherPriority.Background);
        }
    }

    private void RebuildStagedBlockCards()
    {
        StagedBlocksHost.Children.Clear();
        if (DataContext is not ManualEditViewModel vm)
            return;

        Grid? currentRow = null;
        var currentColumn = 0;
        foreach (var item in vm.StagedBlocks)
        {
            if (currentRow == null || currentColumn >= StagedBlocksPerRow)
            {
                currentRow = CreateStagedBlockRow();
                StagedBlocksHost.Children.Add(currentRow);
                currentColumn = 0;
            }

            var card = UnifiedTimetableControl.MakeChipBorder(
                item.Assignment,
                GradeToBrushConverter.BrushFor(item.Grade),
                crossLabel: null);
            ApplyCompactStagedCardStyle(card);
            card.Tag = item;
            card.Height = Math.Max(36.0, StagedBlockPeriodHeight * Math.Max(1, item.RowSpan));
            card.Margin = new Thickness(0, 0, 6, 6);
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.Cursor = Cursors.Hand;
            var isSelected = string.Equals(vm.SelectedStagedBlock?.Id, item.Id, StringComparison.Ordinal);
            card.BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8))
                : Brushes.Transparent;
            card.BorderThickness = isSelected
                ? new Thickness(2)
                : new Thickness(0);
            card.PreviewMouseLeftButtonDown += OnStagedCardMouseDown;
            card.MouseMove += OnStagedCardMouseMove;
            card.PreviewMouseLeftButtonUp += OnStagedCardMouseUp;
            Grid.SetColumn(card, currentColumn);
            currentRow.Children.Add(card);
            currentColumn++;
        }
    }

    private static void ApplyCompactStagedCardStyle(DependencyObject element)
    {
        switch (element)
        {
            case TextBlock text:
                if (text.FontSize >= 10)
                {
                    text.FontSize = 9;
                    text.LineHeight = 11;
                }
                else
                {
                    text.FontSize = 6.5;
                    text.LineHeight = 8;
                }

                text.Margin = new Thickness(0, 1, 0, 0);
                break;
            case StackPanel stack:
                stack.Margin = new Thickness(3, 4, 3, 3);
                foreach (UIElement child in stack.Children)
                    ApplyCompactStagedCardStyle(child);
                break;
            case Panel panel:
                foreach (UIElement child in panel.Children)
                    ApplyCompactStagedCardStyle(child);
                break;
            case Border border:
                if (border.Child != null)
                    ApplyCompactStagedCardStyle(border.Child);
                break;
            case Decorator decorator:
                if (decorator.Child != null)
                    ApplyCompactStagedCardStyle(decorator.Child);
                break;
            case ContentControl contentControl when contentControl.Content is DependencyObject child:
                ApplyCompactStagedCardStyle(child);
                break;
        }
    }

    private static Grid CreateStagedBlockRow()
    {
        var row = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 2),
        };
        for (var i = 0; i < StagedBlocksPerRow; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        return row;
    }

    private void OnStagedCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ManualEditViewModel.StagedBlockItem item })
            return;

        _stagedDragStartPoint = e.GetPosition(this);
        _stagedDragItem = item;
        _stagedDragClickOffset = e.GetPosition((IInputElement)sender);
        e.Handled = true;
    }

    private void OnStagedCardMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not ManualEditViewModel vm
            || sender is not FrameworkElement card
            || e.LeftButton != MouseButtonState.Pressed
            || _stagedDragStartPoint is not Point start
            || _stagedDragItem == null)
            return;

        var current = e.GetPosition(this);
        var horizontalThreshold = Math.Max(2.0, SystemParameters.MinimumHorizontalDragDistance * 0.65);
        var verticalThreshold = Math.Max(2.0, SystemParameters.MinimumVerticalDragDistance * 0.65);
        if (Math.Abs(current.X - start.X) < horizontalThreshold
            && Math.Abs(current.Y - start.Y) < verticalThreshold)
            return;

        var rowSpan = Math.Max(1, _stagedDragItem.RowSpan);
        var periodHeight = card.ActualHeight / rowSpan;
        var grabbedPeriodOffset = periodHeight > 0
            ? Math.Clamp((int)(_stagedDragClickOffset.Y / periodHeight), 0, rowSpan - 1)
            : 0;
        var source = new UnifiedTimetableControl.CellClickedEventArgs(
            _stagedDragItem.SourceDay,
            _stagedDragItem.SourcePeriod,
            _stagedDragItem.Grade,
            0,
            _stagedDragItem.Assignment);

        try
        {
            BeginStagedDragVisuals(card, current);
            vm.SelectedStagedBlock = _stagedDragItem;
            _suppressNextStagedClick = true;
            DragDrop.DoDragDrop(
                card,
                UnifiedTimetableControl.CreateExternalAssignmentDragData(source, grabbedPeriodOffset),
                DragDropEffects.Move);
        }
        finally
        {
            _stagedDragStartPoint = null;
            _stagedDragItem = null;
            RestoreStagedDragVisuals();
            Dispatcher.BeginInvoke(
                () => _suppressNextStagedClick = false,
                DispatcherPriority.Background);
        }
    }

    private void OnStagedCardMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_suppressNextStagedClick)
        {
            e.Handled = true;
            return;
        }

        if (DataContext is ManualEditViewModel vm
            && sender is FrameworkElement { Tag: ManualEditViewModel.StagedBlockItem item })
        {
            if (string.Equals(vm.SelectedStagedBlock?.Id, item.Id, StringComparison.Ordinal))
                vm.ClearStagedBlockSelectionCommand.Execute(null);
            else
                vm.SelectedStagedBlock = item;
            e.Handled = true;
        }

        _stagedDragStartPoint = null;
        _stagedDragItem = null;
    }

    private void BeginStagedDragVisuals(FrameworkElement sourceCard, Point currentPosition)
    {
        RestoreStagedDragVisuals();
        _stagedDragSourceCard = sourceCard;
        _stagedDragSourceOriginalOpacity = sourceCard.Opacity;
        sourceCard.Opacity = 0.45;

        _stagedDragPreviewLayer = AdornerLayer.GetAdornerLayer(this);
        if (_stagedDragPreviewLayer == null)
            return;

        _stagedDragPreviewAdorner = new DragPreviewAdorner(
            this,
            sourceCard,
            sourceCard.ActualWidth,
            sourceCard.ActualHeight,
            _stagedDragClickOffset);
        _stagedDragPreviewLayer.Add(_stagedDragPreviewAdorner);
        UpdateStagedDragPreview(currentPosition);
    }

    private void OnManualEditDragOverForPreview(object sender, DragEventArgs e)
    {
        if (_stagedDragPreviewAdorner != null)
            UpdateStagedDragPreview(e.GetPosition(this));
    }

    private void UpdateStagedDragPreview(Point position)
    {
        _stagedDragPreviewAdorner?.Update(position);
    }

    private void RestoreStagedDragVisuals()
    {
        if (_stagedDragSourceCard != null)
            _stagedDragSourceCard.Opacity = _stagedDragSourceOriginalOpacity;
        if (_stagedDragPreviewAdorner != null && _stagedDragPreviewLayer != null)
            _stagedDragPreviewLayer.Remove(_stagedDragPreviewAdorner);

        _stagedDragSourceCard = null;
        _stagedDragSourceOriginalOpacity = 1.0;
        _stagedDragPreviewAdorner = null;
        _stagedDragPreviewLayer = null;
    }

    private void OnStagingEmptyMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinStagedCard(e.OriginalSource as DependencyObject)
            || FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
            return;

        if (DataContext is ManualEditViewModel vm)
            vm.ClearStagedBlockSelectionCommand.Execute(null);
    }

    private void OnStagingDragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanDropTimetableBlockIntoStaging(e)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnStagingDrop(object sender, DragEventArgs e)
    {
        if (DataContext is ManualEditViewModel vm
            && CanDropTimetableBlockIntoStaging(e)
            && UnifiedTimetableControl.TryGetCellDragData(e.Data, out var source, out _, out _)
            && source.Assignment != null)
        {
            vm.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
            if (vm.StageSelectedBlockCommand.CanExecute(null))
                vm.StageSelectedBlockCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static bool CanDropTimetableBlockIntoStaging(DragEventArgs e) =>
        UnifiedTimetableControl.TryGetCellDragData(e.Data, out var source, out _, out var isExternal)
        && !isExternal
        && source.Assignment != null;

    private static bool IsWithinStagedCard(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is FrameworkElement { Tag: ManualEditViewModel.StagedBlockItem })
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T match)
                return match;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
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
        {
            if (vm.SelectedStagedBlock != null)
            {
                return vm.CanPlaceSelectedStagedBlock(
                    e.Target.Day,
                    e.Target.Period,
                    e.Target.Grade,
                    e.Target.SubColumnIdx);
            }

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
        }
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
        {
            if (vm.SelectedStagedBlock != null)
            {
                vm.HandleStagedBlockDrop(
                    e.Target.Day,
                    e.Target.Period,
                    e.Target.Grade,
                    e.Target.SubColumnIdx);
                return;
            }

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
