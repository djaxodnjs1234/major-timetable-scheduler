using System.IO;
using System.Runtime.CompilerServices;

namespace TimetableScheduler.Wpf.Tests;

public class AppExceptionHandlingTests
{
    [Fact]
    public void App_RegistersGlobalExceptionHandlers()
    {
        var source = File.ReadAllText(FindAppCodeBehind());

        Assert.Contains("RegisterGlobalExceptionHandlers();", source);
        Assert.Contains("DispatcherUnhandledException", source);
        Assert.Contains("args.Handled = true;", source);
        Assert.Contains("TaskScheduler.UnobservedTaskException", source);
        Assert.Contains("args.SetObserved();", source);
        Assert.Contains("AppDomain.CurrentDomain.UnhandledException", source);
    }

    private static string FindAppCodeBehind([CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(sourceFile) ?? "", AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;

            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var candidate = Path.Combine(
                    dir.FullName,
                    "wpf",
                    "TimetableScheduler.Wpf",
                    "App.xaml.cs");
                if (File.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }

            var directCandidate = Path.Combine(start, "TimetableScheduler.Wpf", "App.xaml.cs");
            if (File.Exists(directCandidate))
                return directCandidate;
        }

        throw new FileNotFoundException("Could not find App.xaml.cs");
    }
}
