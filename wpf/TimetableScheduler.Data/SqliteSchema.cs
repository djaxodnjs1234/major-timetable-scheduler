namespace TimetableScheduler.Data;

internal static class SqliteSchema
{
    public const string CreateAll = @"
CREATE TABLE IF NOT EXISTS Courses (
    Id                TEXT NOT NULL,
    Name              TEXT NOT NULL,
    Grade             INTEGER NOT NULL,
    HoursPerWeek      INTEGER NOT NULL,
    CourseType        TEXT NOT NULL,
    ProfessorId       TEXT NOT NULL,
    Section           INTEGER NOT NULL DEFAULT 1,
    Department        TEXT NOT NULL,
    FixedRoomsJson    TEXT NOT NULL,
    UnavailableRoomsJson TEXT NOT NULL DEFAULT '[]',
    BlockStructureJson TEXT NOT NULL,
    IsFixed           INTEGER NOT NULL,
    FixedSlotsJson    TEXT NOT NULL,
    IsSchoolFixed     INTEGER NOT NULL DEFAULT 0,
    SchoolFixedTargetGrade INTEGER NOT NULL DEFAULT 0,
    CoteachProfsJson  TEXT NOT NULL,
    PRIMARY KEY (Id, Section)
);

CREATE TABLE IF NOT EXISTS Professors (
    Id                   TEXT PRIMARY KEY,
    Name                 TEXT NOT NULL,
    UnavailableSlotsJson TEXT NOT NULL,
    UnavailableRoomsJson TEXT NOT NULL DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS Rooms (
    Id                  TEXT PRIMARY KEY,
    Name                TEXT NOT NULL,
    IsLab               INTEGER NOT NULL DEFAULT 0,
    Capacity            INTEGER NOT NULL DEFAULT 0,
    IsImportedFromExcel INTEGER NOT NULL DEFAULT 0
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

CREATE TABLE IF NOT EXISTS AppSettings (
    Id                 INTEGER PRIMARY KEY CHECK (Id = 1),
    SchedulePolicyJson TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS SavedTimetables (
    Id              TEXT PRIMARY KEY,
    Name            TEXT NOT NULL,
    CreatedAt       TEXT NOT NULL,
    AssignmentsJson TEXT NOT NULL,
    SnapshotJson    TEXT,
    LunchPeriodsJson TEXT
);

CREATE TABLE IF NOT EXISTS SavedTimetableManualCrossLinks (
    Id              TEXT PRIMARY KEY,
    SavedTimetableId TEXT NOT NULL,
    SourceCourseId  TEXT NOT NULL,
    SourceGrade     INTEGER NOT NULL,
    SourceSection   TEXT NULL,
    SourceDay       INTEGER NOT NULL,
    SourcePeriod    INTEGER NOT NULL,
    SourceRoomId    TEXT NOT NULL,
    TargetCourseId  TEXT NOT NULL,
    TargetGrade     INTEGER NOT NULL,
    TargetSection   TEXT NULL,
    TargetDay       INTEGER NOT NULL,
    TargetPeriod    INTEGER NOT NULL,
    TargetRoomId    TEXT NOT NULL,
    PolicyType      TEXT NOT NULL,
    SourceAssignmentId TEXT NULL,
    TargetAssignmentId TEXT NULL,
    CreatedAt       TEXT NOT NULL
);
";
}
