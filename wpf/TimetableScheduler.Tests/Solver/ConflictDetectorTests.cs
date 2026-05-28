using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Solver;

public class ConflictDetectorTests
{
    [Fact]
    public void EmptyAssignment_NoConflicts()
    {
        var conflicts = ConflictDetector.Detect(
            Array.Empty<SolutionAssignment>(),
            Array.Empty<Course>());
        Assert.Empty(conflicts);
    }

    [Fact]
    public void RoomDoubleBooked_DetectsConflict()
    {
        var courses = new List<Course>
        {
            new() { Id = "A", Name = "A" },
            new() { Id = "B", Name = "B" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("A", 0, 1, "R1"),
            new("B", 0, 1, "R1"),
        };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.Single(conflicts);
        Assert.Equal(ConflictType.RoomConflict, conflicts[0].Type);
    }

    [Fact]
    public void SameCourseMultiRoom_NotConflict()
    {
        var courses = new List<Course> { new() { Id = "X", Name = "X" } };
        var assignment = new List<SolutionAssignment>
        {
            new("X", 0, 1, "R1"),
            new("X", 0, 1, "R2"),
        };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void ProfessorDoubleBooked_DetectsConflict()
    {
        var courses = new List<Course>
        {
            new() { Id = "A", Name = "A", ProfessorId = "P1" },
            new() { Id = "B", Name = "B", ProfessorId = "P1" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("A", 0, 1, "R1"),
            new("B", 0, 1, "R2"),
        };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.Contains(conflicts, c => c.Type == ConflictType.ProfessorConflict);
    }

    [Fact]
    public void LunchPeriod_DetectsConflict()
    {
        var courses = new List<Course> { new() { Id = "X", Name = "X" } };
        var assignment = new List<SolutionAssignment> { new("X", 0, 5, "R1") };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.Single(conflicts);
        Assert.Equal(ConflictType.LunchConflict, conflicts[0].Type);
    }

    [Fact]
    public void SameBaseDifferentSection_DetectsConflict()
    {
        var courses = new List<Course>
        {
            new() { Id = "GA1004-01", Name = "X" },
            new() { Id = "GA1004-02", Name = "X" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("GA1004-01", 0, 1, "R1"),
            new("GA1004-02", 0, 1, "R2"),
        };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.Contains(conflicts, c => c.Type == ConflictType.SectionConflict);
    }

    [Fact]
    public void ProfUnavailableSlot_DetectsConflict()
    {
        var courses = new List<Course> { new() { Id = "X", Name = "X", ProfessorId = "P1" } };
        var profs = new List<Professor>
        {
            new() { Id = "P1", Name = "P", UnavailableSlots = new List<TimeSlot> { new(0, 3) } },
        };
        var assignment = new List<SolutionAssignment> { new("X", 0, 3, "R1") };
        var conflicts = ConflictDetector.Detect(assignment, courses, profs);
        Assert.Contains(conflicts, c => c.Type == ConflictType.ProfUnavailable);
    }

    [Fact]
    public void FixedRoomViolation_DetectsConflict()
    {
        var courses = new List<Course>
        {
            new() { Id = "X", Name = "X", FixedRooms = new List<string> { "R1", "R2" } },
        };
        var assignment = new List<SolutionAssignment> { new("X", 0, 1, "R9") };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.Contains(conflicts, c => c.Type == ConflictType.FixedRoomViolation);
    }

    [Fact]
    public void FixedRoom_AllowedRoom_NotConflict()
    {
        var courses = new List<Course>
        {
            new() { Id = "X", Name = "X", FixedRooms = new List<string> { "R1", "R2" } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X", 0, 1, "R1"),
            new("X", 0, 1, "R2"),
        };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.FixedRoomViolation);
    }

    [Fact]
    public void ProfRoomInconsistent_DetectsWarning()
    {
        var courses = new List<Course>
        {
            new() { Id = "A", Name = "A", ProfessorId = "P1" },
            new() { Id = "B", Name = "B", ProfessorId = "P1" },
        };
        // Both courses no FixedRooms, same prof, different rooms — HC-21 violation
        var assignment = new List<SolutionAssignment>
        {
            new("A", 0, 1, "R1"),
            new("B", 1, 1, "R2"),
        };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.Contains(conflicts, c => c.Type == ConflictType.ProfRoomInconsistent);
    }

    [Fact]
    public void FixedCourseMovedOffFixedSlot_DetectsConflict()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "X",
                Name = "X",
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
            },
        };
        var assignment = new List<SolutionAssignment> { new("X", 1, 1, "R1") };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.Contains(conflicts, c => c.Type == ConflictType.FixedTimeViolation);
    }

    [Fact]
    public void Len2BlockWrongStart_DetectsConflict()
    {
        var courses = new List<Course>
        {
            new() { Id = "X", Name = "X", HoursPerWeek = 2, BlockStructure = new List<int> { 2 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X", 0, 2, "R1"),
            new("X", 0, 3, "R1"),
        };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.Contains(conflicts, c => c.Type == ConflictType.BlockStartViolation);
    }

    [Fact]
    public void SameGradeDifferentCourses_DetectsConflict()
    {
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 2 },
            new() { Id = "B-01", Name = "B", Grade = 2 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("A-01", 0, 1, "R1"),
            new("B-01", 0, 1, "R2"),
        };
        var conflicts = ConflictDetector.Detect(assignment, courses);
        Assert.Contains(conflicts, c => c.Type == ConflictType.GradeConflict);
    }
}
