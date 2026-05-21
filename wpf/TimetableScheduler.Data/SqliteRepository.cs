using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Data;

public sealed class SqliteRepository
{
    private readonly string _connStr;

    public string DbPath { get; }

    public SqliteRepository(string dbPath)
    {
        DbPath = dbPath;
        _connStr = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public static SqliteRepository OpenNextToExe(string fileName = "timetable.db")
    {
        var dir = AppContext.BaseDirectory;
        return new SqliteRepository(Path.Combine(dir, fileName));
    }

    public void ExportTo(string destPath)
    {
        SqliteConnection.ClearAllPools();
        File.Copy(DbPath, destPath, overwrite: true);
    }

    public void ReplaceWith(string sourcePath)
    {
        SqliteConnection.ClearAllPools();
        File.Copy(sourcePath, DbPath, overwrite: true);
        EnsureCreated();
    }

    public void EnsureCreated()
    {
        using var conn = Open();
        conn.Execute(SqliteSchema.CreateAll);
        MigrateCoursePkIfNeeded(conn);
        MigrateSavedTimetableSnapshot(conn);
    }

    private static void MigrateSavedTimetableSnapshot(SqliteConnection conn)
    {
        var columns = conn.Query("PRAGMA table_info(SavedTimetables)")
            .Select(row => (string)row.name);
        if (columns.Contains("SnapshotJson")) return;
        conn.Execute("ALTER TABLE SavedTimetables ADD COLUMN SnapshotJson TEXT");
    }

    private static void MigrateCoursePkIfNeeded(SqliteConnection conn)
    {
        var ddl = conn.QueryFirstOrDefault<string>(
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='Courses'");
        if (ddl == null || ddl.Contains("PRIMARY KEY (Id, Section)")) return;

        // Recreate Courses table with composite primary key, preserving data
        conn.Execute("CREATE TABLE Courses_v2 (" +
            "Id TEXT NOT NULL, Name TEXT NOT NULL, Grade INTEGER NOT NULL," +
            "HoursPerWeek INTEGER NOT NULL, CourseType TEXT NOT NULL," +
            "ProfessorId TEXT NOT NULL, Section INTEGER NOT NULL DEFAULT 1," +
            "Department TEXT NOT NULL, FixedRoomsJson TEXT NOT NULL," +
            "BlockStructureJson TEXT NOT NULL, IsFixed INTEGER NOT NULL," +
            "FixedSlotsJson TEXT NOT NULL, CoteachProfsJson TEXT NOT NULL," +
            "PRIMARY KEY (Id, Section))");
        conn.Execute("INSERT OR IGNORE INTO Courses_v2 SELECT * FROM Courses");
        conn.Execute("DROP TABLE Courses");
        conn.Execute("ALTER TABLE Courses_v2 RENAME TO Courses");
    }

    public AppData LoadAll()
    {
        using var conn = Open();

        var courses = conn.Query<CourseRow>("SELECT * FROM Courses").Select(ToCourse).ToList();
        var profs = conn.Query<ProfessorRow>("SELECT * FROM Professors").Select(ToProf).ToList();
        var rooms = conn.Query<Room>("SELECT Id, Name FROM Rooms").ToList();
        var crosses = conn.Query<CrossRow>("SELECT * FROM CrossGroups").Select(ToCross).ToList();
        var retakes = conn.Query<RetakeScenario>(
            "SELECT CurrentGrade, RetakeBaseId FROM RetakeScenarios").ToList();

        return new AppData(courses, profs, rooms, crosses, retakes);
    }

    public void SaveAll(AppData data)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        conn.Execute("DELETE FROM Courses; DELETE FROM Professors; DELETE FROM Rooms; " +
                     "DELETE FROM CrossGroups; DELETE FROM RetakeScenarios;", transaction: tx);

        foreach (var c in data.Courses)
            conn.Execute(@"INSERT INTO Courses
                (Id,Name,Grade,HoursPerWeek,CourseType,ProfessorId,Section,Department,
                 FixedRoomsJson,BlockStructureJson,IsFixed,FixedSlotsJson,CoteachProfsJson)
                VALUES (@Id,@Name,@Grade,@HoursPerWeek,@CourseType,@ProfessorId,@Section,@Department,
                 @FixedRoomsJson,@BlockStructureJson,@IsFixed,@FixedSlotsJson,@CoteachProfsJson)",
                FromCourse(c), tx);

        foreach (var p in data.Professors)
            conn.Execute(@"INSERT INTO Professors
                (Id,Name,UnavailableSlotsJson,AllowedRoomsJson)
                VALUES (@Id,@Name,@UnavailableSlotsJson,@AllowedRoomsJson)",
                FromProf(p), tx);

        foreach (var r in data.Rooms)
            conn.Execute("INSERT INTO Rooms (Id,Name) VALUES (@Id,@Name)", r, tx);

        foreach (var g in data.CrossGroups)
            conn.Execute("INSERT INTO CrossGroups (Id,BaseIdsJson) VALUES (@Id,@BaseIdsJson)",
                new { g.Id, BaseIdsJson = JsonSerializer.Serialize(g.BaseIds) }, tx);

        foreach (var r in data.RetakeScenarios)
            conn.Execute("INSERT INTO RetakeScenarios (CurrentGrade,RetakeBaseId) VALUES (@CurrentGrade,@RetakeBaseId)",
                r, tx);

        tx.Commit();
    }

    private sealed class SavedTimetableRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string AssignmentsJson { get; set; } = "[]";
        public string? SnapshotJson { get; set; }
    }

    private sealed class SavedManualCrossLinkDbRow
    {
        public string SavedTimetableId { get; set; } = "";
        public string SourceCourseId { get; set; } = "";
        public int SourceGrade { get; set; }
        public string? SourceSection { get; set; }
        public int SourceDay { get; set; }
        public int SourcePeriod { get; set; }
        public string SourceRoomId { get; set; } = "";
        public string TargetCourseId { get; set; } = "";
        public int TargetGrade { get; set; }
        public string? TargetSection { get; set; }
        public int TargetDay { get; set; }
        public int TargetPeriod { get; set; }
        public string TargetRoomId { get; set; } = "";
        public string PolicyType { get; set; } = "";
    }

    public List<SavedTimetableRecord> LoadSavedTimetables()
    {
        using var conn = Open();
        var crossLinks = conn.Query<SavedManualCrossLinkDbRow>(
                "SELECT * FROM SavedTimetableManualCrossLinks")
            .GroupBy(r => r.SavedTimetableId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SavedManualCrossLinkRow>)g
                    .Select(r => new SavedManualCrossLinkRow(
                        r.SourceCourseId,
                        r.SourceGrade,
                        r.SourceSection,
                        r.SourceDay,
                        r.SourcePeriod,
                        r.SourceRoomId,
                        r.TargetCourseId,
                        r.TargetGrade,
                        r.TargetSection,
                        r.TargetDay,
                        r.TargetPeriod,
                        r.TargetRoomId,
                        r.PolicyType))
                    .ToList());

        return conn.Query<SavedTimetableRow>(
                "SELECT * FROM SavedTimetables ORDER BY CreatedAt DESC")
            .Select(r => new SavedTimetableRecord(
                r.Id,
                r.Name,
                DateTime.Parse(r.CreatedAt),
                JsonSerializer.Deserialize<List<TimetableAssignmentRow>>(r.AssignmentsJson) ?? new(),
                crossLinks.TryGetValue(r.Id, out var links) ? links : Array.Empty<SavedManualCrossLinkRow>(),
                r.SnapshotJson))
            .ToList();
    }

    public void UpsertSavedTimetable(SavedTimetableRecord t)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        conn.Execute(
            @"INSERT OR REPLACE INTO SavedTimetables (Id, Name, CreatedAt, AssignmentsJson, SnapshotJson)
              VALUES (@Id, @Name, @CreatedAt, @AssignmentsJson, @SnapshotJson)",
            new
            {
                t.Id,
                t.Name,
                CreatedAt = t.CreatedAt.ToString("O"),
                AssignmentsJson = JsonSerializer.Serialize(t.Assignments),
                t.SnapshotJson,
            },
            tx);

        conn.Execute("DELETE FROM SavedTimetableManualCrossLinks WHERE SavedTimetableId = @Id", new { t.Id }, tx);

        foreach (var link in t.ManualCrossLinks ?? Array.Empty<SavedManualCrossLinkRow>())
        {
            conn.Execute(
                @"INSERT INTO SavedTimetableManualCrossLinks
                  (Id, SavedTimetableId, SourceCourseId, SourceGrade, SourceSection, SourceDay, SourcePeriod, SourceRoomId,
                   TargetCourseId, TargetGrade, TargetSection, TargetDay, TargetPeriod, TargetRoomId, PolicyType, CreatedAt)
                  VALUES
                  (@Id, @SavedTimetableId, @SourceCourseId, @SourceGrade, @SourceSection, @SourceDay, @SourcePeriod, @SourceRoomId,
                   @TargetCourseId, @TargetGrade, @TargetSection, @TargetDay, @TargetPeriod, @TargetRoomId, @PolicyType, @CreatedAt)",
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    SavedTimetableId = t.Id,
                    link.SourceCourseId,
                    link.SourceGrade,
                    link.SourceSection,
                    link.SourceDay,
                    link.SourcePeriod,
                    link.SourceRoomId,
                    link.TargetCourseId,
                    link.TargetGrade,
                    link.TargetSection,
                    link.TargetDay,
                    link.TargetPeriod,
                    link.TargetRoomId,
                    link.PolicyType,
                    CreatedAt = t.CreatedAt.ToString("O"),
                },
                tx);
        }

        tx.Commit();
    }

    public void DeleteSavedTimetable(string id)
    {
        using var conn = Open();
        conn.Execute("DELETE FROM SavedTimetableManualCrossLinks WHERE SavedTimetableId = @Id", new { Id = id });
        conn.Execute("DELETE FROM SavedTimetables WHERE Id = @Id", new { Id = id });
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connStr);
        conn.Open();
        return conn;
    }

    // --- row DTOs ---

    private sealed class CourseRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Grade { get; set; }
        public int HoursPerWeek { get; set; }
        public string CourseType { get; set; } = "";
        public string ProfessorId { get; set; } = "";
        public int Section { get; set; }
        public string Department { get; set; } = "";
        public string FixedRoomsJson { get; set; } = "[]";
        public string BlockStructureJson { get; set; } = "[]";
        public long IsFixed { get; set; }
        public string FixedSlotsJson { get; set; } = "[]";
        public string CoteachProfsJson { get; set; } = "[]";
    }

    private sealed class ProfessorRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string UnavailableSlotsJson { get; set; } = "[]";
        public string AllowedRoomsJson { get; set; } = "[]";
    }

    private sealed class CrossRow
    {
        public string Id { get; set; } = "";
        public string BaseIdsJson { get; set; } = "[]";
    }

    private static Course ToCourse(CourseRow r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Grade = r.Grade,
        HoursPerWeek = r.HoursPerWeek,
        CourseType = r.CourseType,
        ProfessorId = r.ProfessorId,
        Section = r.Section,
        Department = r.Department,
        FixedRooms = JsonSerializer.Deserialize<List<string>>(r.FixedRoomsJson) ?? new(),
        BlockStructure = JsonSerializer.Deserialize<List<int>>(r.BlockStructureJson) ?? new(),
        IsFixed = r.IsFixed != 0,
        FixedSlots = JsonSerializer.Deserialize<List<TimeSlot>>(r.FixedSlotsJson) ?? new(),
        CoteachProfs = JsonSerializer.Deserialize<List<string>>(r.CoteachProfsJson) ?? new(),
    };

    private static object FromCourse(Course c) => new
    {
        c.Id, c.Name, c.Grade, c.HoursPerWeek, c.CourseType, c.ProfessorId,
        c.Section, c.Department,
        FixedRoomsJson = JsonSerializer.Serialize(c.FixedRooms),
        BlockStructureJson = JsonSerializer.Serialize(c.BlockStructure),
        IsFixed = c.IsFixed ? 1 : 0,
        FixedSlotsJson = JsonSerializer.Serialize(c.FixedSlots),
        CoteachProfsJson = JsonSerializer.Serialize(c.CoteachProfs),
    };

    private static Professor ToProf(ProfessorRow r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        UnavailableSlots = JsonSerializer.Deserialize<List<TimeSlot>>(r.UnavailableSlotsJson) ?? new(),
        AllowedRooms = JsonSerializer.Deserialize<List<string>>(r.AllowedRoomsJson) ?? new(),
    };

    private static object FromProf(Professor p) => new
    {
        p.Id, p.Name,
        UnavailableSlotsJson = JsonSerializer.Serialize(p.UnavailableSlots),
        AllowedRoomsJson = JsonSerializer.Serialize(p.AllowedRooms),
    };

    private static CrossGroup ToCross(CrossRow r) => new()
    {
        Id = r.Id,
        BaseIds = JsonSerializer.Deserialize<List<string>>(r.BaseIdsJson) ?? new(),
    };
}
