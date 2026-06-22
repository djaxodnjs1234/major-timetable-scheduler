using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Scoring;

public sealed record SolutionScore(double Sc01, double Sc02, double Sc03, double Total);

public sealed record RankedSolution(
    IReadOnlyList<SolutionAssignment> Assignment,
    SolutionScore Score);

public static class SolutionScoring
{
    public static readonly int Sc01RawDenominator = 20;

    public static readonly IReadOnlyDictionary<string, double> ScWeights =
        new Dictionary<string, double> { ["SC01"] = 1, ["SC02"] = 1, ["SC03"] = 1 };

    public static readonly IReadOnlyDictionary<string, double> ScPenaltyPower =
        new Dictionary<string, double> { ["SC01"] = 1, ["SC02"] = 1, ["SC03"] = 1 };

    public static double Sc01SpecialSlots(IReadOnlyList<SolutionAssignment> assignment)
    {
        if (assignment.Count == 0) return 1.0;
        int bad = assignment.Count(a => SoftConstraints.PenalizedSlots.Contains((a.Day, a.Period)));
        return Math.Max(0.0, 1.0 - (double)bad / Sc01RawDenominator);
    }

    public static double Sc02ProfConcentration(
        IReadOnlyList<SolutionAssignment> assignment,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Course> courses)
    {
        if (professors.Count == 0) return 1.0;

        IEnumerable<string> PidsOf(Course c)
        {
            if (SoftConstraints.Sc02ExcludeCoteach)
                return string.IsNullOrEmpty(c.ProfessorId)
                    ? Array.Empty<string>()
                    : new[] { c.ProfessorId };
            return DomainHelpers.CourseProfIds(c);
        }

        var coursesByProf = new Dictionary<string, List<Course>>();
        foreach (var c in courses)
        {
            if (SoftConstraints.Sc02ExcludeFixed && c.IsFixed) continue;
            foreach (var pid in PidsOf(c))
            {
                if (string.IsNullOrEmpty(pid)) continue;
                if (!coursesByProf.TryGetValue(pid, out var list))
                    coursesByProf[pid] = list = new List<Course>();
                list.Add(c);
            }
        }

        var eligible = coursesByProf
            .Where(kv => kv.Value.Count >= SoftConstraints.Sc02MinCourses)
            .Select(kv => kv.Key)
            .ToHashSet();
        if (eligible.Count == 0) return 1.0;

        var courseMap = courses.ToDictionary(c => c.Id);
        var daysByProf = new Dictionary<string, HashSet<int>>();
        foreach (var a in assignment)
        {
            if (!courseMap.TryGetValue(a.CourseId, out var c)) continue;
            if (SoftConstraints.Sc02ExcludeFixed && c.IsFixed) continue;
            foreach (var pid in PidsOf(c))
            {
                if (!eligible.Contains(pid)) continue;
                if (!daysByProf.TryGetValue(pid, out var set))
                    daysByProf[pid] = set = new HashSet<int>();
                set.Add(a.Day);
            }
        }

        int overCap = Constants.Days - SoftConstraints.Sc02DayThreshold;
        if (overCap <= 0) return 1.0;

        int totalExcess = daysByProf.Values
            .Sum(ds => Math.Max(0, ds.Count - SoftConstraints.Sc02DayThreshold));
        int maxPossible = eligible.Count * overCap;
        return 1.0 - (double)totalExcess / Math.Max(1, maxPossible);
    }

    public static double Sc03MinGap(
        IReadOnlyList<SolutionAssignment> assignment,
        IReadOnlyList<Course> courses)
    {
        var multi = courses.Where(c => c.BlockStructure.Count >= 2).ToList();
        if (multi.Count == 0) return 1.0;

        var daysByCid = new Dictionary<string, HashSet<int>>();
        foreach (var a in assignment)
        {
            if (!daysByCid.TryGetValue(a.CourseId, out var set))
                daysByCid[a.CourseId] = set = new HashSet<int>();
            set.Add(a.Day);
        }

        double totalScore = 0.0;
        foreach (var c in multi)
        {
            if (!daysByCid.TryGetValue(c.Id, out var set) || set.Count < 2) continue;
            var ds = set.OrderBy(d => d).ToList();
            var pairScores = new List<double>();
            for (int i = 0; i < ds.Count - 1; i++)
                for (int j = i + 1; j < ds.Count; j++)
                    pairScores.Add(Sc03GapScore(ds[j] - ds[i]));
            if (pairScores.Count > 0)
                totalScore += pairScores.Average();
        }
        return totalScore / multi.Count;
    }

    private static double Sc03GapScore(int gap) => gap switch
    {
        2 => 1.0,
        1 => 0.8,
        3 or 4 => 0.2,
        _ => 0.0,
    };

    public static SolutionScore Score(
        IReadOnlyList<SolutionAssignment> assignment,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors)
    {
        double sc01 = Sc01SpecialSlots(assignment);
        double sc02 = Sc02ProfConcentration(assignment, professors, courses);
        double sc03 = Sc03MinGap(assignment, courses);
        double total =
            ScWeights["SC01"] * Math.Pow(sc01, ScPenaltyPower["SC01"])
            + ScWeights["SC02"] * Math.Pow(sc02, ScPenaltyPower["SC02"])
            + ScWeights["SC03"] * Math.Pow(sc03, ScPenaltyPower["SC03"]);
        return new SolutionScore(sc01, sc02, sc03, total);
    }

    public static IReadOnlyList<RankedSolution> Rank(
        IReadOnlyList<IReadOnlyList<SolutionAssignment>> solutions,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        int topM = 10)
    {
        return solutions
            .Select(a => new RankedSolution(a, Score(a, courses, professors)))
            .OrderByDescending(r => r.Score.Total)
            .Take(topM)
            .ToList();
    }
}
