using Microsoft.Extensions.DependencyInjection;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.Tests.ViewModel;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _sp;

    public MainWindowViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"main_test_{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddTimetableScheduler(_dbPath);
        _sp = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _sp.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void DI_ResolvesAllPageVms()
    {
        Assert.NotNull(_sp.GetRequiredService<TimetableSelectionViewModel>());
        Assert.NotNull(_sp.GetRequiredService<DataInputViewModel>());
        Assert.NotNull(_sp.GetRequiredService<ResultsViewModel>());
        Assert.NotNull(_sp.GetRequiredService<ManualEditViewModel>());
        Assert.NotNull(_sp.GetRequiredService<MainWindowViewModel>());
    }

    [Fact]
    public void Workspace_IsSingleton()
    {
        var w1 = _sp.GetRequiredService<WorkspaceService>();
        var w2 = _sp.GetRequiredService<WorkspaceService>();
        Assert.Same(w1, w2);
    }

    [Fact]
    public void DefaultPage_IsSelection()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        Assert.IsType<TimetableSelectionViewModel>(main.CurrentPage);
    }

    [Fact]
    public void CreateNew_NavigatesToInputPage()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        main.Selection.CreateNewCommand.Execute(null);
        Assert.IsType<DataInputViewModel>(main.CurrentPage);
        Assert.Equal("정보 입력", main.CurrentPage.Title);
    }

    [Fact]
    public void CreateNew_StartsWithEmptySessionWorkspace()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        var global = _sp.GetRequiredService<WorkspaceService>();
        global.AddCourse(new TimetableScheduler.Domain.Course
        {
            Id = "GLOBAL-01",
            Name = "전역과목",
            Grade = 1,
            HoursPerWeek = 1,
        });
        global.AddProfessor(new TimetableScheduler.Domain.Professor { Id = "P1", Name = "전역교수" });
        global.AddRoom(new TimetableScheduler.Domain.Room { Id = "R1", Name = "전역강의실" });

        main.Selection.CreateNewCommand.Execute(null);

        Assert.True(main.Input.IsSessionMode);
        Assert.False(main.Input.IsExistingMode);
        Assert.Empty(main.Input.Workspace.Courses);
        Assert.Empty(main.Input.Workspace.Professors);
        Assert.Empty(main.Input.Workspace.Rooms);
        Assert.Single(global.Courses);
    }

    [Fact]
    public void CreateNew_EditsDoNotTouchGlobalDbUntilTimetableSave()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        var global = _sp.GetRequiredService<WorkspaceService>();
        global.AddCourse(new TimetableScheduler.Domain.Course
        {
            Id = "GLOBAL-01",
            Name = "전역과목",
            Grade = 1,
            HoursPerWeek = 1,
        });

        main.Selection.CreateNewCommand.Execute(null);
        main.Input.Workspace.AddCourse(new TimetableScheduler.Domain.Course
        {
            Id = "SESSION-01",
            Name = "세션과목",
            Grade = 2,
            HoursPerWeek = 1,
        });

        Assert.Equal("SESSION-01", main.Input.Workspace.Courses.Single().Id);
        Assert.Equal("GLOBAL-01", global.Courses.Single().Id);
    }

    [Fact]
    public void Edit_NavigatesToInputPageInExistingMode()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        var workspace = _sp.GetRequiredService<WorkspaceService>();
        workspace.SaveTimetable("nav-test", Array.Empty<TimetableScheduler.Solver.SolutionAssignment>());
        var record = workspace.SavedTimetables.First(t => t.Name == "nav-test");

        main.Selection.EditCommand.Execute(record);

        Assert.IsType<DataInputViewModel>(main.CurrentPage);
        Assert.Equal("정보 입력", main.CurrentPage.Title);
        Assert.True(main.Input.IsExistingMode);
    }

    [Fact]
    public void EditThenGoToManual_NavigatesToManualPage()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        var workspace = _sp.GetRequiredService<WorkspaceService>();
        workspace.SaveTimetable("nav-test", Array.Empty<TimetableScheduler.Solver.SolutionAssignment>());
        var record = workspace.SavedTimetables.First(t => t.Name == "nav-test");

        main.Selection.EditCommand.Execute(record);
        main.Input.GoToManualCommand.Execute(null);

        Assert.IsType<ManualEditViewModel>(main.CurrentPage);
        Assert.Equal("수동 편집", main.CurrentPage.Title);
        Assert.Equal("nav-test", main.Manual.SaveName);
    }

    [Fact]
    public void CreateNew_DisablesGoToManual()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        main.Selection.CreateNewCommand.Execute(null);

        Assert.False(main.Input.IsExistingMode);
        Assert.False(main.Input.GoToManualCommand.CanExecute(null));
    }

    [Fact]
    public void SaveTimetable_NavigatesBackToSelectionPage()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        var workspace = _sp.GetRequiredService<WorkspaceService>();
        workspace.SaveTimetable("nav-test", Array.Empty<TimetableScheduler.Solver.SolutionAssignment>());
        var record = workspace.SavedTimetables.First(t => t.Name == "nav-test");
        main.Selection.EditCommand.Execute(record);
        main.Input.GoToManualCommand.Execute(null);

        main.Manual.SaveTimetableCommand.Execute(null);

        Assert.IsType<TimetableSelectionViewModel>(main.CurrentPage);
        Assert.Equal("시간표 선택", main.CurrentPage.Title);
    }

    [Fact]
    public void ResultsEditSelected_LiveWorkspace_PreservesProfessorInfoInManualEdit()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        var workspace = _sp.GetRequiredService<WorkspaceService>();
        workspace.AddProfessor(new Professor { Id = "P1", Name = "김교수" });
        workspace.AddProfessor(new Professor { Id = "P2", Name = "박교수" });
        workspace.AddRoom(new Room { Id = "R1", Name = "공학관 101" });
        workspace.AddCourse(new Course
        {
            Id = "C-01",
            Name = "알고리즘",
            Grade = 2,
            HoursPerWeek = 1,
            CourseType = "전필",
            ProfessorId = "P1",
            CoteachProfs = new List<string> { "P2" },
            BlockStructure = new List<int> { 1 },
        });

        main.Results.SetSolutions(new[]
        {
            new RankedSolution(
                new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
                new SolutionScore(0, 0, 0, 0)),
        });

        main.Results.EditSelectedCommand.Execute(null);

        var assignment = Assert.Single(main.Manual.Grid.Cells).Assignment;
        Assert.Equal("김교수, 박교수", assignment.ProfessorLine);
    }

    [Fact]
    public void ResultsEditSelected_SessionSnapshot_PreservesProfessorInfoInManualEdit()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        var snapshot = new AppData(
            new List<Course>
            {
                new()
                {
                    Id = "C-01",
                    Name = "알고리즘",
                    Grade = 2,
                    HoursPerWeek = 1,
                    CourseType = "전필",
                    ProfessorId = "P1",
                    CoteachProfs = new List<string> { "P2" },
                    BlockStructure = new List<int> { 1 },
                },
            },
            new List<Professor>
            {
                new() { Id = "P1", Name = "김교수" },
                new() { Id = "P2", Name = "박교수" },
            },
            new List<Room> { new() { Id = "R1", Name = "공학관 101" } },
            new List<CrossGroup>(),
            new List<RetakeScenario>());

        main.Results.SetSolutions(new[]
        {
            new RankedSolution(
                new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
                new SolutionScore(0, 0, 0, 0)),
        }, snapshot);

        main.Results.EditSelectedCommand.Execute(null);

        var assignment = Assert.Single(main.Manual.Grid.Cells).Assignment;
        Assert.Equal("김교수, 박교수", assignment.ProfessorLine);
    }

    [Fact]
    public void ResultsBackToInput_SessionWorkspace_PreservesCourseProfessorIdsAndProfessorList()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();

        main.Selection.CreateNewCommand.Execute(null);
        main.Input.Workspace.AddProfessor(new Professor { Id = "P1", Name = "김교수" });
        main.Input.Workspace.AddRoom(new Room { Id = "R1", Name = "공학관 101" });
        main.Input.Workspace.AddCourse(new Course
        {
            Id = "C-01",
            Name = "알고리즘",
            Grade = 2,
            HoursPerWeek = 1,
            CourseType = "전필",
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1 },
        });

        main.Results.SetSolutions(new[]
        {
            new RankedSolution(
                new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
                new SolutionScore(0, 0, 0, 0)),
        }, main.Input.CurrentSnapshot());

        var beforeGroup = main.Input.CourseGroups.Single();

        main.Results.BackCommand.Execute(null);

        Assert.NotSame(beforeGroup, main.Input.CourseGroups.Single());
        Assert.Equal("P1", main.Input.Workspace.Courses.Single().ProfessorId);
        Assert.Equal("김교수", main.Input.CourseGroups.Single().HeaderProfessor);
        Assert.Equal("P1", main.Input.CourseGroups.Single().Sections[0].ProfessorId);
        Assert.Single(main.Input.Workspace.Professors);
        Assert.Equal("P1", main.Input.Workspace.Professors.Single().Id);
    }
}
