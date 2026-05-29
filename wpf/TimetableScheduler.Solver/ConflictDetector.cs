using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

public enum ConflictSeverity { Warning, Error }
public enum ConflictType
{
    RoomConflict,
    ProfessorConflict,
    LunchConflict,
    SectionConflict,
    GradeConflict,
    ProfUnavailable,
    FixedTimeViolation,
    FixedRoomViolation,
    BlockStartViolation,
    ProfAllowedRoomViolation,
    ProfRoomInconsistent,
    SameCourseSameDayConflict,
}

public sealed record ConflictItem(
    ConflictType Type,
    ConflictSeverity Severity,
    string Description,
    int Day,
    int Period);

public static class ConflictDetector
{
    public static IReadOnlyList<ConflictItem> Detect(
        IReadOnlyList<SolutionAssignment> assignment,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor>? professors = null,
        IReadOnlyList<CrossGroup>? crosses = null,
        Func<string, string, int, int, bool>? isManualGradeOverlapAllowed = null)
    {
        var list = new List<ConflictItem>();
        var courseMap = courses.ToDictionary(c => c.Id);
        var profMap = professors?.ToDictionary(p => p.Id) ?? new Dictionary<string, Professor>();

        // HC-12: lunch period (5) — never allowed
        foreach (var a in assignment.Where(a => a.Period == Constants.LunchPeriod))
        {
            var name = courseMap.TryGetValue(a.CourseId, out var c) ? c.Name : a.CourseId;
            list.Add(new ConflictItem(
                ConflictType.LunchConflict, ConflictSeverity.Error,
                $"{name}({a.CourseId})가 점심 시간({DayName(a.Day)} {a.Period}교시)에 배치됨",
                a.Day, a.Period));
        }

        // HC-01: room double-booked at same (day, period, room)
        foreach (var g in assignment.GroupBy(a => (a.Day, a.Period, a.RoomId)))
        {
            // multi-room same course is OK: distinct course ids only
            var distinctCids = g.Select(a => a.CourseId).Distinct().ToList();
            if (distinctCids.Count <= 1) continue;
            var labels = distinctCids.Select(cid =>
                courseMap.TryGetValue(cid, out var c) ? c.Name : cid);
            list.Add(new ConflictItem(
                ConflictType.RoomConflict, ConflictSeverity.Error,
                $"강의실 {g.Key.RoomId}이 {DayName(g.Key.Day)} {g.Key.Period}교시에 중복 점유: {string.Join(", ", labels)}",
                g.Key.Day, g.Key.Period));
        }

        // HC-02: same professor at two different courses at same (day, period)
        var profSlots = new Dictionary<(string Pid, int Day, int Period), HashSet<string>>();
        foreach (var a in assignment)
        {
            if (!courseMap.TryGetValue(a.CourseId, out var c)) continue;
            foreach (var pid in DomainHelpers.CourseProfIds(c))
            {
                var key = (pid, a.Day, a.Period);
                if (!profSlots.TryGetValue(key, out var set))
                    profSlots[key] = set = new HashSet<string>();
                set.Add(a.CourseId);
            }
        }

        // HC-13: fixed courses must stay on their exact fixed slots.
        foreach (var c in courses.Where(c => c.IsFixed))
        {
            var actual = assignment
                .Where(a => a.CourseId == c.Id)
                .Select(a => new TimeSlot(a.Day, a.Period))
                .Distinct()
                .ToHashSet();
            var expected = c.FixedSlots.ToHashSet();
            if (!actual.SetEquals(expected))
            {
                list.Add(new ConflictItem(
                    ConflictType.FixedTimeViolation, ConflictSeverity.Error,
                    $"{c.Name}({c.Id})은 고정 시간표를 벗어날 수 없습니다.",
                    actual.FirstOrDefault().Day, actual.FirstOrDefault().Period));
            }
        }
        foreach (var ((pid, d, p), cids) in profSlots)
        {
            if (cids.Count <= 1) continue;
            var labels = cids.Select(cid =>
                courseMap.TryGetValue(cid, out var c) ? c.Name : cid);
            list.Add(new ConflictItem(
                ConflictType.ProfessorConflict, ConflictSeverity.Error,
                $"교수 {pid}이 {DayName(d)} {p}교시에 중복: {string.Join(", ", labels)}",
                d, p));
        }

        // HC-03: professor unavailable slot
        foreach (var a in assignment)
        {
            if (!courseMap.TryGetValue(a.CourseId, out var c)) continue;
            foreach (var pid in DomainHelpers.CourseProfIds(c))
            {
                if (!profMap.TryGetValue(pid, out var prof)) continue;
                if (a.Period == Constants.LunchPeriod) continue;
                if (prof.UnavailableSlots.Contains(new TimeSlot(a.Day, a.Period)))
                {
                    list.Add(new ConflictItem(
                        ConflictType.ProfUnavailable, ConflictSeverity.Error,
                        $"{c.Name}({a.CourseId})이 교수 {pid} 불가시간 {DayName(a.Day)} {a.Period}교시에 배치됨",
                        a.Day, a.Period));
                }
            }
        }

        // HC-14: course has FixedRooms but uses outside room
        foreach (var c in courses)
        {
            if (c.FixedRooms.Count == 0) continue;
            var allowed = c.FixedRooms.ToHashSet();
            foreach (var a in assignment.Where(a => a.CourseId == c.Id))
            {
                if (!allowed.Contains(a.RoomId))
                {
                    list.Add(new ConflictItem(
                        ConflictType.FixedRoomViolation, ConflictSeverity.Error,
                        $"{c.Name}({c.Id})은 고정 강의실 [{string.Join(",", c.FixedRooms)}] 만 사용 가능 — {a.RoomId} 사용",
                        a.Day, a.Period));
                    break;
                }
            }
        }

        // HC-19: length-2 blocks must start at 1/3/6/8.
        var runs = TimetableRuns.ComputeRuns(assignment);
        foreach (var c in courses)
        {
            if (c.IsFixed) continue;
            var blockLengths = c.BlockStructure.Count > 0
                ? c.BlockStructure
                : new List<int> { c.HoursPerWeek };
            if (!blockLengths.Contains(2)) continue;
            foreach (var ((cid, d, p), len) in runs.RunLen)
            {
                if (cid != c.Id || len != 2) continue;
                if (Constants.Len2StartPeriods.Contains(p)) continue;
                list.Add(new ConflictItem(
                    ConflictType.BlockStartViolation, ConflictSeverity.Error,
                    $"{c.Name}({c.Id})의 2시간 수업은 1, 3, 6, 8교시에만 시작할 수 있습니다.",
                    d, p));
            }
        }

        // HC-20: same course/section/professor must not appear as multiple block occurrences on the same day.
        foreach (var g in assignment
                     .Where(a => courseMap.ContainsKey(a.CourseId))
                     .Select(a => (Assignment: a, Course: courseMap[a.CourseId]))
                     .Select(x => (x.Assignment, x.Course, Key: BuildHc20Key(x.Course, x.Assignment.Day)))
                     .Where(x => x.Key != null)
                     .GroupBy(x => x.Key!))
        {
            var periods = g
                .Select(x => x.Assignment.Period)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            var occurrenceRanges = BuildConsecutiveRanges(periods);
            var groupCourses = g.Select(x => x.Course).DistinctBy(c => c.Id).ToList();
            if (!HasMultipleHc20Occurrences(periods, occurrenceRanges, groupCourses)) continue;

            var sample = g.First().Course;
            var firstRange = occurrenceRanges[0];
            var rangesText = string.Join(", ", occurrenceRanges.Select(FormatPeriodRange));
            list.Add(new ConflictItem(
                ConflictType.SameCourseSameDayConflict,
                ConflictSeverity.Error,
                $"HC-20: 동일 교과목·분반·교수 수업은 같은 요일에 두 번 이상 배치할 수 없습니다. ({sample.Name} / {SectionLabel(sample.Section)}분반 / {ProfessorIdentity(sample)} / {DayName(g.Key.Day)}요일 / {rangesText})",
                g.Key.Day,
                firstRange.Start));
        }

        // HC-21: professor's auto-placed courses (no FixedRooms) should all share one room
        var autoRoomsByProf = new Dictionary<string, HashSet<string>>();
        foreach (var a in assignment)
        {
            if (!courseMap.TryGetValue(a.CourseId, out var c)) continue;
            if (c.FixedRooms.Count > 0) continue;
            var pid = c.ProfessorId;
            if (string.IsNullOrEmpty(pid)) continue;
            if (!autoRoomsByProf.TryGetValue(pid, out var set))
                autoRoomsByProf[pid] = set = new HashSet<string>();
            set.Add(a.RoomId);
        }
        foreach (var (pid, rooms) in autoRoomsByProf)
        {
            if (rooms.Count <= 1) continue;
            list.Add(new ConflictItem(
                ConflictType.ProfRoomInconsistent, ConflictSeverity.Warning,
                $"교수 {pid}의 자동 배정 과목들이 서로 다른 강의실 사용: {string.Join(",", rooms)}",
                0, 0));
        }

        // HC-21 candidate-room subset for auto-assigned courses.
        foreach (var a in assignment)
        {
            if (!courseMap.TryGetValue(a.CourseId, out var c)) continue;
            if (c.FixedRooms.Count > 0) continue;
            if (!profMap.TryGetValue(c.ProfessorId, out var prof)) continue;
            if (prof.AllowedRooms.Count == 0) continue;
            if (prof.AllowedRooms.Contains(a.RoomId)) continue;
            list.Add(new ConflictItem(
                ConflictType.ProfAllowedRoomViolation, ConflictSeverity.Error,
                $"{c.Name}({c.Id})은 교수 {c.ProfessorId}의 허용 강의실 [{string.Join(",", prof.AllowedRooms)}] 안에서만 배정할 수 있습니다.",
                a.Day, a.Period));
        }

        // HC-08: same baseId different sections at same slot
        foreach (var g in assignment.GroupBy(a => (a.Day, a.Period)))
        {
            var byBase = new Dictionary<string, HashSet<string>>();
            foreach (var a in g)
            {
                var bid = DomainHelpers.BaseId(a.CourseId);
                if (!byBase.TryGetValue(bid, out var set))
                    byBase[bid] = set = new HashSet<string>();
                set.Add(a.CourseId);
            }
            foreach (var (bid, cids) in byBase)
            {
                if (cids.Count <= 1) continue;
                list.Add(new ConflictItem(
                    ConflictType.SectionConflict, ConflictSeverity.Error,
                    $"{bid}의 분반이 {DayName(g.Key.Day)} {g.Key.Period}교시에 중복: {string.Join(", ", cids)}",
                    g.Key.Day, g.Key.Period));
            }
        }

        // HC-11: same grade cannot overlap, except same base-id or members of the same cross group.
        bool SameCrossGroup(string b1, string b2)
        {
            if (crosses == null) return false;
            foreach (var cross in crosses)
                if (cross.BaseIds.Contains(b1) && cross.BaseIds.Contains(b2))
                    return true;
            return false;
        }

        foreach (var g in assignment.GroupBy(a => (a.Day, a.Period)))
        {
            var cids = g.Select(a => a.CourseId).Distinct().ToList();
            for (int i = 0; i < cids.Count; i++)
                for (int j = i + 1; j < cids.Count; j++)
                {
                    if (!courseMap.TryGetValue(cids[i], out var c1)) continue;
                    if (!courseMap.TryGetValue(cids[j], out var c2)) continue;
                    if (c1.Grade <= 0 || c2.Grade <= 0 || c1.Grade != c2.Grade) continue;
                    var b1 = DomainHelpers.BaseId(c1.Id);
                    var b2 = DomainHelpers.BaseId(c2.Id);
                    if (b1 == b2
                        || SameCrossGroup(b1, b2)
                        || isManualGradeOverlapAllowed?.Invoke(c1.Id, c2.Id, g.Key.Day, g.Key.Period) == true)
                        continue;
                    list.Add(new ConflictItem(
                        ConflictType.GradeConflict, ConflictSeverity.Error,
                        $"{c1.Grade}학년 과목이 {DayName(g.Key.Day)} {g.Key.Period}교시에 중복: {c1.Name}, {c2.Name}",
                        g.Key.Day, g.Key.Period));
                }
        }

        return list;
    }

    private static string DayName(int d) => d switch
    {
        0 => "월", 1 => "화", 2 => "수", 3 => "목", 4 => "금",
        _ => "?"
    };

    private sealed record Hc20Key(int Day, string CourseIdentity, int Section, string ProfessorIdentity);

    private static Hc20Key? BuildHc20Key(Course course, int day)
    {
        if (course.Section <= 0) return null;
        var courseIdentity = CourseIdentity(course);
        var professorIdentity = ProfessorIdentity(course);
        if (string.IsNullOrWhiteSpace(courseIdentity) || string.IsNullOrWhiteSpace(professorIdentity)) return null;
        return new Hc20Key(day, courseIdentity, course.Section, professorIdentity);
    }

    private static string CourseIdentity(Course course)
    {
        var name = Normalize(course.Name);
        return string.IsNullOrWhiteSpace(name) ? Normalize(course.Id) : name;
    }

    private static string ProfessorIdentity(Course course) =>
        string.Join("|", DomainHelpers.CourseProfIds(course)
            .Where(pid => !string.IsNullOrWhiteSpace(pid))
            .Select(Normalize)
            .OrderBy(pid => pid, StringComparer.Ordinal));

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string SectionLabel(int section) =>
        section >= 1 ? ((char)('A' + section - 1)).ToString() : section.ToString();

    private static List<(int Start, int End)> BuildConsecutiveRanges(IReadOnlyList<int> periods)
    {
        var ranges = new List<(int Start, int End)>();
        if (periods.Count == 0) return ranges;

        var start = periods[0];
        var end = periods[0];
        for (var i = 1; i < periods.Count; i++)
        {
            if (periods[i] == end + 1)
            {
                end = periods[i];
                continue;
            }

            ranges.Add((start, end));
            start = end = periods[i];
        }

        ranges.Add((start, end));
        return ranges;
    }

    private static bool HasMultipleHc20Occurrences(
        IReadOnlyList<int> periods,
        IReadOnlyList<(int Start, int End)> consecutiveRanges,
        IReadOnlyList<Course> courses)
    {
        if (periods.Count <= 1) return false;
        if (consecutiveRanges.Count > 1) return true;

        var singleRangeLength = consecutiveRanges[0].End - consecutiveRanges[0].Start + 1;
        return !courses.Any(course => KnownBlockLengths(course).Contains(singleRangeLength));
    }

    private static IReadOnlySet<int> KnownBlockLengths(Course course)
    {
        var lengths = course.BlockStructure.Count > 0
            ? course.BlockStructure
            : new List<int> { course.HoursPerWeek };

        return lengths
            .Where(length => length > 0)
            .ToHashSet();
    }

    private static string FormatPeriodRange((int Start, int End) range) =>
        range.Start == range.End ? $"{range.Start}교시" : $"{range.Start}~{range.End}교시";
}
