using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

public static class AcademicLevelTimePolicy
{
    public const int GraduateMaxHoursPerWeek = 4;

    public static int GraduateNightCapacity => Constants.Days * Constants.NightPeriods.Count;

    public static int GraduateRequiredSlots(
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses = null)
    {
        var baseRequirements = BaseGradeSlotRequirements(courses);
        var crossOverlapByGrade = CrossSlotOverlapByGrade(crosses, baseRequirements);
        var rawRequired = baseRequirements
            .Where(pair => pair.Key.Grade == AcademicLevels.GraduateGrade)
            .Sum(pair => pair.Value);
        var crossOverlap = crossOverlapByGrade.TryGetValue(AcademicLevels.GraduateGrade, out var overlap)
            ? overlap
            : 0;
        return Math.Max(0, rawRequired - crossOverlap);
    }

    public static int GraduateDaytimeOverflowSlots(
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses = null)
    {
        var slotOverflow = Math.Max(0, GraduateRequiredSlots(courses, crosses) - GraduateNightCapacity);
        var blockOverflow = HasGraduateCross(courses, crosses)
            ? 0
            : Math.Max(0, GraduateThreeHourBlockCount(courses) - Constants.Days) * 3;
        return Math.Max(slotOverflow, blockOverflow);
    }

    public static bool AllowsGraduateDaytimeOverflow(
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses = null) =>
        GraduateDaytimeOverflowSlots(courses, crosses) > 0;

    public static IReadOnlyList<int> AllowedPeriods(
        int grade,
        bool allowGraduateDaytimeOverflow,
        SchedulePolicy? schedulePolicy = null)
    {
        schedulePolicy ??= SchedulePolicy.Default;
        return grade == AcademicLevels.GraduateGrade
            ? allowGraduateDaytimeOverflow
                ? SchedulePolicyRules.CandidateInstructionalPeriods(schedulePolicy)
                : Constants.NightPeriods
            : SchedulePolicyRules.CandidateDaytimePeriods(schedulePolicy);
    }

    public static IReadOnlyList<int> DisallowedPeriods(
        int grade,
        bool allowGraduateDaytimeOverflow,
        SchedulePolicy? schedulePolicy = null)
    {
        var allowed = AllowedPeriods(grade, allowGraduateDaytimeOverflow, schedulePolicy).ToHashSet();
        return Constants.Periods.Where(period => !allowed.Contains(period)).ToArray();
    }

    public static bool IsGraduateOverMaxHours(Course course) =>
        course.Grade == AcademicLevels.GraduateGrade &&
        course.HoursPerWeek > GraduateMaxHoursPerWeek;

    private static int GraduateThreeHourBlockCount(IReadOnlyList<Course> courses) =>
        courses
            .Where(course => course.Grade == AcademicLevels.GraduateGrade)
            .Sum(course => EffectiveBlocks(course).Count(block => block == 3));

    private static bool HasGraduateCross(
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses)
    {
        if (crosses == null || crosses.Count == 0) return false;

        var graduateBaseIds = courses
            .Where(course => course.Grade == AcademicLevels.GraduateGrade)
            .Select(course => DomainHelpers.BaseId(course.Id))
            .ToHashSet(StringComparer.Ordinal);
        return crosses.Any(cross =>
            cross.BaseIds.Count == 2 &&
            cross.BaseIds.All(graduateBaseIds.Contains));
    }

    private static Dictionary<(int Grade, string BaseId), int> BaseGradeSlotRequirements(IReadOnlyList<Course> courses) =>
        courses
            .Where(course => course.Grade > 0)
            .GroupBy(course => (course.Grade, BaseId: DomainHelpers.BaseId(course.Id)))
            .ToDictionary(
                group => group.Key,
                group => group.Sum(RequiredSlotCount));

    private static Dictionary<int, int> CrossSlotOverlapByGrade(
        IReadOnlyList<CrossGroup>? crosses,
        IReadOnlyDictionary<(int Grade, string BaseId), int> baseRequirements)
    {
        var result = new Dictionary<int, int>();
        if (crosses == null || crosses.Count == 0) return result;

        var seenPairs = new HashSet<(int Grade, string FirstBaseId, string SecondBaseId)>();
        foreach (var cross in crosses)
        {
            if (cross.BaseIds.Count != 2) continue;

            var leftBaseId = cross.BaseIds[0];
            var rightBaseId = cross.BaseIds[1];
            if (string.Equals(leftBaseId, rightBaseId, StringComparison.Ordinal))
                continue;

            foreach (var left in baseRequirements.Where(pair => pair.Key.BaseId == leftBaseId))
            {
                if (!baseRequirements.TryGetValue((left.Key.Grade, rightBaseId), out var rightRequired))
                    continue;

                var firstBaseId = string.CompareOrdinal(leftBaseId, rightBaseId) <= 0 ? leftBaseId : rightBaseId;
                var secondBaseId = string.CompareOrdinal(leftBaseId, rightBaseId) <= 0 ? rightBaseId : leftBaseId;
                if (!seenPairs.Add((left.Key.Grade, firstBaseId, secondBaseId)))
                    continue;

                var overlap = Math.Min(left.Value, rightRequired);
                result[left.Key.Grade] = result.TryGetValue(left.Key.Grade, out var previous)
                    ? previous + overlap
                    : overlap;
            }
        }

        return result;
    }

    private static List<int> EffectiveBlocks(Course course) =>
        course.BlockStructure.Count > 0 ? course.BlockStructure.ToList() : new List<int> { course.HoursPerWeek };

    private static int RequiredSlotCount(Course course) =>
        Math.Max(course.HoursPerWeek, course.BlockStructure.Count > 0 ? course.BlockStructure.Sum() : course.HoursPerWeek);
}
