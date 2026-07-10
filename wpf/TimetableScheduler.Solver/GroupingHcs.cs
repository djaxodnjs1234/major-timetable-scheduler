using Google.OrTools.Sat;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

using YDict = Dictionary<(string CourseId, int Day, int Period), BoolVar>;

public static class GroupingHcs
{
    public static void AddHc16_Cross(
        CpModel model, YDict y, IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses)
    {
        if (crosses == null || crosses.Count == 0) return;

        var baseGroups = ConstraintHelpers.GroupByBaseId(courses);
        foreach (var key in baseGroups.Keys.ToList())
            baseGroups[key] = baseGroups[key].OrderBy(c => c.Section).ToList();

        foreach (var cross in crosses)
        {
            var bids = cross.BaseIds.ToList();
            if (bids.Count < 2) continue;

            var groups = bids.Select(b =>
                baseGroups.TryGetValue(b, out var g) ? g : new List<Course>()).ToList();
            if (groups.Any(g => g.Count == 0)) continue;
            int K = groups.Min(g => g.Count);
            if (K < 1) continue;

            for (int i = 0; i < bids.Count - 1; i++)
            {
                var g1 = groups[i];
                var g2 = groups[i + 1];
                for (int k = 0; k < K; k++)
                {
                    var c1 = g1[k];
                    var c2 = g2[(k + 1) % K];
                    for (int d = 0; d < Constants.Days; d++)
                        foreach (var p in Constants.Periods)
                            model.Add(y[(c1.Id, d, p)] == y[(c2.Id, d, p)]);
                }
            }
        }
    }

    public static void AddHc17_Retake(
        CpModel model, YDict y, IReadOnlyList<Course> courses,
        IReadOnlyList<RetakeScenario>? retakes)
    {
        if (retakes == null || retakes.Count == 0) return;

        var baseGroups = ConstraintHelpers.GroupByBaseId(courses);

        foreach (var sc in retakes)
        {
            int G = sc.CurrentGrade;
            string R = sc.RetakeBaseId;
            if (string.IsNullOrWhiteSpace(R))
                throw new InvalidOperationException(
                    $"Invalid retake scenario: CurrentGrade={G}, RetakeBaseId is empty.");

            if (!baseGroups.TryGetValue(R, out var sections) || sections.Count == 0)
                throw new InvalidOperationException(
                    $"Invalid retake scenario: CurrentGrade={G}, RetakeBaseId='{R}' does not match any course base id.");

            sections = sections
                .GroupBy(c => c.Id, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(c => c.Section)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .ToList();

            var majors = courses.Where(c =>
                c.Grade == G && c.CourseType == "전필"
                && DomainHelpers.BaseId(c.Id) != R).ToList();
            if (majors.Count == 0)
                continue;

            var safeVars = new List<BoolVar>();
            foreach (var s in sections)
            {
                var conflictVars = new List<BoolVar>();
                foreach (var m in majors)
                    for (int d = 0; d < Constants.Days; d++)
                        foreach (var p in Constants.Periods)
                        {
                            var sUses = y[(s.Id, d, p)];
                            var mUses = y[(m.Id, d, p)];
                            var cf = model.NewBoolVar(
                                $"retake_cf_{R}_{s.Id}_{m.Id}_{d}_{p}");
                            model.Add(sUses + mUses - 1 <= cf);
                            model.Add(cf <= sUses);
                            model.Add(cf <= mUses);
                            conflictVars.Add(cf);
                        }
                var anyCf = model.NewBoolVar($"retake_any_{R}_{s.Id}");
                if (conflictVars.Count > 0)
                    model.AddMaxEquality(anyCf, conflictVars);
                else
                    model.Add(anyCf == 0);
                var safe = model.NewBoolVar($"retake_safe_{R}_{s.Id}");
                model.Add(safe + anyCf == 1);
                safeVars.Add(safe);
            }

            model.Add(LinearExpr.Sum(safeVars) >= 1);
        }
    }
}
