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
    public IReadOnlyList<string> CoteachProfIds { get; init; } = Array.Empty<string>();

    public string SectionLabel => Section >= 1 ? ((char)('A' + Section - 1)).ToString() : "";

    public string ProfessorLabel
    {
        get
        {
            var ids = new[] { ProfessorId }
                .Concat(CoteachProfIds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return string.Join(", ", ids);
        }
    }

    public string RoomsLabel
    {
        get
        {
            if (Rooms.Count == 0) return "";
            return string.Join("\n", Rooms.OrderBy(r => r, StringComparer.Ordinal));
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
            course.HoursPerWeek, course.IsFixed)
        {
            CoteachProfIds = course.CoteachProfs.ToList(),
        };
}
