using Google.OrTools.Sat;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

using XDict = Dictionary<(string CourseId, int Day, int Period, string RoomId), BoolVar>;
using YDict = Dictionary<(string CourseId, int Day, int Period), BoolVar>;

public static class BasicHcs
{
    public static void AddHc01_RoomSingle(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms)
    {
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in Constants.ValidPeriods)
                foreach (var r in rooms)
                {
                    var terms = courses.Select(c => x[(c.Id, d, p, r.Id)]).ToArray();
                    model.Add(LinearExpr.Sum(terms) <= 1);
                }
    }

    public static void AddHc02_ProfSingle(
        CpModel model, YDict y, IReadOnlyList<Course> courses)
    {
        var profCourses = new Dictionary<string, List<Course>>();
        foreach (var c in courses)
            foreach (var pid in DomainHelpers.CourseProfIds(c))
            {
                if (!profCourses.TryGetValue(pid, out var list))
                    profCourses[pid] = list = new List<Course>();
                list.Add(c);
            }

        foreach (var (_, pcourses) in profCourses)
            for (int d = 0; d < Constants.Days; d++)
                foreach (var p in Constants.ValidPeriods)
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
                    if (slot.Period == Constants.LunchPeriod) continue;
                    foreach (var r in rooms)
                        model.Add(x[(c.Id, slot.Day, slot.Period, r.Id)] == 0);
                }
            }
    }

    public static void AddHc04_Hours(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms)
    {
        foreach (var c in courses)
        {
            int K = Math.Max(c.FixedRooms.Count, 1);
            var terms = new List<BoolVar>();
            for (int d = 0; d < Constants.Days; d++)
                foreach (var p in Constants.ValidPeriods)
                    foreach (var r in rooms)
                        terms.Add(x[(c.Id, d, p, r.Id)]);
            model.Add(LinearExpr.Sum(terms) == c.HoursPerWeek * K);
        }
    }

    public static void AddHc08_SectionNoOverlap(CpModel model, YDict y, IReadOnlyList<Course> courses)
    {
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
                        foreach (var p in Constants.ValidPeriods)
                            model.Add(y[(c1.Id, d, p)] + y[(c2.Id, d, p)] <= 1);
                }
        }
    }

    public static void AddHc11_GradeNoOverlap(
        CpModel model, YDict y, IReadOnlyList<Course> courses, IReadOnlyList<CrossGroup>? crosses)
    {
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
                        foreach (var p in Constants.ValidPeriods)
                            model.Add(y[(c1.Id, d, p)] + y[(c2.Id, d, p)] <= 1);
                }
    }

    public static void AddHc12_Lunch(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms)
    {
        foreach (var c in courses)
            for (int d = 0; d < Constants.Days; d++)
                foreach (var r in rooms)
                    model.Add(x[(c.Id, d, Constants.LunchPeriod, r.Id)] == 0);
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
}
