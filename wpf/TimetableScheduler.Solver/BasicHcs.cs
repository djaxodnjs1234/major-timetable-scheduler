using Google.OrTools.Sat;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

using XDict = Dictionary<(string CourseId, int Day, int Period, string RoomId), BoolVar>;
using YDict = Dictionary<(string CourseId, int Day, int Period), BoolVar>;
using LunchVarMap = Dictionary<(int Day, int Period), BoolVar>;

public static class BasicHcs
{
    public static void AddHc01_RoomSingle(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms,
        SchedulePolicy schedulePolicy)
    {
        var periods = SchedulePolicyRules.CandidateInstructionalPeriods(schedulePolicy);
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in periods)
                foreach (var r in rooms)
                {
                    var terms = courses.Select(c => x[(c.Id, d, p, r.Id)]).ToArray();
                    model.Add(LinearExpr.Sum(terms) <= 1);
                }
    }

    public static void AddHc02_ProfSingle(
        CpModel model, YDict y, IReadOnlyList<Course> courses, SchedulePolicy schedulePolicy)
    {
        var profCourses = new Dictionary<string, List<Course>>();
        foreach (var c in courses)
            foreach (var pid in DomainHelpers.CourseProfIds(c))
            {
                if (!profCourses.TryGetValue(pid, out var list))
                    profCourses[pid] = list = new List<Course>();
                list.Add(c);
            }

        var periods = SchedulePolicyRules.CandidateInstructionalPeriods(schedulePolicy);
        foreach (var (_, pcourses) in profCourses)
            for (int d = 0; d < Constants.Days; d++)
                foreach (var p in periods)
                {
                    var terms = pcourses.Select(c => y[(c.Id, d, p)]).ToArray();
                    model.Add(LinearExpr.Sum(terms) <= 1);
                }
    }

    public static void AddHc03_ProfUnavailable(
        CpModel model, XDict x, IReadOnlyList<Course> courses,
        IReadOnlyList<Room> rooms, Dictionary<string, Professor> profMap)
    {
        foreach (var c in courses)
            foreach (var pid in DomainHelpers.CourseProfIds(c))
            {
                if (!profMap.TryGetValue(pid, out var prof)) continue;
                foreach (var slot in prof.UnavailableSlots)
                {
                    if (slot.Day < 0 || slot.Day >= Constants.Days
                        || !Constants.Periods.Contains(slot.Period))
                        continue;
                    foreach (var r in rooms)
                        model.Add(x[(c.Id, slot.Day, slot.Period, r.Id)] == 0);
                }
            }
    }

    public static void AddHc04_Hours(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms,
        SchedulePolicy schedulePolicy)
    {
        var periods = SchedulePolicyRules.CandidateInstructionalPeriods(schedulePolicy);
        foreach (var c in courses)
        {
            int K = Math.Max(c.FixedRooms.Count, 1);
            var terms = new List<BoolVar>();
            for (int d = 0; d < Constants.Days; d++)
                foreach (var p in periods)
                    foreach (var r in rooms)
                        terms.Add(x[(c.Id, d, p, r.Id)]);
            model.Add(LinearExpr.Sum(terms) == c.HoursPerWeek * K);
        }
    }

    public static void AddHc08_SectionNoOverlap(
        CpModel model, YDict y, IReadOnlyList<Course> courses, SchedulePolicy schedulePolicy)
    {
        var periods = SchedulePolicyRules.CandidateInstructionalPeriods(schedulePolicy);
        var groups = ConstraintHelpers.GroupByBaseId(courses);
        foreach (var (_, group) in groups)
        {
            if (group.Count < 2) continue;
            for (int i = 0; i < group.Count; i++)
                for (int j = i + 1; j < group.Count; j++)
                {
                    var c1 = group[i];
                    var c2 = group[j];
                    for (int d = 0; d < Constants.Days; d++)
                        foreach (var p in periods)
                            model.Add(y[(c1.Id, d, p)] + y[(c2.Id, d, p)] <= 1);
                }
        }
    }

    public static void AddHc11_GradeNoOverlap(
        CpModel model, YDict y, IReadOnlyList<Course> courses, IReadOnlyList<CrossGroup>? crosses,
        SchedulePolicy schedulePolicy)
    {
        var periods = SchedulePolicyRules.CandidateInstructionalPeriods(schedulePolicy);
        var byGrade = new Dictionary<int, List<Course>>();
        foreach (var c in courses)
        {
            if (!byGrade.TryGetValue(c.Grade, out var list))
                byGrade[c.Grade] = list = new List<Course>();
            list.Add(c);
        }

        bool SameCrossGroup(string b1, string b2)
        {
            if (crosses == null) return false;
            foreach (var g in crosses)
                if (g.BaseIds.Contains(b1) && g.BaseIds.Contains(b2))
                    return true;
            return false;
        }

        foreach (var (_, gcourses) in byGrade)
            for (int i = 0; i < gcourses.Count; i++)
                for (int j = i + 1; j < gcourses.Count; j++)
                {
                    var c1 = gcourses[i];
                    var c2 = gcourses[j];
                    var b1 = DomainHelpers.BaseId(c1.Id);
                    var b2 = DomainHelpers.BaseId(c2.Id);
                    if (b1 == b2) continue;
                    if (SameCrossGroup(b1, b2)) continue;
                    for (int d = 0; d < Constants.Days; d++)
                        foreach (var p in periods)
                            model.Add(y[(c1.Id, d, p)] + y[(c2.Id, d, p)] <= 1);
                }
    }

    public static LunchVarMap AddHc12_Lunch(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms,
        SchedulePolicy schedulePolicy)
    {
        var lunchBanVars = new LunchVarMap();
        if (schedulePolicy.LunchMode == LunchPolicyMode.None)
            return lunchBanVars;

        var staticLunch = SchedulePolicyRules.StaticLunchPeriod(schedulePolicy);
        if (staticLunch.HasValue)
        {
            foreach (var c in courses)
                for (int d = 0; d < Constants.Days; d++)
                    foreach (var r in rooms)
                        model.Add(x[(c.Id, d, staticLunch.Value, r.Id)] == 0);
            return lunchBanVars;
        }

        for (var day = 0; day < Constants.Days; day++)
        {
            var ban4 = model.NewBoolVar($"lunch_ban_{day}_4");
            var ban5 = model.NewBoolVar($"lunch_ban_{day}_5");
            lunchBanVars[(day, SchedulePolicyRules.FirstLunchCandidate)] = ban4;
            lunchBanVars[(day, SchedulePolicyRules.SecondLunchCandidate)] = ban5;
            model.Add(ban4 + ban5 == 1);

            foreach (var course in courses)
                foreach (var room in rooms)
                {
                    model.Add(x[(course.Id, day, SchedulePolicyRules.FirstLunchCandidate, room.Id)] + ban4 <= 1);
                    model.Add(x[(course.Id, day, SchedulePolicyRules.SecondLunchCandidate, room.Id)] + ban5 <= 1);
                }
        }
        return lunchBanVars;
    }

    public static void AddHc13_Fixed(
        CpModel model, YDict y, IReadOnlyList<Course> courses)
    {
        foreach (var c in courses)
        {
            if (!c.IsFixed) continue;
            foreach (var slot in c.FixedSlots)
                model.Add(y[(c.Id, slot.Day, slot.Period)] == 1);
        }
    }

    public static void AddHc24_SchoolFixedTimeBlocks(
        CpModel model,
        YDict y,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Course> schoolFixedCourses,
        SchedulePolicy schedulePolicy,
        LunchVarMap lunchBanVars)
    {
        foreach (var schoolFixed in schoolFixedCourses)
        {
            if (!schoolFixed.IsSchoolFixed || !schoolFixed.IsFixed) continue;
            foreach (var slot in schoolFixed.FixedSlots)
            {
                if (SchedulePolicyRules.IsStaticallyBlocked(schedulePolicy, slot.Period))
                {
                    var infeasible = model.NewBoolVar(
                        $"school_fixed_lunch_conflict_{schoolFixed.Id}_{slot.Day}_{slot.Period}");
                    model.Add(infeasible == 0);
                    model.Add(infeasible == 1);
                }
                else if (SchedulePolicyRules.UsesFlexibleLunch(schedulePolicy)
                    && lunchBanVars.TryGetValue((slot.Day, slot.Period), out var lunchBan))
                {
                    model.Add(lunchBan == 0);
                }

                foreach (var course in courses)
                {
                    if (course.IsFixed) continue;
                    if (!SchoolFixedTimePolicy.AppliesToGrade(schoolFixed, course.Grade)) continue;
                    if (y.TryGetValue((course.Id, slot.Day, slot.Period), out var yv))
                        model.Add(yv == 0);
                }
            }
        }
    }

    public static void AddHc23_AcademicLevelTimeBands(
        CpModel model,
        XDict x,
        YDict y,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<CrossGroup>? crosses,
        SchedulePolicy schedulePolicy)
    {
        var allowGraduateDaytimeOverflow =
            AcademicLevelTimePolicy.AllowsGraduateDaytimeOverflow(courses, crosses);
        foreach (var course in courses)
        {
            var disallowedPeriods =
                AcademicLevelTimePolicy.DisallowedPeriods(
                    course.Grade, allowGraduateDaytimeOverflow, schedulePolicy);
            foreach (var day in Enumerable.Range(0, Constants.Days))
                foreach (var period in disallowedPeriods)
                    foreach (var room in rooms)
                        model.Add(x[(course.Id, day, period, room.Id)] == 0);
        }

        if (!allowGraduateDaytimeOverflow) return;

        var overflowLimit = AcademicLevelTimePolicy.GraduateDaytimeOverflowSlots(courses, crosses);
        var graduateDaytimeTerms = courses
            .Where(course => course.Grade == AcademicLevels.GraduateGrade)
            .SelectMany(course => Enumerable.Range(0, Constants.Days)
                .SelectMany(day => SchedulePolicyRules.CandidateDaytimePeriods(schedulePolicy)
                    .Select(period => y[(course.Id, day, period)])))
            .ToArray();
        if (graduateDaytimeTerms.Length > 0)
            model.Add(LinearExpr.Sum(graduateDaytimeTerms) <= overflowLimit);
    }
}
