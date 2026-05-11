using TimetableScheduler.Data;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Tests.Data;

public class SqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tt_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void EnsureCreated_OnFreshFile_CreatesTables()
    {
        var repo = new SqliteRepository(_dbPath);
        repo.EnsureCreated();
        var data = repo.LoadAll();
        Assert.Empty(data.Courses);
        Assert.Empty(data.Professors);
        Assert.Empty(data.Rooms);
        Assert.Empty(data.CrossGroups);
        Assert.Empty(data.RetakeScenarios);
    }

    [Fact]
    public void SaveAll_LoadAll_RoundTrips()
    {
        var repo = new SqliteRepository(_dbPath);
        repo.EnsureCreated();

        var input = new AppData(
            Courses: new List<Course>
            {
                new()
                {
                    Id = "X-01",
                    Name = "테스트",
                    Grade = 2,
                    HoursPerWeek = 3,
                    CourseType = "전필",
                    ProfessorId = "P1",
                    Section = 1,
                    Department = "전자",
                    FixedRooms = new List<string> { "R1", "R2" },
                    BlockStructure = new List<int> { 2, 1 },
                    IsFixed = true,
                    FixedSlots = new List<TimeSlot> { new(0, 1), new(2, 3) },
                    CoteachProfs = new List<string> { "P2" },
                },
            },
            Professors: new List<Professor>
            {
                new()
                {
                    Id = "P1", Name = "교수1",
                    UnavailableSlots = new List<TimeSlot> { new(4, 7) },
                    AllowedRooms = new List<string> { "R1" },
                },
            },
            Rooms: new List<Room> { new() { Id = "R1", Name = "강의실1" } },
            CrossGroups: new List<CrossGroup>
            {
                new() { Id = "G1", BaseIds = new List<string> { "B1", "B2" } },
            },
            RetakeScenarios: new List<RetakeScenario>
            {
                new() { CurrentGrade = 3, RetakeBaseId = "B1" },
            });

        repo.SaveAll(input);
        var loaded = repo.LoadAll();

        Assert.Single(loaded.Courses);
        var c = loaded.Courses[0];
        Assert.Equal("X-01", c.Id);
        Assert.Equal(new[] { "R1", "R2" }, c.FixedRooms);
        Assert.Equal(new[] { 2, 1 }, c.BlockStructure);
        Assert.True(c.IsFixed);
        Assert.Equal(2, c.FixedSlots.Count);
        Assert.Equal(new TimeSlot(0, 1), c.FixedSlots[0]);
        Assert.Equal(new[] { "P2" }, c.CoteachProfs);

        Assert.Single(loaded.Professors);
        var p = loaded.Professors[0];
        Assert.Equal("P1", p.Id);
        Assert.Equal(new TimeSlot(4, 7), p.UnavailableSlots[0]);
        Assert.Equal(new[] { "R1" }, p.AllowedRooms);

        Assert.Single(loaded.Rooms);
        Assert.Equal("R1", loaded.Rooms[0].Id);

        Assert.Single(loaded.CrossGroups);
        Assert.Equal(new[] { "B1", "B2" }, loaded.CrossGroups[0].BaseIds);

        Assert.Single(loaded.RetakeScenarios);
        Assert.Equal(3, loaded.RetakeScenarios[0].CurrentGrade);
    }

    [Fact]
    public void SaveAll_OverwritesPreviousData()
    {
        var repo = new SqliteRepository(_dbPath);
        repo.EnsureCreated();

        var first = AppData.Empty() with
        {
            Rooms = new List<Room> { new() { Id = "R1", Name = "first" } },
        };
        repo.SaveAll(first);

        var second = AppData.Empty() with
        {
            Rooms = new List<Room> { new() { Id = "R2", Name = "second" } },
        };
        repo.SaveAll(second);

        var loaded = repo.LoadAll();
        Assert.Single(loaded.Rooms);
        Assert.Equal("R2", loaded.Rooms[0].Id);
    }
}
