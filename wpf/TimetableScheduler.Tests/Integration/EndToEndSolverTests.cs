using TimetableScheduler.Data;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Integration;

public class EndToEndSolverTests
{
    [Fact]
    public void Xlsx_ToSolver_ProducesAtLeastOneSolution()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);

        var options = new DiverseSolverOptions
        {
            TotalSolutions = 3,
            TimeLimitSec = 30,
            PerSolveTimeSec = 10,
            UseSc01 = false,
            UseSc02 = false,
            UseSc03 = false,
        };

        var result = DiverseSolver.Solve(
            data.Courses, data.Professors, data.Rooms, options);

        Assert.True(result.Solutions.Count >= 1,
            $"Expected ≥1 solution from xlsx data; got 0. Status={result.Status}, attempts={result.Attempts}");
    }

    [Fact]
    public void Xlsx_AllHardConstraintsSatisfied()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);

        var options = new DiverseSolverOptions
        {
            TotalSolutions = 1,
            TimeLimitSec = 30,
            PerSolveTimeSec = 15,
        };

        var result = DiverseSolver.Solve(
            data.Courses, data.Professors, data.Rooms, options);

        Assert.True(result.Solutions.Count >= 1,
            $"No solution produced. Status={result.Status}");

        var sol = result.Solutions[0];
        var courseMap = data.Courses.ToDictionary(c => c.Id);

        // HC-04: total assigned hours == HoursPerWeek * K (K=max(FixedRooms.Count, 1))
        foreach (var (cid, hits) in sol.GroupBy(a => a.CourseId).ToDictionary(g => g.Key, g => g.Count()))
        {
            var c = courseMap[cid];
            int k = Math.Max(c.FixedRooms.Count, 1);
            Assert.Equal(c.HoursPerWeek * k, hits);
        }

        // HC-12: no lunch period
        Assert.DoesNotContain(sol, a => a.Period == Constants.LunchPeriod);

        // HC-01: each (day, period, room) has ≤ 1 course
        var roomSlot = sol.GroupBy(a => (a.Day, a.Period, a.RoomId)).Select(g => g.Count());
        Assert.All(roomSlot, n => Assert.True(n <= 1));
    }

    [Fact]
    public void Xlsx_WithSoftConstraints_ScoreIsValid()
    {
        var path = Path.Combine(TestPaths.FindRepoRoot(), "개설강좌 편람.xlsx");
        var data = XlsxLoader.Load(path);

        var options = new DiverseSolverOptions
        {
            TotalSolutions = 3,
            TimeLimitSec = 60,
            PerSolveTimeSec = 15,
            UseSc01 = true,
            UseSc02 = true,
            UseSc03 = true,
        };

        var result = DiverseSolver.Solve(
            data.Courses, data.Professors, data.Rooms, options);

        Assert.True(result.Solutions.Count >= 1, $"Status={result.Status}");

        var ranked = SolutionScoring.Rank(result.Solutions, data.Courses, data.Professors, topM: 3);
        Assert.NotEmpty(ranked);
        Assert.All(ranked, r =>
        {
            Assert.InRange(r.Score.Sc01, 0, 1);
            Assert.InRange(r.Score.Sc02, 0, 1);
            Assert.InRange(r.Score.Sc03, 0, 1);
            Assert.InRange(r.Score.Total, 0, 3);
        });

        // Best score first
        for (int i = 1; i < ranked.Count; i++)
            Assert.True(ranked[i - 1].Score.Total >= ranked[i].Score.Total);
    }
}
