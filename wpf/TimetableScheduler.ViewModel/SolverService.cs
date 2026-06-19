using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel;

public class SolverService
{
    public virtual Task<DiverseSolverResult> SolveAsync(
        WorkspaceService workspace,
        DiverseSolverOptions options,
        IProgress<SolverProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = workspace.SchedulingSnapshot();
        return Task.Run(() =>
        {
            try
            {
                return DiverseSolver.Solve(
                    snapshot.Courses, snapshot.Professors, snapshot.Rooms,
                    options, snapshot.CrossGroups, snapshot.RetakeScenarios,
                    progress, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new DiverseSolverResult(
                    "CANCELLED",
                    Array.Empty<IReadOnlyList<SolutionAssignment>>(),
                    null,
                    null,
                    null,
                    0);
            }
        });
    }

    public virtual IReadOnlyList<RankedSolution> Rank(
        WorkspaceService workspace,
        DiverseSolverResult result,
        int topM = 10)
    {
        var s = workspace.SchedulingSnapshot();
        return SolutionScoring.Rank(result.Solutions, s.Courses, s.Professors, topM);
    }
}
