using TimetableScheduler.Domain;

namespace TimetableScheduler.ViewModel.Grid;

public sealed record CellAssignment(
    string CourseId,
    string CourseName,
    string ProfessorId,
    int Grade,
    int Section,
    IReadOnlyList<string> Rooms,
    int RowSpan,
    int HoursPerWeek,
    bool IsFixed)
{
    public string SectionLabel => Section >= 1 ? ((char)('A' + Section - 1)).ToString() : "";

    public string RoomsLabel
    {
        get
        {
            if (Rooms.Count == 0) return "";
            var sorted = Rooms.OrderBy(r => r, StringComparer.Ordinal).ToList();
            if (sorted.Count <= 3) return string.Join(",", sorted);
            return $"{sorted[0]} 외 {sorted.Count - 1}";
        }
    }

    public static CellAssignment FromCourse(
        Course course,
        IEnumerable<string> rooms,
        int rowSpan)
        => new(
            course.Id, course.Name, course.ProfessorId,
            course.Grade, course.Section,
            rooms.ToList(), rowSpan,
            course.HoursPerWeek, course.IsFixed);
}
