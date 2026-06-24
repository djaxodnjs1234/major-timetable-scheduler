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
        profs[0].AllowedRooms = new List<string> { "R1" };

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
    public void HC23_RestrictsGraduateAndUndergraduateToTheirOwnTimeBands()
    {
        var courses = new List<Course>
        {
            new() { Id = "UG-01", Name = "Undergraduate", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1" },
            new() { Id = "GR-01", Name = "Graduate", Grade = AcademicLevels.GraduateGrade, HoursPerWeek = 1, ProfessorId = "P2" },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "P1" },
            new() { Id = "P2", Name = "P2" },
        };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R1" } };

        var build = ModelBuilder.Build(courses, professors, rooms);
        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        for (var day = 0; day < Constants.Days; day++)
        {
            foreach (var period in Constants.NightPeriods)
                Assert.Equal(0, solver.Value(build.X[("UG-01", day, period, "R1")]));
            foreach (var period in Constants.DaytimePeriods)
                Assert.Equal(0, solver.Value(build.X[("GR-01", day, period, "R1")]));
        }

        Assert.Contains(Constants.DaytimePeriods, period =>
            Enumerable.Range(0, Constants.Days).Any(day => solver.Value(build.Y[("UG-01", day, period)]) == 1));
        Assert.Contains(Constants.NightPeriods, period =>
            Enumerable.Range(0, Constants.Days).Any(day => solver.Value(build.Y[("GR-01", day, period)]) == 1));
    }

    [Fact]
    public void HC14_UnavailableRooms_BlocksThoseRooms()
    {
        var (courses, profs, rooms) = MakeBaseSetup();
        courses[0].UnavailableRooms = new List<string> { "R1" };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        var r1Count = 0;
        var r2Count = 0;
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in Constants.ValidPeriods)
            {
                r1Count += (int)solver.Value(build.X[("X-01", d, p, "R1")]);
                r2Count += (int)solver.Value(build.X[("X-01", d, p, "R2")]);
            }
        Assert.Equal(0, r1Count);
        Assert.Equal(1, r2Count);
    }

    [Fact]
    public void HC21_ProfessorUnavailableAllRooms_IsInfeasible()
    {
        var (courses, profs, rooms) = MakeBaseSetup();
        profs[0].UnavailableRooms = rooms.Select(r => r.Id).ToList();

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();

        Assert.Equal(CpSolverStatus.Infeasible, solver.Solve(build.Model));
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
    public void CrossGroup_WithUnknownBaseId_ThrowsManagedValidationError()
    {
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 1, Section = 1, HoursPerWeek = 1, ProfessorId = "PA" },
            new() { Id = "B-01", Name = "B", Grade = 1, Section = 1, HoursPerWeek = 1, ProfessorId = "PB" },
        };
        var profs = new List<Professor>
        {
            new() { Id = "PA", Name = "PA" },
            new() { Id = "PB", Name = "PB" },
        };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R1" } };
        var crosses = new List<CrossGroup>
        {
            new() { Id = "manual-assignment-id-looking-row", BaseIds = new List<string> { "A", "missing-assignment-id" } },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ModelBuilder.Build(courses, profs, rooms, crosses));
        Assert.Contains("unknown course base id", ex.Message);
        Assert.Contains("missing-assignment-id", ex.Message);
    }

    [Fact]
    public void CrossGroup_WithDuplicateBaseId_ThrowsManagedValidationError()
    {
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 1, Section = 1, HoursPerWeek = 1, ProfessorId = "PA" },
        };
        var profs = new List<Professor> { new() { Id = "PA", Name = "PA" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "R1" } };
        var crosses = new List<CrossGroup>
        {
            new() { Id = "dup", BaseIds = new List<string> { "A", "A" } },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ModelBuilder.Build(courses, profs, rooms, crosses));
        Assert.Contains("duplicate course id", ex.Message);
    }

    [Fact]
    public void CrossGroup_WithEmptyBaseId_ThrowsManagedValidationError()
    {
        var (courses, profs, rooms) = MakeBaseSetup();
        var crosses = new List<CrossGroup>
        {
            new() { Id = "empty", BaseIds = new List<string> { "X", "" } },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ModelBuilder.Build(courses, profs, rooms, crosses));
        Assert.Contains("empty course id", ex.Message);
    }

    [Fact]
    public void HC21_PerCourseRoomEligibility_AllowsDifferentRoomsForSameProfessor()
    {
        // Each course's unavailable room is local to that course, not all of P1's courses.
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1", UnavailableRooms = new List<string> { "R1" } },
            new() { Id = "B-01", Name = "B", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1", UnavailableRooms = new List<string> { "R2" } },
        };
        var profs = new List<Professor> { new() { Id = "P1", Name = "P", AllowedRooms = new List<string> { "R1", "R2" } } };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "R1" },
            new() { Id = "R2", Name = "R2" },
        };

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);

        // A can only use R2 and B can only use R1.
        string? roomA = null, roomB = null;
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in Constants.ValidPeriods)
                foreach (var r in rooms)
                {
                    if (solver.Value(build.X[("A-01", d, p, r.Id)]) == 1) roomA = r.Id;
                    if (solver.Value(build.X[("B-01", d, p, r.Id)]) == 1) roomB = r.Id;
        }
        Assert.Equal("R2", roomA);
        Assert.Equal("R1", roomB);
    }

    [Fact]
    public void HC22_AutoSectionsOfSameCourse_MustUseOneSharedRoom()
    {
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1" },
            new() { Id = "A-02", Name = "A", Grade = 1, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 },
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

        var build = ModelBuilder.Build(courses, profs, rooms);
        build.Model.Add(build.X[("A-01", 0, 1, "R1")] == 1);
        build.Model.Add(build.X[("A-02", 1, 1, "R2")] == 1);

        var solver = new CpSolver();
        Assert.Equal(CpSolverStatus.Infeasible, solver.Solve(build.Model));
    }

    [Fact]
    public void HC15_ThreeHourBlocks_DoNotRequireSameDayAdjacency()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "GR-01",
                Name = "Graduate",
                Grade = AcademicLevels.GraduateGrade,
                Section = 1,
                HoursPerWeek = 3,
                ProfessorId = "P1",
                BlockStructure = new List<int> { 3 },
            },
            new()
            {
                Id = "GR-02",
                Name = "Graduate",
                Grade = AcademicLevels.GraduateGrade,
                Section = 2,
                HoursPerWeek = 3,
                ProfessorId = "P1",
                BlockStructure = new List<int> { 3 },
            },
        };
        var professors = new List<Professor> { new() { Id = "P1", Name = "Professor" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };
        var build = ModelBuilder.Build(courses, professors, rooms);

        build.Model.Add(build.StartVarsByBlock[("GR-01", 0)][(0, 10)] == 1);
        build.Model.Add(build.StartVarsByBlock[("GR-02", 0)][(1, 10)] == 1);

        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);
    }

    [Fact]
    public void HC22_ExplicitFixedRooms_KeepCourseRoomPriority()
    {
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1", FixedRooms = new List<string> { "R1" } },
            new() { Id = "A-02", Name = "A", Grade = 1, HoursPerWeek = 1, ProfessorId = "P2", Section = 2, FixedRooms = new List<string> { "R2" } },
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

        var build = ModelBuilder.Build(courses, profs, rooms);
        var solver = new CpSolver();
        Assert.True(solver.Solve(build.Model) is CpSolverStatus.Feasible or CpSolverStatus.Optimal);
    }
}
