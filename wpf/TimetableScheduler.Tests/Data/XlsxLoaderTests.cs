using TimetableScheduler.Data;

namespace TimetableScheduler.Tests.Data;

public class XlsxLoaderTests
{
    [Fact]
    public void Load_RealXlsx_ReturnsNonEmpty()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);
        Assert.NotEmpty(data.Courses);
        Assert.NotEmpty(data.Professors);
        Assert.NotEmpty(data.Rooms);
    }

    [Fact]
    public void Load_RealXlsx_AllCoursesValid()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);
        Assert.Equal(
            Enumerable.Range(1, data.Courses.Count).Select(n => n.ToString()),
            data.Courses.Select(c => c.Id));
        foreach (var c in data.Courses)
        {
            Assert.False(string.IsNullOrEmpty(c.Id));
            Assert.True(c.Grade is >= 1 and <= 4);
            Assert.True(c.HoursPerWeek > 0);
            Assert.True(c.CourseType is "전필" or "전선");
            Assert.NotEmpty(c.BlockStructure);
            Assert.Equal(c.HoursPerWeek, c.BlockStructure.Sum());
        }
    }

    [Fact]
    public void Load_RealXlsx_BlockStructure3hr_Is21()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);
        var threeHr = data.Courses.Where(c => c.HoursPerWeek == 3).ToList();
        if (threeHr.Count == 0) return;
        Assert.All(threeHr, c => Assert.Equal(new[] { 2, 1 }, c.BlockStructure));
    }

    [Fact]
    public void Load_RealXlsx_BlockStructure4hr_Is22()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);
        var fourHr = data.Courses.Where(c => c.HoursPerWeek == 4).ToList();
        if (fourHr.Count == 0) return;
        Assert.All(fourHr, c => Assert.Equal(new[] { 2, 2 }, c.BlockStructure));
    }
}
