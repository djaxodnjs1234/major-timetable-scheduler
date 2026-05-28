using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

internal static class ConstraintHelpers
{
    public static Dictionary<string, List<Course>> GroupByBaseId(IEnumerable<Course> courses)
    {
        var result = new Dictionary<string, List<Course>>();
        foreach (var c in courses)
        {
            var bid = DomainHelpers.BaseId(c.Id);
            if (!result.TryGetValue(bid, out var list))
            {
                list = new List<Course>();
                result[bid] = list;
            }
            list.Add(c);
        }
        return result;
    }
}
