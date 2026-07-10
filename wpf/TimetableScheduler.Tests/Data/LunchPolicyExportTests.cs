using ClosedXML.Excel;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Tests.Data;

public class LunchPolicyExportTests
{
    [Fact]
    public void FlexibleLunch_ExportsOrdinaryUnmergedCellsWithEqualRowHeights()
    {
        var policy = new SchedulePolicy
        {
            LunchMode = LunchPolicyMode.BanOneOfPeriods4And5,
        };
        var lunches = new Dictionary<int, int>
        {
            [0] = 4,
            [1] = 5,
            [2] = 4,
            [3] = 5,
            [4] = 4,
        };
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "Period 5 class", Grade = 1, HoursPerWeek = 1 },
            new() { Id = "B-01", Name = "Period 4 class", Grade = 2, HoursPerWeek = 1 },
        };
        var rows = new List<TimetableAssignmentRow>
        {
            new("A-01", 0, 5, "R1"),
            new("B-01", 1, 4, "R2"),
        };
        var path = Path.Combine(Path.GetTempPath(), $"lunch_export_{Guid.NewGuid():N}.xlsx");

        try
        {
            FormattedTimetableExporter.Export(
                "점심 정책 테스트",
                rows,
                courses,
                Array.Empty<Professor>(),
                path,
                new[]
                {
                    new Room { Id = "R1", Name = "R1" },
                    new Room { Id = "R2", Name = "R2" },
                },
                schedulePolicy: policy,
                lunchPeriodsByDay: lunches);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("통합 시간표");
            const int period4Row = 9;
            const int period5Row = 10;
            const int mondayColumn = 2;
            const int tuesdayColumn = 3;

            Assert.Equal("점심", sheet.Cell(period4Row, mondayColumn).GetString());
            Assert.Contains("Period 5 class", sheet.Cell(period5Row, mondayColumn).GetString());
            Assert.Contains("Period 4 class", sheet.Cell(period4Row, tuesdayColumn).GetString());
            Assert.Equal("점심", sheet.Cell(period5Row, tuesdayColumn).GetString());
            Assert.Equal(72, sheet.Row(period4Row).Height);
            Assert.Equal(72, sheet.Row(period5Row).Height);
            Assert.False(IsMerged(sheet, period4Row, mondayColumn));
            Assert.False(IsMerged(sheet, period5Row, tuesdayColumn));
            Assert.Contains("4교시", sheet.Cell(period4Row, 1).GetString());
            Assert.Contains("5교시", sheet.Cell(period5Row, 1).GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static bool IsMerged(IXLWorksheet worksheet, int row, int column) =>
        worksheet.MergedRanges.Any(range =>
            range.RangeAddress.FirstAddress.RowNumber <= row
            && range.RangeAddress.LastAddress.RowNumber >= row
            && range.RangeAddress.FirstAddress.ColumnNumber <= column
            && range.RangeAddress.LastAddress.ColumnNumber >= column);
}
