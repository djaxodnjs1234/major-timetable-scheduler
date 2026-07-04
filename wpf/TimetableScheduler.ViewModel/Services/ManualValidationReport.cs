using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel.Services;

public enum ManualValidationStatus
{
    Passed,
    Warning,
    Failed,
    NotChecked,
    Excluded,
}

public sealed record ManualValidationItem(
    string Id,
    string DisplayName,
    ManualValidationStatus Status,
    string Summary,
    int IssueCount,
    IReadOnlyList<ConflictItem> Details,
    string? Reason = null);

public sealed record ManualValidationReport(
    IReadOnlyList<ManualValidationItem> Items)
{
    public int TotalCount => Items.Count;
    public int PassedCount => Items.Count(i => i.Status == ManualValidationStatus.Passed);
    public int WarningCount => Items.Count(i => i.Status == ManualValidationStatus.Warning);
    public int FailedCount => Items.Count(i => i.Status == ManualValidationStatus.Failed);
    public int NotCheckedCount => Items.Count(i => i.Status == ManualValidationStatus.NotChecked);
    public int ExcludedCount => Items.Count(i => i.Status == ManualValidationStatus.Excluded);
}
