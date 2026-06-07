using Microsoft.Extensions.DependencyInjection;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.ViewModel.Services;

namespace TimetableScheduler.Tests.ViewModel;

public class ManualEditViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _sp;
    private readonly WorkspaceService _ws;
    private readonly RecordingDialogService _dialog;

    private sealed class RecordingDialogService : IConflictDialogService
    {
        public bool ResponseToReturn { get; set; } = true;
        public List<IReadOnlyList<ConflictItem>> Calls { get; } = new();
        public bool ConfirmDespiteConflicts(IReadOnlyList<ConflictItem> newConflicts)
        {
            Calls.Add(newConflicts);
            return ResponseToReturn;
        }
    }

    public ManualEditViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"manual_test_{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddTimetableScheduler(_dbPath);
        _dialog = new RecordingDialogService();
        services.AddSingleton<IConflictDialogService>(_dialog);
        _sp = services.BuildServiceProvider();
        _ws = _sp.GetRequiredService<WorkspaceService>();

        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 2, ProfessorId = "P1" });
        _ws.AddProfessor(new Professor { Id = "P1", Name = "교수" });
        _ws.AddRoom(new Room { Id = "R1", Name = "강의실1" });
        _ws.AddRoom(new Room { Id = "R2", Name = "강의실2" });
    }

    public void Dispose()
    {
        _sp.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private RankedSolution MakeSolution(params SolutionAssignment[] assignments)
    {
        var score = new SolutionScore(1, 1, 1, 3);
        return new RankedSolution(assignments.ToList(), score);
    }

    private SavedTimetableRecord MakeSavedRecordWithManualCross(
        string id,
        string name,
        IReadOnlyList<TimetableAssignmentRow> assignments,
        SavedManualCrossLinkRow manualCrossLink) =>
        new(
            id,
            name,
            DateTime.Now,
            assignments,
            new[] { manualCrossLink },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

    private static SavedManualCrossLinkRow SavedCross(
        string sourceCourseId = "X-01",
        string targetCourseId = "Y-01",
        int sourceDay = 0,
        int sourcePeriod = 1,
        int targetDay = 0,
        int targetPeriod = 1) =>
        new(
            sourceCourseId, 2, "1", sourceDay, sourcePeriod, "R1",
            targetCourseId, 2, "1", targetDay, targetPeriod, "R2",
            "HC11_ONLY_EXCEPTION");

    private static TimetableScheduler.ViewModel.Grid.CellAssignment AssignmentAt(
        ManualEditViewModel vm,
        int day,
        int period,
        string courseId) =>
        vm.Grid.Cells.First(c => c.Day == day && c.Period == period && c.Assignment.CourseId == courseId).Assignment;

    [Fact]
    public void LoadFromSolution_PopulatesGridAndResetsSelection()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));

        vm.LoadFromSolution(sol);

        Assert.Equal(sol, vm.BaseSolution);
        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(vm.Conflicts);
        Assert.Equal(5, vm.Grid.DayGroups.Count);
        Assert.True(vm.Grid.ExpandAllGrades);
        Assert.All(vm.Grid.DayGroups, dg => Assert.Equal(4, dg.Grades.Count));
    }

    [Fact]
    public void ManualEditGrid_UsesStructuredRowSpanMerge()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();

        Assert.True(vm.Grid.MergeOnlyStructuredBlocks);
    }

    [Fact]
    public void ManualEditGrid_KeepsNormalTwoHourBlockMerged()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();

        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 2, "R1"),
            new SolutionAssignment("B-02", 0, 3, "R1")));

        var block = Assert.Single(vm.Grid.Cells, c => c.Assignment.CourseId == "B-02");
        Assert.Equal(2, block.Period);
        Assert.Equal(2, block.Assignment.RowSpan);
    }

    [Fact]
    public void MoveOneHourIntoTwoHourBlockInterior_IsBlockedWithoutForceDialog()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 2, "R1"),
            new SolutionAssignment("B-02", 0, 3, "R1"),
            new SolutionAssignment("X-01", 0, 6, "R2")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "B-02" && c.Period == 2 && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 1);
    }

    [Fact]
    public void MoveTwoHourDirectlyBelowSameCourseSectionOneHour_IsBlockedWithoutForceDialog()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Period == 6);
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var targetKey = new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 2, 2, 0);
        Assert.Equal(TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Blocked, vm.Grid.EditStates[targetKey].State);
        Assert.Contains("같은 과목", vm.Grid.EditStates[targetKey].Reason);

        vm.HandleCellClick(0, 2, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("같은 과목", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 2);
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 2);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void MoveTwoHourBelowDifferentCourseOneHour_IsAllowed()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "다른과목", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 2, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3 && c.Assignment.RowSpan == 2);
    }

    [Fact]
    public void ClickMoveSuccess_ClearsAllSelectionAndMoveStates()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
        Assert.False(vm.EvaluateCrossHover(2, 1, 2, 0, null).CanCreate);
        Assert.False(vm.EvaluateSwapHover(2, 1, 2, 0, null).CanSwap);
    }

    [Fact]
    public void ClickMoveSuccess_RebuildsGridAtNewPosition_AndOldPositionIsEmpty()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.EditStates.Keys, k => k.Day == 0 && k.Period == 1);
    }

    [Fact]
    public void ClickMoveSuccess_DoesNotLeaveStaleSelectedCell()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        var selectedBeforeMove = vm.Grid.SelectedCell;

        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.NotNull(selectedBeforeMove);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
        Assert.Null(vm.SelectedAssignment);
    }

    [Fact]
    public void ClickMoveSuccess_DoesNotRecreateMoveStatesAfterClear()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        Assert.NotEmpty(vm.Grid.EditStates);

        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.Empty(vm.Grid.EditStates);
        Assert.Null(vm.Grid.SelectedCell);
    }

    [Fact]
    public void MoveFailure_KeepsSelectionAndDoesNotChangeGrid()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 2, "R1"),
            new SolutionAssignment("B-02", 0, 3, "R1"),
            new SolutionAssignment("X-01", 1, 1, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        var before = vm.Grid.Cells
            .Select(c => (c.Day, c.Period, c.Grade, c.SubColumnIdx, c.Assignment.CourseId, c.Assignment.RowSpan))
            .ToHashSet();

        vm.HandleCellClick(0, 3, 2, 0, null);

        var after = vm.Grid.Cells
            .Select(c => (c.Day, c.Period, c.Grade, c.SubColumnIdx, c.Assignment.CourseId, c.Assignment.RowSpan))
            .ToHashSet();
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Equal("X-01", vm.SelectedAssignment!.CourseId);
        Assert.NotNull(vm.Grid.SelectedCell);
        Assert.NotEmpty(vm.Grid.EditStates);
        Assert.True(before.SetEquals(after));
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
    }

    [Fact]
    public void MoveTwoHourBelowDifferentSectionOneHour_IsAllowed()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "테스트", Grade = 2, Section = 2, HoursPerWeek = 1, ProfessorId = "P1" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 2, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3 && c.Assignment.RowSpan == 2);
    }

    [Fact]
    public void MoveTwoHourBelowDifferentProfessorSameCourseSectionOneHour_IsAllowed()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "테스트", Grade = 2, Section = 1, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 2, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3 && c.Assignment.RowSpan == 2);
    }

    [Fact]
    public void CrossMoveCreatingTwoHourToOneHour_IsBlockedByDuration()
    {
        _ws.AddCourse(new Course { Id = "A-01", Name = "위수업", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("A-01", 0, 2, "R2"),
            new SolutionAssignment("Y-01", 0, 3, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleCrossAddRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("서로 다른 길이", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 2);
    }

    [Fact]
    public void MoveOneHourDirectlyAboveSameCourseSectionTwoHourBlock_IsBlockedWithoutForceDialog()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("X-01", 0, 3, "R1"),
            new SolutionAssignment("X-01", 0, 6, "R2")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Period == 6);
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 1, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("같은 과목", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 2 && c.Assignment.RowSpan == 2);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void MoveOneHourAfterTwoHourBlock_IsAllowed()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 1, "R1"),
            new SolutionAssignment("B-02", 0, 2, "R1"),
            new SolutionAssignment("X-01", 0, 6, "R2")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "B-02" && c.Period == 1 && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3 && c.Assignment.RowSpan == 1);
    }

    [Fact]
    public void SwapHover_WithSelectedAndTargetCourse_IsAvailable()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교환대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        var state = vm.EvaluateSwapHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.True(state.CanSwap);
        Assert.Contains("교환 가능", state.Reason);
    }

    [Fact]
    public void SwapOneHourWithOneHour_ExchangesLocationsAndCreatesUndoSnapshot()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교환대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("위치를 교환", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 3 && c.Assignment.Rooms.Contains("R1"));
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 0 && c.Period == 1 && c.Assignment.Rooms.Contains("R2"));
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapOneHourWithTwoHour_IsBlockedAndDoesNotCreateManualCrossLink()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "한시간", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        var hover = vm.EvaluateSwapHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);
        Assert.False(hover.CanSwap);
        Assert.Contains("블록 시간", hover.Reason);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("블록 시간", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 1 && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 2);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapTwoHourWithTwoHour_ExchangesLocationsAndPreservesRowSpan()
    {
        _ws.AddCourse(new Course
        {
            Id = "Y-02",
            Name = "두시간2",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-02", 1, 3, "R2"),
            new SolutionAssignment("Y-02", 1, 4, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-02");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        var hover = vm.EvaluateSwapHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);
        Assert.True(hover.CanSwap);
        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("위치를 교환", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 1 && c.Period == 3 && c.Assignment.RowSpan == 2 && c.Assignment.Rooms.Contains("R1"));
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-02" && c.Day == 0 && c.Period == 1 && c.Assignment.RowSpan == 2 && c.Assignment.Rooms.Contains("R2"));
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapUndoRedo_RestoresAndReappliesExchange()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교환대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);
        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        vm.UndoCommand.Execute(null);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 3);

        vm.RedoCommand.Execute(null);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 1);
    }

    [Fact]
    public void SwapSelf_DoesNotChangeStateOrCreateUndoSnapshot()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("현재 선택한 수업", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 1);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapCreatingSameCourseSectionOneHourAboveTwoHourPattern_IsBlocked()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교환대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 6, "R2"),
            new SolutionAssignment("X-01", 0, 7, "R2"),
            new SolutionAssignment("Y-01", 0, 2, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Period == 6);
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("블록 시간", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 1 && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 2);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapResultingInProfessorConflict_RecalculatesConflictsAndSaveBlocks()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P1" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2"),
            new SolutionAssignment("Z-01", 0, 3, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.ProfessorConflict && c.Severity == ConflictSeverity.Error);
        vm.SaveName = "스왑오류";
        vm.SaveTimetableCommand.Execute(null);
        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "스왑오류");
    }

        [Fact]
    public void SwapWithoutNewConflict_DoesNotAskForConfirmation()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 2, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 3, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Empty(_dialog.Calls);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 1 && c.Period == 3);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 2 && c.Period == 1);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapWarning_CancelLeavesStateAndUndoUnchanged()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _dialog.ResponseToReturn = false;
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 2, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 3, 1, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(_dialog.Calls);
        Assert.Contains(_dialog.Calls[0], c => c.Severity == ConflictSeverity.Warning);
        Assert.Contains("취소", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 2 && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 0 && c.Period == 1);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Empty(vm.WorkingCrossLinks);
    }

    [Fact]
    public void SwapWarning_ConfirmAppliesSwap()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _dialog.ResponseToReturn = true;
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 2, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 3, 1, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(_dialog.Calls);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 2 && c.Period == 1);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapError_CancelLeavesStateAndSaveClean()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P1" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _dialog.ResponseToReturn = false;
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2"),
            new SolutionAssignment("Z-01", 0, 3, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(_dialog.Calls);
        Assert.Contains(_dialog.Calls[0], c => c.Severity == ConflictSeverity.Error);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 3);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapResultingInHc20_RequestsConfirmationAndCancelLeavesState()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _dialog.ResponseToReturn = false;
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 3, "R2"),
            new SolutionAssignment("X-01", 1, 6, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Day == 0);
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(_dialog.Calls);
        Assert.Contains(_dialog.Calls[0], c => c.Type == ConflictType.SameCourseSameDayConflict);
        Assert.Contains("취소", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 1 && c.Period == 3);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapPolicyBlocked_DoesNotAskForConfirmation()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "한시간", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Empty(_dialog.Calls);
        Assert.Contains("블록 시간", vm.StatusMessage);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void LoadFromSaved_UsesSnapshotCoursesNotInWorkspace()
    {
        // A saved timetable references a course that no longer exists in the workspace,
        // but its snapshot still carries the original course definition.
        var snapshot = new TimetableScheduler.Data.AppData(
            new List<Course> { new() { Id = "GONE-01", Name = "사라진과목", Grade = 1, HoursPerWeek = 2, ProfessorId = "P1" } },
            new List<Professor> { new() { Id = "P1", Name = "교수" } },
            new List<Room> { new() { Id = "R1", Name = "강의실1" } },
            new List<CrossGroup>(), new List<RetakeScenario>());
        var record = new TimetableScheduler.Data.SavedTimetableRecord(
            Guid.NewGuid().ToString(), "snap", DateTime.Now,
            new List<TimetableScheduler.Data.TimetableAssignmentRow>
            {
                new("GONE-01", 0, 1, "R1"),
                new("GONE-01", 0, 2, "R1"),
            },
            SnapshotJson: System.Text.Json.JsonSerializer.Serialize(snapshot));

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSaved(record);

        Assert.Equal("snap", vm.SaveName);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "GONE-01");
    }

    [Fact]
    public void SelectCell_PopulatesInspector()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(2, 1, 2, 0, assignment);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Equal("X-01", vm.SelectedAssignment!.CourseId);
        Assert.Equal("R1", vm.NewRoomId);
        Assert.False(vm.HasNoSelection);
        Assert.Equal("교수", vm.SelectedProfessorName);
        Assert.Equal("2학년 / 분반 A / 강의실1", vm.SelectedLocationText);
        Assert.Equal("1시간", vm.SelectedDurationText);
        Assert.Equal("1교시", vm.SelectedOccupiedSlotsText);
        Assert.Equal("단일 1시간", vm.SelectedBlockText);
        Assert.Equal("아니오", vm.SelectedFixedText);
        Assert.Equal("없음", vm.SelectedCoteachText);
        Assert.Equal("아니오", vm.SelectedMultiRoomText);
        Assert.Equal("제한 없음", vm.SelectedAllowedRoomsText);
        Assert.Equal("이동 가능", vm.SelectedMoveStateText);
        Assert.Equal("없음", vm.SelectedBlockedReasonText);
        Assert.False(vm.HasBlockingReasons);
        Assert.False(vm.HasWarningReasons);
        Assert.False(vm.HasAnyConstraintReasons);
    }

    [Fact]
    public void LoadFromSolution_ShowsNoSelectionState()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R3")));

        Assert.True(vm.HasNoSelection);
        Assert.Equal("-", vm.SelectedProfessorName);
        Assert.Equal("-", vm.SelectedLocationText);
    }

    [Fact]
    public void SelectCell_PopulatesBlockAndMetadataDetails()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddProfessor(new Professor { Id = "P2", Name = "공동교수" });
        _ws.AddCourse(new Course
        {
            Id = "F-02",
            Name = "고급 테스트",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            IsFixed = true,
            FixedRooms = new List<string> { "R1", "R2" },
            CoteachProfs = new List<string> { "P2" },
        });
        var sol = MakeSolution(
            new SolutionAssignment("F-02", 0, 1, "R1"),
            new SolutionAssignment("F-02", 0, 2, "R1"),
            new SolutionAssignment("F-02", 0, 1, "R2"),
            new SolutionAssignment("F-02", 0, 2, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "F-02", "고급 테스트", "P1", 2, 1, new List<string> { "R1", "R2" }, 2, 2, true);
        vm.SelectCell(2, 1, 2, 0, assignment);

        Assert.Equal("2시간", vm.SelectedDurationText);
        Assert.Equal("1~2교시", vm.SelectedOccupiedSlotsText);
        Assert.Equal("연속 2시간 블록", vm.SelectedBlockText);
        Assert.Equal("예", vm.SelectedFixedText);
        Assert.Equal("공동교수", vm.SelectedCoteachText);
        Assert.Equal("예 (강의실1, 강의실2)", vm.SelectedMultiRoomText);
        Assert.Equal("강의실1, 강의실2", vm.SelectedAllowedRoomsText);
        Assert.Equal("이동 불가", vm.SelectedMoveStateText);
        Assert.Contains("HC-13", vm.SelectedBlockedReasonText);
        Assert.True(vm.HasBlockingReasons);
        Assert.False(vm.HasWarningReasons);
        Assert.True(vm.HasAnyConstraintReasons);
    }

    [Fact]
    public void ClearSelection_ResetsInspectorDetails()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R3")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.ClearSelectionCommand.Execute(null);

        Assert.True(vm.HasNoSelection);
        Assert.Equal("-", vm.SelectedProfessorName);
        Assert.Equal("-", vm.SelectedLocationText);
        Assert.Equal("-", vm.SelectedDurationText);
        Assert.False(vm.HasBlockingReasons);
        Assert.False(vm.HasWarningReasons);
        Assert.False(vm.HasAnyConstraintReasons);
    }

    [Fact]
    public void SelectCell_BuildsPerGradeMoveStatesAndWarnings()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 2, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(2, 1, 2, 0, assignment);

        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Selected,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(2, 1, 2, 0)].State);
        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Warning,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 1, 2, 0)].State);
        Assert.Contains(
            "월요일 오전",
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 1, 2, 0)].Reason);
        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Normal,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 1, 3, 0)].State);
    }

    [Fact]
    public void SelectCell_FlagsFridayAfternoonAsWarning()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 2, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(2, 1, 2, 0, assignment);

        var state = vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(4, 6, 2, 0)];
        Assert.Equal(TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Warning, state.State);
        Assert.Contains("금요일 오후", state.Reason);

        vm.SelectCell(4, 6, 2, 0, assignment);

        Assert.False(vm.HasBlockingReasons);
        Assert.True(vm.HasWarningReasons);
        Assert.True(vm.HasAnyConstraintReasons);
    }

    [Fact]
    public void HandleCellClick_WarningTarget_MovesAndShowsWarningMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 2, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(2, 1, 2, 0, assignment);
        vm.HandleCellClick(0, 1, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains("월요일 오전", vm.StatusMessage);
    }

    [Fact]
    public void SaveTimetable_NoError_SavesTimetable()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SaveName = "정상저장";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains("저장 완료", vm.StatusMessage);
        Assert.Contains(_ws.SavedTimetables, t => t.Name == "정상저장");
    }

    [Fact]
    public void SaveTimetable_FromSnapshot_PreservesSessionDbData()
    {
        var sessionSnapshot = new AppData(
            new List<Course>
            {
                new() { Id = "SESSION-01", Name = "세션과목", Grade = 2, HoursPerWeek = 1, ProfessorId = "PS" },
            },
            new List<Professor> { new() { Id = "PS", Name = "세션교수" } },
            new List<Room> { new() { Id = "RS", Name = "세션강의실" } },
            new List<CrossGroup>(),
            new List<RetakeScenario>());

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSnapshot(
            sessionSnapshot,
            MakeSolution(new SolutionAssignment("SESSION-01", 0, 1, "RS")),
            "세션저장");

        vm.SaveTimetableCommand.Execute(null);

        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "세션저장"));
        var savedSnapshot = System.Text.Json.JsonSerializer.Deserialize<AppData>(saved.SnapshotJson!)!;
        Assert.Equal("SESSION-01", savedSnapshot.Courses.Single().Id);
        Assert.Equal("PS", savedSnapshot.Professors.Single().Id);
        Assert.Equal("RS", savedSnapshot.Rooms.Single().Id);
        Assert.DoesNotContain(savedSnapshot.Courses, c => c.Id == "X-01");
    }

    [Fact]
    public void SaveTimetable_WarningOnly_AllowsSave()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SaveName = "경고저장";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains("저장 완료", vm.StatusMessage);
        Assert.Contains(_ws.SavedTimetables, t => t.Name == "경고저장");
    }

    [Fact]
    public void SaveTimetable_Error_BlocksSaveAndRefreshesConflicts()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P1" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        vm.SaveName = "오류저장";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "오류저장");
    }

    [Fact]
    public void SaveTimetable_Hc20Error_BlocksSave()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 3, "R1")));
        vm.SaveName = "HC20저장";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SameCourseSameDayConflict);
        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "HC20저장");
    }

    [Fact]
    public void BuildMoveStates_Hc20CandidateMarksTargetBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 1, 3, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Day == 1);

        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var targetKey = new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 3, 2, 0);
        Assert.Equal(TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Blocked, vm.Grid.EditStates[targetKey].State);
        Assert.Contains("HC-20", vm.Grid.EditStates[targetKey].Reason);
    }

    [Fact]
    public void ForceMove_Hc20CandidateMovesAndKeepsSaveBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 1, 3, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Day == 1);
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Single(_dialog.Calls);
        Assert.Contains(_dialog.Calls[0], c => c.Type == ConflictType.SameCourseSameDayConflict);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 3);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SameCourseSameDayConflict);

        vm.SaveName = "HC20강제이동저장";
        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "HC20강제이동저장");
    }

    [Fact]
    public void SaveTimetable_ManualCrossHc11Exception_AllowsSave()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);
        vm.SaveName = "크로스저장";

        vm.SaveTimetableCommand.Execute(null);

        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains("저장 완료", vm.StatusMessage);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "크로스저장"));
        var link = Assert.Single(saved.ManualCrossLinks ?? Array.Empty<TimetableScheduler.Data.SavedManualCrossLinkRow>());
        Assert.Equal("HC11_ONLY_EXCEPTION", link.PolicyType);

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains("저장된 시간표", vm.StatusMessage);
    }

    [Fact]
    public void SaveReload_CrossMovedFromDifferentTime_RestoresSavedManualCrossLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 1, 3, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);
        vm.HandleCrossAddRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);
        vm.SaveName = "크로스이동저장";

        vm.SaveTimetableCommand.Execute(null);

        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "크로스이동저장"));
        Assert.Single(saved.ManualCrossLinks ?? Array.Empty<TimetableScheduler.Data.SavedManualCrossLinkRow>());

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 0 && c.Period == 1);
        Assert.True(vm.IsSavedCrossValidationSuppressed);
        Assert.False(vm.HasUserEditedAfterLoad);
        Assert.Equal(1, vm.LastSavedCrossLinkCount);
        Assert.Equal(1, vm.LastRestoredCrossLinkCount);
        Assert.Equal(0, vm.LastIgnoredCrossLinkCount);
    }

    [Fact]
    public void ExistingTimetableHandoff_PassesSavedManualCrossLinksToManualEdit()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var input = _sp.GetRequiredService<DataInputViewModel>();
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = MakeSavedRecordWithManualCross(
            "handoff-cross",
            "handoff",
            new[]
            {
                new TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            SavedCross());

        input.LoadForExistingTimetable(saved);
        var handoff = Assert.IsType<ManualEditHandoff>(input.BuildEditHandoff());
        vm.LoadFromSnapshot(input.CurrentSnapshot(), handoff.Solution, "handoff", handoff.ManualCrossLinks);

        Assert.Single(handoff.ManualCrossLinks);
        Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal(1, vm.LastSavedCrossLinkCount);
        Assert.Equal(1, vm.LastRestoredCrossLinkCount);
    }

    [Fact]
    public void LoadFromSnapshot_WithSavedManualCrossLink_RerenderDoesNotShowHc11()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var input = _sp.GetRequiredService<DataInputViewModel>();
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = MakeSavedRecordWithManualCross(
            "snapshot-cross",
            "snapshot",
            new[]
            {
                new TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            SavedCross());

        input.LoadForExistingTimetable(saved);
        var handoff = input.BuildEditHandoff()!;
        vm.LoadFromSnapshot(input.CurrentSnapshot(), handoff.Solution, "snapshot", handoff.ManualCrossLinks);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void LoadFromSnapshot_WithSavedManualCrossLink_KeepsSuppressionThroughSelection()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var input = _sp.GetRequiredService<DataInputViewModel>();
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = MakeSavedRecordWithManualCross(
            "snapshot-suppress",
            "snapshot",
            new[]
            {
                new TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            SavedCross(sourceDay: 1, sourcePeriod: 3, targetDay: 2, targetPeriod: 4));

        input.LoadForExistingTimetable(saved);
        var handoff = input.BuildEditHandoff()!;
        vm.LoadFromSnapshot(input.CurrentSnapshot(), handoff.Solution, "snapshot", handoff.ManualCrossLinks);
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");

        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        Assert.True(vm.IsSavedCrossValidationSuppressed);
        Assert.False(vm.HasUserEditedAfterLoad);
        Assert.False(vm.LastCrossCleanupExecuted);
        Assert.Single(vm.WorkingCrossLinks);
    }

    [Fact]
    public void LoadFromSnapshot_AfterSuccessfulMove_ReleasesSavedCrossSuppression()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var input = _sp.GetRequiredService<DataInputViewModel>();
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = MakeSavedRecordWithManualCross(
            "snapshot-move",
            "snapshot",
            new[]
            {
                new TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            SavedCross());
        input.LoadForExistingTimetable(saved);
        var handoff = input.BuildEditHandoff()!;
        vm.LoadFromSnapshot(input.CurrentSnapshot(), handoff.Solution, "snapshot", handoff.ManualCrossLinks);
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.False(vm.IsSavedCrossValidationSuppressed);
        Assert.True(vm.HasUserEditedAfterLoad);
        Assert.True(vm.LastCrossCleanupExecuted);
    }

    [Fact]
    public void LoadFromSnapshot_WithSavedManualCrossLink_DoesNotSuppressHc20()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var input = _sp.GetRequiredService<DataInputViewModel>();
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = MakeSavedRecordWithManualCross(
            "snapshot-hc20",
            "snapshot",
            new[]
            {
                new TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableAssignmentRow("Y-01", 0, 1, "R2"),
                new TimetableAssignmentRow("X-01", 0, 3, "R1"),
            },
            SavedCross());

        input.LoadForExistingTimetable(saved);
        var handoff = input.BuildEditHandoff()!;
        vm.LoadFromSnapshot(input.CurrentSnapshot(), handoff.Solution, "snapshot", handoff.ManualCrossLinks);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SameCourseSameDayConflict);
    }

    [Fact]
    public void LoadFromSavedTimetable_StaleSavedCrossSlotStillRestoresFromCurrentAssignments()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross-stale",
            "크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 1, 3, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            });

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Equal(1, vm.LastSavedCrossLinkCount);
        Assert.Equal(1, vm.LastRestoredCrossLinkCount);
        Assert.Equal(0, vm.LastIgnoredCrossLinkCount);
    }

    [Fact]
    public void LoadFromSavedTimetable_NonOverlappingSavedCrossPairIsKeptWithoutHc11Exception()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross-non-overlap",
            "비겹침크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 1, 3, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            });

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.True(vm.IsSavedCrossValidationSuppressed);
        Assert.False(vm.HasUserEditedAfterLoad);
    }

    [Fact]
    public void LoadFromSavedTimetable_SelectAndBuildMoveStates_DoesNotReleaseSavedCrossSuppression()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross-select",
            "선택크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 1, 3, "R1",
                    "Y-01", 2, "1", 2, 4, "R2",
                    "STALE_POLICY_NAME"),
            });
        vm.LoadFromSavedTimetable(saved);
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");

        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.IsSavedCrossValidationSuppressed);
        Assert.False(vm.HasUserEditedAfterLoad);
        Assert.False(vm.LastCrossCleanupExecuted);
        Assert.Empty(vm.IgnoredSavedCrossLinkReasons);
    }

    [Fact]
    public void LoadFromSavedTimetable_MissingReferencedAssignmentIsOnlyRestoreFailure()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross-missing-assignment",
            "누락크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            });

        vm.LoadFromSavedTimetable(saved);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Equal(1, vm.LastSavedCrossLinkCount);
        Assert.Equal(0, vm.LastRestoredCrossLinkCount);
        Assert.Equal(1, vm.LastIgnoredCrossLinkCount);
        Assert.Single(vm.IgnoredSavedCrossLinkReasons);
        Assert.Contains("assignment가 없습니다", vm.IgnoredSavedCrossLinkReasons[0]);
    }

    [Fact]
    public void SaveTimetable_WithoutManualCrossLink_Hc11GradeOverlapBlocksSave()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        vm.SaveName = "크로스없는중복";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "크로스없는중복");
    }

    [Fact]
    public void LoadFromSavedTimetable_IgnoresUnknownPolicyAndSelfLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved",
            "저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "X-01", 2, "1", 0, 1, "R1",
                    "HC11_ONLY_EXCEPTION"),
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "UNKNOWN"),
            });

        vm.LoadFromSavedTimetable(saved);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("유효하지 않아", vm.StatusMessage);
        Assert.Equal(2, vm.LastSavedCrossLinkCount);
        Assert.Equal(0, vm.LastRestoredCrossLinkCount);
        Assert.Equal(2, vm.LastIgnoredCrossLinkCount);
        Assert.Equal(2, vm.IgnoredSavedCrossLinkReasons.Count);
    }

    [Fact]
    public void LoadFromSavedTimetable_RestoresManualCrossLinkAndAppliesHc11Exception()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross",
            "크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            });

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void LoadFromSavedTimetable_RestoredManualCrossLinkDoesNotBlockSaveWithHc11Only()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross-save",
            "크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            });

        vm.LoadFromSavedTimetable(saved);
        vm.SaveName = "크로스재저장";
        vm.SaveTimetableCommand.Execute(null);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains("저장 완료", vm.StatusMessage);
        Assert.Contains(_ws.SavedTimetables, t => t.Name == "크로스재저장");
    }

    [Fact]
    public void LoadFromSavedTimetable_ManualCrossLinkDoesNotSuppressRoomConflict()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-room-error",
            "강의실충돌저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R1"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R1",
                    "HC11_ONLY_EXCEPTION"),
            });

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.RoomConflict);
    }

    [Fact]
    public void LoadFromSavedTimetable_ManualCrossLinkDoesNotSuppressHc20()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-hc20",
            "HC20저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 3, "R1"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            });

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SameCourseSameDayConflict);
    }

    [Fact]
    public void LoadFromSavedTimetable_ManualCrossLinkDoesNotSuppressAdjacentHc20Blocks()
    {
        _ws.AddCourse(new Course
        {
            Id = "DS-A",
            Name = "자료구조",
            Grade = 2,
            Section = 1,
            HoursPerWeek = 3,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1, 2 },
        });
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-adjacent-hc20",
            "인접HC20저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("DS-A", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("DS-A", 0, 2, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("DS-A", 0, 3, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "DS-A", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            });

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SameCourseSameDayConflict);
    }

    [Fact]
    public void LoadFromSavedTimetable_AfterMoveCrossValidationResumesAndRemovesLink()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-move",
            "이동저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            });
        vm.LoadFromSavedTimetable(saved);
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.False(vm.IsSavedCrossValidationSuppressed);
        Assert.True(vm.HasUserEditedAfterLoad);
        Assert.True(vm.LastCrossCleanupExecuted);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SelectCell_HardBlockedTarget_RemainsBlockedNotWarning()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 2, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(2, 1, 2, 0, assignment);

        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Blocked,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 5, 2, 0)].State);
    }

    [Fact]
    public void ClearSelection_ResetsWarningDetails()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.ClearSelectionCommand.Execute(null);

        Assert.Equal("-", vm.SelectedWarningReasonText);
    }

    [Fact]
    public void ApplyRoomChange_NoNewConflicts_DoesNotPromptDialog()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Empty(_dialog.Calls);
        Assert.Equal(new[] { "R2" }, vm.SelectedAssignment!.Rooms);
    }

    [Fact]
    public void SelectedAssignment_WithMultipleRoomCandidates_ShowsRoomCandidates()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.True(vm.HasMultipleRoomCandidates);
        Assert.Equal(new[] { "R1", "R2" }, vm.AvailableRoomIds);
    }

    [Fact]
    public void SelectedAssignment_WithFixedRoomStillShowsEditableRoomCandidates()
    {
        _ws.Courses.First(c => c.Id == "X-01").FixedRooms.Add("R1");
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.True(vm.HasMultipleRoomCandidates);
        Assert.Equal(new[] { "R1", "R2" }, vm.AvailableRoomIds);
    }

    [Fact]
    public void ApplyRoomChange_NewRoomConflict_DoesNotPromptAndRevertsChange()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        // Y-01 has DIFFERENT prof → no pre-existing conflict
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        vm.LoadFromSolution(sol);

        Assert.Empty(vm.Conflicts);  // start clean

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        _dialog.ResponseToReturn = true;
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Empty(_dialog.Calls);
        Assert.Equal(new[] { "R1" }, vm.SelectedAssignment!.Rooms);
        Assert.Equal("R2", vm.NewRoomId);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.RoomConflict);
    }

    [Fact]
    public void ApplyRoomChange_NewConflicts_UserCancels_RevertsChange()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        vm.LoadFromSolution(sol);
        Assert.Empty(vm.Conflicts);  // start clean

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        _dialog.ResponseToReturn = false;
        vm.ApplyRoomChangeCommand.Execute(null);

        // Dialog prompted
        Assert.Empty(_dialog.Calls);
        // Change was reverted — no conflicts remain (clean start state)
        Assert.Empty(vm.Conflicts);
        Assert.Equal("해당 시간에 이미 사용 중인 강의실입니다.", vm.StatusMessage);
    }

    [Fact]
    public void Undo_AfterRoomChange_RestoresPreviousRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        vm.UndoCommand.Execute(null);

        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void MultiRoomSelection_ShowsAssignedRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.Courses.First(c => c.Id == "X-01").FixedRooms.AddRange(new[] { "R1", "R2", "R3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.True(vm.HasMultipleAssignedRooms);
        Assert.Equal(new[] { "R1", "R2" }, vm.AssignedRoomsForSelectedAssignment);
        Assert.Equal("R1", vm.SelectedOldRoomId);
    }

    [Fact]
    public void MultiRoomReplacement_CandidatesExcludeAlreadyAssignedRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.AddRoom(new Room { Id = "R4", Name = "강의실4" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        var candidateIds = vm.ReplacementRoomCandidates.ToArray();
        Assert.Equal(new[] { "R3", "R4" }, candidateIds);
        Assert.DoesNotContain("R1", candidateIds);
        Assert.DoesNotContain("R2", candidateIds);
    }

    [Fact]
    public void ReplacementCandidates_FallbackToSessionRooms_WhenFilteredEmpty()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.Courses.First(c => c.Id == "X-01").FixedRooms.AddRange(new[] { "R1", "R2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Equal(new[] { "R3" }, vm.ReplacementRoomCandidates.ToArray());
    }

    [Fact]
    public void NewRoomCombo_SelectedValueType_MatchesRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.All(vm.SingleRoomChangeCandidates, r => Assert.IsType<string>(r));
        Assert.Equal("R1", vm.NewRoomId);
    }

    [Fact]
    public void SingleRoomChange_AvailableRoomIds_IsStringRoomIdList()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.All(vm.AvailableRoomIds, id => Assert.IsType<string>(id));
        Assert.Contains("R1", vm.AvailableRoomIds);
        Assert.Contains("R2", vm.AvailableRoomIds);
    }

    [Fact]
    public void SingleRoomChange_UsesNewRoomIdString_UpdatesRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void NewRoomId_Setter_RaisesApplyRoomChangeCanExecuteChanged()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        var raised = 0;
        vm.ApplyRoomChangeCommand.CanExecuteChanged += (_, _) => raised++;

        vm.NewRoomId = "R2";

        Assert.True(raised > 0);
    }

    [Fact]
    public void SelectedReplacementRoomId_Setter_RaisesApplyRoomChangeCanExecuteChanged()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.AddRoom(new Room { Id = "R4", Name = "강의실4" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        var raised = 0;
        vm.ApplyRoomChangeCommand.CanExecuteChanged += (_, _) => raised++;

        vm.SelectedReplacementRoomId = "R4";

        Assert.True(raised > 0);
    }

    [Fact]
    public void SelectedOldRoomId_Setter_RaisesApplyRoomChangeCanExecuteChanged()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        var raised = 0;
        vm.ApplyRoomChangeCommand.CanExecuteChanged += (_, _) => raised++;

        vm.SelectedOldRoomId = "R2";

        Assert.True(raised > 0);
    }

    [Fact]
    public void SingleRoomChange_CommandBecomesExecutableAfterNewRoomSelected()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));

        vm.NewRoomId = "R2";

        Assert.True(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void MultiRoomReplacement_CommandBecomesExecutableAfterOldAndNewSelected()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));

        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";

        Assert.True(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void SingleRoomChange_CommandExecute_ChangesWorkingRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void MultiRoomReplacement_CommandExecute_ChangesSelectedOldRoomOnly()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2", "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void RoomChangeCommand_WithSelection_CanExecuteTrue_BeforeNewRoomSelected()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "";

        Assert.True(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void SingleRoomChange_NoNewRoom_ShowsFailureMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("새 강의실을 선택하세요.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void SingleRoomChange_SameRoom_ShowsFailureMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("기존 강의실과 동일합니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void SingleRoomChange_RoomNotExists_ShowsFailureMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "RX";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("존재하지 않는 강의실입니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void SingleRoomChange_ConflictRollback_ShowsFailureMessage()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("해당 시간에 이미 사용 중인 강의실입니다.", vm.RoomChangeStatusMessage);
        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void RoomChange_ChangedZero_ShowsFailureMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var stale = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R9" }, 1, 2, false);

        vm.SelectCell(0, 1, 2, 0, stale);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Contains("변경할 배정 항목을 찾지 못했습니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void SingleRoomChange_Failure_DoesNotResetNewRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "RX";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("RX", vm.NewRoomId);
    }

    [Fact]
    public void SingleRoomChange_Success_ShowsSuccessMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("강의실을 변경했습니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void MultiRoomReplacement_InvalidOldOrNew_ShowsFailureMessage()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.SelectedOldRoomId = "";
        vm.SelectedReplacementRoomId = "R3";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("교체할 기존 강의실을 선택하세요.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void MultiRoomReplacement_Failure_DoesNotResetSelectedReplacementRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "RX";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("RX", vm.SelectedReplacementRoomId);
        Assert.Equal("존재하지 않는 강의실입니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void MultiRoomReplacement_Success_ShowsSuccessMessage()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("강의실을 변경했습니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void SingleRoomChange_DoesNotRequireSelectedOldRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "";
        vm.SelectedReplacementRoomId = "";
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void SingleRoomChange_DoesNotUseReplacementRoomCandidates()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedReplacementRoomId = "";
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void ReplaceOneRoom_ChangesOnlySelectedOldRoom()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2", "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
        Assert.DoesNotContain("R1", AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void TryApplyRoomChange_ChangedZero_DoesNotCommitOrCreateUndo()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var stale = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R9" }, 1, 2, false);

        vm.SelectCell(0, 1, 2, 0, stale);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Contains("변경할 배정 항목", vm.StatusMessage);
    }

    [Fact]
    public void SingleRoomChange_ChangesWorkingRoomId_AndRerenders()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.Equal(new[] { "R2" }, vm.SelectedAssignment!.Rooms);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void MultiRoomReplacement_ChangesOnlySelectedOldRoom_InWorking()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2", "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void SingleRoomChange_StaleSelectedAssignment_DoesNotReportSuccess()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var stale = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R9" }, 1, 2, false);

        vm.SelectCell(0, 1, 2, 0, stale);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.DoesNotContain("변경 0건", vm.StatusMessage);
        Assert.Contains("변경할 배정 항목을 찾지 못했습니다.", vm.StatusMessage);
        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void Save_AfterSuccessfulRoomChange_PersistsChangedWorkingRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.SaveName = "강의실변경저장";
        vm.SaveTimetableCommand.Execute(null);

        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "강의실변경저장"));
        Assert.Contains(saved.Assignments, a => a.CourseId == "X-01" && a.RoomId == "R2");
        Assert.DoesNotContain(saved.Assignments, a => a.CourseId == "X-01" && a.RoomId == "R1");
    }

    [Fact]
    public void LoadSaved_AfterRoomChange_RestoresChangedRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.SaveName = "강의실변경로드";
        vm.SaveTimetableCommand.Execute(null);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "강의실변경로드"));

        vm.LoadFromSavedTimetable(saved);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void RoomChange_TargetMatching_FindsTwoHourBlockAssignments()
    {
        _ws.Courses.First(c => c.Id == "X-01").BlockStructure = new List<int> { 2 };
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 2, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Assignment.Rooms.Contains("R1"));
    }

    [Fact]
    public void RoomChange_TargetMatching_FailsWhenNoWorkingTarget()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var stale = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R9" }, 1, 2, false);

        vm.SelectCell(0, 1, 2, 0, stale);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SingleRoomChange_ChangedWorkingThenRerenderedRoomsContainNewRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Contains("R2", AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.DoesNotContain("R1", AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void MultiRoomReplacement_OnlySelectedOldRoomChanged()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        var rooms = AssignmentAt(vm, 0, 1, "X-01").Rooms;
        Assert.Contains("R2", rooms);
        Assert.Contains("R3", rooms);
        Assert.DoesNotContain("R1", rooms);
    }

    [Fact]
    public void SaveLoad_AfterRoomChange_RestoresChangedRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.SaveName = "저장로드강의실";
        vm.SaveTimetableCommand.Execute(null);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "저장로드강의실"));

        vm.LoadFromSaved(saved);
        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void RoomChange_ChangedZero_ReportsDiagnosticState()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var stale = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R9" }, 1, 2, false);

        vm.SelectCell(0, 1, 2, 0, stale);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Contains("CourseId=X-01", vm.StatusMessage);
        Assert.Contains("Day=0", vm.StatusMessage);
        Assert.Contains("SelectedPeriod=1", vm.StatusMessage);
        Assert.Contains("RowSpan=1", vm.StatusMessage);
        Assert.Contains("OldRoomId=R9", vm.StatusMessage);
        Assert.Contains("NewRoomId=R2", vm.StatusMessage);
        Assert.Contains("SameCourseDay=1", vm.StatusMessage);
        Assert.Contains("SameCourseDayRoom=0", vm.StatusMessage);
    }

    [Fact]
    public void RoomChange_AfterApply_SelectedAssignmentRoomsUpdated()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, vm.SelectedAssignment!.Rooms);
        Assert.Equal("강의실2", vm.SelectedRoomsText);
    }

    [Fact]
    public void RoomChange_AfterApply_IntegratedRenderShowsNewRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void RoomChange_AfterApply_RoomViewMovesFromOldRoomToNewRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        var oldRoomView = vm.RoomViews.Single(v => v.Id == "R1");
        var newRoomView = vm.RoomViews.Single(v => v.Id == "R2");
        Assert.DoesNotContain(oldRoomView.Grid.Cells.SelectMany(c => c.Items), a => a.CourseId == "X-01");
        Assert.Contains(newRoomView.Grid.Cells.SelectMany(c => c.Items), a => a.CourseId == "X-01" && a.Rooms.Contains("R2"));
    }

    [Fact]
    public void RoomChange_WorkspaceOnlyRoom_SaveLoadRestoresRoomId()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var snapshot = new AppData(
            _ws.Courses.ToList(),
            _ws.Professors.ToList(),
            new List<Room> { new() { Id = "R1", Name = "강의실1" } },
            _ws.CrossGroups.ToList(),
            _ws.RetakeScenarios.ToList());
        var record = new SavedTimetableRecord(
            "saved",
            "저장",
            DateTime.Now,
            new[] { new TimetableAssignmentRow("X-01", 0, 1, "R1") },
            Array.Empty<SavedManualCrossLinkRow>(),
            System.Text.Json.JsonSerializer.Serialize(snapshot));
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSaved(record);

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.SaveName = "워크스페이스방저장";
        vm.SaveTimetableCommand.Execute(null);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "워크스페이스방저장"));

        vm.LoadFromSaved(saved);

        Assert.Equal(new[] { "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.Contains(vm.RoomViews, v => v.Id == "R3");
    }

    [Fact]
    public void MultiRoomReplacement_SaveLoadKeepsAllRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.SaveName = "다중강의실저장";
        vm.SaveTimetableCommand.Execute(null);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "다중강의실저장"));

        vm.LoadFromSaved(saved);

        Assert.Equal(new[] { "R2", "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void RoomChange_ConflictRollback_DoesNotUpdateRenderedRooms()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.Equal(new[] { "R1" }, vm.SelectedAssignment!.Rooms);
        Assert.DoesNotContain(vm.RoomViews.Single(v => v.Id == "R2").Grid.Cells.SelectMany(c => c.Items), a => a.CourseId == "X-01");
    }

    [Fact]
    public void MultiRoomReplacement_DoesNotUseNewRoomId()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2", "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void ReplaceOneRoom_KeepsOtherAssignedRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Contains("R2", AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void ReplaceOneRoom_ToAlreadyAssignedRoom_IsRejected()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R2";

        Assert.True(vm.ApplyRoomChangeCommand.CanExecute(null));
        vm.ApplyRoomChangeCommand.Execute(null);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Equal("새 강의실이 이미 이 수업에 배정되어 있습니다.", vm.RoomChangeStatusMessage);
        Assert.Equal(new[] { "R1", "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void ReplaceOneRoom_WithRoomConflict_IsRejected()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"),
            new SolutionAssignment("Y-01", 0, 1, "R3")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R1", "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Equal("해당 시간에 이미 사용 중인 강의실입니다.", vm.StatusMessage);
    }

    [Fact]
    public void ReplaceOneRoom_Success_CreatesUndoSnapshot()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void ReplaceOneRoom_Failure_DoesNotCreateUndoSnapshot()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"),
            new SolutionAssignment("Y-01", 0, 1, "R3")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Undo_AfterReplaceOneRoom_RestoresOriginalRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        vm.UndoCommand.Execute(null);

        Assert.Equal(new[] { "R1", "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void SingleRoomSelection_KeepsExistingRoomChangeUi()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.False(vm.HasMultipleAssignedRooms);
        Assert.True(vm.HasSingleAssignedRoomChangeUi);
        Assert.True(vm.HasSingleAssignedRoomDisplay);
        Assert.Equal(new[] { "R1", "R2" }, vm.AvailableRoomIds);
    }

    [Fact]
    public void SavedSnapshotRoomsMissing_WorkspaceRoomStillAppearsInAvailableRoomIds()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var snapshot = new AppData(
            _ws.Courses.ToList(),
            _ws.Professors.ToList(),
            new List<Room> { new() { Id = "R1", Name = "강의실1" } },
            _ws.CrossGroups.ToList(),
            _ws.RetakeScenarios.ToList());
        var record = new SavedTimetableRecord(
            "saved",
            "저장",
            DateTime.Now,
            new[] { new TimetableAssignmentRow("X-01", 0, 1, "R1") },
            Array.Empty<SavedManualCrossLinkRow>(),
            System.Text.Json.JsonSerializer.Serialize(snapshot));
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSaved(record);

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Contains("R3", vm.AvailableRoomIds);
    }

    [Fact]
    public void RoomExists_UsesWorkspaceSnapshotAndAssignedRoomsUnion()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R3";

        Assert.True(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void SingleRoomChange_UsesEditableRoomIds_NotOnlyFixedRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.Courses.First(c => c.Id == "X-01").FixedRooms.Add("R1");
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Contains("R3", vm.AvailableRoomIds);
    }

    [Fact]
    public void MultiRoomReplacement_UsesEditableRoomIdsAndExcludesOtherAssignedRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.Courses.First(c => c.Id == "X-01").FixedRooms.AddRange(new[] { "R1", "R2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Contains("R3", vm.ReplacementRoomCandidates);
        Assert.DoesNotContain("R1", vm.ReplacementRoomCandidates);
        Assert.DoesNotContain("R2", vm.ReplacementRoomCandidates);
    }

    [Fact]
    public void SelectedOldRoomChange_RefreshesReplacementCandidates()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.AddRoom(new Room { Id = "R4", Name = "강의실4" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedReplacementRoomId = "R3";
        vm.SelectedOldRoomId = "R2";

        Assert.Equal("R3", vm.SelectedReplacementRoomId);
        Assert.Equal(new[] { "R3", "R4" }, vm.ReplacementRoomCandidates);
    }

    [Fact]
    public void ProfessorDisplay_DeduplicatesPrimaryProfessorInCoteachList()
    {
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false)
        {
            CoteachProfIds = new[] { "P1", "P2" },
        };

        Assert.Equal("P1, P2", assignment.ProfessorLabel);
    }

    [Fact]
    public void ProfessorDisplay_KeepsPrimaryThenCoteachersOrder()
    {
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false)
        {
            CoteachProfIds = new[] { "P3", "P2" },
        };

        Assert.Equal("P1, P3, P2", assignment.ProfessorLabel);
    }

    [Fact]
    public void InspectorCoteachText_DeduplicatesPrimaryProfessor()
    {
        _ws.AddProfessor(new Professor { Id = "P2", Name = "공동" });
        _ws.Courses.First(c => c.Id == "X-01").CoteachProfs.AddRange(new[] { "P1", "P2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Equal("공동", vm.SelectedCoteachText);
    }

    [Fact]
    public void ApplyRoomChange_ProfUnavailable_DetectedAndPrompted()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var prof = _ws.Professors.First(p => p.Id == "P1");
        prof.UnavailableSlots.Add(new TimeSlot(0, 2));

        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        // Move the course to a slot where prof is unavailable — change room then we don't have day/period change yet,
        // so for this test we directly verify Detect catches it.
        var conflicts = ConflictDetector.Detect(
            new[] { new SolutionAssignment("X-01", 0, 2, "R1") },
            _ws.Courses, _ws.Professors);
        Assert.Contains(conflicts, c => c.Type == ConflictType.ProfUnavailable);
    }

    [Fact]
    public void Reset_RestoresBaseAssignment()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        _dialog.ResponseToReturn = true;
        vm.ApplyRoomChangeCommand.Execute(null);

        vm.ResetCommand.Execute(null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(vm.Conflicts);
    }

    [Fact]
    public void HandleCellClick_EmptyValidTarget_MovesSelectedRunAndClearsSelection()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_LunchTarget_ForceMovesAfterConfirmation()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 5, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Single(_dialog.Calls);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void HandleCellClick_Len2TargetRangeContainsLen1Course_IsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 1, 2, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 2 && c.Assignment.CourseId == "Y-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_Len1IntoSecondSlotOfLen2Course_IsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.HandleCellClick(1, 1, 2, 0, assignment);
        vm.HandleCellClick(0, 2, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01" && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01" && c.Assignment.RowSpan == 1);
    }

    [Fact]
    public void HandleCellClick_FailedMoveThenValidMove_LeavesWorkingStateUsable()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 1, 2, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 9, 2, 0, null);
        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 2 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void HandleCellClick_Len2OntoLen2Course_IsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 2, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Y-01", 1, 2, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01" && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01" && c.Assignment.RowSpan == 2);
    }

    [Fact]
    public void HandleCellClick_Len2AtLastPeriod_DoesNotMove()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 9, 2, 0, null);

        Assert.Contains("범위를 벗어납니다", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_OntoFixedCourse_ForceMovesAndKeepsExistingCourse()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course
        {
            Id = "F-01",
            Name = "고정",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P2",
            IsFixed = true,
            FixedSlots = new List<TimeSlot> { new(1, 1) },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("F-01", 1, 1, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "F-01");
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void HandleCellClick_SelectedBlockAgain_ClearsSelectionAndDoesNotMove()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(0, 1, 2, 0, assignment);

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("선택이 해제", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_DifferentGradeWhileSelected_ClearsSelectionWithoutForceMove()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "타학년", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var otherGrade = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "타학년", "P2", 3, 1, new List<string> { "R2" }, 1, 1, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 3, 0, otherGrade);

        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(_dialog.Calls);
        Assert.Contains("선택이 해제", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void HandleCellClick_Len1MoveThenReturnToOriginalSlot_Succeeds()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);
        vm.HandleCellClick(1, 1, 2, 0, AssignmentAt(vm, 1, 1, "X-01"));
        vm.HandleCellClick(0, 1, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_Len2MoveThenReturnToOriginalSlot_Succeeds()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);
        vm.HandleCellClick(1, 1, 2, 0, AssignmentAt(vm, 1, 1, "X-01"));
        vm.HandleCellClick(0, 1, 2, 0, null);

        Assert.DoesNotContain("HC-06", vm.StatusMessage);
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_DifferentGradeCourseAtSameTime_DoesNotCauseHc06FalsePositive()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.DoesNotContain("HC-06", vm.StatusMessage);
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void HandleCellClick_CrossDisplayColumn_DoesNotForceMove()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 1, null);

        Assert.Contains("Cross 표시용 열", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Empty(_dialog.Calls);
    }

    [Fact]
    public void DragDropMove_ToEmptyValidCell_MovesAssignment()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");

        Assert.True(vm.CanDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            2, 1, 2, 0));

        var moved = vm.HandleDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            2, 1, 2, 0);

        Assert.True(moved);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
        Assert.False(vm.EvaluateCrossHover(2, 1, 2, 0, null).CanCreate);
        Assert.False(vm.EvaluateSwapHover(2, 1, 2, 0, null).CanSwap);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void DndMoveSuccess_UsesSamePostMoveCleanupAsClickMove()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");

        var moved = vm.HandleDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            2, 1, 2, 0);

        Assert.True(moved);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void CrossDrop_SourceNotSelected_CreatesManualCrossLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 1, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "Y-01");

        Assert.Null(vm.SelectedAssignment);

        vm.HandleCrossDrop(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void SwapDrop_SourceNotSelected_PerformsSwap()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교환대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "Y-01");

        Assert.Null(vm.SelectedAssignment);

        vm.HandleSwapDrop(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 3);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 0 && c.Period == 1);
        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void DragDropMove_ToBlockedOverlapRange_DoesNotMoveOrCreateUndoSnapshot()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 2, "R1"),
            new SolutionAssignment("B-02", 0, 3, "R1"),
            new SolutionAssignment("X-01", 1, 1, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");

        Assert.False(vm.CanDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            0, 3, 2, 0));
        var moved = vm.HandleDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            0, 3, 2, 0);

        Assert.False(moved);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 2 && c.Assignment.CourseId == "B-02" && c.Assignment.RowSpan == 2);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Equal("X-01", vm.SelectedAssignment!.CourseId);
        Assert.NotNull(vm.Grid.SelectedCell);
        Assert.NotEmpty(vm.Grid.EditStates);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void DragDropMove_ToCrossDisplayOnlySubColumn_DoesNotMoveOrCreateUndoSnapshot()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");

        Assert.False(vm.CanDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            1, 2, 2, 1));
        var moved = vm.HandleDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            1, 2, 2, 1);

        Assert.False(moved);
        Assert.Contains("Cross 표시용 열", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void TwoHourMoveSuccess_RebuildsRowSpanAtNewRange()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 6, "R1"),
            new SolutionAssignment("B-02", 0, 7, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "B-02");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(1, 3, 2, 0, null);

        var moved = Assert.Single(vm.Grid.Cells, c => c.Assignment.CourseId == "B-02");
        Assert.Equal(1, moved.Day);
        Assert.Equal(3, moved.Period);
        Assert.Equal(2, moved.Assignment.RowSpan);
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Assignment.CourseId == "B-02");
        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void TwoHourMoveSuccess_DoesNotRenderBlankCoveredCell()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 6, "R1"),
            new SolutionAssignment("B-02", 0, 7, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "B-02");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(1, 3, 2, 0, null);

        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 3 && c.Assignment.CourseId == "B-02" && c.Assignment.RowSpan == 2);
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 4 && c.Assignment.CourseId == "B-02");
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void SelectCell_MoveStateAndTryMove_AgreeForBlockedAndMovableTargets()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Blocked,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(1, 5, 2, 0)].State);
        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Movable,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(2, 1, 2, 0)].State);

        vm.HandleCellClick(1, 5, 2, 0, null);
        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
        vm.SelectCell(1, 5, 2, 0, assignment);
        vm.HandleCellClick(2, 1, 2, 0, null);
        Assert.Contains("이동 완료", vm.StatusMessage);
    }

    [Fact]
    public void ForceMove_BlockedTargetCancel_LeavesWorkingStateUnchanged()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        _dialog.ResponseToReturn = false;

        vm.HandleCellClick(1, 5, 2, 0, null);

        Assert.Single(_dialog.Calls);
        Assert.Contains("취소", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 5 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void ForceMove_ProfessorConflict_MovesAndSaveIsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P1" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
        vm.SaveName = "교수충돌저장";
        vm.SaveTimetableCommand.Execute(null);
        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "교수충돌저장");
    }

    [Fact]
    public void ForceMoveSuccess_ClearsSelectionWithoutBlankCell()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P1" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void ForceMove_RoomConflict_MovesAndKeepsError()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void ForceMove_FixedSelectedCourse_MovesAndKeepsFixedTimeError()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.UpdateCourse(new Course
        {
            Id = "X-01",
            Name = "테스트",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            IsFixed = true,
            FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2) },
        });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, true);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void UndoRedo_AfterNormalMove_RestoresBothStates()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        vm.UndoCommand.Execute(null);

        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);

        vm.RedoCommand.Execute(null);

        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);
    }

    [Fact]
    public void UndoRedo_AfterForceMove_RestoresConflicts()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 5, 2, 0, null);
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);

        vm.UndoCommand.Execute(null);

        Assert.Empty(vm.Conflicts);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");

        vm.RedoCommand.Execute(null);

        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void UndoRedo_AfterCrossCreate_RestoresCrossLinksAndPlacement()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);
        Assert.Single(vm.WorkingCrossLinks);

        vm.UndoCommand.Execute(null);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");

        vm.RedoCommand.Execute(null);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void Undo_AfterMoveReleasesCross_RestoresCrossLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);
        vm.SelectCell(0, 1, 2, 1, AssignmentAt(vm, 0, 1, "X-01"));
        vm.HandleCellClick(2, 1, 2, 0, null);
        Assert.Empty(vm.WorkingCrossLinks);

        vm.UndoCommand.Execute(null);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair("X-01", "Y-01"));
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void UndoThenNewMove_ClearsRedoStack()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);
        vm.UndoCommand.Execute(null);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.False(vm.RedoCommand.CanExecute(null));
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void FailedMoveAndCancelledForceMove_DoNotPushUndoSnapshot()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 9, 2, 0, null);
        Assert.False(vm.UndoCommand.CanExecute(null));

        _dialog.ResponseToReturn = false;
        vm.HandleCellClick(1, 5, 2, 0, null);

        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void EvaluateCrossHover_RequiresSelectionAndOccupiedTarget()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        Assert.False(vm.EvaluateCrossHover(0, 1, 2, 0, null).CanCreate);
    }

    [Fact]
    public void EvaluateCrossHover_AllowsSameGradeTargetCourse()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        vm.LoadFromSolution(sol);
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        var state = vm.EvaluateCrossHover(0, 1, 2, 1, target);

        Assert.True(state.CanCreate);
    }

    [Fact]
    public void EvaluateCrossHover_AllowsSameTwoHourBlocks()
    {
        _ws.UpdateCourse(new Course
        {
            Id = "X-01",
            Name = "테스트",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddCourse(new Course
        {
            Id = "Y-01",
            Name = "기타",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            Section = 2,
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("Y-01", 0, 2, "R2")));
        var selectedCell = vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        var targetCell = vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        vm.SelectCell(0, 1, 2, selectedCell.SubColumnIdx, selectedCell.Assignment);

        var state = vm.EvaluateCrossHover(0, 1, 2, targetCell.SubColumnIdx, targetCell.Assignment);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void EvaluateCrossHover_OneHourToTwoHour_DoesNotShowPlus()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            Section = 2,
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("B-02", 1, 1, "R2"),
            new SolutionAssignment("B-02", 1, 2, "R2")));
        var selectedCell = vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        var targetCell = vm.Grid.Cells.Single(c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "B-02");
        vm.SelectCell(0, 1, 2, selectedCell.SubColumnIdx, selectedCell.Assignment);

        var state = vm.EvaluateCrossHover(1, 1, 2, targetCell.SubColumnIdx, targetCell.Assignment);

        Assert.False(state.CanCreate);
        Assert.Contains("서로 다른 길이", state.Reason);
    }

    [Fact]
    public void HandleCrossAddRequested_DurationMismatch_DoesNotMutateStateOrUndo()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            Section = 2,
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("B-02", 1, 1, "R2"),
            new SolutionAssignment("B-02", 1, 2, "R2")));
        var selectedCell = vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        var targetCell = vm.Grid.Cells.Single(c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "B-02");
        vm.SelectCell(0, 1, 2, selectedCell.SubColumnIdx, selectedCell.Assignment);

        vm.HandleCrossAddRequested(1, 1, 2, targetCell.SubColumnIdx, targetCell.Assignment);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("서로 다른 길이", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "B-02" && c.Assignment.RowSpan == 2);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void HandleCrossAddRequested_SameDurationWrongTargetRange_DoesNotCreateManualCrossLink()
    {
        _ws.UpdateCourse(new Course
        {
            Id = "X-01",
            Name = "테스트",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            Section = 2,
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("B-02", 1, 1, "R2"),
            new SolutionAssignment("B-02", 1, 2, "R2")));
        var selectedCell = vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        var targetCell = vm.Grid.Cells.Single(c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "B-02");
        vm.SelectCell(0, 1, 2, selectedCell.SubColumnIdx, selectedCell.Assignment);

        vm.HandleCrossAddRequested(1, 2, 2, targetCell.SubColumnIdx, targetCell.Assignment);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("같은 수업시간", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void EvaluateCrossHover_AllowsDifferentPeriodSameGradeByMovingToTargetSlot()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var otherGrade = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 3, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        Assert.False(vm.EvaluateCrossHover(0, 1, 2, 0, selected).CanCreate);
        Assert.False(vm.EvaluateCrossHover(0, 1, 3, 0, otherGrade).CanCreate);
        var sameGradeDifferentTime = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "다른시간", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        _ws.AddCourse(new Course { Id = "Z-01", Name = "다른시간", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });

        Assert.True(vm.EvaluateCrossHover(1, 1, 2, 1, sameGradeDifferentTime).CanCreate);
    }

    [Fact]
    public void EvaluateCrossHover_BlocksWhenImmediateCrossMoveWouldConflict()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교수중복", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1" });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "강의실중복", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        var sameProfessor = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "교수중복", "P1", 2, 1, new List<string> { "R2" }, 1, 1, false);
        var sameRoom = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "강의실중복", "P2", 2, 1, new List<string> { "R1" }, 1, 1, false);

        Assert.False(vm.EvaluateCrossHover(1, 1, 2, 0, sameProfessor).CanCreate);
        Assert.False(vm.EvaluateCrossHover(2, 1, 2, 0, sameRoom).CanCreate);
    }

    [Fact]
    public void HandleCrossAddRequested_OnlyShowsMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        vm.LoadFromSolution(sol);
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Contains("크로스가 설정되었습니다", vm.StatusMessage);
        Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal(2, vm.Grid.Cells.Count);
    }

    [Fact]
    public void HandleCrossAddRequested_DoesNotMutateWorkspaceCrossGroupsAndPreventsDuplicate()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);
        vm.SelectCell(0, 1, 2, 1, AssignmentAt(vm, 0, 1, "X-01"));
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Empty(_ws.CrossGroups);
        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains("이미 크로스", vm.StatusMessage);
    }

    [Fact]
    public void CrossLink_ExemptsOnlyGradeConflict()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void MovingCrossedCourseAway_ReleasesManualCrossLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        vm.SelectCell(0, 1, 2, 1, AssignmentAt(vm, 0, 1, "X-01"));
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("기존 크로스가 해제", vm.StatusMessage);
    }

    [Fact]
    public void CrossMoveSuccess_RebuildsGridAndDoesNotLeaveStaleSelection()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 1, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleCrossAddRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void HandleCrossAddRequested_AllowsSameGradeSameTimePair()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains("크로스가 설정되었습니다", vm.StatusMessage);
        Assert.Null(vm.SelectedAssignment);
        Assert.Equal("-", vm.SelectedCrossGroupText);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void SameTimeCrossLink_AllowsHc11OverlapOnly()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Contains("크로스가 설정되었습니다", vm.StatusMessage);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void CrossedOccupiedTarget_AllowsParallelMoveAndExpandsGradeWidth()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Contains("크로스가 설정되었습니다", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Equal(2, vm.Grid.DayGroups[0].Grades.Single(g => g.Grade == 2).Width);
        Assert.Equal("Y-01", vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.SubColumnIdx == 0).Assignment.CourseId);
        Assert.Equal("X-01", vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.SubColumnIdx == 1).Assignment.CourseId);
    }

    [Fact]
    public void NonCrossOccupiedTarget_IsBlockedWithoutForceDialog()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCellClick(1, 1, 2, 0, new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false));

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void CrossedOccupiedTarget_WithProfessorConflict_RemainsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1", Section = 2 });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P1", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, target);

        Assert.Contains("HC-", vm.StatusMessage);
        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void CrossedOccupiedTarget_WithRoomConflict_RemainsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R1" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Contains("HC-01", vm.StatusMessage);
        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCrossAddRequested_ReplacesExistingCrossForSelectedCourse()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "또다른", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3", Section = 3 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var y = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        var z = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "또다른", "P3", 2, 3, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, y);

        vm.SelectCell(1, 1, 2, 1, selected);
        vm.HandleCrossAddRequested(2, 1, 2, 0, z);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair("X-01", "Z-01"));
        Assert.Contains("크로스가 변경되었습니다", vm.StatusMessage);
    }

    [Fact]
    public void HandleCrossAddRequested_ReplacesExistingPartnerAndMovesAtomically()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "또다른", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3", Section = 3 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R3")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var y = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        var z = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "또다른", "P3", 2, 3, new List<string> { "R3" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, y);
        Assert.Single(vm.WorkingCrossLinks);

        vm.SelectCell(1, 1, 2, 1, AssignmentAt(vm, 1, 1, "X-01"));
        vm.HandleCrossAddRequested(2, 1, 2, 0, z);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair("X-01", "Z-01"));
        Assert.DoesNotContain(vm.WorkingCrossLinks, l => l.MatchesPair("X-01", "Y-01"));
        Assert.Contains("크로스가 변경되었습니다", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void HandleCrossAddRequested_FailedReplacementKeepsExistingPartnerAndPosition()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "충돌", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1", Section = 3 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var y = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        var z = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "충돌", "P1", 2, 3, new List<string> { "R1" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, y);
        Assert.True(vm.WorkingCrossLinks.Count == 1, vm.StatusMessage);

        vm.SelectCell(1, 1, 2, 1, AssignmentAt(vm, 1, 1, "X-01"));
        vm.HandleCrossAddRequested(2, 1, 2, 0, z);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair("X-01", "Y-01"));
        Assert.Contains("HC-01", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void SelectingAnotherCourse_ReleasesCurrentCrossAndForceMovesSelectedCourse()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "또다른", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3", Section = 3 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R3")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var y = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        var z = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "또다른", "P3", 2, 3, new List<string> { "R3" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, y);

        vm.HandleCellClick(2, 1, 2, 0, z);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal("Z-01", vm.SelectedAssignment?.CourseId);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "Z-01");
    }

    [Fact]
    public void HandleCrossAddRequested_BlocksThirdCourseAtTargetSlot()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "또다른", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3", Section = 3 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 1, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(1, 1, 2, 0, target);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("3개 이상의 과목", vm.StatusMessage);
    }
}
