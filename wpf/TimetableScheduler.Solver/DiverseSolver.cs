using System.Diagnostics;
using Google.OrTools.Sat;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

public readonly record struct SolutionAssignment(
    string CourseId, int Day, int Period, string RoomId, string AssignmentId = "");

public sealed record SolverProgress(
    string Phase,
    string Message,
    int CurrentAttempt,
    int TotalAttempts,
    int UniqueSolutions);

public sealed record DiverseSolverOptions
{
    public int TotalSolutions { get; init; } = 500;
    public int TimeLimitSec { get; init; } = 120;
    public int PerSolveTimeSec { get; init; } = 5;
    public int BaseSeed { get; init; } = 0;
    public bool ConsiderRetakeStudents { get; init; }
    public bool UseSc01 { get; init; }
    public bool UseSc02 { get; init; }
    public bool UseSc03 { get; init; }
}

public sealed record DiverseSolverResult(
    string Status,
    IReadOnlyList<IReadOnlyList<SolutionAssignment>> Solutions,
    int? Sc01Bound,
    int? Sc02Bound,
    int? Sc03Bound,
    int Attempts);

public static class DiverseSolver
{
    public static DiverseSolverResult Solve(
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room> rooms,
        DiverseSolverOptions? options = null,
        IReadOnlyList<CrossGroup>? crosses = null,
        IReadOnlyList<RetakeScenario>? retakes = null,
        IProgress<SolverProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DiverseSolverOptions();
        var effectiveRetakes = EffectiveRetakes(courses, retakes, options.ConsiderRetakeStudents);
        var totalSw = Stopwatch.StartNew();

        int? sc01Bound = null, sc02Bound = null, sc03Bound = null;
        if (cancellationToken.IsCancellationRequested)
            return Cancelled();

        if (options.UseSc01)
        {
            if (cancellationToken.IsCancellationRequested)
                return Cancelled(sc01Bound, sc02Bound, sc03Bound);
            progress?.Report(new SolverProgress("1A", "SC-01 측정 중", 0, 0, 0));
            var p1 = ModelBuilder.Build(courses, professors, rooms, crosses, effectiveRetakes);
            var term = SoftConstraints.Sc01PenaltyTerm(p1.X, courses, rooms);
            p1.Model.Minimize(term);
            var s = NewSolver(options.PerSolveTimeSec * 2);
            var st = SolveWithCancellation(s, p1.Model, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return Cancelled(sc01Bound, sc02Bound, sc03Bound);
            if (!IsFeasible(st))
                return new DiverseSolverResult(StatusName(st), Array.Empty<IReadOnlyList<SolutionAssignment>>(), null, null, null, 0);
            int opt = (int)s.ObjectiveValue;
            sc01Bound = opt + Math.Max(0, SoftConstraints.Sc01SlackAbs);
            progress?.Report(new SolverProgress(
                "1A", $"SC-01 opt={opt}, bound={sc01Bound}", 0, 0, 0));
        }

        if (options.UseSc02)
        {
            if (cancellationToken.IsCancellationRequested)
                return Cancelled(sc01Bound, sc02Bound, sc03Bound);
            progress?.Report(new SolverProgress("1B", "SC-02 측정 중", 0, 0, 0));
            var p2 = ModelBuilder.Build(courses, professors, rooms, crosses, effectiveRetakes);
            if (sc01Bound.HasValue)
                p2.Model.Add(SoftConstraints.Sc01PenaltyTerm(p2.X, courses, rooms) <= sc01Bound.Value);
            var term = SoftConstraints.Sc02PenaltyTerm(p2.Model, p2.X, courses, rooms);
            p2.Model.Minimize(term);
            var s = NewSolver(options.PerSolveTimeSec * 2);
            var st = SolveWithCancellation(s, p2.Model, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return Cancelled(sc01Bound, sc02Bound, sc03Bound);
            if (!IsFeasible(st))
                return new DiverseSolverResult(StatusName(st), Array.Empty<IReadOnlyList<SolutionAssignment>>(), sc01Bound, null, null, 0);
            int opt = (int)s.ObjectiveValue;
            sc02Bound = opt + Math.Max(0, SoftConstraints.Sc02SlackAbs);
            progress?.Report(new SolverProgress(
                "1B", $"SC-02 opt={opt}, bound={sc02Bound}", 0, 0, 0));
        }

        if (options.UseSc03)
        {
            if (cancellationToken.IsCancellationRequested)
                return Cancelled(sc01Bound, sc02Bound, sc03Bound);
            progress?.Report(new SolverProgress("1C", "SC-03 측정 중", 0, 0, 0));
            var p3 = ModelBuilder.Build(courses, professors, rooms, crosses, effectiveRetakes);
            if (sc01Bound.HasValue)
                p3.Model.Add(SoftConstraints.Sc01PenaltyTerm(p3.X, courses, rooms) <= sc01Bound.Value);
            if (sc02Bound.HasValue)
                p3.Model.Add(SoftConstraints.Sc02PenaltyTerm(p3.Model, p3.X, courses, rooms) <= sc02Bound.Value);
            var term = SoftConstraints.Sc03PenaltyTerm(p3.Model, p3.DayVarsByCourse, courses);
            p3.Model.Minimize(term);
            var s = NewSolver(options.PerSolveTimeSec * 2);
            var st = SolveWithCancellation(s, p3.Model, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return Cancelled(sc01Bound, sc02Bound, sc03Bound);
            if (!IsFeasible(st))
                return new DiverseSolverResult(StatusName(st), Array.Empty<IReadOnlyList<SolutionAssignment>>(), sc01Bound, sc02Bound, null, 0);
            int opt = (int)s.ObjectiveValue;
            sc03Bound = opt + Math.Max(0, SoftConstraints.Sc03SlackAbs);
            progress?.Report(new SolverProgress(
                "1C", $"SC-03 opt={opt}, bound={sc03Bound}", 0, 0, 0));
        }

        // Phase 2: build full model with SC bounds, seed loop
        progress?.Report(new SolverProgress("2", "Phase 2 모델 빌드", 0, options.TotalSolutions, 0));
        var build = ModelBuilder.Build(courses, professors, rooms, crosses, effectiveRetakes);
        if (sc01Bound.HasValue)
            build.Model.Add(SoftConstraints.Sc01PenaltyTerm(build.X, courses, rooms) <= sc01Bound.Value);
        if (sc02Bound.HasValue)
            build.Model.Add(SoftConstraints.Sc02PenaltyTerm(build.Model, build.X, courses, rooms) <= sc02Bound.Value);
        if (sc03Bound.HasValue)
            build.Model.Add(SoftConstraints.Sc03PenaltyTerm(build.Model, build.DayVarsByCourse, courses) <= sc03Bound.Value);

        var seen = new HashSet<string>();
        var solutions = new List<IReadOnlyList<SolutionAssignment>>();
        string lastStatus = "UNKNOWN";
        int attempts = 0;

        for (int i = 0; i < options.TotalSolutions; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                return Cancelled(sc01Bound, sc02Bound, sc03Bound, attempts);

            double elapsed = totalSw.Elapsed.TotalSeconds;
            if (elapsed > options.TimeLimitSec) break;
            attempts++;

            double remaining = Math.Max(1.0, options.TimeLimitSec - elapsed);
            var perSolve = Math.Min(options.PerSolveTimeSec, remaining);
            var solver = NewSolver(perSolve, seed: options.BaseSeed + i, randomize: true);
            var status = SolveWithCancellation(solver, build.Model, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return Cancelled(sc01Bound, sc02Bound, sc03Bound, attempts);
            lastStatus = StatusName(status);

            bool isNew = false;
            if (IsFeasible(status))
            {
                var assignments = ExtractAssignments(solver, build.X);
                var key = MakeKey(assignments);
                if (seen.Add(key))
                {
                    solutions.Add(assignments);
                    isNew = true;
                }
            }

            string tag = isNew ? "+신규" : (IsFeasible(status) ? "=중복" : "실패");
            progress?.Report(new SolverProgress(
                "2", $"#{i + 1}: {lastStatus} {tag}",
                i + 1, options.TotalSolutions, solutions.Count));
        }

        return new DiverseSolverResult(
            lastStatus, solutions, sc01Bound, sc02Bound, sc03Bound, attempts);
    }

    private static CpSolverStatus SolveWithCancellation(
        CpSolver solver,
        CpModel model,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return CpSolverStatus.Unknown;
        using var registration = cancellationToken.Register(static state =>
        {
            if (state is CpSolver cpSolver)
                cpSolver.StopSearch();
        }, solver);
        var status = solver.Solve(model);
        return status;
    }

    private static DiverseSolverResult Cancelled(
        int? sc01Bound = null,
        int? sc02Bound = null,
        int? sc03Bound = null,
        int attempts = 0) =>
        new("CANCELLED", Array.Empty<IReadOnlyList<SolutionAssignment>>(), sc01Bound, sc02Bound, sc03Bound, attempts);

    private static CpSolver NewSolver(double maxTimeSec, int? seed = null, bool randomize = false)
    {
        var s = new CpSolver();
        var parts = new List<string> { $"max_time_in_seconds:{maxTimeSec.ToString(System.Globalization.CultureInfo.InvariantCulture)}" };
        if (seed.HasValue) parts.Add($"random_seed:{seed.Value}");
        if (randomize) parts.Add("randomize_search:true");
        s.StringParameters = string.Join(" ", parts);
        return s;
    }

    private static IReadOnlyList<RetakeScenario>? EffectiveRetakes(
        IReadOnlyList<Course> courses,
        IReadOnlyList<RetakeScenario>? manualRetakes,
        bool considerRetakeStudents)
    {
        if (!considerRetakeStudents)
            return null;

        var byKey = new Dictionary<(int Grade, string BaseId), RetakeScenario>();
        foreach (var retake in DomainHelpers.DeriveAutoRetakes(courses))
            byKey[(retake.CurrentGrade, retake.RetakeBaseId)] = retake;

        if (manualRetakes != null)
            foreach (var retake in manualRetakes)
                byKey[(retake.CurrentGrade, retake.RetakeBaseId)] = retake;

        return byKey.Values.ToList();
    }

    private static bool IsFeasible(CpSolverStatus s) =>
        s is CpSolverStatus.Feasible or CpSolverStatus.Optimal;

    private static string StatusName(CpSolverStatus s) => s switch
    {
        CpSolverStatus.Optimal => "OPTIMAL",
        CpSolverStatus.Feasible => "FEASIBLE",
        CpSolverStatus.Infeasible => "INFEASIBLE",
        CpSolverStatus.ModelInvalid => "MODEL_INVALID",
        _ => "UNKNOWN",
    };

    private static List<SolutionAssignment> ExtractAssignments(
        CpSolver solver,
        Dictionary<(string CourseId, int Day, int Period, string RoomId), BoolVar> x)
    {
        var list = new List<SolutionAssignment>();
        foreach (var (key, var) in x)
            if (solver.Value(var) == 1)
                list.Add(new SolutionAssignment(key.CourseId, key.Day, key.Period, key.RoomId));
        list.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.CourseId, b.CourseId);
            if (c != 0) return c;
            c = a.Day.CompareTo(b.Day);
            if (c != 0) return c;
            c = a.Period.CompareTo(b.Period);
            if (c != 0) return c;
            return string.CompareOrdinal(a.RoomId, b.RoomId);
        });
        return list;
    }

    private static string MakeKey(IReadOnlyList<SolutionAssignment> sorted)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var a in sorted)
        {
            sb.Append(a.CourseId).Append('|')
              .Append(a.Day).Append('|')
              .Append(a.Period).Append('|')
              .Append(a.RoomId).Append(';');
        }
        return sb.ToString();
    }
}
