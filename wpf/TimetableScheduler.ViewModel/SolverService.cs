using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel;

public sealed class SolverService
{
    public Task<DiverseSolverResult> SolveAsync(
        WorkspaceService workspace,
        DiverseSolverOptions options,
        IProgress<SolverProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = workspace.Snapshot();
        return Task.Run(() => DiverseSolver.Solve(
            snapshot.Courses, snapshot.Professors, snapshot.Rooms,
            options, snapshot.CrossGroups, snapshot.RetakeScenarios,
            progress, cancellationToken), cancellationToken);
    }

    public IReadOnlyList<RankedSolution> Rank(
        WorkspaceService workspace,
        DiverseSolverResult result,
        int topM = 10)
    {
        var s = workspace.Snapshot();
        return SolutionScoring.Rank(result.Solutions, s.Courses, s.Professors, topM);
    }
}
