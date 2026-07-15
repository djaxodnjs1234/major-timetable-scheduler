using Google.OrTools.Sat;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Solver;

public class ModelBuilderSmokeTests
{
    [Fact]
    public void TinyModel_BuildsAndSolves()
    {
        var courses = new List<Course>
        {
            new() { Id = "M1-01", Name = "M1", Grade = 1, HoursPerWeek = 2, ProfessorId = "P1", CourseType = "전필" },
            new() { Id = "M2-01", Name = "M2", Grade = 2, HoursPerWeek = 2, ProfessorId = "P2", CourseType = "전필" },
        };
        var profs = new List<Professor>
        {
            new() { Id = "P1", Name = "Prof1" },
            new() { Id = "P2", Name = "Prof2" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "Room1" },
            new() { Id = "R2", Name = "Room2" },
        };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        var status = solver.Solve(build.Model);

        Assert.True(status is CpSolverStatus.Feasible or CpSolverStatus.Optimal,
            $"Expected feasible/optimal, got {status}");

        // Each course must occupy exactly hours_per_week slots
        foreach (var c in courses)
        {
            int total = 0;
            for (int d = 0; d < Constants.Days; d++)
                foreach (var p in SchedulePolicyRules.CandidateInstructionalPeriods(SchedulePolicy.Default))
                    foreach (var r in rooms)
                        total += (int)solver.Value(build.X[(c.Id, d, p, r.Id)]);
            Assert.Equal(c.HoursPerWeek, total);
        }

        // Lunch period must be empty
        foreach (var c in courses)
            for (int d = 0; d < Constants.Days; d++)
                foreach (var r in rooms)
                    Assert.Equal(0, solver.Value(build.X[(
                        c.Id,
                        d,
                        SchedulePolicyRules.SecondLunchCandidate,
                        r.Id)]));
    }

    [Fact]
    public void ProfDoubleBookingForbidden_HC02()
    {
        // Two courses, same prof, both 1 hour. Must be in different (d,p) slots.
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1" },
            new() { Id = "B-01", Name = "B", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1" },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R" } };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        var status = solver.Solve(build.Model);
        Assert.True(status is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        var slotsA = new HashSet<(int, int)>();
        var slotsB = new HashSet<(int, int)>();
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in SchedulePolicyRules.CandidateInstructionalPeriods(SchedulePolicy.Default))
            {
                if (solver.Value(build.Y[("A-01", d, p)]) == 1) slotsA.Add((d, p));
                if (solver.Value(build.Y[("B-01", d, p)]) == 1) slotsB.Add((d, p));
            }
        Assert.Empty(slotsA.Intersect(slotsB));
    }

    [Fact]
    public void TwoBlockCourse_BlocksOnDistinctDays_HC20()
    {
        // 4 hour course split into blocks of [2, 2] — must land on different days
        var courses = new List<Course>
        {
            new()
            {
                Id = "X-01", Name = "X", Grade = 1, HoursPerWeek = 4,
                BlockStructure = new List<int> { 2, 2 }, ProfessorId = "P1",
            },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R" } };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        var status = solver.Solve(build.Model);
        Assert.True(status is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        var dayVars = build.DayVarsByCourse["X-01"];
        var d1 = solver.Value(dayVars[0]);
        var d2 = solver.Value(dayVars[1]);
        Assert.NotEqual(d1, d2);
    }

    [Fact]
    public void TwoBlockCourse_DistantDaysAllowed()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "X-01", Name = "X", Grade = 1, HoursPerWeek = 5,
                BlockStructure = new List<int> { 2, 3 }, ProfessorId = "P1",
            },
        };
        var profs = new List<Professor>
        {
            new()
            {
                Id = "P1",
                Name = "P",
                UnavailableSlots = SchedulePolicyRules.CandidateInstructionalPeriods(SchedulePolicy.Default)
                    .SelectMany(p => Enumerable.Range(0, Constants.Days).Select(d => new TimeSlot(d, p)))
                    .Where(slot =>
                        !(slot.Day == 0 && slot.Period is 1 or 2) &&
                        !(slot.Day == 4 && slot.Period is 6 or 7 or 8))
                    .ToList(),
            },
        };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R" } };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        var status = solver.Solve(build.Model);
        Assert.True(status is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        var days = build.DayVarsByCourse["X-01"]
            .Select(dayVar => solver.Value(dayVar))
            .OrderBy(day => day)
            .ToList();
        Assert.Equal(new long[] { 0, 4 }, days);
    }

    [Fact]
    public void SameProfessorTwoSection_ThreeHourBlocksRemainFeasible()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "X-01", Name = "X", Grade = 1, Section = 1,
                HoursPerWeek = 5, BlockStructure = new List<int> { 2, 3 },
                ProfessorId = "P1",
            },
            new()
            {
                Id = "X-02", Name = "X", Grade = 1, Section = 2,
                HoursPerWeek = 5, BlockStructure = new List<int> { 2, 3 },
                ProfessorId = "P1",
            },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "R1" },
            new() { Id = "R2", Name = "R2" },
        };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        var status = solver.Solve(build.Model);

        Assert.True(status is CpSolverStatus.Feasible or CpSolverStatus.Optimal, status.ToString());
    }

    [Fact]
    public void Len2BlockStartsAtAnyConsecutiveNonLunchPeriod_HC19()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "X-01", Name = "X", Grade = 1, HoursPerWeek = 2,
                BlockStructure = new List<int> { 2 }, ProfessorId = "P1",
            },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R" } };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        // Find the start period (smallest occupied period)
        int startPeriod = int.MaxValue;
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in SchedulePolicyRules.CandidateInstructionalPeriods(SchedulePolicy.Default))
                if (solver.Value(build.X[("X-01", d, p, "R1")]) == 1 && p < startPeriod)
                    startPeriod = p;
        Assert.Contains(startPeriod, new[] { 1, 2, 3, 6, 7, 8 });
    }
}
