using Microsoft.Extensions.DependencyInjection;
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
    public void GoToInputCommand_NavigatesToInputPage()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        main.GoToInputCommand.Execute(null);
        Assert.IsType<DataInputViewModel>(main.CurrentPage);
    }

    [Fact]
    public void Title_ChangesWithPage()
    {
        var main = _sp.GetRequiredService<MainWindowViewModel>();
        Assert.Equal("시간표 선택", main.CurrentPage.Title);
        main.GoToInputCommand.Execute(null);
        Assert.Equal("정보 입력", main.CurrentPage.Title);
        main.GoToResultsCommand.Execute(null);
        Assert.Equal("해 미리보기", main.CurrentPage.Title);
    }
}
