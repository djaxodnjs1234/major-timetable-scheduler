using ClosedXML.Excel;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Data;

public static class FormattedTimetableExporter
{
    private static readonly string[] DayNames = { "월", "화", "수", "목", "금" };
    private static readonly int[] ValidPeriods = { 1, 2, 3, 4, 6, 7, 8, 9 };

    private static readonly XLColor[] GradeFills =
    {
        XLColor.NoColor,
        XLColor.FromHtml("#FFFFCC"), // 1학년: light yellow
        XLColor.FromHtml("#D6E3BC"), // 2학년: light green
        XLColor.FromHtml("#DBE5F1"), // 3학년: light blue
        XLColor.FromHtml("#F2DBDB"), // 4학년: light pink
    };

    private static readonly XLColor HeaderBg = XLColor.FromHtml("#4F81BD");
    private static readonly XLColor LunchBg  = XLColor.FromHtml("#F2F2F2");

    // period → Excel row (period 1 = row 5 ... period 9 = row 13)
    private static int PeriodRow(int p) => p + 4;

    public static void Export(
        string timetableName,
        IEnumerable<TimetableAssignmentRow> assignments,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        string path)
    {
        var aList  = assignments.ToList();
        var cMap   = courses.ToDictionary(c => c.Id);
        var pMap   = professors.ToDictionary(p => p.Id);

        // --- 1. per-day layout calculation ---
        var dayLayouts = BuildDayLayouts(aList, cMap);

        // --- 2. column offsets ---
        // col 1 = A (period label), then day columns, then last col (period label)
        int[] dayStart = new int[5];
        int col = 2;
        for (int d = 0; d < 5; d++)
        {
            dayStart[d] = col;
            col += dayLayouts[d].SubColCount;
        }
        int lastLabelCol = col;
        int totalCols    = lastLabelCol;

        // --- 3. build workbook ---
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("시간표");

        SetColumnWidths(ws, dayStart, dayLayouts, lastLabelCol);
        SetRowHeights(ws);

        // title
        WriteTitleRow(ws, timetableName, totalCols);

        // date
        ws.Cell(2, lastLabelCol).Value    = DateTime.Today.ToString("yyyy-MM-dd");
        ws.Cell(2, lastLabelCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(2, 1, 2, lastLabelCol).Merge();

        // day headers (row 4)
        ApplyOuterBorder(ws.Range(4, 1, 13, totalCols));
        for (int d = 0; d < 5; d++)
        {
            int sc = dayStart[d];
            int ec = sc + dayLayouts[d].SubColCount - 1;
            var hdr = ws.Range(4, sc, 4, ec).Merge();
            hdr.Value = DayNames[d];
            hdr.Style.Font.Bold = true;
            hdr.Style.Font.FontSize = 12;
            hdr.Style.Font.FontColor = XLColor.White;
            hdr.Style.Fill.BackgroundColor = HeaderBg;
            hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            hdr.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            ApplyThinBorder(hdr);
        }

        // period label column (A) and last column
        WritePeriodLabels(ws, lastLabelCol);

        // lunch row (period 5 = row 9)
        var lunchRange = ws.Range(9, 1, 9, totalCols).Merge();
        lunchRange.Value = "점 심 시 간";
        lunchRange.Style.Font.Bold = true;
        lunchRange.Style.Font.FontSize = 11;
        lunchRange.Style.Fill.BackgroundColor = LunchBg;
        lunchRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        lunchRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        ApplyThinBorder(lunchRange);

        // empty course cells (placeholder borders)
        for (int d = 0; d < 5; d++)
        {
            for (int sub = 0; sub < dayLayouts[d].SubColCount; sub++)
            {
                int c2 = dayStart[d] + sub;
                foreach (int p in ValidPeriods)
                {
                    var cell = ws.Cell(PeriodRow(p), c2);
                    ApplyThinBorder(cell.AsRange());
                }
            }
        }

        // course cells
        WriteCourses(ws, aList, cMap, pMap, courses, dayStart, dayLayouts);

        // 비고 row
        WriteRemarks(ws, aList, cMap, dayStart, dayLayouts, totalCols);

        // legend
        WriteLegend(ws);

        ws.SheetView.Freeze(4, 1); // freeze row 4 and col A

        wb.SaveAs(path);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static DayLayout[] BuildDayLayouts(
        List<TimetableAssignmentRow> aList,
        Dictionary<string, Course> cMap)
    {
        var layouts = new DayLayout[5];
        for (int d = 0; d < 5; d++)
        {
            var dayA = aList.Where(a => a.Day == d).ToList();

            // unique course blocks: (courseId, startPeriod, endPeriod)
            var blocks = dayA
                .GroupBy(a => a.CourseId)
                .Select(g => new CourseBlock(
                    g.Key,
                    g.Min(a => a.Period),
                    g.Max(a => a.Period),
                    g.First().RoomId))
                .OrderBy(b => GradeOf(b.CourseId, cMap))
                .ThenBy(b => b.StartPeriod)
                .ToList();

            // greedy interval coloring → assign sub-column per course
            var endPeriods      = new List<int>();
            var courseSubColMap = new Dictionary<string, int>();

            foreach (var block in blocks)
            {
                int sub = endPeriods.FindIndex(end => end < block.StartPeriod);
                if (sub < 0) { sub = endPeriods.Count; endPeriods.Add(0); }
                courseSubColMap[block.CourseId] = sub;
                endPeriods[sub] = block.EndPeriod;
            }

            layouts[d] = new DayLayout(
                Math.Max(1, endPeriods.Count),
                blocks,
                courseSubColMap);
        }
        return layouts;
    }

    private static void WriteCourses(
        IXLWorksheet ws,
        List<TimetableAssignmentRow> aList,
        Dictionary<string, Course> cMap,
        Dictionary<string, Professor> pMap,
        IReadOnlyList<Course> allCourses,
        int[] dayStart,
        DayLayout[] layouts)
    {
        // pre-compute which course names have multiple sections
        var multiSectionNames = allCourses
            .GroupBy(c => (c.Name, c.Grade))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(c => c.Id))
            .ToHashSet();

        for (int d = 0; d < 5; d++)
        {
            foreach (var block in layouts[d].CourseBlocks)
            {
                if (!cMap.TryGetValue(block.CourseId, out var course)) continue;

                int sub      = layouts[d].CourseSubCol[block.CourseId];
                int xlCol    = dayStart[d] + sub;
                int startRow = PeriodRow(block.StartPeriod);
                int endRow   = PeriodRow(block.EndPeriod);

                IXLRange cellRange = startRow == endRow
                    ? ws.Cell(startRow, xlCol).AsRange()
                    : ws.Range(startRow, xlCol, endRow, xlCol).Merge();

                var fill = course.Grade is >= 1 and <= 4
                    ? GradeFills[course.Grade]
                    : XLColor.White;

                string section = multiSectionNames.Contains(course.Id)
                    ? SectionLabel(course.Section)
                    : "";

                var profName = pMap.TryGetValue(course.ProfessorId ?? "", out var prof)
                    ? prof.Name
                    : course.ProfessorId ?? "";

                cellRange.Value = BuildCellText(
                    course.Name, section, profName, course.Grade, block.RoomId);

                cellRange.Style.Fill.BackgroundColor    = fill;
                cellRange.Style.Alignment.WrapText      = true;
                cellRange.Style.Alignment.Horizontal    = XLAlignmentHorizontalValues.Center;
                cellRange.Style.Alignment.Vertical      = XLAlignmentVerticalValues.Center;
                cellRange.Style.Font.FontSize           = 9;
                ApplyThinBorder(cellRange);
            }
        }
    }

    private static void WriteRemarks(
        IXLWorksheet ws,
        List<TimetableAssignmentRow> aList,
        Dictionary<string, Course> cMap,
        int[] dayStart,
        DayLayout[] layouts,
        int totalCols)
    {
        // Row 14: 비고
        var remarksCol = ws.Cell(14, 1);
        remarksCol.Value = "비고";
        remarksCol.Style.Font.Bold = true;
        remarksCol.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        remarksCol.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;

        // collect special notes (fixed courses, graduate courses, etc.)
        var notes = new List<string>();
        foreach (var a in aList)
        {
            if (!cMap.TryGetValue(a.CourseId, out var c)) continue;
            if (c.IsFixed && !string.IsNullOrEmpty(c.Name))
                notes.Add($"{c.Name} ({DayNames[a.Day]}{a.Period}교시, {a.RoomId})");
        }

        var notesRange = ws.Range(14, 2, 14, totalCols).Merge();
        if (notes.Count > 0)
            notesRange.Value = string.Join("  |  ", notes.Distinct());
        notesRange.Style.Alignment.WrapText   = true;
        notesRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        notesRange.Style.Font.FontSize        = 9;
        ApplyThinBorder(ws.Range(14, 1, 14, totalCols));
    }

    private static void WriteLegend(IXLWorksheet ws)
    {
        // Row 16: 범례 header
        ws.Cell(16, 1).Value = "범례";
        ws.Cell(16, 1).Style.Font.Bold = true;

        string[] gradeLabels = { "1학년", "2학년", "3학년", "4학년" };
        for (int i = 0; i < 4; i++)
        {
            int row = 17 + i;
            var swatch = ws.Range(row, 2, row, 3).Merge();
            swatch.Style.Fill.BackgroundColor = GradeFills[i + 1];
            ApplyThinBorder(swatch);

            var label = ws.Cell(row, 4);
            label.Value = gradeLabels[i];
            label.Style.Font.Bold = true;
        }
    }

    private static void WriteTitleRow(IXLWorksheet ws, string title, int totalCols)
    {
        var titleRange = ws.Range(1, 1, 1, totalCols).Merge();
        titleRange.Value = title;
        titleRange.Style.Font.Bold      = true;
        titleRange.Style.Font.FontSize  = 16;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        titleRange.Style.Fill.BackgroundColor = HeaderBg;
        titleRange.Style.Font.FontColor       = XLColor.White;
    }

    private static void WritePeriodLabels(IXLWorksheet ws, int lastLabelCol)
    {
        int[] periods = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        foreach (int p in periods)
        {
            int r = PeriodRow(p);
            string label = p == 5 ? "점심" : p.ToString();

            // left column A
            var left = ws.Cell(r, 1);
            left.Value = label;
            left.Style.Font.Bold = true;
            left.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            left.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            ApplyThinBorder(left.AsRange());

            // right column (last)
            var right = ws.Cell(r, lastLabelCol);
            right.Value = label;
            right.Style.Font.Bold = true;
            right.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            right.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            ApplyThinBorder(right.AsRange());
        }
    }

    private static void SetColumnWidths(
        IXLWorksheet ws,
        int[] dayStart,
        DayLayout[] layouts,
        int lastLabelCol)
    {
        ws.Column(1).Width         = 4;   // period label
        ws.Column(lastLabelCol).Width = 4;
        for (int d = 0; d < 5; d++)
        {
            for (int sub = 0; sub < layouts[d].SubColCount; sub++)
                ws.Column(dayStart[d] + sub).Width = 14;
        }
    }

    private static void SetRowHeights(IXLWorksheet ws)
    {
        ws.Row(1).Height  = 30;   // title
        ws.Row(2).Height  = 14;   // date
        ws.Row(3).Height  = 6;    // spacer
        ws.Row(4).Height  = 22;   // day headers
        ws.Row(9).Height  = 22;   // lunch
        ws.Row(14).Height = 40;   // 비고
        foreach (int p in new[] { 1, 2, 3, 4, 6, 7, 8, 9 })
            ws.Row(PeriodRow(p)).Height = 72;
    }

    private static string BuildCellText(
        string name, string section, string profName, int grade, string roomId)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(name);
        sb.Append('\n');
        sb.Append(section);  // empty string when no section
        sb.Append('\n');
        sb.Append(profName);
        sb.Append('\n');
        sb.Append(grade > 0 ? $"{grade}학년" : "");
        sb.Append('\n');
        sb.Append(roomId);
        return sb.ToString();
    }

    private static string SectionLabel(int section) => section switch
    {
        1 => "A",
        2 => "B",
        3 => "C",
        4 => "D",
        _ => section.ToString(),
    };

    private static int GradeOf(string courseId, Dictionary<string, Course> cMap) =>
        cMap.TryGetValue(courseId, out var c) ? c.Grade : 0;

    private static void ApplyThinBorder(IXLRange range)
    {
        range.Style.Border.OutsideBorder     = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder      = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = XLColor.Black;
        range.Style.Border.InsideBorderColor  = XLColor.Black;
    }

    private static void ApplyOuterBorder(IXLRange range)
    {
        range.Style.Border.OutsideBorder      = XLBorderStyleValues.Medium;
        range.Style.Border.OutsideBorderColor = XLColor.Black;
    }

    // ── inner types ──────────────────────────────────────────────────────────

    private sealed record CourseBlock(
        string CourseId,
        int StartPeriod,
        int EndPeriod,
        string RoomId);

    private sealed record DayLayout(
        int SubColCount,
        List<CourseBlock> CourseBlocks,
        Dictionary<string, int> CourseSubCol);
}
