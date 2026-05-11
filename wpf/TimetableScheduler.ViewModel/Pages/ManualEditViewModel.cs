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

    public override string Title => "수동 편집";

    public UnifiedTimetableViewModel Grid { get; } = new();

    public ObservableCollection<ConflictItem> Conflicts { get; } = new();

    [ObservableProperty]
    private RankedSolution? baseSolution;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyRoomChangeCommand))]
    private CellAssignment? selectedAssignment;

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

    public ManualEditViewModel(WorkspaceService workspace, IConflictDialogService dialog)
    {
        _workspace = workspace;
        _dialog = dialog;
    }

    public void LoadFromSolution(RankedSolution solution)
    {
        BaseSolution = solution;
        _working = solution.Assignment.ToList();
        Rerender();
        SelectedAssignment = null;
        SelectedDay = null;
        SelectedPeriod = null;
        NewRoomId = "";
        StatusMessage = "셀을 클릭해서 편집할 과목을 선택하세요.";
    }

    public void SelectCell(int day, int period, CellAssignment? assignment)
    {
        SelectedDay = day;
        SelectedPeriod = period;
        SelectedAssignment = assignment;
        if (assignment != null)
        {
            NewRoomId = assignment.Rooms.FirstOrDefault() ?? "";
            StatusMessage = $"선택: {assignment.CourseName} ({assignment.CourseId}) — "
                + $"{DayName(day)} {period}교시";
        }
        else
        {
            NewRoomId = "";
            StatusMessage = $"{DayName(day)} {period}교시 — 비어있음";
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

        var snapshot = _working.ToList();  // for revert
        var existingConflictKeys = ConflictKeys(Conflicts);

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

        Rerender();

        // Find conflicts introduced by this change (not already there)
        var newOnes = Conflicts.Where(c => !existingConflictKeys.Contains(KeyOf(c))).ToList();

        if (newOnes.Count > 0)
        {
            bool proceed = _dialog.ConfirmDespiteConflicts(newOnes);
            if (!proceed)
            {
                _working = snapshot;
                Rerender();
                StatusMessage = $"변경 취소됨 — 위반 {newOnes.Count}건 발견, 사용자가 롤백 선택";
                return;
            }
        }

        StatusMessage = $"{cid}의 강의실 {oldRoomId} → {NewRoomId} (변경 {changed}건"
            + (newOnes.Count > 0 ? $", 신규 위반 {newOnes.Count}건 수용" : "")
            + ")";
        SelectedAssignment = SelectedAssignment with { Rooms = new List<string> { NewRoomId } };
    }

    private bool CanApplyRoomChange() =>
        SelectedAssignment != null
        && !string.IsNullOrEmpty(NewRoomId)
        && SelectedAssignment.Rooms.FirstOrDefault() != NewRoomId;

    [RelayCommand]
    private void Reset()
    {
        if (BaseSolution != null) LoadFromSolution(BaseSolution);
    }

    private void Rerender()
    {
        Grid.Render(_working, _workspace.Courses);

        Conflicts.Clear();
        foreach (var c in ConflictDetector.Detect(_working, _workspace.Courses, _workspace.Professors))
            Conflicts.Add(c);
    }

    private static HashSet<string> ConflictKeys(IEnumerable<ConflictItem> conflicts) =>
        conflicts.Select(KeyOf).ToHashSet();

    private static string KeyOf(ConflictItem c) =>
        $"{c.Type}|{c.Day}|{c.Period}|{c.Description}";

    private static string DayName(int d) => d switch
    {
        0 => "월", 1 => "화", 2 => "수", 3 => "목", 4 => "금",
        _ => "?"
    };
}
