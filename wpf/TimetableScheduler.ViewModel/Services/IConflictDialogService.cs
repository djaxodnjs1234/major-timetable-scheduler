using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel.Services;

public interface IConflictDialogService
{
    /// <summary>
    /// Prompt user when applying a change introduces new conflicts.
    /// Returns true to continue (keep the change), false to revert.
    /// </summary>
    bool ConfirmDespiteConflicts(IReadOnlyList<ConflictItem> newConflicts);
}

public sealed class NullConflictDialogService : IConflictDialogService
{
    public bool ConfirmDespiteConflicts(IReadOnlyList<ConflictItem> _) => true;
}
