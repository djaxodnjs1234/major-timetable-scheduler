using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.Wpf.Converters;

namespace TimetableScheduler.Wpf.Controls;

public partial class UnifiedTimetableControl : UserControl
{
    private static readonly Brush EmptyBg = Brushes.White;
    private static readonly Brush LunchBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly Brush HeaderBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly Brush CellBorder = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly Brush MoveAllowedBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
    private static readonly Brush MoveWarningBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1));
    private static readonly Brush MoveBlockedBg = new SolidColorBrush(Color.FromRgb(0xFD, 0xE7, 0xE9));
    private static readonly Brush MoveBlockedBorder = new SolidColorBrush(Color.FromRgb(0xBA, 0x1A, 0x1A));
    private static readonly Brush SelectedBorder = new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8));

    static UnifiedTimetableControl()
    {
        LunchBg.Freeze();
        HeaderBg.Freeze();
        CellBorder.Freeze();
        MoveAllowedBg.Freeze();
        MoveWarningBg.Freeze();
        MoveBlockedBg.Freeze();
        MoveBlockedBorder.Freeze();
        SelectedBorder.Freeze();
    }

    public sealed class CellClickedEventArgs : EventArgs
    {
        public int Day { get; }
        public int Period { get; }
        public int Grade { get; }
        public int SubColumnIdx { get; }
        public CellAssignment? Assignment { get; }
        public CellClickedEventArgs(int day, int period, int grade, int subColumnIdx, CellAssignment? a)
        {
            Day = day; Period = period; Grade = grade; SubColumnIdx = subColumnIdx; Assignment = a;
        }
    }

    public event EventHandler<CellClickedEventArgs>? CellClicked;
    public event EventHandler<CellClickedEventArgs>? CrossAddRequested;

    public bool EnableCrossHover { get; set; }
    public Func<CellClickedEventArgs, CrossHoverState>? CrossHoverEvaluator { get; set; }

    public UnifiedTimetableControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is UnifiedTimetableViewModel oldVm)
            oldVm.Rebuilt -= OnRebuilt;
        if (e.NewValue is UnifiedTimetableViewModel newVm)
        {
            newVm.Rebuilt += OnRebuilt;
            Rebuild(newVm);
        }
    }

    private void OnRebuilt(object? sender, EventArgs e)
    {
        if (DataContext is UnifiedTimetableViewModel vm) Rebuild(vm);
    }

    private void Rebuild(UnifiedTimetableViewModel vm)
    {
        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        // 2 header rows + 9 period rows
        for (int i = 0; i < 2; i++)
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < 9; i++)
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 36 });

        // 2 label cols (period, time) + sum of all grade widths
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

        // Track column index per day_group → column offset
        // Also column index for each (day, gradeColIdx) for cell placement
        var dayColStart = new Dictionary<int, int>();   // day → starting col index (incl. label cols offset)
        int curCol = 2;
        foreach (var dg in vm.DayGroups)
        {
            dayColStart[dg.Day] = curCol;
            for (int k = 0; k < dg.TotalWidth; k++)
                RootGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                    MinWidth = 56,
                });
            curCol += dg.TotalWidth;
        }

        // Top-left labels
        AddBorder("교시", 0, 0, 2, 1, HeaderBg, 11, FontWeights.Bold);
        AddBorder("시간", 0, 1, 2, 1, HeaderBg, 11, FontWeights.Bold);

        // Day headers (row 0) + grade indicators (row 1)
        foreach (var dg in vm.DayGroups)
        {
            int startCol = dayColStart[dg.Day];
            AddBorder(DayName(dg.Day), 0, startCol, 1, dg.TotalWidth, HeaderBg, 12, FontWeights.Bold);

            int sub = startCol;
            foreach (var col in dg.Grades)
            {
                var bg = col.Grade is int g ? GradeToBrushConverter.BrushFor(g) : HeaderBg;
                AddBorder("", 1, sub, 1, col.Width, bg, 0, FontWeights.Normal, height: 4);
                sub += col.Width;
            }
        }

        // Period rows + cells
        // Track which (row, col) are covered by row spans
        var covered = new HashSet<(int Row, int Col)>();

        for (int p = 1; p <= 9; p++)
        {
            int row = 1 + p;  // headers occupy rows 0..1
            AddBorder(p == 5 ? "점심" : p.ToString(), row, 0, 1, 1, HeaderBg, 11, FontWeights.Normal);
            AddBorder($"{8 + p:D2}:00", row, 1, 1, 1, HeaderBg, 10, FontWeights.Normal);

            foreach (var dg in vm.DayGroups)
            {
                int startCol = dayColStart[dg.Day];
                int sub = startCol;
                foreach (var col in dg.Grades)
                {
                    if (p == 5)
                    {
                        AddBorder("점심", row, sub, 1, col.Width, LunchBg, 9, FontWeights.Normal);
                        sub += col.Width;
                        continue;
                    }
                    if (col.Grade is not int g)
                    {
                        AddBorder("", row, sub, 1, col.Width, EmptyBg, 0, FontWeights.Normal);
                        sub += col.Width;
                        continue;
                    }

                    // Place each course in this (day, period, grade) in its sub-column slot
                    var coursesHere = vm.Cells
                        .Where(c => c.Day == dg.Day && c.Period == p && c.Grade == g)
                        .OrderBy(c => c.SubColumnIdx)
                        .ToList();

                    for (int k = 0; k < col.Width; k++)
                    {
                        int targetCol = sub + k;
                        if (covered.Contains((row, targetCol))) continue;

                        var match = coursesHere.FirstOrDefault(c => c.SubColumnIdx == k);
                        if (match == null)
                        {
                            var emptyBorder = MakeEmptyClickableBorder(vm, dg.Day, p, g, k);
                            Grid.SetRow(emptyBorder, row);
                            Grid.SetColumn(emptyBorder, targetCol);
                            RootGrid.Children.Add(emptyBorder);
                        }
                        else
                        {
                            var bg = GradeToBrushConverter.BrushFor(g);
                            var border = MakeChipBorder(
                                match.Assignment,
                                bg,
                                vm.CrossLinkLabels.GetValueOrDefault(match.Assignment.CourseId));
                            if (vm.SelectedCell == new UnifiedCellKey(dg.Day, p, g, k))
                            {
                                border.BorderBrush = SelectedBorder;
                                border.BorderThickness = new Thickness(2);
                            }
                            border.Tag = (dg.Day, p, g, k, match.Assignment);
                            border.MouseLeftButtonDown += OnCellClicked;
                            if (EnableCrossHover)
                            {
                                border.MouseEnter += OnCourseMouseEnter;
                                border.MouseLeave += OnCourseMouseLeave;
                            }
                            border.Cursor = System.Windows.Input.Cursors.Hand;
                            Grid.SetRow(border, row);
                            Grid.SetColumn(border, targetCol);
                            if (match.Assignment.RowSpan > 1)
                                Grid.SetRowSpan(border, match.Assignment.RowSpan);
                            RootGrid.Children.Add(border);
                            for (int dr = 1; dr < match.Assignment.RowSpan; dr++)
                                covered.Add((row + dr, targetCol));
                        }
                    }
                    sub += col.Width;
                }
            }
        }
    }

    private void AddBorder(string text, int row, int col, int rowSpan, int colSpan,
        Brush bg, int fontSize, FontWeight weight, double? height = null)
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
        if (height.HasValue) border.Height = height.Value;
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        if (rowSpan > 1) Grid.SetRowSpan(border, rowSpan);
        if (colSpan > 1) Grid.SetColumnSpan(border, colSpan);
        RootGrid.Children.Add(border);
    }

    private static Border MakeChipBorder(CellAssignment a, Brush bg, string? crossLabel)
    {
        var root = new Grid();
        var panel = new StackPanel { Margin = new Thickness(1) };
        var nameText = string.IsNullOrEmpty(a.SectionLabel)
            ? a.CourseName
            : $"{a.CourseName}·{a.SectionLabel}";
        panel.Children.Add(new TextBlock
        {
            Text = nameText,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = a.ProfessorId,
            FontSize = 8,
            TextAlignment = TextAlignment.Center,
        });
        if (!string.IsNullOrEmpty(a.RoomsLabel))
            panel.Children.Add(new TextBlock
            {
                Text = a.RoomsLabel,
                FontSize = 8,
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.DarkSlateGray,
            });
        root.Children.Add(panel);
        return new Border
        {
            BorderBrush = CellBorder,
            BorderThickness = new Thickness(0.5),
            Background = bg,
            Child = root,
            ToolTip = string.IsNullOrWhiteSpace(crossLabel) ? null : $"크로스: {crossLabel}",
        };
    }

    private Border MakeEmptyClickableBorder(UnifiedTimetableViewModel vm, int day, int period, int grade, int subColumnIdx)
    {
        var bg = EmptyBg;
        var borderBrush = CellBorder;
        string? tooltip = null;
        if (vm.EditStates.TryGetValue(new UnifiedCellKey(day, period, grade, subColumnIdx), out var state))
        {
            bg = state.State switch
            {
                ManualMoveCellState.Movable => MoveAllowedBg,
                ManualMoveCellState.Warning => MoveWarningBg,
                ManualMoveCellState.Blocked => MoveBlockedBg,
                _ => EmptyBg,
            };
            if (state.State == ManualMoveCellState.Blocked)
                borderBrush = MoveBlockedBorder;
            tooltip = state.Reason;
        }
        var border = new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0.5),
            Background = bg,
            Tag = (day, period, grade, subColumnIdx, (CellAssignment?)null),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tooltip,
        };
        border.MouseLeftButtonDown += OnCellClicked;
        return border;
    }

    private void OnCellClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not ValueTuple<int, int, int, int, CellAssignment?> tup) return;
        CellClicked?.Invoke(this, new CellClickedEventArgs(tup.Item1, tup.Item2, tup.Item3, tup.Item4, tup.Item5));
        e.Handled = true;
    }

    private void OnCourseMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border border
            || border.Tag is not ValueTuple<int, int, int, int, CellAssignment?> tup
            || tup.Item5 == null
            || border.Child is not Grid root)
            return;

        RemoveCrossBadge(root);
        var args = new CellClickedEventArgs(tup.Item1, tup.Item2, tup.Item3, tup.Item4, tup.Item5);
        var state = CrossHoverEvaluator?.Invoke(args) ?? CrossHoverState.Hidden();
        border.ToolTip = string.IsNullOrWhiteSpace(state.Reason) ? null : state.Reason;
        if (!state.CanCreate) return;

        var badge = new Button
        {
            Content = "+",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Tag = args,
        };
        badge.Click += OnCrossBadgeClick;
        root.Children.Add(badge);
    }

    private static void OnCourseMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border border && border.Child is Grid root)
            RemoveCrossBadge(root);
    }

    private void OnCrossBadgeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not CellClickedEventArgs args) return;
        CrossAddRequested?.Invoke(this, args);
        e.Handled = true;
    }

    private static void RemoveCrossBadge(Grid root)
    {
        var badge = root.Children.OfType<Button>().FirstOrDefault();
        if (badge != null)
            root.Children.Remove(badge);
    }

    private static string DayName(int day) => day switch
    {
        0 => "월", 1 => "화", 2 => "수", 3 => "목", 4 => "금",
        _ => "?",
    };
}
