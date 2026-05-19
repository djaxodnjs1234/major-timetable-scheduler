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
