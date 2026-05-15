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

    partial void OnSelectedAssignmentChanged(CellAssignment? value) =>
        OnPropertyChanged(nameof(HasSelection));

    public ManualEditViewModel(WorkspaceService workspace, IConflictDialogService dialog)
    {
        _workspace = workspace;
        _dialog = dialog;
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

    public void HandleCellClick(int day, int period, CellAssignment? assignment)
    {
        if (SelectedAssignment != null && assignment == null)
        {
            TryMoveSelectedTo(day, period);
            return;
        }

        SelectCell(day, period, assignment);
    }

    public void SelectCell(int day, int period, CellAssignment? assignment)
    {
        SelectedDay = day;
        SelectedPeriod = period;
        SelectedAssignment = assignment;
        OnPropertyChanged(nameof(SelectedSlotLabel));

        if (assignment != null)
        {
            NewRoomId = assignment.Rooms.FirstOrDefault() ?? "";
            StatusMessage = $"선택: {assignment.CourseName} ({assignment.CourseId}) — "
                + $"{DayName(day)} {period}교시";
            Grid.SetEditState((day, period), BuildMoveStates(assignment, day, period));
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
        Grid.SetEditState((day, period), BuildMoveStates(SelectedAssignment, day, period));
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

    private void TryMoveSelectedTo(int targetDay, int targetPeriod)
    {
        if (SelectedAssignment == null || SelectedDay == null || SelectedPeriod == null) return;

        var state = BuildMoveStates(SelectedAssignment, SelectedDay.Value, SelectedPeriod.Value)
            .GetValueOrDefault((targetDay, targetPeriod));
        if (state == null || !state.CanMove)
        {
            StatusMessage = state?.Reason ?? "이동할 수 없는 칸입니다.";
            return;
        }

        var snapshot = _working.ToList();
        var moved = MoveRun(
            SelectedAssignment.CourseId,
            SelectedDay.Value,
            SelectedPeriod.Value,
            targetDay,
            targetPeriod,
            SelectedAssignment.RowSpan);

        if (!TryCommitChange(
                snapshot,
                $"{SelectedAssignment.CourseId} 이동: {SelectedSlotLabel} → {DayName(targetDay)} {targetPeriod}교시"))
            return;

        _undoSnapshot = snapshot;
        UndoLastMoveCommand.NotifyCanExecuteChanged();
        ClearSelectionCore();
        StatusMessage = $"{moved}건 이동 완료 — {DayName(targetDay)} {targetPeriod}교시";
    }

    private int MoveRun(string cid, int sourceDay, int sourcePeriod, int targetDay, int targetPeriod, int rowSpan)
    {
        var changed = 0;
        for (int i = 0; i < _working.Count; i++)
        {
            var a = _working[i];
            if (a.CourseId != cid || a.Day != sourceDay) continue;
            if (a.Period < sourcePeriod || a.Period >= sourcePeriod + rowSpan) continue;
            int offset = a.Period - sourcePeriod;
            _working[i] = new SolutionAssignment(cid, targetDay, targetPeriod + offset, a.RoomId);
            changed++;
        }
        return changed;
    }

    private IReadOnlyDictionary<(int Day, int Period), EditCellState> BuildMoveStates(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod)
    {
        var states = new Dictionary<(int Day, int Period), EditCellState>();
        foreach (var d in Enumerable.Range(0, Constants.Days))
            foreach (var p in Constants.Periods)
            {
                if (d == sourceDay && p == sourcePeriod)
                {
                    states[(d, p)] = new EditCellState(false, "현재 위치입니다.");
                    continue;
                }

                var reasons = ValidateMove(assignment, sourceDay, sourcePeriod, d, p);
                states[(d, p)] = reasons.Count == 0
                    ? new EditCellState(true, "이동 가능")
                    : new EditCellState(false, reasons[0]);
            }
        return states;
    }

    private IReadOnlyList<string> ValidateMove(
        CellAssignment assignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        int targetPeriod)
    {
        var course = _workspace.Courses.First(c => c.Id == assignment.CourseId);
        if (course.IsFixed)
            return new[] { "HC-13 고정 시간표 보존 위반: 고정된 수업은 이동할 수 없습니다." };

        var targetPeriods = Enumerable.Range(targetPeriod, assignment.RowSpan).ToList();
        if (targetPeriods.Any(p => !Constants.Periods.Contains(p)))
            return new[] { "수업 블록이 시간표 범위를 벗어납니다." };
        if (targetPeriods.Contains(Constants.LunchPeriod))
            return new[] { "HC-12 점심시간 보장 위반: 점심시간에는 수업을 배정할 수 없습니다." };
        if (assignment.RowSpan == 2 && !Constants.Len2StartPeriods.Contains(targetPeriod))
            return new[] { "HC-19 블록 시작 교시 위반: 2시간 수업은 1, 3, 6, 8교시에만 시작할 수 있습니다." };

        var candidate = _working.ToList();
        for (int i = 0; i < candidate.Count; i++)
        {
            var a = candidate[i];
            if (a.CourseId != assignment.CourseId || a.Day != sourceDay) continue;
            if (a.Period < sourcePeriod || a.Period >= sourcePeriod + assignment.RowSpan) continue;
            candidate[i] = new SolutionAssignment(
                a.CourseId,
                targetDay,
                targetPeriod + (a.Period - sourcePeriod),
                a.RoomId);
        }

        var baselineKeys = ConflictKeys(DetectConflicts(_working));
        var newConflicts = DetectConflicts(candidate)
            .Where(c => !baselineKeys.Contains(KeyOf(c)))
            .ToList();
        return newConflicts.Select(ToUserReason).ToList();
    }

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
        NewRoomId = "";
        OnPropertyChanged(nameof(SelectedSlotLabel));
        Grid.ClearEditState();
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
        ConflictType.GradeConflict => $"HC-11 학년 중복 금지 위반: {c.Description}",
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
