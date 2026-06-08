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

        foreach (var row in sheet.RowsUsed())
        {
            string Cell(string col) => row.Cell(col).GetString().Trim();

            var gradeRaw = Cell("E");
            var name = Cell("G");
            if (string.IsNullOrEmpty(gradeRaw) || string.IsNullOrEmpty(name)) continue;

            if (!int.TryParse(gradeRaw, out var grade))
            {
                if (!double.TryParse(gradeRaw, out var gradeDouble)) continue;
                grade = (int)gradeDouble;
            }

            var typeRaw = Cell("F");
            var courseType = typeRaw == RequiredType ? MajorRequiredType : MajorElectiveType;

            var creditsRaw = Cell("H");
            if (!int.TryParse(creditsRaw, out var hours))
            {
                if (!double.TryParse(creditsRaw, out var hoursDouble)) continue;
                hours = (int)hoursDouble;
            }

            var code = Cell("I");
            var professorName = Cell("Q");
            var department = Cell("R");
            var scheduleText = Cell("S");

            var schedule = ParseSchedule(scheduleText);
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
            .Select(pair => new Room { Id = pair.Value, Name = pair.Key })
            .ToList();

        return new XlsxLoadResult(courses, professors, rooms);
    }

    private record ScheduleEntry(int Day, List<int> Periods, string Room);

    private static List<ScheduleEntry> ParseSchedule(string text)
    {
        var result = new List<ScheduleEntry>();
        if (string.IsNullOrEmpty(text)) return result;

        foreach (var part in SplitRegex.Split(text.Trim()))
        {
            if (string.IsNullOrEmpty(part) || !part.Contains('/')) continue;

            var slash = part.IndexOf('/');
            var timePart = part[..slash];
            var room = part[(slash + 1)..].Trim();

            var match = DayPeriodRegex.Match(timePart);
            if (!match.Success) continue;

            var day = DayMap[match.Groups[1].Value];
            var periods = match.Groups[2].Value.Select(ch => ch - '0').ToList();
            result.Add(new ScheduleEntry(day, periods, room));
        }

        return result;
    }
}
