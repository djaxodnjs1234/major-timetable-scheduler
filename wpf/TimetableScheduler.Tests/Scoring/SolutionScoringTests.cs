using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Scoring;

public class SolutionScoringTests
{
    [Fact]
    public void Sc01_AllNonPenalizedSlots_ReturnsOne()
    {
        // Tuesday period 3 — not in PenalizedSlots
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 1, 3, "R1"),
        };
        Assert.Equal(1.0, SolutionScoring.Sc01SpecialSlots(assignment), 6);
    }

    [Fact]
    public void Sc01_AllPenalizedSlots_ReducesScore()
    {
        // 5 assignments, all in Mon AM (penalized)
        var assignment = Enumerable.Range(1, 5)
            .Select(p => new SolutionAssignment($"X-{p}", 0, p, "R1")).ToList();
        // bad=4 (only 4 slots in Mon are AM penalized: p=1,2,3,4; p=5 is lunch but here we say p=1..5)
        // Actually p=5 is Mon lunch, not in PenalizedSlots; so bad=4.
        double expected = 1.0 - 4.0 / 20;
        Assert.Equal(expected, SolutionScoring.Sc01SpecialSlots(assignment), 6);
    }

    [Fact]
    public void Sc01_EmptyAssignment_ReturnsOne()
    {
        Assert.Equal(1.0, SolutionScoring.Sc01SpecialSlots(new List<SolutionAssignment>()), 6);
    }

    [Fact]
    public void Sc03_SingleBlockCourse_NotCounted_ReturnsOne()
    {
        var courses = new List<Course>
        {
            new() { Id = "X-01", BlockStructure = new List<int> { 2 } },  // 1 block only
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
        };
        Assert.Equal(1.0, SolutionScoring.Sc03MinGap(assignment, courses), 6);
    }

    [Fact]
    public void Sc03_MultiBlockCourse_GapTwo_Good()
    {
        var courses = new List<Course>
        {
            new() { Id = "X-01", BlockStructure = new List<int> { 1, 1 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),  // Mon
            new("X-01", 2, 1, "R1"),  // Wed (gap = 2)
        };
        Assert.Equal(1.0, SolutionScoring.Sc03MinGap(assignment, courses), 6);
    }

    [Fact]
    public void Sc03_MultiBlockCourse_GapOne_High()
    {
        var courses = new List<Course>
        {
            new() { Id = "X-01", BlockStructure = new List<int> { 1, 1 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),  // Mon
            new("X-01", 1, 1, "R1"),  // Tue (gap = 1)
        };
        Assert.Equal(0.8, SolutionScoring.Sc03MinGap(assignment, courses), 6);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Sc03_MultiBlockCourse_GapThreeOrFour_VeryLow(int day)
    {
        var courses = new List<Course>
        {
            new() { Id = "X-01", BlockStructure = new List<int> { 1, 1 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", day, 1, "R1"),
        };
        Assert.Equal(0.2, SolutionScoring.Sc03MinGap(assignment, courses), 6);
    }

    [Fact]
    public void Sc03_ThreeBlocks_UsesAveragePairScore()
    {
        var courses = new List<Course>
        {
            new() { Id = "X-01", BlockStructure = new List<int> { 1, 1, 1 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 1, 1, "R1"),
            new("X-01", 3, 1, "R1"),
        };
        Assert.Equal((0.8 + 0.2 + 1.0) / 3.0, SolutionScoring.Sc03MinGap(assignment, courses), 6);
    }

    [Fact]
    public void Sc04_AboveTwoFirstPeriodClasses_ReducesScore()
    {
        var courses = new List<Course>
        {
            new() { Id = "A-01", ProfessorId = "P1" },
            new() { Id = "B-01", ProfessorId = "P1" },
            new() { Id = "C-01", ProfessorId = "P1" },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };
        var assignment = new List<SolutionAssignment>
        {
            new("A-01", 0, SoftConstraints.Sc04FirstPeriod, "R1"),
            new("B-01", 1, SoftConstraints.Sc04FirstPeriod, "R1"),
            new("C-01", 2, SoftConstraints.Sc04FirstPeriod, "R1"),
        };

        var expected = 1.0 - 1.0 / (Constants.Days - SoftConstraints.Sc04FirstPeriodThreshold);
        Assert.Equal(expected, SolutionScoring.Sc04FirstPeriodLimit(assignment, courses, profs), 6);
    }

    [Fact]
    public void Sc04_FixedCourses_AreIgnored()
    {
        var courses = new List<Course>
        {
            new() { Id = "A-01", ProfessorId = "P1" },
            new() { Id = "B-01", ProfessorId = "P1" },
            new() { Id = "F-01", ProfessorId = "P1", IsFixed = true },
            new() { Id = "F-02", ProfessorId = "P1", IsFixed = true },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };
        var assignment = new List<SolutionAssignment>
        {
            new("A-01", 0, SoftConstraints.Sc04FirstPeriod, "R1"),
            new("B-01", 1, SoftConstraints.Sc04FirstPeriod, "R1"),
            new("F-01", 2, SoftConstraints.Sc04FirstPeriod, "R1"),
            new("F-02", 3, SoftConstraints.Sc04FirstPeriod, "R1"),
        };

        Assert.Equal(1.0, SolutionScoring.Sc04FirstPeriodLimit(assignment, courses, profs), 6);
    }

    [Fact]
    public void Score_AllPerfect_TotalIsFour()
    {
        var courses = new List<Course>
        {
            new() { Id = "X-01", ProfessorId = "P1" },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };
        var assignment = new List<SolutionAssignment> { new("X-01", 1, 3, "R1") };

        var score = SolutionScoring.Score(assignment, courses, profs);
        Assert.Equal(1.0, score.Sc01, 6);
        Assert.Equal(1.0, score.Sc02, 6);
        Assert.Equal(1.0, score.Sc03, 6);
        Assert.Equal(1.0, score.Sc04, 6);
        Assert.Equal(4.0, score.Total, 6);
    }

    [Fact]
    public void Rank_OrdersByTotalDescending_TakesTopM()
    {
        var courses = new List<Course>
        {
            new() { Id = "X-01", ProfessorId = "P1" },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };

        // Three solutions with varying SC-01 penalty
        var solClean = new List<SolutionAssignment> { new("X-01", 1, 3, "R1") };
        var solOneBad = new List<SolutionAssignment> { new("X-01", 0, 1, "R1") };
        var solManyBad = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"), new("X-01", 0, 2, "R1"),
            new("X-01", 0, 3, "R1"),
        };

        var ranked = SolutionScoring.Rank(
            new[] { solManyBad, solClean, solOneBad },
            courses, profs, topM: 2);

        Assert.Equal(2, ranked.Count);
        Assert.Same(solClean, ranked[0].Assignment);  // best score
        Assert.Same(solOneBad, ranked[1].Assignment);
    }
}
