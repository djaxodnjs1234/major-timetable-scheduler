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

    public static List<Course> ExpandSections(IEnumerable<Course> courses)
    {
        var result = new List<Course>();
        foreach (var c in courses)
        {
            int n = c.Section;
            // ID에 이미 분반 접미사가 있으면 개별 분반으로 이미 저장된 것 — 그대로 유지
            bool alreadyExpanded = BaseId(c.Id) != c.Id;
            if (n <= 1 || alreadyExpanded)
            {
                result.Add(CloneCourse(c, c.Id, n));
            }
            else
            {
                for (int s = 1; s <= n; s++)
                    result.Add(CloneCourse(c, $"{c.Id}-{s:D2}", s));
            }
        }
        return result;
    }

    private static Course CloneCourse(Course src, string newId, int newSection) => new()
    {
        Id = newId,
        Name = src.Name,
        Grade = src.Grade,
        HoursPerWeek = src.HoursPerWeek,
        CourseType = src.CourseType,
        ProfessorId = src.ProfessorId,
        ExpectedEnrollment = src.ExpectedEnrollment,
        Section = newSection,
        Department = src.Department,
        FixedRooms = new List<string>(src.FixedRooms),
        UnavailableRooms = new List<string>(src.UnavailableRooms),
        BlockStructure = new List<int>(src.BlockStructure),
        IsFixed = src.IsFixed,
        FixedSlots = new List<TimeSlot>(src.FixedSlots),
        IsSchoolFixed = src.IsSchoolFixed,
        SchoolFixedTargetGrade = src.SchoolFixedTargetGrade,
        CoteachProfs = new List<string>(src.CoteachProfs),
    };

    public static List<RetakeScenario> DeriveAutoRetakes(
        IEnumerable<Course> courses,
        IEnumerable<int>? grades = null)
    {
        grades ??= AcademicLevels.UndergraduateGrades;
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
