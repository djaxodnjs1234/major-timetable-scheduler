using ClosedXML.Excel;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Data;

public static class FormattedTimetableExporter
{
    private static readonly string[] DayNames = { "월", "화", "수", "목", "금" };
    private static readonly IReadOnlyList<int> ValidPeriods = SchedulePeriods.Instructional;

    private static readonly XLColor[] GradeFills =
    {
        XLColor.NoColor,
        XLColor.FromHtml("#FFF8D8"), // 1학년: soft yellow
        XLColor.FromHtml("#EAF5DC"), // 2학년: soft green
        XLColor.FromHtml("#E6F1FA"), // 3학년: soft blue
        XLColor.FromHtml("#FBE7EA"), // 4학년: soft pink
        XLColor.FromHtml("#EDE7F6"), // 대학원: soft lavender
    };

    private static readonly XLColor TitleBg      = XLColor.FromHtml("#6F879F");
    private static readonly XLColor DayHeaderBg  = XLColor.FromHtml("#E6EEF6");
    private static readonly XLColor GradeHeaderBg = XLColor.FromHtml("#F8FAFC");
    private static readonly XLColor LunchBg      = XLColor.FromHtml("#F7F9FC");
    private static readonly XLColor TextDark     = XLColor.FromHtml("#1F2937");
    private static readonly XLColor TextMuted    = XLColor.FromHtml("#4B5563");
    private static readonly XLColor GridLine     = XLColor.FromHtml("#E5EAF0");
    private static readonly XLColor CardLine     = XLColor.FromHtml("#AAB7C6");
    private static readonly XLColor OuterLine    = XLColor.FromHtml("#94A3B8");
    private static readonly XLColor DayGroupLine = XLColor.FromHtml("#8FA3B8");
    private const int MaxSheetNameLength = 31;
    private const string DataSheetName = "데이터";
    private const string UnknownCourseName = "알 수 없는 수업";
    private const string UnknownProfessorName = "알 수 없는 교수";
    private const string UnknownRoomName = "알 수 없는 강의실";
    private static readonly char[] InvalidSheetNameChars = { '\\', '/', '?', '*', '[', ']', ':' };

    // Row 4 = day headers, row 5 = grade sub-headers, period 1 starts at row 6
    private static int PeriodRow(int p) => p + 5;
    private static int LastPeriodRow => PeriodRow(SchedulePeriods.LastPeriod);
    private static int RemarksRow => LastPeriodRow + 1;
    private static int LegendRow => RemarksRow + 2;

    public static void Export(
        string timetableName,
        IEnumerable<TimetableAssignmentRow> assignments,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        string path,
        IReadOnlyList<Room>? rooms = null,
        bool expandAllGrades = false)
    {
        var aList = assignments.ToList();
        var cMap = BuildCourseMap(aList, courses);

        using var wb = new XLWorkbook();
        var sheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DataSheetName };

        AddTimetableSheet(
            wb,
            sheetNames,
            "통합 시간표",
            timetableName,
            aList,
            courses,
            professors,
            rooms,
            expandAllGrades,
            splitByGrade: true);

        foreach (var grade in AcademicLevels.AllGrades)
        {
            var gradeRows = aList
                .Where(a => cMap.TryGetValue(a.CourseId, out var course) && course.Grade == grade)
                .ToList();
            if (gradeRows.Count == 0) continue;
            var gradeName = AcademicLevels.DisplayName(grade);

            AddTimetableSheet(
                wb,
                sheetNames,
                $"학년별_{gradeName}",
                $"{timetableName} - {gradeName}",
                gradeRows,
                courses,
                professors,
                rooms,
                expandAllGrades: false,
                splitByGrade: true);
        }

        foreach (var professor in professors)
        {
            var professorRows = aList
                .Where(a => cMap.TryGetValue(a.CourseId, out var course) && IsCourseTaughtBy(course, professor.Id))
                .ToList();
            if (professorRows.Count == 0) continue;

            var professorName = string.IsNullOrWhiteSpace(professor.Name)
                ? UnknownProfessorName
                : professor.Name.Trim();
            AddTimetableSheet(
                wb,
                sheetNames,
                $"교수별_{professorName}",
                $"{timetableName} - {professorName}",
                professorRows,
                courses,
                professors,
                rooms,
                expandAllGrades: false,
                splitByGrade: false);
        }

        foreach (var room in rooms ?? Array.Empty<Room>())
        {
            var roomRows = aList
                .Where(a => string.Equals(NormalizeId(a.RoomId), NormalizeId(room.Id), StringComparison.Ordinal))
                .ToList();
            if (roomRows.Count == 0) continue;

            var roomName = string.IsNullOrWhiteSpace(room.Name) ? UnknownRoomName : room.Name.Trim();
            AddTimetableSheet(
                wb,
                sheetNames,
                $"강의실별_{roomName}",
                $"{timetableName} - {roomName}",
                roomRows,
                courses,
                professors,
                rooms,
                expandAllGrades: false,
                splitByGrade: false);
        }

        WriteDataSheet(wb, aList);

        wb.SaveAs(path);
    }

    private static void WriteDataSheet(XLWorkbook wb, List<TimetableAssignmentRow> aList)
    {
        var dataWs = wb.AddWorksheet(DataSheetName);
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
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void AddTimetableSheet(
        XLWorkbook wb,
        HashSet<string> sheetNames,
        string requestedSheetName,
        string timetableName,
        List<TimetableAssignmentRow> assignments,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room>? rooms,
        bool expandAllGrades,
        bool splitByGrade)
    {
        var ws = wb.AddWorksheet(MakeUniqueSheetName(RemoveExamplePrefix(requestedSheetName), sheetNames));
        WriteTimetableSheet(ws, RemoveExamplePrefix(timetableName), assignments, courses, professors, rooms, expandAllGrades, splitByGrade);
    }

    private static void WriteTimetableSheet(
        IXLWorksheet ws,
        string timetableName,
        List<TimetableAssignmentRow> assignments,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room>? rooms,
        bool expandAllGrades,
        bool splitByGrade)
    {
        var cMap  = BuildCourseMap(assignments, courses);
        var pMap  = professors.ToDictionary(p => p.Id);
        var roomNameMap = BuildRoomNameMap(rooms);

        var dayLayouts = BuildDayLayouts(assignments, cMap, roomNameMap, expandAllGrades, splitByGrade);

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

        SetColumnWidths(ws, dayStart, dayLayouts, lastLabelCol);
        SetRowHeights(ws);

        WriteTitleRow(ws, timetableName, totalCols);

        ws.Cell(2, lastLabelCol).Value = DateTime.Today.ToString("yyyy-MM-dd");
        ws.Cell(2, lastLabelCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(2, 1, 2, lastLabelCol).Merge();

        // outer border: day headers (row 4), grade row (row 5), and all period rows
        ApplyOuterBorder(ws.Range(4, 1, LastPeriodRow, totalCols));

        // day headers (row 4)
        for (int d = 0; d < 5; d++)
        {
            int sc = dayStart[d];
            int ec = sc + dayLayouts[d].SubColCount - 1;
            var hdr = ws.Range(4, sc, 4, ec).Merge();
            hdr.Value = DayNames[d];
            hdr.Style.Font.Bold = true;
            hdr.Style.Font.FontSize = 12;
            hdr.Style.Font.FontColor = TextDark;
            hdr.Style.Fill.BackgroundColor = DayHeaderBg;
            hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            hdr.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            ApplyHeaderBorder(hdr);
        }

        // grade sub-headers (row 5)
        WriteGradeHeaders(ws, dayStart, dayLayouts, lastLabelCol);

        // period label columns
        WritePeriodLabels(ws, lastLabelCol);

        // lunch row
        var lunchRow = PeriodRow(SchedulePeriods.LunchPeriod);
        var lunchRange = ws.Range(lunchRow, 1, lunchRow, totalCols).Merge();
        lunchRange.Value = "점 심 시 간";
        lunchRange.Style.Font.Bold = true;
        lunchRange.Style.Font.FontSize = 11;
        lunchRange.Style.Font.FontColor = TextMuted;
        lunchRange.Style.Fill.BackgroundColor = LunchBg;
        lunchRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        lunchRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        ApplyHeaderBorder(lunchRange);

        // empty cell borders
        for (int d = 0; d < 5; d++)
            for (int sub = 0; sub < dayLayouts[d].SubColCount; sub++)
            {
                int c2 = dayStart[d] + sub;
                foreach (int p in ValidPeriods)
                    ApplyGridBorder(ws.Cell(PeriodRow(p), c2).AsRange());
            }

        WriteCourses(ws, assignments, cMap, pMap, courses, dayStart, dayLayouts);
        WriteRemarks(ws, assignments, cMap, roomNameMap, dayStart, dayLayouts, totalCols);
        ApplyDayGroupBorders(ws, dayStart, dayLayouts);
        WriteLegend(ws);

        ws.SheetView.Freeze(5, 1);
    }

    private static Dictionary<string, Course> BuildCourseMap(
        List<TimetableAssignmentRow> assignments,
        IReadOnlyList<Course> courses)
    {
        var map = courses
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var assignment in assignments)
        {
            var courseId = (assignment.CourseId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(courseId) || map.ContainsKey(courseId))
                continue;

            map[courseId] = new Course
            {
                Id = courseId,
                Name = UnknownCourseName,
                Grade = 1,
                Section = 0,
            };
        }

        return map;
    }

    private static DayLayout[] BuildDayLayouts(
        List<TimetableAssignmentRow> aList,
        Dictionary<string, Course> cMap,
        IReadOnlyDictionary<string, string> roomNameMap,
        bool expandAllGrades,
        bool splitByGrade)
    {
        var layouts = new DayLayout[5];
        for (int d = 0; d < 5; d++)
        {
            var dayA = aList.Where(a => a.Day == d).ToList();
            var gradeLayouts = new List<GradeLayout>();
            var courseSubColGlobal = new Dictionary<string, int>();
            int globalOffset = 0;

            if (!splitByGrade)
            {
                var blocks = BuildCourseBlocks(dayA, cMap, roomNameMap)
                    .OrderBy(b => b.StartPeriod)
                    .ThenBy(b => cMap.TryGetValue(b.CourseId, out var course) ? course.Grade : int.MaxValue)
                    .ThenBy(b => cMap.TryGetValue(b.CourseId, out var course) ? course.Section : int.MaxValue)
                    .ThenBy(b => b.CourseId, StringComparer.Ordinal)
                    .ToList();

                var endPeriods = new List<int>();
                foreach (var block in blocks)
                {
                    int sub = endPeriods.FindIndex(end => end < block.StartPeriod);
                    if (sub < 0) { sub = endPeriods.Count; endPeriods.Add(0); }
                    courseSubColGlobal[block.CourseId] = sub;
                    endPeriods[sub] = block.EndPeriod;
                }

                gradeLayouts.Add(new GradeLayout(0, 0, Math.Max(1, endPeriods.Count), blocks));
                layouts[d] = new DayLayout(Math.Max(1, endPeriods.Count), gradeLayouts, courseSubColGlobal);
                continue;
            }

            foreach (int grade in AcademicLevels.AllGrades)
            {
                var gradeBlocks = BuildCourseBlocks(dayA
                    .Where(a => cMap.TryGetValue(a.CourseId, out var c) && c.Grade == grade)
                    .ToList(), cMap, roomNameMap)
                    .OrderBy(b => b.StartPeriod)
                    .ToList();

                if (gradeBlocks.Count == 0)
                {
                    if (!expandAllGrades) continue;

                    gradeLayouts.Add(new GradeLayout(grade, globalOffset, 1, gradeBlocks));
                    globalOffset += 1;
                    continue;
                }

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

    private static IEnumerable<CourseBlock> BuildCourseBlocks(
        IEnumerable<TimetableAssignmentRow> assignments,
        Dictionary<string, Course> cMap,
        IReadOnlyDictionary<string, string> roomNameMap) =>
        assignments
            .Where(a => cMap.ContainsKey(a.CourseId))
            .GroupBy(a => a.CourseId)
            .Select(g => new CourseBlock(
                g.Key,
                g.Min(a => a.Period),
                g.Max(a => a.Period),
                string.Join(" / ", g
                    .Select(a => FormatRoomForExport(a.RoomId, roomNameMap))
                    .Where(room => !string.IsNullOrWhiteSpace(room))
                    .Distinct(StringComparer.Ordinal))));

    private static void WriteGradeHeaders(
        IXLWorksheet ws,
        int[] dayStart,
        DayLayout[] layouts,
        int lastLabelCol)
    {
        // period label column in row 5
        var left = ws.Cell(5, 1).AsRange();
        left.Style.Fill.BackgroundColor = GradeHeaderBg;
        ApplyHeaderBorder(left);

        var right = ws.Cell(5, lastLabelCol).AsRange();
        right.Style.Fill.BackgroundColor = GradeHeaderBg;
        ApplyHeaderBorder(right);

        for (int d = 0; d < 5; d++)
        {
            var layout = layouts[d];
            if (layout.GradeLayouts.Count == 0)
            {
                var blank = ws.Cell(5, dayStart[d]).AsRange();
                blank.Style.Fill.BackgroundColor = GradeHeaderBg;
                ApplyHeaderBorder(blank);
                continue;
            }

            foreach (var gl in layout.GradeLayouts)
            {
                int sc = dayStart[d] + gl.SubColStart;
                int ec = sc + gl.SubColCount - 1;
                var gradeRange = gl.SubColCount > 1
                    ? ws.Range(5, sc, 5, ec).Merge()
                    : ws.Cell(5, sc).AsRange();
                gradeRange.Value = gl.Grade > 0 ? AcademicLevels.DisplayName(gl.Grade) : "";
                gradeRange.Style.Font.Bold = true;
                gradeRange.Style.Font.FontSize = 9;
                gradeRange.Style.Font.FontColor = TextMuted;
                gradeRange.Style.Fill.BackgroundColor = GradeHeaderBg;
                gradeRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                gradeRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                ApplyHeaderBorder(gradeRange);
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

                var fill = course.Grade is >= 1 and <= AcademicLevels.GraduateGrade
                    ? GradeFills[course.Grade]
                    : XLColor.White;

                string section = multiSectionNames.Contains(course.Id)
                    ? FormatSectionForExport(SectionLabel(course.Section))
                    : "";

                var profStr = FormatProfessorsForExport(course, pMap);

                cellRange.Value = BuildCellText(course.Name, section, profStr, block.Rooms);

                cellRange.Style.Fill.BackgroundColor = fill;
                cellRange.Style.Alignment.WrapText   = true;
                cellRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cellRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                cellRange.Style.Font.FontSize        = 9;
                cellRange.Style.Font.FontColor       = TextDark;
                ApplyCourseBorder(cellRange);
            }
        }
    }
    private static void WriteRemarks(
        IXLWorksheet ws,
        List<TimetableAssignmentRow> aList,
        Dictionary<string, Course> cMap,
        IReadOnlyDictionary<string, string> roomNameMap,
        int[] dayStart,
        DayLayout[] layouts,
        int totalCols)
    {
        var remarksCol = ws.Cell(RemarksRow, 1);
        remarksCol.Value = "비고";
        remarksCol.Style.Font.Bold = true;
        remarksCol.Style.Font.FontColor = TextMuted;
        remarksCol.Style.Fill.BackgroundColor = GradeHeaderBg;
        remarksCol.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        remarksCol.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;

        var notes = new List<string>();
        foreach (var a in aList)
        {
            if (!cMap.TryGetValue(a.CourseId, out var c)) continue;
            if (c.IsFixed && !string.IsNullOrEmpty(c.Name))
                notes.Add($"{c.Name} ({DayNames[a.Day]}{a.Period}교시, {FormatRoomForExport(a.RoomId, roomNameMap)})");
        }
        var notesRange = ws.Range(RemarksRow, 2, RemarksRow, totalCols).Merge();
        if (notes.Count > 0)
            notesRange.Value = string.Join("\n", notes.Distinct().Select(note => $"- {note}"));
        notesRange.Style.Alignment.WrapText = true;
        notesRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        notesRange.Style.Font.FontSize      = 9;
        notesRange.Style.Font.FontColor = TextMuted;
        ApplyHeaderBorder(ws.Range(RemarksRow, 1, RemarksRow, totalCols));
    }

    private static void WriteLegend(IXLWorksheet ws)
    {
        int row = LegendRow;
        ws.Cell(row, 1).Value = "범례";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontColor = TextMuted;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var grades = AcademicLevels.AllGrades;
        for (int i = 0; i < grades.Count; i++)
        {
            int swatchCol = 2 + (i * 2);
            var swatch = ws.Cell(row, swatchCol).AsRange();
            swatch.Style.Fill.BackgroundColor = GradeFills[grades[i]];
            ApplyCourseBorder(swatch);

            var label = ws.Cell(row, swatchCol + 1);
            label.Value = AcademicLevels.DisplayName(grades[i]);
            label.Style.Font.FontSize = 9;
            label.Style.Font.FontColor = TextMuted;
            label.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }
        }

    private static void WriteTitleRow(IXLWorksheet ws, string title, int totalCols)
    {
        var titleRange = ws.Range(1, 1, 1, totalCols).Merge();
        titleRange.Value = RemoveExamplePrefix(title);
        titleRange.Style.Font.Bold      = true;
        titleRange.Style.Font.FontSize  = 16;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        titleRange.Style.Fill.BackgroundColor = TitleBg;
        titleRange.Style.Font.FontColor       = XLColor.White;
    }

    private static void WritePeriodLabels(IXLWorksheet ws, int lastLabelCol)
    {
        foreach (int p in SchedulePeriods.All)
        {
            int r = PeriodRow(p);
            string label = FormatPeriodLabel(p);

            var left = ws.Cell(r, 1);
            left.Value = label;
            left.Style.Font.Bold = true;
            left.Style.Font.FontColor = TextMuted;
            left.Style.Fill.BackgroundColor = GradeHeaderBg;
            left.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            left.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            left.Style.Alignment.WrapText   = true;
            ApplyHeaderBorder(left.AsRange());

            var right = ws.Cell(r, lastLabelCol);
            right.Value = label;
            right.Style.Font.Bold = true;
            right.Style.Font.FontColor = TextMuted;
            right.Style.Fill.BackgroundColor = GradeHeaderBg;
            right.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            right.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            right.Style.Alignment.WrapText   = true;
            ApplyHeaderBorder(right.AsRange());
        }
    }

    private static string FormatPeriodLabel(int period) => period == 5
        ? "점심\n13:00~14:00"
        : $"{period}교시\n{SchedulePeriods.TimeRange(period)}";

    private static void SetColumnWidths(
        IXLWorksheet ws,
        int[] dayStart,
        DayLayout[] layouts,
        int lastLabelCol)
    {
        ws.Column(1).Width            = 12;
        ws.Column(lastLabelCol).Width = 12;
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
        ws.Row(PeriodRow(SchedulePeriods.LunchPeriod)).Height = 22;
        ws.Row(RemarksRow).Height = 54;
        ws.Row(LegendRow).Height = 18;
        foreach (int p in ValidPeriods)
            ws.Row(PeriodRow(p)).Height = 72;
    }
    private static string BuildCellText(string name, string section, string profStr, string rooms)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(name);

        var middleParts = new[] { section, profStr }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var middle = string.Join(" · ", middleParts);
        if (!string.IsNullOrWhiteSpace(middle)) { sb.Append('\n'); sb.Append(middle); }
        if (!string.IsNullOrWhiteSpace(rooms)) { sb.Append('\n'); sb.Append(rooms); }

        return sb.ToString();
    }

    private static string FormatProfessorsForExport(Course course, IReadOnlyDictionary<string, Professor> professors)
    {
        var labels = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenLabels = new HashSet<string>(StringComparer.Ordinal);

        Add(course.ProfessorId);
        foreach (var professorId in course.CoteachProfs)
            Add(professorId);

        return string.Join(", ", labels);

        void Add(string? professorId)
        {
            var id = (professorId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id) || !seenIds.Add(id))
                return;

            var label = professors.TryGetValue(id, out var professor) && !string.IsNullOrWhiteSpace(professor.Name)
                ? professor.Name.Trim()
                : UnknownProfessorName;
            if (!seenLabels.Add(label))
                return;

            labels.Add(label);
        }
    }

    public static string FormatSectionForExport(string? section)
    {
        var value = (section ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.TrimEnd('.').Trim();

        if (value.EndsWith("분반", StringComparison.Ordinal))
            return value;

        return $"{value}분반";
    }

    private static Dictionary<string, string> BuildRoomNameMap(IReadOnlyList<Room>? rooms)
    {
        if (rooms == null || rooms.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        return rooms
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => r.Id.Trim(), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var room = g.First();
                    return string.IsNullOrWhiteSpace(room.Name) ? UnknownRoomName : room.Name.Trim();
                },
                StringComparer.Ordinal);
    }

    private static string FormatRoomForExport(string? roomId, IReadOnlyDictionary<string, string> roomNameMap)
    {
        var id = (roomId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;

        return roomNameMap.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : UnknownRoomName;
    }

    private static string MakeUniqueSheetName(string requestedName, HashSet<string> usedNames)
    {
        var baseName = SanitizeSheetName(RemoveExamplePrefix(requestedName));
        var name = TruncateSheetName(baseName);
        var suffix = 2;
        while (usedNames.Contains(name))
        {
            var marker = $"({suffix++})";
            name = $"{TruncateSheetName(baseName, MaxSheetNameLength - marker.Length)}{marker}";
        }

        usedNames.Add(name);
        return name;
    }

    private static string SanitizeSheetName(string value)
    {
        var sanitized = value.Trim();
        foreach (var ch in InvalidSheetNameChars)
            sanitized = sanitized.Replace(ch, '_');

        sanitized = sanitized.Trim('\'').Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Sheet" : sanitized;
    }

    private static string TruncateSheetName(string value, int maxLength = MaxSheetNameLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string RemoveExamplePrefix(string value) =>
        value.Replace("예시-", "", StringComparison.Ordinal);

    private static bool IsCourseTaughtBy(Course course, string professorId) =>
        string.Equals(course.ProfessorId, professorId, StringComparison.Ordinal)
        || course.CoteachProfs.Contains(professorId);

    private static string NormalizeId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? "" : id.Trim();

    private static string SectionLabel(int section) => section switch
    {
        1 => "A",
        2 => "B",
        3 => "C",
        4 => "D",
        _ => section.ToString(),
    };

    private static void ApplyDayGroupBorders(
        IXLWorksheet ws,
        int[] dayStart,
        DayLayout[] layouts)
    {
        for (int d = 0; d < DayNames.Length - 1; d++)
        {
            int endColumn = dayStart[d] + layouts[d].SubColCount - 1;
            var boundary = ws.Range(4, endColumn, 15, endColumn);
            boundary.Style.Border.RightBorder = XLBorderStyleValues.Medium;
            boundary.Style.Border.RightBorderColor = DayGroupLine;
        }
    }

    private static void ApplyGridBorder(IXLRange range)
    {
        range.Style.Border.OutsideBorder      = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder       = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = GridLine;
        range.Style.Border.InsideBorderColor  = GridLine;
    }

    private static void ApplyHeaderBorder(IXLRange range)
    {
        range.Style.Border.OutsideBorder      = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder       = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = OuterLine;
        range.Style.Border.InsideBorderColor  = GridLine;
    }

    private static void ApplyCourseBorder(IXLRange range)
    {
        range.Style.Border.OutsideBorder      = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder       = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = CardLine;
        range.Style.Border.InsideBorderColor  = CardLine;
    }

    private static void ApplyOuterBorder(IXLRange range)
    {
        range.Style.Border.OutsideBorder      = XLBorderStyleValues.Medium;
        range.Style.Border.OutsideBorderColor = OuterLine;
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
