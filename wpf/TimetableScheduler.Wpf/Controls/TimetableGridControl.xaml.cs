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
    private static readonly Brush LunchBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly Brush HeaderBg = new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xF8));
    private static readonly Brush CellBorder = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

    static TimetableGridControl()
    {
        LunchBg.Freeze();
        HeaderBg.Freeze();
        CellBorder.Freeze();
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
                    BodyGrid.Children.Add(MakeCell("점심", row, col, 1, LunchBg, 9, FontWeights.Normal));
                    continue;
                }

                var cellVm = vm.CellAt(d, p);
                if (cellVm.Items.Count == 0)
                {
                    BodyGrid.Children.Add(MakeCell("", row, col, 1, EmptyBg, 0, FontWeights.Normal));
                    continue;
                }

                int maxRs = cellVm.Items.Max(a => a.RowSpan);
                var stack = new StackPanel { Orientation = Orientation.Vertical };
                foreach (var a in cellVm.Items)
                    stack.Children.Add(MakeAssignmentChip(a));

                var border = new Border
                {
                    BorderBrush = CellBorder,
                    BorderThickness = new Thickness(0.5),
                    Background = GradeToBrushConverter.BrushFor(cellVm.Items[0].Grade),
                    Child = stack,
                };
                Grid.SetRow(border, row);
                Grid.SetColumn(border, col);
                if (maxRs > 1) Grid.SetRowSpan(border, maxRs);
                BodyGrid.Children.Add(border);

                for (int k = 1; k < maxRs; k++)
                    covered.Add((row + k, col));
            }
        }
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

    private static FrameworkElement MakeAssignmentChip(CellAssignment a)
    {
        var panel = new StackPanel { Margin = new Thickness(2) };
        var nameText = string.IsNullOrEmpty(a.SectionLabel)
            ? a.CourseName
            : $"{a.CourseName}·{a.SectionLabel}";
        panel.Children.Add(new TextBlock
        {
            Text = nameText,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = a.ProfessorLabel,
            FontSize = 9,
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.DimGray,
        });
        if (!string.IsNullOrEmpty(a.RoomsLabel))
            panel.Children.Add(new TextBlock
            {
                Text = a.RoomsLabel,
                FontSize = 9,
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.DarkSlateGray,
            });
        return panel;
    }
}
