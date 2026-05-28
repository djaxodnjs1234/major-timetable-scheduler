using Microsoft.Extensions.DependencyInjection;
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
    public void MoveOneHourIntoTwoHourBlockInterior_ForceMovesAndKeepsBothBlocks()
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

        Assert.Single(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "B-02" && c.Period == 2 && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3 && c.Assignment.RowSpan == 1);
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
    public void CrossMoveCreatingTwoHourDirectlyBelowDifferentCourseOneHour_IsAllowed()
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

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains("크로스가 설정", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == target.Period && c.Assignment.RowSpan == 2);
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
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 3);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 0 && c.Period == 1);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapOneHourWithTwoHour_PreservesDurationsAndDoesNotCreateManualCrossLink()
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

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 6 && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 1 && c.Assignment.RowSpan == 2);
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
        Assert.Contains("같은 과목", vm.StatusMessage);
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
        vm.SelectCell(0, 1, 2, 0, assignment);

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
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Equal("2시간", vm.SelectedDurationText);
        Assert.Equal("1~2교시", vm.SelectedOccupiedSlotsText);
        Assert.Equal("연속 2시간 블록", vm.SelectedBlockText);
        Assert.Equal("예", vm.SelectedFixedText);
        Assert.Equal("공동교수", vm.SelectedCoteachText);
        Assert.Equal("예 (강의실1, 강의실2)", vm.SelectedMultiRoomText);
        Assert.Equal("강의실1, 강의실2", vm.SelectedAllowedRoomsText);
        Assert.Equal("이동 불가", vm.SelectedMoveStateText);
        Assert.Contains("HC-13", vm.SelectedBlockedReasonText);
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
    public void ApplyRoomChange_NewConflicts_PromptsAndUserAccepts_KeepsChange()
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

        // Dialog prompted with the new room conflict
        Assert.Single(_dialog.Calls);
        Assert.Contains(_dialog.Calls[0], c => c.Type == ConflictType.RoomConflict);
        // Change kept → conflict persists in right panel
        Assert.Equal(new[] { "R2" }, vm.SelectedAssignment!.Rooms);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.RoomConflict);
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
        Assert.Single(_dialog.Calls);
        // Change was reverted — no conflicts remain (clean start state)
        Assert.Empty(vm.Conflicts);
        Assert.Contains("취소", vm.StatusMessage);
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
    public void HandleCellClick_Len2TargetRangeContainsLen1Course_ForceMovesAndKeepsExistingCourse()
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

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 2 && c.Assignment.CourseId == "Y-01");
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void HandleCellClick_Len1IntoSecondSlotOfLen2Course_ForceMovesAndKeepsBothBlocks()
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

        Assert.Single(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01" && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 2 && c.Assignment.CourseId == "Y-01" && c.Assignment.RowSpan == 1);
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
    public void HandleCellClick_Len2OntoLen2Course_ForceMovesAndKeepsBothBlocks()
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

        Assert.Single(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01" && c.Assignment.RowSpan == 2);
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
    public void HandleCellClick_SameGradeDifferentCourseTarget_ForceMovesAndRecordsHc11()
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

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.GradeConflict && c.Severity == ConflictSeverity.Error);
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
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, target);
        Assert.Single(vm.WorkingCrossLinks);

        vm.UndoCommand.Execute(null);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");

        vm.RedoCommand.Execute(null);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void Undo_AfterMoveReleasesCross_RestoresCrossLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, target);
        vm.SelectCell(1, 1, 2, 1, AssignmentAt(vm, 1, 1, "X-01"));
        vm.HandleCellClick(2, 1, 2, 0, null);
        Assert.Empty(vm.WorkingCrossLinks);

        vm.UndoCommand.Execute(null);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair("X-01", "Y-01"));
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
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
    public void HandleCrossAddRequested_AllowsSameGradeDifferentTimePairAndMovesImmediately()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(1, 1, 2, 0, target);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains("크로스가 설정되었습니다", vm.StatusMessage);
        Assert.Null(vm.SelectedAssignment);
        Assert.Equal("-", vm.SelectedCrossGroupText);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void DifferentTimeCrossLink_AllowsLaterHc11OverlapOnly()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, target);

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
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, target);

        Assert.Contains("크로스가 설정되었습니다", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Equal(2, vm.Grid.DayGroups[1].Grades.Single(g => g.Grade == 2).Width);
        Assert.Equal("Y-01", vm.Grid.Cells.Single(c => c.Day == 1 && c.Period == 1 && c.SubColumnIdx == 0).Assignment.CourseId);
        Assert.Equal("X-01", vm.Grid.Cells.Single(c => c.Day == 1 && c.Period == 1 && c.SubColumnIdx == 1).Assignment.CourseId);
    }

    [Fact]
    public void NonCrossOccupiedTarget_ForceMovesSelectedCourse()
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

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
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

        Assert.Contains("HC-02", vm.StatusMessage);
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
            new SolutionAssignment("Y-01", 1, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R1" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, target);

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
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
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
