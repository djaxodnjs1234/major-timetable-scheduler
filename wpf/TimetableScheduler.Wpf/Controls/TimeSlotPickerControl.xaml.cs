using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TimetableScheduler.ViewModel.Editors;

namespace TimetableScheduler.Wpf.Controls;

public partial class TimeSlotPickerControl : UserControl
{
    private static readonly Brush HeaderBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly Brush LunchBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly Brush EmptyBg = Brushes.White;
    private static readonly Brush SelectedBg = CreateSelectedBrush();
    private static readonly Brush Border = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

    static TimeSlotPickerControl()
    {
        HeaderBg.Freeze();
        LunchBg.Freeze();
        SelectedBg.Freeze();
        Border.Freeze();
    }

    public TimeSlotPickerControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is TimeSlotPickerViewModel oldVm)
            foreach (var cell in oldVm.Cells)
                cell.PropertyChanged -= OnCellPropertyChanged;

        if (e.NewValue is TimeSlotPickerViewModel newVm)
        {
            foreach (var cell in newVm.Cells)
                cell.PropertyChanged += OnCellPropertyChanged;
            Build(newVm);
        }
    }

    private void OnCellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TimeSlotPickerCell.IsSelected)) return;
        // Find the matching button and update its background
        if (sender is not TimeSlotPickerCell cell) return;
        foreach (var child in RootGrid.Children)
        {
            if (child is System.Windows.Controls.Border b
                && b.Tag is ValueTuple<int, int> key
                && key.Item1 == cell.Day && key.Item2 == cell.Period)
            {
                b.Background = cell.IsSelected ? SelectedBg : EmptyBg;
                if (b.Child is TextBlock tb)
                {
                    tb.Text = "";
                    tb.Foreground = Brushes.Black;
                }
                break;
            }
        }
    }

    private void Build(TimeSlotPickerViewModel vm)
    {
        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        // 1 header row + 9 period rows = 10 rows
        for (int i = 0; i < 10; i++)
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 1 period-label col + 5 day cols = 6 cols
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        for (int i = 0; i < 5; i++)
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Day headers
        string[] dayNames = { "월", "화", "수", "목", "금" };
        for (int d = 0; d < 5; d++)
        {
            var hdr = new System.Windows.Controls.Border
            {
                Background = HeaderBg,
                BorderBrush = Border,
                BorderThickness = new Thickness(0.5),
                Child = new TextBlock
                {
                    Text = dayNames[d],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11, FontWeight = FontWeights.Bold,
                },
            };
            Grid.SetRow(hdr, 0);
            Grid.SetColumn(hdr, 1 + d);
            RootGrid.Children.Add(hdr);
        }

        // Period rows
        foreach (var cell in vm.Cells)
        {
            // Period label (only once per period, on day 0)
            if (cell.Day == 0)
            {
                var lbl = new System.Windows.Controls.Border
                {
                    Background = HeaderBg,
                    BorderBrush = Border,
                    BorderThickness = new Thickness(0.5),
                    Child = new TextBlock
                    {
                        Text = $"{cell.Period}교시\n({8 + cell.Period:00}:00~)",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 11,
                    },
                };
                Grid.SetRow(lbl, cell.Period);
                Grid.SetColumn(lbl, 0);
                RootGrid.Children.Add(lbl);
            }

            var btn = new System.Windows.Controls.Border
            {
                BorderBrush = Border,
                BorderThickness = new Thickness(0.5),
                Background = cell.IsLunch ? LunchBg : (cell.IsSelected ? SelectedBg : EmptyBg),
                Tag = (cell.Day, cell.Period),
                MinHeight = 22,
                Cursor = cell.IsLunch ? Cursors.Arrow : Cursors.Hand,
                Child = new TextBlock
                {
                    Text = cell.IsLunch ? "점심" : "",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 9,
                    Foreground = Brushes.Black,
                },
            };
            if (!cell.IsLunch) btn.MouseLeftButtonDown += OnCellClicked;
            Grid.SetRow(btn, cell.Period);
            Grid.SetColumn(btn, 1 + cell.Day);
            RootGrid.Children.Add(btn);
        }
    }

    private void OnCellClicked(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not TimeSlotPickerViewModel vm) return;
        if (sender is System.Windows.Controls.Border b
            && b.Tag is ValueTuple<int, int> key)
        {
            vm.Toggle(key.Item1, key.Item2);
            e.Handled = true;
        }
    }

    private static Brush CreateSelectedBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(0xE5, 0xF0, 0xFF)),
            null,
            new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(new SolidColorBrush(Color.FromRgb(0x7C, 0x8B, 0xA1)), 1),
            new GeometryGroup
            {
                Children =
                {
                    new LineGeometry(new Point(-2, 8), new Point(8, -2)),
                    new LineGeometry(new Point(0, 10), new Point(10, 0)),
                },
            }));

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }
}
