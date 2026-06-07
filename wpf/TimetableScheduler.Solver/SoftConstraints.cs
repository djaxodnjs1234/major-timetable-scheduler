using Google.OrTools.Sat;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

using XDict = Dictionary<(string CourseId, int Day, int Period, string RoomId), BoolVar>;
using DayVarMap = Dictionary<string, List<IntVar>>;

public static class SoftConstraints
{
    // SC-01: Mon AM (1~4) + Fri PM (6~9) avoidance
    public static readonly IReadOnlySet<(int Day, int Period)> PenalizedSlots =
        new HashSet<(int, int)>(
            new[] { 1, 2, 3, 4 }.Select(p => (0, p))
            .Concat(new[] { 6, 7, 8, 9 }.Select(p => (4, p))));

    // SC-01 최적값보다 허용할 추가 회피 슬롯 위반 수.
    public static readonly int Sc01SlackAbs = 0;

    // SC-02 교수별 강의 요일 수가 이 값을 넘으면 패널티.
    public static readonly int Sc02DayThreshold = 3;
    // SC-02 적용 대상 교수의 최소 담당 과목 수.
    public static readonly int Sc02MinCourses = 2;
    // SC-02 계산에서 시간 고정 과목 제외 여부.
    public static readonly bool Sc02ExcludeFixed = true;
    // SC-02 계산에서 팀티칭 추가 교수 제외 여부.
    public static readonly bool Sc02ExcludeCoteach = true;
    // SC-02 최적값보다 허용할 추가 교수 요일 초과량.
    public static readonly int Sc02SlackAbs = 1;

    // SC-03 최적값보다 허용할 추가 블록 간격 위반 과목 수.
    public static readonly int Sc03SlackAbs = 0;

    public static LinearExpr Sc01PenaltyTerm(
        XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms)
    {
        var terms = new List<BoolVar>();
        foreach (var c in courses)
            foreach (var r in rooms)
                foreach (var (d, p) in PenalizedSlots)
                    terms.Add(x[(c.Id, d, p, r.Id)]);
        return LinearExpr.Sum(terms);
    }

    public static LinearExpr Sc02PenaltyTerm(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms)
    {
        IEnumerable<string> PidsOf(Course c)
        {
            if (Sc02ExcludeCoteach)
                return string.IsNullOrEmpty(c.ProfessorId)
                    ? Array.Empty<string>()
                    : new[] { c.ProfessorId };
            return DomainHelpers.CourseProfIds(c);
        }

        var coursesByProf = new Dictionary<string, List<Course>>();
        foreach (var c in courses)
        {
            if (Sc02ExcludeFixed && c.IsFixed) continue;
            foreach (var pid in PidsOf(c))
            {
                if (string.IsNullOrEmpty(pid)) continue;
                if (!coursesByProf.TryGetValue(pid, out var list))
                    coursesByProf[pid] = list = new List<Course>();
                list.Add(c);
            }
        }

        var eligible = coursesByProf.Where(kv => kv.Value.Count >= Sc02MinCourses).ToList();
        if (eligible.Count == 0) return LinearExpr.Constant(0);

        int overCap = Constants.Days - Sc02DayThreshold;
        if (overCap <= 0) return LinearExpr.Constant(0);

        var excessTerms = new List<IntVar>();
        foreach (var (pid, profCourses) in eligible)
        {
            var dayUsed = new List<BoolVar>();
            for (int d = 0; d < Constants.Days; d++)
            {
                var cells = new List<BoolVar>();
                foreach (var c in profCourses)
                    foreach (var p in Constants.ValidPeriods)
                        foreach (var r in rooms)
                            cells.Add(x[(c.Id, d, p, r.Id)]);
                var used = model.NewBoolVar($"sc02_used_{pid}_{d}");
                if (cells.Count > 0)
                    model.AddMaxEquality(used, cells);
                else
                    model.Add(used == 0);
                dayUsed.Add(used);
            }

            var excess = model.NewIntVar(0, overCap, $"sc02_excess_{pid}");
            model.Add(excess >= LinearExpr.Sum(dayUsed) - Sc02DayThreshold);
            excessTerms.Add(excess);
        }

        return excessTerms.Count > 0 ? LinearExpr.Sum(excessTerms) : LinearExpr.Constant(0);
    }

    public static LinearExpr Sc03PenaltyTerm(
        CpModel model, DayVarMap dayVarsByCourse, IReadOnlyList<Course> courses)
    {
        var badIndicators = new List<BoolVar>();
        foreach (var c in courses)
        {
            if (c.BlockStructure.Count < 2) continue;
            if (!dayVarsByCourse.TryGetValue(c.Id, out var dayVars) || dayVars.Count < 2) continue;

            var pairBads = new List<BoolVar>();
            for (int i = 0; i < dayVars.Count - 1; i++)
                for (int j = i + 1; j < dayVars.Count; j++)
                {
                    var diff = model.NewIntVar(
                        -Constants.Days, Constants.Days, $"sc03_diff_{c.Id}_{i}_{j}");
                    var absDiff = model.NewIntVar(
                        0, Constants.Days, $"sc03_absdiff_{c.Id}_{i}_{j}");
                    model.Add(diff == dayVars[i] - dayVars[j]);
                    model.AddAbsEquality(absDiff, diff);

                    var pairBad = model.NewBoolVar($"sc03_pairbad_{c.Id}_{i}_{j}");
                    model.Add(absDiff <= 1).OnlyEnforceIf(pairBad);
                    model.Add(absDiff >= 2).OnlyEnforceIf(pairBad.Not());
                    pairBads.Add(pairBad);
                }

            if (pairBads.Count == 0) continue;

            var courseBad = model.NewBoolVar($"sc03_coursebad_{c.Id}");
            model.AddBoolOr(pairBads).OnlyEnforceIf(courseBad);
            model.AddBoolAnd(pairBads.Select(pb => pb.Not()).ToArray()).OnlyEnforceIf(courseBad.Not());
            badIndicators.Add(courseBad);
        }

        return badIndicators.Count > 0 ? LinearExpr.Sum(badIndicators) : LinearExpr.Constant(0);
    }
}
