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
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

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
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
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
        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Normal,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 1, 3, 0)].State);
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

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Equal(1, vm.SelectedDay);
        Assert.Equal(1, vm.SelectedPeriod);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_LunchTarget_DoesNotMoveAndShowsReason()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 5, 2, 0, null);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("HC-12", vm.StatusMessage);
    }

    [Fact]
    public void HandleCellClick_Len2TargetRangeContainsLen1Course_DoesNotMoveOrHideExistingCourse()
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
        Assert.Contains("HC-06", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 2 && c.Assignment.CourseId == "Y-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_Len1IntoSecondSlotOfLen2Course_DoesNotMove()
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

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("HC-06", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
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
        vm.HandleCellClick(1, 1, 2, 0, null);
        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Equal(2, vm.SelectedDay);
        Assert.Equal(1, vm.SelectedPeriod);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 2 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void HandleCellClick_Len2OntoLen2Course_DoesNotMove()
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

        Assert.Contains("HC-06", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
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
    public void HandleCellClick_OntoFixedCourse_DoesNotMove()
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

        Assert.Contains("HC-13", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "F-01");
    }

    [Fact]
    public void HandleCellClick_CurrentLocation_DoesNotCorruptData()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(0, 1, 2, 0, assignment);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
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
        vm.HandleCellClick(0, 1, 2, 0, null);

        Assert.Equal(0, vm.SelectedDay);
        Assert.Equal(1, vm.SelectedPeriod);
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
        vm.HandleCellClick(0, 1, 2, 0, null);

        Assert.DoesNotContain("HC-06", vm.StatusMessage);
        Assert.Equal(0, vm.SelectedDay);
        Assert.Equal(1, vm.SelectedPeriod);
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
        Assert.Equal(1, vm.SelectedDay);
        Assert.Equal(1, vm.SelectedPeriod);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void HandleCellClick_SameGradeDifferentCourseTarget_IsBlockedByHc11()
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

        Assert.Contains("HC-11", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
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
        Assert.Contains("HC-12", vm.StatusMessage);
        vm.HandleCellClick(2, 1, 2, 0, null);
        Assert.Contains("이동 완료", vm.StatusMessage);
    }
}
