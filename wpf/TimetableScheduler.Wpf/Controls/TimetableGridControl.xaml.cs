using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimetableScheduler.Domain;
using TimetableScheduler.Wpf.Converters;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.Wpf.Controls;

public partial class TimetableGridControl : UserControl
{
    private static readonly Brush EmptyBg = Brushes.White;
    private static readonly Brush LunchBg = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7));
    private static readonly Brush NightBg = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF));
    private static readonly Brush HeaderBg = new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xF8));
    private static readonly Brush CellBorder = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF3));
    private static readonly Brush DayBoundaryBorder = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

    static TimetableGridControl()
    {
        LunchBg.Freeze();
        NightBg.Freeze();
        HeaderBg.Freeze();
        CellBorder.Freeze();
        DayBoundaryBorder.Freeze();
    }

    public TimetableGridControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public static readonly DependencyProperty PeriodRowMinHeightProperty =
        DependencyProperty.Register(
            nameof(PeriodRowMinHeight),
            typeof(double),
            typeof(TimetableGridControl),
            new PropertyMetadata(64.0, OnLayoutMetricsChanged));

    public static readonly DependencyProperty LunchRowMinHeightProperty =
        DependencyProperty.Register(
            nameof(LunchRowMinHeight),
            typeof(double),
            typeof(TimetableGridControl),
            new PropertyMetadata(32.0, OnLayoutMetricsChanged));

    public static readonly DependencyProperty NightRowMinHeightProperty =
        DependencyProperty.Register(
            nameof(NightRowMinHeight),
            typeof(double),
            typeof(TimetableGridControl),
            new PropertyMetadata(32.0, OnLayoutMetricsChanged));

    public static readonly DependencyProperty DayColumnMinWidthProperty =
        DependencyProperty.Register(
            nameof(DayColumnMinWidth),
            typeof(double),
            typeof(TimetableGridControl),
            new PropertyMetadata(76.0, OnLayoutMetricsChanged));

    public double PeriodRowMinHeight
    {
        get => (double)GetValue(PeriodRowMinHeightProperty);
        set => SetValue(PeriodRowMinHeightProperty, value);
    }

    public double LunchRowMinHeight
    {
        get => (double)GetValue(LunchRowMinHeightProperty);
        set => SetValue(LunchRowMinHeightProperty, value);
    }

    public double NightRowMinHeight
    {
        get => (double)GetValue(NightRowMinHeightProperty);
        set => SetValue(NightRowMinHeightProperty, value);
    }

    public double DayColumnMinWidth
    {
        get => (double)GetValue(DayColumnMinWidthProperty);
        set => SetValue(DayColumnMinWidthProperty, value);
    }

    private static void OnLayoutMetricsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimetableGridControl control)
            control.Rebuild();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is TimetableGridViewModel oldVm)
        {
            foreach (var c in oldVm.Cells)
                c.Items.CollectionChanged -= OnCellItemsChanged;
        }
        if (e.NewValue is TimetableGridViewModel newVm)
        {
            foreach (var c in newVm.Cells)
                c.Items.CollectionChanged += OnCellItemsChanged;
        }
        Rebuild();
    }

    private void OnCellItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Rebuild();

    private void Rebuild()
    {
        HeaderGrid.Children.Clear();
        HeaderGrid.ColumnDefinitions.Clear();
        BodyGrid.Children.Clear();
        BodyGrid.ColumnDefinitions.Clear();
        if (DataContext is not TimetableGridViewModel vm)
        {
            BodyGrid.RowDefinitions.Clear();
            return;
        }
        ApplyBodyRowHeights(vm.Periods);

        var layout = BuildParallelLayout(vm);
        BuildColumns(HeaderGrid, layout.DayWidths);
        BuildColumns(BodyGrid, layout.DayWidths);
        BuildHeader(layout);

        // Row labels (period + time)
        foreach (var p in vm.Periods)
        {
            int row = BodyRowForPeriod(p);
            var label = p == 5 ? "점심" : p.ToString();
            BodyGrid.Children.Add(MakeLabel(label, row, 0, HeaderBg));
            BodyGrid.Children.Add(MakeLabel(SchedulePeriods.TimeRange(p), row, 1, HeaderBg));
        }
        AddLunchBorder(
            row: vm.Periods.TakeWhile(period => period != 5).Count(),
            colSpan: 2 + layout.DayWidths.Sum());
        AddNightBorder(
            row: NightSeparatorRow,
            colSpan: 2 + layout.DayWidths.Sum());

        // Track which cells are covered by RowSpan above
        var covered = new HashSet<(int Row, int Col)>();

        for (int d = 0; d < 5; d++)
        {
            foreach (var p in vm.Periods)
            {
                int row = BodyRowForPeriod(p);

                if (p == 5)
                {
                    continue;
                }

                var placements = layout.Placements
                    .Where(a => a.Day == d && a.Period == p)
                    .ToList();
                foreach (var placement in placements)
                    AddAssignmentBorder(placement, layout.DayStart[d]);

                for (int sub = 0; sub < layout.DayWidths[d]; sub++)
                {
                    var subCol = layout.DayStart[d] + sub;
                    if (covered.Contains((row, subCol)))
                        continue;

                    var placement = placements.FirstOrDefault(a => a.SubColumn == sub);
                    if (placement != null)
                    {
                        for (int k = 1; k < placement.Assignment.RowSpan; k++)
                            covered.Add((row + k, subCol));
                        continue;
                    }

                    BodyGrid.Children.Add(MakeCell("", row, subCol, 1, EmptyBg, 0, FontWeights.Normal));
                }
            }
        }

        foreach (var start in layout.DayStart)
            AddDayBoundary(start);
    }

    private void BuildHeader(GridLayout layout)
    {
        HeaderGrid.Children.Add(MakeHeaderText("교시", 0, 1, 11));
        HeaderGrid.Children.Add(MakeHeaderText("시간", 1, 1, 11));

        for (var d = 0; d < 5; d++)
        {
            var dayText = MakeHeaderText(DayName(d), layout.DayStart[d], layout.DayWidths[d], 12);
            HeaderGrid.Children.Add(dayText);
            AddHeaderDayBoundary(layout.DayStart[d]);
        }
    }

    private static TextBlock MakeHeaderText(string text, int col, int colSpan, double fontSize)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            FontSize = fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4),
        };
        Grid.SetColumn(block, col);
        if (colSpan > 1) Grid.SetColumnSpan(block, colSpan);
        return block;
    }

    private void BuildColumns(Grid grid, IReadOnlyList<int> dayWidths)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        foreach (var width in dayWidths)
            for (var i = 0; i < width; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = DayColumnMinWidth });
    }

    private void ApplyBodyRowHeights(IReadOnlyList<int> periods)
    {
        BodyGrid.RowDefinitions.Clear();
        foreach (var period in periods)
        {
            if (period == SchedulePeriods.FirstNightPeriod)
            {
                BodyGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star),
                    MinHeight = NightRowMinHeight,
                });
            }
            BodyGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star),
                MinHeight = period == 5 ? LunchRowMinHeight : PeriodRowMinHeight,
            });
        }
    }

    private GridLayout BuildParallelLayout(TimetableGridViewModel vm)
    {
        var placementsByDay = Enumerable.Range(0, 5).Select(_ => new List<AssignmentPlacement>()).ToList();
        var dayWidths = Enumerable.Range(0, 5).Select(_ => 1).ToList();

        for (var d = 0; d < 5; d++)
        {
            var active = new List<AssignmentPlacement>();
            foreach (var cell in vm.Cells
                         .Where(c => c.Day == d && c.IsOccupied)
                         .OrderBy(c => c.Period))
            {
                active.RemoveAll(p => p.EndPeriod < cell.Period);
                foreach (var assignment in cell.Items)
                {
                    var sub = 0;
                    while (active.Any(p => p.SubColumn == sub && p.EndPeriod >= cell.Period))
                        sub++;

                    var placement = new AssignmentPlacement(d, cell.Period, sub, assignment);
                    placementsByDay[d].Add(placement);
                    active.Add(placement);
                    dayWidths[d] = Math.Max(dayWidths[d], sub + 1);
                }
            }
        }

        var dayStart = new int[5];
        var col = 2;
        for (var d = 0; d < 5; d++)
        {
            dayStart[d] = col;
            col += dayWidths[d];
        }

        return new GridLayout(dayWidths, dayStart, placementsByDay.SelectMany(p => p).ToList());
    }

    private void AddAssignmentBorder(AssignmentPlacement placement, int dayStart)
    {
        var chip = UnifiedTimetableControl.MakeChipBorder(
            placement.Assignment,
            GradeToBrushConverter.BrushFor(placement.Assignment.Grade),
            crossLabel: null);
        chip.HorizontalAlignment = HorizontalAlignment.Stretch;
        chip.VerticalAlignment = VerticalAlignment.Stretch;

        var border = new Border
        {
            BorderBrush = CellBorder,
            BorderThickness = new Thickness(0.5),
            Background = EmptyBg,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = chip,
        };

        Grid.SetRow(border, BodyRowForPeriod(placement.Period));
        Grid.SetColumn(border, dayStart + placement.SubColumn);
        if (placement.Assignment.RowSpan > 1)
            Grid.SetRowSpan(border, placement.Assignment.RowSpan);
        BodyGrid.Children.Add(border);
    }

    private static Border MakeLabel(string text, int row, int col, Brush bg)
    {
        var border = new Border
        {
            BorderBrush = CellBorder,
            BorderThickness = new Thickness(0.5),
            Background = bg,
            Child = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            },
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        return border;
    }

    private static Border MakeCell(string text, int row, int col, int rowSpan, Brush bg, int fontSize, FontWeight weight)
    {
        var border = new Border
        {
            BorderBrush = CellBorder,
            BorderThickness = new Thickness(0.5),
            Background = bg,
            Child = string.IsNullOrEmpty(text) ? null : new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = fontSize,
                FontWeight = weight,
            },
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        if (rowSpan > 1) Grid.SetRowSpan(border, rowSpan);
        return border;
    }

    private void AddLunchBorder(int row, int colSpan)
    {
        var border = new Border
        {
            BorderBrush = DayBoundaryBorder,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Background = LunchBg,
            Child = new TextBlock
            {
                Text = "점 심 시 간",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 8,
                FontWeight = FontWeights.Normal,
            },
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, 0);
        Grid.SetColumnSpan(border, colSpan);
        Panel.SetZIndex(border, 30);
        BodyGrid.Children.Add(border);
    }

    private void AddNightBorder(int row, int colSpan)
    {
        var border = new Border
        {
            BorderBrush = DayBoundaryBorder,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Background = NightBg,
            Child = new TextBlock
            {
                Text = "야 간 수 업",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 8,
                FontWeight = FontWeights.Normal,
            },
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, 0);
        Grid.SetColumnSpan(border, colSpan);
        Panel.SetZIndex(border, 30);
        BodyGrid.Children.Add(border);
    }

    internal static int BodyRowForPeriod(int period) =>
        period - 1 + (period >= SchedulePeriods.FirstNightPeriod ? 1 : 0);

    internal static int NightSeparatorRow => SchedulePeriods.FirstNightPeriod - 1;

    private void AddDayBoundary(int col)
    {
        var border = new Border
        {
            BorderBrush = DayBoundaryBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Grid.SetRow(border, 0);
        Grid.SetColumn(border, col);
        Grid.SetRowSpan(border, BodyGrid.RowDefinitions.Count);
        Panel.SetZIndex(border, 20);
        BodyGrid.Children.Add(border);
    }

    private void AddHeaderDayBoundary(int col)
    {
        var border = new Border
        {
            BorderBrush = DayBoundaryBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Grid.SetColumn(border, col);
        Panel.SetZIndex(border, 20);
        HeaderGrid.Children.Add(border);
    }

    private static string DayName(int day) => day switch
    {
        0 => "월",
        1 => "화",
        2 => "수",
        3 => "목",
        4 => "금",
        _ => "",
    };

    private sealed record AssignmentPlacement(
        int Day,
        int Period,
        int SubColumn,
        CellAssignment Assignment)
    {
        public int EndPeriod => Period + Assignment.RowSpan - 1;
    }

    private sealed record GridLayout(
        IReadOnlyList<int> DayWidths,
        IReadOnlyList<int> DayStart,
        IReadOnlyList<AssignmentPlacement> Placements);
}
