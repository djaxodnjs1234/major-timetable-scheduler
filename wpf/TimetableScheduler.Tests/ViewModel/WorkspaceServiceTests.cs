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
    public void ImportFromXlsx_ExpandedSections_RoundtripIsConsistent()
    {
        // Regression test for: ImportFromXlsx stored unexpanded (Section=N) but
        // Persist() saved expanded rows, so restart showed more rows than the session.
        //
        // New invariant: Courses always holds individually expanded sections,
        // so count-after-import == count-after-restart.
        var ws = new WorkspaceService(_repo);

        // Simulate what ImportFromXlsx now does: expand before adding to Courses.
        var xlsxResult = new List<Course>
        {
            new() { Id = "GA1004", Name = "자료구조", Grade = 2,
                    HoursPerWeek = 4, Section = 2, CourseType = "전필",
                    BlockStructure = new List<int> { 2, 2 } },
        };
        foreach (var c in DomainHelpers.ExpandSections(xlsxResult))
            ws.AddCourse(c);

        int countDuringSession = ws.Courses.Count;

        // Simulate app restart by creating a new WorkspaceService from the same DB.
        var ws2 = new WorkspaceService(_repo);
        int countAfterRestart = ws2.Courses.Count;

        Assert.Equal(2, countDuringSession);
        Assert.Equal(countDuringSession, countAfterRestart);
        Assert.Equal("GA1004-01", ws2.Courses[0].Id);
        Assert.Equal("GA1004-02", ws2.Courses[1].Id);
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
