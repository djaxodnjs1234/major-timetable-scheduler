using TimetableScheduler.Data;

namespace TimetableScheduler.Tests.Data;

public class CompareWithPythonTests
{
    [Fact]
    public void Counts_ReflectExplicitSectionImportBehavior()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);

        Assert.Equal(24, data.Courses.Count);
        Assert.Equal(12, data.Professors.Count);
        Assert.Equal(9, data.Rooms.Count);
    }

    [Fact]
    public void RealWorkbook_UsesSequentialImportedCourseIds()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);

        Assert.All(data.Courses, course =>
        {
            if (course.Id.Contains('-'))
            {
                Assert.Matches(@"^\d+-\d{2}$", course.Id);
            }
            else
            {
                Assert.Matches(@"^\d+$", course.Id);
            }
        });
    }
}
