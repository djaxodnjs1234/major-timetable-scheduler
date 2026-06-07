using Microsoft.Extensions.DependencyInjection;
using TimetableScheduler.Domain;
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
    public void ProfessorViews_IncludeCoteachingCourse_ForEachProfessor()
    {
        var workspace = _sp.GetRequiredService<WorkspaceService>();
        workspace.AddProfessor(new Professor { Id = "P1", Name = "대표" });
        workspace.AddProfessor(new Professor { Id = "P2", Name = "공동" });
        workspace.AddProfessor(new Professor { Id = "P3", Name = "무관" });
        workspace.AddRoom(new Room { Id = "R1", Name = "강의실" });
        workspace.AddCourse(new Course
        {
            Id = "T-01",
            Name = "팀티칭",
            Grade = 4,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            CoteachProfs = new List<string> { "P2" },
        });
        workspace.SaveTimetable("team", new[] { new SolutionAssignment("T-01", 0, 1, "R1") });

        var vm = _sp.GetRequiredService<TimetableSelectionViewModel>();
        vm.SelectedTimetable = workspace.SavedTimetables.Single(t => t.Name == "team");

        Assert.True(vm.ProfessorViews.Single(v => v.Id == "P1").Grid.CellAt(0, 1).IsOccupied);
        Assert.True(vm.ProfessorViews.Single(v => v.Id == "P2").Grid.CellAt(0, 1).IsOccupied);
        Assert.False(vm.ProfessorViews.Single(v => v.Id == "P3").Grid.CellAt(0, 1).IsOccupied);
    }
}
