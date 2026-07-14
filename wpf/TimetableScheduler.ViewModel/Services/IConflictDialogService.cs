using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel.Services;

public interface IConflictDialogService
{
    /// <summary>
    /// Prompt user when applying a change introduces new conflicts.
    /// Returns true to continue (keep the change), false to revert.
    /// </summary>
    bool ConfirmDespiteConflicts(IReadOnlyList<ConflictItem> newConflicts);

    bool ConfirmDespiteConflicts(
        IReadOnlyList<ConflictItem> newConflicts,
        ConflictSelectionContext? selection) => ConfirmDespiteConflicts(newConflicts);

    void ShowBlockingConflicts(string title, IReadOnlyList<ConflictItem> conflicts) { }

    void ShowValidationResult(string title, string message, IReadOnlyList<ConflictItem> conflicts) { }

    void ShowValidationResult(
        string title,
        string message,
        IReadOnlyList<ConflictItem> conflicts,
        IReadOnlyList<ValidationCheckItem> checks) =>
        ShowValidationResult(title, message, conflicts);

    bool ConfirmDiscardChanges() => true;
}

public enum ValidationCheckTier
{
    Critical = 1,
    Important = 2,
    Advisory = 3,
}

public sealed record ValidationCheckItem(
    string Name,
    ValidationCheckTier Tier,
    bool IsNormal,
    int ErrorCount,
    int WarningCount,
    string Detail = "",
    IReadOnlyList<ConflictItem>? Conflicts = null)
{
    public IReadOnlyList<ConflictItem> DetailConflicts => Conflicts ?? Array.Empty<ConflictItem>();

    public string TierTitle => "전체 검증";

    public string StatusText =>
        IsNormal ? "정상" : ErrorCount > 0 ? "비정상" : "주의";
}

public sealed record ConflictSelectionContext(
    string CourseId,
    string? AssignmentId,
    int Day,
    int Period);

public sealed class NullConflictDialogService : IConflictDialogService
{
    public bool ConfirmDespiteConflicts(IReadOnlyList<ConflictItem> _) => true;

    public void ShowBlockingConflicts(string title, IReadOnlyList<ConflictItem> conflicts) { }

    public void ShowValidationResult(string title, string message, IReadOnlyList<ConflictItem> conflicts) { }

    public void ShowValidationResult(
        string title,
        string message,
        IReadOnlyList<ConflictItem> conflicts,
        IReadOnlyList<ValidationCheckItem> checks) { }

    public bool ConfirmDiscardChanges() => true;
}
