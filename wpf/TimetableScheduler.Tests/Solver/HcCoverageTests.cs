using Google.OrTools.Sat;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Solver;

public class HcCoverageTests
{
    private static (List<Course>, List<Professor>, List<Room>) MakeBaseSetup()
    {
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "X", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1" },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "R1" },
            new() { Id = "R2", Name = "R2" },
        };
        return (courses, profs, rooms);
    }

    [Fact]
    public void HC03_ProfUnavailable_BlocksSlot()
    {
        var (courses, profs, rooms) = MakeBaseSetup();
        // Prof unavailable Mon period 1
        profs[0].UnavailableSlots.Add(new TimeSlot(0, 1));

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        foreach (var r in rooms)
            Assert.Equal(0, solver.Value(build.X[("X-01", 0, 1, r.Id)]));
    }

    [Fact]
    public void HC14_FixedRooms_RestrictsToOnlyTheseRooms()
    {
        var (courses, profs, rooms) = MakeBaseSetup();
        rooms.Add(new Room { Id = "R3", Name = "R3" });
        courses[0].FixedRooms = new List<string> { "R2" };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        // R1, R3 must be 0; R2 should be the assigned room (1 occupancy)
        int r2Count = 0;
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in Constants.ValidPeriods)
            {
                Assert.Equal(0, solver.Value(build.X[("X-01", d, p, "R1")]));
                Assert.Equal(0, solver.Value(build.X[("X-01", d, p, "R3")]));
                r2Count += (int)solver.Value(build.X[("X-01", d, p, "R2")]);
            }
        Assert.Equal(1, r2Count);
    }

    [Fact]
    public void HC14_MultiRoom_K2_OccupiesBothSimultaneously()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "Cap-01", Name = "Cap", Grade = 4, HoursPerWeek = 1,
                FixedRooms = new List<string> { "R1", "R2" }, ProfessorId = "P1",
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
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        // Find the slot. Both R1 and R2 must be 1 in the same (d, p).
        bool found = false;
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in Constants.ValidPeriods)
            {
                var v1 = solver.Value(build.X[("Cap-01", d, p, "R1")]);
                var v2 = solver.Value(build.X[("Cap-01", d, p, "R2")]);
                if (v1 == 1 || v2 == 1)
                {
                    Assert.Equal(1, v1);
                    Assert.Equal(1, v2);
                    Assert.Equal(1, solver.Value(build.Y[("Cap-01", d, p)]));
                    found = true;
                }
            }
        Assert.True(found, "Capstone never assigned");
    }

    [Fact]
    public void HC13_Fixed_PreservesAssignment()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "Y-01", Name = "Y", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1",
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(2, 3) },  // Wed period 3
            },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R1" } };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        // y must be 1 at the fixed slot (room is decided by HC-21/HC-14)
        Assert.Equal(1, solver.Value(build.Y[("Y-01", 2, 3)]));
    }

    [Fact]
    public void HC16_Cross_SyncsSectionsCyclically()
    {
        // Two courses A, B each with 2 sections. Cross => A.s1↔B.s2 same time, A.s2↔B.s1 same time.
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A1", Grade = 1, Section = 1, HoursPerWeek = 1, ProfessorId = "PA" },
            new() { Id = "A-02", Name = "A2", Grade = 1, Section = 2, HoursPerWeek = 1, ProfessorId = "PA" },
            new() { Id = "B-01", Name = "B1", Grade = 1, Section = 1, HoursPerWeek = 1, ProfessorId = "PB" },
            new() { Id = "B-02", Name = "B2", Grade = 1, Section = 2, HoursPerWeek = 1, ProfessorId = "PB" },
        };
        var profs = new List<Professor>
        {
            new() { Id = "PA", Name = "PA" },
            new() { Id = "PB", Name = "PB" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "R1" },
            new() { Id = "R2", Name = "R2" },
        };
        var crosses = new List<CrossGroup>
        {
            new() { Id = "G", BaseIds = new List<string> { "A", "B" } },
        };

        var build = ModelBuilder.Build(courses, profs, rooms, crosses);
        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        // A-01 == B-02 occupancy; A-02 == B-01 occupancy
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in Constants.ValidPeriods)
            {
                Assert.Equal(solver.Value(build.Y[("A-01", d, p)]), solver.Value(build.Y[("B-02", d, p)]));
                Assert.Equal(solver.Value(build.Y[("A-02", d, p)]), solver.Value(build.Y[("B-01", d, p)]));
            }
    }

    [Fact]
    public void HC21_ProfRoomConsistent_GroupsAutoCoursesIntoSameRoom()
    {
        // Prof P1 teaches 2 auto-allocated courses. Both must end up in same room.
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1" },
            new() { Id = "B-01", Name = "B", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1" },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P" } };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "R1" },
            new() { Id = "R2", Name = "R2" },
        };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        // Find which room each course lands in; they must match.
        string? roomA = null, roomB = null;
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in Constants.ValidPeriods)
                foreach (var r in rooms)
                {
                    if (solver.Value(build.X[("A-01", d, p, r.Id)]) == 1) roomA = r.Id;
                    if (solver.Value(build.X[("B-01", d, p, r.Id)]) == 1) roomB = r.Id;
                }
        Assert.NotNull(roomA);
        Assert.NotNull(roomB);
        Assert.Equal(roomA, roomB);
    }
}
