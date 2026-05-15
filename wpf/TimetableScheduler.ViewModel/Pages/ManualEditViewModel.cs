using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.ViewModel.Services;

namespace TimetableScheduler.ViewModel.Pages;

public sealed partial class ManualEditViewModel : PageViewModelBase
{
    private readonly WorkspaceService _workspace;
    private readonly IConflictDialogService _dialog;
    private List<SolutionAssignment> _working = new();
    private List<SolutionAssignment>? _undoSnapshot;
    private UnifiedCellKey? _selectedGridCell;

    public override string Title => "수동 편집";

    public UnifiedTimetableViewModel Grid { get; } = new();

    public ObservableCollection<ConflictItem> Conflicts { get; } = new();

    [ObservableProperty]
    private RankedSolution? baseSolution;

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
    private string statusMessage = "셀을 클릭해서 편집할 과목을 선택하세요.";

    public IReadOnlyList<string> AvailableRoomIds =>
        _workspace.Rooms.Select(r => r.Id).ToList();

    public string SelectedSlotLabel =>
        SelectedDay is int d && SelectedPeriod is int p ? $"{DayName(d)} {p}교시" : "";

    public string SelectedProfessorName => SelectedCourse == null
        ? "-"
        : _workspace.Professors.FirstOrDefault(p => p.Id == SelectedCourse.ProfessorId)?.Name
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
            return string.Join(", ", SelectedCourse.CoteachProfs.Select(ProfessorDisplayName));
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
            var professor = _workspace.Professors.FirstOrDefault(p => p.Id == SelectedCourse.ProfessorId);
            if (SelectedCourse.FixedRooms.Count > 0)
                return string.Join(", ", SelectedCourse.FixedRooms.Select(RoomDisplayName));
            if (professor == null || professor.AllowedRooms.Count == 0)
                return "제한 없음";
            return string.Join(", ", professor.AllowedRooms.Select(RoomDisplayName));
        }
    }

    public string SelectedMoveStateText => SelectedAssignment == null
        ? "-"
        : SelectedAssignment.IsFixed ? "이동 불가" : "이동 가능";

    public string SelectedBlockedReasonText => SelectedAssignment == null
        ? "-"
        : SelectedAssignment.IsFixed
            ? "HC-13 고정 시간표 보존 위반: 고정된 수업은 이동할 수 없습니다."
            : "없음";

    public string SelectedWarningReasonText =>
        SelectedDay is int d && SelectedPeriod is int p
            ? GetSoftWarning(d, p) ?? "없음"
            : "-";

    public string SelectedCrossGroupText
    {
        get
        {
            if (SelectedCourse == null) return "-";
            var group = _workspace.CrossGroups.FirstOrDefault(g => g.BaseIds.Contains(SelectedCourse.Id));
            return group?.Id ?? "없음";
        }
    }

    private Course? SelectedCourse => SelectedAssignment == null
        ? null
        : _workspace.Courses.FirstOrDefault(c => c.Id == SelectedAssignment.CourseId);

    partial void OnSelectedAssignmentChanged(CellAssignment? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        NotifySelectionDetailChanged();
    }

    public ManualEditViewModel(WorkspaceService workspace, IConflictDialogService dialog)
    {
        _workspace = workspace;
        _dialog = dialog;
        Grid.ExpandAllGrades = true;
    }

    public void LoadFromSolution(RankedSolution solution)
    {
        BaseSolution = solution;
        _working = solution.Assignment.ToList();
        _undoSnapshot = null;
        Rerender();
        ClearSelectionCore();
        StatusMessage = "수업 블록을 클릭한 뒤, 초록색 칸을 클릭해 이동하세요.";
    }

    public void HandleCellClick(int day, int period, int grade, int subColumnIdx, CellAssignment? assignment)
    {
        if (SelectedAssignment != null && assignment == null)
        {
            TryMoveSelectedTo(day, period, grade, subColumnIdx);
            return;
        }

        SelectCell(day, period, grade, subColumnIdx, assignment);
    }

    public void SelectCell(int day, int period, int grade, int subColumnIdx, CellAssignment? assignment)
    {
        SelectedDay = day;
        SelectedPeriod = period;
        SelectedAssignment = assignment;
        _selectedGridCell = assignment == null ? null : new UnifiedCellKey(day, period, grade, subColumnIdx);
        OnPropertyChanged(nameof(SelectedSlotLabel));
        NotifySelectionDetailChanged();

        if (assignment != null)
        {
            NewRoomId = assignment.Rooms.FirstOrDefault() ?? "";
            StatusMessage = $"선택: {assignment.CourseName} ({assignment.CourseId}) — "
                + $"{DayName(day)} {period}교시";
            Grid.SetEditState(_selectedGridCell, BuildMoveStates(assignment, day, period));
        }
        else
        {
            NewRoomId = "";
            StatusMessage = $"{DayName(day)} {period}교시 — 비어있음";
            Grid.ClearEditState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyRoomChange))]
    private void ApplyRoomChange()
    {
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null) return;
        var cid = SelectedAssignment.CourseId;
        int day = SelectedDay.Value;
        int period = SelectedPeriod.Value;

        var oldRoomId = SelectedAssignment.Rooms.FirstOrDefault();
        if (oldRoomId == null) return;
        if (oldRoomId == NewRoomId)
        {
            StatusMessage = "변경 사항 없음 (동일한 강의실).";
            return;
        }

        var snapshot = _working.ToList();
        var changed = 0;
        for (int i = 0; i < _working.Count; i++)
        {
            var a = _working[i];
            if (a.CourseId == cid && a.Day == day && a.RoomId == oldRoomId
                && a.Period >= period && a.Period < period + SelectedAssignment.RowSpan)
            {
                _working[i] = new SolutionAssignment(cid, day, a.Period, NewRoomId);
                changed++;
            }
        }

        if (!TryCommitChange(snapshot, $"{cid}의 강의실 {oldRoomId} → {NewRoomId} (변경 {changed}건)"))
            return;

        SelectedAssignment = SelectedAssignment with { Rooms = new List<string> { NewRoomId } };
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(SelectedAssignment, day, period));
    }

    [RelayCommand(CanExecute = nameof(CanClearSelection))]
    private void ClearSelection()
    {
        ClearSelectionCore();
        StatusMessage = "선택이 해제되었습니다.";
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void UndoLastMove()
    {
        if (_undoSnapshot == null) return;
        _working = _undoSnapshot;
        _undoSnapshot = null;
        Rerender();
        ClearSelectionCore();
        StatusMessage = "직전 이동을 되돌렸습니다.";
        UndoLastMoveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Reset()
    {
        if (BaseSolution != null) LoadFromSolution(BaseSolution);
    }

    private void TryMoveSelectedTo(int targetDay, int targetPeriod, int targetGrade, int targetSubColumnIdx)
    {
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null) return;

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
            StatusMessage = reasons.FirstOrDefault() ?? "이동할 수 없는 칸입니다.";
            return;
        }

        var snapshot = _working.ToList();
        var movedCourseId = SelectedAssignment.CourseId;
        _working = candidate;
        Rerender();

        _undoSnapshot = snapshot;
        UndoLastMoveCommand.NotifyCanExecuteChanged();
        SelectedDay = targetDay;
        SelectedPeriod = targetPeriod;
        _selectedGridCell = new UnifiedCellKey(targetDay, targetPeriod, targetGrade, targetSubColumnIdx);
        OnPropertyChanged(nameof(SelectedSlotLabel));
        NotifySelectionDetailChanged();
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(SelectedAssignment, targetDay, targetPeriod));
        StatusMessage = $"{movedCourseId} 이동 완료 — {DayName(targetDay)} {targetPeriod}교시";
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
                            subColumnIdx);
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
        out IReadOnlyList<string> reasons)
    {
        reasons = ValidateManualMove(
            assignment,
            sourceDay,
            sourcePeriod,
            targetDay,
            targetPeriod,
            targetGrade,
            targetSubColumnIdx);
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
        int targetSubColumnIdx)
    {
        if (targetGrade != assignment.Grade)
            return new[] { "선택한 수업은 같은 학년 열 안에서만 이동할 수 있습니다." };

        var course = _workspace.Courses.First(c => c.Id == assignment.CourseId);
        if (course.IsFixed)
            return new[] { "HC-13 고정 시간표 보존 위반: 고정된 수업은 이동할 수 없습니다." };

        var targetPeriods = GetOccupiedPeriods(targetPeriod, assignment.RowSpan);
        if (targetPeriods.Any(p => !Constants.Periods.Contains(p)))
            return new[] { "수업 블록이 시간표 범위를 벗어납니다." };
        if (targetPeriods.Contains(Constants.LunchPeriod))
            return new[] { "HC-12 점심시간 보장 위반: 점심시간에는 수업을 배정할 수 없습니다." };
        if (assignment.RowSpan == 2 && !Constants.Len2StartPeriods.Contains(targetPeriod))
            return new[] { "HC-19 블록 시작 교시 위반: 2시간 수업은 1, 3, 6, 8교시에만 시작할 수 있습니다." };

        var blockingAssignments = GetBlockingAssignments(
            assignment,
            sourceDay,
            sourcePeriod,
            targetDay,
            targetPeriods,
            targetGrade,
            targetSubColumnIdx);
        if (blockingAssignments.Count > 0)
        {
            var blockingCourses = blockingAssignments
                .Select(a => _workspace.Courses.FirstOrDefault(c => c.Id == a.CourseId))
                .Where(c => c != null)
                .Cast<Course>()
                .ToList();
            if (blockingCourses.Any(c => c.IsFixed))
                return new[] { "HC-13 고정 시간표 보존 위반: 고정 수업 위로 이동할 수 없습니다." };

            return new[] { "HC-06 연속 블록 보장 위반: 연속 수업에 필요한 시간 슬롯이 비어 있지 않습니다. 해당 시간대에 이미 다른 수업이 배정되어 있습니다." };
        }

        var candidate = BuildMovedCandidate(assignment, sourceDay, sourcePeriod, targetDay, targetPeriod);

        var baselineKeys = ConflictKeys(DetectConflicts(_working));
        var newConflicts = DetectConflicts(candidate)
            .Where(c => !baselineKeys.Contains(KeyOf(c)))
            .ToList();
        return newConflicts.Select(ToUserReason).ToList();
    }

    private static List<int> GetOccupiedPeriods(int startPeriod, int rowSpan) =>
        Enumerable.Range(startPeriod, rowSpan).ToList();

    private static string? GetSoftWarning(int day, int period)
    {
        if (day == 0 && period is >= 1 and <= 4)
            return "이동 가능하지만 권장되지 않는 조건이 있습니다: 월요일 오전 배정입니다.";
        if (day == 4 && period is >= 6 and <= 9)
            return "이동 가능하지만 권장되지 않는 조건이 있습니다: 금요일 오후 배정입니다.";
        return null;
    }

    private List<SolutionAssignment> GetBlockingAssignments(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        IReadOnlyList<int> targetPeriods,
        int targetGrade,
        int targetSubColumnIdx)
    {
        var occupiedCourseIds = Grid.Cells
            .Where(c => c.Day == targetDay
                && c.Grade == targetGrade
                && c.SubColumnIdx == targetSubColumnIdx
                && targetPeriods.Any(p => p >= c.Period && p < c.Period + c.Assignment.RowSpan))
            .Where(c => !IsSameMovingCell(c, assignment, sourceDay, sourcePeriod))
            .Select(c => c.Assignment.CourseId)
            .ToHashSet();

        return _working
            .Where(a => occupiedCourseIds.Contains(a.CourseId))
            .Where(a => a.Day == targetDay && targetPeriods.Contains(a.Period))
            .ToList();
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
        cell.Assignment.CourseId == assignment.CourseId
        && cell.Day == sourceDay
        && cell.Period == sourcePeriod
        && cell.Assignment.Rooms.SequenceEqual(assignment.Rooms);

    private bool TryCommitChange(List<SolutionAssignment> snapshot, string successMessage)
    {
        Rerender();
        var baselineKeys = ConflictKeys(DetectConflicts(snapshot));
        var newOnes = Conflicts.Where(c => !baselineKeys.Contains(KeyOf(c))).ToList();

        if (newOnes.Count > 0)
        {
            bool proceed = _dialog.ConfirmDespiteConflicts(newOnes);
            if (!proceed)
            {
                _working = snapshot;
                Rerender();
                StatusMessage = $"변경 취소됨 — {ToUserReason(newOnes[0])}";
                return false;
            }
        }

        StatusMessage = successMessage
            + (newOnes.Count > 0 ? $" — 신규 위반 {newOnes.Count}건 수용" : "");
        return true;
    }

    private void Rerender()
    {
        Grid.Render(_working, _workspace.Courses);
        Conflicts.Clear();
        foreach (var c in DetectConflicts(_working))
            Conflicts.Add(c);
    }

    private IReadOnlyList<ConflictItem> DetectConflicts(IReadOnlyList<SolutionAssignment> assignment) =>
        ConflictDetector.Detect(
            assignment,
            _workspace.Courses,
            _workspace.Professors,
            _workspace.CrossGroups);

    private void ClearSelectionCore()
    {
        SelectedAssignment = null;
        SelectedDay = null;
        SelectedPeriod = null;
        _selectedGridCell = null;
        NewRoomId = "";
        OnPropertyChanged(nameof(SelectedSlotLabel));
        NotifySelectionDetailChanged();
        Grid.ClearEditState();
    }

    private string ProfessorDisplayName(string id) =>
        _workspace.Professors.FirstOrDefault(p => p.Id == id)?.Name ?? id;

    private string RoomDisplayName(string id) =>
        _workspace.Rooms.FirstOrDefault(r => r.Id == id)?.Name ?? id;

    private static string Fallback(string value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;

    private void NotifySelectionDetailChanged()
    {
        OnPropertyChanged(nameof(SelectedProfessorName));
        OnPropertyChanged(nameof(SelectedLocationText));
        OnPropertyChanged(nameof(SelectedRoomsText));
        OnPropertyChanged(nameof(SelectedDurationText));
        OnPropertyChanged(nameof(SelectedOccupiedSlotsText));
        OnPropertyChanged(nameof(SelectedBlockText));
        OnPropertyChanged(nameof(SelectedFixedText));
        OnPropertyChanged(nameof(SelectedCoteachText));
        OnPropertyChanged(nameof(SelectedMultiRoomText));
        OnPropertyChanged(nameof(SelectedAllowedRoomsText));
        OnPropertyChanged(nameof(SelectedMoveStateText));
        OnPropertyChanged(nameof(SelectedBlockedReasonText));
        OnPropertyChanged(nameof(SelectedWarningReasonText));
        OnPropertyChanged(nameof(SelectedCrossGroupText));
    }

    private bool CanApplyRoomChange() =>
        SelectedAssignment != null
        && !string.IsNullOrEmpty(NewRoomId)
        && SelectedAssignment.Rooms.FirstOrDefault() != NewRoomId;

    private bool CanClearSelection() => SelectedAssignment != null;

    private bool CanUndo() => _undoSnapshot != null;

    private static HashSet<string> ConflictKeys(IEnumerable<ConflictItem> conflicts) =>
        conflicts.Select(KeyOf).ToHashSet();

    private static string KeyOf(ConflictItem c) =>
        $"{c.Type}|{c.Day}|{c.Period}|{c.Description}";

    private static string ToUserReason(ConflictItem c) => c.Type switch
    {
        ConflictType.RoomConflict => $"HC-01 강의실 단일 배정 위반: {c.Description}",
        ConflictType.ProfessorConflict => $"HC-02 교수 단일 배정 위반: {c.Description}",
        ConflictType.ProfUnavailable => $"HC-03 교수 불가능 시간 위반: {c.Description}",
        ConflictType.SectionConflict => $"HC-08 분반 중복 금지 위반: {c.Description}",
        ConflictType.GradeConflict => $"HC-11 학년 내 과목 충돌 위반: {c.Description}",
        ConflictType.LunchConflict => $"HC-12 점심시간 보장 위반: {c.Description}",
        ConflictType.FixedTimeViolation => $"HC-13 고정 시간표 보존 위반: {c.Description}",
        ConflictType.FixedRoomViolation => $"HC-14 고정 강의실 위반: {c.Description}",
        ConflictType.BlockStartViolation => $"HC-19 블록 시작 교시 위반: {c.Description}",
        ConflictType.ProfAllowedRoomViolation => $"HC-21 허용 강의실 위반: {c.Description}",
        ConflictType.ProfRoomInconsistent => $"HC-21 교수 강의실 일관성 위반: {c.Description}",
        _ => c.Description,
    };

    private static string DayName(int d) => d switch
    {
        0 => "월", 1 => "화", 2 => "수", 3 => "목", 4 => "금",
        _ => "?"
    };
}
