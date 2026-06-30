namespace TimetableScheduler.Domain;

public static class SchoolFixedTimePolicy
{
    public const int AllGrades = 0;

    public static bool IsSchoolFixedCourse(Course course) => course.IsSchoolFixed;

    public static bool IsSchedulableCourse(Course course) => !course.IsSchoolFixed;

    public static bool AppliesToGrade(Course schoolFixedCourse, int grade) =>
        schoolFixedCourse.SchoolFixedTargetGrade == AllGrades ||
        schoolFixedCourse.SchoolFixedTargetGrade == grade;

    public static IReadOnlyList<Course> SchedulableCourses(IEnumerable<Course> courses) =>
        courses.Where(IsSchedulableCourse).ToList();

    public static IReadOnlyList<Course> SchoolFixedCourses(IEnumerable<Course> courses) =>
        courses.Where(IsSchoolFixedCourse).ToList();

    public static string ScopeDisplayName(int targetGrade) =>
        targetGrade == AllGrades ? "전체" : AcademicLevels.DisplayName(targetGrade);
}
