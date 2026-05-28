namespace TimetableScheduler.Domain;

public class Course
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Grade { get; set; }
    public int HoursPerWeek { get; set; }

    public string CourseType { get; set; } = "전필";

    public string ProfessorId { get; set; } = "";
    public int Section { get; set; } = 1;
    public string Department { get; set; } = "";

    public List<string> FixedRooms { get; set; } = new();

    public List<int> BlockStructure { get; set; } = new();

    public bool IsFixed { get; set; }

    public List<TimeSlot> FixedSlots { get; set; } = new();

    public List<string> CoteachProfs { get; set; } = new();
}
