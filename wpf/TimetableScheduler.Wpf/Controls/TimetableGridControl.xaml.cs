using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimetableScheduler.Wpf.Converters;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.Wpf.Controls;

public partial class TimetableGridControl : UserControl
{
    private static readonly Brush EmptyBg = Brushes.White;
    private static readonly Brush LunchBg = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7));
    private static readonly Brush HeaderBg = new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xF8));
    private static readonly Brush CellBorder = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF3));
    private static readonly Brush DayBoundaryBorder = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

    static TimetableGridControl()
    {
        LunchBg.Freeze();
        HeaderBg.Freeze();
        CellBorder.Freeze();
        DayBoundaryBorder.Freeze();
    }

    public TimetableGridControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        BodyGrid.Children.Clear();
        if (DataContext is not TimetableGridViewModel vm) return;

        // Row labels (period + time)
        for (int p = 1; p <= 9; p++)
        {
            int row = p - 1;
            var label = p == 5 ? "점심" : p.ToString();
            BodyGrid.Children.Add(MakeLabel(label, row, 0, HeaderBg));
            BodyGrid.Children.Add(MakeLabel($"{8 + p:D2}:00", row, 1, HeaderBg));
        }
        AddLunchBorder(row: 4, colSpan: 7);

        // Track which cells are covered by RowSpan above
        var covered = new HashSet<(int Row, int Col)>();

        for (int d = 0; d < 5; d++)
        {
            for (int p = 1; p <= 9; p++)
            {
                int row = p - 1;
                int col = 2 + d;
                if (covered.Contains((row, col))) continue;

                if (p == 5)
                {
                    continue;
                }

                var cellVm = vm.CellAt(d, p);
                if (cellVm.Items.Count == 0)
                {
                    BodyGrid.Children.Add(MakeCell("", row, col, 1, EmptyBg, 0, FontWeights.Normal));
                    continue;
                }

                int maxRs = cellVm.Items.Max(a => a.RowSpan);
                var assignmentHost = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                for (int i = 0; i < cellVm.Items.Count; i++)
                {
                    assignmentHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    var a = cellVm.Items[i];
                    var chip = UnifiedTimetableControl.MakeChipBorder(
                        a,
                        GradeToBrushConverter.BrushFor(a.Grade),
                        crossLabel: null);
                    chip.HorizontalAlignment = HorizontalAlignment.Stretch;
                    chip.VerticalAlignment = VerticalAlignment.Stretch;
                    Grid.SetRow(chip, i);
                    assignmentHost.Children.Add(chip);
                }

                var border = new Border
                {
                    BorderBrush = CellBorder,
                    BorderThickness = new Thickness(0.5),
                    Background = EmptyBg,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Child = assignmentHost,
                };
                Grid.SetRow(border, row);
                Grid.SetColumn(border, col);
                if (maxRs > 1) Grid.SetRowSpan(border, maxRs);
                BodyGrid.Children.Add(border);

                for (int k = 1; k < maxRs; k++)
                    covered.Add((row + k, col));
            }
        }

        for (int col = 2; col <= 6; col++)
            AddDayBoundary(col);
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
}
