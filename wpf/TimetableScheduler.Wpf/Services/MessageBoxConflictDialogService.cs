using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Services;
using TimetableScheduler.Wpf.Converters;

namespace TimetableScheduler.Wpf.Services;

public sealed class MessageBoxConflictDialogService : IConflictDialogService
{
    private readonly WorkspaceService _workspace;
    private Window? _manualValidationReportWindow;

    public MessageBoxConflictDialogService(WorkspaceService workspace)
    {
        _workspace = workspace;
    }

    public bool ConfirmDespiteConflicts(IReadOnlyList<ConflictItem> newConflicts)
        => ConfirmDespiteConflicts(newConflicts, null);

    public bool ConfirmDespiteConflicts(
        IReadOnlyList<ConflictItem> newConflicts,
        ConflictSelectionContext? selection)
    {
        var dialog = new Window
        {
            Title = "제약 위반 경고",
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
        };

        var layout = new Grid
        {
            Margin = new Thickness(16),
            MinWidth = 420,
            MaxWidth = 680,
        };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = "이 변경으로 다음 제약 사항이 위반됩니다:",
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var conflictList = BuildConflictList(newConflicts, selection);
        Grid.SetRow(conflictList, 1);
        layout.Children.Add(conflictList);

        var footer = new StackPanel();
        footer.Children.Add(new TextBlock
        {
            Text = "계속하시겠습니까?",
            Margin = new Thickness(0, 8, 0, 4),
            TextWrapping = TextWrapping.Wrap,
        });
        footer.Children.Add(new TextBlock
        {
            Text = "[확인] 변경을 그대로 적용 (위반 상태 유지)\n[취소] 변경을 되돌림",
            Margin = new Thickness(0, 0, 0, 14),
            TextWrapping = TextWrapping.Wrap,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var keep = false;
        AddButton("확인", true);
        AddButton("취소", false);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        dialog.Content = layout;
        dialog.ShowDialog();
        return keep;

        void AddButton(string content, bool value)
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
                keep = value;
                dialog.DialogResult = true;
            };
            buttons.Children.Add(button);
        }
    }

    public bool ConfirmDiscardChanges()
    {
        var dialog = new Window
        {
            Title = "변경사항 확인",
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(16),
            MinWidth = 280,
        };
        panel.Children.Add(new TextBlock
        {
            Text = "저장되지 않은 변경사항이 있습니다.",
            Margin = new Thickness(0, 0, 0, 14),
            TextWrapping = TextWrapping.Wrap,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var discard = false;
        AddButton("변경사항 폐기", true);
        AddButton("취소", false);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.ShowDialog();
        return discard;

        void AddButton(string content, bool value)
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
                discard = value;
                dialog.DialogResult = true;
            };
            buttons.Children.Add(button);
        }
    }

    public void ShowBlockingConflicts(string title, IReadOnlyList<ConflictItem> conflicts)
        => ShowValidationResult(title, "제약조건 위반 Error가 남아 있습니다.", conflicts);

    public void ShowValidationResult(string title, string message, IReadOnlyList<ConflictItem> conflicts)
    {
        var dialog = new Window
        {
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
        };

        var layout = new Grid
        {
            Margin = new Thickness(16),
            MinWidth = 420,
            MaxWidth = 680,
        };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        if (conflicts.Count > 0)
        {
            var conflictList = BuildConflictList(conflicts, null);
            Grid.SetRow(conflictList, 1);
            layout.Children.Add(conflictList);
        }

        var ok = new Button
        {
            Content = "확인",
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        Grid.SetRow(ok, 2);
        layout.Children.Add(ok);
        dialog.Content = layout;
        dialog.ShowDialog();
    }

    public void ShowManualValidationReport(ManualValidationReport report)
    {
        if (_manualValidationReportWindow == null)
        {
            _manualValidationReportWindow = new Window
            {
                Title = "전체 검증 결과",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 760,
                Height = 720,
                MinWidth = 620,
                MinHeight = 420,
                ResizeMode = ResizeMode.CanResize,
                Background = Brushes.White,
                Owner = Application.Current?.MainWindow,
            };
            _manualValidationReportWindow.Closed += (_, _) => _manualValidationReportWindow = null;
        }

        _manualValidationReportWindow.Content = BuildManualValidationReportContent(report);
        if (_manualValidationReportWindow.WindowState == WindowState.Minimized)
            _manualValidationReportWindow.WindowState = WindowState.Normal;
        if (!_manualValidationReportWindow.IsVisible)
            _manualValidationReportWindow.Show();
        _manualValidationReportWindow.Activate();
    }

    private FrameworkElement BuildManualValidationReportContent(ManualValidationReport report)
    {
        var layout = new Grid
        {
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(16, 16, 16, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "전체 검증 결과",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        header.Children.Add(new TextBlock
        {
            Text = BuildReportSummary(report),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
        });
        header.Children.Add(new TextBlock
        {
            Text = BuildReportMessage(report),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var items = new StackPanel();
        foreach (var item in report.Items)
            items.Children.Add(BuildReportItem(item));
        var scroll = new ScrollViewer
        {
            Margin = new Thickness(16, 0, 16, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = items,
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        var close = new Button
        {
            Content = "닫기",
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 12, 16, 16),
            Padding = new Thickness(10, 4, 10, 4),
        };
        close.Click += (_, _) => _manualValidationReportWindow?.Close();
        Grid.SetRow(close, 2);
        layout.Children.Add(close);
        return layout;
    }

    private static string BuildReportSummary(ManualValidationReport report) =>
        $"검사 항목: {report.TotalCount}개\n"
        + $"정상: {report.PassedCount}개\n"
        + $"주의: {report.WarningCount}개\n"
        + $"오류: {report.FailedCount}개\n"
        + $"검사 불가: {report.NotCheckedCount}개\n"
        + $"제외: {report.ExcludedCount}개";

    private static string BuildReportMessage(ManualValidationReport report)
    {
        if (report.FailedCount > 0)
            return "저장할 수 없는 오류가 있습니다.";
        if (report.WarningCount > 0)
            return "저장은 가능하지만 주의가 필요한 항목이 있습니다.";
        return "검사한 모든 항목에 이상이 없습니다.";
    }

    private FrameworkElement BuildReportItem(ManualValidationItem item)
    {
        var accent = item.Status switch
        {
            ManualValidationStatus.Passed => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
            ManualValidationStatus.Warning => new SolidColorBrush(Color.FromRgb(0xB7, 0x79, 0x1F)),
            ManualValidationStatus.Failed => new SolidColorBrush(Color.FromRgb(0xBA, 0x1A, 0x1A)),
            _ => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
        };
        var icon = item.Status switch
        {
            ManualValidationStatus.Passed => "✓",
            ManualValidationStatus.Warning => "!",
            ManualValidationStatus.Failed => "✕",
            _ => "－",
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = icon,
            Foreground = accent,
            FontWeight = FontWeights.Bold,
            Width = 22,
        });
        header.Children.Add(new TextBlock
        {
            Text = item.DisplayName,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 210,
        });
        header.Children.Add(new TextBlock
        {
            Text = item.Summary,
            Foreground = accent,
            FontWeight = FontWeights.SemiBold,
        });

        var body = new StackPanel { Margin = new Thickness(22, 6, 0, 0) };
        if (!string.IsNullOrWhiteSpace(item.Reason))
        {
            body.Children.Add(new TextBlock
            {
                Text = item.Reason,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }
        if (item.Details.Count > 0)
        {
            var detailItems = new StackPanel();
            var reportReasonLabel = ReportReasonLabel(item);
            foreach (var conflict in item.Details)
                detailItems.Children.Add(BuildConflictBlock(conflict, null, reportReasonLabel));
            body.Children.Add(new ScrollViewer
            {
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = detailItems,
            });
        }

        if (item.Status == ManualValidationStatus.Passed)
        {
            return new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Child = header,
            };
        }

        return new Expander
        {
            Header = header,
            IsExpanded = item.Status is ManualValidationStatus.NotChecked or ManualValidationStatus.Excluded,
            Margin = new Thickness(0, 0, 0, 6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
            BorderThickness = new Thickness(1),
            Content = body,
        };
    }

    private ScrollViewer BuildConflictList(
        IReadOnlyList<ConflictItem> conflicts,
        ConflictSelectionContext? selection)
    {
        var items = new StackPanel();
        foreach (var conflict in conflicts)
            items.Children.Add(BuildConflictBlock(conflict, selection));

        return new ScrollViewer
        {
            MaxHeight = 336,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = items,
        };
    }

    private static string ReportReasonLabel(ManualValidationItem item)
    {
        var label = item.DisplayName.Trim();
        return label.EndsWith(" 검증", StringComparison.Ordinal)
            ? label[..^3]
            : label;
    }

    private Border BuildConflictBlock(
        ConflictItem conflict,
        ConflictSelectionContext? selection,
        string? reasonLabelOverride = null)
    {
        var accent = conflict.Severity == ConflictSeverity.Warning
            ? Color.FromRgb(0xB7, 0x79, 0x1F)
            : Color.FromRgb(0xBA, 0x1A, 0x1A);
        var bg = conflict.Severity == ConflictSeverity.Warning
            ? Color.FromRgb(0xFF, 0xF8, 0xE1)
            : Color.FromRgb(0xFF, 0xF8, 0xF8);
        var accentBrush = new SolidColorBrush(accent);

        var stack = new StackPanel();
        var assignments = (conflict.Assignments ?? Array.Empty<SolutionAssignment>())
            .DistinctBy(AssignmentIdentity)
            .ToList();
        SolutionAssignment? selected = selection == null
            ? null
            : assignments
                .Where(assignment => MatchesSelection(assignment, selection))
                .Select(assignment => (SolutionAssignment?)assignment)
                .FirstOrDefault();

        if (selected is SolutionAssignment selectedAssignment)
            AddCourseLine("선택한 수업:", selectedAssignment, selection);

        foreach (var assignment in assignments.Where(assignment =>
                     selected is not SolutionAssignment selectedRow
                     || AssignmentIdentity(assignment) != AssignmentIdentity(selectedRow)))
            AddCourseLine(selected == null ? "관련 수업:" : "충돌 상대:", assignment, null);

        AddLine("사유:", reasonLabelOverride ?? KoreanLabel(conflict.Type), accentBrush, FontWeights.SemiBold);
        foreach (var line in conflict.Description.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            AddLine("상세:", line, accentBrush, FontWeights.Normal);

        void AddCourseLine(
            string role,
            SolutionAssignment assignment,
            ConflictSelectionContext? selectedContext)
        {
            var course = _workspace.ExpandedCourses.FirstOrDefault(c =>
                string.Equals(c.Id, assignment.CourseId, StringComparison.Ordinal));
            if (course == null)
                return;

            var day = selectedContext?.Day ?? assignment.Day;
            var period = selectedContext?.Period ?? assignment.Period;
            AddLine(
                role,
                $"{course.Name} {course.SectionLabel}분반 / {DayName(day)} {period}교시",
                GradeToBrushConverter.BrushFor(course.Grade),
                FontWeights.SemiBold);
        }

        void AddLine(string role, string text, Brush valueBrush, FontWeight fontWeight)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var roleText = new TextBlock
            {
                Text = role,
                Foreground = accentBrush,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 6, 0),
            };
            var valueText = new TextBlock
            {
                Text = text,
                Foreground = valueBrush,
                FontWeight = fontWeight,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(valueText, 1);
            row.Children.Add(roleText);
            row.Children.Add(valueText);
            stack.Children.Add(row);
        }

        return new Border
        {
            MinHeight = 78,
            Background = new SolidColorBrush(bg),
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(4, 1, 1, 1),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Child = stack,
        };
    }

    private static bool MatchesSelection(SolutionAssignment assignment, ConflictSelectionContext selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.AssignmentId))
            return string.Equals(assignment.AssignmentId, selection.AssignmentId, StringComparison.Ordinal);
        return string.Equals(assignment.CourseId, selection.CourseId, StringComparison.Ordinal)
            && assignment.Day == selection.Day
            && assignment.Period == selection.Period;
    }

    private static string AssignmentIdentity(SolutionAssignment assignment) =>
        string.IsNullOrWhiteSpace(assignment.AssignmentId)
            ? string.Join("\u001f", assignment.CourseId, assignment.Day, assignment.Period, assignment.RoomId)
            : assignment.AssignmentId;

    private static string DayName(int day) => day switch
    {
        0 => "월",
        1 => "화",
        2 => "수",
        3 => "목",
        4 => "금",
        _ => "?",
    };

    private static string KoreanLabel(ConflictType type) => type switch
    {
        ConflictType.RoomConflict => "강의실 시간 중복",
        ConflictType.ProfessorConflict => "교수 시간 중복",
        ConflictType.ProfUnavailable => "교수 불가시간",
        ConflictType.LunchConflict => "점심시간 배치",
        ConflictType.SectionConflict => "분반 시간 중복",
        ConflictType.GradeConflict => "학년 시간 중복",
        ConflictType.FixedRoomViolation => "고정 강의실 위반",
        ConflictType.CourseUnavailableRoomViolation => "불가 강의실 위반",
        ConflictType.FixedTimeViolation => "고정 시간 위반",
        ConflictType.BlockStartViolation => "블록 시작 교시",
        ConflictType.SameCourseSameDayConflict => "같은 요일 중복 배치",
        ConflictType.ProfUnavailableRoomViolation => "교수 불가 강의실",
        ConflictType.ProfRoomInconsistent => "교수 강의실 일관성",
        ConflictType.AcademicLevelTimeBandViolation => "학위과정 시간대 위반",
        _ => "제약조건 위반",
    };
}
