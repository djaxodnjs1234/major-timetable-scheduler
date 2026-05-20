namespace TimetableScheduler.Tests;

internal static class TestPaths
{
    public static string FindRepoRoot()
    {
        var starts = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };

        foreach (var start in starts)
        {
            var dir = start;
            for (int i = 0; i < 16 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "개설강좌 편람.xlsx")))
                    return dir;
                var prototypeDir = Path.Combine(dir, "prototype-py");
                if (File.Exists(Path.Combine(prototypeDir, "개설강좌 편람.xlsx")))
                    return prototypeDir;
                dir = Path.GetDirectoryName(dir);
            }
        }

        throw new InvalidOperationException("repo root not found with 개설강좌 편람.xlsx");
    }
}
