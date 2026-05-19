using ClosedXML.Excel;

namespace TimetableScheduler.Data;

public sealed record TimetableAssignmentRow(string CourseId, int Day, int Period, string RoomId);

public static class TimetableXlsxService
{
    public static void Export(IEnumerable<TimetableAssignmentRow> rows, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("시간표");
        ws.Cell(1, 1).Value = "과목ID";
        ws.Cell(1, 2).Value = "요일번호";
        ws.Cell(1, 3).Value = "교시";
        ws.Cell(1, 4).Value = "강의실ID";
        int r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.CourseId;
            ws.Cell(r, 2).Value = row.Day;
            ws.Cell(r, 3).Value = row.Period;
            ws.Cell(r, 4).Value = row.RoomId;
            r++;
        }
        wb.SaveAs(path);
    }

    public static List<TimetableAssignmentRow> Import(string path)
    {
        using var wb = new XLWorkbook(path);
        // Prefer the hidden "데이터" sheet (round-trip from FormattedTimetableExporter)
        var ws = wb.Worksheets.TryGetWorksheet("데이터", out var dataWs)
            ? dataWs
            : wb.Worksheet(1);
        var result = new List<TimetableAssignmentRow>();
        bool first = true;
        foreach (var row in ws.RowsUsed())
        {
            if (first) { first = false; continue; }
            var courseId = row.Cell(1).GetString();
            if (string.IsNullOrWhiteSpace(courseId)) continue;
            if (!int.TryParse(row.Cell(2).GetString(), out int day)) continue;
            if (!int.TryParse(row.Cell(3).GetString(), out int period)) continue;
            var roomId = row.Cell(4).GetString();
            result.Add(new TimetableAssignmentRow(courseId, day, period, roomId));
        }
        return result;
    }
}
