namespace TimetableScheduler.Data;

internal static class SqliteSchema
{
    public const string CreateAll = @"
CREATE TABLE IF NOT EXISTS Courses (
    Id                TEXT PRIMARY KEY,
    Name              TEXT NOT NULL,
    Grade             INTEGER NOT NULL,
    HoursPerWeek      INTEGER NOT NULL,
    CourseType        TEXT NOT NULL,
    ProfessorId       TEXT NOT NULL,
    Section           INTEGER NOT NULL,
    Department        TEXT NOT NULL,
    FixedRoomsJson    TEXT NOT NULL,
    BlockStructureJson TEXT NOT NULL,
    IsFixed           INTEGER NOT NULL,
    FixedSlotsJson    TEXT NOT NULL,
    CoteachProfsJson  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Professors (
    Id                   TEXT PRIMARY KEY,
    Name                 TEXT NOT NULL,
    UnavailableSlotsJson TEXT NOT NULL,
    AllowedRoomsJson     TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Rooms (
    Id   TEXT PRIMARY KEY,
    Name TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS CrossGroups (
    Id          TEXT PRIMARY KEY,
    BaseIdsJson TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS RetakeScenarios (
    CurrentGrade  INTEGER NOT NULL,
    RetakeBaseId  TEXT NOT NULL,
    PRIMARY KEY (CurrentGrade, RetakeBaseId)
);
";
}
