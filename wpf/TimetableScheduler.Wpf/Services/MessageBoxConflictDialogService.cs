using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Services;
using TimetableScheduler.Wpf.Converters;

namespace TimetableScheduler.Wpf.Services;

public sealed class MessageBoxConflictDialogService : IConflictDialogService
{
    private readonly WorkspaceService _workspace;

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

    public bool ConfirmSaveDespiteConflicts(IReadOnlyList<ConflictItem> conflicts)
    {
        var dialog = new Window
        {
            Title = "저장 경고",
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
            Text = $"현재 제약조건 위반 {conflicts.Count}건이 있습니다. 그래도 저장하시겠습니까?",
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var conflictList = BuildConflictList(conflicts, null);
        Grid.SetRow(conflictList, 1);
        layout.Children.Add(conflictList);

        var footer = new StackPanel();
        footer.Children.Add(new TextBlock
        {
            Text = "[저장] 위반 사항을 포함한 현재 시간표를 저장합니다.\n[취소] 저장하지 않고 수동편집 화면으로 돌아갑니다.",
            Margin = new Thickness(0, 8, 0, 14),
            TextWrapping = TextWrapping.Wrap,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var save = false;
        AddButton("저장", true);
        AddButton("취소", false);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        dialog.Content = layout;
        dialog.ShowDialog();
        return save;

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
                save = value;
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
        => ShowValidationResult(title, message, conflicts, Array.Empty<ValidationCheckItem>());

    public void ShowValidationResult(
        string title,
        string message,
        IReadOnlyList<ConflictItem> conflicts,
        IReadOnlyList<ValidationCheckItem> checks)
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

        var body = new StackPanel();
        if (checks.Count > 0)
        {
            body.Children.Add(BuildValidationCheckList(checks));
        }
        else if (conflicts.Count > 0)
        {
            var conflictList = BuildConflictList(conflicts, null);
            body.Children.Add(conflictList);
        }

        if (body.Children.Count > 0)
        {
            Grid.SetRow(body, 1);
            layout.Children.Add(body);
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

    private ScrollViewer BuildValidationCheckList(IReadOnlyList<ValidationCheckItem> checks)
    {
        var items = new StackPanel();
        foreach (var check in checks)
            items.Children.Add(BuildValidationCheckRow(check));

        return new ScrollViewer
        {
            MaxHeight = 460,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = items,
        };
    }

    private Border BuildValidationCheckRow(ValidationCheckItem check)
    {
        var accent = check.IsNormal
            ? Color.FromRgb(0x1B, 0x7F, 0x3A)
            : Color.FromRgb(0xBA, 0x1A, 0x1A);
        var background = check.IsNormal
            ? Color.FromRgb(0xF1, 0xFA, 0xF3)
            : Color.FromRgb(0xFF, 0xF8, 0xF8);
        var accentBrush = new SolidColorBrush(accent);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textPanel = new StackPanel();
        var titlePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };
        titlePanel.Children.Add(new TextBlock
        {
            Text = check.Name,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(check.CountText))
        {
            titlePanel.Children.Add(new TextBlock
            {
                Text = check.CountText,
                Foreground = Brushes.Gray,
                FontSize = 12,
                Margin = new Thickness(8, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        textPanel.Children.Add(titlePanel);
        if (!string.IsNullOrWhiteSpace(check.Detail))
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = check.Detail,
                Foreground = Brushes.DimGray,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var status = new TextBlock
        {
            Text = check.StatusText,
            Foreground = accentBrush,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(status, 1);
        header.Children.Add(textPanel);
        if (check.IsNormal)
            header.Children.Add(status);

        FrameworkElement child = check.IsNormal
            ? header
            : BuildValidationCheckExpander(check, header);

        return new Border
        {
            Background = new SolidColorBrush(background),
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(4, 1, 1, 1),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = string.IsNullOrWhiteSpace(check.Tooltip) ? null : check.Tooltip,
            Child = child,
        };
    }

    private Expander BuildValidationCheckExpander(ValidationCheckItem check, Grid header)
    {
        var details = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
        };

        if (!string.IsNullOrWhiteSpace(check.Detail))
        {
            details.Children.Add(new TextBlock
            {
                Text = check.Detail,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (check.DetailConflicts.Count == 0)
        {
            details.Children.Add(new TextBlock
            {
                Text = "상세 위반 항목이 별도로 기록되지 않은 검증 항목입니다.",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            foreach (var conflict in check.DetailConflicts)
                details.Children.Add(BuildConflictBlock(conflict, null, locationFirst: true));
        }

        return new Expander
        {
            Header = header,
            Content = details,
            IsExpanded = false,
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

    private Border BuildConflictBlock(
        ConflictItem conflict,
        ConflictSelectionContext? selection,
        bool locationFirst = false)
    {
        var accent = Color.FromRgb(0xBA, 0x1A, 0x1A);
        var bg = Color.FromRgb(0xFF, 0xF8, 0xF8);
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

        if (locationFirst)
        {
            AddLine("위치:", BuildConflictLocationText(conflict, assignments, selection), accentBrush, FontWeights.SemiBold);
            AddLine("사유:", BuildReasonLabel(conflict), accentBrush, FontWeights.SemiBold);
            foreach (var line in conflict.Description.Split(
                         new[] { "\r\n", "\n" },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                AddLine("상세:", line, accentBrush, FontWeights.Normal);
        }
        else
        {
            if (selected is SolutionAssignment selectedAssignment)
                AddCourseLine("선택한 수업:", selectedAssignment, selection, includeLocation: true);

            foreach (var assignment in assignments.Where(assignment =>
                         selected is not SolutionAssignment selectedRow
                         || AssignmentIdentity(assignment) != AssignmentIdentity(selectedRow)))
                AddCourseLine(selected == null ? "관련 수업:" : "충돌 상대:", assignment, null, includeLocation: true);

            AddLine("사유:", BuildReasonLabel(conflict), accentBrush, FontWeights.SemiBold);
            foreach (var line in conflict.Description.Split(
                         new[] { "\r\n", "\n" },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                AddLine("상세:", line, accentBrush, FontWeights.Normal);
        }

        void AddCourseLine(
            string role,
            SolutionAssignment assignment,
            ConflictSelectionContext? selectedContext,
            bool includeLocation)
        {
            var course = _workspace.ExpandedCourses.FirstOrDefault(c =>
                string.Equals(c.Id, assignment.CourseId, StringComparison.Ordinal));
            if (course == null)
                return;

            var location = ResolveConflictLocation(conflict, assignment, selectedContext);
            var day = selectedContext?.Day ?? assignment.Day;
            var period = selectedContext?.Period ?? assignment.Period;
            var courseText = $"{course.Name} {course.SectionLabel}분반";
            if (!includeLocation)
            {
                AddLine(
                    role,
                    courseText,
                    GradeToBrushConverter.BrushFor(course.Grade),
                    FontWeights.SemiBold);
                return;
            }

            if (location.StartPeriod <= location.EndPeriod)
            {
                AddLine(
                    role,
                    $"{DayName(location.Day)} {FormatPeriodRange(location.StartPeriod, location.EndPeriod)} / {courseText}",
                    GradeToBrushConverter.BrushFor(course.Grade),
                    FontWeights.SemiBold);
                return;
            }
            AddLine(
                role,
                $"{courseText} / {DayName(day)} {period}교시",
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

    private static (int Day, int StartPeriod, int EndPeriod) ResolveConflictLocation(
        ConflictItem conflict,
        SolutionAssignment assignment,
        ConflictSelectionContext? selectedContext)
    {
        var day = selectedContext?.Day ?? assignment.Day;
        var identity = AssignmentIdentity(assignment);
        var periods = (conflict.Assignments ?? Array.Empty<SolutionAssignment>())
            .Where(row => row.Day == day && string.Equals(AssignmentIdentity(row), identity, StringComparison.Ordinal))
            .Select(row => row.Period)
            .Distinct()
            .OrderBy(period => period)
            .ToList();

        if (periods.Count == 0)
        {
            var period = selectedContext?.Period ?? assignment.Period;
            return (day, period, period);
        }

        return (day, periods[0], periods[^1]);
    }

    private static string BuildConflictLocationText(
        ConflictItem conflict,
        IReadOnlyList<SolutionAssignment> assignments,
        ConflictSelectionContext? selection)
    {
        var locations = assignments
            .Select(assignment => ResolveConflictLocation(conflict, assignment, selection))
            .OrderBy(location => location.Day)
            .ThenBy(location => location.StartPeriod)
            .ThenBy(location => location.EndPeriod)
            .Select(location => $"{DayName(location.Day)} {FormatPeriodRange(location.StartPeriod, location.EndPeriod)}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (locations.Count > 0)
            return string.Join(", ", locations);

        return $"{DayName(conflict.Day)} {conflict.Period}교시";
    }

    private static string FormatPeriodRange(int startPeriod, int endPeriod) =>
        startPeriod == endPeriod
            ? $"{startPeriod}교시"
            : $"{startPeriod}~{endPeriod}교시";

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
        ConflictType.RoomConflict => "강의실 중복",
        ConflictType.ProfessorConflict => "교수 중복",
        ConflictType.ProfUnavailable => "교수 불가시간",
        ConflictType.LunchConflict => "점심시간 배치",
        ConflictType.SectionConflict => "분반 중복",
        ConflictType.GradeConflict => "학년 중복",
        ConflictType.FixedRoomViolation => "고정 강의실 위반",
        ConflictType.CourseUnavailableRoomViolation => "불가 강의실 위반",
        ConflictType.FixedTimeViolation => "고정시간 이탈",
        ConflictType.BlockStartViolation => "블록 시작 교시",
        ConflictType.SameCourseSameDayConflict => "같은 요일 중복",
        ConflictType.ProfRoomInconsistent => "강의실 불일치",
        ConflictType.AcademicLevelTimeBandViolation => "시간대 위반",
        ConflictType.RetakeConflict => "재수강생 고려",
        ConflictType.CourseRoomInconsistent => "교과목별 동일 강의실",
        _ => "제약조건 위반",
    };

    private string BuildReasonLabel(ConflictItem conflict)
    {
        var label = KoreanLabel(conflict.Type);
        if (conflict.Type != ConflictType.FixedTimeViolation)
            return label;

        var fixedPosition = BuildOriginalFixedPositionText(conflict);
        return string.IsNullOrWhiteSpace(fixedPosition)
            ? label
            : $"{label} / 원래 고정위치: {fixedPosition}";
    }

    private string BuildOriginalFixedPositionText(ConflictItem conflict)
    {
        foreach (var assignment in conflict.Assignments ?? Array.Empty<SolutionAssignment>())
        {
            var course = ResolveCourseForAssignment(assignment);
            if (course?.FixedSlots.Count > 0)
                return FormatFixedPositionSlots(course.FixedSlots);
        }

        return "";
    }

    private Course? ResolveCourseForAssignment(SolutionAssignment assignment)
    {
        var candidates = _workspace.ExpandedCourses
            .Where(course => string.Equals(course.Id, assignment.CourseId, StringComparison.Ordinal))
            .ToList();
        return candidates.Count == 1
            ? candidates[0]
            : candidates.FirstOrDefault(course =>
                course.FixedRooms.Any(room => string.Equals(room, assignment.RoomId, StringComparison.Ordinal)));
    }

    private static string FormatFixedPositionSlots(IEnumerable<TimeSlot> slots)
    {
        var labels = new List<string>();
        foreach (var dayGroup in slots
                     .GroupBy(slot => slot.Day)
                     .OrderBy(group => group.Key))
        {
            var periods = dayGroup
                .Select(slot => slot.Period)
                .Distinct()
                .OrderBy(period => period)
                .ToList();
            if (periods.Count == 0)
                continue;

            var start = periods[0];
            var end = periods[0];
            foreach (var period in periods.Skip(1))
            {
                if (period == end + 1)
                {
                    end = period;
                    continue;
                }

                labels.Add($"{DayName(dayGroup.Key)} {FormatFixedPositionPeriodRange(start, end)}");
                start = period;
                end = period;
            }

            labels.Add($"{DayName(dayGroup.Key)} {FormatFixedPositionPeriodRange(start, end)}");
        }

        return string.Join(", ", labels);
    }

    private static string FormatFixedPositionPeriodRange(int startPeriod, int endPeriod) =>
        startPeriod == endPeriod ? $"{startPeriod}시" : $"{startPeriod}~{endPeriod}시";
}
