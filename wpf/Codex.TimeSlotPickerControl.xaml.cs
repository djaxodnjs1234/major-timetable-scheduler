using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TimetableScheduler.ViewModel.Editors;

namespace TimetableScheduler.Wpf.Controls;

public partial class TimeSlotPickerControl : UserControl
{
    private static readonly Brush HeaderBg = new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8));
    private static readonly Brush LunchBg = new SolidColorBrush(Color.FromRgb(0xD7, 0xE6, 0xF7));
    private static readonly Brush EmptyBg = Brushes.White;
    private static readonly Brush SelectedBg = new SolidColorBrush(Color.FromRgb(0xE2, 0xE5, 0xEA));
    private static readonly Brush Border = new SolidColorBrush(Color.FromRgb(0xC2, 0xC6, 0xD4));
    private static readonly Brush HeaderForeground = Brushes.White;
    private static readonly Brush SelectedStroke = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x7D));

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
        {
            foreach (var cell in oldVm.Cells)
                cell.PropertyChanged -= OnCellPropertyChanged;
        }

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
        if (sender is not TimeSlotPickerCell cell) return;
        foreach (var child in RootGrid.Children)
        {
            if (child is System.Windows.Controls.Border b
                && b.Tag is ValueTuple<int, int> key
                && key.Item1 == cell.Day && key.Item2 == cell.Period)
            {
                b.Background = cell.IsSelected ? SelectedBg : EmptyBg;
                b.Child = BuildSlotContent(cell);
                break;
            }
        }
    }

    private void Build(TimeSlotPickerViewModel vm)
    {
        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        var periods = vm.Cells
            .Select(cell => cell.Period)
            .Distinct()
            .OrderBy(period => period)
            .ToList();
        var rowByPeriod = periods
            .Select((period, index) => new { period, row = index + 1 })
            .ToDictionary(item => item.period, item => item.row);

        RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in periods)
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        for (int i = 0; i < 5; i++)
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string[] dayNames = { "\uC6D4", "\uD654", "\uC218", "\uBAA9", "\uAE08" };
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
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = HeaderForeground,
                },
            };
            Grid.SetRow(hdr, 0);
            Grid.SetColumn(hdr, 1 + d);
            RootGrid.Children.Add(hdr);
        }

        foreach (var cell in vm.Cells)
        {
            var row = rowByPeriod[cell.Period];
            if (cell.Day == 0)
            {
                var lbl = new System.Windows.Controls.Border
                {
                    Background = HeaderBg,
                    BorderBrush = Border,
                    BorderThickness = new Thickness(0.5),
                    Child = new TextBlock
                    {
                        Text = cell.IsLunch
                            ? "\uC810\uC2EC"
                            : $"{cell.Period}\uAD50\uC2DC\n{8 + cell.Period:00}:00~{9 + cell.Period:00}:00",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 11,
                        Foreground = HeaderForeground,
                    },
                };
                Grid.SetRow(lbl, row);
                Grid.SetColumn(lbl, 0);
                RootGrid.Children.Add(lbl);
            }

            var btn = new System.Windows.Controls.Border
            {
                BorderBrush = Border,
                BorderThickness = new Thickness(0.5),
                Background = cell.IsLunch ? LunchBg : (cell.IsSelected ? SelectedBg : EmptyBg),
                Tag = (cell.Day, cell.Period),
                MinHeight = 34,
                Cursor = cell.IsLunch ? Cursors.Arrow : Cursors.Hand,
                Child = BuildSlotContent(cell),
            };
            if (!cell.IsLunch) btn.MouseLeftButtonDown += OnCellClicked;
            Grid.SetRow(btn, row);
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

    private static UIElement BuildSlotContent(TimeSlotPickerCell cell)
    {
        if (cell.IsLunch)
        {
            return new TextBlock
            {
                Text = "\uC810\uC2EC",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 9,
                Foreground = HeaderForeground,
            };
        }

        var grid = new Grid();
        if (!cell.IsSelected) return grid;

        grid.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = 0,
            Y1 = 0,
            X2 = 1,
            Y2 = 1,
            Stretch = Stretch.Fill,
            Stroke = SelectedStroke,
            StrokeThickness = 2,
            SnapsToDevicePixels = true,
        });
        grid.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = 1,
            Y1 = 0,
            X2 = 0,
            Y2 = 1,
            Stretch = Stretch.Fill,
            Stroke = SelectedStroke,
            StrokeThickness = 2,
            SnapsToDevicePixels = true,
        });
        return grid;
    }
}
