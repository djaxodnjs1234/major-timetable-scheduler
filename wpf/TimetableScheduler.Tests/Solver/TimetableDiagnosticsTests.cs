using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Solver;

public class TimetableDiagnosticsTests
{
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
    public void GenerationErrors_SameProfessorSectionsWithThreeHourBlocks_ReportGe030()
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

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "GE-030" && diagnostic.Message.Contains("3시간 블록"));
    }

    [Fact]
    public void GenerationErrors_SixGraduateThreeHourCourses_ReportGe031()
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

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "GE-031"));
        Assert.Contains("3시간 연속 수업 블록이 6개", diagnostic.Message);
        Assert.Contains("최대 5개", diagnostic.Message);
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
