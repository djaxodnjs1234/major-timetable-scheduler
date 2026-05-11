namespace TimetableScheduler.Domain;

public static class DomainHelpers
{
    public static string BaseId(string courseId)
    {
        var idx = courseId.LastIndexOf('-');
        return idx >= 0 ? courseId[..idx] : courseId;
    }

    public static HashSet<string> CourseProfIds(Course c)
    {
        var ids = new HashSet<string>();
        if (!string.IsNullOrEmpty(c.ProfessorId))
            ids.Add(c.ProfessorId);
        foreach (var pid in c.CoteachProfs)
            if (!string.IsNullOrEmpty(pid))
                ids.Add(pid);
        return ids;
    }

    public static List<RetakeScenario> DeriveAutoRetakes(
        IEnumerable<Course> courses,
        IEnumerable<int>? grades = null)
    {
        grades ??= new[] { 1, 2, 3, 4 };
        var gradesList = grades.ToList();

        var majorsByGrade = new Dictionary<int, HashSet<string>>();
        foreach (var c in courses)
        {
            if (c.CourseType != "전필") continue;
            if (!majorsByGrade.TryGetValue(c.Grade, out var set))
            {
                set = new HashSet<string>();
                majorsByGrade[c.Grade] = set;
            }
            set.Add(BaseId(c.Id));
        }

        var output = new List<RetakeScenario>();
        foreach (var (gLower, bids) in majorsByGrade)
        {
            foreach (var bid in bids)
            {
                foreach (var gHigher in gradesList)
                {
                    if (gHigher > gLower)
                    {
                        output.Add(new RetakeScenario
                        {
                            CurrentGrade = gHigher,
                            RetakeBaseId = bid,
                        });
                    }
                }
            }
        }
        return output;
    }
}
