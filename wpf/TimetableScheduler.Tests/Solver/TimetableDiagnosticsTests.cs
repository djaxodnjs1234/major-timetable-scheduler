using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Solver;

public class TimetableDiagnosticsTests
{
    [Fact]
    public void InputErrors_MissingRoomReferences_ReportIe021AndIe022()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "C-01",
                Name = "Course",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                FixedRooms = new List<string> { "R-missing-fixed" },
                UnavailableRooms = new List<string> { "R-missing-unavailable" },
                BlockStructure = new List<int> { 1 },
            },
        };
        var professors = new List<Professor> { new() { Id = "P1", Name = "Professor" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };

        var diagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "IE-021");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "IE-022");
    }

    [Fact]
    public void GenerationErrors_FixedRoomAndProfessorConflict_ReportGe007AndGe008()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "A-01",
                Name = "A",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                FixedRooms = new List<string> { "R1" },
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
            new()
            {
                Id = "B-01",
                Name = "B",
                Grade = 2,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                FixedRooms = new List<string> { "R1" },
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
        };
        var professors = new List<Professor> { new() { Id = "P1", Name = "Professor" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };

        var diagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GE-007");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GE-008");
    }

    [Fact]
    public void GenerationErrors_RetakeFixedRequiredConflict_ReportsGe021()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "M1-01",
                Name = "Lower Required",
                Grade = 1,
                HoursPerWeek = 1,
                CourseType = "전필",
                ProfessorId = "P1",
                FixedRooms = new List<string> { "R1" },
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
            new()
            {
                Id = "M2-01",
                Name = "Current Required",
                Grade = 2,
                HoursPerWeek = 1,
                CourseType = "전필",
                ProfessorId = "P2",
                FixedRooms = new List<string> { "R2" },
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "P1" },
            new() { Id = "P2", Name = "P2" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "R1" },
            new() { Id = "R2", Name = "R2" },
        };

        var diagnostics = TimetableDiagnostics.GetGenerationErrors(
            courses,
            professors,
            rooms,
            considerRetakeStudents: true);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GE-021");
    }
}
