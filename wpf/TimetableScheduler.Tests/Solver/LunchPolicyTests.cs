using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Solver;

public class LunchPolicyTests
{
    private static readonly DiverseSolverOptions OneSolution = new()
    {
        TotalSolutions = 1,
        TimeLimitSec = 10,
        PerSolveTimeSec = 2,
    };

    [Fact]
    public void DerivedTwoHourStarts_FollowEachLunchMode()
    {
        var none = new SchedulePolicy { LunchMode = LunchPolicyMode.None };
        var ban5 = new SchedulePolicy { LunchMode = LunchPolicyMode.BanPeriod5 };
        var ban4 = new SchedulePolicy { LunchMode = LunchPolicyMode.BanPeriod4 };
        var flexible = new SchedulePolicy { LunchMode = LunchPolicyMode.BanOneOfPeriods4And5 };

        Assert.Equal(
            new[] { 1, 3, 5, 7 },
            SchedulePolicyRules.PossibleBlockStarts(
                none,
                SchedulePolicyRules.CandidateDaytimePeriods(none),
                2));
        Assert.Equal(
            new[] { 1, 3, 6, 8 },
            SchedulePolicyRules.PossibleBlockStarts(
                ban5,
                SchedulePolicyRules.CandidateDaytimePeriods(ban5),
                2));
        Assert.Equal(
            new[] { 1, 5, 7 },
            SchedulePolicyRules.PossibleBlockStarts(
                ban4,
                SchedulePolicyRules.CandidateDaytimePeriods(ban4),
                2));
        Assert.Equal(
            new[] { 1, 3, 5, 6, 7, 8 },
            SchedulePolicyRules.PossibleBlockStarts(
                flexible,
                SchedulePolicyRules.CandidateDaytimePeriods(flexible),
                2));
    }

    [Fact]
    public void NoLunchMode_AllowsFixedClassesAtPeriods4And5()
    {
        var courses = new[]
        {
            FixedCourse("A-01", 1, "P1", "R1", day: 0, period: 4),
            FixedCourse("B-01", 2, "P2", "R2", day: 0, period: 5),
        };
        var result = DiverseSolver.Solve(
            courses,
            new[]
            {
                new Professor { Id = "P1", Name = "P1" },
                new Professor { Id = "P2", Name = "P2" },
            },
            new[]
            {
                new Room { Id = "R1", Name = "R1" },
                new Room { Id = "R2", Name = "R2" },
            },
            OneSolution,
            schedulePolicy: new SchedulePolicy { LunchMode = LunchPolicyMode.None });

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE", result.Status);
        var solution = Assert.Single(result.Solutions);
        Assert.Contains(solution, assignment => assignment.Period == 4);
        Assert.Contains(solution, assignment => assignment.Period == 5);
        Assert.Empty(solution.LunchPeriodsByDay);
    }

    [Fact]
    public void BanPeriod4_AllowsFixedClassAtPeriod5_AndReturnsPeriod4Lunch()
    {
        var course = FixedCourse("C-01", grade: 1, professorId: "P1", roomId: "R1", day: 0, period: 5);
        var result = DiverseSolver.Solve(
            new[] { course },
            new[] { new Professor { Id = "P1", Name = "P1" } },
            new[] { new Room { Id = "R1", Name = "R1" } },
            OneSolution,
            schedulePolicy: new SchedulePolicy { LunchMode = LunchPolicyMode.BanPeriod4 });

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE", result.Status);
        var solution = Assert.Single(result.Solutions);
        Assert.Contains(solution, assignment => assignment.Period == 5);
        Assert.All(solution.LunchPeriodsByDay, pair => Assert.Equal(4, pair.Value));
    }

    [Fact]
    public void FlexibleLunch_FixedCandidateSlotsForceTheOtherPeriodPerDay()
    {
        var courses = new[]
        {
            FixedCourse("A-01", 1, "P1", "R1", day: 0, period: 4),
            FixedCourse("B-01", 2, "P2", "R2", day: 1, period: 5),
        };
        var result = DiverseSolver.Solve(
            courses,
            new[]
            {
                new Professor { Id = "P1", Name = "P1" },
                new Professor { Id = "P2", Name = "P2" },
            },
            new[]
            {
                new Room { Id = "R1", Name = "R1" },
                new Room { Id = "R2", Name = "R2" },
            },
            OneSolution,
            schedulePolicy: new SchedulePolicy
            {
                LunchMode = LunchPolicyMode.BanOneOfPeriods4And5,
            });

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE", result.Status);
        var solution = Assert.Single(result.Solutions);
        Assert.Equal(5, solution.LunchPeriodsByDay[0]);
        Assert.Equal(4, solution.LunchPeriodsByDay[1]);
        Assert.Equal(Constants.Days, solution.LunchPeriodsByDay.Count);
        Assert.All(solution.LunchPeriodsByDay.Values, period => Assert.Contains(period, new[] { 4, 5 }));
    }

    [Fact]
    public void FlexibleLunch_TwoHourBlockCannotUseBothCandidatePeriods()
    {
        var course = new Course
        {
            Id = "C-01",
            Name = "C",
            Grade = 1,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        };
        var professor = new Professor
        {
            Id = "P1",
            Name = "P1",
            UnavailableSlots = Constants.Periods
                .SelectMany(period => Enumerable.Range(0, Constants.Days)
                    .Select(day => new TimeSlot(day, period)))
                .Where(slot => slot != new TimeSlot(0, 4) && slot != new TimeSlot(0, 5))
                .ToList(),
        };

        var result = DiverseSolver.Solve(
            new[] { course },
            new[] { professor },
            new[] { new Room { Id = "R1", Name = "R1" } },
            OneSolution,
            schedulePolicy: new SchedulePolicy
            {
                LunchMode = LunchPolicyMode.BanOneOfPeriods4And5,
            });

        Assert.Equal("INFEASIBLE", result.Status);
        Assert.Empty(result.Solutions);
    }

    [Fact]
    public void FlexibleLunch_SchoolFixedCandidateForcesTheOtherLunchPeriod()
    {
        var normal = FixedCourse("C-01", 1, "P1", "R1", day: 2, period: 1);
        var schoolFixed = new Course
        {
            Id = "SF-01",
            Name = "School",
            Grade = 1,
            HoursPerWeek = 1,
            IsFixed = true,
            IsSchoolFixed = true,
            SchoolFixedTargetGrade = SchoolFixedTimePolicy.AllGrades,
            FixedSlots = new List<TimeSlot> { new(0, 4) },
            BlockStructure = new List<int> { 1 },
        };

        var result = DiverseSolver.Solve(
            new[] { normal, schoolFixed },
            new[] { new Professor { Id = "P1", Name = "P1" } },
            new[] { new Room { Id = "R1", Name = "R1" } },
            OneSolution,
            schedulePolicy: new SchedulePolicy
            {
                LunchMode = LunchPolicyMode.BanOneOfPeriods4And5,
            });

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE", result.Status);
        Assert.Equal(5, Assert.Single(result.Solutions).LunchPeriodsByDay[0]);
    }

    private static Course FixedCourse(
        string id,
        int grade,
        string professorId,
        string roomId,
        int day,
        int period) =>
        new()
        {
            Id = id,
            Name = id,
            Grade = grade,
            HoursPerWeek = 1,
            ProfessorId = professorId,
            FixedRooms = new List<string> { roomId },
            IsFixed = true,
            FixedSlots = new List<TimeSlot> { new(day, period) },
            BlockStructure = new List<int> { 1 },
        };
}
