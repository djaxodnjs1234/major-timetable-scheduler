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
    public string AssignmentId { get; init; } = "";
    public string CourseKey { get; init; } = "";
    public bool ShowSectionLabel { get; init; } = true;
    public IReadOnlyList<string> CoteachProfIds { get; init; } = Array.Empty<string>();
    public string CourseType { get; init; } = "";
    public string ProfessorDisplayName { get; init; } = "";
    public IReadOnlyList<string> CoteachProfDisplayNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RoomDisplayNames { get; init; } = Array.Empty<string>();
    public bool IsSchoolFixed { get; init; }
    public int SchoolFixedTargetGrade { get; init; }

    public string ProfessorLabel => string.IsNullOrWhiteSpace(ProfessorDisplayName)
        ? ProfessorId
        : ProfessorDisplayName;

    public IReadOnlyList<string> CoteachProfLabels => CoteachProfDisplayNames.Count == 0
        ? CoteachProfIds
        : CoteachProfDisplayNames;

    public string SectionLabel => ShowSectionLabel && Section >= 1 ? ((char)('A' + Section - 1)).ToString() : "";

    public string TitleLabel
    {
        get
        {
            if (IsSchoolFixed)
                return $"{SchoolFixedTitlePrefix}{CourseName}";

            var title = string.IsNullOrEmpty(SectionLabel)
                ? CourseName
                : $"{CourseName}·{SectionLabel}";
            return IsFixed ? $"★ {title}" : title;
        }
    }

    private string SchoolFixedTitlePrefix => SchoolFixedTargetGrade == SchoolFixedTimePolicy.AllGrades
        ? "[학교고정] "
        : "[학년고정] ";

    public string ProfessorLine
    {
        get
        {
            if (IsSchoolFixed)
                return "";

            var labels = new List<string>();
            if (!string.IsNullOrWhiteSpace(ProfessorLabel))
                labels.Add(ProfessorLabel);
            for (var i = 0; i < CoteachProfLabels.Count; i++)
            {
                var label = CoteachProfLabels[i];
                if (string.IsNullOrWhiteSpace(label)) continue;
                var id = i < CoteachProfIds.Count ? CoteachProfIds[i] : "";
                if (string.Equals(id, ProfessorId, StringComparison.Ordinal)) continue;
                if (string.Equals(label, ProfessorLabel, StringComparison.Ordinal)) continue;
                labels.Add(label);
            }
            return string.Join(", ", labels);
        }
    }

    public string RoomsLabel
    {
        get
        {
            if (IsSchoolFixed)
                return "";

            var labels = RoomDisplayNames.Count == 0 ? Rooms : RoomDisplayNames;
            if (labels.Count == 0) return "";
            return string.Join("\n", labels.OrderBy(r => r, StringComparer.Ordinal));
        }
    }

    public static CellAssignment FromCourse(
        Course course,
        IEnumerable<string> rooms,
        int rowSpan,
        IReadOnlyDictionary<string, string>? professorNames = null,
        IReadOnlyDictionary<string, string>? roomNames = null,
        string assignmentId = "",
        string courseKey = "",
        bool showSectionLabel = true)
    {
        var roomList = rooms.ToList();
        return new(
            course.Id, course.Name, course.ProfessorId,
            course.Grade, course.Section,
            roomList, rowSpan,
            course.HoursPerWeek, course.IsFixed)
        {
            AssignmentId = assignmentId,
            CourseKey = courseKey,
            ShowSectionLabel = showSectionLabel,
            CourseType = course.CourseType,
            IsSchoolFixed = course.IsSchoolFixed,
            SchoolFixedTargetGrade = course.SchoolFixedTargetGrade,
            CoteachProfIds = course.CoteachProfs.ToList(),
            ProfessorDisplayName = DisplayName(professorNames, course.ProfessorId),
            CoteachProfDisplayNames = course.CoteachProfs.Select(id => DisplayName(professorNames, id)).ToList(),
            RoomDisplayNames = roomList.Select(id => DisplayName(roomNames, id)).ToList(),
        };

        static string DisplayName(IReadOnlyDictionary<string, string>? names, string id) =>
            !string.IsNullOrWhiteSpace(id)
            && names != null
            && names.TryGetValue(id, out var name)
            && !string.IsNullOrWhiteSpace(name)
                ? name
                : id;
    }
}
