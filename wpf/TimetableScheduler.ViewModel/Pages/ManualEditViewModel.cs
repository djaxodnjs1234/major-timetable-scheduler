using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.ViewModel.Services;

namespace TimetableScheduler.ViewModel.Pages;

public sealed partial class ManualEditViewModel : PageViewModelBase
{
    private const string ManualCrossPolicyType = "HC11_ONLY_EXCEPTION";
    private const string OneHourAboveTwoHourBlockedReason =
        "같은 과목·같은 분반의 1시간 수업 바로 아래에는 2시간 이상 블록을 배치할 수 없습니다.";
    private const string CrossDisplayColumnBlockedReason =
        "Cross 표시용 열에는 일반 이동할 수 없습니다.";
    private const string BlockRangeOverlapBlockedReason =
        "수업 블록의 전체 점유 범위가 다른 수업과 겹칩니다.";
    private const string CrossDurationMismatchReason =
        "서로 다른 길이의 수업은 Cross할 수 없습니다.";
    private const string CrossTimeRangeMismatchReason =
        "같은 수업시간의 수업끼리만 Cross할 수 있습니다.";
    private static readonly Regex ConstraintCodeRegex = new(
        @"(?:\[(?:HC|SC)-?\d+\]\s*)?\b(?:HC|SC)-?\d+\b\s*(?:[:\-–]\s*)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record ManualCrossLink(
        string CourseIdA,
        string CourseIdB,
        int Day,
        int PeriodA,
        int RowSpanA,
        int PeriodB,
        int RowSpanB,
        ManualCrossAssignmentKey SourceKey,
        ManualCrossAssignmentKey TargetKey)
    {
        public bool MatchesPair(string left, string right) =>
            (CourseIdA == left && CourseIdB == right)
            || (CourseIdA == right && CourseIdB == left);

        public bool MatchesPair(ManualCrossAssignmentKey left, ManualCrossAssignmentKey right) =>
            (SourceKey == left && TargetKey == right)
            || (SourceKey == right && TargetKey == left);

        public bool Contains(ManualCrossAssignmentKey key) =>
            SourceKey == key || TargetKey == key;

        public bool Covers(string left, string right, int day, int period) =>
            CoversOrdered(SourceKey, TargetKey, left, right, day, period)
            || CoversOrdered(TargetKey, SourceKey, left, right, day, period);

        private static bool CoversOrdered(
            ManualCrossAssignmentKey source,
            ManualCrossAssignmentKey target,
            string left,
            string right,
            int day,
            int period) =>
            source.CourseId == left
            && target.CourseId == right
            && source.Day == day
            && target.Day == day
            && IsInside(period, source.Period, source.RowSpan)
            && IsInside(period, target.Period, target.RowSpan);

        private static bool IsInside(int period, int start, int span) =>
            period >= start && period < start + span;
    }

    public readonly record struct ManualCrossAssignmentKey(
        string CourseId,
        string Section,
        int Day,
        int Period,
        int RowSpan,
        string RoomIdsKey);

    private readonly record struct CrossTargetStart(
        CellAssignment Assignment,
        int Day,
        int Period,
        int Grade,
        int SubColumnIdx);

    public sealed record RoomOption(
        string RoomId,
        string DisplayName,
        int Capacity,
        bool IsLab,
        string SearchText);

    private static ManualCrossAssignmentKey BuildManualCrossAssignmentKey(
        CellAssignment assignment,
        int day,
        int period) =>
        new(
            assignment.CourseId,
            NormalizeManualCrossSection(assignment.Section),
            day,
            period,
            assignment.RowSpan,
            BuildRoomIdsKey(assignment.Rooms));

    private static ManualCrossAssignmentKey BuildManualCrossAssignmentKey(
        SolutionAssignment assignment,
        Course course,
        int rowSpan) =>
        new(
            assignment.CourseId,
            NormalizeManualCrossSection(course.Section),
            assignment.Day,
            assignment.Period,
            rowSpan,
            BuildRoomIdsKey(new[] { assignment.RoomId }));

    private static string BuildRoomIdsKey(IEnumerable<string> roomIds) =>
        string.Join("|", roomIds
            .Select(roomId => roomId.Trim())
            .Where(roomId => !string.IsNullOrEmpty(roomId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(roomId => roomId, StringComparer.Ordinal));

    private static string NormalizeManualCrossSection(int section) =>
        section > 0 ? section.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";

    private static string NormalizeManualCrossSection(string? section) =>
        string.IsNullOrWhiteSpace(section) ? "" : section.Trim();

    public static string StripConstraintCodeForDisplay(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return message;
        var stripped = ConstraintCodeRegex.Replace(message, "");
        stripped = stripped.Replace("[", "").Replace("]", "").Trim();
        return stripped;
    }

    private static string RepresentativeRoomId(ManualCrossAssignmentKey key) =>
        key.RoomIdsKey
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(roomId => roomId, StringComparer.Ordinal)
            .FirstOrDefault() ?? "";

    private static string ToManualCrossDisplayKey(ManualCrossAssignmentKey key) =>
        UnifiedTimetableViewModel.BuildManualCrossDisplayKey(
            key.CourseId,
            int.TryParse(key.Section, out var section) ? section : 0,
            key.Day,
            key.Period,
            key.RowSpan,
            key.RoomIdsKey.Split('|', StringSplitOptions.RemoveEmptyEntries));

    private readonly WorkspaceService _workspace;
    private readonly IConflictDialogService _dialog;
    private List<SolutionAssignment> _working = new();
    private List<CrossGroup> _workingCrossGroups = new();
    private readonly List<ManualCrossLink> _workingCrossLinks = new();
    private readonly List<string> _ignoredSavedCrossLinkReasons = new();
    private bool _isRestoringSavedTimetable;
    private bool _suppressSavedCrossValidation;
    private bool _hasUserEditedAfterLoad;
    private int _lastSavedCrossLinkCount;
    private int _lastRestoredCrossLinkCount;
    private int _lastIgnoredCrossLinkCount;
    private bool _lastCrossCleanupExecuted;
    private string _lastCrossCleanupSkipReason = "";
    private ManualEditSnapshot? _resetBaselineSnapshot;
    private sealed class ManualEditSnapshot
    {
        public List<SolutionAssignment> Assignments { get; init; } = new();
        public List<ManualCrossLink> CrossLinks { get; init; } = new();
    }

    private readonly Stack<ManualEditSnapshot> _undoStack = new();
    private readonly Stack<ManualEditSnapshot> _redoStack = new();
    private UnifiedCellKey? _selectedGridCell;

    // Session data: snapshot from a saved timetable if loaded that way, else live workspace.
    private AppData? _sessionData;
    private IReadOnlyList<Course> SessionCourses =>
        (IReadOnlyList<Course>?)_sessionData?.Courses ?? _workspace.ExpandedCourses;
    private IReadOnlyList<Professor> SessionProfessors =>
        (IReadOnlyList<Professor>?)_sessionData?.Professors ?? _workspace.Professors;
    private IReadOnlyList<Room> SessionRooms =>
        (IReadOnlyList<Room>?)_sessionData?.Rooms ?? _workspace.Rooms;
    private IReadOnlyList<CrossGroup> SessionCrossGroups =>
        (IReadOnlyList<CrossGroup>?)_sessionData?.CrossGroups ?? _workspace.CrossGroups;

    public override string Title => "수동 편집";

    public event EventHandler? BackRequested;

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    public UnifiedTimetableViewModel Grid { get; } = new();
    public ObservableCollection<NamedGridViewModel> GradeViews { get; } = new();
    public ObservableCollection<NamedGridViewModel> RoomViews { get; } = new();
    public ObservableCollection<NamedGridViewModel> ProfessorViews { get; } = new();

    public ObservableCollection<ConflictDisplayItem> Conflicts { get; } = new();
    public IReadOnlyList<ManualCrossLink> WorkingCrossLinks => _workingCrossLinks;
    public IReadOnlyList<string> IgnoredSavedCrossLinkReasons => _ignoredSavedCrossLinkReasons;
    public int LastSavedCrossLinkCount => _lastSavedCrossLinkCount;
    public int LastRestoredCrossLinkCount => _lastRestoredCrossLinkCount;
    public int LastIgnoredCrossLinkCount => _lastIgnoredCrossLinkCount;
    public bool IsSavedCrossValidationSuppressed => _suppressSavedCrossValidation;
    public bool HasUserEditedAfterLoad => _hasUserEditedAfterLoad;
    public bool LastCrossCleanupExecuted => _lastCrossCleanupExecuted;
    public string LastCrossCleanupSkipReason => _lastCrossCleanupSkipReason;
    public IReadOnlyList<CrossGroup> WorkingCrossGroups => _workingCrossGroups;
    public IReadOnlyList<string> PendingCrossExportLabels =>
        _workingCrossLinks.Select(FormatCrossLink).ToList();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportXlsxCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveTimetableCommand))]
    private RankedSolution? baseSolution;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveTimetableCommand))]
    private string saveName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyRoomChangeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    private CellAssignment? selectedAssignment;

    public bool HasSelection => SelectedAssignment != null;
    public bool HasNoSelection => !HasSelection;

    [ObservableProperty]
    private int? selectedDay;

    [ObservableProperty]
    private int? selectedPeriod;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyRoomChangeCommand))]
    private string newRoomId = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyRoomChangeCommand))]
    private string selectedOldRoomId = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyRoomChangeCommand))]
    private string selectedReplacementRoomId = "";

    [ObservableProperty]
    private string statusMessage = "셀을 클릭해서 편집할 과목을 선택하세요.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoomChangeStatusMessage))]
    private string roomChangeStatusMessage = "";

    public bool HasRoomChangeStatusMessage => !string.IsNullOrWhiteSpace(RoomChangeStatusMessage);

    public IReadOnlyList<string> AvailableRoomIds => BuildAllowedRoomCandidateIds(includeAssignedRooms: true);

    public IReadOnlyList<string> SingleRoomChangeCandidates => AvailableRoomIds;

    public IReadOnlyList<RoomOption> AvailableRoomOptions =>
        BuildRoomOptions(AvailableRoomIds, includeUnknownAssignedRooms: true);

    public bool HasMultipleRoomCandidates => SingleRoomChangeCandidates.Count > 1;

    public IReadOnlyList<string> AssignedRoomsForSelectedAssignment =>
        SelectedAssignment == null
            ? Array.Empty<string>()
            : SelectedAssignment.Rooms.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();

    public IReadOnlyList<RoomOption> AssignedRoomOptions =>
        BuildRoomOptions(AssignedRoomsForSelectedAssignment, includeUnknownAssignedRooms: true);

    public bool HasMultipleAssignedRooms => AssignedRoomsForSelectedAssignment.Count > 1;

    public bool HasSingleAssignedRoomDisplay => !HasMultipleAssignedRooms;

    public bool HasSingleAssignedRoomChangeUi => !HasMultipleAssignedRooms && HasSelection;

    public bool HasRoomChangeApplyUi => HasSingleAssignedRoomChangeUi || HasMultipleAssignedRooms;

    public bool HasPendingInspectorChanges
    {
        get
        {
            if (SelectedAssignment == null) return false;
            if (HasMultipleAssignedRooms)
            {
                var oldRoom = NormalizeRoomId(SelectedOldRoomId);
                var replacement = NormalizeRoomId(SelectedReplacementRoomId);
                return !string.IsNullOrWhiteSpace(oldRoom)
                    && !string.IsNullOrWhiteSpace(replacement)
                    && AssignedRoomsForSelectedAssignment.Contains(oldRoom)
                    && !AssignedRoomsForSelectedAssignment.Contains(replacement);
            }

            var original = NormalizeRoomId(SelectedAssignment.Rooms.FirstOrDefault());
            var pending = NormalizeRoomId(NewRoomId);
            return !string.IsNullOrWhiteSpace(original)
                && !string.IsNullOrWhiteSpace(pending)
                && !string.Equals(original, pending, StringComparison.Ordinal);
        }
    }

    public bool CanApplyInspectorChanges => HasPendingInspectorChanges;

    public IReadOnlyList<string> ReplacementRoomCandidates =>
        BuildReplacementRoomCandidates(SelectedOldRoomId);

    public IReadOnlyList<RoomOption> ReplacementRoomOptions =>
        BuildRoomOptions(ReplacementRoomCandidates, includeUnknownAssignedRooms: false);

    public string SelectedProfessorName => SelectedCourse == null
        ? "-"
        : SessionProfessors.FirstOrDefault(p => p.Id == SelectedCourse.ProfessorId)?.Name
            ?? SelectedCourse.ProfessorId
            ?? "-";

    public string SelectedLocationText => SelectedAssignment == null
        ? "-"
        : $"{SelectedAssignment.Grade}학년 / 분반 {Fallback(SelectedAssignment.SectionLabel)} / {SelectedRoomsText}";

    public string SelectedRoomsText => SelectedAssignment == null || SelectedAssignment.Rooms.Count == 0
        ? "-"
        : string.Join(", ", SelectedAssignment.Rooms.Select(RoomDisplayName));

    public string SelectedDurationText => SelectedAssignment == null
        ? "-"
        : $"{SelectedAssignment.RowSpan}시간";

    public string SelectedOccupiedSlotsText
    {
        get
        {
            if (SelectedAssignment == null || SelectedPeriod is not int period) return "-";
            var last = period + SelectedAssignment.RowSpan - 1;
            return period == last ? $"{period}교시" : $"{period}~{last}교시";
        }
    }

    public string SelectedBlockText => SelectedAssignment == null
        ? "-"
        : SelectedAssignment.RowSpan > 1 ? $"연속 {SelectedAssignment.RowSpan}시간 블록" : "단일 1시간";

    public string SelectedFixedText => SelectedAssignment == null
        ? "-"
        : SelectedAssignment.IsFixed ? "예" : "아니오";

    public string SelectedCoteachText
    {
        get
        {
            if (SelectedCourse == null || SelectedCourse.CoteachProfs.Count == 0) return "없음";
            var primary = SelectedCourse.ProfessorId;
            var coteachers = SelectedCourse.CoteachProfs
                .Where(id => !IsSameProfessorId(id, primary))
                .Distinct(StringComparer.Ordinal)
                .Select(ProfessorDisplayName)
                .ToList();
            return coteachers.Count == 0 ? "없음" : string.Join(", ", coteachers);
        }
    }

    public string SelectedMultiRoomText => SelectedAssignment == null
        ? "-"
        : SelectedAssignment.Rooms.Count > 1 ? $"예 ({SelectedRoomsText})" : "아니오";

    public string SelectedAllowedRoomsText
    {
        get
        {
            if (SelectedCourse == null) return "-";
            var professor = SessionProfessors.FirstOrDefault(p => p.Id == SelectedCourse.ProfessorId);
            if (SelectedCourse.FixedRooms.Count > 0)
                return string.Join(", ", SelectedCourse.FixedRooms.Select(RoomDisplayName));
            if (professor == null || professor.AllowedRooms.Count == 0)
                return "제한 없음";
            return string.Join(", ", professor.AllowedRooms.Select(RoomDisplayName));
        }
    }

    public string SelectedSlotDisplayText
    {
        get
        {
            if (SelectedDay is not int d || SelectedPeriod is not int period)
                return "";

            var rowSpan = SelectedAssignment?.RowSpan ?? 1;
            var last = period + rowSpan - 1;

            var periodText = period == last
                ? $"{period}교시"
                : $"{period}~{last}교시";

            return $"{DayName(d)}요일 {periodText}";
        }
    }

    public string SelectedMoveStateText => SelectedAssignment == null
        ? "-"
        : SelectedAssignment.IsFixed ? "이동 불가" : "이동 가능";

    public string SelectedBlockedReasonText => SelectedAssignment == null
        ? "-"
        : SelectedAssignment.IsFixed
            ? "고정 시간표 보존 위반: 고정된 수업은 이동할 수 없습니다."
            : "없음";

    public bool HasBlockingReasons => SelectedAssignment?.IsFixed == true;

    public string SelectedWarningReasonText =>
        SelectedDay is int d && SelectedPeriod is int p
            ? GetSoftWarning(d, p) ?? "없음"
            : "-";

    public bool HasWarningReasons =>
        SelectedAssignment != null
        && SelectedDay is int d
        && SelectedPeriod is int p
        && GetSoftWarning(d, p) != null;

    public bool HasAnyConstraintReasons => HasBlockingReasons || HasWarningReasons;

    public string SelectedCrossGroupText
    {
        get
        {
            if (SelectedAssignment == null) return "-";
            var labels = _workingCrossLinks
                .Where(l => l.CourseIdA == SelectedAssignment.CourseId || l.CourseIdB == SelectedAssignment.CourseId)
                .Select(FormatCrossLink)
                .ToList();
            return labels.Count == 0 ? "없음" : string.Join("; ", labels);
        }
    }

    private Course? SelectedCourse => SelectedAssignment == null
        ? null
        : ResolveCourseForCellAssignment(SelectedAssignment);

    partial void OnSelectedAssignmentChanged(CellAssignment? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        NotifySelectionDetailChanged();
        ApplyRoomChangeCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewRoomIdChanged(string value)
    {
        OnPropertyChanged(nameof(HasPendingInspectorChanges));
        OnPropertyChanged(nameof(CanApplyInspectorChanges));
        ApplyRoomChangeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedOldRoomIdChanged(string value)
    {
        OnPropertyChanged(nameof(ReplacementRoomCandidates));
        OnPropertyChanged(nameof(ReplacementRoomOptions));
        if (HasMultipleAssignedRooms && !ReplacementRoomCandidates.Contains(SelectedReplacementRoomId))
            SelectedReplacementRoomId = ReplacementRoomCandidates.FirstOrDefault() ?? "";
        OnPropertyChanged(nameof(HasPendingInspectorChanges));
        OnPropertyChanged(nameof(CanApplyInspectorChanges));
        ApplyRoomChangeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedReplacementRoomIdChanged(string value)
    {
        OnPropertyChanged(nameof(HasPendingInspectorChanges));
        OnPropertyChanged(nameof(CanApplyInspectorChanges));
        ApplyRoomChangeCommand.NotifyCanExecuteChanged();
    }

    public ManualEditViewModel(WorkspaceService workspace, IConflictDialogService dialog)
    {
        _workspace = workspace;
        _dialog = dialog;
        Grid.ExpandAllGrades = true;
        Grid.MergeOnlyStructuredBlocks = true;
    }

    public void LoadFromSaved(SavedTimetableRecord record)
    {
        _sessionData = SavedTimetableSnapshotResolver.Resolve(record.SnapshotJson);
        var assignments = record.Assignments
            .Select(r => new SolutionAssignment(r.CourseId, r.Day, r.Period, r.RoomId))
            .ToList();
        LoadCore(new RankedSolution(assignments, new SolutionScore(0, 0, 0, 0)));
        SaveName = record.Name;
    }

    public void LoadFromSolution(RankedSolution solution)
    {
        _sessionData = null;
        LoadCore(solution);
    }

    /// <summary>
    /// Enter manual editing for an existing timetable being re-edited via screen 2:
    /// the constraint snapshot was modified there, so use it as the session data.
    /// </summary>
    public void LoadFromSnapshot(
        AppData snapshot,
        RankedSolution solution,
        string saveName,
        IReadOnlyList<SavedManualCrossLinkRow>? manualCrossLinks = null)
    {
        _sessionData = snapshot;
        LoadCore(
            solution,
            manualCrossLinks,
            suppressSavedCrossValidation: manualCrossLinks is { Count: > 0 });
        SaveName = saveName;
    }

    private void LoadCore(
        RankedSolution solution,
        IReadOnlyList<SavedManualCrossLinkRow>? manualCrossLinks = null,
        bool suppressSavedCrossValidation = false)
    {
        _suppressSavedCrossValidation = suppressSavedCrossValidation;
        _hasUserEditedAfterLoad = false;
        _lastCrossCleanupExecuted = false;
        _lastCrossCleanupSkipReason = "";
        _lastSavedCrossLinkCount = 0;
        _lastRestoredCrossLinkCount = 0;
        _lastIgnoredCrossLinkCount = 0;
        _ignoredSavedCrossLinkReasons.Clear();
        BaseSolution = solution;
        _working = solution.Assignment.ToList();
        _workingCrossGroups = SessionCrossGroups
            .Select(g => new CrossGroup { Id = g.Id, BaseIds = g.BaseIds.ToList() })
            .ToList();
        _workingCrossLinks.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        NotifyUndoRedoCanExecuteChanged();
        if (manualCrossLinks != null)
            RestoreSavedManualCrossLinks(manualCrossLinks);
        _resetBaselineSnapshot = CaptureSnapshot();
        Rerender();
        ClearSelectionCore();
        StatusMessage = "수업 블록을 클릭한 뒤, 초록색 칸을 클릭해 이동하세요.";
    }

    public void LoadFromSavedTimetable(SavedTimetableRecord timetable)
    {
        _sessionData = SavedTimetableSnapshotResolver.Resolve(timetable.SnapshotJson);
        var assignments = timetable.Assignments
            .Select(r => new SolutionAssignment(r.CourseId, r.Day, r.Period, r.RoomId))
            .ToList();

        _isRestoringSavedTimetable = true;
        _suppressSavedCrossValidation = true;
        _hasUserEditedAfterLoad = false;
        _lastCrossCleanupExecuted = false;
        _lastCrossCleanupSkipReason = "";
        try
        {
            BaseSolution = new RankedSolution(assignments, new SolutionScore(0, 0, 0, 0));
            _working = assignments;
            _workingCrossGroups = SessionCrossGroups
                .Select(g => new CrossGroup { Id = g.Id, BaseIds = g.BaseIds.ToList() })
                .ToList();
            _undoStack.Clear();
            _redoStack.Clear();
            NotifyUndoRedoCanExecuteChanged();

            var ignored = RestoreSavedManualCrossLinks(timetable.ManualCrossLinks ?? Array.Empty<SavedManualCrossLinkRow>());
            _resetBaselineSnapshot = CaptureSnapshot();
            Rerender();
            ClearSelectionCore();
            SaveName = timetable.Name;
            StatusMessage = ignored == 0
                ? $"저장된 시간표 '{timetable.Name}'을(를) 수정 모드로 열었습니다."
                : $"저장된 시간표 '{timetable.Name}'을(를) 수정 모드로 열었습니다. 일부 Cross 관계 {ignored}건은 유효하지 않아 불러오지 않았습니다.";
        }
        finally
        {
            _isRestoringSavedTimetable = false;
        }
    }

    public void HandleCellClick(int day, int period, int grade, int subColumnIdx, CellAssignment? assignment)
    {
        if (SelectedAssignment != null)
        {
            if (assignment != null
                && SelectedDay is int currentSelectedDay
                && SelectedPeriod is int currentSelectedPeriod
                && IsSameMovingCell(
                    new UnifiedCell(day, period, grade, subColumnIdx, assignment),
                    SelectedAssignment,
                    currentSelectedDay,
                    currentSelectedPeriod))
            {
                ClearSelectionCore();
                StatusMessage = "선택이 해제되었습니다.";
                return;
            }

            if (grade != SelectedAssignment.Grade)
            {
                ClearSelectionCore();
                StatusMessage = "선택이 해제되었습니다.";
                return;
            }
        }

        if (SelectedAssignment != null && assignment == null)
        {
            var selectedCourseId = SelectedAssignment.CourseId;
            if (TryMoveSelectedTo(day, period, grade, subColumnIdx))
                RemoveCrossForCourse(selectedCourseId, "빈 칸 이동으로 기존 크로스가 해제되었습니다.", recordUndo: false);
            return;
        }

        if (SelectedAssignment != null
            && assignment != null
            && SelectedDay is int selectedDay
            && SelectedPeriod is int selectedPeriod
            && !IsSameMovingCell(
                new UnifiedCell(day, period, grade, subColumnIdx, assignment),
                SelectedAssignment,
                selectedDay,
                selectedPeriod))
        {
            if (HasOccupiedTargetRangeConflict(SelectedAssignment, selectedDay, selectedPeriod, day, period, grade, subColumnIdx))
            {
                StatusMessage = BlockRangeOverlapBlockedReason;
                Grid.SetEditState(_selectedGridCell, BuildMoveStates(SelectedAssignment, selectedDay, selectedPeriod));
                return;
            }

            SelectCell(day, period, grade, subColumnIdx, assignment);
            return;
        }

        SelectCell(day, period, grade, subColumnIdx, assignment);
    }

    public CrossHoverState EvaluateCrossHover(
        int day,
        int period,
        int grade,
        int subColumnIdx,
        CellAssignment? target)
    {
        static CrossHoverState Exit(CrossHoverState state, string reason) => state;

        if (SelectedAssignment == null || SelectedDay is not int selectedDay || SelectedPeriod is not int selectedPeriod)
            return Exit(CrossHoverState.Hidden(), "missing-selection");
        if (target == null)
            return Exit(CrossHoverState.Hidden(), "target-null");
        if (IsSameMovingCell(
                new UnifiedCell(day, period, grade, subColumnIdx, target),
                SelectedAssignment,
                selectedDay,
                selectedPeriod))
            return Exit(CrossHoverState.Hidden("현재 선택한 수업입니다."), "same-moving-cell");
        if (target.Grade != SelectedAssignment.Grade)
            return Exit(CrossHoverState.Hidden("같은 학년 수업끼리만 크로스를 만들 수 있습니다."), "different-grade");
        if (!HasSameCrossDuration(SelectedAssignment, target))
            return Exit(CrossHoverState.Hidden(CrossDurationMismatchReason), "duration-mismatch");
        if (!TryNormalizeCrossTarget(target, day, period, grade, subColumnIdx, out var normalizedTarget))
            return Exit(CrossHoverState.Hidden(CrossTimeRangeMismatchReason), "normalize-target-failed");
        target = normalizedTarget.Assignment;
        day = normalizedTarget.Day;
        period = normalizedTarget.Period;
        grade = normalizedTarget.Grade;
        subColumnIdx = normalizedTarget.SubColumnIdx;

        var selectedCourse = ResolveCourseForCellAssignment(SelectedAssignment);
        var targetCourse = ResolveCourseForCellAssignment(target);
        if (selectedCourse == null || targetCourse == null)
            return Exit(CrossHoverState.Hidden("수업 정보를 확인할 수 없습니다."), "course-resolve-failed");
        var selectedKey = BuildManualCrossAssignmentKey(SelectedAssignment, selectedDay, selectedPeriod);
        var targetKey = BuildManualCrossAssignmentKey(target, day, period);
        if (selectedKey == targetKey)
            return Exit(CrossHoverState.Hidden("현재 선택한 수업입니다."), "same-assignment-key");
        if (IsAlreadyCrossed(selectedKey, targetKey))
            return Exit(CrossHoverState.Hidden("이미 크로스가 설정된 수업입니다."), "already-crossed-pair");
        if (IsAssignmentAlreadyCrossed(selectedKey)
            || IsAssignmentAlreadyCrossed(targetKey))
            return Exit(CrossHoverState.Hidden("이미 다른 수업과 크로스된 과목입니다. 크로스는 두 과목끼리만 설정할 수 있습니다."), "assignment-already-crossed");
        if (RequiresSameCourseAssignmentConflictCheck(SelectedAssignment, target))
        {
            if (HasProfessorOverlap(SelectedAssignment, target))
                return Exit(CrossHoverState.Hidden("교수 단일 배정 위반: 같은 교수 수업은 Cross할 수 없습니다."), "same-course-professor-overlap");
            if (HasRoomOverlap(SelectedAssignment, target))
                return Exit(CrossHoverState.Hidden("강의실 단일 배정 위반: 같은 강의실 수업은 Cross할 수 없습니다."), "same-course-room-overlap");
        }

        var reasons = ValidateCrossMoveWithProvisionalLink(SelectedAssignment, target, day, period, grade, subColumnIdx, null);
        if (reasons.Count > 0)
            return Exit(CrossHoverState.Hidden(reasons[0]), "validate-cross-move-failed");

        return Exit(CrossHoverState.Available(), "available");
    }

    public CrossHoverState EvaluateCrossDropHover(
        int sourceDay,
        int sourcePeriod,
        int sourceGrade,
        int sourceSubColumnIdx,
        CellAssignment? source,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        CellAssignment? target)
    {
        static CrossHoverState Exit(CrossHoverState state, string reason) => state;

        if (source == null || target == null)
            return Exit(CrossHoverState.Hidden(), "source-or-target-null");
        if (IsSameMovingCell(
                new UnifiedCell(targetDay, targetPeriod, targetGrade, targetSubColumnIdx, target),
                source,
                sourceDay,
                sourcePeriod))
            return Exit(CrossHoverState.Hidden("현재 선택한 수업입니다."), "same-moving-cell");
        if (target.Grade != source.Grade)
            return Exit(CrossHoverState.Hidden("같은 학년 수업끼리만 크로스를 만들 수 있습니다."), "different-grade");
        if (!HasSameCrossDuration(source, target))
            return Exit(CrossHoverState.Hidden(CrossDurationMismatchReason), "duration-mismatch");
        if (!TryNormalizeCrossTarget(target, targetDay, targetPeriod, targetGrade, targetSubColumnIdx, out var normalizedTarget))
            return Exit(CrossHoverState.Hidden(CrossTimeRangeMismatchReason), "normalize-target-failed");
        target = normalizedTarget.Assignment;
        targetDay = normalizedTarget.Day;
        targetPeriod = normalizedTarget.Period;
        targetGrade = normalizedTarget.Grade;
        targetSubColumnIdx = normalizedTarget.SubColumnIdx;

        var selectedCourse = ResolveCourseForCellAssignment(source);
        var targetCourse = ResolveCourseForCellAssignment(target);
        if (selectedCourse == null || targetCourse == null)
            return Exit(CrossHoverState.Hidden("수업 정보를 확인할 수 없습니다."), "course-resolve-failed");
        var sourceKey = BuildManualCrossAssignmentKey(source, sourceDay, sourcePeriod);
        var targetKey = BuildManualCrossAssignmentKey(target, targetDay, targetPeriod);
        if (sourceKey == targetKey)
            return Exit(CrossHoverState.Hidden("현재 선택한 수업입니다."), "same-assignment-key");
        if (IsAlreadyCrossed(sourceKey, targetKey))
            return Exit(CrossHoverState.Hidden("이미 크로스가 설정된 수업입니다."), "already-crossed-pair");
        if (IsAssignmentAlreadyCrossed(sourceKey)
            || IsAssignmentAlreadyCrossed(targetKey))
            return Exit(CrossHoverState.Hidden("이미 다른 수업과 크로스된 과목입니다. 크로스는 두 과목끼리만 설정할 수 있습니다."), "assignment-already-crossed");
        if (RequiresSameCourseAssignmentConflictCheck(source, target))
        {
            if (HasProfessorOverlap(source, target))
                return Exit(CrossHoverState.Hidden("교수 단일 배정 위반: 같은 교수 수업은 Cross할 수 없습니다."), "same-course-professor-overlap");
            if (HasRoomOverlap(source, target))
                return Exit(CrossHoverState.Hidden("강의실 단일 배정 위반: 같은 강의실 수업은 Cross할 수 없습니다."), "same-course-room-overlap");
        }

        var reasons = ValidateCrossMoveWithProvisionalLink(
            source,
            target,
            targetDay,
            targetPeriod,
            targetGrade,
            targetSubColumnIdx,
            null,
            sourceDay,
            sourcePeriod);
        if (reasons.Count > 0)
            return Exit(CrossHoverState.Hidden(reasons[0]), "validate-cross-move-failed");

        return Exit(CrossHoverState.Available(), "available");
    }

    public void HandleCrossAddRequested(
        int day,
        int period,
        int grade,
        int subColumnIdx,
        CellAssignment target)
    {
        var normalized = TryNormalizeCrossTarget(target, day, period, grade, subColumnIdx, out var normalizedTarget);
        var effectiveDay = normalized ? normalizedTarget.Day : day;
        var effectivePeriod = normalized ? normalizedTarget.Period : period;
        var effectiveGrade = normalized ? normalizedTarget.Grade : grade;
        var effectiveSubColumnIdx = normalized ? normalizedTarget.SubColumnIdx : subColumnIdx;
        var effectiveTarget = normalized ? normalizedTarget.Assignment : target;
        var state = EvaluateCrossHover(effectiveDay, effectivePeriod, effectiveGrade, effectiveSubColumnIdx, effectiveTarget);
        if (!state.CanCreate || SelectedAssignment == null)
        {
            if (!string.IsNullOrWhiteSpace(state.Reason))
                StatusMessage = state.Reason;
            return;
        }

        if (!TryCreateOrReplaceCrossAndMove(SelectedAssignment, effectiveTarget, effectiveDay, effectivePeriod, effectiveGrade, effectiveSubColumnIdx, out var reason))
        {
            StatusMessage = reason;
            return;
        }
    }

    public void HandleCrossDrop(
        int sourceDay,
        int sourcePeriod,
        int sourceGrade,
        int sourceSubColumnIdx,
        CellAssignment? source,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        CellAssignment? target)
    {
        if (source == null || target == null) return;
        SelectCell(sourceDay, sourcePeriod, sourceGrade, sourceSubColumnIdx, source);
        HandleCrossAddRequested(targetDay, targetPeriod, targetGrade, targetSubColumnIdx, target);
    }

    public bool CanDropMove(
        int sourceDay,
        int sourcePeriod,
        int sourceGrade,
        int sourceSubColumnIdx,
        CellAssignment? assignment,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx)
    {
        if (assignment == null) return false;
        return ValidateManualMove(
            assignment,
            sourceDay,
            sourcePeriod,
            targetDay,
            targetPeriod,
            targetGrade,
            targetSubColumnIdx,
            allowManualCrossOverlap: false,
            allowCrossDisplayColumn: false).Count == 0;
    }

    public bool HandleDropMove(
        int sourceDay,
        int sourcePeriod,
        int sourceGrade,
        int sourceSubColumnIdx,
        CellAssignment? assignment,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx)
    {
        if (assignment == null) return false;
        SelectCell(sourceDay, sourcePeriod, sourceGrade, sourceSubColumnIdx, assignment);
        return TryMoveSelectedTo(targetDay, targetPeriod, targetGrade, targetSubColumnIdx);
    }

    public SwapHoverState EvaluateSwapHover(
        int day,
        int period,
        int grade,
        int subColumnIdx,
        CellAssignment? target)
    {
        if (SelectedAssignment == null || SelectedDay is not int selectedDay || SelectedPeriod is not int selectedPeriod)
            return SwapHoverState.Hidden();
        if (target == null)
            return SwapHoverState.Hidden();
        if (IsSameMovingCell(
                new UnifiedCell(day, period, grade, subColumnIdx, target),
                SelectedAssignment,
                selectedDay,
                selectedPeriod))
            return SwapHoverState.Hidden("현재 선택한 수업입니다.");
        if (target.Grade != SelectedAssignment.Grade)
            return SwapHoverState.Hidden("같은 학년 수업끼리만 교환할 수 있습니다.");

        var reason = ValidateSwap(SelectedAssignment, selectedDay, selectedPeriod, target, day, period);
        return reason == null ? SwapHoverState.Available() : SwapHoverState.Hidden(reason);
    }

    public SwapHoverState EvaluateSwapDropHover(
        int sourceDay,
        int sourcePeriod,
        int sourceGrade,
        int sourceSubColumnIdx,
        CellAssignment? source,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        CellAssignment? target)
    {
        if (source == null || target == null)
            return SwapHoverState.Hidden();
        if (IsSameMovingCell(
                new UnifiedCell(targetDay, targetPeriod, targetGrade, targetSubColumnIdx, target),
                source,
                sourceDay,
                sourcePeriod))
            return SwapHoverState.Hidden("현재 선택한 수업입니다.");
        if (target.Grade != source.Grade)
            return SwapHoverState.Hidden("같은 학년 수업끼리만 교환할 수 있습니다.");

        var reason = ValidateSwap(source, sourceDay, sourcePeriod, target, targetDay, targetPeriod);
        return reason == null ? SwapHoverState.Available() : SwapHoverState.Hidden(reason);
    }

    public void HandleSwapRequested(
        int day,
        int period,
        int grade,
        int subColumnIdx,
        CellAssignment target)
    {
        var state = EvaluateSwapHover(day, period, grade, subColumnIdx, target);
        if (!state.CanSwap || SelectedAssignment == null || SelectedDay is not int selectedDay || SelectedPeriod is not int selectedPeriod)
        {
            if (!string.IsNullOrWhiteSpace(state.Reason))
                StatusMessage = state.Reason;
            return;
        }

        if (!TrySwapSelectedWith(target, day, period, out var reason))
            StatusMessage = reason;
    }

    public void HandleSwapDrop(
        int sourceDay,
        int sourcePeriod,
        int sourceGrade,
        int sourceSubColumnIdx,
        CellAssignment? source,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        CellAssignment? target)
    {
        if (source == null || target == null) return;
        SelectCell(sourceDay, sourcePeriod, sourceGrade, sourceSubColumnIdx, source);
        HandleSwapRequested(targetDay, targetPeriod, targetGrade, targetSubColumnIdx, target);
    }

    public void SelectCell(int day, int period, int grade, int subColumnIdx, CellAssignment? assignment)
    {
        RoomChangeStatusMessage = "";
        SelectedDay = day;
        SelectedPeriod = period;
        SelectedAssignment = assignment;
        _selectedGridCell = assignment == null ? null : new UnifiedCellKey(day, period, grade, subColumnIdx);
        OnPropertyChanged(nameof(SelectedSlotDisplayText));
        NotifySelectionDetailChanged();

        if (assignment != null)
        {
            NewRoomId = assignment.Rooms.FirstOrDefault() ?? "";
            SelectedOldRoomId = NewRoomId;
            SelectedReplacementRoomId = ReplacementRoomCandidates.FirstOrDefault() ?? "";
            StatusMessage = $"선택: {assignment.CourseName} ({assignment.CourseId}) — "
                + $"{DayName(day)} {period}교시";
            Grid.SetEditState(_selectedGridCell, BuildMoveStates(assignment, day, period));
        }
        else
        {
            NewRoomId = "";
            SelectedOldRoomId = "";
            SelectedReplacementRoomId = "";
            StatusMessage = $"{DayName(day)} {period}교시 — 비어있음";
            Grid.ClearEditState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyRoomChange))]
    private void ApplyRoomChange()
    {
        RoomChangeStatusMessage = "";
        if (HasMultipleAssignedRooms)
            ApplyMultiRoomReplacement();
        else
            ApplySingleRoomChange();
    }

    private void ApplySingleRoomChange()
    {
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null)
        {
            SetRoomChangeFailure("수업을 선택하세요.");
            return;
        }
        var cid = SelectedAssignment.CourseId;
        int day = SelectedDay.Value;
        int period = SelectedPeriod.Value;

        var oldRoomId = SelectedAssignment.Rooms.FirstOrDefault();
        var replacementRoomId = NewRoomId;
        if (string.IsNullOrWhiteSpace(replacementRoomId))
        {
            SetRoomChangeFailure("새 강의실을 선택하세요.");
            return;
        }
        if (oldRoomId == null)
        {
            SetRoomChangeFailure("변경할 배정 항목을 찾지 못했습니다.");
            return;
        }
        if (oldRoomId == replacementRoomId)
        {
            SetRoomChangeFailure("기존 강의실과 동일합니다.");
            return;
        }

        if (!RoomExists(replacementRoomId))
        {
            SetRoomChangeFailure("존재하지 않는 강의실입니다.");
            return;
        }

        if (!TryApplyRoomChange(oldRoomId, replacementRoomId, out var changed))
        {
            return;
        }

        RefreshSelectionAfterRoomChange(cid, day, period);
        NewRoomId = SelectedAssignment.Rooms.FirstOrDefault() ?? "";
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(SelectedAssignment, day, period));
        SetRoomChangeSuccess("강의실을 변경했습니다.");
    }

    private void ApplyMultiRoomReplacement()
    {
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null)
        {
            SetRoomChangeFailure("수업을 선택하세요.");
            return;
        }
        int day = SelectedDay.Value;
        int period = SelectedPeriod.Value;

        var oldRoomId = SelectedOldRoomId;
        var replacementRoomId = SelectedReplacementRoomId;
        if (string.IsNullOrWhiteSpace(oldRoomId))
        {
            SetRoomChangeFailure("교체할 기존 강의실을 선택하세요.");
            return;
        }
        if (string.IsNullOrWhiteSpace(replacementRoomId))
        {
            SetRoomChangeFailure("새 강의실을 선택하세요.");
            return;
        }
        if (oldRoomId == replacementRoomId)
        {
            SetRoomChangeFailure("기존 강의실과 동일합니다.");
            return;
        }

        if (!AssignedRoomsForSelectedAssignment.Contains(oldRoomId))
        {
            SetRoomChangeFailure("선택한 기존 강의실이 현재 배정 목록에 없습니다.");
            return;
        }

        if (AssignedRoomsForSelectedAssignment.Contains(replacementRoomId))
        {
            SetRoomChangeFailure("새 강의실이 이미 이 수업에 배정되어 있습니다.");
            return;
        }

        if (!RoomExists(replacementRoomId))
        {
            SetRoomChangeFailure("존재하지 않는 강의실입니다.");
            return;
        }

        if (!TryApplyRoomChange(oldRoomId, replacementRoomId, out var changed))
        {
            return;
        }

        RefreshSelectionAfterRoomChange(SelectedAssignment.CourseId, day, period);
        NewRoomId = SelectedAssignment.Rooms.FirstOrDefault() ?? "";
        SelectedOldRoomId = replacementRoomId;
        SelectedReplacementRoomId = ReplacementRoomCandidates.FirstOrDefault() ?? "";
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(SelectedAssignment, day, period));
        SetRoomChangeSuccess("강의실을 변경했습니다.");
    }

    private bool TryApplyRoomChange(string oldRoomId, string replacementRoomId, out int changed)
    {
        changed = 0;
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null) return false;
        var cid = SelectedAssignment.CourseId;
        int day = SelectedDay.Value;
        int period = SelectedPeriod.Value;

        var snapshot = CaptureSnapshot();
        var candidate = _working.ToList();
        foreach (var i in FindWorkingAssignmentsForRoomChange(candidate, cid, day, period, SelectedAssignment.RowSpan, oldRoomId))
        {
            var a = candidate[i];
            candidate[i] = new SolutionAssignment(cid, day, a.Period, replacementRoomId);
            changed++;
        }

        if (changed == 0)
        {
            SetRoomChangeFailure(BuildRoomChangeTargetNotFoundMessage(cid, day, period, SelectedAssignment.RowSpan, oldRoomId, replacementRoomId));
            return false;
        }

        var newErrors = DetectNewConflicts(_working, candidate)
            .Where(c => c.Severity == ConflictSeverity.Error)
            .ToList();
        if (newErrors.Count > 0)
        {
            SetRoomChangeFailure(newErrors[0].Type == ConflictType.RoomConflict
                ? "해당 시간에 이미 사용 중인 강의실입니다."
                : ToUserReason(newErrors[0]));
            return false;
        }

        _working = candidate;
        if (!TryCommitChange(snapshot, $"{cid}의 강의실 {oldRoomId} → {replacementRoomId} (변경 {changed}건)"))
        {
            return false;
        }

        return true;
    }

    private void SetRoomChangeFailure(string message)
    {
        RoomChangeStatusMessage = message;
        StatusMessage = message;
    }

    private void SetRoomChangeSuccess(string message)
    {
        RoomChangeStatusMessage = message;
        StatusMessage = message;
    }

    private IReadOnlyList<int> FindWorkingAssignmentsForRoomChange(
        IReadOnlyList<SolutionAssignment> assignments,
        string courseId,
        int day,
        int selectedPeriod,
        int rowSpan,
        string oldRoomId)
    {
        var direct = Enumerable.Range(0, assignments.Count)
            .Where(i => MatchesSelectedBlock(assignments[i], courseId, day, selectedPeriod, rowSpan, oldRoomId))
            .ToList();
        if (direct.Count >= Math.Max(1, rowSpan)) return direct;

        var sameRoomPeriods = assignments
            .Where(a => a.CourseId == courseId && a.Day == day && a.RoomId == oldRoomId)
            .Select(a => a.Period)
            .Distinct()
            .OrderBy(p => p)
            .ToList();
        if (!sameRoomPeriods.Contains(selectedPeriod)) return direct;

        var blockStart = selectedPeriod;
        while (sameRoomPeriods.Contains(blockStart - 1))
            blockStart--;

        var blockPeriods = Enumerable.Range(blockStart, Math.Max(1, rowSpan)).ToHashSet();
        return Enumerable.Range(0, assignments.Count)
            .Where(i => assignments[i].CourseId == courseId
                && assignments[i].Day == day
                && assignments[i].RoomId == oldRoomId
                && blockPeriods.Contains(assignments[i].Period))
            .ToList();
    }

    private static bool MatchesSelectedBlock(
        SolutionAssignment assignment,
        string courseId,
        int day,
        int selectedPeriod,
        int rowSpan,
        string oldRoomId) =>
        assignment.CourseId == courseId
        && assignment.Day == day
        && assignment.RoomId == oldRoomId
        && assignment.Period >= selectedPeriod
        && assignment.Period < selectedPeriod + rowSpan;

    private string BuildRoomChangeTargetNotFoundMessage(
        string courseId,
        int day,
        int selectedPeriod,
        int rowSpan,
        string oldRoomId,
        string newRoomId)
    {
        var sameCourseDayCount = _working.Count(a => a.CourseId == courseId && a.Day == day);
        var sameCourseDayRoomCount = _working.Count(a => a.CourseId == courseId && a.Day == day && a.RoomId == oldRoomId);
        return "변경 불가 — 변경할 배정 항목을 찾지 못했습니다. "
            + $"CourseId={courseId}, Day={day}, SelectedPeriod={selectedPeriod}, RowSpan={rowSpan}, "
            + $"OldRoomId={oldRoomId}, NewRoomId={newRoomId}, "
            + $"SameCourseDay={sameCourseDayCount}, SameCourseDayRoom={sameCourseDayRoomCount}";
    }

    private void RefreshSelectionAfterRoomChange(string courseId, int day, int selectedPeriod)
    {
        var cell = Grid.Cells.FirstOrDefault(c =>
            c.Day == day
            && c.Assignment.CourseId == courseId
            && selectedPeriod >= c.Period
            && selectedPeriod < c.Period + c.Assignment.RowSpan);
        if (cell == null) return;

        SelectedDay = cell.Day;
        SelectedPeriod = cell.Period;
        SelectedAssignment = cell.Assignment;
        _selectedGridCell = new UnifiedCellKey(cell.Day, cell.Period, cell.Grade, cell.SubColumnIdx);
        OnPropertyChanged(nameof(SelectedSlotDisplayText));
        NotifySelectionDetailChanged();
    }

    [RelayCommand(CanExecute = nameof(CanClearSelection))]
    private void ClearSelection()
    {
        if (SelectedAssignment != null)
            RemoveCrossForCourse(SelectedAssignment.CourseId, null);
        ClearSelectionCore();
        StatusMessage = "선택이 해제되었습니다.";
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void UndoLastMove()
    {
        Undo();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_undoStack.Pop());
        StatusMessage = "뒤로가기를 실행했습니다.";
        NotifyUndoRedoCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(CaptureSnapshot());
        RestoreSnapshot(_redoStack.Pop());
        StatusMessage = "앞으로가기를 실행했습니다.";
        NotifyUndoRedoCanExecuteChanged();
    }

    [RelayCommand]
    private void Reset()
    {
        if (_resetBaselineSnapshot == null) return;
        PushUndoSnapshot();
        RestoreResetBaseline();
        ClearSelectionCore();
        Rerender();
        StatusMessage = "베이스 시간표로 초기화했습니다.";
    }

    public event EventHandler? SavedRequested;

    [RelayCommand(CanExecute = nameof(CanSaveTimetable))]
    private void SaveTimetable()
    {
        if (!ValidateBeforeSave()) return;

        _workspace.SaveTimetable(SaveName.Trim(), _working, ToSavedManualCrossLinks(), BuildSaveSnapshot());
        StatusMessage = $"'{SaveName.Trim()}' 저장 완료";
        SaveName = "";
        SavedRequested?.Invoke(this, EventArgs.Empty);
    }

    private AppData? BuildSaveSnapshot()
    {
        if (_sessionData == null)
            return null;

        var rooms = _sessionData.Rooms.ToList();
        var knownRoomIds = rooms.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var roomId in _working
            .Select(a => a.RoomId)
            .Where(roomId => !string.IsNullOrWhiteSpace(roomId))
            .Distinct(StringComparer.Ordinal))
        {
            if (knownRoomIds.Contains(roomId)) continue;
            var workspaceRoom = _workspace.Rooms.FirstOrDefault(r => r.Id == roomId);
            if (workspaceRoom == null) continue;
            rooms.Add(workspaceRoom);
            knownRoomIds.Add(roomId);
        }

        return _sessionData with { Rooms = rooms };
    }

    private bool CanSaveTimetable() => !string.IsNullOrWhiteSpace(SaveName) && BaseSolution != null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportXlsx(string path)
    {
        var rows = _working.Select(a => new TimetableAssignmentRow(a.CourseId, a.Day, a.Period, a.RoomId)).ToList();
        var name = string.IsNullOrWhiteSpace(SaveName) ? "시간표" : SaveName.Trim();
        FormattedTimetableExporter.Export(name, rows, SessionCourses, SessionProfessors, path, SessionRooms);
    }

    private bool CanExport() => BaseSolution != null;

    private bool ValidateBeforeSave()
    {
        RefreshConflicts(strictManualCrossValidation: true);
        var errorCount = Conflicts.Count(c => c.Severity == ConflictSeverity.Error);
        if (errorCount == 0) return true;

        StatusMessage = $"저장 차단: 제약조건 위반 Error {errorCount}건이 남아 있습니다. 위반 항목을 수정한 뒤 다시 저장하세요.";
        return false;
    }

    private bool TryMoveSelectedTo(int targetDay, int targetPeriod, int targetGrade, int targetSubColumnIdx)
    {
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null) return false;

        if (!TryMoveAssignment(
                SelectedAssignment,
                SelectedDay.Value,
                SelectedPeriod.Value,
                targetDay,
                targetPeriod,
                targetGrade,
                targetSubColumnIdx,
                out var candidate,
                out var reasons))
        {
            return TryForceMoveSelectedTo(targetDay, targetPeriod, targetGrade, targetSubColumnIdx, reasons);
        }

        var snapshot = CaptureSnapshot();
        var movedCourseId = SelectedAssignment.CourseId;
        _working = candidate;
        MarkUserEditedAfterLoad();
        var removedLinks = RemoveInvalidCrossesAfterMove(refreshLabels: false);
        Rerender();

        PushUndoSnapshot(snapshot);
        var warning = GetSoftWarning(targetDay, targetPeriod);
        StatusMessage = removedLinks > 0
            ? $"{movedCourseId} 이동 완료 — {DayName(targetDay)} {targetPeriod}교시. 수업 식별 또는 학년 조건 변경으로 크로스 관계가 해제되었습니다."
            : warning == null
            ? $"{movedCourseId} 이동 완료 — {DayName(targetDay)} {targetPeriod}교시"
            : $"{movedCourseId} 이동 완료 — {DayName(targetDay)} {targetPeriod}교시. {warning}";
        ClearSelectionCore();
        return true;
    }

    private bool TrySwapSelectedWith(
        CellAssignment target,
        int targetDay,
        int targetPeriod,
        out string reason)
    {
        reason = "";
        if (SelectedAssignment == null || SelectedDay is not int selectedDay || SelectedPeriod is not int selectedPeriod)
        {
            reason = "선택한 수업이 없습니다.";
            return false;
        }

        var validationReason = ValidateSwap(SelectedAssignment, selectedDay, selectedPeriod, target, targetDay, targetPeriod);
        if (validationReason != null)
        {
            reason = validationReason;
            return false;
        }

        var snapshot = CaptureSnapshot();
        var selectedCourseId = SelectedAssignment.CourseId;
        var targetCourseId = target.CourseId;
        var candidate = BuildSwappedCandidate(SelectedAssignment, selectedDay, selectedPeriod, target, targetDay, targetPeriod);
        var newConflicts = DetectNewConflicts(_working, candidate)
            .Concat(BuildSwapSoftWarnings(SelectedAssignment, selectedDay, selectedPeriod, target, targetDay, targetPeriod))
            .ToList();
        if (newConflicts.Count > 0 && !_dialog.ConfirmDespiteConflicts(newConflicts))
        {
            reason = "교환이 취소되었습니다.";
            return false;
        }

        _working = candidate;
        MarkUserEditedAfterLoad();
        Rerender();
        PushUndoSnapshot(snapshot);
        var removedLinks = RemoveInvalidCrossesAfterMove();
        StatusMessage = $"{selectedCourseId} ↔ {targetCourseId} 위치를 교환했습니다."
            + (removedLinks > 0 ? " 수업 식별 또는 학년 조건 변경으로 크로스 관계가 해제되었습니다." : "");
        ClearSelectionCore();
        return true;
    }

    private IReadOnlyList<ConflictItem> DetectNewConflicts(
        IReadOnlyList<SolutionAssignment> baseline,
        IReadOnlyList<SolutionAssignment> candidate)
    {
        var baselineKeys = ConflictKeys(DetectConflicts(baseline));
        return DetectConflicts(candidate)
            .Where(c => !baselineKeys.Contains(KeyOf(c)))
            .ToList();
    }

    private static IReadOnlyList<ConflictItem> BuildSwapSoftWarnings(
        CellAssignment selected,
        int selectedDay,
        int selectedPeriod,
        CellAssignment target,
        int targetDay,
        int targetPeriod)
    {
        var warnings = new List<ConflictItem>();
        AddWarning(selected, targetDay, targetPeriod);
        AddWarning(target, selectedDay, selectedPeriod);
        return warnings;

        void AddWarning(CellAssignment assignment, int day, int period)
        {
            var warning = GetSoftWarning(day, period);
            if (warning == null) return;
            warnings.Add(new ConflictItem(
                ConflictType.GradeConflict,
                ConflictSeverity.Warning,
                $"{assignment.CourseName}({assignment.CourseId}) 교환 후 주의 시간대: {warning}",
                day,
                period));
        }
    }

    private string? ValidateSwap(
        CellAssignment selected,
        int selectedDay,
        int selectedPeriod,
        CellAssignment target,
        int targetDay,
        int targetPeriod)
    {
        if (selected.CourseId == target.CourseId
            && selectedDay == targetDay
            && selectedPeriod == targetPeriod
            && selected.Rooms.SequenceEqual(target.Rooms))
            return "현재 선택한 수업입니다.";
        if (selected.Grade != target.Grade)
            return "같은 학년 수업끼리만 교환할 수 있습니다.";

        var selectedCourse = ResolveCourseForCellAssignment(selected);
        var targetCourse = ResolveCourseForCellAssignment(target);
        if (selectedCourse == null || targetCourse == null)
            return "수업 정보를 확인할 수 없습니다.";
        if (selectedCourse.IsFixed || targetCourse.IsFixed)
            return "고정 시간표 보존 위반: 고정된 수업은 교환할 수 없습니다.";
        if (!HasSameSwapDuration(selected, target))
            return "블록 시간이 같은 수업끼리만 교환할 수 있습니다.";

        var selectedAtTarget = GetOccupiedPeriods(targetPeriod, selected.RowSpan);
        var targetAtSelected = GetOccupiedPeriods(selectedPeriod, target.RowSpan);
        if (selectedAtTarget.Concat(targetAtSelected).Any(p => !Constants.Periods.Contains(p)))
            return "수업 블록이 시간표 범위를 벗어납니다.";
        if (selectedAtTarget.Concat(targetAtSelected).Contains(Constants.LunchPeriod))
            return "점심시간 보장 위반: 점심시간에는 수업을 배정할 수 없습니다.";

        var candidate = BuildSwappedCandidate(selected, selectedDay, selectedPeriod, target, targetDay, targetPeriod);
        if (WouldCreateOneHourAboveMultiHourPattern(candidate, new[] { selected.CourseId, target.CourseId }))
            return OneHourAboveTwoHourBlockedReason;

        return null;
    }

    private static bool HasSameSwapDuration(CellAssignment selected, CellAssignment target) =>
        GetSwapBlockDuration(selected) == GetSwapBlockDuration(target);

    private static int GetSwapBlockDuration(CellAssignment assignment) => assignment.RowSpan;

    private List<SolutionAssignment> BuildSwappedCandidate(
        CellAssignment selected,
        int selectedDay,
        int selectedPeriod,
        CellAssignment target,
        int targetDay,
        int targetPeriod)
    {
        var selectedAssignments = _working
            .Where(a => IsSameMovingAssignment(a, selected, selectedDay, selectedPeriod))
            .OrderBy(a => a.Period)
            .ToList();
        var targetAssignments = _working
            .Where(a => IsSameMovingAssignment(a, target, targetDay, targetPeriod))
            .OrderBy(a => a.Period)
            .ToList();

        var candidate = _working
            .Where(a => !IsSameMovingAssignment(a, selected, selectedDay, selectedPeriod)
                && !IsSameMovingAssignment(a, target, targetDay, targetPeriod))
            .ToList();

        foreach (var a in selectedAssignments.DistinctBy(a => a.Period))
        {
            var newPeriod = targetPeriod + (a.Period - selectedPeriod);
            candidate.Add(new SolutionAssignment(a.CourseId, targetDay, newPeriod, a.RoomId));
        }
        foreach (var a in targetAssignments.DistinctBy(a => a.Period))
        {
            var newPeriod = selectedPeriod + (a.Period - targetPeriod);
            candidate.Add(new SolutionAssignment(a.CourseId, selectedDay, newPeriod, a.RoomId));
        }

        return candidate;
    }

    private bool TryForceMoveSelectedTo(
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        IReadOnlyList<string> blockedReasons)
    {
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null) return false;
        var placementGuardReason = blockedReasons.FirstOrDefault(IsPlacementGuardReason);
        if (placementGuardReason != null)
        {
            StatusMessage = placementGuardReason;
            return false;
        }
        if (targetDay < 0 || targetDay >= Constants.Days || !Constants.Periods.Contains(targetPeriod))
        {
            StatusMessage = "화면에 존재하지 않는 시간 슬롯으로는 이동할 수 없습니다.";
            return false;
        }
        var targetPeriods = GetOccupiedPeriods(targetPeriod, SelectedAssignment.RowSpan);
        if (targetPeriods.Any(p => !Constants.Periods.Contains(p)))
        {
            StatusMessage = "수업 블록이 시간표 범위를 벗어납니다.";
            return false;
        }

        var snapshot = CaptureSnapshot();
        var movedCourseId = SelectedAssignment.CourseId;
        var candidate = BuildMovedCandidate(
            SelectedAssignment,
            SelectedDay.Value,
            SelectedPeriod.Value,
            targetDay,
            targetPeriod);
        var conflictsAfterMove = DetectConflicts(candidate).ToList();
        var dialogItems = conflictsAfterMove.Count > 0
            ? conflictsAfterMove
            : blockedReasons.Select(r => new ConflictItem(
                ConflictType.GradeConflict,
                ConflictSeverity.Error,
                r,
                targetDay,
                targetPeriod)).ToList();

        if (!_dialog.ConfirmDespiteConflicts(dialogItems))
        {
            StatusMessage = "강제 이동이 취소되었습니다.";
            return false;
        }

        _working = candidate;
        MarkUserEditedAfterLoad();
        var removedLinks = RemoveInvalidCrossesAfterMove(refreshLabels: false);
        Rerender();

        PushUndoSnapshot(snapshot);
        var errorCount = Conflicts.Count(c => c.Severity == ConflictSeverity.Error);
        var warningCount = Conflicts.Count(c => c.Severity == ConflictSeverity.Warning);
        StatusMessage = $"제약조건 위반 상태로 강제 이동했습니다. Error {errorCount}건, Warning {warningCount}건이 남아 있습니다. Error가 남아 있으면 저장할 수 없습니다."
            + (removedLinks > 0 ? " 수업 식별 또는 학년 조건 변경으로 크로스 관계가 해제되었습니다." : "");
        ClearSelectionCore();
        return true;
    }

    private IReadOnlyDictionary<UnifiedCellKey, EditCellState> BuildMoveStates(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod)
    {
        var states = new Dictionary<UnifiedCellKey, EditCellState>();
        foreach (var dayGroup in Grid.DayGroups)
        {
            foreach (var gradeColumn in dayGroup.Grades)
            {
                if (gradeColumn.Grade is not int grade) continue;
                for (int subColumnIdx = 0; subColumnIdx < gradeColumn.Width; subColumnIdx++)
                {
                    foreach (var p in Constants.Periods)
                    {
                        var key = new UnifiedCellKey(dayGroup.Day, p, grade, subColumnIdx);
                        if (_selectedGridCell == key)
                        {
                            states[key] = new EditCellState(ManualMoveCellState.Selected, "현재 위치입니다.");
                            continue;
                        }

                        if (grade != assignment.Grade)
                        {
                            states[key] = new EditCellState(ManualMoveCellState.Normal, "");
                            continue;
                        }

                        var reasons = ValidateManualMove(
                            assignment,
                            sourceDay,
                            sourcePeriod,
                            dayGroup.Day,
                            p,
                            grade,
                            subColumnIdx,
                            allowManualCrossOverlap: false,
                            allowCrossDisplayColumn: false);
                        if (reasons.Count > 0)
                        {
                            states[key] = new EditCellState(ManualMoveCellState.Blocked, reasons[0]);
                            continue;
                        }

                        var warning = GetSoftWarning(dayGroup.Day, p);
                        states[key] = warning == null
                            ? new EditCellState(ManualMoveCellState.Movable, "이동 가능")
                            : new EditCellState(ManualMoveCellState.Warning, warning);
                    }
                }
            }
        }
        return states;
    }

    private bool TryMoveAssignment(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        out List<SolutionAssignment> candidate,
        out IReadOnlyList<string> reasons,
        bool allowManualCrossOverlap = false,
        bool allowCrossDisplayColumn = false)
    {
        reasons = ValidateManualMove(
            assignment,
            sourceDay,
            sourcePeriod,
            targetDay,
            targetPeriod,
            targetGrade,
            targetSubColumnIdx,
            allowManualCrossOverlap,
            allowCrossDisplayColumn);
        if (reasons.Count > 0)
        {
            candidate = _working.ToList();
            return false;
        }

        candidate = BuildMovedCandidate(assignment, sourceDay, sourcePeriod, targetDay, targetPeriod);
        return true;
    }

    private IReadOnlyList<string> ValidateManualMove(
    CellAssignment assignment,
    int sourceDay,
    int sourcePeriod,
    int targetDay,
    int targetPeriod,
    int targetGrade,
    int targetSubColumnIdx,
    bool allowManualCrossOverlap = false,
    bool allowCrossDisplayColumn = false)
    {
        if (targetGrade != assignment.Grade)
            return new[] { "선택한 수업은 같은 학년 열 안에서만 이동할 수 있습니다." };

        var course = ResolveCourseForCellAssignment(assignment) ?? SessionCourses.First(c => c.Id == assignment.CourseId);
        if (course.IsFixed)
            return new[] { "고정 시간표 보존 위반: 고정된 수업은 이동할 수 없습니다." };

        var targetPeriods = GetOccupiedPeriods(targetPeriod, assignment.RowSpan);
        if (targetPeriods.Any(p => !Constants.Periods.Contains(p)))
            return new[] { "수업 블록이 시간표 범위를 벗어납니다." };
        if (targetPeriods.Contains(Constants.LunchPeriod))
            return new[] { "점심시간 보장 위반: 점심시간에는 수업을 배정할 수 없습니다." };
        if (!allowCrossDisplayColumn && IsCrossDisplayOnlyColumn(targetDay, targetPeriod, targetGrade, targetSubColumnIdx))
            return new[] { CrossDisplayColumnBlockedReason };
        if (WouldCreateOneHourAboveMultiHourPattern(
                assignment,
                sourceDay,
                sourcePeriod,
                targetDay,
                targetPeriod,
                targetGrade,
                targetSubColumnIdx))
            return new[] { OneHourAboveTwoHourBlockedReason };
        if (assignment.RowSpan == 2 && !Constants.Len2StartPeriods.Contains(targetPeriod))
            return new[] { "블록 시작 교시 위반: 2시간 수업은 1, 3, 6, 8교시에만 시작할 수 있습니다." };

        var blockingAssignments = GetBlockingAssignments(
            assignment,
            sourceDay,
            sourcePeriod,
            targetDay,
            targetPeriods,
            targetGrade,
            targetSubColumnIdx,
            allowManualCrossOverlap);
        if (blockingAssignments.Count > 0)
        {
            var blockingCourses = blockingAssignments
                .Select(a => ResolveCourseForAssignment(a))
                .Where(c => c != null)
                .Cast<Course>()
                .ToList();
            if (blockingCourses.Any(c => c.IsFixed))
                return new[] { "고정 시간표 보존 위반: 고정 수업 위로 이동할 수 없습니다." };

            return new[] { BlockRangeOverlapBlockedReason };
        }

        var candidate = BuildMovedCandidate(assignment, sourceDay, sourcePeriod, targetDay, targetPeriod);

        var baselineKeys = ConflictKeys(DetectConflicts(_working));
        var detectedConflicts = DetectConflicts(candidate)
            .Where(c => !baselineKeys.Contains(KeyOf(c)))
            .ToList();
        var newConflicts = detectedConflicts
            .Where(c =>
            {
                var allowed = IsAllowedManualCrossOverlapConflict(
                    c,
                    candidate,
                    assignment,
                    sourceDay,
                    sourcePeriod,
                    targetDay,
                    targetPeriods,
                    targetGrade,
                    allowManualCrossOverlap);
                return !allowed;
            })
            .ToList();

        return newConflicts.Select(ToUserReason).ToList();
    }

    private bool IsAllowedManualCrossOverlapConflict(
        ConflictItem conflict,
        IReadOnlyList<SolutionAssignment> candidate,
        CellAssignment movingAssignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        IReadOnlyList<int> targetPeriods,
        int targetGrade,
        bool allowManualCrossOverlap)
    {
        if (!allowManualCrossOverlap)
            return false;
        if (conflict.Type is not ConflictType.GradeConflict and not ConflictType.SectionConflict)
            return false;
        if (conflict.Day != targetDay || !targetPeriods.Contains(conflict.Period))
            return false;

        var conflictBaseId = GetSectionConflictBaseId(conflict);
        var movedKey = BuildManualCrossAssignmentKey(movingAssignment, targetDay, targetPeriods[0]);
        foreach (var link in _workingCrossLinks.Where(link => LinkContainsAssignmentIgnoringSlot(link, movedKey)))
        {
            var sourceBase = DomainHelpers.BaseId(link.SourceKey.CourseId);
            var targetBase = DomainHelpers.BaseId(link.TargetKey.CourseId);
            if (conflict.Type == ConflictType.SectionConflict)
            {
                if (string.IsNullOrWhiteSpace(conflictBaseId))
                    continue;
                if (!string.Equals(sourceBase, targetBase, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(conflictBaseId, sourceBase, StringComparison.Ordinal))
                    continue;
            }

            var involved = GetAssignmentsInConflictSlot(
                candidate,
                conflict,
                targetGrade,
                conflict.Type,
                link);
            if (involved.Count != 2)
                continue;

            var left = involved[0];
            var right = involved[1];
            var leftKey = BuildManualCrossAssignmentKey(left.Assignment, left.Day, left.Period);
            var rightKey = BuildManualCrossAssignmentKey(right.Assignment, right.Day, right.Period);
            var keyMatch = LinkMatchesPairIgnoringSlot(link, leftKey, rightKey);
            var sourceIdentityMatched = IsSameManualCrossAssignmentIgnoringSlot(link.SourceKey, leftKey)
                || IsSameManualCrossAssignmentIgnoringSlot(link.SourceKey, rightKey);
            var targetIdentityMatched = IsSameManualCrossAssignmentIgnoringSlot(link.TargetKey, leftKey)
                || IsSameManualCrossAssignmentIgnoringSlot(link.TargetKey, rightKey);
            if (!keyMatch)
                continue;
            if (left.Assignment.Grade != right.Assignment.Grade)
                continue;
            if (!HasSameCrossDuration(left.Assignment, right.Assignment))
                continue;
            if (IsSameManualCrossAssignmentIgnoringSlot(leftKey, rightKey))
                continue;
            if (HasProfessorOverlap(left.Assignment, right.Assignment))
                continue;
            if (HasRoomOverlap(left.Assignment, right.Assignment))
                continue;

            return true;
        }

        return false;
    }

    private List<UnifiedCell> GetAssignmentsInConflictSlot(
        IReadOnlyList<SolutionAssignment> candidate,
        ConflictItem conflict,
        int targetGrade,
        ConflictType conflictType,
        ManualCrossLink link)
    {
        var cellsAtConflict = RenderManualCells(candidate)
            .Where(c => c.Day == conflict.Day
                && c.Period <= conflict.Period
                && conflict.Period < c.Period + c.Assignment.RowSpan)
            .ToList();

        if (conflictType == ConflictType.SectionConflict)
        {
            var conflictBaseId = GetSectionConflictBaseId(conflict);
            cellsAtConflict = cellsAtConflict
                .Where(c =>
                {
                    var baseId = DomainHelpers.BaseId(c.Assignment.CourseId);
                    return string.Equals(baseId, conflictBaseId, StringComparison.Ordinal);
                })
                .ToList();
        }
        else
        {
            cellsAtConflict = cellsAtConflict
                .Where(c => c.Grade == targetGrade)
                .ToList();
        }

        return cellsAtConflict
            .DistinctBy(c => BuildManualCrossAssignmentKey(c.Assignment, c.Day, c.Period))
            .ToList();
    }

    private static bool LinkContainsAssignmentIgnoringSlot(ManualCrossLink link, ManualCrossAssignmentKey key) =>
        IsSameManualCrossAssignmentIgnoringSlot(link.SourceKey, key)
        || IsSameManualCrossAssignmentIgnoringSlot(link.TargetKey, key);

    private static bool LinkMatchesPairIgnoringSlot(
        ManualCrossLink link,
        ManualCrossAssignmentKey left,
        ManualCrossAssignmentKey right) =>
        (IsSameManualCrossAssignmentIgnoringSlot(link.SourceKey, left)
            && IsSameManualCrossAssignmentIgnoringSlot(link.TargetKey, right))
        || (IsSameManualCrossAssignmentIgnoringSlot(link.SourceKey, right)
            && IsSameManualCrossAssignmentIgnoringSlot(link.TargetKey, left));

    private static bool IsSameManualCrossAssignmentIgnoringSlot(
        ManualCrossAssignmentKey left,
        ManualCrossAssignmentKey right) =>
        left.CourseId == right.CourseId
        && left.Section == right.Section
        && left.RowSpan == right.RowSpan
        && left.RoomIdsKey == right.RoomIdsKey;

    private static string GetSectionConflictBaseId(ConflictItem conflict)
    {
        if (conflict.Type != ConflictType.SectionConflict)
            return "";

        var match = Regex.Match(
            conflict.Description,
            @"^(?<base>.+?)의\s+분반이\b",
            RegexOptions.CultureInvariant);
        return match.Success
            ? DomainHelpers.BaseId(match.Groups["base"].Value.Trim())
            : "";
    }

    private static List<int> GetOccupiedPeriods(int startPeriod, int rowSpan) =>
        Enumerable.Range(startPeriod, rowSpan).ToList();

    private static string? GetSoftWarning(int day, int period)
    {
        if (day == 0 && period is >= 1 and <= 4)
            return "주의 필요: 월요일 오전은 권장되지 않습니다.";
        if (day == 4 && period is >= 6 and <= 9)
            return "주의 필요: 금요일 오후는 권장되지 않습니다.";
        return null;
    }

    private bool WouldCreateOneHourAboveMultiHourPattern(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx)
    {
        if (WouldCreatePatternAroundTarget(assignment, targetDay, targetPeriod, targetGrade))
            return true;

        var baselinePatterns = GetOneHourAboveMultiHourPatterns(RenderManualCells(_working));
        var candidate = BuildMovedCandidate(assignment, sourceDay, sourcePeriod, targetDay, targetPeriod);
        var candidatePatterns = GetOneHourAboveMultiHourPatterns(RenderManualCells(candidate));

        return candidatePatterns.Any(p =>
            !baselinePatterns.Contains(p)
            && (p.OneHour.CourseId == assignment.CourseId || p.MultiHour.CourseId == assignment.CourseId));
    }

    private bool WouldCreateOneHourAboveMultiHourPattern(
        IReadOnlyList<SolutionAssignment> candidate,
        IReadOnlyCollection<string> changedCourseIds)
    {
        var baselinePatterns = GetOneHourAboveMultiHourPatterns(RenderManualCells(_working));
        var candidatePatterns = GetOneHourAboveMultiHourPatterns(RenderManualCells(candidate));
        return candidatePatterns.Any(p =>
            !baselinePatterns.Contains(p)
            && (changedCourseIds.Contains(p.OneHour.CourseId) || changedCourseIds.Contains(p.MultiHour.CourseId)));
    }

    private bool WouldCreatePatternAroundTarget(
        CellAssignment assignment,
        int targetDay,
        int targetPeriod,
        int targetGrade)
    {
        var currentCells = RenderManualCells(_working);
        if (assignment.RowSpan >= 2)
        {
            var abovePeriod = targetPeriod - 1;
            return Constants.Periods.Contains(abovePeriod)
                && currentCells.Any(c => c.Day == targetDay
                    && c.Period == abovePeriod
                    && c.Grade == targetGrade
                    && c.Assignment.RowSpan == 1
                    && IsSameLogicalCourseSection(c.Assignment, assignment));
        }

        if (assignment.RowSpan == 1)
        {
            var belowPeriod = targetPeriod + 1;
            return Constants.Periods.Contains(belowPeriod)
                && currentCells.Any(c => c.Day == targetDay
                    && c.Period == belowPeriod
                    && c.Grade == targetGrade
                    && c.Assignment.RowSpan >= 2
                    && IsSameLogicalCourseSection(assignment, c.Assignment));
        }

        return false;
    }

    private IReadOnlyList<UnifiedCell> RenderManualCells(IReadOnlyList<SolutionAssignment> assignments)
    {
        var rendered = new UnifiedTimetableViewModel
        {
            ExpandAllGrades = Grid.ExpandAllGrades,
            MergeOnlyStructuredBlocks = Grid.MergeOnlyStructuredBlocks,
        };
        rendered.SetCrossParallelOrder(BuildCrossParallelOrder());
        rendered.Render(assignments, SessionCourses, SessionProfessors, SessionRooms);
        return rendered.Cells.ToList();
    }

    private static HashSet<OneHourAboveMultiHourPattern> GetOneHourAboveMultiHourPatterns(
        IEnumerable<UnifiedCell> cells)
    {
        var cellList = cells.ToList();
        var patterns = new HashSet<OneHourAboveMultiHourPattern>();
        foreach (var oneHourCell in cellList.Where(c => c.Assignment.RowSpan == 1))
        {
            var belowPeriod = oneHourCell.Period + 1;
            if (!Constants.Periods.Contains(belowPeriod)) continue;

            foreach (var multiHourCell in cellList.Where(c =>
                         c.Day == oneHourCell.Day
                         && c.Grade == oneHourCell.Grade
                         && c.Period == belowPeriod
                         && c.Assignment.RowSpan >= 2
                         && IsSameLogicalCourseSection(oneHourCell.Assignment, c.Assignment)))
            {
                patterns.Add(new OneHourAboveMultiHourPattern(
                    oneHourCell.Day,
                    oneHourCell.Grade,
                    oneHourCell.Period,
                    PatternBlock.From(oneHourCell.Assignment),
                    PatternBlock.From(multiHourCell.Assignment)));
            }
        }

        return patterns;
    }

    private static bool IsSameLogicalCourseSection(CellAssignment left, CellAssignment right)
    {
        if (left.Grade != right.Grade) return false;
        if (left.Section <= 0 || right.Section <= 0) return false;

        var sameCourseId = string.Equals(left.CourseId, right.CourseId, StringComparison.OrdinalIgnoreCase);
        var sameCourseName = string.Equals(left.CourseName.Trim(), right.CourseName.Trim(), StringComparison.OrdinalIgnoreCase);
        var sameSection = left.Section == right.Section;
        var sameProfessor = !string.IsNullOrWhiteSpace(ProfessorIdentity(left))
            && string.Equals(ProfessorIdentity(left), ProfessorIdentity(right), StringComparison.Ordinal);

        return (sameCourseId || sameCourseName)
            && sameSection
            && sameProfessor;
    }

    private readonly record struct OneHourAboveMultiHourPattern(
        int Day,
        int Grade,
        int OneHourPeriod,
        PatternBlock OneHour,
        PatternBlock MultiHour);

    private readonly record struct PatternBlock(
        string CourseId,
        string CourseName,
        int Grade,
        int Section,
        string ProfessorKey,
        string RoomsKey)
    {
        public static PatternBlock From(CellAssignment assignment) =>
            new(
                assignment.CourseId,
                assignment.CourseName,
                assignment.Grade,
                assignment.Section,
                ProfessorIdentity(assignment),
                string.Join("|", assignment.Rooms.OrderBy(r => r, StringComparer.Ordinal)));
    }

    private static string ProfessorIdentity(CellAssignment assignment) =>
        string.Join("|", new[] { assignment.ProfessorId }
            .Concat(assignment.CoteachProfIds)
            .Where(pid => !string.IsNullOrWhiteSpace(pid))
            .Select(pid => pid.Trim().ToUpperInvariant())
            .OrderBy(pid => pid, StringComparer.Ordinal));

    private static bool IsPlacementGuardReason(string reason) =>
        reason == OneHourAboveTwoHourBlockedReason
        || reason == CrossDisplayColumnBlockedReason
        || reason == BlockRangeOverlapBlockedReason;

    private bool IsCrossDisplayOnlyColumn(int targetDay, int targetPeriod, int targetGrade, int targetSubColumnIdx)
    {
        if (targetSubColumnIdx <= 0) return false;

        // Sub-columns beyond 0 are created to display parallel/Cross blocks.  If there is
        // no real visual block occupying this exact row range, it must not become a
        // separate "empty" move target.
        return !Grid.Cells.Any(c =>
            c.Day == targetDay
            && c.Grade == targetGrade
            && c.SubColumnIdx == targetSubColumnIdx
            && targetPeriod >= c.Period
            && targetPeriod < c.Period + c.Assignment.RowSpan);
    }

    private List<SolutionAssignment> GetBlockingAssignments(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        IReadOnlyList<int> targetPeriods,
        int targetGrade,
        int targetSubColumnIdx,
        bool allowManualCrossOverlap)
    {
        if (allowManualCrossOverlap)
            return new List<SolutionAssignment>();

        var occupiedCells = Grid.Cells
            .Where(c => c.Day == targetDay
                && c.Grade == targetGrade
                && targetPeriods.Any(p => p >= c.Period && p < c.Period + c.Assignment.RowSpan))
            .Where(c => !IsSameMovingCell(c, assignment, sourceDay, sourcePeriod))
            .ToList();

        var blockedCourseIds = occupiedCells
            .Select(c => c.Assignment.CourseId)
            .ToHashSet();

        return _working
            .Where(a => blockedCourseIds.Contains(a.CourseId))
            .Where(a => a.Day == targetDay)
            .ToList();
    }

    private bool HasOccupiedTargetRangeConflict(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx)
    {
        var movingAssignments = _working
            .Where(a => IsSameMovingAssignment(a, assignment, sourceDay, sourcePeriod))
            .OrderBy(a => a.Period)
            .ToList();
        if (movingAssignments.Count <= 1)
        {
            var rooms = assignment.Rooms.ToHashSet(StringComparer.Ordinal);
            movingAssignments = _working
                .Where(a => a.CourseId == assignment.CourseId
                    && a.Day == sourceDay
                    && a.Period >= sourcePeriod
                    && a.Period < sourcePeriod + assignment.HoursPerWeek
                    && rooms.Contains(a.RoomId))
                .OrderBy(a => a.Period)
                .ToList();
        }
        var effectiveRowSpan = Math.Max(assignment.RowSpan, assignment.HoursPerWeek);
        if (movingAssignments.Count <= 1 && effectiveRowSpan <= 1)
            return false;

        var targetPeriods = movingAssignments.Count > 1
            ? movingAssignments
                .Select(a => targetPeriod + (a.Period - sourcePeriod))
                .ToList()
            : GetOccupiedPeriods(targetPeriod, effectiveRowSpan);

        return GetBlockingAssignments(
                assignment,
                sourceDay,
                sourcePeriod,
                targetDay,
                targetPeriods,
                targetGrade,
                targetSubColumnIdx,
                allowManualCrossOverlap: false)
            .Count > 0;
    }

    private List<SolutionAssignment> BuildMovedCandidate(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        int targetPeriod)
    {
        var movingAssignments = _working
            .Where(a => IsSameMovingAssignment(a, assignment, sourceDay, sourcePeriod))
            .OrderBy(a => a.Period)
            .ToList();

        var candidate = _working
            .Where(a => !IsSameMovingAssignment(a, assignment, sourceDay, sourcePeriod))
            .ToList();

        foreach (var a in movingAssignments)
            candidate.Add(new SolutionAssignment(
                a.CourseId,
                targetDay,
                targetPeriod + (a.Period - sourcePeriod),
                a.RoomId));
        return candidate;
    }

    private static bool IsSameMovingAssignment(
        SolutionAssignment existing,
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod)
    {
        if (existing.CourseId != assignment.CourseId || existing.Day != sourceDay) return false;
        if (existing.Period < sourcePeriod || existing.Period >= sourcePeriod + assignment.RowSpan) return false;
        return assignment.Rooms.Contains(existing.RoomId);
    }

    private static bool IsSameMovingCell(
        UnifiedCell cell,
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod) =>
        BuildManualCrossAssignmentKey(cell.Assignment, cell.Day, cell.Period)
            == BuildManualCrossAssignmentKey(assignment, sourceDay, sourcePeriod);

    private bool TryCommitChange(ManualEditSnapshot snapshot, string successMessage)
    {
        Rerender();
        var baselineKeys = ConflictKeys(DetectConflicts(snapshot.Assignments));
        var newOnes = Conflicts.Where(c => !baselineKeys.Contains(KeyOf(c.Conflict))).ToList();

        if (newOnes.Count > 0)
        {
            bool proceed = _dialog.ConfirmDespiteConflicts(newOnes.Select(c => c.Conflict).ToList());
            if (!proceed)
            {
                RestoreSnapshot(snapshot);
                Rerender();
                StatusMessage = $"변경 취소됨 — {ToUserReason(newOnes[0].Conflict)}";
                return false;
            }
        }

        MarkUserEditedAfterLoad();
        RefreshConflicts();
        PushUndoSnapshot(snapshot);
        StatusMessage = successMessage
            + (newOnes.Count > 0 ? $" — 신규 위반 {newOnes.Count}건 수용" : "");
        return true;
    }

    private void Rerender()
    {
        Grid.SetCrossParallelOrder(BuildCrossParallelOrder());
        Grid.Render(_working, SessionCourses, SessionProfessors, SessionRooms);
        Grid.SetCrossLinkLabels(BuildCrossLinkLabels());
        RenderBreakdownViews();
        RefreshConflicts();
    }

    private void RefreshConflicts(bool strictManualCrossValidation = false)
    {
        Conflicts.Clear();
        foreach (var c in DetectConflicts(_working, strictManualCrossValidation)
            .Where(c => !IsAllowedExistingManualCrossSectionConflict(c, _working)))
            Conflicts.Add(BuildConflictDisplayItem(c));
    }

    private ConflictDisplayItem BuildConflictDisplayItem(ConflictItem conflict)
    {
        var title = BuildConflictDisplayTitle(conflict);
        var description = StripConstraintCodeForDisplay(conflict.Description);
        var detail = string.IsNullOrWhiteSpace(description)
            ? $"제약조건: {conflict.Type}"
            : $"제약조건: {conflict.Type} · {description}";
        return new ConflictDisplayItem(conflict, title, detail);
    }

    private string BuildConflictDisplayTitle(ConflictItem conflict)
    {
        var representative = ResolveRepresentativeConflictAssignment(conflict);
        if (representative != null)
        {
            var (assignment, course) = representative.Value;
            var courseLabel = BuildConflictCourseLabel(course, assignment.CourseId);
            var range = ResolveConflictPeriodRange(assignment, conflict);
            var timeLabel = FormatConflictTimeLabel(conflict.Day, range.Start, range.End);
            var gradeLabel = course?.Grade > 0
                ? $"{course.Grade}학년"
                : assignmentCourseGrade(assignment) is int grade && grade > 0
                    ? $"{grade}학년"
                    : "";

            return string.IsNullOrWhiteSpace(gradeLabel)
                ? $"{courseLabel} / {timeLabel}"
                : $"{courseLabel} / {timeLabel} / {gradeLabel}";
        }

        var fallbackCourseId = ResolveFallbackConflictCourseId(conflict);
        var fallbackTime = IsDisplayableSlot(conflict.Day, conflict.Period)
            ? FormatConflictTimeLabel(conflict.Day, conflict.Period, conflict.Period)
            : "";
        if (!string.IsNullOrWhiteSpace(fallbackCourseId) && !string.IsNullOrWhiteSpace(fallbackTime))
            return $"{fallbackCourseId} / {fallbackTime}";
        if (!string.IsNullOrWhiteSpace(fallbackCourseId))
            return fallbackCourseId;
        if (!string.IsNullOrWhiteSpace(conflict.Type.ToString()))
            return conflict.Type.ToString();
        return "제약조건 위반";

        int? assignmentCourseGrade(SolutionAssignment assignment) =>
            ResolveCourseForAssignment(assignment)?.Grade;
    }

    private (SolutionAssignment Assignment, Course? Course)? ResolveRepresentativeConflictAssignment(ConflictItem conflict)
    {
        var slotAssignments = _working
            .Where(a => a.Day == conflict.Day && a.Period == conflict.Period)
            .ToList();
        if (slotAssignments.Count == 0)
            return null;

        var conflictCourseIds = ExtractConflictCourseIds(conflict).ToList();
        if (conflictCourseIds.Count > 0)
        {
            var byCourseId = slotAssignments
                .Where(a => conflictCourseIds.Contains(a.CourseId, StringComparer.Ordinal))
                .ToList();
            if (byCourseId.Count > 0)
                slotAssignments = byCourseId
                    .OrderBy(a => conflictCourseIds.IndexOf(a.CourseId))
                    .ToList();
        }
        else if (conflict.Type == ConflictType.SectionConflict)
        {
            var baseId = GetSectionConflictBaseId(conflict);
            if (!string.IsNullOrWhiteSpace(baseId))
            {
                var byBaseId = slotAssignments
                    .Where(a => string.Equals(DomainHelpers.BaseId(a.CourseId), baseId, StringComparison.Ordinal))
                    .ToList();
                if (byBaseId.Count > 0)
                    slotAssignments = byBaseId;
            }
        }

        var assignment = slotAssignments
            .OrderBy(a => a.CourseId, StringComparer.Ordinal)
            .ThenBy(a => a.RoomId, StringComparer.Ordinal)
            .First();
        return (assignment, ResolveCourseForAssignment(assignment));
    }

    private (int Start, int End) ResolveConflictPeriodRange(SolutionAssignment assignment, ConflictItem conflict)
    {
        var periods = _working
            .Where(a => a.CourseId == assignment.CourseId
                && a.Day == assignment.Day
                && a.RoomId == assignment.RoomId)
            .Select(a => a.Period)
            .Distinct()
            .ToHashSet();
        if (!periods.Contains(conflict.Period))
            return (conflict.Period, conflict.Period);

        var start = conflict.Period;
        while (periods.Contains(start - 1))
            start--;

        var end = conflict.Period;
        while (periods.Contains(end + 1))
            end++;

        return (start, end);
    }

    private static string BuildConflictCourseLabel(Course? course, string courseId)
    {
        var label = string.IsNullOrWhiteSpace(course?.Name) ? courseId : course.Name.Trim();
        if (string.IsNullOrWhiteSpace(label))
            label = courseId;
        if (course?.Section > 0)
            label += $" {course.Section}분반";
        return string.IsNullOrWhiteSpace(label) ? "제약조건 위반" : label;
    }

    private static string FormatConflictTimeLabel(int day, int startPeriod, int endPeriod)
    {
        var periodLabel = startPeriod == endPeriod
            ? $"{startPeriod}교시"
            : $"{startPeriod}~{endPeriod}교시";
        return $"{DayName(day)} {periodLabel}";
    }

    private static bool IsDisplayableSlot(int day, int period) =>
        day is >= 0 and <= 4 && period > 0;

    private static string ResolveFallbackConflictCourseId(ConflictItem conflict) =>
        ExtractConflictCourseIds(conflict).FirstOrDefault()
        ?? GetSectionConflictBaseId(conflict);

    private static IEnumerable<string> ExtractConflictCourseIds(ConflictItem conflict)
    {
        var description = conflict.Description ?? "";
        var suffixIndex = description.LastIndexOf("중복:", StringComparison.Ordinal);
        if (suffixIndex >= 0)
        {
            foreach (var token in description[(suffixIndex + "중복:".Length)..]
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(token))
                    yield return token;
            }
        }

        foreach (Match match in Regex.Matches(description, @"\(([^)]+)\)"))
        {
            var value = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }

    private bool IsAllowedExistingManualCrossSectionConflict(
        ConflictItem conflict,
        IReadOnlyList<SolutionAssignment> assignments)
    {
        if (conflict.Type != ConflictType.SectionConflict)
            return false;

        foreach (var link in _workingCrossLinks)
        {
            var involved = GetAssignmentsInConflictSlot(
                assignments,
                conflict,
                targetGrade: 0,
                ConflictType.SectionConflict,
                link);
            if (involved.Count != 2)
                continue;

            var left = involved[0];
            var right = involved[1];
            var leftKey = BuildManualCrossAssignmentKey(left.Assignment, left.Day, left.Period);
            var rightKey = BuildManualCrossAssignmentKey(right.Assignment, right.Day, right.Period);
            if (!LinkMatchesPairIgnoringSlot(link, leftKey, rightKey))
                continue;
            if (left.Assignment.Grade != right.Assignment.Grade)
                continue;
            if (!HasSameCrossDuration(left.Assignment, right.Assignment))
                continue;
            if (IsSameManualCrossAssignmentIgnoringSlot(leftKey, rightKey))
                continue;
            if (HasProfessorOverlap(left.Assignment, right.Assignment))
                continue;
            if (HasRoomOverlap(left.Assignment, right.Assignment))
                continue;

            return true;
        }

        return false;
    }

    private void RenderBreakdownViews()
    {
        var courses = SessionCourses;
        var professors = SessionProfessors;
        var rooms = SessionRooms;

        GradeViews.Clear();
        foreach (var g in new[] { 1, 2, 3, 4 })
        {
            var vm = new TimetableGridViewModel();
            vm.Render(_working, courses, (c, _) => c.Grade == g, professors, rooms);
            GradeViews.Add(new NamedGridViewModel(g.ToString(), $"{g}학년", vm));
        }

        RoomViews.Clear();
        foreach (var r in rooms)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(_working, courses, (_, rid) => rid == r.Id, professors, rooms);
            RoomViews.Add(new NamedGridViewModel(r.Id, r.Name, vm));
        }

        ProfessorViews.Clear();
        foreach (var p in professors)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(_working, courses, (c, _) => IsCourseTaughtBy(c, p.Id), professors, rooms);
            ProfessorViews.Add(new NamedGridViewModel(p.Id, p.Name, vm));
        }
    }

    private static bool IsCourseTaughtBy(Course course, string professorId) =>
        course.ProfessorId == professorId || course.CoteachProfs.Contains(professorId);

    private IReadOnlyList<ConflictItem> DetectConflicts(
        IReadOnlyList<SolutionAssignment> assignment,
        bool strictManualCrossValidation = false) =>
        ConflictDetector.Detect(
            assignment,
            SessionCourses,
            SessionProfessors,
            _workingCrossGroups,
            strictManualCrossValidation ? IsManualGradeOverlapAllowedStrict : IsManualGradeOverlapAllowed);

    private void ClearSelectionCore()
    {
        SelectedAssignment = null;
        SelectedDay = null;
        SelectedPeriod = null;
        _selectedGridCell = null;
        NewRoomId = "";
        SelectedOldRoomId = "";
        SelectedReplacementRoomId = "";
        OnPropertyChanged(nameof(SelectedSlotDisplayText));
        NotifySelectionDetailChanged();
        Grid.ClearEditState();
    }

    private string ProfessorDisplayName(string id) =>
        SessionProfessors.FirstOrDefault(p => p.Id == id)?.Name ?? id;

    private static bool IsSameProfessorId(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

    private Course? ResolveCourseForCellAssignment(CellAssignment assignment)
    {
        var candidates = SessionCourses
            .Where(c => c.Id == assignment.CourseId)
            .ToList();
        if (candidates.Count <= 1) return candidates.FirstOrDefault();

        var bySection = candidates
            .Where(c => c.Section == assignment.Section)
            .ToList();
        if (bySection.Count == 1) return bySection[0];
        if (bySection.Count > 0) candidates = bySection;

        var byProfessor = candidates
            .Where(c => string.Equals(c.ProfessorId, assignment.ProfessorId, StringComparison.Ordinal))
            .ToList();
        if (byProfessor.Count == 1) return byProfessor[0];
        if (byProfessor.Count > 0) candidates = byProfessor;

        var assignmentRooms = assignment.Rooms
            .Select(NormalizeRoomId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var byFixedRoom = candidates
            .Where(c => c.FixedRooms.Any(room => assignmentRooms.Contains(NormalizeRoomId(room))))
            .ToList();
        if (byFixedRoom.Count == 1) return byFixedRoom[0];
        if (byFixedRoom.Count > 0) candidates = byFixedRoom;

        return candidates.FirstOrDefault();
    }

    private Course? ResolveCourseForAssignment(SolutionAssignment assignment)
    {
        var candidates = SessionCourses
            .Where(c => c.Id == assignment.CourseId)
            .ToList();
        if (candidates.Count <= 1) return candidates.FirstOrDefault();

        var roomId = NormalizeRoomId(assignment.RoomId);
        var byFixedRoom = candidates
            .Where(c => c.FixedRooms.Any(room => string.Equals(NormalizeRoomId(room), roomId, StringComparison.Ordinal)))
            .ToList();
        if (byFixedRoom.Count == 1) return byFixedRoom[0];
        if (byFixedRoom.Count > 0) candidates = byFixedRoom;

        return candidates.FirstOrDefault();
    }

    private string RoomDisplayName(string id) =>
        _workspace.Rooms.FirstOrDefault(r => r.Id == id)?.Name
        ?? _sessionData?.Rooms.FirstOrDefault(r => r.Id == id)?.Name
        ?? id;

    private IReadOnlyList<RoomOption> BuildRoomOptions(
        IEnumerable<string> roomIds,
        bool includeUnknownAssignedRooms)
    {
        var roomById = SessionRooms
            .Concat(_workspace.Rooms)
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => NormalizeRoomId(r.Id), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var roomByName = SessionRooms
            .Concat(_workspace.Rooms)
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .GroupBy(r => NormalizeRoomId(r.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var options = new Dictionary<string, RoomOption>(StringComparer.Ordinal);
        foreach (var rawId in roomIds)
        {
            var roomId = NormalizeRoomId(rawId);
            if (string.IsNullOrWhiteSpace(roomId)) continue;

            var room = roomById.TryGetValue(roomId, out var byId)
                ? byId
                : roomByName.TryGetValue(roomId, out var byName)
                    ? byName
                    : null;

            if (room == null && IsBareNumericRoomId(roomId) && !includeUnknownAssignedRooms)
                continue;

            var valueId = room?.Id ?? roomId;
            var displayName = room == null
                ? UnknownRoomDisplayName(roomId)
                : string.IsNullOrWhiteSpace(room.Name) ? room.Id : room.Name;
            var dedupeKey = NormalizeRoomId(displayName);
            if (string.IsNullOrWhiteSpace(dedupeKey))
                dedupeKey = NormalizeRoomId(valueId);
            if (options.ContainsKey(dedupeKey)) continue;

            options[dedupeKey] = new RoomOption(
                valueId,
                displayName,
                room?.Capacity ?? 0,
                room?.IsLab ?? false,
                $"{valueId} {displayName}".Trim());
        }

        return options.Values
            .OrderBy(option => option.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsBareNumericRoomId(string roomId) =>
        roomId.All(char.IsDigit);

    private static string UnknownRoomDisplayName(string roomId) =>
        IsBareNumericRoomId(roomId) ? $"알 수 없는 강의실 ({roomId})" : roomId;

    private static string NormalizeRoomId(string? roomId) =>
        string.IsNullOrWhiteSpace(roomId) ? "" : roomId.Trim();

    private IReadOnlyList<string> BuildRoomViewIds()
    {
        var ids = new List<string>();
        ids.AddRange(_workspace.Rooms.Select(r => r.Id));
        if (_sessionData != null)
            ids.AddRange(_sessionData.Rooms.Select(r => r.Id));
        ids.AddRange(_working.Select(a => a.RoomId));
        return ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
    }

    private IReadOnlyList<string> BuildReplacementRoomCandidates(string selectedOldRoomId)
    {
        if (SelectedAssignment == null) return Array.Empty<string>();

        var assigned = AssignedRoomsForSelectedAssignment.ToHashSet(StringComparer.Ordinal);
        var ids = BuildEditableRoomIds(includeAssignedRooms: false)
            .Where(id => !assigned.Contains(id))
            .ToList();

        return ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
    }

    private IReadOnlyList<string> BuildAllowedRoomCandidateIds(bool includeAssignedRooms)
    {
        if (SelectedAssignment == null) return Array.Empty<string>();

        return BuildEditableRoomIds(includeAssignedRooms);
    }

    private IReadOnlyList<string> BuildEditableRoomIds(bool includeAssignedRooms)
    {
        var ids = new List<string>();
        ids.AddRange(_workspace.Rooms.Select(r => r.Id));
        if (_sessionData != null)
            ids.AddRange(_sessionData.Rooms.Select(r => r.Id));
        if (includeAssignedRooms)
            ids.AddRange(SelectedAssignment?.Rooms ?? Array.Empty<string>());

        return ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
    }

    private bool RoomExists(string id) =>
        BuildEditableRoomIds(includeAssignedRooms: true).Contains(id);

    private static string Fallback(string value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;

    private void NotifySelectionDetailChanged()
    {
        OnPropertyChanged(nameof(SelectedProfessorName));
        OnPropertyChanged(nameof(SelectedLocationText));
        OnPropertyChanged(nameof(SelectedRoomsText));
        OnPropertyChanged(nameof(SelectedDurationText));
        OnPropertyChanged(nameof(SelectedOccupiedSlotsText));
        OnPropertyChanged(nameof(SelectedSlotDisplayText));
        OnPropertyChanged(nameof(SelectedBlockText));
        OnPropertyChanged(nameof(SelectedFixedText));
        OnPropertyChanged(nameof(SelectedCoteachText));
        OnPropertyChanged(nameof(SelectedMultiRoomText));
        OnPropertyChanged(nameof(SelectedAllowedRoomsText));
        OnPropertyChanged(nameof(AvailableRoomIds));
        OnPropertyChanged(nameof(SingleRoomChangeCandidates));
        OnPropertyChanged(nameof(AvailableRoomOptions));
        OnPropertyChanged(nameof(HasMultipleRoomCandidates));
        OnPropertyChanged(nameof(AssignedRoomsForSelectedAssignment));
        OnPropertyChanged(nameof(AssignedRoomOptions));
        OnPropertyChanged(nameof(HasMultipleAssignedRooms));
        OnPropertyChanged(nameof(HasSingleAssignedRoomDisplay));
        OnPropertyChanged(nameof(HasSingleAssignedRoomChangeUi));
        OnPropertyChanged(nameof(HasRoomChangeApplyUi));
        OnPropertyChanged(nameof(HasPendingInspectorChanges));
        OnPropertyChanged(nameof(CanApplyInspectorChanges));
        OnPropertyChanged(nameof(ReplacementRoomCandidates));
        OnPropertyChanged(nameof(ReplacementRoomOptions));
        OnPropertyChanged(nameof(SelectedMoveStateText));
        OnPropertyChanged(nameof(SelectedBlockedReasonText));
        OnPropertyChanged(nameof(SelectedWarningReasonText));
        OnPropertyChanged(nameof(HasBlockingReasons));
        OnPropertyChanged(nameof(HasWarningReasons));
        OnPropertyChanged(nameof(HasAnyConstraintReasons));
        OnPropertyChanged(nameof(SelectedCrossGroupText));
        ApplyRoomChangeCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<CrossGroup> CommitWorkingCrossGroupsPreview() =>
        _workingCrossGroups.Select(g => new CrossGroup { Id = g.Id, BaseIds = g.BaseIds.ToList() }).ToList();

    private bool TryCreateOrReplaceCrossAndMove(
        CellAssignment selected,
        CellAssignment target,
        int day,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        out string reason)
    {
        return TryCreateCrossAndMove(selected, target, day, targetPeriod, targetGrade, targetSubColumnIdx, out reason);
    }

    private bool TryCreateCrossAndMove(
        CellAssignment selected,
        CellAssignment target,
        int day,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        out string reason)
    {
        if (SelectedPeriod is not int selectedPeriod)
        {
            reason = "선택한 수업의 시간을 확인할 수 없습니다.";
            return false;
        }
        var selectedKey = BuildManualCrossAssignmentKey(selected, SelectedDay!.Value, selectedPeriod);
        var targetKey = BuildManualCrossAssignmentKey(target, day, targetPeriod);
        if (selectedKey == targetKey)
        {
            reason = "같은 수업끼리는 크로스를 만들 수 없습니다.";
            return false;
        }
        if (IsAlreadyCrossed(selectedKey, targetKey))
        {
            reason = "이미 크로스가 설정된 수업입니다.";
            return false;
        }
        if (IsAssignmentAlreadyCrossed(selectedKey) || IsAssignmentAlreadyCrossed(targetKey))
        {
            reason = "이미 다른 수업과 크로스된 과목입니다. 크로스는 두 과목끼리만 설정할 수 있습니다.";
            return false;
        }
        if (!HasSameCrossDuration(selected, target))
        {
            reason = CrossDurationMismatchReason;
            return false;
        }

        var reasons = ValidateCrossMoveWithProvisionalLink(
            selected, target, day, targetPeriod, targetGrade, targetSubColumnIdx, null);
        if (reasons.Count > 0)
        {
            reason = reasons[0];
            return false;
        }

        var provisionalLink = new ManualCrossLink(
            selected.CourseId,
            target.CourseId,
            day,
            targetPeriod,
            selected.RowSpan,
            targetPeriod,
            target.RowSpan,
            BuildManualCrossAssignmentKey(selected, day, targetPeriod),
            targetKey);
        var snapshot = CaptureSnapshot();
        var linkAdded = AddManualCrossLinkIfMissing(provisionalLink);

        if (!TryMoveAssignment(
                selected,
                SelectedDay!.Value,
                selectedPeriod,
                day,
                targetPeriod,
                targetGrade,
                targetSubColumnIdx,
                out var candidate,
                out var moveReasons,
                allowManualCrossOverlap: true,
                allowCrossDisplayColumn: true))
        {
            if (RequiresSameCourseAssignmentConflictCheck(selected, target)
                && moveReasons.Count > 0
                && moveReasons.All(r => r.Contains("교수 강의실 일관성", StringComparison.Ordinal)))
            {
                candidate = BuildMovedCandidate(selected, SelectedDay!.Value, selectedPeriod, day, targetPeriod);
            }
            else
            {
                if (linkAdded)
                    _workingCrossLinks.Remove(provisionalLink);
                reason = moveReasons.FirstOrDefault() ?? "크로스 이동을 완료할 수 없습니다.";
                return false;
            }
        }

        _working = candidate;
        MarkUserEditedAfterLoad();
        Rerender();
        PushUndoSnapshot(snapshot);
        SelectedDay = day;
        SelectedPeriod = targetPeriod;
        _selectedGridCell = FindGridCellKey(selected, day, targetPeriod, targetGrade)
            ?? new UnifiedCellKey(day, targetPeriod, targetGrade, targetSubColumnIdx);
        OnPropertyChanged(nameof(SelectedSlotDisplayText));
        NotifySelectionDetailChanged();
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(selected, day, targetPeriod));
        StatusMessage = $"크로스가 설정되었습니다: {selected.CourseName} ({selected.CourseId}) ↔ "
            + $"{target.CourseName} ({target.CourseId})";
        ClearSelectionCore();
        reason = "";
        return true;
    }

    private bool IsAlreadyCrossed(ManualCrossAssignmentKey left, ManualCrossAssignmentKey right) =>
        _workingCrossLinks.Any(l => l.MatchesPair(left, right));

    private bool IsAssignmentAlreadyCrossed(ManualCrossAssignmentKey key) =>
        _workingCrossLinks.Any(l => l.Contains(key));

    private bool AddManualCrossLinkIfMissing(ManualCrossLink link)
    {
        if (_workingCrossLinks.Any(existing => IsSameManualCrossLinkIgnoringDirection(existing, link)))
            return false;

        _workingCrossLinks.Add(link);
        return true;
    }

    private static bool IsSameManualCrossLinkIgnoringDirection(ManualCrossLink left, ManualCrossLink right) =>
        LinkMatchesPairIgnoringSlot(left, right.SourceKey, right.TargetKey);

    private static bool RequiresSameCourseAssignmentConflictCheck(CellAssignment left, CellAssignment right) =>
        string.Equals(left.CourseId, right.CourseId, StringComparison.Ordinal);

    private static bool HasProfessorOverlap(CellAssignment left, CellAssignment right)
    {
        var leftProfessors = ManualCrossProfessorIds(left);
        return ManualCrossProfessorIds(right).Any(leftProfessors.Contains);
    }

    private static HashSet<string> ManualCrossProfessorIds(CellAssignment assignment)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(assignment.ProfessorId))
            ids.Add(assignment.ProfessorId.Trim());
        foreach (var id in assignment.CoteachProfIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id.Trim());
        }
        return ids;
    }

    private static bool HasRoomOverlap(CellAssignment left, CellAssignment right)
    {
        var leftRooms = left.Rooms
            .Where(room => !string.IsNullOrWhiteSpace(room))
            .Select(room => room.Trim())
            .ToHashSet(StringComparer.Ordinal);
        return right.Rooms
            .Where(room => !string.IsNullOrWhiteSpace(room))
            .Select(room => room.Trim())
            .Any(leftRooms.Contains);
    }

    private bool IsCrossPair(CellAssignment left, CellAssignment right) =>
        left.Grade == right.Grade
        && _workingCrossLinks.Any(l => l.MatchesPair(left.CourseId, right.CourseId));

    private bool CanShareSlotByCross(CellAssignment left, CellAssignment right)
    {
        if (!IsCrossPair(left, right)) return false;
        var rightCourse = ResolveCourseForCellAssignment(right);
        return rightCourse != null && !rightCourse.IsFixed;
    }

    private bool IsManualGradeOverlapAllowed(string left, string right, int day, int period) =>
        IsManualGradeOverlapAllowedStrict(left, right, day, period);

    private bool IsManualGradeOverlapAllowedStrict(string left, string right, int day, int period) =>
        _workingCrossLinks.Any(l => l.Covers(left, right, day, period));

    private bool ShouldSuppressSavedCrossValidation =>
        _suppressSavedCrossValidation && !_hasUserEditedAfterLoad;

    private int RemoveInvalidCrossesAfterMove(bool refreshLabels = true)
    {
        _lastCrossCleanupExecuted = false;
        _lastCrossCleanupSkipReason = "";
        if (_isRestoringSavedTimetable)
        {
            _lastCrossCleanupSkipReason = "initial restore is running";
            return 0;
        }
        if (ShouldSuppressSavedCrossValidation)
        {
            _lastCrossCleanupSkipReason = "saved Cross validation is suppressed until first user edit";
            return 0;
        }

        _lastCrossCleanupExecuted = true;
        var before = _workingCrossLinks.Count;
        _workingCrossLinks.RemoveAll(link => !IsCrossLinkStillValid(link));
        if (refreshLabels && before != _workingCrossLinks.Count)
        {
            Grid.SetCrossLinkLabels(BuildCrossLinkLabels());
            NotifySelectionDetailChanged();
        }
        return before - _workingCrossLinks.Count;
    }

    private void MarkUserEditedAfterLoad()
    {
        _hasUserEditedAfterLoad = true;
        _suppressSavedCrossValidation = false;
    }

    private IReadOnlyList<string> ValidateCrossMoveWithProvisionalLink(
        CellAssignment selected,
        CellAssignment target,
        int day,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        ManualCrossLink? replacedLink,
        int? sourceDay = null,
        int? sourcePeriod = null)
    {
        if (sourcePeriod == null && SelectedPeriod is not int)
            return new[] { "선택한 수업의 시간을 확인할 수 없습니다." };

        var snapshot = _workingCrossLinks.ToList();
        var provisional = new ManualCrossLink(
            selected.CourseId,
            target.CourseId,
            day,
            targetPeriod,
            selected.RowSpan,
            targetPeriod,
            target.RowSpan,
            BuildManualCrossAssignmentKey(selected, day, targetPeriod),
            BuildManualCrossAssignmentKey(target, day, targetPeriod));
        try
        {
            if (replacedLink != null)
                _workingCrossLinks.Remove(replacedLink);
            AddManualCrossLinkIfMissing(provisional);
            return ValidateCrossMove(
                selected,
                target,
                day,
                targetPeriod,
                targetGrade,
                targetSubColumnIdx,
                sourceDay,
                sourcePeriod);
        }
        finally
        {
            RestoreCrossLinks(snapshot);
        }
    }

    private void RemoveCrossForCourse(string courseId, string? statusMessage, bool recordUndo = true)
    {
        var snapshot = recordUndo ? CaptureSnapshot() : null;
        var removed = _workingCrossLinks.RemoveAll(l => l.CourseIdA == courseId || l.CourseIdB == courseId);
        if (removed == 0) return;
        if (snapshot != null)
            PushUndoSnapshot(snapshot);
        Grid.SetCrossLinkLabels(BuildCrossLinkLabels());
        Rerender();
        NotifySelectionDetailChanged();
        if (!string.IsNullOrWhiteSpace(statusMessage))
            StatusMessage = statusMessage;
    }

    private void RestoreCrossLinks(IEnumerable<ManualCrossLink> snapshot)
    {
        _workingCrossLinks.Clear();
        _workingCrossLinks.AddRange(snapshot);
    }

    private ManualEditSnapshot CaptureSnapshot() => new()
    {
        Assignments = _working
            .Select(a => new SolutionAssignment(a.CourseId, a.Day, a.Period, a.RoomId))
            .ToList(),
        CrossLinks = _workingCrossLinks
            .Select(l => new ManualCrossLink(
                l.CourseIdA,
                l.CourseIdB,
                l.Day,
                l.PeriodA,
                l.RowSpanA,
                l.PeriodB,
                l.RowSpanB,
                l.SourceKey,
                l.TargetKey))
            .ToList(),
    };

    private void PushUndoSnapshot() => PushUndoSnapshot(CaptureSnapshot());

    private void PushUndoSnapshot(ManualEditSnapshot snapshot)
    {
        _undoStack.Push(snapshot);
        _redoStack.Clear();
        NotifyUndoRedoCanExecuteChanged();
    }

    private void RestoreSnapshot(ManualEditSnapshot snapshot)
    {
        _working = snapshot.Assignments
            .Select(a => new SolutionAssignment(a.CourseId, a.Day, a.Period, a.RoomId))
            .ToList();
        RestoreCrossLinks(snapshot.CrossLinks);
        Rerender();
        ClearSelectionCore();
    }

    private void RestoreResetBaseline()
    {
        if (_resetBaselineSnapshot == null) return;

        _working = _resetBaselineSnapshot.Assignments
            .Select(a => new SolutionAssignment(a.CourseId, a.Day, a.Period, a.RoomId))
            .ToList();
        RestoreCrossLinks(_resetBaselineSnapshot.CrossLinks);
    }

    private void NotifyUndoRedoCanExecuteChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        UndoLastMoveCommand.NotifyCanExecuteChanged();
    }

    private bool IsCrossLinkStillValid(ManualCrossLink link)
    {
        var c1 = SessionCourses.FirstOrDefault(c => c.Id == link.CourseIdA);
        var c2 = SessionCourses.FirstOrDefault(c => c.Id == link.CourseIdB);
        return c1 != null
            && c2 != null
            && c1.Grade == c2.Grade
            && _working.Any(a => a.CourseId == link.CourseIdA)
            && _working.Any(a => a.CourseId == link.CourseIdB);
    }

    private IReadOnlyDictionary<string, string> BuildCrossLinkLabels()
    {
        var labels = new Dictionary<string, List<string>>();
        foreach (var link in _workingCrossLinks)
        {
            AddLabel(link.SourceKey, link.CourseIdB);
            AddLabel(link.TargetKey, link.CourseIdA);
        }
        return labels.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value));

        void AddLabel(ManualCrossAssignmentKey sourceKey, string targetId)
        {
            var sourceDisplayKey = ToManualCrossDisplayKey(sourceKey);
            if (!labels.TryGetValue(sourceDisplayKey, out var list))
                labels[sourceDisplayKey] = list = new List<string>();
            list.Add(CourseDisplayName(targetId));
        }
    }

    private IReadOnlyList<SavedManualCrossLinkRow> ToSavedManualCrossLinks()
    {
        var rows = new List<SavedManualCrossLinkRow>();
        var seen = new HashSet<string>();

        foreach (var link in _workingCrossLinks)
        {
            if (!seen.Add(NormalizePair(link.SourceKey, link.TargetKey)))
                continue;

            var sourceCourse = FindCourseForManualCrossKey(link.SourceKey);
            var targetCourse = FindCourseForManualCrossKey(link.TargetKey);
            if (sourceCourse == null || targetCourse == null)
                continue;

            rows.Add(new SavedManualCrossLinkRow(
                link.SourceKey.CourseId,
                sourceCourse.Grade,
                link.SourceKey.Section,
                link.SourceKey.Day,
                link.SourceKey.Period,
                RepresentativeRoomId(link.SourceKey),
                link.TargetKey.CourseId,
                targetCourse.Grade,
                link.TargetKey.Section,
                link.TargetKey.Day,
                link.TargetKey.Period,
                RepresentativeRoomId(link.TargetKey),
                ManualCrossPolicyType));
        }

        return rows;
    }

    private int RestoreSavedManualCrossLinks(IReadOnlyList<SavedManualCrossLinkRow> rows)
    {
        _workingCrossLinks.Clear();
        _ignoredSavedCrossLinkReasons.Clear();
        _lastSavedCrossLinkCount = rows.Count;
        _lastRestoredCrossLinkCount = 0;
        _lastIgnoredCrossLinkCount = 0;
        var ignored = 0;

        foreach (var row in rows)
        {
            var sourceEndpoint = ResolveRestoredCrossAssignment(row, source: true);
            var targetEndpoint = ResolveRestoredCrossAssignment(row, source: false);
            if (sourceEndpoint == null || targetEndpoint == null)
            {
                _ignoredSavedCrossLinkReasons.Add($"{row.SourceCourseId} ↔ {row.TargetCourseId}: 현재 시간표에 참조 수업 assignment가 없습니다.");
                ignored++;
                continue;
            }

            if (sourceEndpoint.Value.Key == targetEndpoint.Value.Key)
            {
                _ignoredSavedCrossLinkReasons.Add($"{row.SourceCourseId} ↔ {row.TargetCourseId}: 같은 수업끼리는 Cross pair를 만들 수 없습니다.");
                ignored++;
                continue;
            }

            if (_workingCrossLinks.Any(link => link.MatchesPair(sourceEndpoint.Value.Key, targetEndpoint.Value.Key)))
                continue;
            if (_workingCrossLinks.Any(link => link.Contains(sourceEndpoint.Value.Key) || link.Contains(targetEndpoint.Value.Key)))
            {
                _ignoredSavedCrossLinkReasons.Add($"{row.SourceCourseId} ↔ {row.TargetCourseId}: 이미 다른 Cross에 포함된 수업입니다.");
                ignored++;
                continue;
            }

            _workingCrossLinks.Add(new ManualCrossLink(
                row.SourceCourseId,
                row.TargetCourseId,
                sourceEndpoint.Value.Key.Day,
                sourceEndpoint.Value.Key.Period,
                sourceEndpoint.Value.Key.RowSpan,
                targetEndpoint.Value.Key.Period,
                targetEndpoint.Value.Key.RowSpan,
                sourceEndpoint.Value.Key,
                targetEndpoint.Value.Key));
            _lastRestoredCrossLinkCount++;
        }

        _lastIgnoredCrossLinkCount = ignored;
        NotifySelectionDetailChanged();
        return ignored;
    }

    private readonly record struct RestoredCrossEndpoint(
        SolutionAssignment Assignment,
        ManualCrossAssignmentKey Key);

    private RestoredCrossEndpoint? ResolveRestoredCrossAssignment(SavedManualCrossLinkRow row, bool source)
    {
        var courseId = source ? row.SourceCourseId : row.TargetCourseId;
        var section = NormalizeManualCrossSection(source ? row.SourceSection : row.TargetSection);
        var day = source ? row.SourceDay : row.TargetDay;
        var period = source ? row.SourcePeriod : row.TargetPeriod;
        var roomId = (source ? row.SourceRoomId : row.TargetRoomId).Trim();

        var exact = _working
            .Where(a => a.CourseId == courseId
                && a.Day == day
                && a.Period == period
                && string.Equals(a.RoomId, roomId, StringComparison.Ordinal)
                && CourseSectionMatches(a.CourseId, section))
            .Select(a => (SolutionAssignment?)a)
            .FirstOrDefault();
        if (exact != null)
            return BuildRestoredEndpoint(exact.Value, section, rowSpan: GetWorkingRowSpan(courseId, day, period), new[] { exact.Value.RoomId });

        var slot = _working
            .Where(a => a.CourseId == courseId
                && a.Day == day
                && a.Period == period
                && CourseSectionMatches(a.CourseId, section))
            .ToList();
        if (slot.Count > 0 && (string.IsNullOrWhiteSpace(roomId) || slot.Any(a => a.RoomId == roomId)))
        {
            var restored = string.IsNullOrWhiteSpace(roomId)
                ? slot.OrderBy(a => a.RoomId, StringComparer.Ordinal).First()
                : slot.First(a => a.RoomId == roomId);
            return BuildRestoredEndpoint(restored, section, GetWorkingRowSpan(courseId, day, period), new[] { restored.RoomId });
        }

        var candidates = BuildRestoredCandidateBlocks(courseId);
        if (candidates.Count == 1)
        {
            var only = candidates[0];
            return BuildRestoredEndpoint(only.Assignment, section, only.RowSpan);
        }

        return null;
    }

    private RestoredCrossEndpoint BuildRestoredEndpoint(
        SolutionAssignment assignment,
        string section,
        int rowSpan,
        IEnumerable<string>? roomIds = null)
    {
        rowSpan = Math.Max(1, rowSpan);
        var endpointRoomIds = roomIds ?? _working
            .Where(a => a.CourseId == assignment.CourseId
                && a.Day == assignment.Day
                && a.Period >= assignment.Period
                && a.Period < assignment.Period + rowSpan)
            .Select(a => a.RoomId);
        var key = new ManualCrossAssignmentKey(
            assignment.CourseId,
            section,
            assignment.Day,
            assignment.Period,
            rowSpan,
            BuildRoomIdsKey(endpointRoomIds));
        return new RestoredCrossEndpoint(assignment, key);
    }

    private List<(SolutionAssignment Assignment, int RowSpan)> BuildRestoredCandidateBlocks(string courseId)
    {
        var blocks = new List<(SolutionAssignment Assignment, int RowSpan)>();
        foreach (var group in _working.Where(a => a.CourseId == courseId).GroupBy(a => a.Day))
        {
            var periods = group.Select(a => a.Period).Distinct().OrderBy(p => p).ToList();
            var starts = periods.Where(p => !periods.Contains(p - 1)).ToList();
            foreach (var start in starts)
            {
                var rowSpan = GetWorkingRowSpan(courseId, group.Key, start);
                var first = group
                    .Where(a => a.Period == start)
                    .OrderBy(a => a.RoomId, StringComparer.Ordinal)
                    .First();
                blocks.Add((first, Math.Max(1, rowSpan)));
            }
        }
        return blocks;
    }

    private bool CourseSectionMatches(string courseId, string section)
    {
        if (string.IsNullOrWhiteSpace(section)) return true;
        return SessionCourses.Any(c => c.Id == courseId && NormalizeManualCrossSection(c.Section) == section);
    }

    private int GetWorkingRowSpan(string courseId, int day, int startPeriod)
    {
        var periods = _working
            .Where(a => a.CourseId == courseId && a.Day == day)
            .Select(a => a.Period)
            .ToHashSet();
        if (!periods.Contains(startPeriod)) return 0;

        var span = 0;
        for (var period = startPeriod; periods.Contains(period); period++)
            span++;
        return span;
    }

    private static string NormalizePair(ManualCrossAssignmentKey left, ManualCrossAssignmentKey right)
    {
        var l = left.ToString();
        var r = right.ToString();
        return string.CompareOrdinal(l, r) <= 0 ? $"{l}|{r}" : $"{r}|{l}";
    }

    private string FormatCrossLink(ManualCrossLink link) =>
        $"{CourseDisplayName(link.CourseIdA)} ↔ {CourseDisplayName(link.CourseIdB)}";

    private string CourseDisplayName(string id) =>
        SessionCourses.FirstOrDefault(c => c.Id == id)?.Name ?? id;

    private Course? FindCourseForManualCrossKey(ManualCrossAssignmentKey key)
    {
        var candidates = SessionCourses
            .Where(c => c.Id == key.CourseId)
            .ToList();
        if (candidates.Count <= 1) return candidates.FirstOrDefault();

        var bySection = candidates
            .Where(c => NormalizeManualCrossSection(c.Section) == key.Section)
            .ToList();
        if (bySection.Count == 1) return bySection[0];
        if (bySection.Count > 0) candidates = bySection;

        var keyRooms = key.RoomIdsKey
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeRoomId)
            .ToHashSet(StringComparer.Ordinal);
        var byFixedRoom = candidates
            .Where(c => c.FixedRooms.Any(room => keyRooms.Contains(NormalizeRoomId(room))))
            .ToList();
        if (byFixedRoom.Count == 1) return byFixedRoom[0];
        if (byFixedRoom.Count > 0) candidates = byFixedRoom;

        return candidates.FirstOrDefault();
    }

    private IReadOnlyDictionary<string, int> BuildCrossParallelOrder()
    {
        var order = new Dictionary<string, int>();
        foreach (var link in _workingCrossLinks)
        {
            order[ToManualCrossDisplayKey(link.TargetKey)] = 0;
            order[ToManualCrossDisplayKey(link.SourceKey)] = 1;
        }
        return order;
    }

    private IReadOnlyList<string> ValidateCrossMove(
        CellAssignment selected,
        CellAssignment target,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        int? sourceDay = null,
        int? sourcePeriod = null)
    {
        if (targetGrade != selected.Grade || target.Grade != selected.Grade)
            return new[] { "같은 학년 수업끼리만 크로스를 만들 수 있습니다." };
        if (!HasSameCrossDuration(selected, target))
            return new[] { CrossDurationMismatchReason };
        var selectedDay = sourceDay ?? SelectedDay ?? targetDay;
        var selectedPeriod = sourcePeriod ?? SelectedPeriod ?? targetPeriod;
        if (!TryNormalizeCrossTarget(target, targetDay, targetPeriod, targetGrade, targetSubColumnIdx, out var normalizedTarget))
            return new[] { CrossTimeRangeMismatchReason };
        target = normalizedTarget.Assignment;
        targetDay = normalizedTarget.Day;
        targetPeriod = normalizedTarget.Period;
        targetGrade = normalizedTarget.Grade;
        targetSubColumnIdx = normalizedTarget.SubColumnIdx;

        var selectedKey = BuildManualCrossAssignmentKey(selected, selectedDay, selectedPeriod);
        var targetKey = BuildManualCrossAssignmentKey(target, targetDay, targetPeriod);
        var targetPeriods = GetOccupiedPeriods(targetPeriod, selected.RowSpan);
        var distinctTargetKeys = Grid.Cells
            .Where(c => c.Day == targetDay
                && c.Grade == targetGrade
                && targetPeriods.Any(p => p >= c.Period && p < c.Period + c.Assignment.RowSpan))
            .Select(c => BuildManualCrossAssignmentKey(c.Assignment, c.Day, c.Period))
            .Where(key => key != selectedKey)
            .Distinct()
            .ToList();
        if (distinctTargetKeys.Any(key => key != targetKey))
            return new[] { "3개 이상의 과목 크로스는 허용되지 않습니다." };

        var provisional = new ManualCrossLink(
            selected.CourseId,
            target.CourseId,
            targetDay,
            targetPeriod,
            selected.RowSpan,
            targetPeriod,
            target.RowSpan,
            BuildManualCrossAssignmentKey(selected, targetDay, targetPeriod),
            BuildManualCrossAssignmentKey(target, targetDay, targetPeriod));
        _workingCrossLinks.Add(provisional);
        var reasons = ValidateManualMove(
        selected,
        selectedDay,
        selectedPeriod,
        targetDay,
        targetPeriod,
        targetGrade,
        targetSubColumnIdx,
        allowManualCrossOverlap: true,
        allowCrossDisplayColumn: true);
        _workingCrossLinks.Remove(provisional);
        return RequiresSameCourseAssignmentConflictCheck(selected, target)
            ? reasons.Where(reason => !reason.Contains("교수 강의실 일관성", StringComparison.Ordinal)).ToList()
            : reasons;
    }

    private static bool HasSameCrossDuration(CellAssignment selected, CellAssignment target) =>
        selected.RowSpan == target.RowSpan;

    private bool IsCrossTargetRangeStart(
        CellAssignment target,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx)
    {
        var targetCells = Grid.Cells
            .Where(c => c.Grade == targetGrade
                && IsSameManualCrossAssignmentIdentity(c.Assignment, target))
            .ToList();
        return targetCells.Count == 0
            || targetCells.Any(c => c.Day == targetDay && c.Period == targetPeriod);
    }

    private bool TryNormalizeCrossTarget(
        CellAssignment target,
        int rawDay,
        int rawPeriod,
        int rawGrade,
        int rawSubColumnIdx,
        out CrossTargetStart normalized)
    {
        var targetCells = Grid.Cells
            .Where(c => c.Grade == rawGrade
                && IsSameManualCrossAssignmentIdentity(c.Assignment, target))
            .ToList();
        var containingCell = targetCells
            .Where(c => c.Day == rawDay
                && c.SubColumnIdx == rawSubColumnIdx
                && rawPeriod >= c.Period
                && rawPeriod < c.Period + c.Assignment.RowSpan)
            .OrderBy(c => c.Period)
            .FirstOrDefault()
            ?? targetCells
                .Where(c => c.Day == rawDay
                    && rawPeriod >= c.Period
                    && rawPeriod < c.Period + c.Assignment.RowSpan)
                .OrderBy(c => c.Period)
                .FirstOrDefault();

        if (containingCell != null)
        {
            normalized = new CrossTargetStart(
                containingCell.Assignment,
                containingCell.Day,
                containingCell.Period,
                containingCell.Grade,
                containingCell.SubColumnIdx);
            return true;
        }

        if (targetCells.Count == 0)
        {
            normalized = new CrossTargetStart(target, rawDay, rawPeriod, rawGrade, rawSubColumnIdx);
            return true;
        }

        normalized = default;
        return false;
    }

    private static bool IsSameManualCrossAssignmentIdentity(CellAssignment left, CellAssignment right)
    {
        var leftKey = BuildManualCrossAssignmentKey(left, day: 0, period: 0);
        var rightKey = BuildManualCrossAssignmentKey(right, day: 0, period: 0);
        return leftKey == rightKey;
    }

    private UnifiedCellKey? FindGridCellKey(CellAssignment assignment, int day, int period, int grade)
    {
        var key = BuildManualCrossAssignmentKey(assignment, day, period);
        var cell = Grid.Cells.FirstOrDefault(c =>
            c.Day == day
            && c.Period == period
            && c.Grade == grade
            && BuildManualCrossAssignmentKey(c.Assignment, c.Day, c.Period) == key);
        return cell == null ? null : new UnifiedCellKey(cell.Day, cell.Period, cell.Grade, cell.SubColumnIdx);
    }

    private bool CanApplyRoomChange() => CanApplyInspectorChanges;

    private bool CanClearSelection() => SelectedAssignment != null;

    private bool CanUndo() => _undoStack.Count > 0;

    private bool CanRedo() => _redoStack.Count > 0;

    private static HashSet<string> ConflictKeys(IEnumerable<ConflictItem> conflicts) =>
        conflicts.Select(KeyOf).ToHashSet();

    private static string KeyOf(ConflictItem c) =>
        $"{c.Type}|{c.Day}|{c.Period}|{c.Description}";

    private static string ToUserReason(ConflictItem c) => StripConstraintCodeForDisplay(c.Type switch
    {
        ConflictType.RoomConflict => $"강의실 단일 배정 위반: {c.Description}",
        ConflictType.ProfessorConflict => $"교수 단일 배정 위반: {c.Description}",
        ConflictType.ProfUnavailable => $"교수 불가능 시간 위반: {c.Description}",
        ConflictType.SectionConflict => $"분반 중복 금지 위반: {c.Description}",
        ConflictType.GradeConflict => $"학년 내 과목 충돌 위반: {c.Description}",
        ConflictType.LunchConflict => $"점심시간 보장 위반: {c.Description}",
        ConflictType.FixedTimeViolation => $"고정 시간표 보존 위반: {c.Description}",
        ConflictType.FixedRoomViolation => $"고정 강의실 위반: {c.Description}",
        ConflictType.CourseUnavailableRoomViolation => $"불가 강의실 위반: {c.Description}",
        ConflictType.BlockStartViolation => $"블록 시작 교시 위반: {c.Description}",
        ConflictType.SameCourseSameDayConflict => $"동일 교과목·분반·교수 동일 요일 중복 배치 위반: {c.Description}",
        ConflictType.ProfAllowedRoomViolation => $"허용 강의실 위반: {c.Description}",
        ConflictType.ProfRoomInconsistent => $"교수 강의실 일관성 위반: {c.Description}",
        _ => c.Description,
    });

    private static string DayName(int d) => d switch
    {
        0 => "월", 1 => "화", 2 => "수", 3 => "목", 4 => "금",
        _ => "?"
    };
}

public sealed record ConflictDisplayItem(
    ConflictItem Conflict,
    string DisplayTitle,
    string DisplayDescription)
{
    public ConflictType Type => Conflict.Type;
    public ConflictSeverity Severity => Conflict.Severity;
    public string Description => Conflict.Description;
    public int Day => Conflict.Day;
    public int Period => Conflict.Period;
}
