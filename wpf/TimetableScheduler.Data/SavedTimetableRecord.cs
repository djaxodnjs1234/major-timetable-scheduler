namespace TimetableScheduler.Data;

public sealed record SavedTimetableRecord(
    string Id,
    string Name,
    DateTime CreatedAt,
    IReadOnlyList<TimetableAssignmentRow> Assignments,
    IReadOnlyList<SavedManualCrossLinkRow>? ManualCrossLinks = null,
    string? SnapshotJson = null);

public sealed record SavedManualCrossLinkRow(
    string SourceCourseId,
    int SourceGrade,
    string? SourceSection,
    int SourceDay,
    int SourcePeriod,
    string SourceRoomId,
    string TargetCourseId,
    int TargetGrade,
    string? TargetSection,
    int TargetDay,
    int TargetPeriod,
    string TargetRoomId,
    string PolicyType);
