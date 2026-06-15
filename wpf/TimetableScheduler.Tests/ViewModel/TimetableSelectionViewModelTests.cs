using ClosedXML.Excel;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.Tests.ViewModel;

public class TimetableSelectionViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteRepository _repo;

    public TimetableSelectionViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"selection_vm_{Guid.NewGuid():N}.db");
        _repo = new SqliteRepository(_dbPath);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void SavedPreview_UsesEmbeddedSnapshot_WhenLiveWorkspaceChanged()
    {
        var workspace = new WorkspaceService(_repo);
        workspace.AddProfessor(new Professor { Id = "P1", Name = "스냅교수" });
        workspace.AddProfessor(new Professor { Id = "P2", Name = "팀티칭교수" });
        workspace.AddRoom(new Room { Id = "R1", Name = "스냅강의실" });
        workspace.AddCourse(new Course
        {
            Id = "C-01",
            Name = "스냅과목",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            CoteachProfs = new List<string> { "P2" },
            BlockStructure = new List<int> { 1 },
        });

        var savedSnapshot = workspace.Snapshot();
        workspace.SaveTimetable(
            "saved",
            new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
            snapshot: savedSnapshot);

        workspace.DeleteCourse(workspace.Courses.Single());
        workspace.DeleteProfessor("P1");
        workspace.DeleteRoom("R1");

        var vm = new TimetableSelectionViewModel(workspace);

        var previewCell = Assert.Single(vm.Preview.Cells);
        Assert.Equal("C-01", previewCell.Assignment.CourseId);
        Assert.Equal("스냅교수", previewCell.Assignment.ProfessorLabel);
        Assert.Equal("스냅강의실", previewCell.Assignment.RoomsLabel);
        Assert.Single(vm.RoomViews);
        Assert.Equal("스냅강의실", vm.RoomViews[0].Name);
        Assert.Equal(2, vm.ProfessorViews.Count);
        Assert.Equal("스냅교수", vm.ProfessorViews[0].Name);
        var coteacherView = vm.ProfessorViews.Single(v => v.Id == "P2");
        var occupied = Assert.Single(coteacherView.Grid.Cells.Where(c => c.IsOccupied));
        Assert.Equal("C-01", Assert.Single(occupied.Items).CourseId);
    }

    [Fact]
    public void SaveTimetable_AutoSelectsNewRecord_AndBuildsPreviewOnLiveSelectionVm()
    {
        var workspace = new WorkspaceService(_repo);
        var vm = new TimetableSelectionViewModel(workspace);

        Assert.Null(vm.SelectedTimetable);
        Assert.Empty(vm.Preview.Cells);

        workspace.AddProfessor(new Professor { Id = "P1", Name = "저장교수" });
        workspace.AddRoom(new Room { Id = "R1", Name = "저장강의실" });
        workspace.AddCourse(new Course
        {
            Id = "C-01",
            Name = "저장과목",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1 },
        });

        workspace.SaveTimetable(
            "saved",
            new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
            snapshot: workspace.Snapshot());

        Assert.NotNull(vm.SelectedTimetable);
        Assert.Equal("saved", vm.SelectedTimetable!.Name);
        var previewCell = Assert.Single(vm.Preview.Cells);
        Assert.Equal("C-01", previewCell.Assignment.CourseId);
        Assert.Equal("저장교수", previewCell.Assignment.ProfessorLabel);
        Assert.Equal("저장강의실", previewCell.Assignment.RoomsLabel);
    }

    [Fact]
    public void SavedPreview_InvalidSnapshotWithBlankProfessorAndCourseType_ShowsNoCells()
    {
        var workspace = new WorkspaceService(_repo);
        workspace.AddProfessor(new Professor { Id = "P1", Name = "라이브교수" });
        workspace.AddRoom(new Room { Id = "R1", Name = "라이브강의실" });
        workspace.AddCourse(new Course
        {
            Id = "C-01",
            Name = "라이브과목",
            Grade = 2,
            Section = 1,
            HoursPerWeek = 1,
            CourseType = "전필",
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1 },
        });

        var incompleteSnapshot = new AppData(
            new List<Course>
            {
                new()
                {
                    Id = "C-01",
                    Name = "라이브과목",
                    Grade = 2,
                    Section = 1,
                    HoursPerWeek = 1,
                    CourseType = "",
                    ProfessorId = "",
                },
            },
            new List<Professor>(),
            new List<Room>(),
            new List<CrossGroup>(),
            new List<RetakeScenario>());

        workspace.SaveTimetable(
            "saved",
            new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
            snapshot: incompleteSnapshot);

        var vm = new TimetableSelectionViewModel(workspace);

        Assert.Empty(vm.Preview.Cells);
        Assert.All(vm.GradeViews, view => Assert.Empty(view.Grid.Cells.Where(c => c.IsOccupied)));
    }

    [Fact]
    public void FirstScreenExport_UsesSnapshot_AndShowsGradeHeadersOnUnifiedAndGradeSheets()
    {
        var workspace = new WorkspaceService(_repo);
        workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor One" });
        workspace.AddRoom(new Room { Id = "R1", Name = "Room One" });
        workspace.AddCourse(new Course
        {
            Id = "G1-01",
            Name = "Grade One Course",
            Grade = 1,
            Section = 1,
            HoursPerWeek = 1,
            CourseType = "Major",
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1 },
        });
        workspace.AddCourse(new Course
        {
            Id = "G4-01",
            Name = "Grade Four Course",
            Grade = 4,
            Section = 1,
            HoursPerWeek = 1,
            CourseType = "Major",
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1 },
        });

        workspace.SaveTimetable(
            "saved",
            new[]
            {
                new SolutionAssignment("G1-01", 0, 1, "R1"),
                new SolutionAssignment("G4-01", 0, 2, "R1"),
            },
            snapshot: workspace.Snapshot());

        foreach (var course in workspace.Courses.ToList())
            workspace.DeleteCourse(course);

        var vm = new TimetableSelectionViewModel(workspace);
        var path = Path.Combine(Path.GetTempPath(), $"first_screen_export_{Guid.NewGuid():N}.xlsx");

        try
        {
            vm.ExportXlsxCommand.Execute(path);

            using var workbook = new XLWorkbook(path);
            var unifiedSheet = workbook.Worksheet("통합 시간표");
            var gradeOneSheet = workbook.Worksheet("학년별_1학년");

            Assert.Contains("1학년", unifiedSheet.Row(5).CellsUsed().Select(cell => cell.GetString()));
            Assert.Contains("1학년", gradeOneSheet.Row(5).CellsUsed().Select(cell => cell.GetString()));
            Assert.Contains(unifiedSheet.CellsUsed(), cell => cell.GetString().Contains("Grade Four Course", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
