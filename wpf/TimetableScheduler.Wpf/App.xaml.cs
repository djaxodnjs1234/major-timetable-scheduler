using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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
    private static int isShowingExceptionMessage;

    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();

        try
        {
            var dataDir = FindDataDir();
            var dbPath = Path.Combine(dataDir, DbFileName);

            var services = new ServiceCollection();
            services.AddTimetableScheduler(dbPath);
            // Override the default no-op dialog with the WPF MessageBox impl
            services.AddSingleton<IConflictDialogService, MessageBoxConflictDialogService>();
            Services = services.BuildServiceProvider();

            var autoImportFailed = AutoImportIfEmpty(dataDir);

            var window = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
            window.Show();
            if (autoImportFailed)
            {
                MessageBox.Show(
                    window,
                    "불러오기에 실패하였습니다.",
                    "불러오기 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            ShowFatalException(ex);
            Shutdown(-1);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            ShowRecoverableException(args.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            ShowRecoverableException(args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception
                ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown error");
            ShowFatalException(exception);
        };
    }

    private static void ShowRecoverableException(Exception exception)
    {
        var app = Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(new Action(() => ShowRecoverableException(exception)));
            return;
        }

        ShowExceptionMessage(
            "오류",
            "예상치 못한 오류가 발생했지만 화면은 유지됩니다.\n\n" +
            "작업을 다시 시도해 보세요. 같은 문제가 반복되면 입력값을 확인한 뒤 앱을 다시 시작하세요.\n\n" +
            $"상세: {RootMessage(exception)}");
    }

    private static void ShowFatalException(Exception exception)
    {
        ShowExceptionMessage(
            "오류",
            "앱을 계속 실행할 수 없는 오류가 발생했습니다.\n\n" +
            "앱을 다시 시작한 뒤 같은 문제가 반복되면 마지막으로 수정한 입력값을 확인해 주세요.\n\n" +
            $"상세: {RootMessage(exception)}");
    }

    private static void ShowExceptionMessage(string title, string message)
    {
        if (Interlocked.Exchange(ref isShowingExceptionMessage, 1) == 1)
            return;

        try
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Volatile.Write(ref isShowingExceptionMessage, 0);
        }
    }

    private static string RootMessage(Exception exception) =>
        exception.GetBaseException().Message;

    private bool AutoImportIfEmpty(string dataDir)
    {
        var workspace = Services.GetRequiredService<WorkspaceService>();
        if (workspace.Courses.Count > 0) return false;

        var xlsx = Path.Combine(dataDir, XlsxFileName);
        if (!File.Exists(xlsx)) return false;
        try
        {
            workspace.ImportFromXlsx(xlsx);
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XLSX_IMPORT] Auto import failed: {ex}");
            return true;
        }
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
