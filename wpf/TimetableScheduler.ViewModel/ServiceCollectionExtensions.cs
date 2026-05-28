using Microsoft.Extensions.DependencyInjection;
using TimetableScheduler.Data;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.ViewModel.Services;

namespace TimetableScheduler.ViewModel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTimetableScheduler(
        this IServiceCollection services,
        string? dbPath = null)
    {
        services.AddSingleton(_ => dbPath != null
            ? new SqliteRepository(dbPath)
            : SqliteRepository.OpenNextToExe());

        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<SolverService>();
        services.AddSingleton<IConflictDialogService, NullConflictDialogService>();

        services.AddSingleton<TimetableSelectionViewModel>();
        services.AddSingleton<DataInputViewModel>();
        services.AddSingleton<ResultsViewModel>();
        services.AddSingleton<ManualEditViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services;
    }
}
