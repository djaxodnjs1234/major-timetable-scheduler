using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.ViewModel.Services;
using TimetableScheduler.Wpf.Services;

namespace TimetableScheduler.Wpf;

public partial class App : Application
{
    private const string XlsxFileName = "개설강좌 편람.xlsx";

    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        services.AddTimetableScheduler();
        // Override the default no-op dialog with the WPF MessageBox impl
        services.AddSingleton<IConflictDialogService, MessageBoxConflictDialogService>();
        Services = services.BuildServiceProvider();

        AutoImportIfEmpty();

        var window = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainWindowViewModel>(),
        };
        window.Show();

        base.OnStartup(e);
    }

    private void AutoImportIfEmpty()
    {
        var workspace = Services.GetRequiredService<WorkspaceService>();
        if (workspace.Courses.Count > 0) return;

        var xlsx = FindXlsx();
        if (xlsx == null) return;
        try { workspace.ImportFromXlsx(xlsx); }
        catch { /* ignore — user can manually import */ }
    }

    private static string? FindXlsx()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, XlsxFileName);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
