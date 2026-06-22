using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.ViewModel;

namespace TimetableScheduler.Tests.ViewModel;

public class WorkspaceServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteRepository _repo;

    public WorkspaceServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"vm_test_{Guid.NewGuid():N}.db");
        _repo = new SqliteRepository(_dbPath);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void Constructor_LoadsExistingData()
    {
        _repo.EnsureCreated();
        _repo.SaveAll(new AppData(
            new List<Course> { new() { Id = "X-01", Name = "n", Grade = 2, HoursPerWeek = 3 } },
            new List<Professor>(), new List<Room>(),
            new List<CrossGroup>(), new List<RetakeScenario>()));

        var ws = new WorkspaceService(_repo);
        Assert.Single(ws.Courses);
        Assert.Equal("X-01", ws.Courses[0].Id);
    }

    [Fact]
    public void AddCourse_PersistsImmediately()
    {
        var ws = new WorkspaceService(_repo);
        ws.AddCourse(new Course { Id = "Y-02", Name = "test", Grade = 3, HoursPerWeek = 2 });

        var fresh = new WorkspaceService(_repo);
        Assert.Single(fresh.Courses);
        Assert.Equal("Y-02", fresh.Courses[0].Id);
    }

    [Fact]
    public void DeleteCourse_PersistsImmediately()
    {
        var ws = new WorkspaceService(_repo);
        var courseA = new Course { Id = "A", Name = "n1", Grade = 1, HoursPerWeek = 1 };
        ws.AddCourse(courseA);
        ws.AddCourse(new Course { Id = "B", Name = "n2", Grade = 1, HoursPerWeek = 1 });
        ws.DeleteCourse(courseA);

        var fresh = new WorkspaceService(_repo);
        Assert.Single(fresh.Courses);
        Assert.Equal("B", fresh.Courses[0].Id);
    }

    [Fact]
    public void UpdateCourse_PersistsImmediately()
    {
        var ws = new WorkspaceService(_repo);
        ws.AddCourse(new Course { Id = "C", Name = "old", Grade = 1, HoursPerWeek = 1 });
        ws.UpdateCourse(new Course { Id = "C", Name = "new", Grade = 2, HoursPerWeek = 3 });

        var fresh = new WorkspaceService(_repo);
        Assert.Equal("new", fresh.Courses[0].Name);
        Assert.Equal(2, fresh.Courses[0].Grade);
    }

    [Fact]
    public void UpdateCourse_DetachesCallerOwnedCourse()
    {
        var ws = new WorkspaceService(_repo);
        ws.AddCourse(new Course
        {
            Id = "C",
            Name = "old",
            Grade = 1,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1 },
        });
        var editorCourse = new Course
        {
            Id = "C",
            Name = "new",
            Grade = 2,
            HoursPerWeek = 3,
            ProfessorId = "P2",
            BlockStructure = new List<int> { 1, 2 },
        };

        ws.UpdateCourse(editorCourse);
        editorCourse.ProfessorId = "";
        editorCourse.BlockStructure.Clear();

        var saved = Assert.Single(ws.Courses);
        Assert.Equal("P2", saved.ProfessorId);
        Assert.Equal(new[] { 1, 2 }, saved.BlockStructure);
        Assert.NotSame(editorCourse, saved);
    }

    [Fact]
    public void AddProfessor_PersistsImmediately()
    {
        var ws = new WorkspaceService(_repo);
        ws.AddProfessor(new Professor { Id = "P1", Name = "교수1" });

        var fresh = new WorkspaceService(_repo);
        Assert.Single(fresh.Professors);
        Assert.Equal("P1", fresh.Professors[0].Id);
    }

    [Fact]
    public void AddRoom_PersistsImmediately()
    {
        var ws = new WorkspaceService(_repo);
        ws.AddRoom(new Room { Id = "R1", Name = "강의실1" });

        var fresh = new WorkspaceService(_repo);
        Assert.Single(fresh.Rooms);
    }

    [Fact]
    public void SectionCountMetadata_DoesNotFanOutExpandedCoursesOrSnapshots()
    {
        var ws = new WorkspaceService(_repo);
        var course = new Course
        {
            Id = "GA1004",
            Name = "자료구조",
            Grade = 2,
            HoursPerWeek = 3,
            Section = 2,
            CourseType = "전필",
            BlockStructure = new List<int> { 3 },
        };
        ws.AddCourse(course);

        var expanded = ws.ExpandedCourses;
        var snapshot = ws.Snapshot();
        var restarted = new WorkspaceService(_repo);

        Assert.Single(ws.Courses);
        Assert.Single(expanded);
        Assert.Single(snapshot.Courses);
        Assert.Single(restarted.Courses);
        Assert.Equal("GA1004", expanded[0].Id);
        Assert.Equal(2, expanded[0].Section);
        Assert.Equal(new[] { 3 }, expanded[0].BlockStructure);
    }

    [Fact]
    public void ExplicitSectionRows_RemainDistinctAcrossSnapshotAndRestart()
    {
        var ws = new WorkspaceService(_repo);
        ws.AddCourse(new Course { Id = "GA1004-01", Name = "자료구조", Grade = 2, HoursPerWeek = 3, Section = 1, BlockStructure = new List<int> { 3 } });
        ws.AddCourse(new Course { Id = "GA1004-02", Name = "자료구조", Grade = 2, HoursPerWeek = 3, Section = 2, BlockStructure = new List<int> { 3 } });

        var expanded = ws.ExpandedCourses;
        var snapshot = ws.Snapshot();
        var restarted = new WorkspaceService(_repo);

        Assert.Equal(2, expanded.Count);
        Assert.Equal(2, snapshot.Courses.Count);
        Assert.Equal(2, restarted.Courses.Count);
        Assert.Equal(new[] { "GA1004-01", "GA1004-02" }, expanded.Select(c => c.Id));
    }

    [Fact]
    public void SchedulingSnapshot_NormalizesSharedSectionValuesWithoutMutatingWorkspace()
    {
        var ws = new WorkspaceService(_repo);
        ws.AddCourse(new Course
        {
            Id = "A-01",
            Name = "A",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            Section = 1,
            FixedRooms = new List<string> { "R1" },
            BlockStructure = new List<int> { 1 },
        });
        ws.AddCourse(new Course
        {
            Id = "A-02",
            Name = "A",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P2",
            Section = 2,
            FixedRooms = new List<string> { "R2" },
            BlockStructure = new List<int> { 1 },
        });

        var raw = ws.Snapshot();
        var scheduling = ws.SchedulingSnapshot();

        Assert.Equal("P2", raw.Courses.Single(course => course.Id == "A-02").ProfessorId);
        Assert.Equal("P2", ws.Courses.Single(course => course.Id == "A-02").ProfessorId);

        var normalizedSecond = scheduling.Courses.Single(course => course.Id == "A-02");
        Assert.Equal("P2", normalizedSecond.ProfessorId);
        Assert.Equal(new[] { "R1" }, normalizedSecond.FixedRooms);
    }

    [Fact]
    public void SaveTimetable_EmbedsWorkspaceSnapshot()
    {
        var ws = new WorkspaceService(_repo);
        ws.AddCourse(new Course { Id = "S-01", Name = "스냅샷과목", Grade = 2, HoursPerWeek = 3 });
        ws.AddProfessor(new Professor { Id = "SP", Name = "스냅샷교수" });

        ws.SaveTimetable("snap", Array.Empty<TimetableScheduler.Solver.SolutionAssignment>());

        var fresh = new WorkspaceService(_repo);
        var saved = fresh.SavedTimetables.Single(t => t.Name == "snap");
        Assert.False(string.IsNullOrEmpty(saved.SnapshotJson));

        var snapshot = System.Text.Json.JsonSerializer.Deserialize<AppData>(saved.SnapshotJson!)!;
        Assert.Equal("S-01", snapshot.Courses.Single().Id);
        Assert.Equal("SP", snapshot.Professors.Single().Id);
    }

    [Fact]
    public void SaveTimetable_WithExistingId_UpdatesTheOriginalRecord()
    {
        var ws = new WorkspaceService(_repo);
        var original = ws.SaveTimetable(
            "before",
            Array.Empty<TimetableScheduler.Solver.SolutionAssignment>());

        var updated = ws.SaveTimetable(
            "after",
            new[] { new TimetableScheduler.Solver.SolutionAssignment("C-01", 0, 1, "R-01") },
            id: original.Id);

        var saved = Assert.Single(ws.SavedTimetables);
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal("after", saved.Name);
        Assert.Single(saved.Assignments);
        Assert.Equal("C-01", saved.Assignments[0].CourseId);
    }

    [Fact]
    public void SaveTimetable_SnapshotIsIndependentOfLaterEdits()
    {
        var ws = new WorkspaceService(_repo);
        ws.AddCourse(new Course { Id = "S-01", Name = "원래이름", Grade = 2, HoursPerWeek = 3 });
        ws.SaveTimetable("snap", Array.Empty<TimetableScheduler.Solver.SolutionAssignment>());

        // Edit the course after saving — snapshot must still hold the original.
        ws.UpdateCourse(new Course { Id = "S-01", Name = "바뀐이름", Grade = 2, HoursPerWeek = 3 });

        var fresh = new WorkspaceService(_repo);
        var saved = fresh.SavedTimetables.Single(t => t.Name == "snap");
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<AppData>(saved.SnapshotJson!)!;
        Assert.Equal("원래이름", snapshot.Courses.Single().Name);
    }

    [Fact]
    public void SaveTimetable_UsesProvidedSnapshotWhenPresent()
    {
        var ws = new WorkspaceService(_repo);
        ws.AddCourse(new Course { Id = "GLOBAL-01", Name = "전역", Grade = 1, HoursPerWeek = 3 });
        var snapshot = new AppData(
            new List<Course> { new() { Id = "SESSION-01", Name = "세션", Grade = 2, HoursPerWeek = 3 } },
            new List<Professor>(), new List<Room>(),
            new List<CrossGroup>(), new List<RetakeScenario>());

        ws.SaveTimetable(
            "session-snap",
            Array.Empty<TimetableScheduler.Solver.SolutionAssignment>(),
            snapshot: snapshot);

        var fresh = new WorkspaceService(_repo);
        var saved = fresh.SavedTimetables.Single(t => t.Name == "session-snap");
        var savedSnapshot = System.Text.Json.JsonSerializer.Deserialize<AppData>(saved.SnapshotJson!)!;
        Assert.Equal("SESSION-01", savedSnapshot.Courses.Single().Id);
    }

    [Fact]
    public void CreateSession_CrudDoesNotTouchMainDb()
    {
        // Seed the main DB via a global workspace.
        var global = new WorkspaceService(_repo);
        global.AddCourse(new Course { Id = "MAIN-01", Name = "메인", Grade = 1, HoursPerWeek = 3 });

        // A session workspace seeded from a snapshot — its CRUD must stay in memory.
        var snapshot = new AppData(
            new List<Course> { new() { Id = "SNAP-01", Name = "스냅", Grade = 2, HoursPerWeek = 3 } },
            new List<Professor>(), new List<Room>(),
            new List<CrossGroup>(), new List<RetakeScenario>());
        var session = WorkspaceService.CreateSession(snapshot);
        Assert.True(session.IsSession);

        session.AddCourse(new Course { Id = "SNAP-02", Name = "추가", Grade = 2, HoursPerWeek = 3 });
        session.DeleteCourse(snapshot.Courses[0]);

        // Main DB still holds only what the global workspace saved.
        var fresh = new WorkspaceService(_repo);
        Assert.Single(fresh.Courses);
        Assert.Equal("MAIN-01", fresh.Courses[0].Id);
    }

    [Fact]
    public void Changed_FiresOnEachMutation()
    {
        var ws = new WorkspaceService(_repo);
        int count = 0;
        ws.Changed += (_, _) => count++;
        ws.AddCourse(new Course { Id = "X", Name = "x", Grade = 1, HoursPerWeek = 1 });
        ws.AddProfessor(new Professor { Id = "P", Name = "p" });
        ws.AddRoom(new Room { Id = "R", Name = "r" });
        Assert.Equal(3, count);
    }
}
