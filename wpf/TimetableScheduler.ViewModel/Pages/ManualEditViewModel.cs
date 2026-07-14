using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Text.Json;
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
    private const string SchoolFixedDisplayCourseMarker = "__school_fixed__";
    private const string ManualBlockCourseIdPrefix = "manual-block-";
    private const int ManualBlockMaxRowSpan = 9;
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
            (ManualCrossAssignmentMatches(SourceKey, left) && ManualCrossAssignmentMatches(TargetKey, right))
            || (ManualCrossAssignmentMatches(SourceKey, right) && ManualCrossAssignmentMatches(TargetKey, left));

        public bool Contains(ManualCrossAssignmentKey key) =>
            ManualCrossAssignmentMatches(SourceKey, key) || ManualCrossAssignmentMatches(TargetKey, key);

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

    private static bool ManualCrossAssignmentMatches(
        ManualCrossAssignmentKey left,
        ManualCrossAssignmentKey right) =>
        !string.IsNullOrWhiteSpace(left.AssignmentId) && !string.IsNullOrWhiteSpace(right.AssignmentId)
            ? string.Equals(left.AssignmentId, right.AssignmentId, StringComparison.Ordinal)
            : left.CourseId == right.CourseId
                && left.Section == right.Section
                && left.Day == right.Day
                && left.Period == right.Period
                && left.RowSpan == right.RowSpan
                && left.RoomIdsKey == right.RoomIdsKey;

    public readonly record struct ManualCrossAssignmentKey(
        string CourseId,
        string Section,
        int Day,
        int Period,
        int RowSpan,
        string RoomIdsKey,
        string AssignmentId = "");

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
        string SearchText,
        bool IsSelectable)
    {
        public string DisplayText
        {
            get
            {
                var roomType = IsLab ? "실습실" : "일반";
                var capacity = Capacity > 0 ? $"{Capacity}명" : "-";
                return $"{DisplayName}\t{roomType}\t{capacity}";
            }
        }
    }

    public sealed record ProfessorOption(
        string ProfessorId,
        string DisplayName,
        string SearchText,
        bool IsSelectable = true);

    public enum ManualBlockKind
    {
        Course,
        SchoolFixed,
        GradeFixed,
    }

    public sealed record ManualBlockKindOption(ManualBlockKind Kind, string DisplayName);

    public sealed record StagedBlockItem(
        string Id,
        CellAssignment Assignment,
        int SourceDay,
        int SourcePeriod,
        IReadOnlyList<SolutionAssignment> Assignments,
        string Title,
        string Subtitle,
        string Detail)
    {
        public string AssignmentId => Assignment.AssignmentId;
        public string CourseId => Assignment.CourseId;
        public int Grade => Assignment.Grade;
        public int RowSpan => Assignment.RowSpan;
    }

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
            BuildRoomIdsKey(assignment.Rooms),
            assignment.AssignmentId);

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
            BuildRoomIdsKey(new[] { assignment.RoomId }),
            assignment.AssignmentId);

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
        public List<StagedBlockItem> StagedBlocks { get; init; } = new();
        public List<ManualCrossLink> CrossLinks { get; init; } = new();
        public AppData? SessionData { get; init; }
        public string? SelectedAssignmentId { get; init; }
        public string? SelectedStagedBlockId { get; init; }
        public int? SelectedDay { get; init; }
        public int? SelectedPeriod { get; init; }
        public int? SelectedGrade { get; init; }
        public int? SelectedSubColumnIdx { get; init; }
        public bool ShowRoomAdditionalInfo { get; init; }
    }

    private sealed record ManualMoveValidationResult(
        IReadOnlyList<string> BlockingReasons,
        IReadOnlyList<ConflictItem> Warnings);

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
    private SchedulePolicy SessionSchedulePolicy =>
        _sessionData?.SchedulePolicy ?? _workspace.SchedulePolicy;
    private IReadOnlyDictionary<int, int> _lunchPeriodsByDay =
        SchedulePolicyRules.StaticLunchPeriodsByDay(SchedulePolicy.Default, Constants.Days);

    public override string Title => "수동 편집";

    public event EventHandler? BackRequested;
    public event EventHandler? ConstraintEditRequested;
    public event EventHandler<ConflictFocusRequestedEventArgs>? ConflictFocusRequested;

    [RelayCommand]
    private void Back()
    {
        if (HasUnsavedChanges)
        {
            if (!_dialog.ConfirmDiscardChanges())
                return;

            RestoreBaselineForDiscard();
        }

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void EditConstraints() => ConstraintEditRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ValidateAll()
    {
        var validationConflicts = CollapseBlockConflicts(GetFullValidationConflicts(strictManualCrossValidation: true))
            .Select(c => c with { Description = BuildUserConflictDescription(c) })
            .ToList();

        if (StagedBlocks.Count > 0)
        {
            var stagedMessage = $"보관함에 빠져 있는 블럭 {StagedBlocks.Count}개를 시간표에 다시 넣어야 합니다.";
            _dialog.ShowValidationResult(
                "전체 검증",
                stagedMessage,
                validationConflicts,
                BuildValidationCheckItems(validationConflicts, StagedBlocks.Count));
            StatusMessage = $"전체 검증 Error 1건: {stagedMessage}";
            return;
        }

        var errorCount = validationConflicts.Count(c => c.Severity == ConflictSeverity.Error);
        var warningCount = validationConflicts.Count(c => c.Severity == ConflictSeverity.Warning);
        var message = errorCount > 0
            ? "저장할 수 없는 제약조건 위반이 있습니다."
            : warningCount > 0
                ? "주의가 필요한 항목이 있습니다."
                : "전체 제약조건을 충족합니다.";

        _dialog.ShowValidationResult(
            "전체 검증",
            message,
            validationConflicts,
            BuildValidationCheckItems(validationConflicts, StagedBlocks.Count));
        StatusMessage = errorCount > 0
            ? $"전체 검증: Error {errorCount}건, Warning {warningCount}건"
            : warningCount > 0
                ? $"전체 검증: Warning {warningCount}건"
                : "전체 검증: 전체 제약조건을 충족합니다.";
    }

    private IReadOnlyList<ConflictItem> GetFullValidationConflicts(bool strictManualCrossValidation = false) =>
        DetectAllConflicts(_working, strictManualCrossValidation)
            .Where(c => !IsAllowedExistingManualCrossSectionConflict(c, _working))
            .Where(c => !IsExcludedManualEditConflict(c))
            .Select(ClassifyManualEditConflict)
            .ToList();

    private IReadOnlyList<ConflictItem> CollapseBlockConflicts(IEnumerable<ConflictItem> conflicts)
    {
        var collapsed = new List<ConflictItem>();
        foreach (var group in conflicts.GroupBy(BuildBlockConflictGroupKey))
        {
            var currentRun = new List<ConflictItem>();
            int? previousPeriod = null;

            foreach (var periodGroup in group
                         .GroupBy(conflict => conflict.Period)
                         .OrderBy(periodGroup => periodGroup.Key))
            {
                if (previousPeriod != null && periodGroup.Key > previousPeriod.Value + 1)
                {
                    collapsed.Add(CollapseBlockConflictRun(currentRun));
                    currentRun.Clear();
                }

                currentRun.AddRange(periodGroup.OrderBy(conflict => conflict.Description, StringComparer.Ordinal));
                previousPeriod = periodGroup.Key;
            }

            if (currentRun.Count > 0)
                collapsed.Add(CollapseBlockConflictRun(currentRun));
        }

        return collapsed
            .OrderBy(conflict => conflict.Day)
            .ThenBy(conflict => conflict.Period)
            .ThenBy(conflict => ConflictSortOrder(conflict.Type))
            .ThenBy(conflict => conflict.Description, StringComparer.Ordinal)
            .ToList();
    }

    private string BuildBlockConflictGroupKey(ConflictItem conflict)
    {
        var assignmentKeys = ConflictAssignmentsInSlot(conflict)
            .Select(BlockConflictIdentity)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal);
        return string.Join(
            "\u001f",
            conflict.Type,
            conflict.Severity,
            conflict.Day,
            string.Join("\u001e", assignmentKeys));
    }

    private string BlockConflictIdentity(SolutionAssignment assignment)
    {
        if (!string.IsNullOrWhiteSpace(assignment.AssignmentId))
            return assignment.AssignmentId;

        var course = ResolveCourseForAssignment(assignment);
        return string.Join(
            "\u001d",
            assignment.CourseId,
            course?.Section ?? 0,
            course?.ProfessorId ?? "",
            assignment.Day,
            assignment.RoomId);
    }

    private ConflictItem CollapseBlockConflictRun(IReadOnlyList<ConflictItem> conflicts)
    {
        if (conflicts.Count == 1)
            return conflicts[0];

        var first = conflicts
            .OrderBy(conflict => conflict.Period)
            .ThenBy(conflict => conflict.Description, StringComparer.Ordinal)
            .First();
        var assignments = conflicts
            .SelectMany(ConflictAssignmentsInSlot)
            .DistinctBy(ConflictAssignmentRowKey)
            .OrderBy(assignment => assignment.Day)
            .ThenBy(assignment => assignment.Period)
            .ThenBy(assignment => assignment.CourseId, StringComparer.Ordinal)
            .ThenBy(assignment => assignment.RoomId, StringComparer.Ordinal)
            .ToList();
        var severity = conflicts.Any(conflict => conflict.Severity == ConflictSeverity.Error)
            ? ConflictSeverity.Error
            : ConflictSeverity.Warning;
        return new ConflictItem(
            first.Type,
            severity,
            first.Description,
            first.Day,
            conflicts.Min(conflict => conflict.Period),
            assignments);
    }

    private static string ConflictAssignmentRowKey(SolutionAssignment assignment) =>
        string.Join(
            "\u001f",
            assignment.CourseId,
            assignment.Day,
            assignment.Period,
            assignment.RoomId,
            assignment.AssignmentId);

    private static IReadOnlyList<ValidationCheckItem> BuildValidationCheckItems(
        IReadOnlyList<ConflictItem> conflicts,
        int stagedBlockCount)
    {
        var checks = new List<ValidationCheckItem>();
        if (stagedBlockCount > 0)
        {
            checks.Add(new ValidationCheckItem(
                "보관함 블록 배치",
                ValidationCheckTier.Critical,
                false,
                1,
                0,
                $"보관함에 빠져 있는 블록 {stagedBlockCount}개를 시간표에 다시 넣어야 합니다."));
        }

        foreach (var definition in ValidationCheckDefinitions())
        {
            var matched = conflicts
                .Where(conflict => definition.Types.Contains(conflict.Type))
                .OrderBy(conflict => conflict.Day)
                .ThenBy(conflict => conflict.Period)
                .ToList();
            var errorCount = matched.Count(conflict => conflict.Severity == ConflictSeverity.Error);
            var warningCount = matched.Count(conflict => conflict.Severity == ConflictSeverity.Warning);
            var detail = errorCount > 0 || warningCount > 0
                ? $"Error {errorCount}건, Warning {warningCount}건"
                : "";
            checks.Add(new ValidationCheckItem(
                definition.Name,
                definition.Tier,
                errorCount == 0 && warningCount == 0,
                errorCount,
                warningCount,
                detail,
                matched));
        }

        return checks;
    }

    private static IReadOnlyList<(string Name, ValidationCheckTier Tier, ConflictType[] Types)> ValidationCheckDefinitions() =>
        new (string Name, ValidationCheckTier Tier, ConflictType[] Types)[]
        {
            ("강의실 중복", ValidationCheckTier.Critical, new[] { ConflictType.RoomConflict }),
            ("교수 중복", ValidationCheckTier.Critical, new[] { ConflictType.ProfessorConflict }),
            ("분반 중복", ValidationCheckTier.Critical, new[] { ConflictType.SectionConflict }),
            ("학년 중복", ValidationCheckTier.Critical, new[] { ConflictType.GradeConflict }),
            ("같은 요일 중복", ValidationCheckTier.Critical, new[] { ConflictType.SameCourseSameDayConflict }),
            ("교수 불가시간", ValidationCheckTier.Important, new[] { ConflictType.ProfUnavailable }),
            ("고정시간 이탈", ValidationCheckTier.Important, new[] { ConflictType.FixedTimeViolation }),
            ("강의실 불일치", ValidationCheckTier.Important, new[] { ConflictType.ProfRoomInconsistent }),
            ("시간대 위반", ValidationCheckTier.Important, new[] { ConflictType.AcademicLevelTimeBandViolation }),
        };

    [RelayCommand]
    private void FocusConflict(ConflictDisplayItem? item)
    {
        if (item == null)
            return;

        var cell = ResolveConflictFocusCell(item.Conflict, item.SelectedAssignment);
        if (cell == null)
            return;

        SelectCell(cell.Day, cell.Period, cell.Grade, cell.SubColumnIdx, cell.Assignment);
        var relatedTargets = item.RelatedAssignments
            .Select(ResolveConflictFocusCell)
            .Where(related => related != null)
            .Select(related => new ConflictFocusTarget(
                related!.Day,
                related.Period,
                related.Grade,
                related.SubColumnIdx))
            .Distinct()
            .ToList();
        ConflictFocusRequested?.Invoke(
            this,
            new ConflictFocusRequestedEventArgs(
                cell.Day,
                cell.Period,
                cell.Grade,
                cell.SubColumnIdx,
                relatedTargets));
    }

    public UnifiedTimetableViewModel Grid { get; } = new();
    public ObservableCollection<NamedGridViewModel> GradeViews { get; } = new();
    public ObservableCollection<NamedGridViewModel> RoomViews { get; } = new();
    public ObservableCollection<NamedGridViewModel> ProfessorViews { get; } = new();

    public ObservableCollection<ConflictDisplayItem> Conflicts { get; } = new();
    public ObservableCollection<ConflictDisplayItem> ConflictGroups { get; } = new();
    public ObservableCollection<StagedBlockItem> StagedBlocks { get; } = new();
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
    [NotifyCanExecuteChangedFor(nameof(SaveCopyCommand))]
    private RankedSolution? baseSolution;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveTimetableCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCopyCommand))]
    private string saveName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingSavedTimetable))]
    [NotifyPropertyChangedFor(nameof(IsNewTimetable))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private string? editingSavedTimetableId;

    public bool IsEditingSavedTimetable => !string.IsNullOrWhiteSpace(EditingSavedTimetableId);

    public bool IsNewTimetable => !IsEditingSavedTimetable;

    public string SaveButtonText => IsEditingSavedTimetable ? "변경사항 저장" : "저장";

    public bool HasUnsavedChanges =>
        _resetBaselineSnapshot != null && !SnapshotContentEquals(_resetBaselineSnapshot, CaptureSnapshot());

    public string ExportFileNameBase => string.IsNullOrWhiteSpace(SaveName) ? "시간표" : SaveName.Trim();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyRoomChangeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(StageSelectedBlockCommand))]
    private CellAssignment? selectedAssignment;

    public bool HasSelection => SelectedAssignment != null;
    public bool HasNoSelection => !HasSelection;
    public CellAssignment? InspectorAssignment => SelectedAssignment ?? SelectedStagedBlock?.Assignment;
    public bool HasInspectorSelection => InspectorAssignment != null;
    public bool HasNoInspectorSelection => !HasInspectorSelection;

    [ObservableProperty]
    private StagedBlockItem? selectedStagedBlock;

    public bool HasStagedBlocks => StagedBlocks.Count > 0;
    public bool HasNoStagedBlocks => !HasStagedBlocks;
    public int StagedBlockCount => StagedBlocks.Count;
    public bool HasSelectedStagedBlock => SelectedStagedBlock != null;

    public IReadOnlyList<ManualBlockKindOption> ManualBlockKindOptions { get; } =
        new[]
        {
            new ManualBlockKindOption(ManualBlockKind.Course, "수업 블럭"),
            new ManualBlockKindOption(ManualBlockKind.SchoolFixed, "학교 고정"),
            new ManualBlockKindOption(ManualBlockKind.GradeFixed, "학년 고정"),
        };

    public IReadOnlyList<int> ManualBlockGradeOptions => AcademicLevels.AllGrades;

    public IReadOnlyList<Course> ManualBlockCourseOptions =>
        SessionCourses
            .Where(course => !course.IsSchoolFixed)
            .OrderBy(course => course.Grade)
            .ThenBy(course => course.Name, StringComparer.Ordinal)
            .ThenBy(course => course.Section)
            .ToList();

    public IReadOnlyList<RoomOption> ManualBlockRoomOptions =>
        BuildRoomOptions(
            BuildManualBlockRoomIds(),
            includeUnknownAssignedRooms: true,
            forceSelectable: true);

    public bool IsManualBlockCourseMode => NewBlockKind == ManualBlockKind.Course;

    public bool IsManualBlockSpecialMode => NewBlockKind != ManualBlockKind.Course;

    public bool HasManualBlockStatusMessage => !string.IsNullOrWhiteSpace(ManualBlockStatusMessage);

    [ObservableProperty]
    private ManualBlockKind newBlockKind = ManualBlockKind.Course;

    [ObservableProperty]
    private string newBlockCourseId = "";

    [ObservableProperty]
    private string newBlockCourseName = "";

    [ObservableProperty]
    private string newBlockProfessorName = "";

    [ObservableProperty]
    private string newBlockRoomName = "";

    [ObservableProperty]
    private int newBlockGrade = AcademicLevels.AllGrades[0];

    [ObservableProperty]
    private string newBlockRoomId = "";

    [ObservableProperty]
    private int newBlockRowSpan = 1;

    [ObservableProperty]
    private string newBlockStructureText = "";

    public IReadOnlyList<string> NewBlockStructureOptions => BuildManualBlockStructureOptions(NewBlockRowSpan);

    [ObservableProperty]
    private bool isNewBlockStructureEnabled;

    [ObservableProperty]
    private int newBlockSectionCount = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManualBlockStatusMessage))]
    private string manualBlockStatusMessage = "";

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
    private string selectedPrimaryProfessorId = "";

    [ObservableProperty]
    private string selectedCoteachProfessorId = "";

    [ObservableProperty]
    private string selectedRoomToAddId = "";

    [ObservableProperty]
    private bool deleteEntireSelectedCourse;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRoomAdditionalInfo))]
    private bool showRoomAdditionalInfo = ManualEditUserSettings.Load().ShowRoomAdditionalInfo;

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
        InspectorAssignment == null
            ? Array.Empty<string>()
            : InspectorAssignment.Rooms.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();

    public IReadOnlyList<RoomOption> AssignedRoomOptions =>
        BuildRoomOptions(AssignedRoomsForSelectedAssignment, includeUnknownAssignedRooms: true);

    public bool HasMultipleAssignedRooms => AssignedRoomsForSelectedAssignment.Count > 1;

    public bool HasSingleAssignedRoomDisplay => !HasMultipleAssignedRooms;

    public bool HasSingleAssignedRoomChangeUi => !HasMultipleAssignedRooms && HasInspectorSelection;

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
        BuildReplacementRoomCandidates();

    public IReadOnlyList<RoomOption> ReplacementRoomOptions =>
        BuildRoomOptions(ReplacementRoomCandidates, includeUnknownAssignedRooms: false);

    public IReadOnlyList<ProfessorOption> ProfessorOptions => BuildProfessorOptions();

    public IReadOnlyList<ProfessorOption> AddableCoteachProfessorOptions =>
        ProfessorOptions
            .Where(option => !IsProfessorDisplayAlreadyUsed(option.ProfessorId))
            .ToList();

    public IReadOnlyList<ProfessorOption> SelectedCoteachProfessorOptions =>
        SelectedCourse == null
            ? Array.Empty<ProfessorOption>()
            : SelectedCourse.CoteachProfs
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .DistinctBy(ProfessorDisplayKey)
                .Select(id => new ProfessorOption(id, ProfessorDisplayName(id), id))
                .ToList();

    public bool HasSelectedCoteachProfessors => SelectedCoteachProfessorOptions.Count > 0;

    public bool HasNoSelectedCoteachProfessors => HasInspectorSelection && !HasSelectedCoteachProfessors;

    public IReadOnlyList<RoomOption> AddableRoomOptions =>
        BuildRoomOptions(
            BuildEditableRoomIds(includeAssignedRooms: false)
                .Where(id => !AssignedRoomsForSelectedAssignment.Contains(id)),
            includeUnknownAssignedRooms: true,
            forceSelectable: true);

    public string SelectedRoomAdditionalInfo
    {
        get
        {
            var roomId = HasMultipleAssignedRooms
                ? NormalizeRoomId(SelectedReplacementRoomId)
                : NormalizeRoomId(NewRoomId);
            if (string.IsNullOrWhiteSpace(roomId)) return "";

            var room = BuildRoomCatalog()
                .FirstOrDefault(r => string.Equals(NormalizeRoomId(r.Id), roomId, StringComparison.Ordinal));
            if (room == null) return "";

            var lines = new List<string>();
            if (room.Capacity > 0)
                lines.Add($"수용 인원: {room.Capacity}명");
            lines.Add($"유형: {(room.IsLab ? "실습실" : "일반")}");
            if (room.IsImportedFromExcel)
                lines.Add("Excel에서 가져옴");

            return string.Join(Environment.NewLine, lines);
        }
    }

    public bool HasSelectedRoomAdditionalInfo =>
        ShowRoomAdditionalInfo && !string.IsNullOrWhiteSpace(SelectedRoomAdditionalInfo);

    public string SelectedProfessorName => SelectedCourse == null
        ? "-"
        : ProfessorDisplayName(SelectedCourse.ProfessorId);

    public string SelectedLocationText => InspectorAssignment == null
        ? "-"
        : $"{AcademicLevels.DisplayName(InspectorAssignment.Grade)} / 분반 {Fallback(InspectorAssignment.SectionLabel)} / {SelectedRoomsText}";

    public string SelectedRoomsText => InspectorAssignment == null || InspectorAssignment.Rooms.Count == 0
        ? "-"
        : string.Join(", ", InspectorAssignment.Rooms.Select(RoomDisplayName));

    public string SelectedDurationText => InspectorAssignment == null
        ? "-"
        : $"{InspectorAssignment.RowSpan}시간";

    public string SelectedOccupiedSlotsText
    {
        get
        {
            if (SelectedAssignment != null && SelectedPeriod is int period)
            {
                var last = period + SelectedAssignment.RowSpan - 1;
                return period == last ? $"{period}교시" : $"{period}~{last}교시";
            }

            if (SelectedStagedBlock != null)
                return "보관함";

            return "-";
        }
    }

    public string SelectedBlockText => InspectorAssignment == null
        ? "-"
        : InspectorAssignment.RowSpan > 1 ? $"연속 {InspectorAssignment.RowSpan}시간 블록" : "단일 1시간";

    public string SelectedFixedText => InspectorAssignment == null
        ? "-"
        : InspectorAssignment.IsFixed ? "예" : "아니오";

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

    public string SelectedMultiRoomText => InspectorAssignment == null
        ? "-"
        : InspectorAssignment.Rooms.Count > 1 ? $"예 ({SelectedRoomsText})" : "아니오";

    public string SelectedSlotDisplayText
    {
        get
        {
            if (SelectedDay is not int d || SelectedPeriod is not int period)
            {
                if (SelectedStagedBlock == null)
                    return "";

                return $"보관함 / {AcademicLevels.DisplayName(SelectedStagedBlock.Grade)} / {SelectedStagedBlock.RowSpan}시간";
            }

            var rowSpan = InspectorAssignment?.RowSpan ?? 1;
            var last = period + rowSpan - 1;

            var periodText = period == last
                ? $"{period}교시"
                : $"{period}~{last}교시";

            return $"{DayName(d)}요일 {periodText}";
        }
    }

    public string SelectedMoveStateText => InspectorAssignment == null
        ? "-"
        : SelectedAssignment == null ? "보관함" : "이동 가능";

    public string SelectedBlockedReasonText => SelectedAssignment == null
        ? "-"
        : "없음";

    public bool HasBlockingReasons => false;

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
            if (InspectorAssignment == null) return "-";
            var labels = _workingCrossLinks
                .Where(l => l.CourseIdA == InspectorAssignment.CourseId || l.CourseIdB == InspectorAssignment.CourseId)
                .Select(FormatCrossLink)
                .ToList();
            return labels.Count == 0 ? "없음" : string.Join("; ", labels);
        }
    }

    private Course? SelectedCourse => InspectorAssignment == null
        ? null
        : ResolveCourseForCellAssignment(InspectorAssignment);

    partial void OnSelectedAssignmentChanged(CellAssignment? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        if (value != null)
            DeleteEntireSelectedCourse = false;
        NotifySelectionDetailChanged();
        ApplyRoomChangeCommand.NotifyCanExecuteChanged();
        StageSelectedBlockCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedStagedBlockChanged(StagedBlockItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedStagedBlock));
        if (value != null)
            DeleteEntireSelectedCourse = false;
        if (value != null && SelectedAssignment != null)
            ClearGridSelectionForStagedPlacement();
        if (value != null)
            LoadManualBlockEditorFromStagedBlock(value);
        SyncInspectorControlSelections();
        NotifySelectionDetailChanged();
        UpdateStagedPlacementPreview();
    }

    private void SyncInspectorControlSelections()
    {
        if (InspectorAssignment == null)
        {
            NewRoomId = "";
            SelectedOldRoomId = "";
            SelectedReplacementRoomId = "";
            SelectedPrimaryProfessorId = "";
            SelectedCoteachProfessorId = "";
            SelectedRoomToAddId = "";
            return;
        }

        NewRoomId = InspectorAssignment.Rooms.FirstOrDefault() ?? "";
        SelectedOldRoomId = NewRoomId;
        SelectedReplacementRoomId = ReplacementRoomCandidates.FirstOrDefault() ?? "";
        SelectedPrimaryProfessorId = SelectedCourse?.ProfessorId ?? "";
        SelectedCoteachProfessorId = AddableCoteachProfessorOptions.FirstOrDefault()?.ProfessorId ?? "";
        SelectedRoomToAddId = AddableRoomOptions.FirstOrDefault()?.RoomId ?? "";
    }

    partial void OnNewBlockKindChanged(ManualBlockKind value)
    {
        OnPropertyChanged(nameof(IsManualBlockCourseMode));
        OnPropertyChanged(nameof(IsManualBlockSpecialMode));
        if (value == ManualBlockKind.Course && string.IsNullOrWhiteSpace(NewBlockCourseId))
            NewBlockCourseId = ManualBlockCourseOptions.FirstOrDefault()?.Id ?? "";
    }

    partial void OnNewBlockCourseIdChanged(string value)
    {
        if (NewBlockKind != ManualBlockKind.Course)
            return;

        var course = ManualBlockCourseOptions.FirstOrDefault(c =>
            string.Equals(c.Id, value, StringComparison.Ordinal));
        if (course == null)
            return;

        NewBlockGrade = course.Grade;
        if (NewBlockRowSpan <= 0)
            NewBlockRowSpan = Math.Max(1, course.BlockStructure.FirstOrDefault());
    }

    partial void OnNewBlockRoomIdChanged(string value)
    {
        OnPropertyChanged(nameof(ManualBlockRoomOptions));
    }

    partial void OnNewBlockRowSpanChanged(int value)
    {
        if (value < 1)
            NewBlockRowSpan = 1;
        else if (value > ManualBlockMaxRowSpan)
            NewBlockRowSpan = ManualBlockMaxRowSpan;
        OnPropertyChanged(nameof(NewBlockStructureOptions));
        EnsureNewBlockStructureSelection();
    }

    partial void OnIsNewBlockStructureEnabledChanged(bool value)
    {
        if (!value)
            NewBlockStructureText = "";
        else
            EnsureNewBlockStructureSelection();
    }

    partial void OnNewBlockSectionCountChanged(int value)
    {
        if (value < 1)
            NewBlockSectionCount = 1;
        else if (value > 9)
            NewBlockSectionCount = 9;
    }

    partial void OnNewRoomIdChanged(string value)
    {
        OnPropertyChanged(nameof(HasPendingInspectorChanges));
        OnPropertyChanged(nameof(CanApplyInspectorChanges));
        OnPropertyChanged(nameof(SelectedRoomAdditionalInfo));
        OnPropertyChanged(nameof(HasSelectedRoomAdditionalInfo));
        ApplyRoomChangeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedOldRoomIdChanged(string value)
    {
        OnPropertyChanged(nameof(ReplacementRoomCandidates));
        OnPropertyChanged(nameof(ReplacementRoomOptions));
        if (HasMultipleAssignedRooms && !ReplacementRoomCandidates.Contains(SelectedReplacementRoomId))
            SelectedReplacementRoomId = ReplacementRoomOptions.FirstOrDefault(option => option.IsSelectable)?.RoomId
                ?? ReplacementRoomCandidates.FirstOrDefault()
                ?? "";
        OnPropertyChanged(nameof(SelectedRoomAdditionalInfo));
        OnPropertyChanged(nameof(HasSelectedRoomAdditionalInfo));
        OnPropertyChanged(nameof(HasPendingInspectorChanges));
        OnPropertyChanged(nameof(CanApplyInspectorChanges));
        ApplyRoomChangeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedReplacementRoomIdChanged(string value)
    {
        OnPropertyChanged(nameof(HasPendingInspectorChanges));
        OnPropertyChanged(nameof(CanApplyInspectorChanges));
        OnPropertyChanged(nameof(SelectedRoomAdditionalInfo));
        OnPropertyChanged(nameof(HasSelectedRoomAdditionalInfo));
        ApplyRoomChangeCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowRoomAdditionalInfoChanged(bool value)
    {
        new ManualEditUserSettings { ShowRoomAdditionalInfo = value }.Save();
        OnPropertyChanged(nameof(SelectedRoomAdditionalInfo));
        OnPropertyChanged(nameof(HasSelectedRoomAdditionalInfo));
        OnPropertyChanged(nameof(HasUnsavedChanges));
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
        _sessionData = CloneAppData(SavedTimetableSnapshotResolver.Resolve(record.SnapshotJson));
        var assignments = record.Assignments
            .Select(r => new SolutionAssignment(r.CourseId, r.Day, r.Period, r.RoomId, r.AssignmentId ?? ""))
            .ToList();
        LoadCore(new RankedSolution(
            assignments,
            new SolutionScore(0, 0, 0, 0),
            record.LunchPeriodsByDay));
        EditingSavedTimetableId = record.Id;
        SaveName = record.Name;
    }

    public ManualConstraintEditContext BuildConstraintEditContext()
    {
        var snapshot = BuildSaveSnapshot() ?? _workspace.SchedulingSnapshot();
        var snapshotCopy = JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(snapshot))!;
        var solution = new RankedSolution(
            _working.ToList(),
            BaseSolution?.Score ?? new SolutionScore(0, 0, 0, 0),
            _lunchPeriodsByDay);
        var handoff = new ManualEditHandoff(
            solution,
            ToSavedManualCrossLinks(),
            EditingSavedTimetableId,
            SaveName);
        return new ManualConstraintEditContext(snapshotCopy, handoff);
    }

    public void LoadFromSolution(RankedSolution solution)
    {
        _sessionData = null;
        EditingSavedTimetableId = null;
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
        IReadOnlyList<SavedManualCrossLinkRow>? manualCrossLinks = null,
        string? savedTimetableId = null)
    {
        _sessionData = CloneAppData(snapshot);
        EditingSavedTimetableId = savedTimetableId;
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
        _lunchPeriodsByDay = solution.LunchPeriodsByDay == null
            ? SchedulePolicyRules.StaticLunchPeriodsByDay(SessionSchedulePolicy, Constants.Days)
            : new Dictionary<int, int>(solution.LunchPeriodsByDay);
        _working = EnsureAssignmentIds(solution.Assignment);
        _workingCrossGroups = SessionCrossGroups
            .Select(g => new CrossGroup { Id = g.Id, BaseIds = g.BaseIds.ToList() })
            .ToList();
        _workingCrossLinks.Clear();
        StagedBlocks.Clear();
        SelectedStagedBlock = null;
        NotifyStagedBlocksChanged();
        _undoStack.Clear();
        _redoStack.Clear();
        NotifyUndoRedoCanExecuteChanged();
        InitializeManualBlockEditorDefaults(clearStatus: true);
        if (manualCrossLinks != null)
            RestoreSavedManualCrossLinks(manualCrossLinks);
        _resetBaselineSnapshot = CaptureSnapshot();
        Rerender();
        ClearSelectionCore();
        OnPropertyChanged(nameof(HasUnsavedChanges));
        StatusMessage = "수업 블록을 클릭한 뒤, 초록색 칸을 클릭해 이동하세요.";
    }

    private List<SolutionAssignment> EnsureAssignmentIds(IReadOnlyList<SolutionAssignment> assignments)
    {
        var source = assignments.ToList();
        var idsByIndex = new string[source.Count];
        var courseKeys = source.Select(BuildAssignmentCourseIdentityKey).ToList();
        var indexed = source
            .Select((Assignment, Index) => new { Assignment, Index, CourseKey = courseKeys[Index] })
            .ToList();

        foreach (var group in indexed.GroupBy(x => (x.CourseKey, x.Assignment.Day)))
        {
            var byPeriod = group
                .GroupBy(x => x.Assignment.Period)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToList());
            var orderedPeriods = byPeriod.Keys.OrderBy(p => p).ToList();
            var i = 0;
            while (i < orderedPeriods.Count)
            {
                var start = orderedPeriods[i];
                var roomSet = byPeriod[start]
                    .Select(x => x.Assignment.RoomId)
                    .Where(room => !string.IsNullOrWhiteSpace(room))
                    .OrderBy(room => room, StringComparer.Ordinal)
                    .ToList();
                var maxLength = MaxOccurrenceLength(group.Key.CourseKey);
                var j = i;
                while (j + 1 < orderedPeriods.Count
                    && orderedPeriods[j + 1] == orderedPeriods[j] + 1
                    && j - i + 1 < maxLength
                    && SameRooms(roomSet, byPeriod[orderedPeriods[j + 1]].Select(x => x.Assignment.RoomId)))
                {
                    j++;
                }

                var periods = orderedPeriods.Skip(i).Take(j - i + 1).ToHashSet();
                var rows = group
                    .Where(x => periods.Contains(x.Assignment.Period)
                        && roomSet.Contains(x.Assignment.RoomId, StringComparer.Ordinal))
                    .ToList();
                var existingIds = rows
                    .Select(x => x.Assignment.AssignmentId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var id = existingIds.Count == 1
                    ? existingIds[0]
                    : BuildGeneratedAssignmentId(group.Key.CourseKey, group.Key.Day, start, roomSet);
                foreach (var row in rows)
                    idsByIndex[row.Index] = id;

                i = j + 1;
            }
        }

        return source
            .Select((a, index) => new SolutionAssignment(
                a.CourseId,
                a.Day,
                a.Period,
                a.RoomId,
                string.IsNullOrWhiteSpace(idsByIndex[index]) ? a.AssignmentId : idsByIndex[index]))
            .ToList();

        static bool SameRooms(IReadOnlyList<string> left, IEnumerable<string> right) =>
            left.SequenceEqual(
                right
                    .Where(room => !string.IsNullOrWhiteSpace(room))
                    .OrderBy(room => room, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private string BuildAssignmentCourseIdentityKey(SolutionAssignment assignment)
    {
        var candidates = SessionCourses
            .Select((Course, Index) => new { Course, Index })
            .Where(c => c.Course.Id == assignment.CourseId)
            .ToList();
        if (candidates.Count == 0)
            return string.Join("\u001f", assignment.CourseId, assignment.RoomId);
        if (candidates.Count == 1)
            return CourseIdentityKey(candidates[0].Course, candidates[0].Index);

        var roomId = NormalizeRoomId(assignment.RoomId);
        var byFixedRoom = candidates
            .Where(c => c.Course.FixedRooms.Any(room => string.Equals(NormalizeRoomId(room), roomId, StringComparison.Ordinal)))
            .ToList();
        if (byFixedRoom.Count == 1)
            return CourseIdentityKey(byFixedRoom[0].Course, byFixedRoom[0].Index);

        return string.Join("\u001f", assignment.CourseId, "room", roomId);
    }

    private int MaxOccurrenceLength(string courseIdentityKey)
    {
        var course = SessionCourses
            .Select((Course, Index) => new { Course, Index })
            .FirstOrDefault(c => CourseIdentityKey(c.Course, c.Index) == courseIdentityKey)
            ?.Course;
        if (course == null)
            return 1;
        if (course.BlockStructure.Count > 0)
            return Math.Max(1, course.BlockStructure.Max());
        return course.HoursPerWeek > 1 ? course.HoursPerWeek : 1;
    }

    private static string CourseIdentityKey(Course course, int index) =>
        string.Join("\u001f", course.Id, course.Section, course.ProfessorId, course.Name, index);

    private static string BuildGeneratedAssignmentId(
        string courseIdentityKey,
        int day,
        int startPeriod,
        IReadOnlyList<string> rooms) =>
        string.Join("\u001f", "occ", courseIdentityKey, day, startPeriod, BuildRoomIdsKey(rooms));

    public void LoadFromSavedTimetable(SavedTimetableRecord timetable)
    {
        _sessionData = CloneAppData(SavedTimetableSnapshotResolver.Resolve(timetable.SnapshotJson));
        EditingSavedTimetableId = timetable.Id;
        var assignments = timetable.Assignments
            .Select(r => new SolutionAssignment(r.CourseId, r.Day, r.Period, r.RoomId, r.AssignmentId ?? ""))
            .ToList();

        _isRestoringSavedTimetable = true;
        _suppressSavedCrossValidation = true;
        _hasUserEditedAfterLoad = false;
        _lastCrossCleanupExecuted = false;
        _lastCrossCleanupSkipReason = "";
        try
        {
            _working = EnsureAssignmentIds(assignments);
            _lunchPeriodsByDay = timetable.LunchPeriodsByDay == null
                ? SchedulePolicyRules.StaticLunchPeriodsByDay(SessionSchedulePolicy, Constants.Days)
                : new Dictionary<int, int>(timetable.LunchPeriodsByDay);
            BaseSolution = new RankedSolution(
                _working.ToList(),
                new SolutionScore(0, 0, 0, 0),
                _lunchPeriodsByDay);
            _workingCrossGroups = SessionCrossGroups
                .Select(g => new CrossGroup { Id = g.Id, BaseIds = g.BaseIds.ToList() })
                .ToList();
            StagedBlocks.Clear();
            SelectedStagedBlock = null;
            NotifyStagedBlocksChanged();
            _undoStack.Clear();
            _redoStack.Clear();
            NotifyUndoRedoCanExecuteChanged();

            var ignored = RestoreSavedManualCrossLinks(timetable.ManualCrossLinks ?? Array.Empty<SavedManualCrossLinkRow>());
            _resetBaselineSnapshot = CaptureSnapshot();
            Rerender();
            ClearSelectionCore();
            OnPropertyChanged(nameof(HasUnsavedChanges));
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
        if (SelectedStagedBlock != null)
        {
            if (grade != SelectedStagedBlock.Grade)
            {
                ClearStagedBlockSelection();
                return;
            }

            if (assignment == null)
                TryPlaceSelectedStagedBlock(day, period, grade, subColumnIdx);
            else
            {
                SelectedStagedBlock = null;
                SelectCell(day, period, grade, subColumnIdx, assignment);
            }
            return;
        }

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
            var selectedKey = BuildManualCrossAssignmentKey(SelectedAssignment, SelectedDay ?? day, SelectedPeriod ?? period);
            if (TryMoveSelectedTo(day, period, grade, subColumnIdx))
                RemoveCrossForAssignment(selectedKey, "빈 칸 이동으로 기존 크로스가 해제되었습니다.", recordUndo: false);
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
        if (ManualCrossAssignmentMatches(selectedKey, targetKey))
            return Exit(CrossHoverState.Hidden("현재 선택한 수업입니다."), "same-assignment-key");
        if (IsAlreadyCrossed(selectedKey, targetKey))
            return Exit(CrossHoverState.Hidden("이미 크로스가 설정된 수업입니다."), "already-crossed-pair");
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
        if (ManualCrossAssignmentMatches(sourceKey, targetKey))
            return Exit(CrossHoverState.Hidden("현재 선택한 수업입니다."), "same-assignment-key");
        if (IsAlreadyCrossed(sourceKey, targetKey))
            return Exit(CrossHoverState.Hidden("이미 크로스가 설정된 수업입니다."), "already-crossed-pair");
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
        return ValidateGeneralManualMove(
            assignment,
            sourceDay,
            sourcePeriod,
            targetDay,
            targetPeriod,
            targetGrade,
            targetSubColumnIdx).BlockingReasons.Count == 0;
    }

    public void BeginDragMovePreview(
        int sourceDay,
        int sourcePeriod,
        int sourceGrade,
        int sourceSubColumnIdx,
        CellAssignment? source)
    {
        if (source == null) return;
        SelectedStagedBlock = null;
        SelectedDay = sourceDay;
        SelectedPeriod = sourcePeriod;
        SelectedAssignment = source;
        _selectedGridCell = new UnifiedCellKey(sourceDay, sourcePeriod, sourceGrade, sourceSubColumnIdx);
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(source, sourceDay, sourcePeriod));
    }

    public void EndDragMovePreview()
    {
        if (SelectedStagedBlock != null)
        {
            UpdateStagedPlacementPreview();
            return;
        }

        if (SelectedAssignment != null && SelectedDay is int day && SelectedPeriod is int period)
        {
            Grid.SetEditState(_selectedGridCell, BuildMoveStates(SelectedAssignment, day, period));
            return;
        }

        Grid.ClearEditState();
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
        if (assignment != null && SelectedStagedBlock != null)
            SelectedStagedBlock = null;
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
            SelectedPrimaryProfessorId = SelectedCourse?.ProfessorId ?? "";
            SelectedCoteachProfessorId = AddableCoteachProfessorOptions.FirstOrDefault()?.ProfessorId ?? "";
            SelectedRoomToAddId = AddableRoomOptions.FirstOrDefault()?.RoomId ?? "";
            StatusMessage = $"선택: {CourseSectionDisplayName(assignment)} — "
                + $"{DayName(day)} {period}교시";
            Grid.SetEditState(_selectedGridCell, BuildMoveStates(assignment, day, period));
            RefreshConflicts();
        }
        else
        {
            NewRoomId = "";
            SelectedOldRoomId = "";
            SelectedReplacementRoomId = "";
            SelectedPrimaryProfessorId = "";
            SelectedCoteachProfessorId = "";
            SelectedRoomToAddId = "";
            StatusMessage = $"{DayName(day)} {period}교시 — 비어있음";
            Grid.ClearEditState();
            RefreshConflicts();
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

        var oldRoomId = NormalizeRoomId(SelectedAssignment.Rooms.FirstOrDefault());
        var replacementRoomId = NormalizeRoomId(NewRoomId);
        if (string.IsNullOrWhiteSpace(replacementRoomId))
        {
            SetRoomChangeFailure("새 강의실을 선택하세요.");
            return;
        }
        if (string.IsNullOrWhiteSpace(oldRoomId))
        {
            SetRoomChangeFailure("변경할 배정 항목을 찾지 못했습니다.");
            return;
        }
        if (oldRoomId == replacementRoomId)
        {
            SetRoomChangeFailure("기존 강의실과 동일합니다.");
            return;
        }

        if (!KnownRoomExists(replacementRoomId))
        {
            SetRoomChangeFailure("존재하지 않는 강의실입니다.");
            return;
        }

        if (!IsRoomSelectableForCurrentChange(oldRoomId, replacementRoomId))
        {
            SetRoomChangeFailure("선택할 수 없는 강의실입니다.");
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

        var oldRoomId = NormalizeRoomId(SelectedOldRoomId);
        var replacementRoomId = NormalizeRoomId(SelectedReplacementRoomId);
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

        if (!KnownRoomExists(replacementRoomId))
        {
            SetRoomChangeFailure("존재하지 않는 강의실입니다.");
            return;
        }

        if (!IsRoomSelectableForCurrentChange(oldRoomId, replacementRoomId))
        {
            SetRoomChangeFailure("선택할 수 없는 강의실입니다.");
            return;
        }

        if (!TryApplyRoomChange(oldRoomId, replacementRoomId, out var changed))
        {
            return;
        }

        RefreshSelectionAfterRoomChange(SelectedAssignment.CourseId, day, period);
        NewRoomId = SelectedAssignment.Rooms.FirstOrDefault() ?? "";
        SelectedOldRoomId = replacementRoomId;
        SelectedReplacementRoomId = ReplacementRoomOptions.FirstOrDefault(option => option.IsSelectable)?.RoomId
            ?? ReplacementRoomCandidates.FirstOrDefault()
            ?? "";
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
        if (!TryBuildRoomChangeCandidate(
                oldRoomId,
                replacementRoomId,
                out var candidate,
                out var affectedCourses,
                out changed))
        {
            SetRoomChangeFailure(BuildRoomChangeTargetNotFoundMessage(cid, day, period, SelectedAssignment.RowSpan, oldRoomId, replacementRoomId));
            return false;
        }

        if (!IsRoomChangeCandidateSelectable(oldRoomId, replacementRoomId, affectedCourses))
        {
            SetRoomChangeFailure("선택할 수 없는 강의실입니다.");
            return false;
        }

        _working = candidate;
        MarkUserEditedAfterLoad();
        Rerender();
        PushUndoSnapshot(snapshot);
        StatusMessage = $"{CourseDisplayName(cid)}의 강의실 {RoomDisplayName(oldRoomId)} → {RoomDisplayName(replacementRoomId)} (변경 {changed}건)";

        return true;
    }

    private bool IsRoomSelectableForCurrentChange(string oldRoomId, string replacementRoomId)
    {
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null)
            return false;

        if (string.Equals(NormalizeRoomId(oldRoomId), NormalizeRoomId(replacementRoomId), StringComparison.Ordinal))
            return true;

        if (!TryBuildRoomChangeCandidate(
                oldRoomId,
                replacementRoomId,
                out var candidate,
                out var affectedCourses,
                out _))
            return false;

        return IsRoomChangeCandidateSelectable(oldRoomId, replacementRoomId, affectedCourses);
    }

    private bool IsProfessorDisplayAlreadyUsed(string professorId)
    {
        if (SelectedCourse == null)
            return false;

        var displayKey = ProfessorDisplayKey(professorId);
        if (string.IsNullOrWhiteSpace(displayKey))
            return false;

        return ProfessorDisplayKey(SelectedCourse.ProfessorId) == displayKey
            || SelectedCourse.CoteachProfs.Any(id => ProfessorDisplayKey(id) == displayKey);
    }

    private bool TryBuildRoomChangeCandidate(
        string oldRoomId,
        string replacementRoomId,
        out List<SolutionAssignment> candidate,
        out IReadOnlyList<Course> affectedCourses,
        out int changed)
    {
        candidate = _working.ToList();
        affectedCourses = Array.Empty<Course>();
        changed = 0;
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null)
            return false;

        var selectedCourse = SelectedCourse;
        if (selectedCourse == null)
            return false;

        var normalizedOldRoomId = NormalizeRoomId(oldRoomId);
        var linkedCourses = SessionCourses
            .Where(course => IsSameCourseFamily(selectedCourse, course))
            .Where(course => IsSameCourseSection(selectedCourse, course)
                || _working.Any(assignment =>
                    string.Equals(NormalizeRoomId(assignment.RoomId), normalizedOldRoomId, StringComparison.Ordinal)
                    && ReferenceEquals(ResolveCourseForAssignment(assignment), course)))
            .Distinct()
            .ToList();
        if (!linkedCourses.Contains(selectedCourse))
            linkedCourses.Insert(0, selectedCourse);

        var affected = new HashSet<Course>(linkedCourses);
        for (var i = 0; i < candidate.Count; i++)
        {
            var a = candidate[i];
            if (!string.Equals(NormalizeRoomId(a.RoomId), normalizedOldRoomId, StringComparison.Ordinal))
                continue;
            var course = ResolveCourseForAssignment(a);
            if (course == null || !affected.Contains(course))
                continue;

            candidate[i] = new SolutionAssignment(a.CourseId, a.Day, a.Period, replacementRoomId, a.AssignmentId);
            changed++;
        }

        affectedCourses = linkedCourses
            .Where(course => _working.Any(assignment =>
                string.Equals(NormalizeRoomId(assignment.RoomId), normalizedOldRoomId, StringComparison.Ordinal)
                && ReferenceEquals(ResolveCourseForAssignment(assignment), course)))
            .ToList();
        return changed > 0;
    }

    private bool IsRoomChangeCandidateSelectable(
        string oldRoomId,
        string replacementRoomId,
        IReadOnlyList<Course> affectedCourses)
    {
        if (SelectedAssignment == null)
            return false;

        if (string.Equals(NormalizeRoomId(oldRoomId), NormalizeRoomId(replacementRoomId), StringComparison.Ordinal))
            return true;

        if (!BuildRoomCatalog().Any(r =>
                string.Equals(
                    NormalizeRoomId(r.Id),
                    NormalizeRoomId(replacementRoomId),
                    StringComparison.Ordinal)))
            return false;

        return affectedCourses.Count > 0;
    }

    private static bool IsSameCourseFamily(Course selected, Course candidate)
    {
        var selectedBaseId = DomainHelpers.BaseId(selected.Id).Trim();
        var candidateBaseId = DomainHelpers.BaseId(candidate.Id).Trim();
        if (!string.IsNullOrWhiteSpace(selectedBaseId)
            && string.Equals(selectedBaseId, candidateBaseId, StringComparison.OrdinalIgnoreCase))
            return true;

        var selectedName = NormalizeCourseName(selected.Name);
        return !string.IsNullOrWhiteSpace(selectedName)
            && string.Equals(selectedName, NormalizeCourseName(candidate.Name), StringComparison.Ordinal);
    }

    private static bool IsSameCourseSection(Course left, Course right) =>
        ReferenceEquals(left, right)
        || string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && left.Section == right.Section
            && string.Equals(left.ProfessorId, right.ProfessorId, StringComparison.Ordinal)
            && string.Equals(NormalizeCourseName(left.Name), NormalizeCourseName(right.Name), StringComparison.Ordinal);

    private static string NormalizeCourseName(string? name) =>
        string.Concat((name ?? "").Where(ch => !char.IsWhiteSpace(ch))).ToUpperInvariant();

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

    private string BuildRoomChangeTargetNotFoundMessage(
        string courseId,
        int day,
        int selectedPeriod,
        int rowSpan,
        string oldRoomId,
        string newRoomId)
    {
        return $"변경 불가 — {CourseDisplayName(courseId)}의 강의실 배정 정보를 찾지 못했습니다. 시간표를 새로고침한 뒤 다시 시도하세요.";
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
            RemoveCrossForAssignment(BuildManualCrossAssignmentKey(SelectedAssignment, SelectedDay ?? 0, SelectedPeriod ?? 0), null);
        ClearSelectionCore();
        StatusMessage = "선택이 해제되었습니다.";
    }

    [RelayCommand(CanExecute = nameof(CanStageSelectedBlock))]
    private void StageSelectedBlock()
    {
        if (SelectedAssignment == null || SelectedDay is not int day || SelectedPeriod is not int period)
        {
            StatusMessage = "보관할 블럭을 선택하세요.";
            return;
        }

        if (!TryGetMovingAssignments(SelectedAssignment, day, period, out var rows, out var reason))
        {
            if (SelectedAssignment.IsSchoolFixed)
            {
                var specialSnapshot = CaptureSnapshot();
                if (TryBuildSchoolFixedDisplayStagedBlock(
                    SelectedAssignment,
                    day,
                    period,
                    out var specialStaged,
                    out var specialReason))
                {
                    MarkUserEditedAfterLoad();
                    ClearSelectionCore();
                    StagedBlocks.Add(specialStaged);
                    NotifyStagedBlocksChanged();
                    Rerender();
                    PushUndoSnapshot(specialSnapshot);
                    StatusMessage = $"{specialStaged.Title} 블럭을 보관함으로 뺐습니다. 보관함에서 다시 선택하거나 드래그하세요.";
                    return;
                }

                RestoreManualBlockSessionOnly(specialSnapshot);
                if (!string.IsNullOrWhiteSpace(specialReason))
                    reason = specialReason;
            }

            StatusMessage = reason;
            return;
        }

        var snapshot = CaptureSnapshot();
        var staged = BuildStagedBlockItem(SelectedAssignment, day, period, rows);
        var key = BuildManualCrossAssignmentKey(SelectedAssignment, day, period);

        var stagedRows = rows.ToHashSet();
        _working = _working
            .Where(a => !stagedRows.Contains(a))
            .ToList();
        _workingCrossLinks.RemoveAll(link => link.Contains(key));
        MarkUserEditedAfterLoad();

        ClearSelectionCore();
        StagedBlocks.Add(staged);
        NotifyStagedBlocksChanged();
        Rerender();
        PushUndoSnapshot(snapshot);

        StatusMessage = $"{staged.Title} 블럭을 보관함으로 뺐습니다. 보관함에서 다시 선택하거나 드래그하세요.";
    }

    [RelayCommand]
    private void ClearStagedBlockSelection()
    {
        SelectedStagedBlock = null;
        Grid.ClearEditState();
        StatusMessage = "보관함 선택을 해제했습니다.";
    }

    [RelayCommand]
    private void AddManualBlockToStaging()
    {
        var snapshot = CaptureSnapshot();
        if (HasSimpleManualBlockInput())
        {
            if (!TryBuildSimpleManualCourseStagedBlocks(preferredId: null, out var items, out var simpleReason))
            {
                RestoreManualBlockSessionOnly(snapshot);
                SetManualBlockError(simpleReason);
                return;
            }

            foreach (var stagedItem in items)
                StagedBlocks.Add(stagedItem);
            SelectedStagedBlock = items.LastOrDefault();
            NotifyStagedBlocksChanged();
            MarkUserEditedAfterLoad();
            PushUndoSnapshot(snapshot);
            SetManualBlockSuccess(items.Count == 1
                ? $"{items[0].Title} 블럭을 보관함에 추가했습니다."
                : $"{items.Count}개 블럭을 보관함에 추가했습니다.");
            return;
        }

        if (!TryBuildManualStagedBlock(preferredId: null, out var item, out var reason))
        {
            SetManualBlockError(reason);
            return;
        }

        StagedBlocks.Add(item);
        SelectedStagedBlock = item;
        NotifyStagedBlocksChanged();
        MarkUserEditedAfterLoad();
        PushUndoSnapshot(snapshot);
        SetManualBlockSuccess($"{item.Title} 블럭을 보관함에 추가했습니다.");
    }

    [RelayCommand]
    private void UpdateSelectedStagedBlock()
    {
        var selected = SelectedStagedBlock;
        if (selected == null)
        {
            SetManualBlockError("수정할 보관함 블럭을 먼저 선택하세요.");
            return;
        }

        var snapshot = CaptureSnapshot();
        if (!TryBuildManualStagedBlock(selected.Id, out var updated, out var reason))
        {
            RestoreManualBlockSessionOnly(snapshot);
            SetManualBlockError(reason);
            return;
        }

        var index = StagedBlocks.IndexOf(selected);
        if (index < 0)
        {
            RestoreManualBlockSessionOnly(snapshot);
            SetManualBlockError("선택한 보관함 블럭을 찾을 수 없습니다.");
            return;
        }

        StagedBlocks[index] = updated;
        RemoveUnusedManualBlockCourses();
        SelectedStagedBlock = updated;
        NotifyStagedBlocksChanged();
        MarkUserEditedAfterLoad();
        PushUndoSnapshot(snapshot);
        SetManualBlockSuccess($"{updated.Title} 블럭을 수정했습니다.");
    }

    [RelayCommand]
    private void DeleteSelectedStagedBlock()
    {
        var selected = SelectedStagedBlock;
        if (selected == null)
        {
            SetManualBlockError("삭제할 보관함 블럭을 먼저 선택하세요.");
            return;
        }

        if (DeleteEntireSelectedCourse)
        {
            DeleteEntireCourseForAssignment(selected.Assignment);
            return;
        }

        var snapshot = CaptureSnapshot();
        StagedBlocks.Remove(selected);
        SelectedStagedBlock = null;
        DeleteEntireSelectedCourse = false;
        RemoveUnusedManualBlockCourses();
        NotifyStagedBlocksChanged();
        MarkUserEditedAfterLoad();
        PushUndoSnapshot(snapshot);
        SetManualBlockSuccess($"{selected.Title} 블럭을 보관함에서 삭제했습니다.");
    }

    [RelayCommand]
    private void ResetManualBlockEditor()
    {
        InitializeManualBlockEditorDefaults(clearStatus: true);
        StatusMessage = "블럭 추가 입력값을 초기화했습니다.";
    }

    [RelayCommand]
    private void ApplyPrimaryProfessorChange()
    {
        var professorId = NormalizeManualInput(SelectedPrimaryProfessorId);
        if (string.IsNullOrWhiteSpace(professorId))
        {
            SetRoomChangeFailure("담당 교수는 최소 1명 필요합니다.");
            return;
        }

        if (!ProfessorOptions.Any(option => string.Equals(option.ProfessorId, professorId, StringComparison.Ordinal)))
        {
            SetRoomChangeFailure("선택할 수 없는 교수입니다.");
            return;
        }

        ApplySelectedCourseMutation(
            course =>
            {
                course.ProfessorId = professorId;
                course.CoteachProfs.RemoveAll(id => IsSameProfessorId(id, professorId));
            },
            "담당 교수를 변경했습니다.");
    }

    [RelayCommand]
    private void AddCoteachProfessor()
    {
        var professorId = NormalizeManualInput(SelectedCoteachProfessorId);
        if (string.IsNullOrWhiteSpace(professorId))
        {
            SetRoomChangeFailure("추가할 팀티칭 교수를 선택하세요.");
            return;
        }

        if (SelectedCourse != null && IsSameProfessorId(SelectedCourse.ProfessorId, professorId))
        {
            SetRoomChangeFailure("담당 교수는 팀티칭 교수로 중복 추가할 수 없습니다.");
            return;
        }

        if (!ProfessorOptions.Any(option => string.Equals(option.ProfessorId, professorId, StringComparison.Ordinal)))
        {
            SetRoomChangeFailure("선택할 수 없는 교수입니다.");
            return;
        }

        ApplySelectedCourseMutation(
            course =>
            {
                if (!course.CoteachProfs.Any(id => IsSameProfessorId(id, professorId)))
                    course.CoteachProfs.Add(professorId);
            },
            "팀티칭 교수를 추가했습니다.");
    }

    [RelayCommand]
    private void RemoveCoteachProfessor(string? professorId)
    {
        var normalizedProfessorId = NormalizeManualInput(professorId);
        if (string.IsNullOrWhiteSpace(normalizedProfessorId))
        {
            SetRoomChangeFailure("삭제할 팀티칭 교수를 선택하세요.");
            return;
        }

        ApplySelectedCourseMutation(
            course => course.CoteachProfs.RemoveAll(id => IsSameProfessorId(id, normalizedProfessorId)),
            "팀티칭 교수를 삭제했습니다.");
    }

    [RelayCommand]
    private void AddSelectedRoomToAssignment()
    {
        if (SelectedAssignment == null || SelectedDay is not int day || SelectedPeriod is not int period)
        {
            if (SelectedStagedBlock != null)
            {
                AddSelectedRoomToStagedBlock();
                return;
            }

            SetRoomChangeFailure("수업을 선택하세요.");
            return;
        }

        var roomId = NormalizeRoomId(SelectedRoomToAddId);
        if (string.IsNullOrWhiteSpace(roomId))
        {
            SetRoomChangeFailure("추가할 강의실을 선택하세요.");
            return;
        }

        if (!KnownRoomExists(roomId))
        {
            SetRoomChangeFailure("존재하지 않는 강의실입니다.");
            return;
        }

        if (AssignedRoomsForSelectedAssignment.Contains(roomId))
        {
            SetRoomChangeFailure("이미 배정된 강의실입니다.");
            return;
        }

        if (!TryGetMovingAssignments(SelectedAssignment, day, period, out var rows, out var reason))
        {
            SetRoomChangeFailure(reason);
            return;
        }

        var snapshot = CaptureSnapshot();
        var periods = rows
            .Select(row => row.Period)
            .Distinct()
            .OrderBy(value => value)
            .ToList();
        foreach (var rowPeriod in periods)
        {
            _working.Add(new SolutionAssignment(
                SelectedAssignment.CourseId,
                day,
                rowPeriod,
                roomId,
                SelectedAssignment.AssignmentId));
        }

        MarkUserEditedAfterLoad();
        Rerender();
        RefreshSelectionAfterBlockEdit(SelectedAssignment.CourseId, day, period, SelectedAssignment.AssignmentId);
        PushUndoSnapshot(snapshot);
        SetRoomChangeSuccess("강의실을 추가했습니다.");
    }

    [RelayCommand]
    private void RemoveAssignedRoom(string? roomId)
    {
        if (SelectedAssignment == null || SelectedDay is not int day || SelectedPeriod is not int period)
        {
            if (SelectedStagedBlock != null)
            {
                RemoveRoomFromSelectedStagedBlock(roomId);
                return;
            }

            SetRoomChangeFailure("수업을 선택하세요.");
            return;
        }

        var normalizedRoomId = NormalizeRoomId(roomId);
        if (string.IsNullOrWhiteSpace(normalizedRoomId))
        {
            SetRoomChangeFailure("삭제할 강의실을 선택하세요.");
            return;
        }

        if (AssignedRoomsForSelectedAssignment.Count <= 1)
        {
            SetRoomChangeFailure("강의실은 최소 1개 필요합니다.");
            return;
        }

        if (!TryGetMovingAssignments(SelectedAssignment, day, period, out var rows, out var reason))
        {
            SetRoomChangeFailure(reason);
            return;
        }

        var removeRows = rows
            .Where(row => string.Equals(NormalizeRoomId(row.RoomId), normalizedRoomId, StringComparison.Ordinal))
            .ToHashSet();
        if (removeRows.Count == 0)
        {
            SetRoomChangeFailure("선택한 강의실 배정을 찾을 수 없습니다.");
            return;
        }

        var snapshot = CaptureSnapshot();
        _working = _working.Where(row => !removeRows.Contains(row)).ToList();
        MarkUserEditedAfterLoad();
        Rerender();
        RefreshSelectionAfterBlockEdit(SelectedAssignment.CourseId, day, period, SelectedAssignment.AssignmentId);
        PushUndoSnapshot(snapshot);
        SetRoomChangeSuccess("강의실을 삭제했습니다.");
    }

    [RelayCommand]
    private void DeleteSelectedBlock()
    {
        if (SelectedAssignment == null || SelectedDay is not int day || SelectedPeriod is not int period)
        {
            if (SelectedStagedBlock != null)
            {
                DeleteSelectedStagedBlock();
                return;
            }

            SetRoomChangeFailure("삭제할 블럭을 선택하세요.");
            return;
        }

        if (DeleteEntireSelectedCourse)
        {
            DeleteEntireCourseForAssignment(SelectedAssignment);
            return;
        }

        var snapshot = CaptureSnapshot();
        if (!TryGetMovingAssignments(SelectedAssignment, day, period, out var rows, out var reason))
        {
            if (!SelectedAssignment.IsSchoolFixed
                || !TryDeleteSelectedSchoolFixedBlock(day, period, out reason))
            {
                RestoreManualBlockSessionOnly(snapshot);
                SetRoomChangeFailure(reason);
                return;
            }

            MarkUserEditedAfterLoad();
            ClearSelectionCore();
            DeleteEntireSelectedCourse = false;
            Rerender();
            PushUndoSnapshot(snapshot);
            StatusMessage = "선택한 고정 블럭을 삭제했습니다.";
            return;
        }

        var removeRows = rows.ToHashSet();
        var key = BuildManualCrossAssignmentKey(SelectedAssignment, day, period);
        _working = _working.Where(row => !removeRows.Contains(row)).ToList();
        _workingCrossLinks.RemoveAll(link => link.Contains(key));
        RemoveUnusedManualBlockCourses();
        MarkUserEditedAfterLoad();
        ClearSelectionCore();
        DeleteEntireSelectedCourse = false;
        Rerender();
        PushUndoSnapshot(snapshot);
        StatusMessage = "선택한 블럭을 삭제했습니다.";
    }

    private void DeleteEntireCourseForAssignment(CellAssignment assignment)
    {
        var targetCourse = ResolveCourseForWholeCourseDelete(assignment);
        var targetCourseIds = BuildWholeCourseDeleteCourseIds(assignment, targetCourse);
        if (targetCourseIds.Count == 0)
        {
            SetRoomChangeFailure("삭제할 과목 정보를 확인할 수 없습니다.");
            DeleteEntireSelectedCourse = false;
            return;
        }

        var snapshot = CaptureSnapshot();
        var courseName = WholeCourseDeleteDisplayName(assignment, targetCourse);
        var removedRows = _working.Count(row => RowBelongsToWholeCourseDeleteTarget(row, targetCourseIds, targetCourse, assignment));
        _working = _working
            .Where(row => !RowBelongsToWholeCourseDeleteTarget(row, targetCourseIds, targetCourse, assignment))
            .ToList();

        var stagedToRemove = StagedBlocks
            .Where(block => AssignmentBelongsToWholeCourseDeleteTarget(block.Assignment, targetCourseIds, targetCourse, assignment))
            .ToList();
        foreach (var staged in stagedToRemove)
            StagedBlocks.Remove(staged);

        var fixedSlotsRemoved = ClearSchoolFixedCourseSlotsIfNeeded(targetCourseIds);
        _workingCrossLinks.RemoveAll(link => CrossLinkReferencesAnyCourse(link, targetCourseIds));
        RemoveUnusedManualBlockCourses();

        SelectedStagedBlock = null;
        DeleteEntireSelectedCourse = false;
        MarkUserEditedAfterLoad();
        ClearSelectionCore();
        NotifyStagedBlocksChanged();
        Rerender();
        PushUndoSnapshot(snapshot);

        StatusMessage = removedRows > 0 || stagedToRemove.Count > 0 || fixedSlotsRemoved > 0
            ? $"{courseName} 전체 블럭을 삭제했습니다."
            : $"{courseName}에서 삭제할 블럭을 찾지 못했습니다.";
    }

    private Course? ResolveCourseForWholeCourseDelete(CellAssignment assignment)
    {
        if (assignment.IsSchoolFixed && TryResolveSchoolFixedSourceCourse(assignment, out var schoolFixedCourse, out _))
            return schoolFixedCourse;

        return ResolveCourseForCellAssignment(assignment);
    }

    private IReadOnlySet<string> BuildWholeCourseDeleteCourseIds(CellAssignment assignment, Course? targetCourse)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (targetCourse != null)
        {
            foreach (var course in SessionCourses.Where(course => IsSameCourseFamily(targetCourse, course)))
            {
                if (!string.IsNullOrWhiteSpace(course.Id))
                    ids.Add(course.Id);
            }
        }
        else
        {
            var targetName = NormalizeCourseName(assignment.CourseName);
            if (!string.IsNullOrWhiteSpace(targetName))
            {
                foreach (var course in SessionCourses.Where(course =>
                             string.Equals(NormalizeCourseName(course.Name), targetName, StringComparison.Ordinal)))
                {
                    if (!string.IsNullOrWhiteSpace(course.Id))
                        ids.Add(course.Id);
                }
            }
        }

        if (ids.Count == 0 && !string.IsNullOrWhiteSpace(assignment.CourseId))
            ids.Add(assignment.CourseId);
        return ids;
    }

    private bool RowBelongsToWholeCourseDeleteTarget(
        SolutionAssignment row,
        IReadOnlySet<string> targetCourseIds,
        Course? targetCourse,
        CellAssignment targetAssignment)
    {
        if (targetCourseIds.Contains(row.CourseId))
            return true;

        var rowCourse = ResolveCourseForAssignment(row);
        return rowCourse != null && CourseBelongsToWholeCourseDeleteTarget(rowCourse, targetCourse, targetAssignment);
    }

    private bool AssignmentBelongsToWholeCourseDeleteTarget(
        CellAssignment assignment,
        IReadOnlySet<string> targetCourseIds,
        Course? targetCourse,
        CellAssignment targetAssignment)
    {
        if (targetCourseIds.Contains(assignment.CourseId))
            return true;

        var assignmentCourse = ResolveCourseForCellAssignment(assignment);
        return assignmentCourse != null && CourseBelongsToWholeCourseDeleteTarget(assignmentCourse, targetCourse, targetAssignment);
    }

    private static bool CourseBelongsToWholeCourseDeleteTarget(
        Course course,
        Course? targetCourse,
        CellAssignment targetAssignment)
    {
        if (targetCourse != null)
            return IsSameCourseFamily(targetCourse, course);

        var targetName = NormalizeCourseName(targetAssignment.CourseName);
        return !string.IsNullOrWhiteSpace(targetName)
            && string.Equals(NormalizeCourseName(course.Name), targetName, StringComparison.Ordinal);
    }

    private string WholeCourseDeleteDisplayName(CellAssignment assignment, Course? targetCourse)
    {
        var name = targetCourse?.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = assignment.CourseName;
        return string.IsNullOrWhiteSpace(name) ? CourseDisplayName(assignment.CourseId) : name.Trim();
    }

    private int ClearSchoolFixedCourseSlotsIfNeeded(IReadOnlySet<string> courseIds)
    {
        var courses = SessionCourses.Where(c =>
            c.IsSchoolFixed
            && courseIds.Contains(c.Id)
            && c.FixedSlots.Count > 0).ToList();

        var removed = 0;
        foreach (var course in courses)
        {
            removed += course.FixedSlots.Count;
            course.FixedSlots.Clear();
        }

        if (removed > 0)
            NotifyManualBlockCatalogChanged();
        return removed;
    }

    private static bool CrossLinkReferencesAnyCourse(ManualCrossLink link, IReadOnlySet<string> courseIds) =>
        courseIds.Contains(link.CourseIdA)
        || courseIds.Contains(link.CourseIdB)
        || courseIds.Contains(link.SourceKey.CourseId)
        || courseIds.Contains(link.TargetKey.CourseId);

    private void ApplySelectedCourseMutation(Action<Course> mutate, string successMessage)
    {
        if (SelectedAssignment == null || SelectedDay is not int day || SelectedPeriod is not int period)
        {
            if (SelectedStagedBlock != null)
            {
                ApplySelectedStagedCourseMutation(mutate, successMessage);
                return;
            }

            SetRoomChangeFailure("수업을 선택하세요.");
            return;
        }

        var selectedAssignmentId = SelectedAssignment.AssignmentId;
        var snapshot = CaptureSnapshot();
        if (!TryPrepareSelectedBlockCourseEdit(out var course, out var reason))
        {
            RestoreManualBlockSessionOnly(snapshot);
            SetRoomChangeFailure(reason);
            return;
        }

        mutate(course);
        NotifyManualBlockCatalogChanged();
        MarkUserEditedAfterLoad();
        Rerender();
        RefreshSelectionAfterBlockEdit(course.Id, day, period, selectedAssignmentId);
        PushUndoSnapshot(snapshot);
        SetRoomChangeSuccess(successMessage);
    }

    private void ApplySelectedStagedCourseMutation(Action<Course> mutate, string successMessage)
    {
        var selected = SelectedStagedBlock;
        if (selected == null)
        {
            SetRoomChangeFailure("수업을 선택하세요.");
            return;
        }

        var snapshot = CaptureSnapshot();
        if (!TryPrepareSelectedStagedBlockCourseEdit(selected, out var course, out var rows, out var reason))
        {
            RestoreManualBlockSessionOnly(snapshot);
            SetRoomChangeFailure(reason);
            return;
        }

        mutate(course);
        NotifyManualBlockCatalogChanged();
        var updated = BuildStagedBlockItemForCourse(selected, course, rows);
        if (!ReplaceSelectedStagedBlock(selected, updated, out reason))
        {
            RestoreManualBlockSessionOnly(snapshot);
            SetRoomChangeFailure(reason);
            return;
        }

        RemoveUnusedManualBlockCourses();
        MarkUserEditedAfterLoad();
        PushUndoSnapshot(snapshot);
        SetRoomChangeSuccess(successMessage);
    }

    private void AddSelectedRoomToStagedBlock()
    {
        var selected = SelectedStagedBlock;
        if (selected == null)
        {
            SetRoomChangeFailure("수업을 선택하세요.");
            return;
        }

        var roomId = NormalizeRoomId(SelectedRoomToAddId);
        if (string.IsNullOrWhiteSpace(roomId))
        {
            SetRoomChangeFailure("추가할 강의실을 선택하세요.");
            return;
        }

        if (!KnownRoomExists(roomId))
        {
            SetRoomChangeFailure("존재하지 않는 강의실입니다.");
            return;
        }

        if (AssignedRoomsForSelectedAssignment.Contains(roomId))
        {
            SetRoomChangeFailure("이미 배정된 강의실입니다.");
            return;
        }

        var snapshot = CaptureSnapshot();
        var rows = CloneAssignments(selected.Assignments);
        var slots = rows
            .GroupBy(row => new { row.Day, row.Period })
            .OrderBy(group => group.Key.Day)
            .ThenBy(group => group.Key.Period)
            .Select(group => group.Key)
            .ToList();
        foreach (var slot in slots)
        {
            rows.Add(new SolutionAssignment(
                selected.CourseId,
                slot.Day,
                slot.Period,
                roomId,
                selected.AssignmentId));
        }

        var updated = BuildStagedBlockItemFromRows(selected, rows);
        if (!ReplaceSelectedStagedBlock(selected, updated, out var reason))
        {
            RestoreManualBlockSessionOnly(snapshot);
            SetRoomChangeFailure(reason);
            return;
        }

        MarkUserEditedAfterLoad();
        PushUndoSnapshot(snapshot);
        SetRoomChangeSuccess("강의실을 추가했습니다.");
    }

    private void RemoveRoomFromSelectedStagedBlock(string? roomId)
    {
        var selected = SelectedStagedBlock;
        if (selected == null)
        {
            SetRoomChangeFailure("수업을 선택하세요.");
            return;
        }

        var normalizedRoomId = NormalizeRoomId(roomId);
        if (string.IsNullOrWhiteSpace(normalizedRoomId))
        {
            SetRoomChangeFailure("삭제할 강의실을 선택하세요.");
            return;
        }

        if (AssignedRoomsForSelectedAssignment.Count <= 1)
        {
            SetRoomChangeFailure("강의실은 최소 1개 필요합니다.");
            return;
        }

        var rows = CloneAssignments(selected.Assignments)
            .Where(row => !string.Equals(NormalizeRoomId(row.RoomId), normalizedRoomId, StringComparison.Ordinal))
            .ToList();
        if (rows.Count == selected.Assignments.Count)
        {
            SetRoomChangeFailure("선택한 강의실 배정을 찾을 수 없습니다.");
            return;
        }

        var snapshot = CaptureSnapshot();
        var updated = BuildStagedBlockItemFromRows(selected, rows);
        if (!ReplaceSelectedStagedBlock(selected, updated, out var reason))
        {
            RestoreManualBlockSessionOnly(snapshot);
            SetRoomChangeFailure(reason);
            return;
        }

        MarkUserEditedAfterLoad();
        PushUndoSnapshot(snapshot);
        SetRoomChangeSuccess("강의실을 삭제했습니다.");
    }

    private bool TryPrepareSelectedStagedBlockCourseEdit(
        StagedBlockItem selected,
        out Course course,
        out IReadOnlyList<SolutionAssignment> rows,
        out string reason)
    {
        course = null!;
        rows = Array.Empty<SolutionAssignment>();
        reason = "";

        EnsureManualEditSessionData();
        var selectedCourse = ResolveCourseForCellAssignment(selected.Assignment);
        if (selectedCourse == null)
        {
            reason = "수업 정보를 확인할 수 없습니다.";
            return false;
        }

        var clonedRows = CloneAssignments(selected.Assignments);
        var sameCourseHasOtherRows = _working.Any(row =>
                string.Equals(row.CourseId, selectedCourse.Id, StringComparison.Ordinal))
            || StagedBlocks
                .Where(block => !string.Equals(block.Id, selected.Id, StringComparison.Ordinal))
                .SelectMany(block => block.Assignments)
                .Any(row => string.Equals(row.CourseId, selectedCourse.Id, StringComparison.Ordinal));

        if (selectedCourse.Id.StartsWith(ManualBlockCourseIdPrefix, StringComparison.Ordinal)
            && !sameCourseHasOtherRows)
        {
            course = selectedCourse;
            rows = clonedRows;
            return true;
        }

        var clonedCourse = CloneCourse(selectedCourse);
        clonedCourse.Id = NewManualBlockCourseId("edit");
        clonedCourse.Grade = selected.Grade;
        clonedCourse.HoursPerWeek = Math.Max(1, selected.RowSpan);
        clonedCourse.BlockStructure = new List<int> { Math.Max(1, selected.RowSpan) };
        clonedCourse.IsFixed = false;
        clonedCourse.FixedSlots.Clear();
        clonedCourse.IsSchoolFixed = false;
        clonedCourse.SchoolFixedTargetGrade = 0;
        _sessionData!.Courses.Add(clonedCourse);

        rows = clonedRows
            .Select(row => new SolutionAssignment(
                clonedCourse.Id,
                row.Day,
                row.Period,
                row.RoomId,
                row.AssignmentId))
            .ToList();
        course = clonedCourse;
        return true;
    }

    private bool TryPrepareSelectedBlockCourseEdit(out Course course, out string reason)
    {
        course = null!;
        reason = "";
        if (SelectedAssignment == null || SelectedDay is not int day || SelectedPeriod is not int period)
        {
            reason = "수업을 선택하세요.";
            return false;
        }

        if (!TryGetMovingAssignments(SelectedAssignment, day, period, out var rows, out reason))
            return false;

        EnsureManualEditSessionData();
        var selectedCourse = ResolveCourseForCellAssignment(SelectedAssignment);
        if (selectedCourse == null)
        {
            reason = "수업 정보를 확인할 수 없습니다.";
            return false;
        }

        var rowSet = rows.ToHashSet();
        var sameCourseHasOtherRows = _working.Any(row =>
            string.Equals(row.CourseId, selectedCourse.Id, StringComparison.Ordinal)
            && !rowSet.Contains(row));
        if (selectedCourse.Id.StartsWith(ManualBlockCourseIdPrefix, StringComparison.Ordinal)
            && !sameCourseHasOtherRows)
        {
            course = selectedCourse;
            return true;
        }

        var clonedCourse = CloneCourse(selectedCourse);
        clonedCourse.Id = NewManualBlockCourseId("edit");
        clonedCourse.Grade = SelectedAssignment.Grade;
        clonedCourse.HoursPerWeek = Math.Max(1, SelectedAssignment.RowSpan);
        clonedCourse.BlockStructure = new List<int> { Math.Max(1, SelectedAssignment.RowSpan) };
        clonedCourse.IsFixed = false;
        clonedCourse.FixedSlots.Clear();
        clonedCourse.IsSchoolFixed = false;
        clonedCourse.SchoolFixedTargetGrade = 0;
        _sessionData!.Courses.Add(clonedCourse);

        for (var i = 0; i < _working.Count; i++)
        {
            var row = _working[i];
            if (!rowSet.Contains(row))
                continue;

            _working[i] = new SolutionAssignment(
                clonedCourse.Id,
                row.Day,
                row.Period,
                row.RoomId,
                row.AssignmentId);
        }

        course = clonedCourse;
        return true;
    }

    private void RefreshSelectionAfterBlockEdit(
        string courseId,
        int day,
        int selectedPeriod,
        string assignmentId)
    {
        var cell = Grid.Cells.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(assignmentId)
                && string.Equals(c.Assignment.AssignmentId, assignmentId, StringComparison.Ordinal)
                && c.Day == day
                && selectedPeriod >= c.Period
                && selectedPeriod < c.Period + c.Assignment.RowSpan)
            ?? Grid.Cells.FirstOrDefault(c =>
                c.Day == day
                && c.Assignment.CourseId == courseId
                && selectedPeriod >= c.Period
                && selectedPeriod < c.Period + c.Assignment.RowSpan);
        if (cell == null)
        {
            ClearSelectionCore();
            return;
        }

        SelectedDay = cell.Day;
        SelectedPeriod = cell.Period;
        SelectedAssignment = cell.Assignment;
        _selectedGridCell = new UnifiedCellKey(cell.Day, cell.Period, cell.Grade, cell.SubColumnIdx);
        SelectedPrimaryProfessorId = SelectedCourse?.ProfessorId ?? "";
        SelectedCoteachProfessorId = AddableCoteachProfessorOptions.FirstOrDefault()?.ProfessorId ?? "";
        SelectedRoomToAddId = AddableRoomOptions.FirstOrDefault()?.RoomId ?? "";
        OnPropertyChanged(nameof(SelectedSlotDisplayText));
        NotifySelectionDetailChanged();
    }

    private bool TryDeleteSelectedSchoolFixedBlock(int day, int period, out string reason)
    {
        reason = "";
        if (SelectedAssignment == null)
        {
            reason = "삭제할 블럭을 선택하세요.";
            return false;
        }

        EnsureManualEditSessionData();
        if (!TryResolveSchoolFixedSourceCourse(SelectedAssignment, out var course, out _))
        {
            reason = "학교/학년 고정 블럭의 원본 과목 정보를 찾을 수 없습니다.";
            return false;
        }

        var periods = GetOccupiedPeriods(period, SelectedAssignment.RowSpan).ToHashSet();
        var removed = course.FixedSlots.RemoveAll(slot =>
            slot.Day == day && periods.Contains(slot.Period));
        if (removed == 0)
        {
            reason = "선택한 고정 블럭의 슬롯을 찾을 수 없습니다.";
            return false;
        }

        NotifyManualBlockCatalogChanged();
        return true;
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

    public event EventHandler<SavedTimetableRecord>? SavedRequested;

    [RelayCommand(CanExecute = nameof(CanSaveTimetable))]
    private void SaveTimetable()
    {
        SaveTimetableCore(saveAsCopy: false);
    }

    [RelayCommand(CanExecute = nameof(CanSaveTimetable))]
    private void SaveCopy()
    {
        SaveTimetableCore(saveAsCopy: true);
    }

    public bool SaveCurrentForExport() => SaveTimetableCore(saveAsCopy: false, notifySaved: false);

    private bool SaveTimetableCore(bool saveAsCopy, bool notifySaved = true)
    {
        if (!ValidateBeforeSave()) return false;

        var originalSnapshot = CaptureSnapshot();
        var originalEditingSavedTimetableId = EditingSavedTimetableId;
        var originalBaselineSnapshot = _resetBaselineSnapshot;
        var name = saveAsCopy
            ? NextCopySaveName(SaveName.Trim())
            : SaveName.Trim();
        AppData? saveSnapshot;
        try
        {
            saveSnapshot = BuildSaveSnapshot();
            var record = _workspace.SaveTimetable(
                name,
                _working,
                ToSavedManualCrossLinks(),
                saveSnapshot,
                saveAsCopy ? null : EditingSavedTimetableId,
                _lunchPeriodsByDay);
            EditingSavedTimetableId = record.Id;
            SaveName = record.Name;
            _resetBaselineSnapshot = CaptureSnapshot();
            BaseSolution = new RankedSolution(
                _working.ToList(),
                BaseSolution?.Score ?? new SolutionScore(0, 0, 0, 0),
                _lunchPeriodsByDay);
            OnPropertyChanged(nameof(HasUnsavedChanges));
            StatusMessage = saveAsCopy
                ? $"'{record.Name}' 복사본 저장 완료"
                : $"'{record.Name}' 저장 완료";
            if (notifySaved)
                SavedRequested?.Invoke(this, record);
            return true;
        }
        catch (Exception ex)
        {
            RestoreSnapshot(originalSnapshot);
            EditingSavedTimetableId = originalEditingSavedTimetableId;
            _resetBaselineSnapshot = originalBaselineSnapshot;
            StatusMessage = ex.Message;
            return false;
        }
    }

    private string NextCopySaveName(string requestedName)
    {
        var baseName = requestedName.Trim();
        var usedNames = _workspace.SavedTimetables
            .Select(t => t.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!usedNames.Contains(baseName))
            return baseName;

        var n = 1;
        while (usedNames.Contains($"{baseName} ({n})"))
            n++;

        return $"{baseName} ({n})";
    }

    private AppData? BuildSaveSnapshot()
    {
        if (_sessionData == null)
            return null;

        var rooms = _sessionData.Rooms.ToList();
        var knownRoomIds = rooms.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var normalizedAssignments = new List<SolutionAssignment>(_working.Count);
        foreach (var assignment in _working)
        {
            var normalizedRoomId = NormalizeRoomIdForSaveSnapshot(assignment.RoomId, rooms);
            normalizedAssignments.Add(string.Equals(assignment.RoomId, normalizedRoomId, StringComparison.Ordinal)
                ? assignment
                : new SolutionAssignment(
                    assignment.CourseId,
                    assignment.Day,
                    assignment.Period,
                    normalizedRoomId,
                    assignment.AssignmentId));
        }

        _working = normalizedAssignments;
        foreach (var roomId in _working
            .Select(a => a.RoomId)
            .Where(roomId => !string.IsNullOrWhiteSpace(roomId))
            .Distinct(StringComparer.Ordinal))
        {
            if (knownRoomIds.Contains(roomId)) continue;
            var workspaceRoom = _workspace.Rooms.FirstOrDefault(r => r.Id == roomId);
            if (workspaceRoom == null) continue;
            var workspaceRoomName = NormalizeRoomName(workspaceRoom.Name);
            if (!string.IsNullOrWhiteSpace(workspaceRoomName)
                && rooms.Any(r => string.Equals(NormalizeRoomName(r.Name), workspaceRoomName, StringComparison.Ordinal)))
                continue;
            rooms.Add(workspaceRoom);
            knownRoomIds.Add(roomId);
        }

        var courses = BuildSaveSnapshotCourses();
        return _sessionData with { Courses = courses, Rooms = rooms };
    }

    private static string NormalizeRoomIdForSaveSnapshot(string roomId, IReadOnlyList<Room> snapshotRooms)
    {
        var normalizedRoomId = NormalizeRoomId(roomId);
        if (string.IsNullOrWhiteSpace(normalizedRoomId)) return normalizedRoomId;
        if (snapshotRooms.Any(r => string.Equals(NormalizeRoomId(r.Id), normalizedRoomId, StringComparison.Ordinal)))
            return normalizedRoomId;

        var matches = snapshotRooms
            .Where(r => string.Equals(NormalizeRoomName(r.Name), normalizedRoomId, StringComparison.Ordinal))
            .ToList();
        if (matches.Count == 1)
            return matches[0].Id;
        if (matches.Count > 1)
            throw new InvalidOperationException($"강의실 '{roomId}' 이름이 저장 스냅샷에서 여러 개와 일치하여 저장할 수 없습니다.");

        return normalizedRoomId;
    }

    private List<Course> BuildSaveSnapshotCourses() =>
        _sessionData?.Courses.Select(CloneCourse).ToList() ?? new List<Course>();

    private static AppData? CloneAppData(AppData? src) =>
        src == null
            ? null
            : new AppData(
                src.Courses.Select(CloneCourse).ToList(),
                src.Professors.Select(CloneProfessor).ToList(),
                src.Rooms.Select(CloneRoom).ToList(),
                src.CrossGroups.Select(CloneCrossGroup).ToList(),
                src.RetakeScenarios.Select(CloneRetakeScenario).ToList())
            {
                SchedulePolicy = src.SchedulePolicy,
            };

    private static Course CloneCourse(Course src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        Grade = src.Grade,
        HoursPerWeek = src.HoursPerWeek,
        CourseType = src.CourseType,
        ProfessorId = src.ProfessorId,
        Section = src.Section,
        Department = src.Department,
        FixedRooms = new List<string>(src.FixedRooms),
        UnavailableRooms = new List<string>(src.UnavailableRooms),
        BlockStructure = new List<int>(src.BlockStructure),
        IsFixed = src.IsFixed,
        FixedSlots = src.FixedSlots.ToList(),
        IsSchoolFixed = src.IsSchoolFixed,
        SchoolFixedTargetGrade = src.SchoolFixedTargetGrade,
        CoteachProfs = new List<string>(src.CoteachProfs),
    };

    private static Professor CloneProfessor(Professor src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        UnavailableSlots = src.UnavailableSlots.ToList(),
        UnavailableRooms = new List<string>(),
    };

    private static Room CloneRoom(Room src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        IsLab = src.IsLab,
        Capacity = src.Capacity,
        IsImportedFromExcel = src.IsImportedFromExcel,
    };

    private static CrossGroup CloneCrossGroup(CrossGroup src) => new()
    {
        Id = src.Id,
        BaseIds = src.BaseIds.ToList(),
    };

    private static RetakeScenario CloneRetakeScenario(RetakeScenario src) => new()
    {
        CurrentGrade = src.CurrentGrade,
        RetakeBaseId = src.RetakeBaseId,
    };

    private bool CanSaveTimetable() => !string.IsNullOrWhiteSpace(SaveName) && BaseSolution != null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportXlsx(string path)
    {
        var rows = _working.Select(a => new TimetableAssignmentRow(a.CourseId, a.Day, a.Period, a.RoomId, a.AssignmentId)).ToList();
        var name = string.IsNullOrWhiteSpace(SaveName) ? "시간표" : SaveName.Trim();
        FormattedTimetableExporter.Export(
            name,
            rows,
            SessionCourses,
            SessionProfessors,
            path,
            SessionRooms,
            schedulePolicy: SessionSchedulePolicy,
            lunchPeriodsByDay: _lunchPeriodsByDay);
    }

    private bool CanExport() => BaseSolution != null;

    public bool ValidateBeforeExport() => ValidateBeforeSave("내보내기 차단");

    private bool ValidateBeforeSave(string blockTitle = "저장 차단")
    {
        if (StagedBlocks.Count > 0)
        {
            var message = $"보관함에 빠져 있는 블럭 {StagedBlocks.Count}개를 시간표에 다시 넣어야 저장할 수 있습니다.";
            _dialog.ShowValidationResult(blockTitle, message, Array.Empty<ConflictItem>());
            StatusMessage = $"{blockTitle}: {message}";
            return false;
        }

        RefreshConflicts(strictManualCrossValidation: true);
        var blockingConflicts = GetManualEditVisibleConflicts(strictManualCrossValidation: true)
            .Where(conflict => conflict.Severity == ConflictSeverity.Error)
            .Select(BuildConflictDisplayItem)
            .ToList();
        var errorCount = blockingConflicts.Count;
        if (errorCount == 0) return true;

        var saveConflicts = blockingConflicts
            .Select(c => c.Conflict with { Description = BuildUserConflictDescription(c.Conflict) })
            .ToList();

        if (_dialog.ConfirmSaveDespiteConflicts(saveConflicts))
        {
            StatusMessage = $"저장 경고 확인: 제약조건 위반 Error {errorCount}건을 포함해 저장합니다.";
            return true;
        }

        var errorLines = blockingConflicts
            .SelectMany(c => c.Lines.Select(line => line.Text))
            .Where(text => !string.IsNullOrWhiteSpace(text));
        StatusMessage = string.Join(
            Environment.NewLine,
            new[] { $"{blockTitle}: 제약조건 위반 Error {errorCount}건이 있어 저장을 취소했습니다." }.Concat(errorLines));
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
                out var reasons,
                out var warnings,
                useGeneralMovePolicy: true))
        {
            StatusMessage = reasons.FirstOrDefault() ?? "이동할 수 없는 위치입니다.";
            return false;
        }

        var snapshot = CaptureSnapshot();
        var movedCourseName = CourseSectionDisplayName(SelectedAssignment);
        _working = candidate;
        MarkUserEditedAfterLoad();
        var removedLinks = RemoveInvalidCrossesAfterMove(refreshLabels: false);
        Rerender();

        PushUndoSnapshot(snapshot);
        var warning = GetSoftWarning(targetDay, targetPeriod);
        StatusMessage = removedLinks > 0
            ? $"{movedCourseName} 이동 완료 — {DayName(targetDay)} {targetPeriod}교시. 기존 크로스가 해제되었습니다."
            : warning == null
            ? $"{movedCourseName} 이동 완료 — {DayName(targetDay)} {targetPeriod}교시"
            : $"{movedCourseName} 이동 완료 — {DayName(targetDay)} {targetPeriod}교시. {warning}";
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
        var selectedCourseName = CourseSectionDisplayName(SelectedAssignment);
        var targetCourseName = CourseSectionDisplayName(target);
        if (!TryBuildSwappedCandidate(SelectedAssignment, selectedDay, selectedPeriod, target, targetDay, targetPeriod, out var candidate, out var candidateReason))
        {
            reason = candidateReason;
            return false;
        }
        _working = candidate;
        MarkUserEditedAfterLoad();
        Rerender();
        PushUndoSnapshot(snapshot);
        var removedLinks = RemoveInvalidCrossesAfterMove();
        StatusMessage = $"{selectedCourseName} ↔ {targetCourseName} 위치를 교환했습니다."
            + (removedLinks > 0 ? " 기존 크로스가 해제되었습니다." : "");
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

    private IReadOnlyList<ConflictItem> BuildSwapSoftWarnings(
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
                $"{CourseSectionDisplayName(assignment)} 교환 후 주의 시간대: {warning}",
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
        if (IsSameSwapAssignment(selected, target)
            || (selected.CourseId == target.CourseId
                && selectedDay == targetDay
                && selectedPeriod == targetPeriod
                && selected.Rooms.SequenceEqual(target.Rooms)))
            return "현재 선택한 수업입니다.";
        var selectedCourse = ResolveCourseForCellAssignment(selected);
        var targetCourse = ResolveCourseForCellAssignment(target);
        if (selectedCourse == null || targetCourse == null)
            return "수업 정보를 확인할 수 없습니다.";
        var selectedAtTarget = GetOccupiedPeriods(targetPeriod, selected.RowSpan);
        var targetAtSelected = GetOccupiedPeriods(selectedPeriod, target.RowSpan);
        if (selectedAtTarget.Concat(targetAtSelected).Any(p => !Constants.Periods.Contains(p)))
            return "수업 블록이 시간표 범위를 벗어납니다.";
        if (!TryBuildSwappedCandidate(selected, selectedDay, selectedPeriod, target, targetDay, targetPeriod, out var candidate, out var candidateReason))
            return candidateReason;
        return null;
    }

    private bool SwapOverlapsThirdBlock(
        CellAssignment selected,
        int selectedDay,
        int selectedPeriod,
        CellAssignment target,
        int targetDay,
        int targetPeriod)
    {
        return HasThirdBlock(selected, targetDay, targetPeriod)
            || HasThirdBlock(target, selectedDay, selectedPeriod);

        bool HasThirdBlock(CellAssignment moving, int day, int period)
        {
            var movingPeriods = GetOccupiedPeriods(period, moving.RowSpan);
            return Grid.Cells.Any(cell =>
                cell.Day == day
                && cell.Grade == moving.Grade
                && movingPeriods.Any(candidatePeriod =>
                    candidatePeriod >= cell.Period
                    && candidatePeriod < cell.Period + cell.Assignment.RowSpan)
                && !IsSameMovingCell(cell, selected, selectedDay, selectedPeriod)
                && !IsSameMovingCell(cell, target, targetDay, targetPeriod));
        }
    }

    private static bool IsSameSwapAssignment(CellAssignment selected, CellAssignment target) =>
        !string.IsNullOrWhiteSpace(selected.AssignmentId)
        && string.Equals(selected.AssignmentId, target.AssignmentId, StringComparison.Ordinal);

    private bool TryBuildSwappedCandidate(
        CellAssignment selected,
        int selectedDay,
        int selectedPeriod,
        CellAssignment target,
        int targetDay,
        int targetPeriod,
        out List<SolutionAssignment> candidate,
        out string reason)
    {
        candidate = _working.ToList();
        reason = "";
        if (IsSameSwapAssignment(selected, target))
        {
            reason = "현재 선택한 수업입니다.";
            return false;
        }
        if (!TryGetMovingAssignments(selected, selectedDay, selectedPeriod, out var selectedAssignments, out reason))
            return false;
        if (!TryGetMovingAssignments(target, targetDay, targetPeriod, out var targetAssignments, out reason))
            return false;

        var selectedSet = selectedAssignments.ToHashSet();
        var targetSet = targetAssignments.ToHashSet();
        candidate = _working
            .Where(a => !selectedSet.Contains(a) && !targetSet.Contains(a))
            .ToList();

        AddMovedAssignmentsPreservingVisualBlocks(
            candidate,
            selectedAssignments,
            selectedPeriod,
            targetDay,
            targetPeriod);
        AddMovedAssignmentsPreservingVisualBlocks(
            candidate,
            targetAssignments,
            targetPeriod,
            selectedDay,
            selectedPeriod);

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

                        var validation = ValidateGeneralManualMove(
                            assignment,
                            sourceDay,
                            sourcePeriod,
                            dayGroup.Day,
                            p,
                            grade,
                            subColumnIdx);
                        if (validation.BlockingReasons.Count > 0)
                        {
                            states[key] = new EditCellState(
                                ManualMoveCellState.Blocked,
                                validation.BlockingReasons[0]);
                            continue;
                        }

                        var warning = validation.Warnings.FirstOrDefault();
                        var state = warning == null
                            ? new EditCellState(ManualMoveCellState.Movable, "이동 가능")
                            : new EditCellState(ManualMoveCellState.Warning, ToUserReason(warning));
                        states[key] = state;
                    }
                }
            }
        }
        return states;
    }

    private IReadOnlyDictionary<UnifiedCellKey, EditCellState> BuildStagedPlacementStates(StagedBlockItem item)
    {
        var states = new Dictionary<UnifiedCellKey, EditCellState>();
        var assignment = item.Assignment;
        foreach (var dayGroup in Grid.DayGroups)
        {
            foreach (var gradeColumn in dayGroup.Grades)
            {
                if (gradeColumn.Grade is not int grade) continue;
                for (int subColumnIdx = 0; subColumnIdx < gradeColumn.Width; subColumnIdx++)
                {
                    foreach (var p in Constants.Periods)
                    {
                        if (assignment.Grade > 0 && grade != assignment.Grade)
                            continue;
                        var validation = ValidateGeneralStagedPlacement(
                            item,
                            dayGroup.Day,
                            p,
                            grade,
                            subColumnIdx);
                        if (validation.BlockingReasons.Count > 0)
                        {
                            states[new UnifiedCellKey(dayGroup.Day, p, grade, subColumnIdx)] =
                                new EditCellState(ManualMoveCellState.Blocked, validation.BlockingReasons[0]);
                            continue;
                        }

                        var warning = validation.Warnings.FirstOrDefault();
                        var state = warning == null
                            ? new EditCellState(ManualMoveCellState.Movable, "넣기 가능")
                            : new EditCellState(ManualMoveCellState.Warning, ToUserReason(warning));
                        states[new UnifiedCellKey(dayGroup.Day, p, grade, subColumnIdx)] = state;
                    }
                }
            }
        }

        return states;
    }

    private bool TryPlaceSelectedStagedBlock(int targetDay, int targetPeriod, int targetGrade, int targetSubColumnIdx)
    {
        var item = SelectedStagedBlock;
        if (item == null)
            return false;

        var validation = ValidateGeneralStagedPlacement(item, targetDay, targetPeriod, targetGrade, targetSubColumnIdx);
        if (validation.BlockingReasons.Count > 0)
        {
            StatusMessage = validation.BlockingReasons[0];
            return false;
        }

        if (!TryBuildPlacedCandidate(item, targetDay, targetPeriod, out var candidate, out var reason))
        {
            StatusMessage = reason;
            return false;
        }

        var snapshot = CaptureSnapshot();
        ApplyTargetGradeToStagedBlockCourse(item, targetGrade);
        _working = candidate;
        StagedBlocks.Remove(item);
        NotifyStagedBlocksChanged();
        SelectedStagedBlock = null;
        MarkUserEditedAfterLoad();
        Rerender();
        PushUndoSnapshot(snapshot);

        StatusMessage = $"{item.Title} 블럭을 {DayName(targetDay)} {FormatPeriodRange(targetPeriod, targetPeriod + item.RowSpan - 1)}에 넣었습니다.";
        return true;
    }

    public bool CanPlaceSelectedStagedBlock(int targetDay, int targetPeriod, int targetGrade, int targetSubColumnIdx) =>
        SelectedStagedBlock != null
        && ValidateGeneralStagedPlacement(
            SelectedStagedBlock,
            targetDay,
            targetPeriod,
            targetGrade,
            targetSubColumnIdx).BlockingReasons.Count == 0;

    public bool HandleStagedBlockDrop(int targetDay, int targetPeriod, int targetGrade, int targetSubColumnIdx) =>
        TryPlaceSelectedStagedBlock(targetDay, targetPeriod, targetGrade, targetSubColumnIdx);

    private void ApplyTargetGradeToStagedBlockCourse(StagedBlockItem item, int targetGrade)
    {
        if (item.Assignment.Grade > 0)
            return;

        EnsureManualEditSessionData();
        var course = _sessionData!.Courses.FirstOrDefault(c =>
            string.Equals(c.Id, item.CourseId, StringComparison.Ordinal));
        if (course == null)
            return;

        course.Grade = NormalizeManualBlockGrade(targetGrade);
    }

    private ManualMoveValidationResult ValidateGeneralStagedPlacement(
        StagedBlockItem item,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx)
    {
        var assignment = item.Assignment;
        ManualMoveValidationResult Block(string reason) =>
            new(new[] { reason }, Array.Empty<ConflictItem>());

        if (assignment.Grade > 0 && targetGrade != assignment.Grade)
            return Block("보관한 블럭은 같은 학년 칸에만 넣을 수 있습니다.");

        var course = ResolveCourseForCellAssignment(assignment);
        if (course == null)
            return Block("수업 정보를 확인할 수 없습니다.");

        var targetPeriods = GetOccupiedPeriods(targetPeriod, assignment.RowSpan);
        if (targetPeriods.Any(p => !Constants.Periods.Contains(p)))
            return Block("수업 블럭이 시간표 범위를 벗어납니다.");
        if (IsCrossDisplayOnlyColumn(targetDay, targetPeriod, targetGrade, targetSubColumnIdx))
            return Block(CrossDisplayColumnBlockedReason);

        var softWarnings = targetPeriods
            .Select(period => GetSoftWarning(targetDay, period))
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .Select(warning => new ConflictItem(
                ConflictType.GradeConflict,
                ConflictSeverity.Warning,
                warning!,
                targetDay,
                targetPeriod))
            .ToList();

        if (WouldCreatePatternAroundTarget(assignment, targetDay, targetPeriod, targetGrade))
            return Block(OneHourAboveTwoHourBlockedReason);

        var blockingAssignments = GetBlockingAssignments(
            assignment,
            item.SourceDay,
            item.SourcePeriod,
            targetDay,
            targetPeriods,
            targetGrade,
            targetSubColumnIdx,
            allowManualCrossOverlap: false);
        if (blockingAssignments.Count > 0)
        {
            var blockingCourses = blockingAssignments
                .Select(a => ResolveCourseForAssignment(a))
                .Where(c => c != null)
                .Cast<Course>()
                .ToList();
            if (blockingCourses.Any(c => c.IsFixed))
                return Block("고정 수업이 있는 시간으로는 넣을 수 없습니다.");

            return Block(BlockRangeOverlapBlockedReason);
        }

        if (!TryBuildPlacedCandidate(item, targetDay, targetPeriod, out var candidate, out var candidateReason))
            return Block(candidateReason);
        if (assignment.Grade > 0
            && WouldCreateOneHourAboveMultiHourPattern(candidate, new[] { assignment.CourseId }))
            return Block(OneHourAboveTwoHourBlockedReason);

        var baselineKeys = ConflictKeys(DetectConflicts(_working));
        var classifiedConflicts = DetectConflicts(candidate)
            .Where(c => !baselineKeys.Contains(KeyOf(c)))
            .Where(c => assignment.Grade > 0
                || c.Type != ConflictType.AcademicLevelTimeBandViolation
                || c.Assignments?.Any(row => !string.Equals(row.CourseId, item.CourseId, StringComparison.Ordinal)) == true)
            .ToList();

        var warnings = softWarnings
            .Concat(classifiedConflicts.Select(c => c with { Severity = ConflictSeverity.Warning }))
            .ToList();
        return new(Array.Empty<string>(), warnings);
    }

    private bool TryBuildPlacedCandidate(
        StagedBlockItem item,
        int targetDay,
        int targetPeriod,
        out List<SolutionAssignment> candidate,
        out string reason)
    {
        candidate = _working.ToList();
        reason = "";
        if (item.Assignments.Count == 0)
        {
            reason = "보관한 블럭의 배치 정보를 찾을 수 없습니다.";
            return false;
        }

        foreach (var row in item.Assignments)
        {
            candidate.Add(new SolutionAssignment(
                row.CourseId,
                targetDay,
                targetPeriod + (row.Period - item.SourcePeriod),
                row.RoomId,
                row.AssignmentId));
        }

        return true;
    }

    private ManualMoveValidationResult ValidateGeneralManualMove(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx) =>
        ValidateManualMove(
            assignment,
            sourceDay,
            sourcePeriod,
            targetDay,
            targetPeriod,
            targetGrade,
            targetSubColumnIdx,
            allowManualCrossOverlap: false,
            allowCrossDisplayColumn: false,
            allowFixedCourseMove: true,
            ignoreFixedTimeViolation: false,
            allowRelaxedStartPeriods: false,
            useGeneralMovePolicy: true);

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
        out IReadOnlyList<ConflictItem> warnings,
        bool allowManualCrossOverlap = false,
        bool allowCrossDisplayColumn = false,
        bool allowFixedCourseMove = true,
        bool ignoreFixedTimeViolation = true,
        bool allowRelaxedStartPeriods = true,
        bool useGeneralMovePolicy = false)
    {
        var validation = ValidateManualMove(
            assignment,
            sourceDay,
            sourcePeriod,
            targetDay,
            targetPeriod,
            targetGrade,
            targetSubColumnIdx,
            allowManualCrossOverlap,
            allowCrossDisplayColumn,
            allowFixedCourseMove,
            ignoreFixedTimeViolation,
            allowRelaxedStartPeriods,
            useGeneralMovePolicy);
        reasons = validation.BlockingReasons;
        warnings = validation.Warnings;
        if (reasons.Count > 0)
        {
            candidate = _working.ToList();
            return false;
        }

        if (!TryBuildMovedCandidate(assignment, sourceDay, sourcePeriod, targetDay, targetPeriod, out candidate, out var identityReason))
        {
            reasons = new[] { identityReason };
            return false;
        }
        return true;
    }

    private ManualMoveValidationResult ValidateManualMove(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        bool allowManualCrossOverlap = false,
        bool allowCrossDisplayColumn = false,
        bool allowFixedCourseMove = false,
        bool ignoreFixedTimeViolation = false,
        bool allowRelaxedStartPeriods = false,
        bool useGeneralMovePolicy = false)
    {
        ManualMoveValidationResult Block(string reason) =>
            new(new[] { reason }, Array.Empty<ConflictItem>());

        if (targetGrade != assignment.Grade)
            return Block("선택한 수업은 같은 학년 열 안에서만 이동할 수 있습니다.");

        if (_working.Count > 0 && !TryGetMovingAssignments(assignment, sourceDay, sourcePeriod, out _, out var identityReason))
            return Block(identityReason);

        var course = ResolveCourseForCellAssignment(assignment);
        if (course == null)
            return Block("선택한 수업 정보를 정확히 확인할 수 없습니다.");
        if (course.IsFixed && !allowFixedCourseMove)
            return Block("고정된 수업은 이동할 수 없습니다.");

        var targetPeriods = GetOccupiedPeriods(targetPeriod, assignment.RowSpan);
        if (targetPeriods.Any(p => !Constants.Periods.Contains(p)))
            return Block("수업 블록이 시간표 범위를 벗어납니다.");
        if (!allowCrossDisplayColumn && IsCrossDisplayOnlyColumn(targetDay, targetPeriod, targetGrade, targetSubColumnIdx))
            return Block(CrossDisplayColumnBlockedReason);
        var softWarnings = useGeneralMovePolicy
            ? targetPeriods
                .Select(period => GetSoftWarning(targetDay, period))
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.Ordinal)
                .Select(warning => new ConflictItem(
                    ConflictType.GradeConflict,
                    ConflictSeverity.Warning,
                    warning!,
                    targetDay,
                    targetPeriod))
                .ToList()
            : new List<ConflictItem>();
        if (WouldCreateOneHourAboveMultiHourPattern(
                assignment,
                sourceDay,
                sourcePeriod,
                targetDay,
                targetPeriod,
                targetGrade,
                targetSubColumnIdx))
            return Block(OneHourAboveTwoHourBlockedReason);
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
                return Block("고정 수업이 있는 시간으로는 이동할 수 없습니다.");

            return Block(BlockRangeOverlapBlockedReason);
        }

        if (!TryBuildMovedCandidate(assignment, sourceDay, sourcePeriod, targetDay, targetPeriod, out var candidate, out var candidateReason))
            return Block(candidateReason);

        var baselineKeys = ConflictKeys(DetectConflicts(_working));
        var detectedConflicts = DetectConflicts(candidate)
            .Where(c => !baselineKeys.Contains(KeyOf(c)))
            .ToList();
        var classifiedConflicts = detectedConflicts
            .Where(c =>
            {
                if (ignoreFixedTimeViolation && c.Type == ConflictType.FixedTimeViolation)
                    return false;
                if (allowRelaxedStartPeriods && c.Type == ConflictType.BlockStartViolation)
                    return false;
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

        if (!useGeneralMovePolicy)
            return new(classifiedConflicts.Select(ToUserReason).ToList(), Array.Empty<ConflictItem>());

        var warnings = softWarnings
            .Concat(classifiedConflicts.Select(c => c with { Severity = ConflictSeverity.Warning }))
            .ToList();
        return new(Array.Empty<string>(), warnings);
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
            if (IsAllowedManualCrossConflictGroup(conflict, involved))
                return true;
        }

        return false;
    }

    private bool IsAllowedManualCrossConflictGroup(
        ConflictItem conflict,
        IReadOnlyList<UnifiedCell> involved)
    {
        if (involved.Count < 2)
            return false;

        var keys = DistinctManualCrossKeysIgnoringSlot(involved
            .Select(cell => BuildManualCrossAssignmentKey(cell.Assignment, cell.Day, cell.Period)));
        if (keys.Count < 2)
            return false;

        if (involved.Select(cell => cell.Assignment.Grade).Distinct().Count() != 1)
            return false;

        if (conflict.Type == ConflictType.SectionConflict)
        {
            var conflictBaseId = GetSectionConflictBaseId(conflict);
            if (string.IsNullOrWhiteSpace(conflictBaseId))
                return false;
            if (involved
                .Select(cell => DomainHelpers.BaseId(cell.Assignment.CourseId))
                .Any(baseId => !string.Equals(baseId, conflictBaseId, StringComparison.Ordinal)))
                return false;
        }

        for (var i = 0; i < involved.Count; i++)
        {
            for (var j = i + 1; j < involved.Count; j++)
            {
                var leftKey = BuildManualCrossAssignmentKey(involved[i].Assignment, involved[i].Day, involved[i].Period);
                var rightKey = BuildManualCrossAssignmentKey(involved[j].Assignment, involved[j].Day, involved[j].Period);
                if (IsSameManualCrossAssignmentIgnoringSlot(leftKey, rightKey))
                    return false;
                if (HasProfessorOverlap(involved[i].Assignment, involved[j].Assignment))
                    return false;
                if (HasRoomOverlap(involved[i].Assignment, involved[j].Assignment))
                    return false;
            }
        }

        return AreManualCrossKeysConnected(keys);
    }

    private static List<ManualCrossAssignmentKey> DistinctManualCrossKeysIgnoringSlot(
        IEnumerable<ManualCrossAssignmentKey> keys)
    {
        var distinct = new List<ManualCrossAssignmentKey>();
        foreach (var key in keys)
        {
            if (distinct.Any(existing => IsSameManualCrossAssignmentIgnoringSlot(existing, key)))
                continue;
            distinct.Add(key);
        }
        return distinct;
    }

    private bool AreManualCrossKeysConnected(IReadOnlyList<ManualCrossAssignmentKey> keys)
    {
        if (keys.Count < 2)
            return false;

        var visited = new bool[keys.Count];
        var queue = new Queue<int>();
        visited[0] = true;
        queue.Enqueue(0);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var link in _workingCrossLinks)
            {
                var sourceIndex = FindManualCrossKeyIndex(keys, link.SourceKey);
                var targetIndex = FindManualCrossKeyIndex(keys, link.TargetKey);
                if (sourceIndex < 0 || targetIndex < 0)
                    continue;
                if (sourceIndex == current && !visited[targetIndex])
                {
                    visited[targetIndex] = true;
                    queue.Enqueue(targetIndex);
                }
                if (targetIndex == current && !visited[sourceIndex])
                {
                    visited[sourceIndex] = true;
                    queue.Enqueue(sourceIndex);
                }
            }
        }

        return visited.All(value => value);
    }

    private static int FindManualCrossKeyIndex(
        IReadOnlyList<ManualCrossAssignmentKey> keys,
        ManualCrossAssignmentKey target)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (IsSameManualCrossAssignmentIgnoringSlot(keys[i], target))
                return i;
        }

        return -1;
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
        ManualCrossAssignmentKey right)
    {
        if (!string.IsNullOrWhiteSpace(left.AssignmentId) && !string.IsNullOrWhiteSpace(right.AssignmentId))
            return string.Equals(left.AssignmentId, right.AssignmentId, StringComparison.Ordinal);

        return left.CourseId == right.CourseId
        && left.Section == right.Section
        && left.RowSpan == right.RowSpan
        && left.RoomIdsKey == right.RoomIdsKey;
    }

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

    private static string? GetSoftWarning(int day, int period) => null;

    private bool WouldCreateOneHourAboveMultiHourPattern(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx)
    {
        if (_working.Count == 0)
            return false;

        if (WouldCreatePatternAroundTarget(assignment, targetDay, targetPeriod, targetGrade))
            return true;

        var baselinePatterns = GetOneHourAboveMultiHourPatterns(RenderManualCells(_working));
        if (!TryBuildMovedCandidate(assignment, sourceDay, sourcePeriod, targetDay, targetPeriod, out var candidate, out _))
            return true;
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

    private bool TryBuildMovedCandidate(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        int targetPeriod,
        out List<SolutionAssignment> candidate,
        out string reason)
    {
        candidate = _working.ToList();
        reason = "";
        if (_working.Count == 0)
            return true;

        if (!TryGetMovingAssignments(assignment, sourceDay, sourcePeriod, out var movingAssignments, out reason))
            return false;

        var movingSet = movingAssignments.ToHashSet();
        candidate = _working
            .Where(a => !movingSet.Contains(a))
            .ToList();

        AddMovedAssignmentsPreservingVisualBlocks(
            candidate,
            movingAssignments,
            sourcePeriod,
            targetDay,
            targetPeriod);
        return true;
    }

    private static void AddMovedAssignmentsPreservingVisualBlocks(
        List<SolutionAssignment> candidate,
        IReadOnlyList<SolutionAssignment> movingAssignments,
        int sourcePeriod,
        int targetDay,
        int targetPeriod)
    {
        var movedRows = movingAssignments
            .Select(a => new SolutionAssignment(
                a.CourseId,
                targetDay,
                targetPeriod + (a.Period - sourcePeriod),
                a.RoomId,
                a.AssignmentId))
            .ToList();

        if (NeedsManualVisualOccurrenceId(candidate, movedRows)
            && movedRows.All(row => !CellAssignment.IsManualVisualOccurrenceAssignmentId(row.AssignmentId)))
        {
            var visualId = NewManualVisualOccurrenceAssignmentId();
            for (var i = 0; i < movedRows.Count; i++)
            {
                var row = movedRows[i];
                movedRows[i] = row with { AssignmentId = visualId };
            }
        }

        candidate.AddRange(movedRows);
    }

    private static bool NeedsManualVisualOccurrenceId(
        IReadOnlyList<SolutionAssignment> existingRows,
        IReadOnlyList<SolutionAssignment> movedRows) =>
        movedRows.Any(moved => existingRows.Any(existing =>
            existing.CourseId == moved.CourseId
            && existing.Day == moved.Day
            && existing.Period == moved.Period));

    private static string NewManualVisualOccurrenceAssignmentId() =>
        $"{CellAssignment.ManualVisualOccurrenceAssignmentIdPrefix}{Guid.NewGuid():N}";

    private bool TryGetMovingAssignments(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        out List<SolutionAssignment> movingAssignments,
        out string reason)
    {
        reason = "";
        movingAssignments = _working
            .Where(a => IsSameMovingAssignment(a, assignment, sourceDay, sourcePeriod))
            .OrderBy(a => a.Period)
            .ThenBy(a => a.RoomId, StringComparer.Ordinal)
            .ToList();

        var sourcePeriods = GetOccupiedPeriods(sourcePeriod, assignment.RowSpan).ToHashSet();
        var assignmentRooms = assignment.Rooms
            .Where(room => !string.IsNullOrWhiteSpace(room))
            .Select(room => room.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var expectedRows = Math.Max(1, assignment.RowSpan) * Math.Max(1, assignmentRooms.Count);

        if (movingAssignments.Count == 0
            || !IsValidMovingAssignmentBlock(movingAssignments, assignment, sourceDay, sourcePeriods, assignmentRooms)
            || movingAssignments.Count != expectedRows)
        {
            var visualBlockAssignments = FindMovingAssignmentsByVisualBlock(
                assignment,
                sourceDay,
                sourcePeriods,
                assignmentRooms);
            if (visualBlockAssignments.Count > 0)
                movingAssignments = visualBlockAssignments;
        }

        if (movingAssignments.Count == 0)
        {
            reason = "선택한 수업 assignment를 작업본에서 찾지 못했습니다.";
            return false;
        }

        var validRows = IsValidMovingAssignmentBlock(
            movingAssignments,
            assignment,
            sourceDay,
            sourcePeriods,
            assignmentRooms);
        if (!validRows)
        {
            reason = "선택한 수업 identity가 둘 이상의 위치를 가리킵니다.";
            return false;
        }

        if (movingAssignments.Count != expectedRows)
        {
            reason = "선택한 수업 블록의 assignment 수가 화면 블록 정보와 일치하지 않습니다.";
            return false;
        }

        return true;
    }

    private List<SolutionAssignment> FindMovingAssignmentsByVisualBlock(
        CellAssignment assignment,
        int sourceDay,
        IReadOnlySet<int> sourcePeriods,
        IReadOnlySet<string> assignmentRooms)
    {
        return _working
            .Where(a =>
                a.CourseId == assignment.CourseId
                && a.Day == sourceDay
                && sourcePeriods.Contains(a.Period)
                && MovingRoomMatches(a.RoomId, assignmentRooms)
                && AssignmentIdMatchesVisualSelection(a, assignment))
            .OrderBy(a => a.Period)
            .ThenBy(a => a.RoomId, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsValidMovingAssignmentBlock(
        IReadOnlyList<SolutionAssignment> movingAssignments,
        CellAssignment assignment,
        int sourceDay,
        IReadOnlySet<int> sourcePeriods,
        IReadOnlySet<string> assignmentRooms) =>
        movingAssignments.All(a =>
            a.CourseId == assignment.CourseId
            && a.Day == sourceDay
            && sourcePeriods.Contains(a.Period)
            && MovingRoomMatches(a.RoomId, assignmentRooms)
            && AssignmentIdMatchesVisualSelection(a, assignment));

    private static bool IsSameMovingAssignment(
        SolutionAssignment existing,
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod)
    {
        if (!string.IsNullOrWhiteSpace(assignment.AssignmentId))
            return string.Equals(existing.AssignmentId, assignment.AssignmentId, StringComparison.Ordinal);

        if (existing.CourseId != assignment.CourseId || existing.Day != sourceDay) return false;
        if (existing.Period < sourcePeriod || existing.Period >= sourcePeriod + assignment.RowSpan) return false;
        var assignmentRooms = assignment.Rooms
            .Where(room => !string.IsNullOrWhiteSpace(room))
            .Select(room => room.Trim())
            .ToHashSet(StringComparer.Ordinal);
        return MovingRoomMatches(existing.RoomId, assignmentRooms)
            && AssignmentIdMatchesVisualSelection(existing, assignment);
    }

    private static bool MovingRoomMatches(string rowRoomId, IReadOnlySet<string> assignmentRooms) =>
        assignmentRooms.Count == 0
            ? string.IsNullOrWhiteSpace(rowRoomId)
            : assignmentRooms.Contains(rowRoomId);

    private static bool AssignmentIdMatchesVisualSelection(
        SolutionAssignment existing,
        CellAssignment assignment)
    {
        if (CellAssignment.IsManualVisualOccurrenceAssignmentId(assignment.AssignmentId))
            return string.Equals(existing.AssignmentId, assignment.AssignmentId, StringComparison.Ordinal);

        return !CellAssignment.IsManualVisualOccurrenceAssignmentId(existing.AssignmentId);
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
            var relevantNewOnes = newOnes
                .Select(c => c.Conflict)
                .Where(IsConflictRelevantForCurrentSelection)
                .ToList();
            if (relevantNewOnes.Count == 0)
            {
                MarkUserEditedAfterLoad();
                RefreshConflicts();
                PushUndoSnapshot(snapshot);
                StatusMessage = successMessage;
                return true;
            }

            bool proceed = _dialog.ConfirmDespiteConflicts(relevantNewOnes.Select(BuildUserConflictItem).ToList());
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

    private bool TryBuildManualStagedBlock(
        string? preferredId,
        out StagedBlockItem item,
        out string reason)
    {
        item = null!;
        reason = "";

        var rowSpan = NormalizeManualBlockRowSpan(NewBlockRowSpan);
        if (rowSpan != NewBlockRowSpan)
            NewBlockRowSpan = rowSpan;

        if (HasSimpleManualBlockInput())
        {
            if (!TryBuildSimpleManualCourseStagedBlocks(preferredId, out var items, out reason))
                return false;
            if (items.Count != 1)
            {
                reason = "여러 분반/블럭구조는 보관함 블럭 수정이 아니라 새로 추가할 때 사용하세요.";
                return false;
            }

            item = items[0];
            return true;
        }

        return NewBlockKind == ManualBlockKind.Course
            ? TryBuildManualCourseStagedBlock(preferredId, rowSpan, out item, out reason)
            : TryBuildManualSpecialStagedBlock(preferredId, rowSpan, out item, out reason);
    }

    private bool HasSimpleManualBlockInput() =>
        !string.IsNullOrWhiteSpace(NewBlockCourseName)
        || !string.IsNullOrWhiteSpace(NewBlockProfessorName)
        || !string.IsNullOrWhiteSpace(NewBlockRoomName);

    private void EnsureNewBlockStructureSelection()
    {
        if (!IsNewBlockStructureEnabled)
            return;

        var options = NewBlockStructureOptions;
        if (options.Count == 0)
        {
            NewBlockStructureText = "";
            return;
        }

        if (!options.Contains(NewBlockStructureText, StringComparer.Ordinal))
            NewBlockStructureText = options[0];
    }

    private static IReadOnlyList<string> BuildManualBlockStructureOptions(int totalHours)
    {
        var normalized = NormalizeManualBlockRowSpan(totalHours);
        var values = new List<IReadOnlyList<int>>();
        BuildManualBlockStructureOptionsCore(normalized, normalized, new List<int>(), values);
        return values
            .Select(parts => string.Join(",", parts))
            .ToList();
    }

    private static void BuildManualBlockStructureOptionsCore(
        int remaining,
        int maxNext,
        List<int> current,
        List<IReadOnlyList<int>> results)
    {
        if (remaining == 0)
        {
            results.Add(current.ToList());
            return;
        }

        for (var value = Math.Min(maxNext, remaining); value >= 1; value--)
        {
            current.Add(value);
            BuildManualBlockStructureOptionsCore(remaining - value, value, current, results);
            current.RemoveAt(current.Count - 1);
        }
    }

    private bool TryResolveManualBlockStructure(
        int totalHours,
        out IReadOnlyList<int> blockStructure,
        out string reason)
    {
        reason = "";
        if (!IsNewBlockStructureEnabled)
        {
            blockStructure = new[] { totalHours };
            return true;
        }

        EnsureNewBlockStructureSelection();
        var text = NormalizeManualInput(NewBlockStructureText);
        if (string.IsNullOrWhiteSpace(text))
        {
            blockStructure = Array.Empty<int>();
            reason = "블럭구조를 선택하세요.";
            return false;
        }

        var values = Regex.Matches(text, @"\d+")
            .Select(match => int.Parse(match.Value))
            .ToList();
        if (values.Count == 0)
        {
            blockStructure = Array.Empty<int>();
            reason = "블럭구조를 숫자로 입력하세요. 예: 2,1";
            return false;
        }

        if (values.Any(value => value < 1 || value > ManualBlockMaxRowSpan))
        {
            blockStructure = Array.Empty<int>();
            reason = $"블럭구조의 각 값은 1~{ManualBlockMaxRowSpan} 사이여야 합니다.";
            return false;
        }

        var sum = values.Sum();
        if (sum != totalHours)
        {
            blockStructure = Array.Empty<int>();
            reason = $"블럭구조 합계({sum})가 수업시수({totalHours})와 같아야 합니다.";
            return false;
        }

        blockStructure = values;
        return true;
    }

    private bool TryResolveManualBlockSectionCount(out int sectionCount, out string reason)
    {
        sectionCount = NewBlockSectionCount;
        reason = "";
        if (sectionCount < 1)
        {
            reason = "분반 갯수는 1개 이상이어야 합니다.";
            return false;
        }

        if (sectionCount > 9)
        {
            reason = "분반 갯수는 최대 9개까지 설정할 수 있습니다.";
            return false;
        }

        return true;
    }

    private bool TryBuildSimpleManualCourseStagedBlocks(
        string? preferredId,
        out List<StagedBlockItem> items,
        out string reason)
    {
        items = new List<StagedBlockItem>();
        reason = "";

        var totalHours = NormalizeManualBlockRowSpan(NewBlockRowSpan);
        if (totalHours != NewBlockRowSpan)
            NewBlockRowSpan = totalHours;
        if (!TryResolveManualBlockStructure(totalHours, out var blockStructure, out reason))
            return false;
        if (!TryResolveManualBlockSectionCount(out var sectionCount, out reason))
            return false;

        var courseName = NormalizeManualInput(NewBlockCourseName);
        var professorName = NormalizeManualInput(NewBlockProfessorName);
        var roomName = NormalizeManualInput(NewBlockRoomName);
        if (string.IsNullOrWhiteSpace(courseName))
        {
            reason = "교과목명을 입력하세요.";
            return false;
        }

        if (courseName.Any(char.IsWhiteSpace))
        {
            reason = "교과목명에는 띄어쓰기를 사용할 수 없습니다.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(professorName))
        {
            reason = "교수명을 입력하세요.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(roomName))
        {
            reason = "강의실을 입력하세요.";
            return false;
        }

        EnsureManualEditSessionData();
        var professorId = EnsureSessionProfessorByName(professorName);
        var roomId = EnsureSessionRoomByName(roomName);

        for (var section = 1; section <= sectionCount; section++)
        {
            var course = new Course
            {
                Id = NewManualBlockCourseId($"course{section}"),
                Name = courseName,
                Grade = NormalizeManualBlockGrade(NewBlockGrade),
                HoursPerWeek = totalHours,
                CourseType = "",
                ProfessorId = professorId,
                Section = section,
                Department = "",
                FixedRooms = new List<string>(),
                UnavailableRooms = new List<string>(),
                BlockStructure = blockStructure.ToList(),
                IsFixed = false,
                FixedSlots = new List<TimeSlot>(),
                IsSchoolFixed = false,
                SchoolFixedTargetGrade = 0,
                CoteachProfs = new List<string>(),
            };

            _sessionData!.Courses.Add(course);
            foreach (var blockLength in blockStructure)
            {
                var assignmentId = NewManualVisualOccurrenceAssignmentId();
                var rows = BuildManualBlockRows(course.Id, 0, Constants.Periods.First(), blockLength, roomId, assignmentId);
                var assignment = CellAssignment.FromCourse(
                    course,
                    new[] { roomId },
                    blockLength,
                    BuildProfessorNameMap(),
                    BuildRoomNameMap(),
                    assignmentId);
                var staged = BuildStagedBlockItem(assignment, 0, Constants.Periods.First(), rows);
                if (!string.IsNullOrWhiteSpace(preferredId) && items.Count == 0)
                    staged = staged with { Id = preferredId };
                items.Add(staged);
            }
        }

        NotifyManualBlockCatalogChanged();
        return true;
    }

    private void InitializeManualBlockEditorDefaults(bool clearStatus)
    {
        if (clearStatus)
            ManualBlockStatusMessage = "";
        NewBlockKind = ManualBlockKind.Course;
        NewBlockCourseId = ManualBlockCourseOptions.FirstOrDefault()?.Id ?? "";
        NewBlockCourseName = "";
        NewBlockProfessorName = "";
        NewBlockRoomName = "";
        NewBlockGrade = AcademicLevels.AllGrades[0];
        NewBlockRoomId = ManualBlockRoomOptions.FirstOrDefault()?.RoomId ?? "";
        NewBlockRowSpan = 1;
        IsNewBlockStructureEnabled = false;
        NewBlockStructureText = "";
        NewBlockSectionCount = 1;
    }

    private bool TryBuildManualCourseStagedBlock(
        string? preferredId,
        int rowSpan,
        out StagedBlockItem item,
        out string reason)
    {
        item = null!;
        reason = "";

        var course = ManualBlockCourseOptions.FirstOrDefault(c =>
            string.Equals(c.Id, NewBlockCourseId, StringComparison.Ordinal));
        if (course == null)
        {
            reason = "추가할 수업 과목을 선택하세요.";
            return false;
        }

        var roomId = NormalizeRoomId(NewBlockRoomId);
        if (string.IsNullOrWhiteSpace(roomId))
        {
            reason = "수업 블럭은 강의실을 먼저 선택해야 합니다.";
            return false;
        }

        var assignmentId = NewManualVisualOccurrenceAssignmentId();
        var rows = BuildManualBlockRows(course.Id, 0, Constants.Periods.First(), rowSpan, roomId, assignmentId);
        var assignment = CellAssignment.FromCourse(
            course,
            new[] { roomId },
            rowSpan,
            BuildProfessorNameMap(),
            BuildRoomNameMap(),
            assignmentId);
        item = BuildStagedBlockItem(assignment, 0, Constants.Periods.First(), rows);
        if (!string.IsNullOrWhiteSpace(preferredId))
            item = item with { Id = preferredId };
        return true;
    }

    private bool TryBuildManualSpecialStagedBlock(
        string? preferredId,
        int rowSpan,
        out StagedBlockItem item,
        out string reason)
    {
        item = null!;
        reason = "";

        EnsureManualEditSessionData();
        var grade = NormalizeManualBlockGrade(NewBlockGrade);
        var isSchoolFixed = NewBlockKind == ManualBlockKind.SchoolFixed;
        var targetGrade = isSchoolFixed ? SchoolFixedTimePolicy.AllGrades : grade;
        var label = isSchoolFixed
            ? "학교 고정"
            : $"{AcademicLevels.DisplayName(grade)} 고정";
        var course = new Course
        {
            Id = NewManualBlockCourseId(isSchoolFixed ? "school" : $"grade{grade}"),
            Name = label,
            Grade = grade,
            HoursPerWeek = rowSpan,
            CourseType = "",
            ProfessorId = "",
            Section = 0,
            Department = "",
            FixedRooms = new List<string>(),
            UnavailableRooms = new List<string>(),
            BlockStructure = new List<int> { rowSpan },
            IsFixed = true,
            FixedSlots = new List<TimeSlot>(),
            IsSchoolFixed = true,
            SchoolFixedTargetGrade = targetGrade,
            CoteachProfs = new List<string>(),
        };

        _sessionData!.Courses.Add(course);
        NotifyManualBlockCatalogChanged();

        var assignmentId = NewManualVisualOccurrenceAssignmentId();
        var rows = BuildManualBlockRows(course.Id, 0, Constants.Periods.First(), rowSpan, "", assignmentId);
        var assignment = CellAssignment.FromCourse(
            course,
            Array.Empty<string>(),
            rowSpan,
            BuildProfessorNameMap(),
            BuildRoomNameMap(),
            assignmentId);
        item = BuildStagedBlockItem(assignment, 0, Constants.Periods.First(), rows);
        if (!string.IsNullOrWhiteSpace(preferredId))
            item = item with { Id = preferredId };
        return true;
    }

    private bool TryBuildSchoolFixedDisplayStagedBlock(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        out StagedBlockItem item,
        out string reason)
    {
        item = null!;
        reason = "";

        EnsureManualEditSessionData();
        if (!TryResolveSchoolFixedSourceCourse(assignment, out var course, out var displayGrade))
        {
            reason = "학교/학년 고정 블럭의 원본 과목 정보를 찾을 수 없습니다.";
            return false;
        }

        var rowSpan = NormalizeManualBlockRowSpan(assignment.RowSpan);
        var sourcePeriods = GetOccupiedPeriods(sourcePeriod, rowSpan).ToHashSet();
        var removed = course.FixedSlots.RemoveAll(slot =>
            slot.Day == sourceDay && sourcePeriods.Contains(slot.Period));
        if (removed == 0)
        {
            reason = "선택한 학교/학년 고정 블럭의 고정 슬롯을 찾을 수 없습니다.";
            return false;
        }

        var displayCourse = CloneCourse(course);
        displayCourse.Grade = NormalizeManualBlockGrade(displayGrade);
        displayCourse.HoursPerWeek = rowSpan;
        displayCourse.BlockStructure = new List<int> { rowSpan };
        displayCourse.FixedSlots.Clear();

        var assignmentId = NewManualVisualOccurrenceAssignmentId();
        var rows = BuildManualBlockRows(course.Id, sourceDay, sourcePeriod, rowSpan, "", assignmentId);
        var stagedAssignment = CellAssignment.FromCourse(
            displayCourse,
            Array.Empty<string>(),
            rowSpan,
            BuildProfessorNameMap(),
            BuildRoomNameMap(),
            assignmentId);
        item = BuildStagedBlockItem(stagedAssignment, sourceDay, sourcePeriod, rows);
        NotifyManualBlockCatalogChanged();
        return true;
    }

    private void LoadManualBlockEditorFromStagedBlock(StagedBlockItem item)
    {
        ManualBlockStatusMessage = "";
        NewBlockRowSpan = item.RowSpan;
        NewBlockGrade = NormalizeManualBlockGrade(item.Grade);

        var firstRoomId = item.Assignments
            .Select(row => NormalizeRoomId(row.RoomId))
            .Concat(item.Assignment.Rooms.Select(NormalizeRoomId))
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? "";
        NewBlockRoomId = firstRoomId;

        if (item.Assignment.IsSchoolFixed)
        {
            NewBlockKind = item.Assignment.SchoolFixedTargetGrade == SchoolFixedTimePolicy.AllGrades
                ? ManualBlockKind.SchoolFixed
                : ManualBlockKind.GradeFixed;
            NewBlockCourseId = ManualBlockCourseOptions.FirstOrDefault()?.Id ?? "";
            return;
        }

        NewBlockKind = ManualBlockKind.Course;
        NewBlockCourseId = item.CourseId;
    }

    private void EnsureManualEditSessionData()
    {
        if (_sessionData != null)
            return;

        _sessionData = CloneAppData(_workspace.Snapshot()) ?? AppData.Empty();
        NotifyManualBlockCatalogChanged();
    }

    private static string NormalizeManualInput(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    private string EnsureSessionProfessorByName(string professorName)
    {
        EnsureManualEditSessionData();
        var normalized = NormalizeManualInput(professorName);
        var existing = _sessionData!.Professors
            .Concat(_workspace.Professors)
            .FirstOrDefault(professor =>
                string.Equals(NormalizeManualInput(professor.Name), normalized, StringComparison.Ordinal)
                || string.Equals(NormalizeManualInput(professor.Id), normalized, StringComparison.Ordinal));
        if (existing != null)
        {
            if (!_sessionData.Professors.Any(professor => string.Equals(professor.Id, existing.Id, StringComparison.Ordinal)))
                _sessionData.Professors.Add(CloneProfessor(existing));
            return existing.Id;
        }

        var id = NewManualBlockEntityId("prof");
        _sessionData.Professors.Add(new Professor { Id = id, Name = normalized });
        return id;
    }

    private string EnsureSessionRoomByName(string roomName)
    {
        EnsureManualEditSessionData();
        var normalized = NormalizeManualInput(roomName);
        var existing = BuildRoomCatalog()
            .FirstOrDefault(room =>
                string.Equals(NormalizeRoomName(room.Name), normalized, StringComparison.Ordinal)
                || string.Equals(NormalizeRoomId(room.Id), normalized, StringComparison.Ordinal));
        if (existing != null)
        {
            if (!_sessionData!.Rooms.Any(room => string.Equals(room.Id, existing.Id, StringComparison.Ordinal)))
                _sessionData.Rooms.Add(CloneRoom(existing));
            return existing.Id;
        }

        var id = NewManualBlockEntityId("room");
        _sessionData!.Rooms.Add(new Room { Id = id, Name = normalized });
        return id;
    }

    private void RemoveUnusedManualBlockCourses()
    {
        if (_sessionData == null)
            return;

        var usedCourseIds = _working
            .Select(row => row.CourseId)
            .Concat(StagedBlocks.SelectMany(block => block.Assignments.Select(row => row.CourseId)))
            .ToHashSet(StringComparer.Ordinal);
        var removed = _sessionData.Courses.RemoveAll(course =>
            course.Id.StartsWith(ManualBlockCourseIdPrefix, StringComparison.Ordinal)
            && !usedCourseIds.Contains(course.Id));
        if (removed > 0)
            NotifyManualBlockCatalogChanged();
    }

    private void RestoreManualBlockSessionOnly(ManualEditSnapshot snapshot)
    {
        _sessionData = CloneAppData(snapshot.SessionData);
        NotifyManualBlockCatalogChanged();
    }

    private void NotifyManualBlockCatalogChanged()
    {
        OnPropertyChanged(nameof(ManualBlockCourseOptions));
        OnPropertyChanged(nameof(ManualBlockRoomOptions));
        OnPropertyChanged(nameof(ProfessorOptions));
        OnPropertyChanged(nameof(AddableCoteachProfessorOptions));
        OnPropertyChanged(nameof(SelectedCoteachProfessorOptions));
        OnPropertyChanged(nameof(AddableRoomOptions));
    }

    private static List<SolutionAssignment> BuildManualBlockRows(
        string courseId,
        int sourceDay,
        int sourcePeriod,
        int rowSpan,
        string roomId,
        string assignmentId) =>
        Enumerable.Range(0, rowSpan)
            .Select(offset => new SolutionAssignment(
                courseId,
                sourceDay,
                sourcePeriod + offset,
                roomId,
                assignmentId))
            .ToList();

    private IReadOnlyDictionary<string, string> BuildProfessorNameMap() =>
        SessionProfessors
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

    private IReadOnlyDictionary<string, string> BuildRoomNameMap() =>
        BuildRoomCatalog()
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => NormalizeRoomId(r.Id), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

    private bool TryResolveSchoolFixedSourceCourse(
        CellAssignment assignment,
        out Course course,
        out int displayGrade)
    {
        displayGrade = NormalizeManualBlockGrade(assignment.Grade);
        var originalCourseId = TryParseSchoolFixedDisplayCourseId(assignment.CourseId, out var parsedCourseId, out var parsedGrade)
            ? parsedCourseId
            : assignment.CourseId;
        if (parsedGrade > 0)
            displayGrade = parsedGrade;
        if (TryParseSchoolFixedAssignmentId(assignment.AssignmentId, out var assignmentCourseId, out var assignmentGrade))
        {
            originalCourseId = assignmentCourseId;
            displayGrade = assignmentGrade;
        }

        course = SessionCourses.FirstOrDefault(c =>
            c.IsSchoolFixed
            && string.Equals(c.Id, originalCourseId, StringComparison.Ordinal))!;
        return course != null;
    }

    private static bool TryParseSchoolFixedDisplayCourseId(
        string courseId,
        out string originalCourseId,
        out int grade)
    {
        originalCourseId = courseId;
        grade = 0;
        var markerIndex = courseId.LastIndexOf(SchoolFixedDisplayCourseMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return false;

        originalCourseId = courseId[..markerIndex];
        var gradeText = courseId[(markerIndex + SchoolFixedDisplayCourseMarker.Length)..];
        return int.TryParse(gradeText, out grade);
    }

    private static bool TryParseSchoolFixedAssignmentId(
        string assignmentId,
        out string originalCourseId,
        out int grade)
    {
        originalCourseId = "";
        grade = 0;
        if (string.IsNullOrWhiteSpace(assignmentId))
            return false;

        var parts = assignmentId.Split('\u001f');
        if (parts.Length != 3 || !string.Equals(parts[0], "school-fixed", StringComparison.Ordinal))
            return false;

        originalCourseId = parts[1];
        return int.TryParse(parts[2], out grade);
    }

    private static int NormalizeManualBlockRowSpan(int rowSpan) =>
        Math.Min(ManualBlockMaxRowSpan, Math.Max(1, rowSpan));

    private static int NormalizeManualBlockGrade(int grade) =>
        AcademicLevels.AllGrades.Contains(grade) ? grade : AcademicLevels.AllGrades[0];

    private static string NewManualBlockCourseId(string suffix) =>
        $"{ManualBlockCourseIdPrefix}{suffix}-{Guid.NewGuid():N}";

    private static string NewManualBlockEntityId(string suffix) =>
        $"{ManualBlockCourseIdPrefix}{suffix}-{Guid.NewGuid():N}";

    private void SetManualBlockError(string message)
    {
        ManualBlockStatusMessage = message;
        StatusMessage = message;
    }

    private void SetManualBlockSuccess(string message)
    {
        ManualBlockStatusMessage = message;
        StatusMessage = message;
    }

    private void Rerender()
    {
        Grid.SetCrossParallelOrder(BuildCrossParallelOrder());
        Grid.Render(
            _working,
            SessionCourses,
            SessionProfessors,
            SessionRooms,
            SessionSchedulePolicy,
            _lunchPeriodsByDay);
        Grid.SetCrossLinkLabels(BuildCrossLinkLabels());
        RenderBreakdownViews();
        RefreshConflicts();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private StagedBlockItem BuildStagedBlockItemFromRows(
        StagedBlockItem source,
        IReadOnlyList<SolutionAssignment> rows)
    {
        var course = ResolveCourseForCellAssignment(source.Assignment);
        if (course != null)
            return BuildStagedBlockItemForCourse(source, course, rows);

        var roomIds = rows
            .Select(row => NormalizeRoomId(row.RoomId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var assignment = source.Assignment with
        {
            Rooms = roomIds,
            RoomDisplayNames = roomIds.Select(RoomDisplayName).ToList(),
        };
        return BuildStagedBlockItem(assignment, source.SourceDay, source.SourcePeriod, rows) with
        {
            Id = source.Id,
        };
    }

    private StagedBlockItem BuildStagedBlockItemForCourse(
        StagedBlockItem source,
        Course course,
        IReadOnlyList<SolutionAssignment> rows)
    {
        var roomIds = rows
            .Select(row => NormalizeRoomId(row.RoomId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var assignment = CellAssignment.FromCourse(
            course,
            roomIds,
            source.RowSpan,
            BuildProfessorNameMap(),
            BuildRoomNameMap(),
            source.Assignment.AssignmentId,
            source.Assignment.CourseKey,
            source.Assignment.ShowSectionLabel);
        return BuildStagedBlockItem(assignment, source.SourceDay, source.SourcePeriod, rows) with
        {
            Id = source.Id,
        };
    }

    private bool ReplaceSelectedStagedBlock(
        StagedBlockItem selected,
        StagedBlockItem updated,
        out string reason)
    {
        reason = "";
        var index = StagedBlocks.IndexOf(selected);
        if (index < 0)
        {
            index = StagedBlocks
                .Select((block, i) => new { block, i })
                .FirstOrDefault(pair => string.Equals(pair.block.Id, selected.Id, StringComparison.Ordinal))
                ?.i ?? -1;
        }

        if (index < 0)
        {
            reason = "선택한 보관함 블럭을 찾을 수 없습니다.";
            return false;
        }

        StagedBlocks[index] = updated;
        SelectedStagedBlock = updated;
        NotifyStagedBlocksChanged();
        SyncInspectorControlSelections();
        NotifySelectionDetailChanged();
        return true;
    }

    private StagedBlockItem BuildStagedBlockItem(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        IReadOnlyList<SolutionAssignment> rows)
    {
        var clonedRows = CloneAssignments(rows);
        var rooms = assignment.Rooms.Count == 0
            ? "-"
            : string.Join(", ", assignment.Rooms.Select(RoomDisplayName));
        var sourceRange = FormatConflictTimeLabel(sourceDay, sourcePeriod, sourcePeriod + assignment.RowSpan - 1);
        var gradeLabel = assignment.Grade > 0
            ? AcademicLevels.DisplayName(assignment.Grade)
            : "배치할 칸의 학년";
        var subtitle = $"{gradeLabel} / {sourceRange}";
        var detail = $"{assignment.RowSpan}시간 / {rooms}";
        return new StagedBlockItem(
            Guid.NewGuid().ToString("N"),
            CloneCellAssignment(assignment),
            sourceDay,
            sourcePeriod,
            clonedRows,
            CourseSectionDisplayName(assignment),
            subtitle,
            detail);
    }

    private static List<SolutionAssignment> CloneAssignments(IEnumerable<SolutionAssignment> rows) =>
        rows.Select(a => new SolutionAssignment(a.CourseId, a.Day, a.Period, a.RoomId, a.AssignmentId)).ToList();

    private static CellAssignment CloneCellAssignment(CellAssignment assignment) =>
        assignment with
        {
            Rooms = assignment.Rooms.ToList(),
            CoteachProfIds = assignment.CoteachProfIds.ToList(),
            CoteachProfDisplayNames = assignment.CoteachProfDisplayNames.ToList(),
            RoomDisplayNames = assignment.RoomDisplayNames.ToList(),
        };

    private static StagedBlockItem CloneStagedBlock(StagedBlockItem item) =>
        item with
        {
            Assignment = CloneCellAssignment(item.Assignment),
            Assignments = CloneAssignments(item.Assignments),
        };

    private void NotifyStagedBlocksChanged()
    {
        OnPropertyChanged(nameof(HasStagedBlocks));
        OnPropertyChanged(nameof(HasNoStagedBlocks));
        OnPropertyChanged(nameof(StagedBlockCount));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void UpdateStagedPlacementPreview()
    {
        if (SelectedStagedBlock == null)
        {
            if (SelectedAssignment == null)
                Grid.ClearEditState();
            return;
        }

        Grid.SetEditState(null, BuildStagedPlacementStates(SelectedStagedBlock));
    }

    private void ClearGridSelectionForStagedPlacement()
    {
        SelectedAssignment = null;
        SelectedDay = null;
        SelectedPeriod = null;
        _selectedGridCell = null;
        NewRoomId = "";
        SelectedOldRoomId = "";
        SelectedReplacementRoomId = "";
        SelectedPrimaryProfessorId = "";
        SelectedCoteachProfessorId = "";
        SelectedRoomToAddId = "";
        OnPropertyChanged(nameof(SelectedSlotDisplayText));
        NotifySelectionDetailChanged();
        RefreshConflicts();
    }

    private void RefreshConflicts(bool strictManualCrossValidation = false)
    {
        var visibleConflicts = GetManualEditVisibleConflicts(strictManualCrossValidation);
        Conflicts.Clear();
        foreach (var c in visibleConflicts)
            Conflicts.Add(BuildConflictDisplayItem(c));
        ConflictGroups.Clear();
        foreach (var item in BuildConflictGroupDisplayItems(visibleConflicts))
            ConflictGroups.Add(item);
        UpdateConflictVisuals(visibleConflicts);
    }

    private void UpdateConflictVisuals(IReadOnlyList<ConflictItem> conflicts)
    {
        var labelsByCell = new Dictionary<UnifiedCellKey, HashSet<string>>();
        var connectors = new List<ConflictConnector>();
        var connectorKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var conflict in conflicts)
        {
            var connectorLabel = CompactConflictLabel(conflict.Type);
            var cells = ResolveConflictCells(conflict);

            foreach (var cell in cells)
            {
                var key = new UnifiedCellKey(
                    cell.Day,
                    cell.Period,
                    cell.Grade,
                    cell.SubColumnIdx);
                if (!labelsByCell.TryGetValue(key, out var labels))
                    labelsByCell[key] = labels = new HashSet<string>(StringComparer.Ordinal);
                labels.Add(connectorLabel);
            }

            if (!IsRelationalConflict(conflict.Type) || cells.Count < 2)
                continue;

            var source = new UnifiedCellKey(
                cells[0].Day,
                cells[0].Period,
                cells[0].Grade,
                cells[0].SubColumnIdx);
            foreach (var targetCell in cells.Skip(1))
            {
                var target = new UnifiedCellKey(
                    targetCell.Day,
                    targetCell.Period,
                    targetCell.Grade,
                    targetCell.SubColumnIdx);
                var ordered = string.CompareOrdinal(source.ToString(), target.ToString()) <= 0
                    ? (source, target)
                    : (target, source);
                var connectorKey = $"{ordered.Item1}|{ordered.Item2}|{connectorLabel}";
                if (connectorKeys.Add(connectorKey))
                    connectors.Add(new ConflictConnector(ordered.Item1, ordered.Item2, connectorLabel));
            }
        }

        Grid.SetConflictVisuals(
            labelsByCell.ToDictionary(
                pair => pair.Key,
                pair => string.Join(" · ", pair.Value.OrderBy(value => value).Take(3))),
            connectors);
    }

    private static bool IsRelationalConflict(ConflictType type) =>
        type is ConflictType.RoomConflict
            or ConflictType.ProfessorConflict
            or ConflictType.SectionConflict
            or ConflictType.GradeConflict
            or ConflictType.SameCourseSameDayConflict
            or ConflictType.ProfRoomInconsistent;

    private static string CompactConflictLabel(ConflictType type) => type switch
    {
        ConflictType.RoomConflict => "강의실 중복",
        ConflictType.ProfessorConflict => "교수 중복",
        ConflictType.SectionConflict => "분반 중복",
        ConflictType.GradeConflict => "학년 중복",
        ConflictType.LunchConflict => "점심 배치",
        ConflictType.ProfUnavailable => "교수 불가시간",
        ConflictType.FixedTimeViolation => "고정시간 이탈",
        ConflictType.FixedRoomViolation => "고정강의실 이탈",
        ConflictType.CourseUnavailableRoomViolation => "과목 불가강의실",
        ConflictType.BlockStartViolation => "블록 시작 위반",
        ConflictType.ProfRoomInconsistent => "강의실 불일치",
        ConflictType.SameCourseSameDayConflict => "같은 요일 중복",
        ConflictType.AcademicLevelTimeBandViolation => "시간대 위반",
        _ => "제약 위반",
    };

    private static int ConflictSortOrder(ConflictType type) => type switch
    {
        ConflictType.RoomConflict => 10,
        ConflictType.ProfessorConflict => 20,
        ConflictType.SectionConflict => 30,
        ConflictType.GradeConflict => 40,
        ConflictType.ProfUnavailable => 50,
        ConflictType.FixedTimeViolation => 60,
        ConflictType.ProfRoomInconsistent => 70,
        ConflictType.SameCourseSameDayConflict => 80,
        ConflictType.AcademicLevelTimeBandViolation => 90,
        _ => 1000,
    };

    private static bool IsManualEditDisplayConflictType(ConflictType type) =>
        type is ConflictType.RoomConflict
            or ConflictType.ProfessorConflict
            or ConflictType.SectionConflict
            or ConflictType.GradeConflict
            or ConflictType.ProfUnavailable
            or ConflictType.FixedTimeViolation
            or ConflictType.ProfRoomInconsistent
            or ConflictType.SameCourseSameDayConflict
            or ConflictType.AcademicLevelTimeBandViolation;

    private bool IsExcludedManualEditConflict(ConflictItem conflict) =>
        !IsManualEditDisplayConflictType(conflict.Type)
        || IsAllowedManualGraduateDaytimeConflict(conflict);

    private IReadOnlyList<ConflictItem> GetManualEditVisibleConflicts(bool strictManualCrossValidation = false) =>
        CollapseBlockConflicts(DetectAllConflicts(_working, strictManualCrossValidation)
            .Where(c => !IsExcludedManualEditConflict(c))
            .Where(c => !IsAllowedExistingManualCrossSectionConflict(c, _working))
            .Select(ClassifyManualEditConflict));

    private static ConflictItem ClassifyManualEditConflict(ConflictItem conflict) =>
        conflict.Type is ConflictType.ProfUnavailable or ConflictType.FixedTimeViolation
            ? conflict with { Severity = ConflictSeverity.Warning }
            : conflict;

    private bool IsAllowedManualGraduateDaytimeConflict(ConflictItem conflict)
    {
        if (conflict.Type != ConflictType.AcademicLevelTimeBandViolation)
            return false;

        return conflict.Assignments is { Count: > 0 }
            && conflict.Assignments.All(assignment =>
        {
            var course = ResolveCourseForAssignment(assignment);
            return course?.Grade == AcademicLevels.GraduateGrade
                && SchedulePeriods.Daytime.Contains(assignment.Period);
        });
    }

    private sealed class ConflictBlockGroup
    {
        public ConflictBlockGroup(UnifiedCell cell)
        {
            Cell = cell;
        }

        public UnifiedCell Cell { get; }
        public List<ConflictItem> Conflicts { get; } = new();
    }

    private IReadOnlyList<ConflictDisplayItem> BuildConflictGroupDisplayItems(
        IReadOnlyList<ConflictItem> conflicts)
    {
        var groups = new Dictionary<UnifiedCellKey, ConflictBlockGroup>();
        foreach (var conflict in conflicts)
        {
            foreach (var cell in ResolveConflictCells(conflict))
            {
                var key = new UnifiedCellKey(
                    cell.Day,
                    cell.Period,
                    cell.Grade,
                    cell.SubColumnIdx);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new ConflictBlockGroup(cell);
                    groups[key] = group;
                }

                group.Conflicts.Add(conflict);
            }
        }

        return groups.Values
            .OrderBy(group => group.Cell.Day)
            .ThenBy(group => group.Cell.Period)
            .ThenBy(group => group.Cell.Grade)
            .ThenBy(group => group.Cell.SubColumnIdx)
            .Select(group => BuildConflictGroupDisplayItem(group.Cell, group.Conflicts))
            .ToList();
    }

    private IReadOnlyList<UnifiedCell> ResolveConflictCells(ConflictItem conflict)
    {
        var assignments = conflict.Assignments is { Count: > 0 }
            ? conflict.Assignments
            : _working
                .Where(assignment => assignment.Day == conflict.Day
                    && assignment.Period == conflict.Period)
                .ToList();
        var cells = assignments
            .Select(ResolveConflictFocusCell)
            .Where(cell => cell != null)
            .Select(cell => cell!)
            .DistinctBy(cell => new UnifiedCellKey(
                cell.Day,
                cell.Period,
                cell.Grade,
                cell.SubColumnIdx))
            .ToList();

        if (cells.Count == 0 && ResolveConflictFocusCell(conflict) is { } fallback)
            cells.Add(fallback);

        return cells;
    }

    private ConflictDisplayItem BuildConflictDisplayItem(ConflictItem conflict)
    {
        var title = BuildConflictDisplayTitle(conflict);
        var label = ConflictDisplayLabel(conflict.Type);
        var description = BuildUserConflictDescription(conflict);
        var assignments = ConflictAssignmentsInSlot(conflict)
            .DistinctBy(AssignmentConflictIdentity)
            .ToList();
        SolutionAssignment? selected = assignments
            .Where(IsSelectedConflictAssignment)
            .Select(assignment => (SolutionAssignment?)assignment)
            .FirstOrDefault();
        var related = selected is not SolutionAssignment selectedAssignment
            ? assignments
            : assignments.Where(a => !IsSameAssignmentIdentity(a, selectedAssignment)).ToList();
        return new ConflictDisplayItem(
            conflict,
            title,
            BuildConflictDisplayLines(conflict, label, description, selected, related),
            selected,
            related)
        {
            DetailConflicts = new[] { BuildUserConflictItem(conflict) },
        };
    }

    private ConflictDisplayItem BuildConflictGroupDisplayItem(
        UnifiedCell cell,
        IReadOnlyList<ConflictItem> conflicts)
    {
        var orderedConflicts = conflicts
            .DistinctBy(conflict => KeyOf(conflict))
            .OrderBy(conflict => ConflictSortOrder(conflict.Type))
            .ThenBy(conflict => conflict.Day)
            .ThenBy(conflict => conflict.Period)
            .ToList();
        var primary = orderedConflicts.First();
        var selected = FindAssignmentForConflictCell(cell);
        var related = orderedConflicts
            .SelectMany(ConflictAssignmentsInSlot)
            .DistinctBy(AssignmentConflictIdentity)
            .Where(assignment => selected is not SolutionAssignment selectedAssignment
                || !IsSameAssignmentIdentity(assignment, selectedAssignment))
            .ToList();

        return new ConflictDisplayItem(
            primary,
            BuildConflictDisplayTitle(cell),
            BuildConflictGroupDisplayLines(cell, orderedConflicts, selected, related),
            selected,
            related)
        {
            DetailConflicts = orderedConflicts.Select(BuildUserConflictItem).ToList(),
        };
    }

    private ConflictItem BuildUserConflictItem(ConflictItem conflict) =>
        conflict with { Description = BuildUserConflictDescription(conflict) };

    private IReadOnlyList<ConflictDisplayLine> BuildConflictDisplayLines(
        ConflictItem conflict,
        string label,
        string description,
        SolutionAssignment? selected,
        IReadOnlyList<SolutionAssignment> related)
    {
        var lines = new List<ConflictDisplayLine>();
        if (selected is SolutionAssignment selectedAssignment)
            AddCourseLine("선택한 수업:", selectedAssignment);

        foreach (var assignment in related)
            AddCourseLine(selected is null ? "관련 수업:" : "충돌 상대:", assignment);

        lines.Add(new ConflictDisplayLine("사유:", label, null, false));
        if (!string.IsNullOrWhiteSpace(description)
            && !string.Equals(description, label, StringComparison.Ordinal))
            lines.Add(new ConflictDisplayLine("상세:", description, null, false));

        return lines;

        void AddCourseLine(string role, SolutionAssignment assignment)
        {
            var course = ResolveCourseForAssignment(assignment);
            if (course == null)
                return;

            var range = ResolveConflictPeriodRange(assignment, conflict);
            var text = $"{BuildConflictCourseLabel(course, assignment.CourseId)} / {FormatConflictTimeLabel(assignment.Day, range.Start, range.End)}";
            lines.Add(new ConflictDisplayLine(role, text, course.Grade, true));
        }
    }

    private IReadOnlyList<ConflictDisplayLine> BuildConflictGroupDisplayLines(
        UnifiedCell cell,
        IReadOnlyList<ConflictItem> conflicts,
        SolutionAssignment? selected,
        IReadOnlyList<SolutionAssignment> related)
    {
        var lines = new List<ConflictDisplayLine>();
        if (selected is SolutionAssignment selectedAssignment)
            AddCourseLine("위반 블럭:", selectedAssignment);
        else
            lines.Add(new ConflictDisplayLine(
                "위반 블럭:",
                $"{CourseSectionDisplayName(cell.Assignment)} / {FormatConflictTimeLabel(cell.Day, cell.Period, cell.Period + cell.Assignment.RowSpan - 1)}",
                cell.Assignment.Grade,
                true));

        foreach (var assignment in related)
            AddCourseLine("관련 수업:", assignment);

        foreach (var conflict in conflicts)
        {
            var description = BuildUserConflictDescription(conflict);
            var text = string.IsNullOrWhiteSpace(description)
                ? ConflictDisplayLabel(conflict.Type)
                : $"{ConflictDisplayLabel(conflict.Type)} — {description}";
            lines.Add(new ConflictDisplayLine("사유:", text, null, false));
        }

        return lines;

        void AddCourseLine(string role, SolutionAssignment assignment)
        {
            var course = ResolveCourseForAssignment(assignment);
            if (course == null)
                return;

            var range = ResolveConflictPeriodRange(
                assignment,
                conflicts.First(),
                _working.Where(a => IsSameConflictRangeAssignment(a, assignment)).ToList());
            var text = $"{BuildConflictCourseLabel(course, assignment.CourseId)} / {FormatConflictTimeLabel(assignment.Day, range.Start, range.End)}";
            lines.Add(new ConflictDisplayLine(role, text, course.Grade, true));
        }
    }

    private static ConflictSelectionContext BuildConflictSelectionContext(
        CellAssignment assignment,
        int targetDay,
        int targetPeriod) =>
        new(assignment.CourseId, assignment.AssignmentId, targetDay, targetPeriod);

    private string BuildUserConflictDescription(ConflictItem conflict) => conflict.Type switch
    {
        ConflictType.RoomConflict => BuildRoomConflictDescription(conflict),
        ConflictType.ProfessorConflict => BuildProfessorConflictDescription(conflict),
        ConflictType.ProfUnavailable => BuildProfessorUnavailableDescription(conflict),
        ConflictType.FixedTimeViolation => BuildFixedTimeViolationDescription(conflict),
        ConflictType.FixedRoomViolation => BuildFixedRoomViolationDescription(conflict),
        ConflictType.CourseUnavailableRoomViolation => BuildCourseUnavailableRoomDescription(conflict),
        ConflictType.ProfRoomInconsistent => BuildProfessorRoomInconsistentDescription(conflict),
        ConflictType.BlockStartViolation => BuildBlockStartViolationDescription(conflict),
        ConflictType.SameCourseSameDayConflict => BuildSameCourseSameDayDescription(conflict),
        _ => SanitizeConflictDescription(conflict.Description),
    };

    private static string ConflictDisplayLabel(ConflictType type) => type switch
    {
        ConflictType.RoomConflict => "강의실 중복",
        ConflictType.ProfessorConflict => "교수 중복",
        ConflictType.ProfUnavailable => "교수 불가시간",
        ConflictType.SectionConflict => "분반 중복",
        ConflictType.GradeConflict => "학년 중복",
        ConflictType.LunchConflict => "점심시간 배치",
        ConflictType.FixedTimeViolation => "고정시간 이탈",
        ConflictType.FixedRoomViolation => "고정 강의실 위반",
        ConflictType.CourseUnavailableRoomViolation => "불가 강의실 위반",
        ConflictType.BlockStartViolation => "블록 시작 교시 위반",
        ConflictType.SameCourseSameDayConflict => "같은 요일 중복",
        ConflictType.ProfRoomInconsistent => "강의실 불일치",
        ConflictType.AcademicLevelTimeBandViolation => "시간대 위반",
        _ => "제약조건 위반",
    };

    private string BuildRoomConflictDescription(ConflictItem conflict)
    {
        var conflictAssignments = ConflictAssignmentsInSlot(conflict);
        var conflictingRooms = conflictAssignments
            .GroupBy(a => a.RoomId)
            .Where(g => g.Select(AssignmentConflictIdentity).Distinct(StringComparer.Ordinal).Count() > 1)
            .ToList();
        var group = conflictingRooms.FirstOrDefault();
        if (group == null)
            return SanitizeConflictDescription(conflict.Description);

        var courses = group
            .Select(a => BuildConflictCourseLabel(ResolveCourseForAssignment(a), a.CourseId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var range = ResolveConflictPeriodRange(group.First(), conflict, group.ToList());
        return $"{string.Join(", ", courses)} / {RoomDisplayName(group.Key)} / {FormatConflictTimeLabel(conflict.Day, range.Start, range.End)}";
    }

    private string BuildProfessorConflictDescription(ConflictItem conflict)
    {
        var slot = ConflictAssignmentsInSlot(conflict)
            .Select(a => (Assignment: a, Course: ResolveCourseForAssignment(a)))
            .Where(x => x.Course != null)
            .ToList();

        var professorGroups = new Dictionary<string, List<(SolutionAssignment Assignment, Course Course)>>(StringComparer.Ordinal);
        foreach (var item in slot)
        {
            foreach (var professorId in DomainHelpers.CourseProfIds(item.Course!))
            {
                if (!professorGroups.TryGetValue(professorId, out var list))
                    professorGroups[professorId] = list = new List<(SolutionAssignment, Course)>();
                list.Add((item.Assignment, item.Course!));
            }
        }

        var conflictGroup = professorGroups
            .FirstOrDefault(g => g.Value.Select(x => AssignmentConflictIdentity(x.Assignment)).Distinct(StringComparer.Ordinal).Count() > 1);
        if (string.IsNullOrWhiteSpace(conflictGroup.Key))
            return SanitizeConflictDescription(conflict.Description);

        var courses = conflictGroup.Value
            .Select(x => BuildConflictCourseLabel(x.Course, x.Assignment.CourseId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var range = ResolveConflictPeriodRange(
            conflictGroup.Value[0].Assignment,
            conflict,
            conflictGroup.Value.Select(x => x.Assignment).ToList());
        return $"{string.Join(", ", courses)} / {ProfessorDisplayName(conflictGroup.Key)} / {FormatConflictTimeLabel(conflict.Day, range.Start, range.End)}";
    }

    private string BuildProfessorUnavailableDescription(ConflictItem conflict)
    {
        var representative = ResolveRepresentativeConflictAssignment(conflict);
        if (representative == null)
            return SanitizeConflictDescription(conflict.Description);

        var (assignment, course) = representative.Value;
        var professorName = course == null
            ? ProfessorDisplayName(assignment.CourseId)
            : string.Join(", ", DomainHelpers.CourseProfIds(course).Select(ProfessorDisplayName));
        var range = ResolveConflictPeriodRange(
            assignment,
            conflict,
            ConflictAssignmentsInSlot(conflict)
                .Where(a => IsSameConflictRangeAssignment(a, assignment))
                .ToList());
        return $"{BuildConflictCourseLabel(course, assignment.CourseId)} / {professorName} / {FormatConflictTimeLabel(conflict.Day, range.Start, range.End)}";
    }

    private string BuildFixedRoomViolationDescription(ConflictItem conflict)
    {
        var representative = ResolveRepresentativeConflictAssignment(conflict);
        if (representative == null)
            return SanitizeConflictDescription(conflict.Description);

        var (assignment, course) = representative.Value;
        if (course == null || course.FixedRooms.Count == 0)
            return SanitizeConflictDescription(conflict.Description);

        var allowedRooms = string.Join(", ", course.FixedRooms.Select(RoomDisplayName));
        return $"{BuildConflictCourseLabel(course, assignment.CourseId)}은 지정된 강의실만 사용할 수 있습니다. 지정 강의실: {allowedRooms}. 현재 강의실: {RoomDisplayName(assignment.RoomId)}";
    }

    private string BuildFixedTimeViolationDescription(ConflictItem conflict)
    {
        var representative = ResolveRepresentativeConflictAssignment(conflict);
        if (representative == null)
            return SanitizeConflictDescription(conflict.Description);

        var (assignment, course) = representative.Value;
        if (course == null || course.FixedSlots.Count == 0)
            return SanitizeConflictDescription(conflict.Description);

        var range = ResolveConflictPeriodRange(assignment, conflict);
        return $"{BuildConflictCourseLabel(course, assignment.CourseId)}의 원래 고정시간은 {FormatFixedSlots(course.FixedSlots)}입니다. 현재 위치: {FormatConflictTimeLabel(assignment.Day, range.Start, range.End)}";
    }

    private static string FormatFixedSlots(IEnumerable<TimeSlot> slots)
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

                labels.Add(FormatConflictTimeLabel(dayGroup.Key, start, end));
                start = period;
                end = period;
            }

            labels.Add(FormatConflictTimeLabel(dayGroup.Key, start, end));
        }

        return labels.Count == 0 ? "-" : string.Join(", ", labels);
    }

    private string BuildCourseUnavailableRoomDescription(ConflictItem conflict)
    {
        var representative = ResolveRepresentativeConflictAssignment(conflict);
        if (representative == null)
            return SanitizeConflictDescription(conflict.Description);

        var (assignment, course) = representative.Value;
        if (course == null || course.UnavailableRooms.Count == 0)
            return SanitizeConflictDescription(conflict.Description);

        var unavailableRooms = string.Join(", ", course.UnavailableRooms.Select(RoomDisplayName));
        return $"{BuildConflictCourseLabel(course, assignment.CourseId)}은 사용할 수 없는 강의실에 배정되었습니다. 불가 강의실: {unavailableRooms}. 현재 강의실: {RoomDisplayName(assignment.RoomId)}";
    }

    private string BuildProfessorRoomInconsistentDescription(ConflictItem conflict)
    {
        if (conflict.Assignments is { Count: > 0 })
        {
            var rows = conflict.Assignments
                .Select(a => (Assignment: a, Course: ResolveCourseForAssignment(a)))
                .Where(x => x.Course != null)
                .Select(x => (x.Assignment, Course: x.Course!))
                .ToList();
            var selectedGroup = rows
                .GroupBy(x => x.Course.ProfessorId, StringComparer.Ordinal)
                .FirstOrDefault(g => g.Any(x => IsSelectedConflictAssignment(x.Assignment)))
                ?? rows.GroupBy(x => x.Course.ProfessorId, StringComparer.Ordinal).FirstOrDefault();
            if (selectedGroup != null)
            {
                var rooms = selectedGroup
                    .Select(x => RoomDisplayName(x.Assignment.RoomId))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var courses = selectedGroup
                    .Select(x => BuildConflictCourseLabel(x.Course, x.Assignment.CourseId))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                return $"{ProfessorDisplayName(selectedGroup.Key)} 교수님의 자동 배정 과목들이 서로 다른 강의실을 사용합니다. 수업: {string.Join(", ", courses)}. 강의실: {string.Join(", ", rooms)}";
            }
        }

        var description = SanitizeConflictDescription(conflict.Description);
        foreach (var professor in SessionProfessors.Concat(_workspace.Professors))
            description = ReplaceToken(description, professor.Id, ProfessorDisplayName(professor.Id));
        foreach (var room in SessionRooms.Concat(_workspace.Rooms))
            description = ReplaceToken(description, room.Id, RoomDisplayName(room.Id));
        return description.Contains("교수 강의실 일관성", StringComparison.Ordinal)
            ? description
            : $"교수 강의실 일관성: {description}";
    }

    private string BuildBlockStartViolationDescription(ConflictItem conflict)
    {
        // HC-19 설명에는 허용 시작 교시(예: 1, 3, 6, 8)가 포함된다.
        // 숫자 교수/강의실 ID를 이름으로 치환하는 일반 정제 로직을 거치면
        // 교시 숫자가 사람 이름으로 바뀔 수 있으므로 원문에서 제약 코드만 제거한다.
        var description = StripConstraintCodeForDisplay(conflict.Description ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(description))
            return description;

        var representative = ResolveRepresentativeConflictAssignment(conflict);
        if (representative == null)
            return "수업 블록은 해당 교시에 시작할 수 없습니다.";

        var (assignment, course) = representative.Value;
        var courseLabel = BuildConflictCourseLabel(course, assignment.CourseId);
        var rowSpan = Grid.Cells
            .FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(assignment.AssignmentId)
                && string.Equals(
                    c.Assignment.AssignmentId,
                    assignment.AssignmentId,
                    StringComparison.Ordinal))
            ?.Assignment.RowSpan ?? 1;

        return $"{courseLabel}의 {Math.Max(1, rowSpan)}시간 수업은 해당 교시에 시작할 수 없습니다.";
    }

    private string BuildSameCourseSameDayDescription(ConflictItem conflict)
    {
        var representative = ResolveRepresentativeConflictAssignment(conflict);
        if (representative == null)
            return SanitizeConflictDescription(conflict.Description);

        var (assignment, course) = representative.Value;
        var conflictAssignments = conflict.Assignments is { Count: > 0 }
            ? conflict.Assignments
            : Array.Empty<SolutionAssignment>();
        var range = ResolveConflictPeriodRange(assignment, conflict, conflictAssignments);
        return $"{BuildConflictCourseLabel(course, assignment.CourseId)}이 같은 요일에 중복 배치되었습니다. 시간: {FormatConflictTimeLabel(conflict.Day, range.Start, range.End)}";
    }

    private string SanitizeConflictDescription(string? description)
    {
        var value = StripConstraintCodeForDisplay(description ?? "");
        foreach (var course in SessionCourses)
            value = value.Replace($"({course.Id})", "", StringComparison.Ordinal);

        // 숫자로만 된 ID는 교시, 학년, 시간, 인원 등의 일반 숫자와 구분할 수 없다.
        // 일반 설명 정제에서는 치환하지 않고, 교수/강의실 전용 설명 생성 함수에서 표시한다.
        foreach (var professor in SessionProfessors
            .Concat(_workspace.Professors)
            .Where(p => !string.IsNullOrWhiteSpace(p.Id) && !p.Id.All(char.IsDigit))
            .DistinctBy(p => p.Id))
        {
            value = ReplaceToken(value, professor.Id, ProfessorDisplayName(professor.Id));
        }

        foreach (var room in SessionRooms
            .Concat(_workspace.Rooms)
            .Where(r => !string.IsNullOrWhiteSpace(r.Id) && !r.Id.All(char.IsDigit))
            .DistinctBy(r => r.Id))
        {
            value = ReplaceToken(value, room.Id, RoomDisplayName(room.Id));
        }

        return value.Trim();
    }

    private static string ReplaceToken(string text, string token, string replacement)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(replacement))
            return text;
        return Regex.Replace(
            text,
            $@"(?<![\p{{L}}\p{{N}}_-]){Regex.Escape(token)}(?![\p{{L}}\p{{N}}_-])",
            replacement);
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
                ? AcademicLevels.DisplayName(course.Grade)
                : assignmentCourseGrade(assignment) is int grade && grade > 0
                    ? AcademicLevels.DisplayName(grade)
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
            return $"알 수 없는 수업 / {fallbackTime}";
        if (!string.IsNullOrWhiteSpace(fallbackCourseId))
            return "알 수 없는 수업";
        if (!string.IsNullOrWhiteSpace(conflict.Type.ToString()))
            return conflict.Type.ToString();
        return "제약조건 위반";

        int? assignmentCourseGrade(SolutionAssignment assignment) =>
            ResolveCourseForAssignment(assignment)?.Grade;
    }

    private string BuildConflictDisplayTitle(UnifiedCell cell)
    {
        var courseLabel = CourseSectionDisplayName(cell.Assignment);
        var timeLabel = FormatConflictTimeLabel(
            cell.Day,
            cell.Period,
            cell.Period + Math.Max(1, cell.Assignment.RowSpan) - 1);
        return $"{timeLabel} / {courseLabel}";
    }

    private (SolutionAssignment Assignment, Course? Course)? ResolveRepresentativeConflictAssignment(ConflictItem conflict)
    {
        var slotAssignments = conflict.Assignments is { Count: > 0 }
            ? conflict.Assignments.ToList()
            : _working
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
            .OrderByDescending(IsSelectedConflictAssignment)
            .ThenBy(a => a.CourseId, StringComparer.Ordinal)
            .ThenBy(a => a.RoomId, StringComparer.Ordinal)
            .First();
        return (assignment, ResolveCourseForAssignment(assignment));
    }

    private IReadOnlyList<SolutionAssignment> ConflictAssignmentsInSlot(ConflictItem conflict)
    {
        if (conflict.Assignments is { Count: > 0 })
            return conflict.Assignments;

        return _working
            .Where(a => a.Day == conflict.Day && a.Period == conflict.Period)
            .ToList();
    }

    private bool IsConflictRelevantForCurrentSelection(ConflictItem conflict)
    {
        if (conflict.Type != ConflictType.ProfRoomInconsistent)
            return true;
        if (conflict.Assignments is not { Count: > 0 })
            return true;
        if (SelectedAssignment == null)
            return false;

        return conflict.Assignments.Any(IsSelectedConflictAssignment);
    }

    private UnifiedCell? ResolveConflictFocusCell(
        ConflictItem conflict,
        SolutionAssignment? preferredAssignment = null)
    {
        if (preferredAssignment.HasValue && ResolveConflictFocusCell(preferredAssignment.Value) is { } preferredCell)
            return preferredCell;

        var assignments = conflict.Assignments is { Count: > 0 }
            ? conflict.Assignments
            : _working
                .Where(a => a.Day == conflict.Day && a.Period == conflict.Period)
                .ToList();

        foreach (var assignment in assignments)
        {
            var cell = Grid.Cells.FirstOrDefault(c => IsSameConflictCell(assignment, c)
                && c.Day == assignment.Day
                && assignment.Period >= c.Period
                && assignment.Period < c.Period + c.Assignment.RowSpan);
            if (cell != null)
                return cell;
        }

        if (IsDisplayableSlot(conflict.Day, conflict.Period))
        {
            var cell = Grid.Cells.FirstOrDefault(c => c.Day == conflict.Day
                && conflict.Period >= c.Period
                && conflict.Period < c.Period + c.Assignment.RowSpan);
            if (cell != null)
                return cell;
        }

        return null;
    }

    private UnifiedCell? ResolveConflictFocusCell(SolutionAssignment assignment) =>
        Grid.Cells.FirstOrDefault(c => IsSameConflictCell(assignment, c)
            && c.Day == assignment.Day
            && assignment.Period >= c.Period
            && assignment.Period < c.Period + c.Assignment.RowSpan);

    private SolutionAssignment? FindAssignmentForConflictCell(UnifiedCell cell)
    {
        foreach (var assignment in _working)
        {
            if (IsSameConflictCell(assignment, cell)
                && assignment.Day == cell.Day
                && assignment.Period == cell.Period)
                return assignment;
        }

        foreach (var assignment in _working)
        {
            if (IsSameConflictCell(assignment, cell)
                && assignment.Day == cell.Day
                && assignment.Period >= cell.Period
                && assignment.Period < cell.Period + cell.Assignment.RowSpan)
                return assignment;
        }

        return null;
    }

    private static bool IsSameConflictCell(SolutionAssignment assignment, UnifiedCell cell)
    {
        if (!string.IsNullOrWhiteSpace(assignment.AssignmentId)
            && !string.IsNullOrWhiteSpace(cell.Assignment.AssignmentId))
        {
            return string.Equals(assignment.AssignmentId, cell.Assignment.AssignmentId, StringComparison.Ordinal);
        }

        return assignment.CourseId == cell.Assignment.CourseId
            && cell.Assignment.Rooms.Contains(assignment.RoomId);
    }

    private bool IsSelectedConflictAssignment(SolutionAssignment assignment) =>
        SelectedAssignment != null
        && IsSameAssignmentIdentity(assignment, SelectedAssignment);

    private bool IsSameAssignmentIdentity(SolutionAssignment assignment, CellAssignment selected)
    {
        if (!string.IsNullOrWhiteSpace(selected.AssignmentId))
            return string.Equals(assignment.AssignmentId, selected.AssignmentId, StringComparison.Ordinal);
        return assignment.CourseId == selected.CourseId
            && SelectedDay is int selectedDay
            && SelectedPeriod is int selectedPeriod
            && assignment.Day == selectedDay
            && assignment.Period == selectedPeriod
            && selected.Rooms.Contains(assignment.RoomId);
    }

    private static bool IsSameAssignmentIdentity(SolutionAssignment left, SolutionAssignment right) =>
        string.Equals(AssignmentConflictIdentity(left), AssignmentConflictIdentity(right), StringComparison.Ordinal);

    private static string AssignmentConflictIdentity(SolutionAssignment assignment) =>
        string.IsNullOrWhiteSpace(assignment.AssignmentId)
            ? string.Join("\u001f", assignment.CourseId, assignment.Day, assignment.Period, assignment.RoomId)
            : assignment.AssignmentId;

    private (int Start, int End) ResolveConflictPeriodRange(
        SolutionAssignment assignment,
        ConflictItem conflict,
        IReadOnlyList<SolutionAssignment>? scopedAssignments = null)
    {
        var source = scopedAssignments is { Count: > 0 }
            ? scopedAssignments
            : _working;
        var periods = source
            .Where(a => IsSameConflictRangeAssignment(a, assignment))
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

    private static bool IsSameConflictRangeAssignment(SolutionAssignment candidate, SolutionAssignment assignment)
    {
        if (!string.IsNullOrWhiteSpace(assignment.AssignmentId))
            return string.Equals(candidate.AssignmentId, assignment.AssignmentId, StringComparison.Ordinal);
        return candidate.CourseId == assignment.CourseId
            && candidate.Day == assignment.Day
            && candidate.RoomId == assignment.RoomId;
    }

    private static string BuildConflictCourseLabel(Course? course, string courseId)
    {
        if (course == null)
            return "알 수 없는 수업";
        return CourseSectionDisplayName(course);
    }

    private static string SectionDisplayName(int section) =>
        section <= 0 ? "" : $"{SectionLetter(section)}분반";

    private static string SectionLetter(int section) =>
        section >= 1 && section <= 26 ? ((char)('A' + section - 1)).ToString() : section.ToString();

    private static string FormatConflictTimeLabel(int day, int startPeriod, int endPeriod)
    {
        var periodLabel = startPeriod == endPeriod
            ? $"{startPeriod}교시"
            : $"{startPeriod}~{endPeriod}교시";
        return $"{DayName(day)} {periodLabel}";
    }

    private static string FormatPeriodRange(int startPeriod, int endPeriod) =>
        startPeriod == endPeriod ? $"{startPeriod}교시" : $"{startPeriod}~{endPeriod}교시";

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
            if (IsAllowedManualCrossConflictGroup(conflict, involved))
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
        foreach (var g in AcademicLevels.AllGrades)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(
                _working,
                courses,
                (c, _) => c.Grade == g,
                professors,
                rooms,
                SessionSchedulePolicy,
                _lunchPeriodsByDay);
            GradeViews.Add(new NamedGridViewModel(g.ToString(), AcademicLevels.DisplayName(g), vm));
        }

        RoomViews.Clear();
        foreach (var r in rooms)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(
                _working,
                courses,
                (_, rid) => rid == r.Id,
                professors,
                rooms,
                SessionSchedulePolicy,
                _lunchPeriodsByDay);
            RoomViews.Add(new NamedGridViewModel(r.Id, r.Name, vm));
        }

        ProfessorViews.Clear();
        foreach (var p in professors)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(
                _working,
                courses,
                (c, _) => IsCourseTaughtBy(c, p.Id),
                professors,
                rooms,
                SessionSchedulePolicy,
                _lunchPeriodsByDay);
            ProfessorViews.Add(new NamedGridViewModel(p.Id, p.Name, vm));
        }
    }

    private static bool IsCourseTaughtBy(Course course, string professorId) =>
        course.ProfessorId == professorId || course.CoteachProfs.Contains(professorId);

    private IReadOnlyList<ConflictItem> DetectAllConflicts(
        IReadOnlyList<SolutionAssignment> assignments,
        bool strictManualCrossValidation = false) =>
        ConflictDetector.Detect(
            assignments,
            SessionCourses,
            SessionProfessors,
            _workingCrossGroups,
            strictManualCrossValidation ? IsManualGradeOverlapAllowedStrict : IsManualGradeOverlapAllowed,
            AllowGraduateDaytimeOverflow,
            SessionSchedulePolicy,
            _lunchPeriodsByDay);

    private IReadOnlyList<ConflictItem> DetectConflicts(
        IReadOnlyList<SolutionAssignment> assignment,
        bool strictManualCrossValidation = false) =>
        DetectAllConflicts(assignment, strictManualCrossValidation)
            .Where(c => !IsExcludedManualEditConflict(c))
            .ToList();

    private bool AllowGraduateDaytimeOverflow =>
        AcademicLevelTimePolicy.AllowsGraduateDaytimeOverflow(SessionCourses, SessionCrossGroups);

    private void ClearSelectionCore()
    {
        SelectedAssignment = null;
        SelectedDay = null;
        SelectedPeriod = null;
        _selectedGridCell = null;
        DeleteEntireSelectedCourse = false;
        NewRoomId = "";
        SelectedOldRoomId = "";
        SelectedReplacementRoomId = "";
        SelectedPrimaryProfessorId = "";
        SelectedCoteachProfessorId = "";
        SelectedRoomToAddId = "";
        OnPropertyChanged(nameof(SelectedSlotDisplayText));
        NotifySelectionDetailChanged();
        Grid.ClearEditState();
        RefreshConflicts();
    }

    private string ProfessorDisplayName(string id) =>
        SessionProfessors.FirstOrDefault(p => p.Id == id)?.Name
        ?? _workspace.Professors.FirstOrDefault(p => p.Id == id)?.Name
        ?? UnknownProfessorDisplayName(id);

    private string ProfessorDisplayKey(string professorId) =>
        NormalizeProfessorDisplayKey(ProfessorDisplayName(professorId));

    private static string NormalizeProfessorDisplayKey(string? displayName) =>
        NormalizeManualInput(displayName).ToUpperInvariant();

    private static bool IsSameProfessorId(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

    private Course? ResolveCourseForCellAssignment(CellAssignment assignment)
    {
        var candidates = SessionCourses
            .Where(c => c.Id == assignment.CourseId)
            .ToList();
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

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

        return null;
    }

    private Course? ResolveCourseForAssignment(SolutionAssignment assignment)
    {
        var candidates = SessionCourses
            .Where(c => c.Id == assignment.CourseId)
            .ToList();
        if (candidates.Count <= 1) return candidates.FirstOrDefault();

        var assignmentIdCourse = ResolveCourseFromAssignmentId(assignment.AssignmentId, candidates);
        if (assignmentIdCourse != null) return assignmentIdCourse;

        var roomId = NormalizeRoomId(assignment.RoomId);
        var byFixedRoom = candidates
            .Where(c => c.FixedRooms.Any(room => string.Equals(NormalizeRoomId(room), roomId, StringComparison.Ordinal)))
            .ToList();
        if (byFixedRoom.Count == 1) return byFixedRoom[0];

        return null;
    }

    private static Course? ResolveCourseFromAssignmentId(
        string? assignmentId,
        IReadOnlyList<Course> candidates)
    {
        if (string.IsNullOrWhiteSpace(assignmentId)) return null;

        var parts = assignmentId.Split('\u001f');
        if (parts.Length < 7 || !string.Equals(parts[0], "occ", StringComparison.Ordinal))
            return null;

        var courseId = parts[1];
        var sectionText = parts[2];
        var professorId = parts[3];
        if (!int.TryParse(sectionText, out var section)) return null;

        var matches = candidates
            .Where(c => string.Equals(c.Id, courseId, StringComparison.Ordinal)
                && c.Section == section
                && string.Equals(c.ProfessorId, professorId, StringComparison.Ordinal))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private string RoomDisplayName(string id) =>
        _workspace.Rooms.FirstOrDefault(r => r.Id == id)?.Name
        ?? _sessionData?.Rooms.FirstOrDefault(r => r.Id == id)?.Name
        ?? UnknownRoomDisplayName(id);

    private IReadOnlyList<ProfessorOption> BuildProfessorOptions()
    {
        var optionsByDisplayName = new Dictionary<string, ProfessorOption>(StringComparer.Ordinal);
        var selectedProfessorIds = new HashSet<string>(StringComparer.Ordinal);
        if (SelectedCourse != null)
        {
            if (!string.IsNullOrWhiteSpace(SelectedCourse.ProfessorId))
                selectedProfessorIds.Add(SelectedCourse.ProfessorId);
            foreach (var id in SelectedCourse.CoteachProfs.Where(id => !string.IsNullOrWhiteSpace(id)))
                selectedProfessorIds.Add(id);
        }

        void Add(Professor professor)
        {
            var id = NormalizeManualInput(professor.Id);
            if (string.IsNullOrWhiteSpace(id))
                return;

            var displayName = string.IsNullOrWhiteSpace(professor.Name)
                ? id
                : professor.Name.Trim();
            var displayKey = NormalizeProfessorDisplayKey(displayName);
            if (string.IsNullOrWhiteSpace(displayKey))
                displayKey = id;
            var option = new ProfessorOption(
                id,
                displayName,
                $"{id} {displayName}".Trim());
            var prefer = selectedProfessorIds.Contains(id);
            if (!optionsByDisplayName.TryGetValue(displayKey, out _)
                || prefer)
            {
                optionsByDisplayName[displayKey] = option;
            }
        }

        if (_sessionData != null)
        {
            foreach (var professor in _sessionData.Professors)
                Add(professor);
        }

        foreach (var professor in _workspace.Professors)
            Add(professor);

        if (SelectedCourse != null)
        {
            Add(new Professor { Id = SelectedCourse.ProfessorId, Name = ProfessorDisplayName(SelectedCourse.ProfessorId) });
            foreach (var id in SelectedCourse.CoteachProfs)
                Add(new Professor { Id = id, Name = ProfessorDisplayName(id) });
        }

        return optionsByDisplayName.Values
            .OrderBy(option => option.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private IReadOnlyList<RoomOption> BuildRoomOptions(
        IEnumerable<string> roomIds,
        bool includeUnknownAssignedRooms,
        bool forceSelectable = false)
    {
        var roomById = BuildRoomCatalog()
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => NormalizeRoomId(r.Id), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var options = new Dictionary<string, RoomOption>(StringComparer.Ordinal);
        foreach (var rawId in roomIds)
        {
            var roomId = NormalizeRoomId(rawId);
            if (string.IsNullOrWhiteSpace(roomId)) continue;

            var room = roomById.TryGetValue(roomId, out var byId)
                ? byId
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

            var isSelectable = forceSelectable || IsRoomOptionSelectable(valueId);
            options[dedupeKey] = new RoomOption(
                valueId,
                displayName,
                room?.Capacity ?? 0,
                room?.IsLab ?? false,
                $"{valueId} {displayName}".Trim(),
                isSelectable);
        }

        return options.Values
            .OrderBy(option => option.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private bool IsRoomOptionSelectable(string roomId)
    {
        if (SelectedAssignment == null)
            return false;

        var replacementRoomId = NormalizeRoomId(roomId);
        if (string.IsNullOrWhiteSpace(replacementRoomId))
            return false;

        var oldRoomId = HasMultipleAssignedRooms
            ? NormalizeRoomId(SelectedOldRoomId)
            : NormalizeRoomId(SelectedAssignment.Rooms.FirstOrDefault());

        if (string.IsNullOrWhiteSpace(oldRoomId))
            return false;

        if (HasMultipleAssignedRooms
            && AssignedRoomsForSelectedAssignment.Contains(replacementRoomId)
            && !string.Equals(oldRoomId, replacementRoomId, StringComparison.Ordinal))
        {
            return false;
        }

        return IsRoomSelectableForCurrentChange(oldRoomId, replacementRoomId);
    }

    private static bool IsBareNumericRoomId(string roomId) =>
        roomId.All(char.IsDigit);

    private static string UnknownRoomDisplayName(string roomId) =>
        "알 수 없는 강의실";

    private static string UnknownProfessorDisplayName(string professorId) =>
        "알 수 없는 교수";

    private static string NormalizeRoomId(string? roomId) =>
        string.IsNullOrWhiteSpace(roomId) ? "" : roomId.Trim();

    private static string NormalizeRoomName(string? roomName) =>
        string.IsNullOrWhiteSpace(roomName) ? "" : roomName.Trim();

    private IReadOnlyList<Room> BuildRoomCatalog()
    {
        var rooms = new List<Room>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);

        void Add(Room room)
        {
            var id = NormalizeRoomId(room.Id);
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) return;

            var name = NormalizeRoomName(room.Name);
            if (!string.IsNullOrWhiteSpace(name) && !names.Add(name))
            {
                ids.Remove(id);
                return;
            }

            rooms.Add(room);
        }

        if (_sessionData != null)
        {
            foreach (var room in _sessionData.Rooms)
                Add(room);
        }

        foreach (var room in _workspace.Rooms)
            Add(room);

        return rooms;
    }

    private IReadOnlyList<string> BuildRoomViewIds()
    {
        var ids = new List<string>();
        ids.AddRange(_workspace.Rooms.Select(r => r.Id));
        if (_sessionData != null)
            ids.AddRange(_sessionData.Rooms.Select(r => r.Id));
        ids.AddRange(_working.Select(a => a.RoomId));
        return ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
    }

    private IReadOnlyList<string> BuildManualBlockRoomIds()
    {
        var ids = new List<string>();
        ids.AddRange(BuildRoomCatalog().Select(r => r.Id));
        ids.AddRange(_working.Select(a => a.RoomId));
        if (!string.IsNullOrWhiteSpace(NewBlockRoomId))
            ids.Add(NewBlockRoomId);
        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private IReadOnlyList<string> BuildReplacementRoomCandidates()
    {
        if (InspectorAssignment == null) return Array.Empty<string>();

        var assignedRooms = AssignedRoomsForSelectedAssignment.ToHashSet(StringComparer.Ordinal);
        return BuildEditableRoomIds(includeAssignedRooms: true)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(id => !assignedRooms.Contains(id))
            .Distinct()
            .ToList();
    }

    private IReadOnlyList<string> BuildAllowedRoomCandidateIds(bool includeAssignedRooms)
    {
        if (InspectorAssignment == null) return Array.Empty<string>();

        return BuildEditableRoomIds(includeAssignedRooms);
    }

    private IReadOnlyList<string> BuildEditableRoomIds(bool includeAssignedRooms)
    {
        var ids = new List<string>();
        ids.AddRange(BuildRoomCatalog().Select(r => r.Id));
        if (includeAssignedRooms)
            ids.AddRange(InspectorAssignment?.Rooms ?? Array.Empty<string>());

        return ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
    }

    private bool KnownRoomExists(string id)
    {
        var normalized = NormalizeRoomId(id);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        return _workspace.Rooms.Any(r => string.Equals(NormalizeRoomId(r.Id), normalized, StringComparison.Ordinal))
            || (_sessionData?.Rooms.Any(r => string.Equals(NormalizeRoomId(r.Id), normalized, StringComparison.Ordinal)) ?? false);
    }

    private static string Fallback(string value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;

    private void NotifySelectionDetailChanged()
    {
        OnPropertyChanged(nameof(InspectorAssignment));
        OnPropertyChanged(nameof(HasInspectorSelection));
        OnPropertyChanged(nameof(HasNoInspectorSelection));
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
        OnPropertyChanged(nameof(ProfessorOptions));
        OnPropertyChanged(nameof(AddableCoteachProfessorOptions));
        OnPropertyChanged(nameof(SelectedCoteachProfessorOptions));
        OnPropertyChanged(nameof(HasSelectedCoteachProfessors));
        OnPropertyChanged(nameof(HasNoSelectedCoteachProfessors));
        OnPropertyChanged(nameof(AddableRoomOptions));
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
        OnPropertyChanged(nameof(SelectedRoomAdditionalInfo));
        OnPropertyChanged(nameof(HasSelectedRoomAdditionalInfo));
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
        if (ManualCrossAssignmentMatches(selectedKey, targetKey))
        {
            reason = "같은 수업끼리는 크로스를 만들 수 없습니다.";
            return false;
        }
        if (IsAlreadyCrossed(selectedKey, targetKey))
        {
            reason = "이미 크로스가 설정된 수업입니다.";
            return false;
        }
        ManualCrossLink? replacedLink = null;

        var reasons = ValidateCrossMoveWithProvisionalLink(
            selected, target, day, targetPeriod, targetGrade, targetSubColumnIdx, replacedLink);
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
        if (replacedLink != null)
            _workingCrossLinks.Remove(replacedLink);
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
                out _,
                allowManualCrossOverlap: true,
                allowCrossDisplayColumn: true,
                allowFixedCourseMove: true,
                ignoreFixedTimeViolation: false,
                allowRelaxedStartPeriods: false,
                useGeneralMovePolicy: true))
        {
            if (RequiresSameCourseAssignmentConflictCheck(selected, target)
                && moveReasons.Count > 0
                && moveReasons.All(r => r.Contains("교수 강의실 일관성", StringComparison.Ordinal)))
            {
                if (!TryBuildMovedCandidate(selected, SelectedDay!.Value, selectedPeriod, day, targetPeriod, out candidate, out var identityReason))
                {
                    if (linkAdded)
                        _workingCrossLinks.Remove(provisionalLink);
                    if (replacedLink != null)
                        _workingCrossLinks.Add(replacedLink);
                    reason = identityReason;
                    return false;
                }
            }
            else
            {
                if (linkAdded)
                    _workingCrossLinks.Remove(provisionalLink);
                if (replacedLink != null)
                    _workingCrossLinks.Add(replacedLink);
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
        StatusMessage = $"크로스가 설정되었습니다: {CourseSectionDisplayName(selected)} ↔ {CourseSectionDisplayName(target)}";
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
        && _workingCrossLinks.Any(l => LinkMatchesPairIgnoringSlot(
            l,
            BuildManualCrossAssignmentKey(left, day: 0, period: 0),
            BuildManualCrossAssignmentKey(right, day: 0, period: 0)));

    private bool CanShareSlotByCross(CellAssignment left, CellAssignment right)
    {
        return IsCrossPair(left, right);
    }

    private bool IsManualGradeOverlapAllowed(string left, string right, int day, int period) =>
        IsManualGradeOverlapAllowedStrict(left, right, day, period);

    private bool IsManualGradeOverlapAllowedStrict(string left, string right, int day, int period) =>
        _workingCrossLinks.Any(l => l.Covers(left, right, day, period))
        || AreManualCrossCoursesConnectedAtSlot(left, right, day, period);

    private bool AreManualCrossCoursesConnectedAtSlot(string left, string right, int day, int period)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
            return true;

        var edges = _workingCrossLinks
            .Where(link => IsManualCrossLinkActiveAtSlot(link, day, period))
            .Select(link => (Left: link.SourceKey.CourseId, Right: link.TargetKey.CourseId))
            .ToList();
        if (edges.Count == 0)
            return false;

        var visited = new HashSet<string>(StringComparer.Ordinal) { left };
        var queue = new Queue<string>();
        queue.Enqueue(left);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in edges)
            {
                var next = string.Equals(edge.Left, current, StringComparison.Ordinal)
                    ? edge.Right
                    : string.Equals(edge.Right, current, StringComparison.Ordinal)
                        ? edge.Left
                        : null;
                if (next == null || !visited.Add(next))
                    continue;
                if (string.Equals(next, right, StringComparison.Ordinal))
                    return true;
                queue.Enqueue(next);
            }
        }

        return false;
    }

    private static bool IsManualCrossLinkActiveAtSlot(ManualCrossLink link, int day, int period) =>
        link.SourceKey.Day == day
        && link.TargetKey.Day == day
        && IsInsideManualCrossRange(period, link.SourceKey)
        && IsInsideManualCrossRange(period, link.TargetKey);

    private static bool IsInsideManualCrossRange(int period, ManualCrossAssignmentKey key) =>
        period >= key.Period && period < key.Period + key.RowSpan;

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

    private void RemoveCrossForAssignment(ManualCrossAssignmentKey key, string? statusMessage, bool recordUndo = true)
    {
        var snapshot = recordUndo ? CaptureSnapshot() : null;
        var removed = _workingCrossLinks.RemoveAll(l => l.Contains(key));
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
            .Select(a => new SolutionAssignment(a.CourseId, a.Day, a.Period, a.RoomId, a.AssignmentId))
            .ToList(),
        StagedBlocks = StagedBlocks
            .Select(CloneStagedBlock)
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
        SessionData = CloneAppData(_sessionData),
        SelectedAssignmentId = SelectedAssignment?.AssignmentId,
        SelectedStagedBlockId = SelectedStagedBlock?.Id,
        SelectedDay = SelectedDay,
        SelectedPeriod = SelectedPeriod,
        SelectedGrade = _selectedGridCell?.Grade,
        SelectedSubColumnIdx = _selectedGridCell?.SubColumnIdx,
        ShowRoomAdditionalInfo = ShowRoomAdditionalInfo,
    };

    private static bool SnapshotContentEquals(ManualEditSnapshot left, ManualEditSnapshot right)
    {
        var leftAssignments = left.Assignments
            .Select(a => string.Join("\u001f", a.CourseId, a.Day, a.Period, a.RoomId, a.AssignmentId))
            .OrderBy(x => x, StringComparer.Ordinal);
        var rightAssignments = right.Assignments
            .Select(a => string.Join("\u001f", a.CourseId, a.Day, a.Period, a.RoomId, a.AssignmentId))
            .OrderBy(x => x, StringComparer.Ordinal);
        if (!leftAssignments.SequenceEqual(rightAssignments, StringComparer.Ordinal))
            return false;

        var leftStagedBlocks = left.StagedBlocks
            .Select(StagedBlockContentKey)
            .OrderBy(x => x, StringComparer.Ordinal);
        var rightStagedBlocks = right.StagedBlocks
            .Select(StagedBlockContentKey)
            .OrderBy(x => x, StringComparer.Ordinal);
        if (!leftStagedBlocks.SequenceEqual(rightStagedBlocks, StringComparer.Ordinal))
            return false;

        var leftCrossLinks = left.CrossLinks
            .Select(CrossLinkContentKey)
            .OrderBy(x => x, StringComparer.Ordinal);
        var rightCrossLinks = right.CrossLinks
            .Select(CrossLinkContentKey)
            .OrderBy(x => x, StringComparer.Ordinal);
        if (!leftCrossLinks.SequenceEqual(rightCrossLinks, StringComparer.Ordinal))
            return false;

        if (!string.Equals(SessionContentKey(left.SessionData), SessionContentKey(right.SessionData), StringComparison.Ordinal))
            return false;

        return left.ShowRoomAdditionalInfo == right.ShowRoomAdditionalInfo;
    }

    private static string StagedBlockContentKey(StagedBlockItem item) =>
        string.Join(
            "\u001f",
            item.Id,
            item.SourceDay,
            item.SourcePeriod,
            string.Join("\u001e", item.Assignments
                .Select(a => string.Join("\u001d", a.CourseId, a.Day, a.Period, a.RoomId, a.AssignmentId))
                .OrderBy(x => x, StringComparer.Ordinal)));

    private static string CrossLinkContentKey(ManualCrossLink link) =>
        string.Join(
            "\u001f",
            link.CourseIdA,
            link.CourseIdB,
            link.Day,
            link.PeriodA,
            link.RowSpanA,
            link.PeriodB,
            link.RowSpanB,
            link.SourceKey.ToString(),
            link.TargetKey.ToString());

    private static string SessionContentKey(AppData? sessionData)
    {
        if (sessionData == null) return "";

        var courses = sessionData.Courses
            .Select(c => string.Join(
                "\u001d",
                c.Id,
                c.Name,
                c.Grade,
                c.HoursPerWeek,
                c.CourseType,
                c.ProfessorId,
                c.Section,
                c.Department,
                string.Join("|", c.FixedRooms),
                string.Join("|", c.UnavailableRooms),
                string.Join("|", c.BlockStructure),
                c.IsFixed,
                string.Join("|", c.FixedSlots.Select(s => $"{s.Day}:{s.Period}")),
                c.IsSchoolFixed,
                c.SchoolFixedTargetGrade,
                string.Join("|", c.CoteachProfs)))
            .OrderBy(x => x, StringComparer.Ordinal);
        var rooms = sessionData.Rooms
            .Select(r => string.Join("\u001d", r.Id, r.Name, r.IsLab, r.Capacity, r.IsImportedFromExcel))
            .OrderBy(x => x, StringComparer.Ordinal);

        return string.Join("\u001e", courses)
            + "\u001c"
            + string.Join("\u001e", rooms);
    }

    private void PushUndoSnapshot() => PushUndoSnapshot(CaptureSnapshot());

    private void PushUndoSnapshot(ManualEditSnapshot snapshot)
    {
        _undoStack.Push(snapshot);
        _redoStack.Clear();
        NotifyUndoRedoCanExecuteChanged();
    }

    private void RestoreSnapshot(ManualEditSnapshot snapshot)
    {
        _sessionData = CloneAppData(snapshot.SessionData);
        NotifyManualBlockCatalogChanged();
        _working = snapshot.Assignments
            .Select(a => new SolutionAssignment(a.CourseId, a.Day, a.Period, a.RoomId, a.AssignmentId))
            .ToList();
        StagedBlocks.Clear();
        foreach (var item in snapshot.StagedBlocks.Select(CloneStagedBlock))
            StagedBlocks.Add(item);
        NotifyStagedBlocksChanged();
        RestoreCrossLinks(snapshot.CrossLinks);
        ShowRoomAdditionalInfo = snapshot.ShowRoomAdditionalInfo;
        Rerender();
        RestoreSelectionFromSnapshot(snapshot);
        SelectedStagedBlock = StagedBlocks.FirstOrDefault(item =>
            string.Equals(item.Id, snapshot.SelectedStagedBlockId, StringComparison.Ordinal));
    }

    private void RestoreBaselineForDiscard()
    {
        if (_resetBaselineSnapshot == null) return;

        _sessionData = CloneAppData(_resetBaselineSnapshot.SessionData);
        NotifyManualBlockCatalogChanged();
        _working = _resetBaselineSnapshot.Assignments
            .Select(a => new SolutionAssignment(a.CourseId, a.Day, a.Period, a.RoomId, a.AssignmentId))
            .ToList();
        StagedBlocks.Clear();
        foreach (var item in _resetBaselineSnapshot.StagedBlocks.Select(CloneStagedBlock))
            StagedBlocks.Add(item);
        SelectedStagedBlock = StagedBlocks.FirstOrDefault(item =>
            string.Equals(item.Id, _resetBaselineSnapshot.SelectedStagedBlockId, StringComparison.Ordinal));
        NotifyStagedBlocksChanged();
        RestoreCrossLinks(_resetBaselineSnapshot.CrossLinks);
        ShowRoomAdditionalInfo = _resetBaselineSnapshot.ShowRoomAdditionalInfo;
        new ManualEditUserSettings { ShowRoomAdditionalInfo = ShowRoomAdditionalInfo }.Save();
        _redoStack.Clear();
        Rerender();
        ClearSelectionCore();
        UpdateStagedPlacementPreview();
        NotifyUndoRedoCanExecuteChanged();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void RestoreSelectionFromSnapshot(ManualEditSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SelectedAssignmentId))
        {
            ClearSelectionCore();
            return;
        }

        var cell = Grid.Cells.FirstOrDefault(c =>
            string.Equals(c.Assignment.AssignmentId, snapshot.SelectedAssignmentId, StringComparison.Ordinal)
            && (snapshot.SelectedDay == null || c.Day == snapshot.SelectedDay)
            && (snapshot.SelectedPeriod == null || c.Period == snapshot.SelectedPeriod)
            && (snapshot.SelectedGrade == null || c.Grade == snapshot.SelectedGrade)
            && (snapshot.SelectedSubColumnIdx == null || c.SubColumnIdx == snapshot.SelectedSubColumnIdx))
            ?? Grid.Cells.FirstOrDefault(c =>
                string.Equals(c.Assignment.AssignmentId, snapshot.SelectedAssignmentId, StringComparison.Ordinal));
        if (cell == null)
        {
            ClearSelectionCore();
            return;
        }

        SelectedDay = cell.Day;
        SelectedPeriod = cell.Period;
        SelectedAssignment = cell.Assignment;
        _selectedGridCell = new UnifiedCellKey(cell.Day, cell.Period, cell.Grade, cell.SubColumnIdx);
        NewRoomId = cell.Assignment.Rooms.FirstOrDefault() ?? "";
        SelectedOldRoomId = NewRoomId;
        SelectedReplacementRoomId = ReplacementRoomCandidates.FirstOrDefault() ?? "";
        OnPropertyChanged(nameof(SelectedSlotDisplayText));
        NotifySelectionDetailChanged();
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(cell.Assignment, cell.Day, cell.Period));
    }

    private void RestoreResetBaseline()
    {
        if (_resetBaselineSnapshot == null) return;

        _working = _resetBaselineSnapshot.Assignments
            .Select(a => new SolutionAssignment(a.CourseId, a.Day, a.Period, a.RoomId, a.AssignmentId))
            .ToList();
        RestoreCrossLinks(_resetBaselineSnapshot.CrossLinks);
        ShowRoomAdditionalInfo = _resetBaselineSnapshot.ShowRoomAdditionalInfo;
    }

    private void NotifyUndoRedoCanExecuteChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        UndoLastMoveCommand.NotifyCanExecuteChanged();
    }

    private bool IsCrossLinkStillValid(ManualCrossLink link)
    {
        var c1 = FindCourseForManualCrossKey(link.SourceKey);
        var c2 = FindCourseForManualCrossKey(link.TargetKey);
        return c1 != null
            && c2 != null
            && c1.Grade == c2.Grade
            && HasWorkingAssignmentForManualCrossKey(link.SourceKey)
            && HasWorkingAssignmentForManualCrossKey(link.TargetKey);
    }

    private bool HasWorkingAssignmentForManualCrossKey(ManualCrossAssignmentKey key) =>
        !string.IsNullOrWhiteSpace(key.AssignmentId)
            ? _working.Any(a => string.Equals(a.AssignmentId, key.AssignmentId, StringComparison.Ordinal))
            : _working.Any(a => a.CourseId == key.CourseId
                && a.Day == key.Day
                && a.Period == key.Period
                && (string.IsNullOrWhiteSpace(key.RoomIdsKey) || key.RoomIdsKey.Split('|').Contains(a.RoomId)));

    private IReadOnlyDictionary<string, string> BuildCrossLinkLabels()
    {
        var labels = new Dictionary<string, List<string>>();
        foreach (var link in _workingCrossLinks)
        {
            AddLabel(link.SourceKey, link.TargetKey);
            AddLabel(link.TargetKey, link.SourceKey);
        }
        return labels.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value));

        void AddLabel(ManualCrossAssignmentKey sourceKey, ManualCrossAssignmentKey targetKey)
        {
            var sourceDisplayKey = ToManualCrossDisplayKey(sourceKey);
            if (!labels.TryGetValue(sourceDisplayKey, out var list))
                labels[sourceDisplayKey] = list = new List<string>();
            list.Add(CourseDisplayName(targetKey));
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
                ManualCrossPolicyType,
                link.SourceKey.AssignmentId,
                link.TargetKey.AssignmentId));
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

            if (ManualCrossAssignmentMatches(sourceEndpoint.Value.Key, targetEndpoint.Value.Key))
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
        var assignmentId = (source ? row.SourceAssignmentId : row.TargetAssignmentId) ?? "";

        if (!string.IsNullOrWhiteSpace(assignmentId))
        {
            var byId = _working
                .Where(a => string.Equals(a.AssignmentId, assignmentId, StringComparison.Ordinal))
                .ToList();

            if (byId.Count > 0)
            {
                var blocks = BuildRestoredAssignmentBlocks(byId);

                if (blocks.Count == 1)
                    return BuildRestoredEndpoint(blocks[0], section);

                var matchingBlocks = blocks
                    .Where(block => SavedEndpointMatchesBlock(
                        block,
                        courseId,
                        section,
                        day,
                        period,
                        roomId))
                    .ToList();

                if (matchingBlocks.Count == 1)
                    return BuildRestoredEndpoint(matchingBlocks[0], section);

                if (byId.Count > 1)
                    return null;
            }
        }

        var exact = _working
            .Where(a => a.CourseId == courseId
                && a.Day == day
                && a.Period == period
                && string.Equals(a.RoomId, roomId, StringComparison.Ordinal)
                && CourseSectionMatches(a.CourseId, section))
            .ToList();
        if (exact.Count == 1)
            return BuildRestoredEndpoint(exact[0], section, rowSpan: GetWorkingRowSpan(exact[0]), new[] { exact[0].RoomId });
        if (exact.Count > 1)
            return null;

        var slot = _working
            .Where(a => a.CourseId == courseId
                && a.Day == day
                && a.Period == period
                && CourseSectionMatches(a.CourseId, section))
            .ToList();
        if (slot.Count > 0 && (string.IsNullOrWhiteSpace(roomId) || slot.Any(a => a.RoomId == roomId)))
        {
            var slotCandidates = string.IsNullOrWhiteSpace(roomId)
                ? slot
                : slot.Where(a => string.Equals(a.RoomId, roomId, StringComparison.Ordinal)).ToList();
            if (slotCandidates.Count != 1)
                return null;
            var restored = slotCandidates[0];
            return BuildRestoredEndpoint(restored, section, GetWorkingRowSpan(restored), new[] { restored.RoomId });
        }

        var candidates = BuildRestoredCandidateBlocks(courseId);
        if (candidates.Count == 1)
        {
            var only = candidates[0];
            return BuildRestoredEndpoint(only.Assignment, section, only.RowSpan);
        }

        return null;
    }

    private sealed record RestoredAssignmentBlock(
        SolutionAssignment Representative,
        string AssignmentId,
        string CourseId,
        string Section,
        string ProfessorId,
        int Day,
        int StartPeriod,
        int RowSpan,
        IReadOnlyList<string> RoomIds);

    private RestoredCrossEndpoint BuildRestoredEndpoint(
        RestoredAssignmentBlock block,
        string savedSection) =>
        BuildRestoredEndpoint(
            block.Representative,
            string.IsNullOrWhiteSpace(savedSection)
                ? block.Section
                : savedSection,
            block.RowSpan,
            block.RoomIds);

    private List<RestoredAssignmentBlock> BuildRestoredAssignmentBlocks(
        IReadOnlyList<SolutionAssignment> rows)
    {
        var blocks = new List<RestoredAssignmentBlock>();

        var indexedRows = rows
            .Select(a => new
            {
                Assignment = a,
                Course = ResolveCourseForAssignment(a),
            })
            .GroupBy(x => new
            {
                AssignmentId = x.Assignment.AssignmentId ?? "",
                x.Assignment.CourseId,
                Section = x.Course == null
                    ? ""
                    : NormalizeManualCrossSection(x.Course.Section),
                ProfessorId = x.Course?.ProfessorId ?? "",
                x.Assignment.Day,
            });

        foreach (var group in indexedRows)
        {
            var groupRows = group
                .Select(x => x.Assignment)
                .ToList();

            var periods = groupRows
                .Select(a => a.Period)
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            var i = 0;

            while (i < periods.Count)
            {
                var start = periods[i];
                var end = start;
                i++;

                while (i < periods.Count && periods[i] == end + 1)
                {
                    end = periods[i];
                    i++;
                }

                var runPeriods = Enumerable
                    .Range(start, end - start + 1)
                    .ToList();

                var firstRoomSet = RoomSetForPeriod(groupRows, start);

                if (firstRoomSet.Count == 0)
                    continue;

                if (runPeriods.Any(
                        p => !firstRoomSet.SequenceEqual(
                            RoomSetForPeriod(groupRows, p))))
                {
                    continue;
                }

                var representative = groupRows
                    .Where(a => a.Period == start)
                    .OrderBy(a => a.RoomId, StringComparer.Ordinal)
                    .First();

                blocks.Add(new RestoredAssignmentBlock(
                    representative,
                    group.Key.AssignmentId,
                    group.Key.CourseId,
                    group.Key.Section,
                    group.Key.ProfessorId,
                    group.Key.Day,
                    start,
                    runPeriods.Count,
                    firstRoomSet));
            }
        }

        return blocks;
    }

    private static List<string> RoomSetForPeriod(
        IEnumerable<SolutionAssignment> rows,
        int period) =>
        rows
            .Where(a => a.Period == period)
            .Select(a => NormalizeRoomId(a.RoomId))
            .Where(room => !string.IsNullOrWhiteSpace(room))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(room => room, StringComparer.Ordinal)
            .ToList();

    private static bool SavedEndpointMatchesBlock(
        RestoredAssignmentBlock block,
        string courseId,
        string section,
        int day,
        int period,
        string roomId)
    {
        if (!string.Equals(
                block.CourseId,
                courseId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(section)
            && !string.Equals(
                block.Section,
                section,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (block.Day != day || block.StartPeriod != period)
            return false;

        return string.IsNullOrWhiteSpace(roomId)
            || block.RoomIds.Contains(
                NormalizeRoomId(roomId),
                StringComparer.Ordinal);
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
            BuildRoomIdsKey(endpointRoomIds),
            assignment.AssignmentId);
        return new RestoredCrossEndpoint(assignment, key);
    }

    private int GetWorkingRowSpan(SolutionAssignment assignment) =>
        GetWorkingRowSpan(assignment.CourseId, assignment.Day, assignment.Period);

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
        $"{CourseDisplayName(link.SourceKey)} ↔ {CourseDisplayName(link.TargetKey)}";

    private string CourseDisplayName(ManualCrossAssignmentKey key) =>
        CourseSectionDisplayName(FindCourseForManualCrossKey(key), key.CourseId);

    private string CourseDisplayName(string id) =>
        CourseSectionDisplayName(SessionCourses.FirstOrDefault(c => c.Id == id), id);

    private string CourseSectionDisplayName(CellAssignment? assignment)
    {
        if (assignment == null)
            return "알 수 없는 수업";
        var course = ResolveCourseForCellAssignment(assignment);
        return CourseSectionDisplayName(course, assignment.CourseId);
    }

    private string CourseSectionDisplayName(Course? course, string fallbackCourseId) =>
        course == null ? "알 수 없는 수업" : CourseSectionDisplayName(course);

    private static string CourseSectionDisplayName(Course course)
    {
        var name = string.IsNullOrWhiteSpace(course.Name) ? "알 수 없는 수업" : course.Name.Trim();
        var section = SectionDisplayName(course.Section);
        return string.IsNullOrWhiteSpace(section) ? name : $"{name} {section}";
    }

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

        return null;
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
        var selectedDay = sourceDay ?? SelectedDay ?? targetDay;
        var selectedPeriod = sourcePeriod ?? SelectedPeriod ?? targetPeriod;
        if (!TryNormalizeCrossTarget(target, targetDay, targetPeriod, targetGrade, targetSubColumnIdx, out var normalizedTarget))
            return new[] { CrossTimeRangeMismatchReason };
        target = normalizedTarget.Assignment;
        targetDay = normalizedTarget.Day;
        targetPeriod = normalizedTarget.Period;
        targetGrade = normalizedTarget.Grade;
        targetSubColumnIdx = normalizedTarget.SubColumnIdx;
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
            allowCrossDisplayColumn: true,
            allowFixedCourseMove: true,
            ignoreFixedTimeViolation: false,
            allowRelaxedStartPeriods: false,
            useGeneralMovePolicy: true).BlockingReasons;
        _workingCrossLinks.Remove(provisional);
        return RequiresSameCourseAssignmentConflictCheck(selected, target)
            ? reasons.Where(reason => !reason.Contains("교수 강의실 일관성", StringComparison.Ordinal)).ToList()
            : reasons;
    }

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
        return ManualCrossAssignmentMatches(leftKey, rightKey);
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

    private bool CanStageSelectedBlock() =>
        SelectedAssignment != null
        && SelectedDay != null
        && SelectedPeriod != null;

    private bool CanUndo() => _undoStack.Count > 0;

    private bool CanRedo() => _redoStack.Count > 0;

    private static HashSet<string> ConflictKeys(IEnumerable<ConflictItem> conflicts) =>
        conflicts.Select(KeyOf).ToHashSet();

    private static string KeyOf(ConflictItem c) =>
        $"{c.Type}|{c.Day}|{c.Period}|{c.Description}";

    private string ToUserReason(ConflictItem c)
    {
        if (c.Severity == ConflictSeverity.Warning
            && c.Type == ConflictType.GradeConflict
            && c.Description.StartsWith("주의 필요:", StringComparison.Ordinal))
            return c.Description;

        var sentence = BuildMoveWarningSentence(c);
        if (!string.IsNullOrWhiteSpace(sentence))
            return sentence;

        var description = BuildUserConflictDescription(c);
        return string.IsNullOrWhiteSpace(description) ? ConflictDisplayLabel(c.Type) : description;
    }

    private string BuildMoveWarningSentence(ConflictItem c) => c.Type switch
    {
        ConflictType.FixedRoomViolation => BuildFixedRoomMoveWarning(c),
        ConflictType.CourseUnavailableRoomViolation => BuildCourseUnavailableRoomMoveWarning(c),
        ConflictType.ProfessorConflict => "해당 교수님의 다른 수업과 시간이 겹칩니다.",
        ConflictType.RoomConflict => "해당 강의실의 다른 수업과 시간이 겹칩니다.",
        ConflictType.GradeConflict => "같은 학년의 다른 수업과 시간이 겹칩니다.",
        ConflictType.SameCourseSameDayConflict => "같은 과목/분반/교수가 같은 요일에 중복 배치됩니다.",
        ConflictType.SectionConflict => "같은 과목의 다른 분반과 시간이 겹칩니다.",
        ConflictType.LunchConflict => "점심시간에는 수업을 배치할 수 없습니다.",
        ConflictType.FixedTimeViolation => "고정된 시간표를 벗어날 수 없습니다.",
        ConflictType.ProfUnavailable => "해당 교수님의 불가능 시간과 겹칩니다.",
        ConflictType.BlockStartViolation => "이 수업 블록은 해당 교시에 시작할 수 없습니다.",
        ConflictType.AcademicLevelTimeBandViolation => "대학원 과목은 야간에만, 학부 과목은 야간 이외 시간에만 배치할 수 있습니다.",
        _ => "",
    };

    private string BuildFixedRoomMoveWarning(ConflictItem c)
    {
        var representative = ResolveRepresentativeConflictAssignment(c);
        if (representative?.Course == null || representative.Value.Course.FixedRooms.Count == 0)
            return "지정된 강의실이 아닙니다.";
        var rooms = string.Join(", ", representative.Value.Course.FixedRooms.Select(RoomDisplayName));
        return $"지정된 강의실이 아닙니다. 지정 강의실: {rooms}.";
    }

    private string BuildCourseUnavailableRoomMoveWarning(ConflictItem c)
    {
        var representative = ResolveRepresentativeConflictAssignment(c);
        if (representative?.Course == null)
            return "사용할 수 없는 강의실입니다.";
        return $"{BuildConflictCourseLabel(representative.Value.Course, representative.Value.Assignment.CourseId)}은 이 강의실을 사용할 수 없습니다.";
    }

    private static string DayName(int d) => d switch
    {
        0 => "월",
        1 => "화",
        2 => "수",
        3 => "목",
        4 => "금",
        _ => "?"
    };
}

public sealed record ConflictDisplayItem(
    ConflictItem Conflict,
    string DisplayTitle,
    IReadOnlyList<ConflictDisplayLine> Lines,
    SolutionAssignment? SelectedAssignment,
    IReadOnlyList<SolutionAssignment> RelatedAssignments)
{
    public IReadOnlyList<ConflictItem> DetailConflicts { get; init; } = Array.Empty<ConflictItem>();
    public ConflictType Type => Conflict.Type;
    public ConflictSeverity Severity => Conflict.Severity;
    public string Description => Conflict.Description;
    public string DisplayDescription => string.Join(
        Environment.NewLine,
        Lines.Select(line => $"{line.Label} {line.Text}"));
    public int Day => Conflict.Day;
    public int Period => Conflict.Period;
}

public sealed record ConflictDisplayLine(string Label, string Text, int? Grade, bool IsCourse);

public sealed record ConflictFocusTarget(int Day, int Period, int Grade, int SubColumnIdx);

public sealed record ConflictFocusRequestedEventArgs(
    int Day,
    int Period,
    int Grade,
    int SubColumnIdx,
    IReadOnlyList<ConflictFocusTarget> RelatedTargets);
