using TimetableScheduler.Data;

namespace TimetableScheduler.Tests.Data;

public class CompareWithPythonTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "개설강좌 편람.xlsx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void Counts_MatchPythonBaseline()
    {
        var path = Path.Combine(FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);
        // Python baseline (data.xlsx_loader.load_from_xlsx): 29/12/9
        Assert.Equal(29, data.Courses.Count);
        Assert.Equal(12, data.Professors.Count);
        Assert.Equal(9, data.Rooms.Count);
    }

    [Fact]
    public void FirstCourse_MatchesPythonBaseline()
    {
        var path = Path.Combine(FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);
        var c0 = data.Courses[0];
        Assert.Equal("GA1004-01", c0.Id);
        Assert.Equal(2, c0.Grade);
        Assert.Equal(4, c0.HoursPerWeek);
        Assert.Equal("전필", c0.CourseType);
        Assert.Equal(new[] { 2, 2 }, c0.BlockStructure);
        Assert.Equal(new[] { "D327" }, c0.FixedRooms);
    }
}
