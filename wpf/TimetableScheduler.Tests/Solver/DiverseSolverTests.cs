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
        var progress = new Progress<SolverProgress>(p => reports.Add(p));
        DiverseSolver.Solve(courses, profs, rooms, opts, progress: progress);

        Thread.Sleep(50);  // let Progress<T> drain

        Assert.Contains(reports, r => r.Phase == "1A");
        Assert.Contains(reports, r => r.Phase == "2");
    }

    [Fact]
    public void Cancellation_ThrowsOperationCanceled()
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

        Assert.Throws<OperationCanceledException>(() =>
            DiverseSolver.Solve(courses, profs, rooms, opts, cancellationToken: cts.Token));
    }
}
