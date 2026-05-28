namespace TimetableScheduler.Solver;

public sealed record RunInfo(
    IReadOnlyDictionary<(string CourseId, int Day, int Period), int> RunLen,
    IReadOnlySet<(string CourseId, int Day, int Period)> Inside);

public static class TimetableRuns
{
    public static RunInfo ComputeRuns(IReadOnlyList<SolutionAssignment> assignments)
    {
        var byCourseDay = new Dictionary<(string Cid, int Day), HashSet<int>>();
        foreach (var a in assignments)
        {
            var key = (a.CourseId, a.Day);
            if (!byCourseDay.TryGetValue(key, out var ps))
                byCourseDay[key] = ps = new HashSet<int>();
            ps.Add(a.Period);
        }

        var runLen = new Dictionary<(string, int, int), int>();
        var inside = new HashSet<(string, int, int)>();

        foreach (var ((cid, d), pset) in byCourseDay)
        {
            var ps = pset.OrderBy(p => p).ToList();
            int i = 0;
            while (i < ps.Count)
            {
                int j = i;
                while (j + 1 < ps.Count && ps[j + 1] == ps[j] + 1) j++;
                runLen[(cid, d, ps[i])] = j - i + 1;
                for (int k = i + 1; k <= j; k++)
                    inside.Add((cid, d, ps[k]));
                i = j + 1;
            }
        }

        return new RunInfo(runLen, inside);
    }
}
