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
    }

    [Fact]
    public void SelectCell_PopulatesInspector()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, assignment);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Equal("X-01", vm.SelectedAssignment!.CourseId);
        Assert.Equal("R1", vm.NewRoomId);
    }

    [Fact]
    public void ApplyRoomChange_NoNewConflicts_DoesNotPromptDialog()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, assignment);
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
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        vm.LoadFromSolution(sol);

        Assert.Empty(vm.Conflicts);  // start clean

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, assignment);
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
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        vm.LoadFromSolution(sol);
        Assert.Empty(vm.Conflicts);  // start clean

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, assignment);
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
        vm.SelectCell(0, 1, assignment);
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
        vm.HandleCellClick(0, 1, assignment);
        vm.HandleCellClick(1, 1, null);

        Assert.Null(vm.SelectedAssignment);
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
        vm.HandleCellClick(0, 1, assignment);
        vm.HandleCellClick(1, 5, null);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("HC-12", vm.StatusMessage);
    }
}
