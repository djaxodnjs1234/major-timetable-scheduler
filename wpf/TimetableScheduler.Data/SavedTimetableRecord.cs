namespace TimetableScheduler.Data;

public sealed record SavedTimetableRecord(
    string Id,
    string Name,
    DateTime CreatedAt,
    IReadOnlyList<TimetableAssignmentRow> Assignments);
