using TimetableScheduler.Data;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Tests.Data;

public class TimetableXlsxRoundTripTests
{
    [Fact]
    public void FormattedExport_Then_Import_PreservesRows()
    {
        var rows = new List<TimetableAssignmentRow>
        {
            new("GA1004-01", 0, 1, "D327"),
            new("GA1004-01", 0, 2, "D327"),
            new("GA1005-01", 1, 3, "D438"),
        };
        var courses = new List<Course>
        {
            new() { Id = "GA1004-01", Name = "자료구조", Grade = 2, Section = 1, HoursPerWeek = 4 },
            new() { Id = "GA1005-01", Name = "컴퓨터구조", Grade = 2, Section = 1, HoursPerWeek = 3 },
        };
        var profs = new List<Professor>();

        var path = Path.Combine(Path.GetTempPath(), $"roundtrip_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export("테스트", rows, courses, profs, path);
            var imported = TimetableXlsxService.Import(path);

            Assert.Equal(rows.Count, imported.Count);
            foreach (var orig in rows)
                Assert.Contains(imported, r =>
                    r.CourseId == orig.CourseId && r.Day == orig.Day &&
                    r.Period == orig.Period && r.RoomId == orig.RoomId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
