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
    private const string DbFileName = "timetable.db";

    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var dataDir = FindDataDir();
        var dbPath = Path.Combine(dataDir, DbFileName);

        var services = new ServiceCollection();
        services.AddTimetableScheduler(dbPath);
        // Override the default no-op dialog with the WPF MessageBox impl
        services.AddSingleton<IConflictDialogService, MessageBoxConflictDialogService>();
        Services = services.BuildServiceProvider();

        AutoImportIfEmpty(dataDir);

        var window = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainWindowViewModel>(),
        };
        window.Show();

        base.OnStartup(e);
    }

    private void AutoImportIfEmpty(string dataDir)
    {
        var workspace = Services.GetRequiredService<WorkspaceService>();
        if (workspace.Courses.Count > 0) return;

        var xlsx = Path.Combine(dataDir, XlsxFileName);
        if (!File.Exists(xlsx)) return;
        try { workspace.ImportFromXlsx(xlsx); }
        catch { /* ignore — user can manually import */ }
    }

    /// <summary>
    /// exe 위치에서 상위로 탐색하며 data/ 폴더를 찾는다.
    /// 없으면 exe 옆에 data/ 폴더를 생성해 반환.
    /// </summary>
    public static string FindDataDir()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "data");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        var fallback = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
