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
    CourseUnavailableRoomViolation,
    BlockStartViolation,
    ProfRoomInconsistent,
    SameCourseSameDayConflict,
    AcademicLevelTimeBandViolation,
}

public sealed record ConflictItem(
    ConflictType Type,
    ConflictSeverity Severity,
    string Description,
    int Day,
    int Period,
    IReadOnlyList<SolutionAssignment>? Assignments = null);

public static class ConflictDetector
{
    public static IReadOnlyList<ConflictItem> Detect(
        IReadOnlyList<SolutionAssignment> assignment,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor>? professors = null,
        IReadOnlyList<CrossGroup>? crosses = null,
        Func<string, string, int, int, bool>? isManualGradeOverlapAllowed = null,
        bool allowGraduateDaytimeOverflow = false,
        SchedulePolicy? schedulePolicy = null,
        IReadOnlyDictionary<int, int>? lunchPeriodsByDay = null)
    {
        schedulePolicy ??= SchedulePolicy.Default;
        lunchPeriodsByDay ??=
            SchedulePolicyRules.StaticLunchPeriodsByDay(schedulePolicy, Constants.Days);
        var list = new List<ConflictItem>();
        var profMap = professors?.ToDictionary(p => p.Id) ?? new Dictionary<string, Professor>();
        var resolved = assignment
            .Select(a => (Assignment: a, Course: ResolveCourseForAssignment(a, courses)))
            .ToList();

        // HC-12: the configured or solver-selected lunch cell must stay empty.
        foreach (var a in assignment.Where(a => SchedulePolicyRules.IsLunch(
                     schedulePolicy, lunchPeriodsByDay, a.Day, a.Period)))
        {
            var name = ResolveCourseForAssignment(a, courses)?.Name ?? a.CourseId;
            list.Add(new ConflictItem(
                ConflictType.LunchConflict, ConflictSeverity.Error,
                $"{name}({a.CourseId})가 점심 시간({DayName(a.Day)} {a.Period}교시)에 배치됨",
                a.Day, a.Period,
                new[] { a }));
        }

        // HC-23: graduate courses use night periods unless graduate overflow needs daytime;
        // undergraduate courses use daytime only.
        foreach (var (a, course) in resolved)
        {
            if (course == null || SchedulePolicyRules.IsLunch(
                    schedulePolicy, lunchPeriodsByDay, a.Day, a.Period)) continue;
            var allowedPeriods =
                AcademicLevelTimePolicy.AllowedPeriods(
                    course.Grade, allowGraduateDaytimeOverflow, schedulePolicy);
            if (allowedPeriods.Contains(a.Period)) continue;

            list.Add(new ConflictItem(
                ConflictType.AcademicLevelTimeBandViolation,
                ConflictSeverity.Error,
                course.Grade == AcademicLevels.GraduateGrade
                    ? $"{course.Name}({course.Id}) 대학원 과목은 야간 10~13교시에만 배치할 수 있습니다."
                    : $"{course.Name}({course.Id}) 학부 과목은 야간 10~13교시에 배치할 수 없습니다.",
                a.Day,
                a.Period,
                new[] { a }));
        }

        // HC-01: room double-booked at same (day, period, room)
        foreach (var g in assignment.GroupBy(a => (a.Day, a.Period, a.RoomId)))
        {
            // multi-room same occurrence is OK: distinct assignment identities only
            var distinctAssignments = g
                .DistinctBy(AssignmentConflictIdentity)
                .ToList();
            if (distinctAssignments.Count <= 1) continue;
            var labels = distinctAssignments.Select(a =>
                ResolveCourseForAssignment(a, courses)?.Name ?? a.CourseId);
            list.Add(new ConflictItem(
                ConflictType.RoomConflict, ConflictSeverity.Error,
                $"강의실 {g.Key.RoomId}이 {DayName(g.Key.Day)} {g.Key.Period}교시에 중복 점유: {string.Join(", ", labels)}",
                g.Key.Day, g.Key.Period,
                distinctAssignments));
        }

        // HC-02: same professor at two different courses at same (day, period)
        var profSlots = new Dictionary<(string Pid, int Day, int Period), HashSet<string>>();
        foreach (var (a, c) in resolved)
        {
            if (c == null) continue;
            foreach (var pid in DomainHelpers.CourseProfIds(c))
            {
                var key = (pid, a.Day, a.Period);
                if (!profSlots.TryGetValue(key, out var set))
                    profSlots[key] = set = new HashSet<string>();
                set.Add(AssignmentConflictIdentity(a));
            }
        }

        // HC-13: fixed courses must stay on their exact fixed slots.
        foreach (var c in courses.Where(c => c.IsFixed && !c.IsSchoolFixed))
        {
            var actual = assignment
                .Where(a => ReferenceEquals(ResolveCourseForAssignment(a, courses), c))
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
        foreach (var ((pid, d, p), assignmentIds) in profSlots)
        {
            if (assignmentIds.Count <= 1) continue;
            var conflictAssignments = assignment
                .Where(a => a.Day == d
                    && a.Period == p
                    && assignmentIds.Contains(AssignmentConflictIdentity(a)))
                .ToList();
            var labels = conflictAssignments
                .Select(a => ResolveCourseForAssignment(a, courses)?.Name ?? a.CourseId);
            list.Add(new ConflictItem(
                ConflictType.ProfessorConflict, ConflictSeverity.Error,
                $"교수 {pid}이 {DayName(d)} {p}교시에 중복: {string.Join(", ", labels)}",
                d, p,
                conflictAssignments));
        }

        // HC-03: professor unavailable slot
        foreach (var a in assignment)
        {
            var c = ResolveCourseForAssignment(a, courses);
            if (c == null) continue;
            foreach (var pid in DomainHelpers.CourseProfIds(c))
            {
                if (!profMap.TryGetValue(pid, out var prof)) continue;
                if (SchedulePolicyRules.IsLunch(
                        schedulePolicy, lunchPeriodsByDay, a.Day, a.Period)) continue;
                if (prof.UnavailableSlots.Contains(new TimeSlot(a.Day, a.Period)))
                {
                    list.Add(new ConflictItem(
                        ConflictType.ProfUnavailable, ConflictSeverity.Error,
                        $"{c.Name}({a.CourseId})이 교수 {pid} 불가시간 {DayName(a.Day)} {a.Period}교시에 배치됨",
                        a.Day, a.Period,
                        new[] { a }));
                }
            }
        }

        // HC-14: course has FixedRooms but uses outside room
        foreach (var a in assignment)
        {
            var c = ResolveCourseForAssignment(a, courses);
            if (c == null || c.FixedRooms.Count == 0) continue;
            var allowed = c.FixedRooms.ToHashSet();
            if (!allowed.Contains(a.RoomId))
            {
                list.Add(new ConflictItem(
                    ConflictType.FixedRoomViolation, ConflictSeverity.Error,
                    $"{c.Name}({c.Id})은 고정 강의실 [{string.Join(",", c.FixedRooms)}] 만 사용 가능 — {a.RoomId} 사용",
                    a.Day, a.Period,
                    new[] { a }));
            }
        }

        foreach (var a in assignment)
        {
            var c = ResolveCourseForAssignment(a, courses);
            if (c == null || c.UnavailableRooms.Count == 0) continue;
            var blocked = c.UnavailableRooms.ToHashSet();
            if (!blocked.Contains(a.RoomId)) continue;
            list.Add(new ConflictItem(
                ConflictType.CourseUnavailableRoomViolation, ConflictSeverity.Error,
                $"{c.Name}({c.Id})은 불가 강의실 [{string.Join(",", c.UnavailableRooms)}]을 사용할 수 없습니다 — {a.RoomId} 사용",
                a.Day, a.Period,
                new[] { a }));
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
                var runAssignment = assignment
                    .Where(a => a.CourseId == cid && a.Day == d && a.Period >= p && a.Period < p + len)
                    .FirstOrDefault(a => ReferenceEquals(ResolveCourseForAssignment(a, courses), c));
                if (runAssignment == default) continue;
                var allowedPeriods = AcademicLevelTimePolicy.AllowedPeriods(
                    c.Grade, allowGraduateDaytimeOverflow, schedulePolicy);
                if (lunchPeriodsByDay.TryGetValue(d, out var lunchPeriod))
                    allowedPeriods = allowedPeriods.Where(period => period != lunchPeriod).ToArray();
                var allowedStarts = SchedulePolicyRules.DeriveTwoHourStarts(allowedPeriods);
                if (allowedStarts.Contains(p)) continue;
                list.Add(new ConflictItem(
                    ConflictType.BlockStartViolation, ConflictSeverity.Error,
                    $"{c.Name}({c.Id})의 2시간 수업 시작 교시가 현재 점심시간 정책과 맞지 않습니다.",
                    d, p,
                    assignment
                        .Where(a => a.CourseId == cid && a.Day == d && a.Period >= p && a.Period < p + len)
                        .Where(a => ReferenceEquals(ResolveCourseForAssignment(a, courses), c))
                        .ToList()));
            }
        }

        // HC-20: same course/section/professor must not appear as multiple block occurrences on the same day.
        foreach (var g in assignment
                     .Select(a => (Assignment: a, Course: ResolveCourseForAssignment(a, courses)))
                     .Where(x => x.Course != null)
                     .Select(x => (x.Assignment, Course: x.Course!))
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
                firstRange.Start,
                g.Select(x => x.Assignment).ToList()));
        }

        // HC-08: same baseId different sections at same slot
        foreach (var g in assignment.GroupBy(a => (a.Day, a.Period)))
        {
            var byBase = new Dictionary<string, List<(SolutionAssignment Assignment, Course? Course)>>();
            foreach (var a in g)
            {
                var c = ResolveCourseForAssignment(a, courses);
                var bid = DomainHelpers.BaseId(c?.Id ?? a.CourseId);
                if (!byBase.TryGetValue(bid, out var set))
                    byBase[bid] = set = new List<(SolutionAssignment, Course?)>();
                set.Add((a, c));
            }
            foreach (var (bid, rows) in byBase)
            {
                var hasMultipleSections = rows.Select(x => x.Assignment.CourseId).Distinct(StringComparer.Ordinal).Count() > 1
                    || rows.Select(x => x.Course?.Section).Where(section => section is > 0).Distinct().Count() > 1;
                if (!hasMultipleSections) continue;
                list.Add(new ConflictItem(
                    ConflictType.SectionConflict, ConflictSeverity.Error,
                    $"{bid}의 분반이 {DayName(g.Key.Day)} {g.Key.Period}교시에 중복: {string.Join(", ", rows.Select(x => x.Course?.Name ?? x.Assignment.CourseId))}",
                    g.Key.Day, g.Key.Period,
                    rows.Select(x => x.Assignment).ToList()));
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
            var rows = g
                .Select(a => (Assignment: a, Course: ResolveCourseForAssignment(a, courses)))
                .Where(x => x.Course != null)
                .Select(x => (x.Assignment, Course: x.Course!))
                .DistinctBy(x => AssignmentConflictIdentity(x.Assignment))
                .ToList();
            for (int i = 0; i < rows.Count; i++)
                for (int j = i + 1; j < rows.Count; j++)
                {
                    var c1 = rows[i].Course;
                    var c2 = rows[j].Course;
                    if (c1.Grade <= 0 || c2.Grade <= 0 || c1.Grade != c2.Grade) continue;
                    var b1 = DomainHelpers.BaseId(c1.Id);
                    var b2 = DomainHelpers.BaseId(c2.Id);
                    if (b1 == b2
                        || SameCrossGroup(b1, b2)
                        || isManualGradeOverlapAllowed?.Invoke(c1.Id, c2.Id, g.Key.Day, g.Key.Period) == true)
                        continue;
                    list.Add(new ConflictItem(
                        ConflictType.GradeConflict, ConflictSeverity.Error,
                        $"{AcademicLevels.DisplayName(c1.Grade)} 과목이 {DayName(g.Key.Day)} {g.Key.Period}교시에 중복: {c1.Name}, {c2.Name}",
                        g.Key.Day, g.Key.Period,
                        new[] { rows[i].Assignment, rows[j].Assignment }));
                }
        }

        return list;
    }

    private static Course? ResolveCourseForAssignment(
        SolutionAssignment assignment,
        IReadOnlyList<Course> courses)
    {
        var candidates = courses
            .Where(c => c.Id == assignment.CourseId)
            .ToList();
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        var assignmentIdCourse = ResolveCourseFromAssignmentId(assignment.AssignmentId, candidates);
        if (assignmentIdCourse != null) return assignmentIdCourse;

        var roomId = NormalizeRoomId(assignment.RoomId);
        var fixedRoomMatches = candidates
            .Where(c => c.FixedRooms.Any(room => string.Equals(NormalizeRoomId(room), roomId, StringComparison.Ordinal)))
            .ToList();
        if (fixedRoomMatches.Count == 1) return fixedRoomMatches[0];
        if (fixedRoomMatches.Count > 0) return null;

        var unavailableRoomExclusions = candidates
            .Where(c => c.UnavailableRooms.All(room => !string.Equals(NormalizeRoomId(room), roomId, StringComparison.Ordinal)))
            .ToList();
        if (unavailableRoomExclusions.Count == 1) return unavailableRoomExclusions[0];
        if (unavailableRoomExclusions.Count > 0) return null;

        return null;
    }

    private static Course? ResolveCourseFromAssignmentId(
        string? assignmentId,
        IReadOnlyList<Course> candidates)
    {
        if (string.IsNullOrWhiteSpace(assignmentId)) return null;

        var parts = assignmentId.Split('\u001f');
        if (parts.Length < 7 || !string.Equals(parts[0], "occ", StringComparison.Ordinal))
            return null;

        var courseId = parts[1];
        var sectionText = parts[2];
        var professorId = parts[3];
        if (!int.TryParse(sectionText, out var section)) return null;

        var matches = candidates
            .Where(c => string.Equals(c.Id, courseId, StringComparison.Ordinal)
                && c.Section == section
                && string.Equals(c.ProfessorId, professorId, StringComparison.Ordinal))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string AssignmentConflictIdentity(SolutionAssignment assignment) =>
        string.IsNullOrWhiteSpace(assignment.AssignmentId)
            ? string.Join("\u001f", assignment.CourseId, assignment.Day, assignment.Period, assignment.RoomId)
            : assignment.AssignmentId;

    private static string NormalizeRoomId(string? roomId) =>
        string.IsNullOrWhiteSpace(roomId) ? "" : roomId.Trim();

    private static string DayName(int d) => d switch
    {
        0 => "월",
        1 => "화",
        2 => "수",
        3 => "목",
        4 => "금",
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
