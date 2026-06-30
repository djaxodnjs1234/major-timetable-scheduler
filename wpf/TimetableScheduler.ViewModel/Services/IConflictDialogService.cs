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

    bool ConfirmDiscardChanges() => true;
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

    public bool ConfirmDiscardChanges() => true;
}
