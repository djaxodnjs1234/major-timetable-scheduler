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
    }

    public List<SavedTimetableRecord> LoadSavedTimetables()
    {
        using var conn = Open();
        return conn.Query<SavedTimetableRow>(
                "SELECT * FROM SavedTimetables ORDER BY CreatedAt DESC")
            .Select(r => new SavedTimetableRecord(
                r.Id,
                r.Name,
                DateTime.Parse(r.CreatedAt),
                JsonSerializer.Deserialize<List<TimetableAssignmentRow>>(r.AssignmentsJson) ?? new()))
            .ToList();
    }

    public void UpsertSavedTimetable(SavedTimetableRecord t)
    {
        using var conn = Open();
        conn.Execute(
            @"INSERT OR REPLACE INTO SavedTimetables (Id, Name, CreatedAt, AssignmentsJson)
              VALUES (@Id, @Name, @CreatedAt, @AssignmentsJson)",
            new
            {
                t.Id,
                t.Name,
                CreatedAt = t.CreatedAt.ToString("O"),
                AssignmentsJson = JsonSerializer.Serialize(t.Assignments),
            });
    }

    public void DeleteSavedTimetable(string id)
    {
        using var conn = Open();
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
