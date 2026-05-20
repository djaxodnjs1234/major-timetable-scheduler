using System.Collections.ObjectModel;
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
    public sealed record ManualCrossLink(
        string CourseIdA,
        string CourseIdB,
        int Day,
        int PeriodA,
        int RowSpanA,
        int PeriodB,
        int RowSpanB)
    {
        public bool MatchesPair(string left, string right) =>
            (CourseIdA == left && CourseIdB == right)
            || (CourseIdA == right && CourseIdB == left);

        public bool Covers(string left, string right, int day, int period) =>
            MatchesPair(left, right);
    }

    private readonly WorkspaceService _workspace;
    private readonly IConflictDialogService _dialog;
    private List<SolutionAssignment> _working = new();
    private List<CrossGroup> _workingCrossGroups = new();
    private readonly List<ManualCrossLink> _workingCrossLinks = new();
    private sealed class ManualEditSnapshot
    {
        public List<SolutionAssignment> Assignments { get; init; } = new();
        public List<ManualCrossLink> CrossLinks { get; init; } = new();
    }

    private readonly Stack<ManualEditSnapshot> _undoStack = new();
    private readonly Stack<ManualEditSnapshot> _redoStack = new();
    private UnifiedCellKey? _selectedGridCell;

    public override string Title => "수동 편집";

    public UnifiedTimetableViewModel Grid { get; } = new();

    public ObservableCollection<ConflictItem> Conflicts { get; } = new();
    public IReadOnlyList<ManualCrossLink> WorkingCrossLinks => _workingCrossLinks;
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
        : _workspace.ExpandedCourses.FirstOrDefault(c => c.Id == SelectedAssignment.CourseId);

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
        _workingCrossGroups = _workspace.CrossGroups
            .Select(g => new CrossGroup { Id = g.Id, BaseIds = g.BaseIds.ToList() })
            .ToList();
        _workingCrossLinks.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        NotifyUndoRedoCanExecuteChanged();
        Rerender();
        ClearSelectionCore();
        StatusMessage = "수업 블록을 클릭한 뒤, 초록색 칸을 클릭해 이동하세요.";
    }

    public void LoadFromSavedTimetable(SavedTimetableRecord timetable)
    {
        var assignments = timetable.Assignments
            .Select(r => new SolutionAssignment(r.CourseId, r.Day, r.Period, r.RoomId))
            .ToList();

        LoadFromSolution(new RankedSolution(assignments, new SolutionScore(0, 0, 0, 0)));
        SaveName = timetable.Name;
        StatusMessage = $"저장된 시간표 '{timetable.Name}'을(를) 수정 모드로 열었습니다.";
    }

    public void HandleCellClick(int day, int period, int grade, int subColumnIdx, CellAssignment? assignment)
    {
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
            var selectedCourseId = SelectedAssignment.CourseId;
            if (TryMoveSelectedTo(day, period, grade, subColumnIdx))
                RemoveCrossForCourse(selectedCourseId, "이동으로 기존 크로스가 해제되었습니다.", recordUndo: false);
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
        if (SelectedAssignment == null || SelectedDay is not int selectedDay || SelectedPeriod is not int selectedPeriod)
            return CrossHoverState.Hidden();
        if (target == null)
            return CrossHoverState.Hidden();
        if (IsSameMovingCell(
                new UnifiedCell(day, period, grade, subColumnIdx, target),
                SelectedAssignment,
                selectedDay,
                selectedPeriod))
            return CrossHoverState.Hidden("현재 선택한 수업입니다.");
        if (target.Grade != SelectedAssignment.Grade)
            return CrossHoverState.Hidden("같은 학년 수업끼리만 크로스를 만들 수 있습니다.");

        var selectedCourse = _workspace.ExpandedCourses.FirstOrDefault(c => c.Id == SelectedAssignment.CourseId);
        var targetCourse = _workspace.ExpandedCourses.FirstOrDefault(c => c.Id == target.CourseId);
        if (selectedCourse == null || targetCourse == null)
            return CrossHoverState.Hidden("수업 정보를 확인할 수 없습니다.");
        var existingLink = GetCrossLinkForCourse(SelectedAssignment.CourseId);
        if (IsAlreadyCrossed(SelectedAssignment, target))
            return CrossHoverState.Hidden("이미 크로스가 설정된 수업입니다.");
        if (IsCourseAlreadyCrossedExcept(SelectedAssignment.CourseId, existingLink)
            || IsCourseAlreadyCrossedExcept(target.CourseId, existingLink))
            return CrossHoverState.Hidden("이미 다른 수업과 크로스된 과목입니다. 크로스는 두 과목끼리만 설정할 수 있습니다.");

        var reasons = ValidateCrossMoveWithProvisionalLink(SelectedAssignment, target, day, period, grade, subColumnIdx, existingLink);
        if (reasons.Count > 0)
            return CrossHoverState.Hidden(reasons[0]);

        return CrossHoverState.Available();
    }

    public void HandleCrossAddRequested(
        int day,
        int period,
        int grade,
        int subColumnIdx,
        CellAssignment target)
    {
        var state = EvaluateCrossHover(day, period, grade, subColumnIdx, target);
        if (!state.CanCreate || SelectedAssignment == null)
        {
            if (!string.IsNullOrWhiteSpace(state.Reason))
                StatusMessage = state.Reason;
            return;
        }

        if (!TryCreateOrReplaceCrossAndMove(SelectedAssignment, target, day, period, grade, subColumnIdx, out var reason))
        {
            StatusMessage = reason;
            return;
        }
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

        var snapshot = CaptureSnapshot();
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
        if (BaseSolution == null) return;
        PushUndoSnapshot();
        _working = BaseSolution.Assignment.ToList();
        _workingCrossLinks.Clear();
        Rerender();
        ClearSelectionCore();
        StatusMessage = "베이스 시간표로 초기화했습니다.";
    }

    [RelayCommand(CanExecute = nameof(CanSaveTimetable))]
    private void SaveTimetable()
    {
        if (!ValidateBeforeSave()) return;

        _workspace.SaveTimetable(SaveName.Trim(), _working);
        StatusMessage = $"'{SaveName.Trim()}' 저장 완료";
        SaveName = "";
    }

    private bool CanSaveTimetable() => !string.IsNullOrWhiteSpace(SaveName) && BaseSolution != null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportXlsx(string path)
    {
        var rows = _working.Select(a => new TimetableAssignmentRow(a.CourseId, a.Day, a.Period, a.RoomId)).ToList();
        var name = string.IsNullOrWhiteSpace(SaveName) ? "시간표" : SaveName.Trim();
        FormattedTimetableExporter.Export(name, rows, _workspace.ExpandedCourses, _workspace.Professors, path);
    }

    private bool CanExport() => BaseSolution != null;

    private bool ValidateBeforeSave()
    {
        RefreshConflicts();
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
        Rerender();

        PushUndoSnapshot(snapshot);
        SelectedDay = targetDay;
        SelectedPeriod = targetPeriod;
        _selectedGridCell = FindGridCellKey(movedCourseId, targetDay, targetPeriod, targetGrade)
            ?? new UnifiedCellKey(targetDay, targetPeriod, targetGrade, targetSubColumnIdx);
        OnPropertyChanged(nameof(SelectedSlotLabel));
        NotifySelectionDetailChanged();
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(SelectedAssignment, targetDay, targetPeriod));
        var removedLinks = RemoveInvalidCrossesAfterMove();
        var warning = GetSoftWarning(targetDay, targetPeriod);
        StatusMessage = removedLinks > 0
            ? $"{movedCourseId} 이동 완료 — {DayName(targetDay)} {targetPeriod}교시. 수업 식별 또는 학년 조건 변경으로 크로스 관계가 해제되었습니다."
            : warning == null
            ? $"{movedCourseId} 이동 완료 — {DayName(targetDay)} {targetPeriod}교시"
            : $"{movedCourseId} 이동 완료 — {DayName(targetDay)} {targetPeriod}교시. {warning}";
        return true;
    }

    private bool TryForceMoveSelectedTo(
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        IReadOnlyList<string> blockedReasons)
    {
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null) return false;
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
        Rerender();

        PushUndoSnapshot(snapshot);
        SelectedDay = targetDay;
        SelectedPeriod = targetPeriod;
        _selectedGridCell = FindGridCellKey(movedCourseId, targetDay, targetPeriod, targetGrade)
            ?? new UnifiedCellKey(targetDay, targetPeriod, targetGrade, targetSubColumnIdx);
        OnPropertyChanged(nameof(SelectedSlotLabel));
        NotifySelectionDetailChanged();
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(SelectedAssignment, targetDay, targetPeriod));
        var removedLinks = RemoveInvalidCrossesAfterMove();
        var errorCount = Conflicts.Count(c => c.Severity == ConflictSeverity.Error);
        var warningCount = Conflicts.Count(c => c.Severity == ConflictSeverity.Warning);
        StatusMessage = $"제약조건 위반 상태로 강제 이동했습니다. Error {errorCount}건, Warning {warningCount}건이 남아 있습니다. Error가 남아 있으면 저장할 수 없습니다."
            + (removedLinks > 0 ? " 수업 식별 또는 학년 조건 변경으로 크로스 관계가 해제되었습니다." : "");
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

        var course = _workspace.ExpandedCourses.First(c => c.Id == assignment.CourseId);
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
                .Select(a => _workspace.ExpandedCourses.FirstOrDefault(c => c.Id == a.CourseId))
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
            return "이동은 가능하지만 주의가 필요합니다: 월요일 오전은 권장되지 않는 시간대입니다.";
        if (day == 4 && period is >= 6 and <= 9)
            return "이동은 가능하지만 주의가 필요합니다: 금요일 오후는 권장되지 않는 시간대입니다.";
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
        var occupiedCells = Grid.Cells
            .Where(c => c.Day == targetDay
                && c.Grade == targetGrade
                && c.SubColumnIdx == targetSubColumnIdx
                && targetPeriods.Any(p => p >= c.Period && p < c.Period + c.Assignment.RowSpan))
            .Where(c => !IsSameMovingCell(c, assignment, sourceDay, sourcePeriod))
            .ToList();

        var blockedCourseIds = occupiedCells
            .Where(c => !CanShareSlotByCross(assignment, c.Assignment))
            .Select(c => c.Assignment.CourseId)
            .ToHashSet();

        return _working
            .Where(a => blockedCourseIds.Contains(a.CourseId))
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

    private bool TryCommitChange(ManualEditSnapshot snapshot, string successMessage)
    {
        Rerender();
        var baselineKeys = ConflictKeys(DetectConflicts(snapshot.Assignments));
        var newOnes = Conflicts.Where(c => !baselineKeys.Contains(KeyOf(c))).ToList();

        if (newOnes.Count > 0)
        {
            bool proceed = _dialog.ConfirmDespiteConflicts(newOnes);
            if (!proceed)
            {
                RestoreSnapshot(snapshot);
                Rerender();
                StatusMessage = $"변경 취소됨 — {ToUserReason(newOnes[0])}";
                return false;
            }
        }

        PushUndoSnapshot(snapshot);
        StatusMessage = successMessage
            + (newOnes.Count > 0 ? $" — 신규 위반 {newOnes.Count}건 수용" : "");
        return true;
    }

    private void Rerender()
    {
        Grid.SetCrossParallelOrder(BuildCrossParallelOrder());
        Grid.Render(_working, _workspace.ExpandedCourses);
        Grid.SetCrossLinkLabels(BuildCrossLinkLabels());
        RefreshConflicts();
    }

    private void RefreshConflicts()
    {
        Conflicts.Clear();
        foreach (var c in DetectConflicts(_working))
            Conflicts.Add(c);
    }

    private IReadOnlyList<ConflictItem> DetectConflicts(IReadOnlyList<SolutionAssignment> assignment) =>
        ConflictDetector.Detect(
            assignment,
            _workspace.ExpandedCourses,
            _workspace.Professors,
            _workingCrossGroups,
            IsManualGradeOverlapAllowed);

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
        var existingLink = GetCrossLinkForCourse(selected.CourseId);
        if (existingLink != null)
            return TryReplaceCrossAndMove(selected, target, day, targetPeriod, targetGrade, targetSubColumnIdx, existingLink, out reason);
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
        if (IsAlreadyCrossed(selected, target))
        {
            reason = "이미 크로스가 설정된 수업입니다.";
            return false;
        }
        if (IsCourseAlreadyCrossed(selected.CourseId) || IsCourseAlreadyCrossed(target.CourseId))
        {
            reason = "이미 다른 수업과 크로스된 과목입니다. 크로스는 두 과목끼리만 설정할 수 있습니다.";
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
            selectedPeriod,
            selected.RowSpan,
            targetPeriod,
            target.RowSpan);
        var snapshot = CaptureSnapshot();
        _workingCrossLinks.Add(provisionalLink);

        if (!TryMoveAssignment(
                selected,
                SelectedDay!.Value,
                selectedPeriod,
                day,
                targetPeriod,
                targetGrade,
                targetSubColumnIdx,
                out var candidate,
                out var moveReasons))
        {
            _workingCrossLinks.Remove(provisionalLink);
            reason = moveReasons.FirstOrDefault() ?? "크로스 이동을 완료할 수 없습니다.";
            return false;
        }

        _working = candidate;
        Rerender();
        PushUndoSnapshot(snapshot);
        SelectedDay = day;
        SelectedPeriod = targetPeriod;
        _selectedGridCell = FindGridCellKey(selected.CourseId, day, targetPeriod, targetGrade)
            ?? new UnifiedCellKey(day, targetPeriod, targetGrade, targetSubColumnIdx);
        OnPropertyChanged(nameof(SelectedSlotLabel));
        NotifySelectionDetailChanged();
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(selected, day, targetPeriod));
        StatusMessage = $"크로스가 설정되었습니다: {selected.CourseName} ({selected.CourseId}) ↔ "
            + $"{target.CourseName} ({target.CourseId})";
        reason = "";
        return true;
    }

    private bool TryReplaceCrossAndMove(
        CellAssignment selected,
        CellAssignment target,
        int day,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        ManualCrossLink existingLink,
        out string reason)
    {
        if (SelectedPeriod is not int selectedPeriod)
        {
            reason = "선택한 수업의 시간을 확인할 수 없습니다.";
            return false;
        }
        if (existingLink.MatchesPair(selected.CourseId, target.CourseId))
        {
            reason = "이미 크로스가 설정된 수업입니다.";
            return false;
        }
        if (IsCourseAlreadyCrossedExcept(target.CourseId, existingLink))
        {
            reason = "이미 다른 수업과 크로스된 과목입니다. 크로스는 두 과목끼리만 설정할 수 있습니다.";
            return false;
        }

        var reasons = ValidateCrossMoveWithProvisionalLink(
            selected, target, day, targetPeriod, targetGrade, targetSubColumnIdx, existingLink);
        if (reasons.Count > 0)
        {
            reason = reasons[0];
            return false;
        }

        var snapshot = CaptureSnapshot();
        var crossSnapshot = _workingCrossLinks.ToList();
        var workingSnapshot = _working.ToList();
        var replacementLink = new ManualCrossLink(
            selected.CourseId,
            target.CourseId,
            day,
            selectedPeriod,
            selected.RowSpan,
            targetPeriod,
            target.RowSpan);

        _workingCrossLinks.Remove(existingLink);
        _workingCrossLinks.Add(replacementLink);
        if (!TryMoveAssignment(
                selected,
                SelectedDay!.Value,
                selectedPeriod,
                day,
                targetPeriod,
                targetGrade,
                targetSubColumnIdx,
                out var candidate,
                out var moveReasons))
        {
            RestoreCrossLinks(crossSnapshot);
            _working = workingSnapshot;
            reason = moveReasons.FirstOrDefault() ?? "크로스 교체를 완료할 수 없습니다.";
            return false;
        }

        _working = candidate;
        Rerender();
        PushUndoSnapshot(snapshot);
        SelectedDay = day;
        SelectedPeriod = targetPeriod;
        _selectedGridCell = FindGridCellKey(selected.CourseId, day, targetPeriod, targetGrade)
            ?? new UnifiedCellKey(day, targetPeriod, targetGrade, targetSubColumnIdx);
        OnPropertyChanged(nameof(SelectedSlotLabel));
        NotifySelectionDetailChanged();
        Grid.SetEditState(_selectedGridCell, BuildMoveStates(selected, day, targetPeriod));
        StatusMessage = $"크로스가 변경되었습니다: {selected.CourseName} ({selected.CourseId}) ↔ "
            + $"{target.CourseName} ({target.CourseId})";
        reason = "";
        return true;
    }

    private bool IsAlreadyCrossed(CellAssignment left, CellAssignment right) =>
        _workingCrossLinks.Any(l => l.MatchesPair(left.CourseId, right.CourseId));

    private bool IsCourseAlreadyCrossed(string courseId) =>
        _workingCrossLinks.Any(l => l.CourseIdA == courseId || l.CourseIdB == courseId);

    private bool IsCourseAlreadyCrossedExcept(string courseId, ManualCrossLink? ignoredLink) =>
        _workingCrossLinks.Any(l => !ReferenceEquals(l, ignoredLink)
            && (l.CourseIdA == courseId || l.CourseIdB == courseId));

    private ManualCrossLink? GetCrossLinkForCourse(string courseId) =>
        _workingCrossLinks.FirstOrDefault(l => l.CourseIdA == courseId || l.CourseIdB == courseId);

    private bool IsCrossPair(CellAssignment left, CellAssignment right) =>
        left.Grade == right.Grade && IsAlreadyCrossed(left, right);

    private bool CanShareSlotByCross(CellAssignment left, CellAssignment right)
    {
        if (!IsCrossPair(left, right)) return false;
        var rightCourse = _workspace.ExpandedCourses.FirstOrDefault(c => c.Id == right.CourseId);
        return rightCourse != null && !rightCourse.IsFixed;
    }

    private bool IsManualGradeOverlapAllowed(string left, string right, int day, int period) =>
        _workingCrossLinks.Any(l => l.Covers(left, right, day, period));

    private int RemoveInvalidCrossesAfterMove()
    {
        var before = _workingCrossLinks.Count;
        _workingCrossLinks.RemoveAll(link => !IsCrossLinkStillValid(link));
        if (before != _workingCrossLinks.Count)
        {
            Grid.SetCrossLinkLabels(BuildCrossLinkLabels());
            NotifySelectionDetailChanged();
        }
        return before - _workingCrossLinks.Count;
    }

    private IReadOnlyList<string> ValidateCrossMoveWithProvisionalLink(
        CellAssignment selected,
        CellAssignment target,
        int day,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx,
        ManualCrossLink? replacedLink)
    {
        if (SelectedPeriod is not int selectedPeriod)
            return new[] { "선택한 수업의 시간을 확인할 수 없습니다." };

        var snapshot = _workingCrossLinks.ToList();
        var provisional = new ManualCrossLink(
            selected.CourseId,
            target.CourseId,
            day,
            selectedPeriod,
            selected.RowSpan,
            targetPeriod,
            target.RowSpan);
        if (replacedLink != null)
            _workingCrossLinks.Remove(replacedLink);
        _workingCrossLinks.Add(provisional);
        var reasons = ValidateCrossMove(selected, target, day, targetPeriod, targetGrade, targetSubColumnIdx);
        RestoreCrossLinks(snapshot);
        return reasons;
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
                l.RowSpanB))
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

    private void NotifyUndoRedoCanExecuteChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        UndoLastMoveCommand.NotifyCanExecuteChanged();
    }

    private bool IsCrossLinkStillValid(ManualCrossLink link)
    {
        var c1 = _workspace.ExpandedCourses.FirstOrDefault(c => c.Id == link.CourseIdA);
        var c2 = _workspace.ExpandedCourses.FirstOrDefault(c => c.Id == link.CourseIdB);
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
            AddLabel(link.CourseIdA, link.CourseIdB);
            AddLabel(link.CourseIdB, link.CourseIdA);
        }
        return labels.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value));

        void AddLabel(string sourceId, string targetId)
        {
            if (!labels.TryGetValue(sourceId, out var list))
                labels[sourceId] = list = new List<string>();
            list.Add(CourseDisplayName(targetId));
        }
    }

    private string FormatCrossLink(ManualCrossLink link) =>
        $"{CourseDisplayName(link.CourseIdA)} ↔ {CourseDisplayName(link.CourseIdB)}";

    private string CourseDisplayName(string id) =>
        _workspace.ExpandedCourses.FirstOrDefault(c => c.Id == id)?.Name ?? id;

    private IReadOnlyDictionary<string, int> BuildCrossParallelOrder()
    {
        var order = new Dictionary<string, int>();
        foreach (var link in _workingCrossLinks)
        {
            order[link.CourseIdB] = 0;
            order[link.CourseIdA] = 1;
        }
        return order;
    }

    private IReadOnlyList<string> ValidateCrossMove(
        CellAssignment selected,
        CellAssignment target,
        int targetDay,
        int targetPeriod,
        int targetGrade,
        int targetSubColumnIdx)
    {
        if (targetGrade != selected.Grade || target.Grade != selected.Grade)
            return new[] { "같은 학년 수업끼리만 크로스를 만들 수 있습니다." };

        var targetPeriods = GetOccupiedPeriods(targetPeriod, selected.RowSpan);
        var assignmentsAtTarget = Grid.Cells
            .Where(c => c.Day == targetDay
                && c.Grade == targetGrade
                && targetPeriods.Any(p => p >= c.Period && p < c.Period + c.Assignment.RowSpan))
            .Where(c => c.Assignment.CourseId != selected.CourseId)
            .ToList();
        var distinctTargetCourses = assignmentsAtTarget
            .Select(c => c.Assignment.CourseId)
            .Distinct()
            .ToList();
        if (distinctTargetCourses.Any(cid => cid != target.CourseId))
            return new[] { "3개 이상의 과목 크로스는 허용되지 않습니다." };

        var selectedDay = SelectedDay ?? targetDay;
        var selectedPeriod = SelectedPeriod ?? targetPeriod;
        var provisional = new ManualCrossLink(
            selected.CourseId,
            target.CourseId,
            targetDay,
            selectedPeriod,
            selected.RowSpan,
            targetPeriod,
            target.RowSpan);
        _workingCrossLinks.Add(provisional);
        var reasons = ValidateManualMove(
            selected,
            selectedDay,
            selectedPeriod,
            targetDay,
            targetPeriod,
            targetGrade,
            targetSubColumnIdx);
        _workingCrossLinks.Remove(provisional);
        return reasons;
    }

    private UnifiedCellKey? FindGridCellKey(string courseId, int day, int period, int grade)
    {
        var cell = Grid.Cells.FirstOrDefault(c =>
            c.Day == day
            && c.Period == period
            && c.Grade == grade
            && c.Assignment.CourseId == courseId);
        return cell == null ? null : new UnifiedCellKey(cell.Day, cell.Period, cell.Grade, cell.SubColumnIdx);
    }

    private bool CanApplyRoomChange() =>
        SelectedAssignment != null
        && !string.IsNullOrEmpty(NewRoomId)
        && SelectedAssignment.Rooms.FirstOrDefault() != NewRoomId;

    private bool CanClearSelection() => SelectedAssignment != null;

    private bool CanUndo() => _undoStack.Count > 0;

    private bool CanRedo() => _redoStack.Count > 0;

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
