using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using TimetableScheduler.Domain;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.Wpf.Converters;

namespace TimetableScheduler.Wpf.Controls;

public partial class UnifiedTimetableControl : UserControl
{
    private const string DragDataFormat = "TimetableScheduler.UnifiedCellDrag";
    internal const int MaxCardTitleLinesPerBlock = 2;
    internal const int MaxCardProfessorLinesPerBlock = 2;
    internal const int MaxCardRoomLinesPerBlock = 1;
    internal const double CardTitleLineHeight = 13;
    internal const double CardMetaLineHeight = 10;
    private static readonly Brush EmptyBg = Brushes.White;
    private static readonly Brush LunchBg = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7));
    private static readonly Brush HeaderBg = new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xF8));
    private static readonly Brush CellBorder = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF3));
    private static readonly Brush DayBoundaryBorder = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
    private static readonly Brush CourseBlockBorder = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));
    private static readonly Brush CourseTitleText = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
    private static readonly Brush CourseMetaText = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
    private static readonly Brush MoveAllowedBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
    private static readonly Brush MoveWarningBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1));
    private static readonly Brush MoveBlockedBg = new SolidColorBrush(Color.FromRgb(0xFD, 0xE7, 0xE9));
    private static readonly Brush MoveBlockedBorder = new SolidColorBrush(Color.FromRgb(0xBA, 0x1A, 0x1A));
    private static readonly Brush SelectedBorder = new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8));

    [Flags]
    private enum HoverBadgeKind
    {
        None = 0,
        Swap = 1,
        Cross = 2,
    }

    static UnifiedTimetableControl()
    {
        LunchBg.Freeze();
        HeaderBg.Freeze();
        CellBorder.Freeze();
        DayBoundaryBorder.Freeze();
        CourseBlockBorder.Freeze();
        CourseTitleText.Freeze();
        CourseMetaText.Freeze();
        MoveAllowedBg.Freeze();
        MoveWarningBg.Freeze();
        MoveBlockedBg.Freeze();
        MoveBlockedBorder.Freeze();
        SelectedBorder.Freeze();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

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

    public sealed class CellDropMoveEventArgs : EventArgs
    {
        public CellClickedEventArgs Source { get; }
        public CellClickedEventArgs Target { get; }

        public CellDropMoveEventArgs(CellClickedEventArgs source, CellClickedEventArgs target)
        {
            Source = source;
            Target = target;
        }
    }

    private sealed record CellDragData(CellClickedEventArgs Source, int GrabbedPeriodOffset);

    public event EventHandler<CellClickedEventArgs>? CellClicked;
    public event EventHandler<CellClickedEventArgs>? CrossAddRequested;
    public event EventHandler<CellClickedEventArgs>? SwapRequested;
    public event EventHandler<CellDropMoveEventArgs>? CrossDropRequested;
    public event EventHandler<CellDropMoveEventArgs>? SwapDropRequested;
    public event EventHandler<CellDropMoveEventArgs>? DropMoveRequested;
    public event EventHandler<CellClickedEventArgs>? DragMovePreviewStarted;
    public event EventHandler? DragMovePreviewEnded;

    public static readonly DependencyProperty PeriodRowMinHeightProperty =
        DependencyProperty.Register(
            nameof(PeriodRowMinHeight),
            typeof(double),
            typeof(UnifiedTimetableControl),
            new PropertyMetadata(64.0, OnLayoutMetricsChanged));

    public static readonly DependencyProperty LunchRowHeightProperty =
        DependencyProperty.Register(
            nameof(LunchRowHeight),
            typeof(double),
            typeof(UnifiedTimetableControl),
            new PropertyMetadata(22.0, OnLayoutMetricsChanged));

    public static readonly DependencyProperty NightSeparatorThicknessProperty =
        DependencyProperty.Register(
            nameof(NightSeparatorThickness),
            typeof(double),
            typeof(UnifiedTimetableControl),
            new PropertyMetadata(3.0, OnLayoutMetricsChanged));

    public static readonly DependencyProperty DayColumnWidthProperty =
        DependencyProperty.Register(
            nameof(DayColumnWidth),
            typeof(double),
            typeof(UnifiedTimetableControl),
            new PropertyMetadata(80.0, OnLayoutMetricsChanged));

    public double PeriodRowMinHeight
    {
        get => (double)GetValue(PeriodRowMinHeightProperty);
        set => SetValue(PeriodRowMinHeightProperty, value);
    }

    public double LunchRowHeight
    {
        get => (double)GetValue(LunchRowHeightProperty);
        set => SetValue(LunchRowHeightProperty, value);
    }

    public double NightSeparatorThickness
    {
        get => (double)GetValue(NightSeparatorThicknessProperty);
        set => SetValue(NightSeparatorThicknessProperty, value);
    }

    public double DayColumnWidth
    {
        get => (double)GetValue(DayColumnWidthProperty);
        set => SetValue(DayColumnWidthProperty, value);
    }

    public bool EnableCrossHover { get; set; }
    public Func<CellClickedEventArgs, CrossHoverState>? CrossHoverEvaluator { get; set; }
    public Func<CellDropMoveEventArgs, CrossHoverState>? CrossDropHoverEvaluator { get; set; }
    public bool EnableSwapHover { get; set; }
    public Func<CellClickedEventArgs, SwapHoverState>? SwapHoverEvaluator { get; set; }
    public Func<CellDropMoveEventArgs, SwapHoverState>? SwapDropHoverEvaluator { get; set; }
    public bool EnableDragDropMove { get; set; }
    public Func<CellDropMoveEventArgs, bool>? DropMoveEvaluator { get; set; }

    private Point? _dragStartPoint;
    private CellClickedEventArgs? _dragSource;
    private bool _suppressNextClick;
    private Border? _dragSourceBorder;
    private double _dragSourceOriginalOpacity = 1.0;
    private Point _dragClickOffset;
    private int _grabbedPeriodOffset;
    private DragPreviewAdorner? _dragPreviewAdorner;
    private AdornerLayer? _dragPreviewLayer;
    private Border? _activeBadgeBorder;
    private CellClickedEventArgs? _activeBadgeTarget;
    private HoverBadgeKind _activeBadgeKind;
    private bool _activeBadgeIsDrag;

    internal bool HasActiveHoverBadgeForTests => _activeBadgeTarget != null;
    internal bool HasDragSourceForTests => _dragSource != null;
    internal void SetDragSourceForTests(CellClickedEventArgs source) => _dragSource = source;
    internal bool ShowHoverBadgeForTests(string assignmentId, bool cross, bool swap)
    {
        var border = RootGrid.Children
            .OfType<Border>()
            .FirstOrDefault(b =>
                b.Tag is ValueTuple<int, int, int, int, CellAssignment?> tag
                && string.Equals(tag.Item5?.AssignmentId, assignmentId, StringComparison.Ordinal));
        if (border == null || TryBuildCurrentArgs(border, allowEmpty: false) is not { } args)
            return false;

        RefreshCrossSwapBadges(
            border,
            args,
            cross ? CrossHoverState.Available() : CrossHoverState.Hidden(),
            swap ? SwapHoverState.Available() : SwapHoverState.Hidden(),
            isDrag: false);
        return true;
    }

    public UnifiedTimetableControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private static void OnLayoutMetricsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UnifiedTimetableControl control && control.DataContext is UnifiedTimetableViewModel vm)
            control.Rebuild(vm);
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
        ClearAllCrossSwapBadges();
        ClearStaleDragState();
        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        // 2 header rows + period rows.
        for (int i = 0; i < 2; i++)
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var period in vm.Periods)
        {
            if (period == 5)
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LunchRowHeight) });
            else
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = PeriodRowMinHeight });
        }

        // 2 label cols (period, time) + sum of all grade widths
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });

        // Track column index per day_group → column offset
        // Also column index for each (day, gradeColIdx) for cell placement
        var dayColStart = new Dictionary<int, int>();   // day → starting col index (incl. label cols offset)
        int curCol = 2;
        foreach (var dg in vm.DayGroups)
        {
            dayColStart[dg.Day] = curCol;
            for (int k = 0; k < dg.TotalWidth; k++)
                RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DayColumnWidth) });
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
            AddDayBoundary(startCol);

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

        int totalDayCols = vm.DayGroups.Sum(dg => dg.TotalWidth);

        foreach (var p in vm.Periods)
        {
            int row = GridRowForPeriod(p);

            if (p == 5)
            {
                // Single merged lunch row spanning all columns
                AddLunchBorder(row, 2 + totalDayCols);
                continue;
            }

            AddBorder(p.ToString(), row, 0, 1, 1, HeaderBg, 11, FontWeights.Normal);
            AddBorder($"{8 + p:00}:00~{9 + p:00}:00", row, 1, 1, 1, HeaderBg, 9, FontWeights.Normal);

            foreach (var dg in vm.DayGroups)
            {
                int startCol = dayColStart[dg.Day];
                int sub = startCol;
                foreach (var col in dg.Grades)
                {
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
                            var crossDisplayKey = UnifiedTimetableViewModel.BuildManualCrossDisplayKey(
                                match.Assignment,
                                dg.Day,
                                p);
                            var border = MakeChipBorder(
                                match.Assignment,
                                bg,
                                vm.CrossLinkLabels.GetValueOrDefault(crossDisplayKey));
                            var isSelected = vm.SelectedCell == new UnifiedCellKey(dg.Day, p, g, k);
                            border.Tag = (dg.Day, p, g, k, match.Assignment);
                            border.AllowDrop = EnableDragDropMove;
                            border.PreviewMouseLeftButtonDown += OnDragSourceMouseDown;
                            border.MouseMove += OnDragSourceMouseMove;
                            border.DragOver += OnCellDragOver;
                            border.Drop += OnCellDrop;
                            border.MouseLeftButtonDown += OnCellClicked;
                            if (EnableCrossHover || EnableSwapHover)
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
                            if (!isSelected
                                && vm.EditStates.TryGetValue(new UnifiedCellKey(dg.Day, p, g, k), out var moveState)
                                && moveState.State is ManualMoveCellState.Movable
                                    or ManualMoveCellState.Warning
                                    or ManualMoveCellState.Blocked)
                            {
                                AddMoveStateOverlay(row, targetCol, match.Assignment.RowSpan, moveState);
                            }
                            if (isSelected)
                                AddSelectedOverlay(row, targetCol, match.Assignment.RowSpan);
                            for (int dr = 1; dr < match.Assignment.RowSpan; dr++)
                                covered.Add((row + dr, targetCol));
                        }
                    }
                    sub += col.Width;
                }
            }
        }

        AddNightSeparatorLine(NightSeparatorRow, 2 + totalDayCols);
    }

    private void AddSelectedOverlay(int row, int column, int rowSpan)
    {
        var overlay = new Border
        {
            BorderBrush = SelectedBorder,
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(2),
            IsHitTestVisible = false,
        };
        Grid.SetRow(overlay, row);
        Grid.SetColumn(overlay, column);
        if (rowSpan > 1)
            Grid.SetRowSpan(overlay, rowSpan);
        Panel.SetZIndex(overlay, 50);
        RootGrid.Children.Add(overlay);
    }

    private void AddMoveStateOverlay(int row, int column, int rowSpan, EditCellState state)
    {
        var background = state.State switch
        {
            ManualMoveCellState.Movable => MoveAllowedBg,
            ManualMoveCellState.Warning => MoveWarningBg,
            ManualMoveCellState.Blocked => MoveBlockedBg,
            _ => Brushes.Transparent,
        };
        var overlay = new Border
        {
            BorderBrush = state.State == ManualMoveCellState.Blocked ? MoveBlockedBorder : Brushes.Transparent,
            BorderThickness = state.State == ManualMoveCellState.Blocked ? new Thickness(2) : new Thickness(0),
            Background = background,
            Opacity = 0.62,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(2),
            IsHitTestVisible = false,
            ToolTip = NullIfBlank(state.Reason),
        };
        Grid.SetRow(overlay, row);
        Grid.SetColumn(overlay, column);
        if (rowSpan > 1)
            Grid.SetRowSpan(overlay, rowSpan);
        Panel.SetZIndex(overlay, 45);
        RootGrid.Children.Add(overlay);
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
        RootGrid.Children.Add(border);
    }

    private void AddNightSeparatorLine(int row, int colSpan)
    {
        var border = new Border
        {
            BorderBrush = DayBoundaryBorder,
            BorderThickness = new Thickness(0, NightSeparatorThickness, 0, 0),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, 0);
        Grid.SetColumnSpan(border, colSpan);
        Panel.SetZIndex(border, 30);
        RootGrid.Children.Add(border);
    }

    internal static int GridRowForPeriod(int period) =>
        1 + period;

    internal static int NightSeparatorRow => GridRowForPeriod(SchedulePeriods.FirstNightPeriod);

    internal static Border MakeChipBorder(CellAssignment a, Brush bg, string? crossLabel)
    {
        var titleLines = CardTitleLinesFor(a.RowSpan);
        var professorLines = CardProfessorLinesFor(a.RowSpan);
        var roomLines = CardRoomLinesFor(a.RowSpan);
        var content = new StackPanel
        {
            Margin = new Thickness(5, 7, 5, 5),
        };

        var nameText = a.TitleLabel;

        content.Children.Add(new TextBlock
        {
            Text = nameText,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = CourseTitleText,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = CardTitleLineHeight * titleLines,
            LineHeight = CardTitleLineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        });
        if (!string.IsNullOrEmpty(a.ProfessorLine))
            content.Children.Add(new TextBlock
            {
                Text = a.ProfessorLine,
                FontSize = 7.5,
                FontWeight = FontWeights.Normal,
                Foreground = CourseMetaText,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = CardMetaLineHeight * professorLines,
                LineHeight = CardMetaLineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 2, 0, 0),
            });
        if (!string.IsNullOrEmpty(a.RoomsLabel))
            content.Children.Add(new TextBlock
            {
                Text = FormatRoomsForCard(a.RoomsLabel),
                FontSize = 7.5,
                FontWeight = FontWeights.Normal,
                TextAlignment = TextAlignment.Center,
                TextWrapping = roomLines == 1 ? TextWrapping.NoWrap : TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = CardMetaLineHeight * roomLines,
                LineHeight = CardMetaLineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = CourseMetaText,
            });

        var cardBg = LightenBrush(bg);
        var accentBg = DarkenBrush(bg);
        var card = new Border
        {
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Background = cardBg,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(2),
            ClipToBounds = true,
            Child = new Grid
            {
                Children =
                {
                    new Border
                    {
                        Height = 4,
                        Background = accentBg,
                        CornerRadius = new CornerRadius(5, 5, 0, 0),
                        VerticalAlignment = VerticalAlignment.Top,
                    },
                    content,
                },
            },
        };

        var overlay = new Grid();
        overlay.Children.Add(card);

        return new Border
        {
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Child = overlay,
            ToolTip = NullIfBlank(crossLabel) is { } label ? $"크로스: {label}" : null,
        };
    }

    internal static int CardTitleLinesFor(int rowSpan) =>
        SafeCardRowSpan(rowSpan) * MaxCardTitleLinesPerBlock;

    internal static int CardProfessorLinesFor(int rowSpan) =>
        SafeCardRowSpan(rowSpan) * MaxCardProfessorLinesPerBlock;

    internal static int CardRoomLinesFor(int rowSpan) =>
        SafeCardRowSpan(rowSpan) * MaxCardRoomLinesPerBlock;

    private static int SafeCardRowSpan(int rowSpan) => Math.Max(1, rowSpan);

    internal static string FormatRoomsForCard(string? roomsLabel)
    {
        if (string.IsNullOrWhiteSpace(roomsLabel))
            return string.Empty;

        var lines = roomsLabel
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        return string.Join(", ", lines);
    }

    private static string AddWrapHints(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        return text
            .Replace(" - ", "\u200B - \u200B")
            .Replace("/", "/\u200B")
            .Replace("·", "·\u200B")
            .Replace(",", ",\u200B")
            .Replace("프로그래밍", "프로그래밍\u200B")
            .Replace("시스템", "시스템\u200B")
            .Replace("데이터", "데이터\u200B")
            .Replace("서버", "서버\u200B")
            .Replace("소프트웨어", "소프트웨어\u200B")
            .Replace("컴퓨터", "컴퓨터\u200B")
            .Replace("알고리즘", "알고리즘\u200B")
            .Replace("네트워크", "네트워크\u200B")
            .Replace("데이터베이스", "데이터베이스\u200B");
    }

    private static string FormatSectionForCard(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return string.Empty;

        return section.Trim().Replace("분반", "").Trim();
    }

    private static Brush DarkenBrush(Brush brush)
    {
        if (brush is SolidColorBrush solid)
        {
            var color = solid.Color;
            var accent = Color.FromRgb(
                DarkenChannel(color.R),
                DarkenChannel(color.G),
                DarkenChannel(color.B));
            var result = new SolidColorBrush(accent);
            result.Freeze();
            return result;
        }

        return CourseBlockBorder;

        static byte DarkenChannel(byte channel) =>
            (byte)Math.Max(0, Math.Round(channel * 0.72));
    }

    private static Brush LightenBrush(Brush brush)
    {
        if (brush is SolidColorBrush solid)
        {
            var color = solid.Color;
            var softened = Color.FromRgb(
                LightenChannel(color.R),
                LightenChannel(color.G),
                LightenChannel(color.B));
            var result = new SolidColorBrush(softened);
            result.Freeze();
            return result;
        }

        return brush;

        static byte LightenChannel(byte channel) =>
            (byte)Math.Min(255, Math.Round(channel + (255 - channel) * 0.20));
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
        Grid.SetRowSpan(border, RootGrid.RowDefinitions.Count);
        Panel.SetZIndex(border, 20);
        RootGrid.Children.Add(border);
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
            ToolTip = NullIfBlank(tooltip),
        };
        border.AllowDrop = EnableDragDropMove;
        border.DragOver += OnCellDragOver;
        border.Drop += OnCellDrop;
        border.MouseLeftButtonDown += OnCellClicked;
        return border;
    }

    private void OnDragSourceMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _dragSource = TryBuildCurrentArgs(sender, allowEmpty: false);
        if (sender is Border border)
        {
            _dragClickOffset = e.GetPosition(border);
            var rowSpan = Math.Max(1, _dragSource?.Assignment?.RowSpan ?? 1);
            var periodHeight = border.ActualHeight / rowSpan;
            _grabbedPeriodOffset = periodHeight > 0
                ? Math.Clamp((int)(_dragClickOffset.Y / periodHeight), 0, rowSpan - 1)
                : 0;
        }
    }

    private void OnDragSourceMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!EnableDragDropMove
            || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed
            || _dragStartPoint is not Point start
            || _dragSource == null
            || _dragSource.Assignment == null
            || !IsCurrentCellArgs(_dragSource))
            return;

        var current = e.GetPosition(this);
        var horizontalThreshold = Math.Max(2.0, SystemParameters.MinimumHorizontalDragDistance * 0.65);
        var verticalThreshold = Math.Max(2.0, SystemParameters.MinimumVerticalDragDistance * 0.65);
        if (Math.Abs(current.X - start.X) < horizontalThreshold
            && Math.Abs(current.Y - start.Y) < verticalThreshold)
            return;

        if (sender is Border border)
            BeginDragVisuals(border, current);
        try
        {
            DragMovePreviewStarted?.Invoke(this, _dragSource);
            var data = new DataObject(DragDataFormat, new CellDragData(_dragSource, _grabbedPeriodOffset));
            _suppressNextClick = true;
            DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        }
        finally
        {
            DragMovePreviewEnded?.Invoke(this, EventArgs.Empty);
            ClearDragState();
            Dispatcher.BeginInvoke(
                () => _suppressNextClick = false,
                DispatcherPriority.Background);
        }
    }

    private void OnCellDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        var cursorPosition = e.GetPosition(this);
        UpdateDragPreview(cursorPosition);
        AutoScrollDuringDrag(e);
        if (EnableDragDropMove
            && TryGetDragData(e.Data, out var source, out var grabbedPeriodOffset)
            && IsCurrentCellArgs(source)
            && TryBuildCurrentArgs(sender, allowEmpty: true) is { } rawTarget)
        {
            var target = AdjustMoveTarget(rawTarget, grabbedPeriodOffset);
            var moveArgs = new CellDropMoveEventArgs(source, target);
            var canDrop = rawTarget.Assignment == null
                && (DropMoveEvaluator?.Invoke(moveArgs) ?? true);
            var rawDropArgs = new CellDropMoveEventArgs(source, rawTarget);
            var crossState = source.Assignment != null && rawTarget.Assignment != null && !IsSameCellArgs(source, rawTarget)
                ? CrossDropHoverEvaluator?.Invoke(rawDropArgs) ?? CrossHoverState.Hidden()
                : CrossHoverState.Hidden();
            var swapState = source.Assignment != null && rawTarget.Assignment != null && !IsSameCellArgs(source, rawTarget)
                ? SwapDropHoverEvaluator?.Invoke(rawDropArgs) ?? SwapHoverState.Hidden()
                : SwapHoverState.Hidden();

            if (sender is Border border
                && source.Assignment != null
                && rawTarget.Assignment != null)
            {
                if (IsSameCellArgs(source, rawTarget))
                {
                    ClearActiveBadge();
                }
                else
                    RefreshDragHoverBadges(border, rawTarget, crossState, swapState);
            }
            else
            {
                ClearActiveBadge();
            }

            if (canDrop && TryGetMoveTargetTopLeft(target, out var targetTopLeft))
                UpdateDragPreview(cursorPosition, targetTopLeft);

            e.Effects = canDrop || crossState.CanCreate || swapState.CanSwap
                ? DragDropEffects.Move
                : DragDropEffects.None;
        }
        else
        {
            ClearActiveBadge();
        }
        e.Handled = true;
    }

    private static CellClickedEventArgs AdjustMoveTarget(CellClickedEventArgs hoverTarget, int grabbedPeriodOffset) =>
        new(
            hoverTarget.Day,
            hoverTarget.Period - grabbedPeriodOffset,
            hoverTarget.Grade,
            hoverTarget.SubColumnIdx,
            null);

    private bool TryGetMoveTargetTopLeft(CellClickedEventArgs target, out Point topLeft)
    {
        topLeft = default;
        var border = RootGrid.Children
            .OfType<Border>()
            .FirstOrDefault(candidate =>
                candidate.Tag is ValueTuple<int, int, int, int, CellAssignment?> tag
                && tag.Item1 == target.Day
                && tag.Item2 == target.Period
                && tag.Item3 == target.Grade
                && tag.Item4 == target.SubColumnIdx
                && tag.Item5 == null);
        if (border == null)
            return false;

        topLeft = border.TranslatePoint(new Point(0, 0), this);
        return true;
    }

    private static bool TryGetDragData(IDataObject dataObject, out CellClickedEventArgs source, out int grabbedPeriodOffset)
    {
        source = null!;
        grabbedPeriodOffset = 0;
        if (!dataObject.GetDataPresent(DragDataFormat)
            || dataObject.GetData(DragDataFormat) is not CellDragData dragData)
            return false;

        source = dragData.Source;
        grabbedPeriodOffset = dragData.GrabbedPeriodOffset;
        return true;
    }

    private void OnCellDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (EnableDragDropMove
                && TryGetDragData(e.Data, out var source, out var grabbedPeriodOffset)
                && IsCurrentCellArgs(source)
                && TryBuildCurrentArgs(sender, allowEmpty: true) is { } rawTarget
                && rawTarget.Assignment == null
                && AdjustMoveTarget(rawTarget, grabbedPeriodOffset) is { } target
                && (DropMoveEvaluator?.Invoke(new CellDropMoveEventArgs(source, target)) ?? true))
            {
                DropMoveRequested?.Invoke(this, new CellDropMoveEventArgs(source, target));
                _suppressNextClick = true;
                Dispatcher.BeginInvoke(
                    () => _suppressNextClick = false,
                    DispatcherPriority.Background);
                e.Handled = true;
            }
        }
        finally
        {
            ClearDragState();
        }
    }

    private static CellClickedEventArgs? TryBuildArgs(object sender)
    {
        if (sender is not Border b || b.Tag is not ValueTuple<int, int, int, int, CellAssignment?> tup)
            return null;
        return new CellClickedEventArgs(tup.Item1, tup.Item2, tup.Item3, tup.Item4, tup.Item5);
    }

    private CellClickedEventArgs? TryBuildCurrentArgs(object sender, bool allowEmpty)
    {
        var args = TryBuildArgs(sender);
        if (args == null)
        {
            return null;
        }
        if (!allowEmpty && args.Assignment == null)
        {
            return null;
        }
        var isCurrent = IsCurrentCellArgs(args);
        return isCurrent ? args : null;
    }

    private bool IsCurrentCellArgs(CellClickedEventArgs args)
    {
        if (DataContext is not UnifiedTimetableViewModel vm)
        {
            return false;
        }
        var current = vm.Cells.FirstOrDefault(c =>
            c.Day == args.Day
            && c.Period == args.Period
            && c.Grade == args.Grade
            && c.SubColumnIdx == args.SubColumnIdx);

        if (args.Assignment == null)
        {
            var emptyCurrent = current == null && IsRenderedEmptySlot(vm, args);
            return emptyCurrent;
        }
        if (current == null)
        {
            return false;
        }

        var assignment = current.Assignment;
        if (!string.IsNullOrWhiteSpace(assignment.AssignmentId)
            || !string.IsNullOrWhiteSpace(args.Assignment.AssignmentId))
        {
            return string.Equals(assignment.AssignmentId, args.Assignment.AssignmentId, StringComparison.Ordinal);
        }

        var result = assignment.CourseId == args.Assignment.CourseId
            && assignment.Section == args.Assignment.Section
            && assignment.ProfessorId == args.Assignment.ProfessorId
            && assignment.RowSpan == args.Assignment.RowSpan
            && assignment.Rooms.SequenceEqual(args.Assignment.Rooms);
        return result;
    }

    private static bool IsRenderedEmptySlot(UnifiedTimetableViewModel vm, CellClickedEventArgs args)
    {
        var dayGroup = vm.DayGroups.FirstOrDefault(dg => dg.Day == args.Day);
        if (dayGroup == null) return false;
        var gradeColumn = dayGroup.Grades.FirstOrDefault(g => g.Grade == args.Grade);
        return gradeColumn != null
            && args.SubColumnIdx >= 0
            && args.SubColumnIdx < gradeColumn.Width;
    }

    private void ClearStaleDragState()
    {
        if (_dragSource != null && !IsCurrentCellArgs(_dragSource))
            ClearDragState();
    }

    private void ClearDragState()
    {
        ClearActiveBadge();
        RestoreDragVisuals();
        _dragStartPoint = null;
        _dragSource = null;
        _grabbedPeriodOffset = 0;
    }

    private void BeginDragVisuals(Border sourceBorder, Point currentPosition)
    {
        _dragSourceBorder = sourceBorder;
        _dragSourceOriginalOpacity = sourceBorder.Opacity;
        sourceBorder.Opacity = 0.45;

        _dragPreviewLayer = AdornerLayer.GetAdornerLayer(this);
        if (_dragPreviewLayer == null)
            return;

        _dragPreviewAdorner = new DragPreviewAdorner(this, sourceBorder, sourceBorder.ActualWidth, sourceBorder.ActualHeight, _dragClickOffset);
        _dragPreviewLayer.Add(_dragPreviewAdorner);
        UpdateDragPreview(currentPosition);
    }

    private void UpdateDragPreview(Point position, Point? snappedTopLeft = null)
    {
        _dragPreviewAdorner?.Update(position, snappedTopLeft);
    }

    private void RestoreDragVisuals()
    {
        if (_dragSourceBorder != null)
            _dragSourceBorder.Opacity = _dragSourceOriginalOpacity;
        if (_dragPreviewAdorner != null && _dragPreviewLayer != null)
            _dragPreviewLayer.Remove(_dragPreviewAdorner);

        _dragSourceBorder = null;
        _dragSourceOriginalOpacity = 1.0;
        _dragPreviewAdorner = null;
        _dragPreviewLayer = null;
    }

    private void AutoScrollDuringDrag(DragEventArgs e)
    {
        var viewer = FindVisualChild<ScrollViewer>(this);
        if (viewer == null) return;

        var p = e.GetPosition(viewer);
        const double edge = 32.0;
        const double step = 18.0;
        if (p.Y < edge)
            viewer.ScrollToVerticalOffset(Math.Max(0, viewer.VerticalOffset - step));
        else if (p.Y > viewer.ViewportHeight - edge)
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset + step);

        if (p.X < edge)
            viewer.ScrollToHorizontalOffset(Math.Max(0, viewer.HorizontalOffset - step));
        else if (p.X > viewer.ViewportWidth - edge)
            viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + step);
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }
        return null;
    }

    private void OnCellClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            e.Handled = true;
            return;
        }

        var args = TryBuildCurrentArgs(sender, allowEmpty: true);
        if (args == null)
        {
            return;
        }
        CellClicked?.Invoke(this, args);
        e.Handled = true;
    }

    private void OnCourseMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        if (TryBuildCurrentArgs(sender, allowEmpty: false) is not { } args)
        {
            return;
        }

        if (args.Assignment == null)
        {
            return;
        }

        var crossState = EnableCrossHover ? CrossHoverEvaluator?.Invoke(args) ?? CrossHoverState.Hidden() : CrossHoverState.Hidden();
        var swapState = EnableSwapHover ? SwapHoverEvaluator?.Invoke(args) ?? SwapHoverState.Hidden() : SwapHoverState.Hidden();
        RefreshCrossSwapBadges(
            border,
            args,
            crossState,
            swapState,
            isDrag: false);
    }

    private void RefreshDragHoverBadges(
        Border border,
        CellClickedEventArgs target,
        CrossHoverState crossState,
        SwapHoverState swapState)
    {
        var kind = BadgeKind(crossState, swapState);
        if (kind == HoverBadgeKind.None)
        {
            ClearActiveBadge();
            return;
        }

        if (IsSameActiveBadge(border, target, kind, isDrag: true))
            return;

        RefreshCrossSwapBadges(border, target, crossState, swapState, isDrag: true);
    }

    private static HoverBadgeKind BadgeKind(CrossHoverState crossState, SwapHoverState swapState)
    {
        var kind = HoverBadgeKind.None;
        if (swapState.CanSwap) kind |= HoverBadgeKind.Swap;
        if (crossState.CanCreate) kind |= HoverBadgeKind.Cross;
        return kind;
    }

    private void RefreshCrossSwapBadges(
        Border border,
        CellClickedEventArgs args,
        CrossHoverState crossState,
        SwapHoverState swapState,
        bool isDrag)
    {
        if (args.Assignment == null)
        {
            return;
        }
        var kind = BadgeKind(crossState, swapState);
        if (kind == HoverBadgeKind.None)
        {
            ClearActiveBadge();
            return;
        }

        if (IsSameActiveBadge(border, args, kind, isDrag))
        {
            return;
        }

        ClearActiveBadge();

        Grid overlay;
        if (border.Child is Grid g)
        {
            overlay = g;
            RemoveHoverBadges(overlay);
        }
        else if (border.Child is StackPanel sp)
        {
            overlay = new Grid();
            border.Child = null;
            overlay.Children.Add(sp);
            border.Child = overlay;
        }
        else
        {
            return;
        }

        border.ToolTip = NullIfBlank(crossState.CanCreate ? crossState.Reason : swapState.Reason ?? crossState.Reason);

        if (swapState.CanSwap)
        {
            var swapBadge = new Button
            {
                Content = "⇄",
                Width = 22,
                Height = 18,
                Padding = new Thickness(0),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2, 2, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x55, 0x6B, 0x2F)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                ToolTip = NullIfBlank(swapState.Reason),
                Tag = args,
                AllowDrop = true,
            };
            swapBadge.DragOver += OnSwapBadgeDragOver;
            swapBadge.Drop += OnSwapBadgeDrop;
            swapBadge.Click += OnSwapBadgeClick;
            overlay.Children.Add(swapBadge);
        }

        _activeBadgeBorder = border;
        _activeBadgeTarget = args;
        _activeBadgeKind = kind;
        _activeBadgeIsDrag = isDrag;

        if (!crossState.CanCreate)
        {
            return;
        }

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
            ToolTip = NullIfBlank(crossState.Reason),
            Tag = args,
            AllowDrop = true,
        };
        badge.DragOver += OnCrossBadgeDragOver;
        badge.Drop += OnCrossBadgeDrop;
        badge.Click += OnCrossBadgeClick;
        overlay.Children.Add(badge);
    }

    private void OnCourseMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragSource != null)
            return;

        ClearActiveBadge();
    }

    private bool IsSameActiveBadge(Border border, CellClickedEventArgs target, HoverBadgeKind kind, bool isDrag)
    {
        return ReferenceEquals(_activeBadgeBorder, border)
            && _activeBadgeTarget != null
            && IsSameCellArgs(_activeBadgeTarget, target)
            && _activeBadgeKind == kind
            && _activeBadgeIsDrag == isDrag;
    }

    private void ClearActiveBadge()
    {
        if (_activeBadgeBorder != null)
            ClearCrossSwapBadges(_activeBadgeBorder);
        ClearAllCrossSwapBadges();
        _activeBadgeBorder = null;
        _activeBadgeTarget = null;
        _activeBadgeKind = HoverBadgeKind.None;
        _activeBadgeIsDrag = false;
    }

    private static void ClearCrossSwapBadges(Border border)
    {
        if (border.Child is not Grid overlay) return;
        RemoveHoverBadges(overlay);
        if (overlay.Children.Count == 1 && overlay.Children[0] is StackPanel sp)
        {
            overlay.Children.Clear();
            border.Child = sp;
        }
    }

    private void OnCrossBadgeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button button
                || !TryGetCurrentBadgeArgs(button, out var args))
                return;
            CrossAddRequested?.Invoke(this, args);
            e.Handled = true;
        }
        finally
        {
            ClearActiveBadge();
        }
    }

    private void OnSwapBadgeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button button
                || !TryGetCurrentBadgeArgs(button, out var args))
                return;
            SwapRequested?.Invoke(this, args);
            e.Handled = true;
        }
        finally
        {
            ClearActiveBadge();
        }
    }

    private void OnCrossBadgeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanDropOnCrossBadge(sender, e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCrossBadgeDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (CanDropOnCrossBadge(sender, e)
                && TryGetCurrentDragSource(e, out var source)
                && sender is Button button
                && TryGetCurrentBadgeArgs(button, out var args))
            {
                CrossDropRequested?.Invoke(this, new CellDropMoveEventArgs(source, args));
                SuppressNextClick();
            }
        }
        finally
        {
            ClearDragState();
        }
    }

    private void ClearAllCrossSwapBadges()
    {
        foreach (var border in RootGrid.Children.OfType<Border>().ToList())
            ClearCrossSwapBadges(border);
    }

    private void OnSwapBadgeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanDropOnSwapBadge(sender, e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSwapBadgeDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (CanDropOnSwapBadge(sender, e)
                && TryGetCurrentDragSource(e, out var source)
                && sender is Button button
                && TryGetCurrentBadgeArgs(button, out var args))
            {
                SwapDropRequested?.Invoke(this, new CellDropMoveEventArgs(source, args));
                SuppressNextClick();
            }
        }
        finally
        {
            ClearDragState();
        }
    }

    private bool CanDropOnCrossBadge(object sender, DragEventArgs e)
    {
        return TryGetCurrentDragSource(e, out var source)
            && sender is Button button
            && TryGetCurrentBadgeArgs(button, out var args)
            && !IsSameCellArgs(source, args);
    }

    private bool CanDropOnSwapBadge(object sender, DragEventArgs e)
    {
        return TryGetCurrentDragSource(e, out var source)
            && sender is Button button
            && TryGetCurrentBadgeArgs(button, out var args)
            && !IsSameCellArgs(source, args);
    }

    private bool TryGetCurrentBadgeArgs(Button button, out CellClickedEventArgs args)
    {
        args = null!;
        if (button.Tag is not CellClickedEventArgs current || !IsCurrentCellArgs(current))
            return false;
        args = current;
        return true;
    }

    private bool TryGetCurrentDragSource(DragEventArgs e, out CellClickedEventArgs source)
    {
        source = null!;
        if (!EnableDragDropMove
            || _dragSource?.Assignment == null
            || !IsCurrentCellArgs(_dragSource)
            || !TryGetDragData(e.Data, out var dragSource, out _)
            || dragSource.Assignment == null
            || !IsCurrentCellArgs(dragSource)
            || !IsSameCellArgs(dragSource, _dragSource))
        {
            return false;
        }

        source = dragSource;
        return true;
    }

    private static bool IsSameCellArgs(CellClickedEventArgs a, CellClickedEventArgs b)
    {
        return a.Day == b.Day
            && a.Period == b.Period
            && a.Grade == b.Grade
            && a.SubColumnIdx == b.SubColumnIdx
            && (string.IsNullOrWhiteSpace(a.Assignment?.AssignmentId)
                && string.IsNullOrWhiteSpace(b.Assignment?.AssignmentId)
                || string.Equals(a.Assignment?.AssignmentId, b.Assignment?.AssignmentId, StringComparison.Ordinal))
            && a.Assignment?.CourseId == b.Assignment?.CourseId
            && a.Assignment?.Section == b.Assignment?.Section
            && a.Assignment?.RowSpan == b.Assignment?.RowSpan
            && ((a.Assignment == null && b.Assignment == null)
                || (a.Assignment != null
                    && b.Assignment != null
                    && a.Assignment.Rooms.SequenceEqual(b.Assignment.Rooms)));
    }

    private void SuppressNextClick()
    {
        _suppressNextClick = true;
        Dispatcher.BeginInvoke(
            () => _suppressNextClick = false,
            DispatcherPriority.Background);
    }

    private static void RemoveHoverBadges(Grid root)
    {
        foreach (var badge in root.Children.OfType<Button>().ToList())
            root.Children.Remove(badge);
    }

    private static string DayName(int day) => day switch
    {
        0 => "월", 1 => "화", 2 => "수", 3 => "목", 4 => "금",
        _ => "?",
    };
}

internal sealed class DragPreviewAdorner : Adorner
{
    private readonly VisualBrush _brush;
    private readonly double _width;
    private readonly double _height;
    private readonly Point _clickOffset;
    private Point _position;
    private Point? _snappedTopLeft;

    public DragPreviewAdorner(UIElement adornedElement, Visual source, double width, double height, Point clickOffset)
        : base(adornedElement)
    {
        _brush = new VisualBrush(source)
        {
            Opacity = 0.65,
            Stretch = Stretch.Fill,
        };
        _width = width;
        _height = height;
        _clickOffset = clickOffset;
        IsHitTestVisible = false;
    }

    public void Update(Point cursorPosition, Point? snappedTopLeft = null)
    {
        _position = cursorPosition;
        _snappedTopLeft = snappedTopLeft;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var topLeft = _snappedTopLeft ?? new Point(
            _position.X - _clickOffset.X,
            _position.Y - _clickOffset.Y);
        var rect = new Rect(
            topLeft.X,
            topLeft.Y,
            _width,
            _height);
        drawingContext.DrawRectangle(_brush, null, rect);
    }
}
