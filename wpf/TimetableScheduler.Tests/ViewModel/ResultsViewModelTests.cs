using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.Tests.ViewModel;

public class ResultsViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteRepository _repo;

    public ResultsViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"results_vm_{Guid.NewGuid():N}.db");
        _repo = new SqliteRepository(_dbPath);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void SetSolutions_UsesSessionSnapshotToResolveProfessorRoomAndCourseType()
    {
        var workspace = new WorkspaceService(_repo);
        var vm = new ResultsViewModel(workspace);
        var snapshot = new AppData(
            new List<Course>
            {
                new()
                {
                    Id = "C-01",
                    Name = "알고리즘",
                    Grade = 2,
                    Section = 1,
                    HoursPerWeek = 1,
                    CourseType = "전필",
                    ProfessorId = "P1",
                    CoteachProfs = new List<string> { "P2" },
                    BlockStructure = new List<int> { 1 },
                },
            },
            new List<Professor>
            {
                new() { Id = "P1", Name = "스냅교수" },
                new() { Id = "P2", Name = "팀티칭교수" },
            },
            new List<Room> { new() { Id = "R1", Name = "스냅강의실" } },
            new List<CrossGroup>(),
            new List<RetakeScenario>());

        var ranked = new[]
        {
            new RankedSolution(
                new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
                new SolutionScore(0, 0, 0, 0)),
        };

        vm.SetSolutions(ranked, snapshot);

        var unifiedCell = Assert.Single(vm.Unified.Cells);
        Assert.Equal("알고리즘", unifiedCell.Assignment.CourseName);
        Assert.Equal("전필", unifiedCell.Assignment.CourseType);
        Assert.Equal("스냅교수", unifiedCell.Assignment.ProfessorLabel);
        Assert.Equal("스냅강의실", unifiedCell.Assignment.RoomsLabel);
        Assert.Equal(2, unifiedCell.Assignment.Grade);
        Assert.Equal(1, unifiedCell.Assignment.Section);
        Assert.Equal(0, unifiedCell.Day);
        Assert.Equal(1, unifiedCell.Period);

        var gradeCell = Assert.Single(vm.GradeViews.Single(v => v.Id == "2").Grid.Cells.Where(c => c.IsOccupied));
        var assignment = Assert.Single(gradeCell.Items);
        Assert.Equal("알고리즘", assignment.CourseName);
        Assert.Equal("전필", assignment.CourseType);
        Assert.Equal("스냅교수", assignment.ProfessorLabel);
        Assert.Equal("스냅강의실", assignment.RoomsLabel);

        var coteacherView = vm.ProfessorViews.Single(v => v.Id == "P2");
        var occupied = Assert.Single(coteacherView.Grid.Cells.Where(c => c.IsOccupied));
        Assert.Equal("C-01", Assert.Single(occupied.Items).CourseId);
    }

    [Fact]
    public void SetSolutions_AddsGraduateGradeView()
    {
        var workspace = new WorkspaceService(_repo);
        var vm = new ResultsViewModel(workspace);
        var snapshot = new AppData(
            new List<Course>
            {
                new()
                {
                    Id = "GR-01",
                    Name = "Graduate Seminar",
                    Grade = AcademicLevels.GraduateGrade,
                    Section = 1,
                    HoursPerWeek = 1,
                    ProfessorId = "P1",
                    BlockStructure = new List<int> { 1 },
                },
            },
            new List<Professor> { new() { Id = "P1", Name = "Professor" } },
            new List<Room> { new() { Id = "R1", Name = "Room" } },
            new List<CrossGroup>(),
            new List<RetakeScenario>());

        var ranked = new[]
        {
            new RankedSolution(
                new[] { new SolutionAssignment("GR-01", 0, 1, "R1") },
                new SolutionScore(0, 0, 0, 0)),
        };

        vm.SetSolutions(ranked, snapshot);

        var graduateView = vm.GradeViews.Single(view => view.Id == AcademicLevels.GraduateGrade.ToString());
        Assert.Equal("대학원", graduateView.Name);
        var cell = Assert.Single(graduateView.Grid.Cells.Where(c => c.IsOccupied));
        Assert.Equal("GR-01", Assert.Single(cell.Items).CourseId);
    }
}
