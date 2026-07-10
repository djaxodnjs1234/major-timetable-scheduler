using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Solver;

public class DiverseSolverTests
{
    private static (List<Course>, List<Professor>, List<Room>) MakeSetup()
    {
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1" },
            new() { Id = "B-01", Name = "B", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" },
        };
        var profs = new List<Professor>
        {
            new() { Id = "P1", Name = "P1" },
            new() { Id = "P2", Name = "P2" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "R1" },
            new() { Id = "R2", Name = "R2" },
        };
        return (courses, profs, rooms);
    }

    [Fact]
    public void NoScObjectives_CollectsAtLeastOneSolution()
    {
        var (courses, profs, rooms) = MakeSetup();
        var opts = new DiverseSolverOptions
        {
            TotalSolutions = 5,
            TimeLimitSec = 30,
            PerSolveTimeSec = 2,
        };
        var result = DiverseSolver.Solve(courses, profs, rooms, opts);

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE");
        Assert.True(result.Solutions.Count >= 1);
        Assert.Null(result.Sc01Bound);
        Assert.Null(result.Sc02Bound);
        Assert.Null(result.Sc03Bound);

        // each solution must have exactly hours_per_week assignments per course
        foreach (var sol in result.Solutions)
            foreach (var c in courses)
                Assert.Equal(c.HoursPerWeek, sol.Count(a => a.CourseId == c.Id));
    }

    [Fact]
    public void Sc01Enabled_ProducesBoundAndPenalizedSlotsRespected()
    {
        var (courses, profs, rooms) = MakeSetup();
        var opts = new DiverseSolverOptions
        {
            UseSc01 = true,
            TotalSolutions = 3,
            TimeLimitSec = 30,
            PerSolveTimeSec = 2,
        };
        var result = DiverseSolver.Solve(courses, profs, rooms, opts);

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE");
        Assert.NotNull(result.Sc01Bound);
        Assert.True(result.Solutions.Count >= 1);

        foreach (var sol in result.Solutions)
        {
            int penalty = sol.Count(a =>
                SoftConstraints.PenalizedSlots.Contains((a.Day, a.Period)));
            Assert.True(penalty <= result.Sc01Bound.Value,
                $"penalty {penalty} exceeds bound {result.Sc01Bound}");
        }
    }

    [Fact]
    public void Sc01AndSc03Enabled_GeneratesDistantTwoPlusThreeBlockCourse()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "X-01",
                Name = "X",
                Grade = 1,
                HoursPerWeek = 5,
                ProfessorId = "P1",
                BlockStructure = new List<int> { 2, 3 },
            },
        };
        var profs = new List<Professor>
        {
            new()
            {
                Id = "P1",
                Name = "P1",
                UnavailableSlots = SchedulePolicyRules.CandidateInstructionalPeriods(SchedulePolicy.Default)
                    .SelectMany(p => Enumerable.Range(0, Constants.Days).Select(d => new TimeSlot(d, p)))
                    .Where(slot =>
                        !(slot.Day == 0 && slot.Period is 1 or 2) &&
                        !(slot.Day == 4 && slot.Period is 6 or 7 or 8))
                    .ToList(),
            },
        };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R1" } };
        var opts = new DiverseSolverOptions
        {
            UseSc01 = true,
            UseSc03 = true,
            TotalSolutions = 1,
            TimeLimitSec = 30,
            PerSolveTimeSec = 2,
        };

        var result = DiverseSolver.Solve(courses, profs, rooms, opts);

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE", result.Status);
        var solution = Assert.Single(result.Solutions);
        Assert.Equal(
            new[] { (0, 1), (0, 2), (4, 6), (4, 7), (4, 8) },
            solution.Select(a => (a.Day, a.Period))
                .OrderBy(slot => slot.Day)
                .ThenBy(slot => slot.Period));
    }

    [Fact]
    public void UnlimitedPerSolutionTime_WithTotalLimit_StillCollectsSolutions()
    {
        var (courses, profs, rooms) = MakeSetup();
        var opts = new DiverseSolverOptions
        {
            TotalSolutions = 1,
            TimeLimitSec = 120,
            PerSolveTimeSec = 0,
        };

        var result = DiverseSolver.Solve(courses, profs, rooms, opts);

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE");
        Assert.Single(result.Solutions);
    }

    [Fact]
    public void ConsiderRetakeStudents_Disabled_IgnoresRetakeConflicts()
    {
        var (courses, profs, rooms) = MakeFixedRetakeConflictSetup();
        var retakes = new List<RetakeScenario>
        {
            new() { CurrentGrade = 2, RetakeBaseId = "M1" },
        };
        var opts = new DiverseSolverOptions
        {
            TotalSolutions = 1,
            TimeLimitSec = 10,
            PerSolveTimeSec = 1,
            ConsiderRetakeStudents = false,
        };

        var result = DiverseSolver.Solve(courses, profs, rooms, opts, retakes: retakes);

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE");
        Assert.Single(result.Solutions);
    }

    [Fact]
    public void ConsiderRetakeStudents_Enabled_DerivesRetakesAndBlocksRequiredMajorOverlap()
    {
        var (courses, profs, rooms) = MakeFixedRetakeConflictSetup();
        var opts = new DiverseSolverOptions
        {
            TotalSolutions = 1,
            TimeLimitSec = 10,
            PerSolveTimeSec = 1,
            ConsiderRetakeStudents = true,
        };

        var result = DiverseSolver.Solve(courses, profs, rooms, opts);

        Assert.Equal("INFEASIBLE", result.Status);
        Assert.Empty(result.Solutions);
    }

    [Fact]
    public void ProgressReportsReceivedFromBothPhases()
    {
        var (courses, profs, rooms) = MakeSetup();
        var reports = new List<SolverProgress>();
        var opts = new DiverseSolverOptions
        {
            UseSc01 = true,
            TotalSolutions = 2,
            TimeLimitSec = 20,
            PerSolveTimeSec = 2,
        };
        var progress = new SyncProgress<SolverProgress>(p => reports.Add(p));
        DiverseSolver.Solve(courses, profs, rooms, opts, progress: progress);

        Assert.Contains(reports, r => r.Phase == "1A");
        Assert.Contains(reports, r => r.Phase == "2");
    }

    [Fact]
    public void SchoolFixedGlobalSlot_BlocksNonFixedCourses()
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
                BlockStructure = new List<int> { 1 },
            },
            new()
            {
                Id = "SF-01",
                Name = "School",
                Grade = 1,
                HoursPerWeek = 1,
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
                IsSchoolFixed = true,
                SchoolFixedTargetGrade = SchoolFixedTimePolicy.AllGrades,
            },
        };
        var profs = new List<Professor>
        {
            new()
            {
                Id = "P1",
                Name = "P1",
                UnavailableSlots = SchedulePolicyRules.CandidateInstructionalPeriods(SchedulePolicy.Default)
                    .SelectMany(period => Enumerable.Range(0, Constants.Days).Select(day => new TimeSlot(day, period)))
                    .Where(slot => slot != new TimeSlot(0, 1) && slot != new TimeSlot(0, 2))
                    .ToList(),
            },
        };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R1" } };

        var result = DiverseSolver.Solve(courses, profs, rooms, new DiverseSolverOptions
        {
            TotalSolutions = 1,
            TimeLimitSec = 10,
            PerSolveTimeSec = 1,
        });

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE", result.Status);
        var assignment = Assert.Single(Assert.Single(result.Solutions));
        Assert.Equal("A-01", assignment.CourseId);
        Assert.Equal(new TimeSlot(0, 2), new TimeSlot(assignment.Day, assignment.Period));
    }

    [Fact]
    public void SchoolFixedTargetGrade_DoesNotBlockOtherGrades()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "G1-01",
                Name = "G1",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                BlockStructure = new List<int> { 1 },
            },
            new()
            {
                Id = "G2-01",
                Name = "G2",
                Grade = 2,
                HoursPerWeek = 1,
                ProfessorId = "P2",
                BlockStructure = new List<int> { 1 },
            },
            new()
            {
                Id = "SF-01",
                Name = "School",
                Grade = 1,
                HoursPerWeek = 1,
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
                IsSchoolFixed = true,
                SchoolFixedTargetGrade = 1,
            },
        };
        var allSlots = SchedulePolicyRules.CandidateInstructionalPeriods(SchedulePolicy.Default)
            .SelectMany(period => Enumerable.Range(0, Constants.Days).Select(day => new TimeSlot(day, period)))
            .ToList();
        var profs = new List<Professor>
        {
            new()
            {
                Id = "P1",
                Name = "P1",
                UnavailableSlots = allSlots.Where(slot => slot != new TimeSlot(0, 2)).ToList(),
            },
            new()
            {
                Id = "P2",
                Name = "P2",
                UnavailableSlots = allSlots.Where(slot => slot != new TimeSlot(0, 1)).ToList(),
            },
        };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R1" } };

        var result = DiverseSolver.Solve(courses, profs, rooms, new DiverseSolverOptions
        {
            TotalSolutions = 1,
            TimeLimitSec = 10,
            PerSolveTimeSec = 1,
        });

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE", result.Status);
        var solution = Assert.Single(result.Solutions);
        Assert.Contains(solution, assignment => assignment.CourseId == "G1-01" && assignment.Day == 0 && assignment.Period == 2);
        Assert.Contains(solution, assignment => assignment.CourseId == "G2-01" && assignment.Day == 0 && assignment.Period == 1);
    }

    [Fact]
    public void SchoolFixedSlot_AllowsUserFixedCourseOverride()
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
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
            new()
            {
                Id = "SF-01",
                Name = "School",
                Grade = 1,
                HoursPerWeek = 1,
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
                IsSchoolFixed = true,
                SchoolFixedTargetGrade = SchoolFixedTimePolicy.AllGrades,
            },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P1" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R1" } };

        var result = DiverseSolver.Solve(courses, profs, rooms, new DiverseSolverOptions
        {
            TotalSolutions = 1,
            TimeLimitSec = 10,
            PerSolveTimeSec = 1,
        });

        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE", result.Status);
        var assignment = Assert.Single(Assert.Single(result.Solutions));
        Assert.Equal("A-01", assignment.CourseId);
        Assert.Equal(new TimeSlot(0, 1), new TimeSlot(assignment.Day, assignment.Period));
    }

    private class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public SyncProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }

    private static (List<Course>, List<Professor>, List<Room>) MakeFixedRetakeConflictSetup()
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
            },
        };
        var profs = new List<Professor>
        {
            new() { Id = "P1", Name = "P1" },
            new() { Id = "P2", Name = "P2" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "R1" },
            new() { Id = "R2", Name = "R2" },
        };
        return (courses, profs, rooms);
    }

    [Fact]
    public void Cancellation_ReturnsCancelledResult()
    {
        var (courses, profs, rooms) = MakeSetup();
        var opts = new DiverseSolverOptions
        {
            TotalSolutions = 1000,
            TimeLimitSec = 60,
            PerSolveTimeSec = 1,
        };
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var result = DiverseSolver.Solve(courses, profs, rooms, opts, cancellationToken: cts.Token);

        Assert.Equal("CANCELLED", result.Status);
        Assert.Empty(result.Solutions);
    }
}
