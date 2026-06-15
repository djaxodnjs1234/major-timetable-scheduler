using ClosedXML.Excel;
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

    [Fact]
    public void ExcelExport_RoomId_IsRenderedAsRoomName()
    {
        var text = ExportAndReadVisibleText(
            new List<TimetableAssignmentRow> { new("C1", 0, 1, "3") },
            new List<Course> { new() { Id = "C1", Name = "자료구조", Grade = 2, Section = 1 } },
            rooms: new List<Room> { new() { Id = "3", Name = "컴퓨터실습실" } });

        Assert.Contains("컴퓨터실습실", text);
        Assert.Contains("자료구조\n컴퓨터실습실", text);
        Assert.DoesNotContain("자료구조\n3", text);
    }

    [Fact]
    public void ExcelExport_MultipleRoomIds_AreRenderedAsRoomNames()
    {
        var text = ExportAndReadVisibleText(
            new List<TimetableAssignmentRow>
            {
                new("C1", 0, 1, "2"),
                new("C1", 0, 1, "3"),
            },
            new List<Course> { new() { Id = "C1", Name = "캡스톤디자인", Grade = 4, Section = 1 } },
            rooms: new List<Room>
            {
                new() { Id = "2", Name = "공학관 201호" },
                new() { Id = "3", Name = "컴퓨터실습실" },
            });

        Assert.Contains("공학관 201호", text);
        Assert.Contains("컴퓨터실습실", text);
        Assert.Contains("캡스톤디자인\n공학관 201호 / 컴퓨터실습실", text);
        Assert.DoesNotContain("캡스톤디자인\n2 / 3", text);
    }

    [Fact]
    public void ExcelExport_SectionA_IsRenderedAsASection()
    {
        Assert.Equal("A분반", FormattedTimetableExporter.FormatSectionForExport("A"));
    }

    [Fact]
    public void ExcelExport_SectionWithDot_IsRenderedWithoutDotAndWithSuffix()
    {
        Assert.Equal("A분반", FormattedTimetableExporter.FormatSectionForExport("A."));
    }

    [Fact]
    public void ExcelExport_SectionAlreadyHasSuffix_IsNotDuplicated()
    {
        Assert.Equal("A분반", FormattedTimetableExporter.FormatSectionForExport("A분반"));
        Assert.NotEqual("A분반분반", FormattedTimetableExporter.FormatSectionForExport("A분반"));
    }

    [Fact]
    public void ExcelExport_EmptySection_RemainsEmpty()
    {
        Assert.Equal(string.Empty, FormattedTimetableExporter.FormatSectionForExport(null));
        Assert.Equal(string.Empty, FormattedTimetableExporter.FormatSectionForExport(""));
        Assert.Equal(string.Empty, FormattedTimetableExporter.FormatSectionForExport(" "));
    }

    [Fact]
    public void ExcelExport_VisibleSectionUsesExportSectionSuffix()
    {
        var text = ExportAndReadVisibleText(
            new List<TimetableAssignmentRow> { new("C-A", 0, 1, "R1") },
            new List<Course>
            {
                new() { Id = "C-A", Name = "웹서버프로그래밍", Grade = 3, Section = 1, ProfessorId = "P1" },
                new() { Id = "C-B", Name = "웹서버프로그래밍", Grade = 3, Section = 2 },
            },
            professors: new List<Professor> { new() { Id = "P1", Name = "김교수" } },
            rooms: new List<Room> { new() { Id = "R1", Name = "A관 302호" } });

        Assert.Contains("A분반", text);
        Assert.Contains("웹서버프로그래밍\nA분반 · 김교수\nA관 302호", text);
        Assert.DoesNotContain("\nA\n", text);
    }

    [Fact]
    public void ExcelExport_CoteachProfessor_DoesNotDuplicatePrimaryProfessor()
    {
        var text = ExportAndReadVisibleText(
            new List<TimetableAssignmentRow> { new("C-A", 0, 1, "R1") },
            new List<Course>
            {
                new()
                {
                    Id = "C-A",
                    Name = "운영체제",
                    Grade = 3,
                    Section = 1,
                    ProfessorId = "P1",
                    CoteachProfs = new List<string> { "P1", "P2" },
                },
                new() { Id = "C-B", Name = "운영체제", Grade = 3, Section = 2 },
            },
            professors: new List<Professor>
            {
                new() { Id = "P1", Name = "김교수" },
                new() { Id = "P2", Name = "이교수" },
            },
            rooms: new List<Room> { new() { Id = "R1", Name = "A관 302호" } });

        Assert.Contains("A분반 · 김교수, 이교수", text);
        Assert.DoesNotContain("김교수, 김교수", text);
    }

    [Fact]
    public void ExcelExport_HeaderColors_AreLowSaturation()
    {
        ExportAndInspectWorksheet(
            new List<TimetableAssignmentRow> { new("C1", 0, 1, "R1") },
            new List<Course> { new() { Id = "C1", Name = "자료구조", Grade = 2, Section = 1 } },
            ws =>
            {
                Assert.Equal("#6F879F", ColorHex(ws.Cell(1, 1).Style.Fill.BackgroundColor));
                Assert.Equal("#E6EEF6", ColorHex(ws.Cell(4, 2).Style.Fill.BackgroundColor));
                Assert.Equal("#F8FAFC", ColorHex(ws.Cell(5, 2).Style.Fill.BackgroundColor));
            });
    }

    [Fact]
    public void ExcelExport_DayGroupBorders_AreVisible()
    {
        ExportAndInspectWorksheet(
            new List<TimetableAssignmentRow>
            {
                new("MON", 0, 1, "R1"),
                new("TUE", 1, 1, "R1"),
            },
            new List<Course>
            {
                new() { Id = "MON", Name = "월요일수업", Grade = 1, Section = 1 },
                new() { Id = "TUE", Name = "화요일수업", Grade = 1, Section = 1 },
            },
            ws =>
            {
                var mondayBoundary = ws.Cell(6, 2).Style.Border;
                var tuesdayInnerGrid = ws.Cell(6, 3).Style.Border;

                Assert.Equal(XLBorderStyleValues.Medium, mondayBoundary.RightBorder);
                Assert.Equal("#8FA3B8", ColorHex(mondayBoundary.RightBorderColor));
                Assert.Equal(XLBorderStyleValues.Thin, tuesdayInnerGrid.LeftBorder);
                Assert.NotEqual(ColorHex(tuesdayInnerGrid.LeftBorderColor), ColorHex(mondayBoundary.RightBorderColor));
            });
    }

    private static string ExportAndReadVisibleText(
        IReadOnlyList<TimetableAssignmentRow> rows,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor>? professors = null,
        IReadOnlyList<Room>? rooms = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"export_display_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export(
                "테스트",
                rows,
                courses,
                professors ?? new List<Professor>(),
                path,
                rooms);

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("시간표");
            return string.Join("\n", ws.CellsUsed().Select(c => c.GetString()));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void ExportAndInspectWorksheet(
        IReadOnlyList<TimetableAssignmentRow> rows,
        IReadOnlyList<Course> courses,
        Action<IXLWorksheet> inspect)
    {
        var path = Path.Combine(Path.GetTempPath(), $"export_style_{Guid.NewGuid():N}.xlsx");
        try
        {
            FormattedTimetableExporter.Export(
                "테스트",
                rows,
                courses,
                new List<Professor>(),
                path,
                new List<Room> { new() { Id = "R1", Name = "강의실 1" } });

            using var wb = new XLWorkbook(path);
            inspect(wb.Worksheet("시간표"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string ColorHex(XLColor color) =>
        $"#{color.Color.ToArgb() & 0x00FFFFFF:X6}";
}
