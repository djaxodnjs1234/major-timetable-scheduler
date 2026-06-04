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
    private static readonly Dictionary<string, int> DayMap = new()
    {
        ["월"] = 0, ["화"] = 1, ["수"] = 2, ["목"] = 3, ["금"] = 4,
    };

    private static readonly Regex DayPeriodRegex = new(@"^([월화수목금])(\d+)$", RegexOptions.Compiled);
    private static readonly Regex SplitRegex = new(@"[,\s]+", RegexOptions.Compiled);

    public static XlsxLoadResult Load(string path)
    {
        using var wb = new XLWorkbook(path);
        var sheet = wb.Worksheet(1);

        var profNames = new HashSet<string>();
        var roomIds = new HashSet<string>();
        // (baseId, representative course) — first row per base wins
        var seen = new Dictionary<string, Course>();
        var counts = new Dictionary<string, int>();

        foreach (var row in sheet.RowsUsed())
        {
            string Cell(string col) => row.Cell(col).GetString().Trim();

            var gradeRaw = Cell("E");
            var name = Cell("G");
            if (string.IsNullOrEmpty(gradeRaw) || string.IsNullOrEmpty(name)) continue;
            if (!int.TryParse(gradeRaw, out var grade))
            {
                if (!double.TryParse(gradeRaw, out var gradeD)) continue;
                grade = (int)gradeD;
            }

            var typeRaw = Cell("F");
            var courseType = typeRaw == "필수" ? "전필" : "전선";

            var creditsRaw = Cell("H");
            if (!int.TryParse(creditsRaw, out var hours))
            {
                if (!double.TryParse(creditsRaw, out var hoursD)) continue;
                hours = (int)hoursD;
            }

            var code = Cell("I");
            var prof = Cell("Q");
            var dept = Cell("R");
            var schedStr = Cell("S");

            var sched = ParseSchedule(schedStr);
            var fixedRoom = sched.Count > 0 ? sched[0].Room : null;
            var xlsxBlocks = sched.Select(s => s.Periods.Count).ToList();

            List<int> blockStructure;
            if (hours == 3) blockStructure = new List<int> { 2, 1 };
            else if (hours == 4) blockStructure = new List<int> { 2, 2 };
            else if (xlsxBlocks.Count > 0 && xlsxBlocks.Sum() == hours) blockStructure = xlsxBlocks;
            else blockStructure = new List<int> { hours };

            var sourceBaseId = DomainHelpers.BaseId(code);
            counts[sourceBaseId] = counts.GetValueOrDefault(sourceBaseId, 0) + 1;

            if (!seen.ContainsKey(sourceBaseId))
            {
                seen[sourceBaseId] = new Course
                {
                    Id = (seen.Count + 1).ToString(),
                    Name = name,
                    Grade = grade,
                    HoursPerWeek = hours,
                    CourseType = courseType,
                    ProfessorId = prof,
                    Section = 1,   // updated below
                    Department = dept,
                    FixedRooms = fixedRoom != null ? new List<string> { fixedRoom } : new List<string>(),
                    UnavailableRooms = new List<string>(),
                    BlockStructure = blockStructure,
                };
            }

            if (!string.IsNullOrEmpty(prof)) profNames.Add(prof);
            if (fixedRoom != null) roomIds.Add(fixedRoom);
        }

        // Set Section = number of sections found per base
        var courses = seen.Values.ToList();
        foreach (var (sourceBaseId, course) in seen)
            course.Section = counts[sourceBaseId];

        var professors = profNames.OrderBy(n => n)
            .Select(n => new Professor { Id = n, Name = n }).ToList();
        var rooms = roomIds.OrderBy(r => r)
            .Select(r => new Room { Id = r, Name = r }).ToList();

        return new XlsxLoadResult(courses, professors, rooms);
    }

    private record ScheduleEntry(int Day, List<int> Periods, string Room);

    private static List<ScheduleEntry> ParseSchedule(string s)
    {
        var result = new List<ScheduleEntry>();
        if (string.IsNullOrEmpty(s)) return result;

        foreach (var part in SplitRegex.Split(s.Trim()))
        {
            if (string.IsNullOrEmpty(part) || !part.Contains('/')) continue;
            var slash = part.IndexOf('/');
            var timePart = part[..slash];
            var room = part[(slash + 1)..].Trim();

            var m = DayPeriodRegex.Match(timePart);
            if (!m.Success) continue;
            var day = DayMap[m.Groups[1].Value];
            var digits = m.Groups[2].Value;
            var periods = digits.Select(ch => ch - '0').ToList();
            result.Add(new ScheduleEntry(day, periods, room));
        }
        return result;
    }
}
