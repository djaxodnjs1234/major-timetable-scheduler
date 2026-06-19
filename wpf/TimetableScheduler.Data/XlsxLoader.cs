using System.Diagnostics;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Data;

public sealed record XlsxLoadResult(
    List<Course> Courses,
    List<Professor> Professors,
    List<Room> Rooms);

public static class XlsxLoader
{
    private const string KoreanMonday = "\uC6D4";
    private const string KoreanTuesday = "\uD654";
    private const string KoreanWednesday = "\uC218";
    private const string KoreanThursday = "\uBAA9";
    private const string KoreanFriday = "\uAE08";
    private const string RequiredType = "\uD544\uC218";
    private const string MajorRequiredType = "\uC804\uD544";
    private const string MajorElectiveType = "\uC804\uC120";
    private const string CapstoneKeyword = "\uCEA1\uC2A4\uD1A4\uB514\uC790\uC778";
    private const string CapstoneLeftKeyword = "\uCEA1\uC2A4\uD1A4";
    private const string CapstoneRightKeyword = "\uB514\uC790\uC778";

    private static readonly Regex ExplicitSectionRegex = new(@"^(?<base>.+)-(?<section>\d{2})$", RegexOptions.Compiled);
    private static readonly Dictionary<string, int> DayMap = new()
    {
        [KoreanMonday] = 0,
        [KoreanTuesday] = 1,
        [KoreanWednesday] = 2,
        [KoreanThursday] = 3,
        [KoreanFriday] = 4,
    };

    private static readonly Regex DayPeriodRegex =
        new($"^([{KoreanMonday}{KoreanTuesday}{KoreanWednesday}{KoreanThursday}{KoreanFriday}])(\\d+)$", RegexOptions.Compiled);
    private static readonly Regex SplitRegex = new(@"[,\s]+", RegexOptions.Compiled);
    private static readonly IReadOnlyList<string> RequiredHeaders = new[]
    {
        "이수 대상 학년",
        "이수 구분",
        "교과목명",
        "학점",
        "교과목코드",
        "담당교수",
        "수강학과",
        "강의시간(강의실)",
    };

    private static List<int> DefaultBlockStructureForHours(int hours) => hours switch
    {
        3 => new List<int> { 1, 2 },
        4 => new List<int> { 2, 2 },
        _ => new List<int> { hours },
    };

    public static XlsxLoadResult Load(string path)
    {
        using var wb = new XLWorkbook(path);
        var sheet = wb.Worksheet(1);
        var headerMap = BuildHeaderMap(sheet);

        var profNames = new HashSet<string>();
        var roomIds = new HashSet<string>();
        var seen = new Dictionary<string, Course>();
        var explicitCourses = new List<Course>();
        var counts = new Dictionary<string, int>();
        var importedBaseIds = new Dictionary<string, string>();

        string GetOrCreateImportedBaseId(string sourceBaseId)
        {
            if (importedBaseIds.TryGetValue(sourceBaseId, out var importedBaseId))
                return importedBaseId;

            importedBaseId = (importedBaseIds.Count + 1).ToString();
            importedBaseIds[sourceBaseId] = importedBaseId;
            return importedBaseId;
        }

        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > headerMap.RowNumber))
        {
            string Cell(string header) => row.Cell(headerMap.Columns[NormalizeHeader(header)]).GetString().Trim();

            if (IsIgnorableRow(row, headerMap))
                continue;

            var gradeRaw = RequiredCell(row, headerMap, "이수 대상 학년");
            var typeRaw = RequiredCell(row, headerMap, "이수 구분");
            var name = RequiredCell(row, headerMap, "교과목명");
            var creditsRaw = RequiredCell(row, headerMap, "학점");
            var code = RequiredCell(row, headerMap, "교과목코드");
            var professorName = RequiredCell(row, headerMap, "담당교수");
            var department = RequiredCell(row, headerMap, "수강학과");
            var scheduleText = RequiredCell(row, headerMap, "강의시간(강의실)");

            var grade = ParseRequiredInt(gradeRaw, "이수 대상 학년", row.RowNumber());
            if (grade is < 1 or > 4)
                Fail($"Invalid grade at row {row.RowNumber()}: {gradeRaw}");
            var courseType = typeRaw == RequiredType ? MajorRequiredType : MajorElectiveType;

            var hours = ParseRequiredInt(creditsRaw, "학점", row.RowNumber());
            if (hours <= 0)
                Fail($"Invalid credits at row {row.RowNumber()}: {creditsRaw}");
            if (string.IsNullOrWhiteSpace(code))
                Fail($"Missing course code at row {row.RowNumber()}");
            if (string.IsNullOrWhiteSpace(professorName))
                Fail($"Missing professor at row {row.RowNumber()}");

            var schedule = ParseSchedule(scheduleText, row.RowNumber());
            if (schedule.Count == 0)
                Fail($"Missing schedule entries at row {row.RowNumber()}");
            var fixedRoom = schedule.Count > 0 ? schedule[0].Room : null;
            var xlsxBlocks = schedule.Select(entry => entry.Periods.Count).ToList();

            List<int> blockStructure;
            if (hours is 3 or 4) blockStructure = DefaultBlockStructureForHours(hours);
            else if (xlsxBlocks.Count > 0 && xlsxBlocks.Sum() == hours) blockStructure = xlsxBlocks;
            else blockStructure = DefaultBlockStructureForHours(hours);

            var normalizedName = name.Replace(" ", "", StringComparison.Ordinal);
            var isCapstoneDesign =
                normalizedName.Contains(CapstoneKeyword, StringComparison.Ordinal) ||
                (name.Contains(CapstoneLeftKeyword, StringComparison.Ordinal) &&
                 name.Contains(CapstoneRightKeyword, StringComparison.Ordinal));

            var explicitSectionMatch = ExplicitSectionRegex.Match(code);
            var hasExplicitSectionId = explicitSectionMatch.Success;
            var sourceBaseId = hasExplicitSectionId
                ? explicitSectionMatch.Groups["base"].Value
                : DomainHelpers.BaseId(code);
            var isCapstoneCode = sourceBaseId.StartsWith("CP", StringComparison.OrdinalIgnoreCase);
            var shouldCollapseCapstone = hasExplicitSectionId && (isCapstoneDesign || isCapstoneCode);

            var importedBaseId = GetOrCreateImportedBaseId(sourceBaseId);

            if (hasExplicitSectionId && !isCapstoneDesign && !isCapstoneCode)
            {
                var explicitSection = int.Parse(explicitSectionMatch.Groups["section"].Value);
                explicitCourses.Add(new Course
                {
                    Id = $"{importedBaseId}-{explicitSection:D2}",
                    Name = name,
                    Grade = grade,
                    HoursPerWeek = hours,
                    CourseType = courseType,
                    ProfessorId = professorName,
                    Section = explicitSection,
                    Department = department,
                    FixedRooms = fixedRoom != null ? new List<string> { fixedRoom } : new List<string>(),
                    UnavailableRooms = new List<string>(),
                    BlockStructure = blockStructure,
                });
            }
            else
            {
                counts[sourceBaseId] = shouldCollapseCapstone
                    ? 1
                    : counts.GetValueOrDefault(sourceBaseId, 0) + 1;

                if (!seen.ContainsKey(sourceBaseId))
                {
                    seen[sourceBaseId] = new Course
                    {
                        Id = importedBaseId,
                        Name = name,
                        Grade = grade,
                        HoursPerWeek = hours,
                        CourseType = courseType,
                        ProfessorId = professorName,
                        Section = 1,
                        Department = department,
                        FixedRooms = fixedRoom != null ? new List<string> { fixedRoom } : new List<string>(),
                        UnavailableRooms = new List<string>(),
                        BlockStructure = blockStructure,
                    };
                }
            }

            if (!string.IsNullOrEmpty(professorName)) profNames.Add(professorName);
            if (fixedRoom != null) roomIds.Add(fixedRoom);
        }

        var courses = seen.Values.Concat(explicitCourses).ToList();
        var professorIds = profNames.OrderBy(name => name)
            .Select((name, index) => (name, id: (index + 1).ToString()))
            .ToDictionary(pair => pair.name, pair => pair.id);
        var roomIdsByName = roomIds.OrderBy(name => name)
            .Select((name, index) => (name, id: (index + 1).ToString()))
            .ToDictionary(pair => pair.name, pair => pair.id);

        foreach (var (sourceBaseId, course) in seen)
        {
            course.Section = counts[sourceBaseId];
            if (professorIds.TryGetValue(course.ProfessorId, out var professorId))
                course.ProfessorId = professorId;
            course.FixedRooms = course.FixedRooms
                .Select(room => roomIdsByName.TryGetValue(room, out var roomId) ? roomId : room)
                .ToList();
        }

        foreach (var course in explicitCourses)
        {
            if (professorIds.TryGetValue(course.ProfessorId, out var professorId))
                course.ProfessorId = professorId;
            course.FixedRooms = course.FixedRooms
                .Select(room => roomIdsByName.TryGetValue(room, out var roomId) ? roomId : room)
                .ToList();
        }

        var professors = professorIds
            .OrderBy(pair => int.Parse(pair.Value))
            .Select(pair => new Professor { Id = pair.Value, Name = pair.Key })
            .ToList();
        var rooms = roomIdsByName
            .OrderBy(pair => int.Parse(pair.Value))
            .Select(pair => new Room { Id = pair.Value, Name = pair.Key, IsImportedFromExcel = true })
            .ToList();

        return new XlsxLoadResult(courses, professors, rooms);
    }

    private sealed record HeaderMap(int RowNumber, IReadOnlyDictionary<string, int> Columns);

    private static HeaderMap BuildHeaderMap(IXLWorksheet sheet)
    {
        var required = RequiredHeaders.Select(NormalizeHeader).ToHashSet(StringComparer.Ordinal);
        HeaderMap? best = null;
        var bestCount = -1;

        foreach (var row in sheet.RowsUsed())
        {
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in row.CellsUsed())
            {
                var key = NormalizeHeader(cell.GetString());
                if (string.IsNullOrWhiteSpace(key)) continue;
                columns.TryAdd(key, cell.Address.ColumnNumber);
            }

            var count = required.Count(columns.ContainsKey);
            if (count > bestCount)
            {
                best = new HeaderMap(row.RowNumber(), columns);
                bestCount = count;
            }
            if (required.All(columns.ContainsKey))
                return new HeaderMap(row.RowNumber(), columns);
        }

        var missing = RequiredHeaders
            .Where(header => best == null || !best.Columns.ContainsKey(NormalizeHeader(header)))
            .ToList();
        Fail($"Missing required Excel headers: {string.Join(", ", missing)}");
        throw new UnreachableException();
    }

    private static string NormalizeHeader(string? value) =>
        Regex.Replace((value ?? "").Trim(), @"[\s\t\r\n]+", "");

    private static bool IsIgnorableRow(IXLRow row, HeaderMap headerMap)
    {
        var values = RequiredHeaders
            .Select(header => row.Cell(headerMap.Columns[NormalizeHeader(header)]).GetString().Trim())
            .ToList();
        if (values.All(string.IsNullOrWhiteSpace))
            return true;

        var grade = values[0];
        var name = values[2];
        var code = values[4];
        return string.IsNullOrWhiteSpace(grade)
            && string.IsNullOrWhiteSpace(name)
            && string.IsNullOrWhiteSpace(code);
    }

    private static string RequiredCell(IXLRow row, HeaderMap headerMap, string header)
    {
        var value = row.Cell(headerMap.Columns[NormalizeHeader(header)]).GetString().Trim();
        if (string.IsNullOrWhiteSpace(value))
            Fail($"Missing required cell '{header}' at row {row.RowNumber()}");
        return value;
    }

    private static int ParseRequiredInt(string raw, string header, int rowNumber)
    {
        if (int.TryParse(raw, out var value))
            return value;
        if (double.TryParse(raw, out var doubleValue) && Math.Abs(doubleValue - Math.Truncate(doubleValue)) < 0.000001)
            return (int)doubleValue;
        Fail($"Invalid numeric value for '{header}' at row {rowNumber}: {raw}");
        throw new UnreachableException();
    }

    private record ScheduleEntry(int Day, List<int> Periods, string Room);

    private static List<ScheduleEntry> ParseSchedule(string text, int rowNumber)
    {
        var result = new List<ScheduleEntry>();
        if (string.IsNullOrWhiteSpace(text))
            Fail($"Missing schedule at row {rowNumber}");

        foreach (var part in SplitRegex.Split(text.Trim()))
        {
            if (string.IsNullOrEmpty(part)) continue;
            if (!part.Contains('/'))
                Fail($"Invalid schedule token at row {rowNumber}: {part}");

            var slash = part.IndexOf('/');
            var timePart = part[..slash];
            var room = part[(slash + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(room))
                Fail($"Missing room in schedule at row {rowNumber}: {part}");

            var match = DayPeriodRegex.Match(timePart);
            if (!match.Success)
                Fail($"Invalid schedule time at row {rowNumber}: {part}");

            var day = DayMap[match.Groups[1].Value];
            var periods = match.Groups[2].Value.Select(ch => ch - '0').ToList();
            if (periods.Count == 0 || periods.Any(p => p <= 0 || p > 9))
                Fail($"Invalid period in schedule at row {rowNumber}: {part}");
            result.Add(new ScheduleEntry(day, periods, room));
        }

        return result;
    }

    private static void Fail(string message)
    {
        Debug.WriteLine($"[XLSX_IMPORT] {message}");
        throw new InvalidDataException(message);
    }
}
