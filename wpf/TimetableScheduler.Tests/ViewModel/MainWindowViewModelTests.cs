using Microsoft.Extensions.DependencyInjection;
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
}
