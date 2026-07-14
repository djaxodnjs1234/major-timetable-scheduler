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
                    UnavailableRooms = new List<string> { "R9" },
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
                    UnavailableRooms = new List<string> { "R2" },
                },
            },
            Rooms: new List<Room> { new() { Id = "R1", Name = "강의실1", IsLab = true, Capacity = 40, IsImportedFromExcel = true } },
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
        Assert.Equal(new[] { "R9" }, c.UnavailableRooms);
        Assert.Equal(new[] { 2, 1 }, c.BlockStructure);
        Assert.True(c.IsFixed);
        Assert.Equal(2, c.FixedSlots.Count);
        Assert.Equal(new TimeSlot(0, 1), c.FixedSlots[0]);
        Assert.Equal(new[] { "P2" }, c.CoteachProfs);

        Assert.Single(loaded.Professors);
        var p = loaded.Professors[0];
        Assert.Equal("P1", p.Id);
        Assert.Equal(new TimeSlot(4, 7), p.UnavailableSlots[0]);
        Assert.Empty(p.UnavailableRooms);

        Assert.Single(loaded.Rooms);
        Assert.Equal("R1", loaded.Rooms[0].Id);
        Assert.True(loaded.Rooms[0].IsLab);
        Assert.Equal(40, loaded.Rooms[0].Capacity);
        Assert.True(loaded.Rooms[0].IsImportedFromExcel);

        Assert.Single(loaded.CrossGroups);
        Assert.Equal(new[] { "B1", "B2" }, loaded.CrossGroups[0].BaseIds);

        Assert.Single(loaded.RetakeScenarios);
        Assert.Equal(3, loaded.RetakeScenarios[0].CurrentGrade);
    }

    [Fact]
    public void EnsureCreated_OnOldProfessorRoomWhitelistSchema_RemovesLegacyColumn()
    {
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                   new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE Professors (" +
                "Id TEXT PRIMARY KEY, Name TEXT NOT NULL, " +
                "UnavailableSlotsJson TEXT NOT NULL, " +
                "Allowed" + "RoomsJson TEXT NOT NULL, " +
                "UnavailableRoomsJson TEXT NOT NULL DEFAULT '[]');" +
                "INSERT INTO Professors VALUES ('P1', '교수1', '[]', '[\"R1\"]', '[\"R2\"]');";
            cmd.ExecuteNonQuery();
        }

        var repo = new SqliteRepository(_dbPath);
        repo.EnsureCreated();

        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                   new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString()))
        {
            conn.Open();
            var columns = conn.CreateCommand();
            columns.CommandText = "SELECT name FROM pragma_table_info('Professors')";
            var names = new List<string>();
            using var reader = columns.ExecuteReader();
            while (reader.Read())
                names.Add(reader.GetString(0));

            Assert.DoesNotContain("Allowed" + "RoomsJson", names);
        }

        var professor = Assert.Single(repo.LoadAll().Professors);
        Assert.Equal("P1", professor.Id);
        Assert.Empty(professor.UnavailableRooms);
    }

    [Fact]
    public void SaveAll_LoadAll_PreservesDifferentProfessorsForSameCourseIdSections()
    {
        var repo = new SqliteRepository(_dbPath);
        repo.EnsureCreated();

        repo.SaveAll(new AppData(
            Courses: new List<Course>
            {
                new() { Id = "X", Name = "테스트과목", Grade = 2, HoursPerWeek = 1, CourseType = "전필", ProfessorId = "P1", Section = 1 },
                new() { Id = "X", Name = "테스트과목", Grade = 2, HoursPerWeek = 1, CourseType = "전필", ProfessorId = "P2", Section = 2 },
            },
            Professors: new List<Professor>
            {
                new() { Id = "P1", Name = "교수 A" },
                new() { Id = "P2", Name = "교수 B" },
            },
            Rooms: new List<Room>(),
            CrossGroups: new List<CrossGroup>(),
            RetakeScenarios: new List<RetakeScenario>()));

        var loaded = repo.LoadAll().Courses.OrderBy(course => course.Section).ToList();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("P1", loaded[0].ProfessorId);
        Assert.Equal("P2", loaded[1].ProfessorId);
    }

    [Fact]
    public void EnsureCreated_OnOldRoomSchema_AddsImportedFlagDefaultFalse()
    {
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                   new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE Rooms (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, IsLab INTEGER NOT NULL DEFAULT 0, Capacity INTEGER NOT NULL DEFAULT 0);" +
                "INSERT INTO Rooms (Id, Name, IsLab, Capacity) VALUES ('R1', '기존강의실', 0, 30);";
            cmd.ExecuteNonQuery();
        }

        var repo = new SqliteRepository(_dbPath);
        repo.EnsureCreated();

        var room = Assert.Single(repo.LoadAll().Rooms);
        Assert.Equal("R1", room.Id);
        Assert.False(room.IsImportedFromExcel);
    }

    [Fact]
    public void EnsureCreated_OnOldSchema_AddsSnapshotColumnPreservingData()
    {
        // Simulate a pre-snapshot DB: SavedTimetables without the SnapshotJson column.
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                   new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE SavedTimetables (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, " +
                "CreatedAt TEXT NOT NULL, AssignmentsJson TEXT NOT NULL);" +
                "INSERT INTO SavedTimetables VALUES ('id1', 'old', '2026-01-01T00:00:00.0000000', '[]');";
            cmd.ExecuteNonQuery();
        }

        var repo = new SqliteRepository(_dbPath);
        repo.EnsureCreated();

        var saved = repo.LoadSavedTimetables();
        Assert.Single(saved);
        Assert.Equal("old", saved[0].Name);
        Assert.Null(saved[0].SnapshotJson);

        // New saves with a snapshot work after migration.
        repo.UpsertSavedTimetable(new SavedTimetableRecord(
            "id2", "new", DateTime.Now,
            new List<TimetableAssignmentRow>(), SnapshotJson: "{\"snapshot\":true}"));
        Assert.Equal("{\"snapshot\":true}",
            repo.LoadSavedTimetables().Single(t => t.Name == "new").SnapshotJson);
    }

    [Fact]
    public void SavedManualCrossLinks_DoNotPolluteCrossGroupsAndRoundTripAssignmentIds()
    {
        var repo = new SqliteRepository(_dbPath);
        repo.EnsureCreated();
        repo.SaveAll(new AppData(
            new List<Course>
            {
                new() { Id = "A-01", Name = "A", Grade = 2, HoursPerWeek = 1, CourseType = "전필", ProfessorId = "P1", Section = 1 },
                new() { Id = "B-01", Name = "B", Grade = 2, HoursPerWeek = 1, CourseType = "전필", ProfessorId = "P2", Section = 1 },
            },
            new List<Professor>(),
            new List<Room>(),
            new List<CrossGroup>(),
            new List<RetakeScenario>()));

        repo.UpsertSavedTimetable(new SavedTimetableRecord(
            "saved-1",
            "manual-cross",
            DateTime.Now,
            new List<TimetableAssignmentRow>(),
            new[]
            {
                new SavedManualCrossLinkRow(
                    "A-01", 2, "1", 0, 1, "R1",
                    "B-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION",
                    "assign-a",
                    "assign-b"),
            }));

        var loadedInputs = repo.LoadAll();
        Assert.Empty(loadedInputs.CrossGroups);

        var saved = Assert.Single(repo.LoadSavedTimetables());
        var link = Assert.Single(saved.ManualCrossLinks!);
        Assert.Equal("assign-a", link.SourceAssignmentId);
        Assert.Equal("assign-b", link.TargetAssignmentId);
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
