using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Solver;

public class CodexTightGradePackingTests
{
    [Fact]
    public void GenerationErrors_TightGradeWithUnusableSlot_ReportsGe032()
    {
        var courses = Enumerable.Range(1, 20)
            .Select(index => new Course
            {
                Id = $"TIGHT{index:00}-01",
                Name = $"Tight {index:00}",
                Grade = 2,
                HoursPerWeek = 2,
                ProfessorId = $"P{index:00}",
                BlockStructure = new List<int> { 2 },
            })
            .ToList();
        var professors = courses
            .Select(course => new Professor
            {
                Id = course.ProfessorId,
                Name = course.ProfessorId,
                UnavailableSlots = new List<TimeSlot> { new(0, 1) },
            })
            .ToList();
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room 1" } };

        var diagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "GE-027");
        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "GE-032"));
        Assert.Contains("꽉 찼", diagnostic.Message);
        Assert.Contains("Cross를 추가", diagnostic.Message);
        Assert.DoesNotContain("관련 조건", diagnostic.Message);
        Assert.DoesNotContain("Cross를 해제", diagnostic.Message);
    }
}
