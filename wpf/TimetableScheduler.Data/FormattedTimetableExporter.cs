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

    private static readonly XLColor HeaderBg  = XLColor.FromHtml("#4F81BD");
    private static readonly XLColor LunchBg   = XLColor.FromHtml("#F2F2F2");
    private static readonly XLColor SubHdrBg  = XLColor.FromHtml("#D9D9D9");

    // Row 4 = day headers, row 5 = grade sub-headers, period 1 starts at row 6
    private static int PeriodRow(int p) => p + 5;

    public static void Export(
        string timetableName,
        IEnumerable<TimetableAssignmentRow> assignments,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        string path)
    {
        var aList = assignments.ToList();
        var cMap  = courses.ToDictionary(c => c.Id);
        var pMap  = professors.ToDictionary(p => p.Id);

        var dayLayouts = BuildDayLayouts(aList, cMap);

        // col 1 = A (period label), day columns, last col (period label)
        int[] dayStart = new int[5];
        int col = 2;
        for (int d = 0; d < 5; d++)
        {
            dayStart[d] = col;
            col += dayLayouts[d].SubColCount;
        }
        int lastLabelCol = col;
        int totalCols    = lastLabelCol;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("시간표");

        SetColumnWidths(ws, dayStart, dayLayouts, lastLabelCol);
        SetRowHeights(ws);

        WriteTitleRow(ws, timetableName, totalCols);

        ws.Cell(2, lastLabelCol).Value = DateTime.Today.ToString("yyyy-MM-dd");
        ws.Cell(2, lastLabelCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(2, 1, 2, lastLabelCol).Merge();

        // outer border: day headers (row 4) + grade row (row 5) + all period rows (rows 6-14)
        ApplyOuterBorder(ws.Range(4, 1, 14, totalCols));

        // day headers (row 4)
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

        // grade sub-headers (row 5)
        WriteGradeHeaders(ws, dayStart, dayLayouts, lastLabelCol);

        // period label columns
        WritePeriodLabels(ws, lastLabelCol);

        // lunch row (period 5 = row 10)
        var lunchRange = ws.Range(10, 1, 10, totalCols).Merge();
        lunchRange.Value = "점 심 시 간";
        lunchRange.Style.Font.Bold = true;
        lunchRange.Style.Font.FontSize = 11;
        lunchRange.Style.Fill.BackgroundColor = LunchBg;
        lunchRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        lunchRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        ApplyThinBorder(lunchRange);

        // empty cell borders
        for (int d = 0; d < 5; d++)
            for (int sub = 0; sub < dayLayouts[d].SubColCount; sub++)
            {
                int c2 = dayStart[d] + sub;
                foreach (int p in ValidPeriods)
                    ApplyThinBorder(ws.Cell(PeriodRow(p), c2).AsRange());
            }

        WriteCourses(ws, aList, cMap, pMap, courses, dayStart, dayLayouts);
        WriteRemarks(ws, aList, cMap, dayStart, dayLayouts, totalCols);
        WriteLegend(ws);

        ws.SheetView.Freeze(5, 1);

        // Hidden data sheet for round-trip import
        var dataWs = wb.AddWorksheet("데이터");
        dataWs.Cell(1, 1).Value = "과목ID";
        dataWs.Cell(1, 2).Value = "요일번호";
        dataWs.Cell(1, 3).Value = "교시";
        dataWs.Cell(1, 4).Value = "강의실ID";
        int dr = 2;
        foreach (var row in aList)
        {
            dataWs.Cell(dr, 1).Value = row.CourseId;
            dataWs.Cell(dr, 2).Value = row.Day;
            dataWs.Cell(dr, 3).Value = row.Period;
            dataWs.Cell(dr, 4).Value = row.RoomId;
            dr++;
        }
        dataWs.Visibility = XLWorksheetVisibility.Hidden;

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
            var gradeLayouts = new List<GradeLayout>();
            var courseSubColGlobal = new Dictionary<string, int>();
            int globalOffset = 0;

            foreach (int grade in new[] { 1, 2, 3, 4 })
            {
                var gradeBlocks = dayA
                    .Where(a => cMap.TryGetValue(a.CourseId, out var c) && c.Grade == grade)
                    .GroupBy(a => a.CourseId)
                    .Select(g => new CourseBlock(
                        g.Key,
                        g.Min(a => a.Period),
                        g.Max(a => a.Period),
                        string.Join(", ", g.Select(a => a.RoomId).Distinct())))
                    .OrderBy(b => b.StartPeriod)
                    .ToList();

                if (gradeBlocks.Count == 0) continue;

                // greedy interval coloring within this grade only
                var endPeriods = new List<int>();
                foreach (var block in gradeBlocks)
                {
                    int sub = endPeriods.FindIndex(end => end < block.StartPeriod);
                    if (sub < 0) { sub = endPeriods.Count; endPeriods.Add(0); }
                    courseSubColGlobal[block.CourseId] = globalOffset + sub;
                    endPeriods[sub] = block.EndPeriod;
                }

                int gradeWidth = endPeriods.Count;
                gradeLayouts.Add(new GradeLayout(grade, globalOffset, gradeWidth, gradeBlocks));
                globalOffset += gradeWidth;
            }

            layouts[d] = new DayLayout(
                Math.Max(1, globalOffset),
                gradeLayouts,
                courseSubColGlobal);
        }
        return layouts;
    }

    private static void WriteGradeHeaders(
        IXLWorksheet ws,
        int[] dayStart,
        DayLayout[] layouts,
        int lastLabelCol)
    {
        // period label column in row 5
        var left = ws.Cell(5, 1).AsRange();
        left.Style.Fill.BackgroundColor = SubHdrBg;
        ApplyThinBorder(left);

        var right = ws.Cell(5, lastLabelCol).AsRange();
        right.Style.Fill.BackgroundColor = SubHdrBg;
        ApplyThinBorder(right);

        for (int d = 0; d < 5; d++)
        {
            var layout = layouts[d];
            if (layout.GradeLayouts.Count == 0)
            {
                var blank = ws.Cell(5, dayStart[d]).AsRange();
                blank.Style.Fill.BackgroundColor = SubHdrBg;
                ApplyThinBorder(blank);
                continue;
            }

            foreach (var gl in layout.GradeLayouts)
            {
                int sc = dayStart[d] + gl.SubColStart;
                int ec = sc + gl.SubColCount - 1;
                var gradeRange = gl.SubColCount > 1
                    ? ws.Range(5, sc, 5, ec).Merge()
                    : ws.Cell(5, sc).AsRange();
                gradeRange.Value = $"{gl.Grade}학년";
                gradeRange.Style.Font.Bold = true;
                gradeRange.Style.Font.FontSize = 9;
                gradeRange.Style.Fill.BackgroundColor = GradeFills[gl.Grade];
                gradeRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                gradeRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                ApplyThinBorder(gradeRange);
            }
        }
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
        var multiSectionNames = allCourses
            .GroupBy(c => (c.Name, c.Grade))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(c => c.Id))
            .ToHashSet();

        for (int d = 0; d < 5; d++)
        {
            foreach (var block in layouts[d].AllBlocks)
            {
                if (!cMap.TryGetValue(block.CourseId, out var course)) continue;
                if (!layouts[d].CourseSubCol.TryGetValue(block.CourseId, out int sub)) continue;

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

                var profParts = new List<string>();
                if (!string.IsNullOrEmpty(course.ProfessorId))
                    profParts.Add(pMap.TryGetValue(course.ProfessorId, out var mp) ? mp.Name : course.ProfessorId);
                foreach (var pid in course.CoteachProfs)
                    if (!string.IsNullOrEmpty(pid))
                        profParts.Add(pMap.TryGetValue(pid, out var cp) ? cp.Name : pid);
                var profStr = string.Join(", ", profParts);

                cellRange.Value = BuildCellText(course.Name, section, profStr, block.Rooms);

                cellRange.Style.Fill.BackgroundColor = fill;
                cellRange.Style.Alignment.WrapText   = true;
                cellRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cellRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                cellRange.Style.Font.FontSize        = 9;
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
        // Row 15: 비고
        var remarksCol = ws.Cell(15, 1);
        remarksCol.Value = "비고";
        remarksCol.Style.Font.Bold = true;
        remarksCol.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        remarksCol.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;

        var notes = new List<string>();
        foreach (var a in aList)
        {
            if (!cMap.TryGetValue(a.CourseId, out var c)) continue;
            if (c.IsFixed && !string.IsNullOrEmpty(c.Name))
                notes.Add($"{c.Name} ({DayNames[a.Day]}{a.Period}교시, {a.RoomId})");
        }

        var notesRange = ws.Range(15, 2, 15, totalCols).Merge();
        if (notes.Count > 0)
            notesRange.Value = string.Join("  |  ", notes.Distinct());
        notesRange.Style.Alignment.WrapText = true;
        notesRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        notesRange.Style.Font.FontSize      = 9;
        ApplyThinBorder(ws.Range(15, 1, 15, totalCols));
    }

    private static void WriteLegend(IXLWorksheet ws)
    {
        ws.Cell(17, 1).Value = "범례";
        ws.Cell(17, 1).Style.Font.Bold = true;

        string[] gradeLabels = { "1학년", "2학년", "3학년", "4학년" };
        for (int i = 0; i < 4; i++)
        {
            int row = 18 + i;
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

            var left = ws.Cell(r, 1);
            left.Value = label;
            left.Style.Font.Bold = true;
            left.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            left.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            ApplyThinBorder(left.AsRange());

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
        ws.Column(1).Width            = 4;
        ws.Column(lastLabelCol).Width = 4;
        for (int d = 0; d < 5; d++)
            for (int sub = 0; sub < layouts[d].SubColCount; sub++)
                ws.Column(dayStart[d] + sub).Width = 14;
    }

    private static void SetRowHeights(IXLWorksheet ws)
    {
        ws.Row(1).Height  = 30;  // title
        ws.Row(2).Height  = 14;  // date
        ws.Row(3).Height  = 6;   // spacer
        ws.Row(4).Height  = 22;  // day headers
        ws.Row(5).Height  = 14;  // grade sub-headers
        ws.Row(10).Height = 22;  // lunch (period 5)
        ws.Row(15).Height = 40;  // 비고
        foreach (int p in new[] { 1, 2, 3, 4, 6, 7, 8, 9 })
            ws.Row(PeriodRow(p)).Height = 72;
    }

    private static string BuildCellText(string name, string section, string profStr, string rooms)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(name);
        if (!string.IsNullOrEmpty(section)) { sb.Append('\n'); sb.Append(section); }
        if (!string.IsNullOrEmpty(profStr)) { sb.Append('\n'); sb.Append(profStr); }
        if (!string.IsNullOrEmpty(rooms))   { sb.Append('\n'); sb.Append(rooms); }
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

    private static void ApplyThinBorder(IXLRange range)
    {
        range.Style.Border.OutsideBorder      = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder       = XLBorderStyleValues.Thin;
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
        string Rooms);

    private sealed record GradeLayout(
        int Grade,
        int SubColStart,
        int SubColCount,
        List<CourseBlock> Blocks);

    private sealed record DayLayout(
        int SubColCount,
        List<GradeLayout> GradeLayouts,
        Dictionary<string, int> CourseSubCol)
    {
        public IEnumerable<CourseBlock> AllBlocks => GradeLayouts.SelectMany(g => g.Blocks);
    }
}
