using ClosedXML.Excel;
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
        Assert.Equal(data.Courses.Count, data.Courses.Select(course => course.Id).Distinct().Count());
        foreach (var course in data.Courses)
        {
            Assert.False(string.IsNullOrEmpty(course.Id));
            Assert.True(course.Grade is >= 1 and <= 4);
            Assert.True(course.HoursPerWeek > 0);
            Assert.True(course.CourseType is "전필" or "전선");
            Assert.NotEmpty(course.BlockStructure);
            Assert.Equal(course.HoursPerWeek, course.BlockStructure.Sum());
        }
    }

    [Fact]
    public void Load_RealXlsx_BlockStructure3hr_Is12()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);
        var threeHourCourses = data.Courses.Where(course => course.HoursPerWeek == 3).ToList();
        if (threeHourCourses.Count == 0) return;
        Assert.All(threeHourCourses, course => Assert.Equal(new[] { 1, 2 }, course.BlockStructure));
    }

    [Fact]
    public void Load_RealXlsx_BlockStructure4hr_Is22()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);
        var fourHourCourses = data.Courses.Where(course => course.HoursPerWeek == 4).ToList();
        if (fourHourCourses.Count == 0) return;
        Assert.All(fourHourCourses, course => Assert.Equal(new[] { 2, 2 }, course.BlockStructure));
    }

    [Fact]
    public void Load_ExplicitSectionIds_StayAsSeparateCourses()
    {
        var path = CreateWorkbook(
            ("2", "\uD544\uC218", "\uC790\uB8CC\uAD6C\uC870", "3", "GA1004-01", "\uD64D\uAE38\uB3D9", "\uCEF4\uD4E8\uD130", "\uC6D423/A101"),
            ("2", "\uD544\uC218", "\uC790\uB8CC\uAD6C\uC870", "3", "GA1004-02", "\uD64D\uAE38\uB3D9", "\uCEF4\uD4E8\uD130", "\uD65423/A102"));

        var data = XlsxLoader.Load(path);

        Assert.Equal(new[] { "1-01", "1-02" }, data.Courses.Select(course => course.Id));
        Assert.Equal(new[] { 1, 2 }, data.Courses.Select(course => course.Section));
    }

    [Fact]
    public void Load_MixedCourses_AssignsSequentialImportedBaseIdsFromOne()
    {
        var path = CreateWorkbook(
            ("2", "\uD544\uC218", "\uC790\uB8CC\uAD6C\uC870", "3", "GA1004-01", "\uD64D\uAE38\uB3D9", "\uCEF4\uD4E8\uD130", "\uC6D423/A101"),
            ("2", "\uD544\uC218", "\uC790\uB8CC\uAD6C\uC870", "3", "GA1004-02", "\uD64D\uAE38\uB3D9", "\uCEF4\uD4E8\uD130", "\uD65423/A102"),
            ("3", "\uC120\uD0DD", "\uB370\uC774\uD130\uD1B5\uC2E0", "3", "GB2001", "\uAE40\uAD50\uC218", "\uCEF4\uD4E8\uD130", "\uBAA923/B201"));

        var data = XlsxLoader.Load(path);

        Assert.Equal(new[] { "1-01", "1-02", "2" }, data.Courses.Select(course => course.Id).OrderBy(id => id));
    }

    [Fact]
    public void Load_CapstoneDesignExplicitSectionIds_CollapseToSingleCourse()
    {
        var path = CreateWorkbook(
            ("4", "\uD544\uC218", "\uCEA1\uC2A4\uD1A4\uB514\uC790\uC778", "3", "CP1001-01", "\uAE40\uAD50\uC218", "\uCEF4\uD4E8\uD130", "\uC6D423/B201"),
            ("4", "\uD544\uC218", "\uCEA1\uC2A4\uD1A4\uB514\uC790\uC778", "3", "CP1001-02", "\uAE40\uAD50\uC218", "\uCEF4\uD4E8\uD130", "\uBAA923/B202"));

        var data = XlsxLoader.Load(path);

        var course = Assert.Single(data.Courses);
        Assert.Equal("1", course.Id);
        Assert.Equal(1, course.Section);
        Assert.Equal(new[] { 1, 2 }, course.BlockStructure);
    }

    [Fact]
    public void Load_MapsProfessorAndRequiredTypeAndDefaultBlocks()
    {
        var path = CreateWorkbook(
            ("3", "\uD544\uC218", "\uC54C\uACE0\uB9AC\uC998", "3", "GA2001", "\uD64D\uAE38\uB3D9", "\uCEF4\uD4E8\uD130", "\uD65423/A101"));

        var data = XlsxLoader.Load(path);

        var course = Assert.Single(data.Courses);
        var professor = Assert.Single(data.Professors);
        Assert.Equal("전필", course.CourseType);
        Assert.Equal(new[] { 1, 2 }, course.BlockStructure);
        Assert.Equal(professor.Id, course.ProfessorId);
        Assert.Equal("홍길동", professor.Name);
    }

    private static string CreateWorkbook(params (string Grade, string Type, string Name, string Hours, string Code, string Professor, string Department, string Schedule)[] rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");

        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var excelRow = index + 1;
            sheet.Cell(excelRow, "E").Value = row.Grade;
            sheet.Cell(excelRow, "F").Value = row.Type;
            sheet.Cell(excelRow, "G").Value = row.Name;
            sheet.Cell(excelRow, "H").Value = row.Hours;
            sheet.Cell(excelRow, "I").Value = row.Code;
            sheet.Cell(excelRow, "Q").Value = row.Professor;
            sheet.Cell(excelRow, "R").Value = row.Department;
            sheet.Cell(excelRow, "S").Value = row.Schedule;
        }

        workbook.SaveAs(path);
        return path;
    }
}
