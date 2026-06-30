using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Solver;

public class TimetableDiagnosticsTests
{
    [Fact]
    public void InputErrors_SchoolFixedCourse_IgnoresProfessorButRequiresFixedSlots()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "SF-01",
                Name = "School",
                Grade = 1,
                HoursPerWeek = 1,
                IsSchoolFixed = true,
                SchoolFixedTargetGrade = SchoolFixedTimePolicy.AllGrades,
                BlockStructure = new List<int> { 1 },
            },
        };

        var diagnostics = TimetableDiagnostics.GetInputErrors(
            courses,
            Array.Empty<Professor>(),
            Array.Empty<Room>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "IE-004");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "IE-010");
    }

    [Fact]
    public void InputErrors_MissingRoomReferences_ReportIe021AndIe022()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "C-01",
                Name = "Course",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                FixedRooms = new List<string> { "R-missing-fixed" },
                UnavailableRooms = new List<string> { "R-missing-unavailable" },
                BlockStructure = new List<int> { 1 },
            },
        };
        var professors = new List<Professor> { new() { Id = "P1", Name = "Professor" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };

        var diagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "IE-021");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "IE-022");
    }

    [Fact]
    public void InputErrors_FixedTimeOutsideAcademicLevelBand_ReportsIe040()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "UG-01",
                Name = "Undergraduate",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 10) },
                BlockStructure = new List<int> { 1 },
            },
            new()
            {
                Id = "GR-01",
                Name = "Graduate",
                Grade = AcademicLevels.GraduateGrade,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
        };

        var diagnostics = TimetableDiagnostics.GetInputErrors(
            courses,
            new List<Professor> { new() { Id = "P1", Name = "Professor" } },
            new List<Room> { new() { Id = "R1", Name = "Room" } });

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "IE-040"));
    }

    [Fact]
    public void InputErrors_IncludeAffectedManagementLocations()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "A-01",
                Name = "Alpha",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
            new()
            {
                Id = "B-01",
                Name = "Beta",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
        };
        var professors = new List<Professor> { new() { Id = "P1", Name = "Professor" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "" } };
        var crosses = new List<CrossGroup> { new() { Id = "X001", BaseIds = new List<string> { "A", "Missing" } } };

        var diagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms, crosses);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "IE-003" && diagnostic.Message.Contains("강의실 관리 > R1"));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "IE-016" &&
            diagnostic.Message.Contains("교과목 관리 > Alpha(A-01) / Beta(B-01)") &&
            diagnostic.Message.Contains("월 1교시"));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "IE-017" &&
            diagnostic.Message.Contains("교수 관리 > P1") &&
            diagnostic.Message.Contains("월 1교시"));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "IE-033" && diagnostic.Message.Contains("Cross 설정 > X001 (A, Missing)"));
    }

    [Fact]
    public void GenerationErrors_FixedRoomAndProfessorConflict_ReportGe007AndGe008()
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
                FixedRooms = new List<string> { "R1" },
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
            new()
            {
                Id = "B-01",
                Name = "B",
                Grade = 2,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                FixedRooms = new List<string> { "R1" },
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
        };
        var professors = new List<Professor> { new() { Id = "P1", Name = "Professor" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };

        var diagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GE-007");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GE-008");
    }

    [Fact]
    public void FixedTimeWithoutFixedRooms_WhenRoomCapacityIsInsufficient_ReportsSpecificDiagnostics()
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
                Id = "B-01",
                Name = "B",
                Grade = 2,
                HoursPerWeek = 1,
                ProfessorId = "P2",
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "P1" },
            new() { Id = "P2", Name = "P2" },
        };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };

        var inputDiagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms);
        var generationDiagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms);

        Assert.Contains(inputDiagnostics, diagnostic => diagnostic.Id == "IE-014");
        Assert.Contains(generationDiagnostics, diagnostic => diagnostic.Id == "GE-007");
    }

    [Fact]
    public void FixedTimeWithoutFixedRooms_WhenAnotherRoomIsAvailable_DoesNotReportRoomConflict()
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
                Id = "B-01",
                Name = "B",
                Grade = 2,
                HoursPerWeek = 1,
                ProfessorId = "P2",
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 1) },
                BlockStructure = new List<int> { 1 },
            },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "P1" },
            new() { Id = "P2", Name = "P2" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "Room1" },
            new() { Id = "R2", Name = "Room2" },
        };

        var inputDiagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms);
        var generationDiagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms);

        Assert.DoesNotContain(inputDiagnostics, diagnostic => diagnostic.Id == "IE-014");
        Assert.DoesNotContain(generationDiagnostics, diagnostic => diagnostic.Id == "GE-007");
    }

    [Fact]
    public void GenerationErrors_RetakeFixedRequiredConflict_ReportsGe021()
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
                BlockStructure = new List<int> { 1 },
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
                BlockStructure = new List<int> { 1 },
            },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "P1" },
            new() { Id = "P2", Name = "P2" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "R1" },
            new() { Id = "R2", Name = "R2" },
        };

        var diagnostics = TimetableDiagnostics.GetGenerationErrors(
            courses,
            professors,
            rooms,
            considerRetakeStudents: true);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GE-021");
    }

    [Fact]
    public void CrossBlockStructureMismatch_ReportsSpecificDiagnostics()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "A-01",
                Name = "A",
                Grade = 1,
                HoursPerWeek = 3,
                ProfessorId = "P1",
                BlockStructure = new List<int> { 3 },
            },
            new()
            {
                Id = "B-01",
                Name = "B",
                Grade = 1,
                HoursPerWeek = 3,
                ProfessorId = "P2",
                BlockStructure = new List<int> { 2, 1 },
            },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "P1" },
            new() { Id = "P2", Name = "P2" },
        };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };
        var crosses = new List<CrossGroup>
        {
            new() { Id = "X001", BaseIds = new List<string> { "A", "B" } },
        };

        var inputDiagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms, crosses);
        var generationDiagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms, crosses);

        Assert.Contains(inputDiagnostics, diagnostic => diagnostic.Id == "IE-035");
        Assert.Contains(generationDiagnostics, diagnostic => diagnostic.Id == "GE-026");
        Assert.Contains(generationDiagnostics, diagnostic =>
            diagnostic.Id == "GE-026" && diagnostic.Message.Contains("블록구조가 다릅니다"));
    }

    [Fact]
    public void CrossSharedFixedRoom_ReportsSpecificDiagnostics()
    {
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1", Section = 1, FixedRooms = new List<string> { "R1" }, BlockStructure = new List<int> { 1 } },
            new() { Id = "A-02", Name = "A", Grade = 1, HoursPerWeek = 1, ProfessorId = "P1", Section = 2, FixedRooms = new List<string> { "R1" }, BlockStructure = new List<int> { 1 } },
            new() { Id = "B-01", Name = "B", Grade = 1, HoursPerWeek = 1, ProfessorId = "P2", Section = 1, FixedRooms = new List<string> { "R1" }, BlockStructure = new List<int> { 1 } },
            new() { Id = "B-02", Name = "B", Grade = 1, HoursPerWeek = 1, ProfessorId = "P2", Section = 2, FixedRooms = new List<string> { "R1" }, BlockStructure = new List<int> { 1 } },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "P1" },
            new() { Id = "P2", Name = "P2" },
        };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };
        var crosses = new List<CrossGroup>
        {
            new() { Id = "X001", BaseIds = new List<string> { "A", "B" } },
        };

        var inputDiagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms, crosses);
        var generationDiagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms, crosses);

        Assert.Contains(inputDiagnostics, diagnostic => diagnostic.Id == "IE-039");
        Assert.Contains(generationDiagnostics, diagnostic => diagnostic.Id == "GE-028");
    }

    [Fact]
    public void SameProfessorTwoHourSectionsWithoutAdjacentSlots_ReportSpecificDiagnostics()
    {
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "A", Grade = 1, HoursPerWeek = 2, ProfessorId = "P1", Section = 1, BlockStructure = new List<int> { 2 } },
            new() { Id = "A-02", Name = "A", Grade = 1, HoursPerWeek = 2, ProfessorId = "P1", Section = 2, BlockStructure = new List<int> { 2 } },
        };
        var unavailableSlots = Constants.DaytimePeriods
            .SelectMany(period => Enumerable.Range(0, Constants.Days).Select(day => new TimeSlot(day, period)))
            .Where(slot =>
                !(slot.Day == 0 && slot.Period is 1 or 2) &&
                !(slot.Day == 1 && slot.Period is 1 or 2))
            .ToList();
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "P1", UnavailableSlots = unavailableSlots },
        };
        var rooms = Rooms(1);

        var solverResult = DiverseSolver.Solve(
            courses,
            professors,
            rooms,
            new DiverseSolverOptions { TotalSolutions = 1, TimeLimitSec = 5, PerSolveTimeSec = 1 });
        var inputDiagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms);
        var generationDiagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms);

        Assert.Equal("INFEASIBLE", solverResult.Status);
        Assert.DoesNotContain(inputDiagnostics, diagnostic => diagnostic.Id == "IE-024");
        Assert.Contains(inputDiagnostics, diagnostic =>
            diagnostic.Id == "IE-041" && diagnostic.Message.Contains("인접"));
        Assert.Contains(generationDiagnostics, diagnostic =>
            diagnostic.Id == "GE-030" && diagnostic.Message.Contains("인접"));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 2 }, false)]
    [InlineData(new[] { 2, 3 }, false)]
    [InlineData(new[] { 5 }, true)]
    public void InputErrors_FiveHourBlockStructures_AllowOnlyConfiguredOptions(int[] blocks, bool expectError)
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "C-01",
                Name = "Five Hour Course",
                Grade = 1,
                HoursPerWeek = 5,
                ProfessorId = "P1",
                BlockStructure = blocks.ToList(),
            },
        };
        var professors = new List<Professor> { new() { Id = "P1", Name = "Professor" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };

        var diagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms);

        if (expectError)
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "IE-008");
        else
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "IE-008");
    }

    [Fact]
    public void InputErrors_GraduateFiveHourCourse_IsRejected()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "GR-01",
                Name = "Graduate Five",
                Grade = AcademicLevels.GraduateGrade,
                HoursPerWeek = 5,
                ProfessorId = "P1",
                BlockStructure = new List<int> { 2, 3 },
            },
        };
        var professors = new List<Professor> { new() { Id = "P1", Name = "Professor" } };
        var rooms = new List<Room> { new() { Id = "R1", Name = "Room" } };

        var inputDiagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms);
        var generationDiagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms);

        Assert.Contains(inputDiagnostics, diagnostic => diagnostic.Id == "IE-042");
        Assert.Contains(generationDiagnostics, diagnostic => diagnostic.Id == "GE-033");
    }

    [Fact]
    public void GradeSlotCapacityOverflow_ReportsSpecificDiagnostics()
    {
        var courses = Enumerable.Range(1, 41)
            .Select(index => OneHourCourse($"G2C{index:00}-01", $"P{index:00}"))
            .ToList();
        var professors = ProfessorsFor(courses);
        var rooms = Rooms(2);

        var inputDiagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms);
        var generationDiagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms);

        var inputDiagnostic = Assert.Single(inputDiagnostics.Where(diagnostic => diagnostic.Id == "IE-038"));
        var generationDiagnostic = Assert.Single(generationDiagnostics.Where(diagnostic => diagnostic.Id == "GE-027"));
        Assert.Contains("2학년", inputDiagnostic.Message);
        Assert.Contains("40칸", inputDiagnostic.Message);
        Assert.Contains("41칸", inputDiagnostic.Message);
        Assert.Contains("교과목 관리 > 2학년", inputDiagnostic.Message);
        Assert.Contains("2학년", generationDiagnostic.Message);
    }

    [Fact]
    public void GradeSlotCapacityOverflow_CountsSameCourseSections()
    {
        var courses = Enumerable.Range(1, 20)
            .SelectMany(baseIndex => Enumerable.Range(1, 2)
                .Select(sectionIndex =>
                {
                    var course = OneHourCourse($"G2SEC{baseIndex:00}-{sectionIndex:00}", $"P{baseIndex:00}{sectionIndex:00}");
                    course.Section = sectionIndex;
                    return course;
                }))
            .Append(OneHourCourse("G2OVER-01", "P999"))
            .ToList();
        var professors = ProfessorsFor(courses);
        var rooms = Rooms(2);

        var inputDiagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms);
        var generationDiagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms);

        var inputDiagnostic = Assert.Single(inputDiagnostics.Where(diagnostic => diagnostic.Id == "IE-038"));
        var generationDiagnostic = Assert.Single(generationDiagnostics.Where(diagnostic => diagnostic.Id == "GE-027"));
        Assert.Contains("41", inputDiagnostic.Message);
        Assert.Contains("41", generationDiagnostic.Message);
    }

    [Fact]
    public void GradeSlotCapacityOverflow_CountsSameGradeCrossOverlap()
    {
        var courses = new List<Course>
        {
            OneHourCourse("A-01", "P001"),
            OneHourCourse("B-01", "P002"),
        };
        courses.AddRange(Enumerable.Range(1, 39)
            .Select(index => OneHourCourse($"G2C{index:00}-01", $"P{index + 2:000}")));
        var professors = ProfessorsFor(courses);
        var rooms = Rooms(2);
        var crosses = new List<CrossGroup>
        {
            new() { Id = "X001", BaseIds = new List<string> { "A", "B" } },
        };

        var inputDiagnostics = TimetableDiagnostics.GetInputErrors(courses, professors, rooms, crosses);
        var generationDiagnostics = TimetableDiagnostics.GetGenerationErrors(courses, professors, rooms, crosses);

        Assert.DoesNotContain(inputDiagnostics, diagnostic => diagnostic.Id == "IE-038");
        Assert.DoesNotContain(generationDiagnostics, diagnostic => diagnostic.Id == "GE-027");
    }

    [Fact]
    public void GenerationErrors_SameProfessorSectionsWithThreeHourBlocks_DoNotReportGe030()
    {
        var courses = new List<Course>
        {
            new()
            {
                Id = "JAVA-01",
                Name = "Java",
                Grade = 1,
                HoursPerWeek = 5,
                ProfessorId = "P1",
                Section = 1,
                BlockStructure = new List<int> { 2, 3 },
            },
            new()
            {
                Id = "JAVA-02",
                Name = "Java",
                Grade = 1,
                HoursPerWeek = 5,
                ProfessorId = "P1",
                Section = 2,
                BlockStructure = new List<int> { 2, 3 },
            },
        };

        var diagnostics = TimetableDiagnostics.GetGenerationErrors(
            courses,
            new List<Professor> { new() { Id = "P1", Name = "Professor" } },
            Rooms(1));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "GE-030");
    }

    [Fact]
    public void GenerationErrors_SixGraduateThreeHourCourses_AllowDaytimeOverflow()
    {
        var courses = Enumerable.Range(1, 6)
            .Select(index => new Course
            {
                Id = $"GR{index:00}",
                Name = $"Graduate {index}",
                Grade = AcademicLevels.GraduateGrade,
                HoursPerWeek = 3,
                ProfessorId = $"P{index:00}",
                BlockStructure = new List<int> { 3 },
            })
            .ToList();

        var diagnostics = TimetableDiagnostics.GetGenerationErrors(
            courses,
            ProfessorsFor(courses),
            Rooms(1));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "GE-031");
        Assert.True(AcademicLevelTimePolicy.AllowsGraduateDaytimeOverflow(courses));
        Assert.Equal(3, AcademicLevelTimePolicy.GraduateDaytimeOverflowSlots(courses));
    }

    private static Course OneHourCourse(string id, string professorId) => new()
    {
        Id = id,
        Name = id,
        Grade = 2,
        HoursPerWeek = 1,
        ProfessorId = professorId,
        BlockStructure = new List<int> { 1 },
    };

    private static List<Professor> ProfessorsFor(IEnumerable<Course> courses) =>
        courses
            .Select(course => course.ProfessorId)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new Professor { Id = id, Name = id })
            .ToList();

    private static List<Room> Rooms(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new Room { Id = $"R{index}", Name = $"Room {index}" })
            .ToList();
}
