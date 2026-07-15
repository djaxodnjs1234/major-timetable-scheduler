using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel;

public class SolverService
{
    private readonly SemaphoreSlim _solveGate = new(1, 1);

    public virtual async Task<DiverseSolverResult> SolveAsync(
        WorkspaceService workspace,
        DiverseSolverOptions options,
        IProgress<SolverProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _solveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        try
        {
            var schedulingSnapshot = workspace.SchedulingSnapshot();
            var snapshot = CloneForSolver(schedulingSnapshot);
            AssertSolverSnapshotEquivalent(schedulingSnapshot, snapshot);
            return await Task.Run(() =>
            {
                var schedulableCourses = SchoolFixedTimePolicy.SchedulableCourses(snapshot.Courses);
                var schoolFixedCourses = SchoolFixedTimePolicy.SchoolFixedCourses(snapshot.Courses);
                return DiverseSolver.Solve(
                    schedulableCourses, snapshot.Professors, snapshot.Rooms,
                    options, snapshot.CrossGroups, snapshot.RetakeScenarios,
                    progress, cancellationToken, schoolFixedCourses, snapshot.SchedulePolicy);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }
        finally
        {
            _solveGate.Release();
        }
    }

    public virtual IReadOnlyList<RankedSolution> Rank(
        WorkspaceService workspace,
        DiverseSolverResult result,
        int topM = 10)
    {
        var s = workspace.SchedulingSnapshot();
        var schedulableCourses = SchoolFixedTimePolicy.SchedulableCourses(s.Courses);
        return SolutionScoring.Rank(result.Solutions, schedulableCourses, s.Professors, topM);
    }

    private static DiverseSolverResult Cancelled() =>
        new(
            "CANCELLED",
            Array.Empty<SolverSolution>(),
            null,
            null,
            null,
            0);

    private static AppData CloneForSolver(AppData source) =>
        new(
            source.Courses.Select(CloneCourse).ToList(),
            source.Professors.Select(CloneProfessor).ToList(),
            source.Rooms.Select(CloneRoom).ToList(),
            source.CrossGroups.Select(CloneCrossGroup).ToList(),
            source.RetakeScenarios.Select(CloneRetakeScenario).ToList())
        {
            SchedulePolicy = source.SchedulePolicy,
        };

    private static Course CloneCourse(Course source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            Grade = source.Grade,
            HoursPerWeek = source.HoursPerWeek,
            CourseType = source.CourseType,
            ProfessorId = source.ProfessorId,
            ExpectedEnrollment = source.ExpectedEnrollment,
            Section = source.Section,
            Department = source.Department,
            FixedRooms = source.FixedRooms.ToList(),
            UnavailableRooms = source.UnavailableRooms.ToList(),
            BlockStructure = source.BlockStructure.ToList(),
            IsFixed = source.IsFixed,
            FixedSlots = source.FixedSlots.ToList(),
            IsSchoolFixed = source.IsSchoolFixed,
            SchoolFixedTargetGrade = source.SchoolFixedTargetGrade,
            CoteachProfs = source.CoteachProfs.ToList(),
        };

    private static Professor CloneProfessor(Professor source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            UnavailableSlots = source.UnavailableSlots.ToList(),
            UnavailableRooms = new List<string>(),
        };

    private static Room CloneRoom(Room source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            IsLab = source.IsLab,
            Capacity = source.Capacity,
            IsImportedFromExcel = source.IsImportedFromExcel,
        };

    private static CrossGroup CloneCrossGroup(CrossGroup source) =>
        new()
        {
            Id = source.Id,
            BaseIds = source.BaseIds.ToList(),
        };

    private static RetakeScenario CloneRetakeScenario(RetakeScenario source) =>
        new()
        {
            CurrentGrade = source.CurrentGrade,
            RetakeBaseId = source.RetakeBaseId,
        };

    private static void AssertSolverSnapshotEquivalent(AppData source, AppData snapshot)
    {
        if (source.RetakeScenarios.Count != snapshot.RetakeScenarios.Count)
            throw new InvalidOperationException(
                $"Retake snapshot count mismatch: source={source.RetakeScenarios.Count}, snapshot={snapshot.RetakeScenarios.Count}.");

        for (int i = 0; i < source.RetakeScenarios.Count; i++)
        {
            var a = source.RetakeScenarios[i];
            var b = snapshot.RetakeScenarios[i];
            if (a.CurrentGrade != b.CurrentGrade ||
                !string.Equals(a.RetakeBaseId, b.RetakeBaseId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Retake snapshot value mismatch at index " + i +
                    $": source=(grade:{a.CurrentGrade},baseId:{a.RetakeBaseId}) " +
                    $"snapshot=(grade:{b.CurrentGrade},baseId:{b.RetakeBaseId}).");
            }
        }
    }

}
