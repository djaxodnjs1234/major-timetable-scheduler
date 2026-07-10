using Google.OrTools.Sat;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

using XDict = Dictionary<(string CourseId, int Day, int Period, string RoomId), BoolVar>;
using YDict = Dictionary<(string CourseId, int Day, int Period), BoolVar>;
using StartVarMap = Dictionary<(string CourseId, int BlockIdx), Dictionary<(int Day, int StartPeriod), BoolVar>>;
using DayVarMap = Dictionary<string, List<IntVar>>;
using LunchVarMap = Dictionary<(int Day, int Period), BoolVar>;

public sealed class BuildResult
{
    public required CpModel Model { get; init; }
    public required XDict X { get; init; }
    public required YDict Y { get; init; }
    public required StartVarMap StartVarsByBlock { get; init; }
    public required DayVarMap DayVarsByCourse { get; init; }
    public required LunchVarMap LunchBanVars { get; init; }
    public required SchedulePolicy SchedulePolicy { get; init; }
    public required IReadOnlyList<Course> Courses { get; init; }
    public required IReadOnlyList<Room> Rooms { get; init; }
}

public static class ModelBuilder
{
    public static BuildResult Build(
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<CrossGroup>? crosses = null,
        IReadOnlyList<RetakeScenario>? retakes = null,
        IReadOnlyList<Course>? schoolFixedCourses = null,
        SchedulePolicy? schedulePolicy = null)
    {
        schedulePolicy ??= SchedulePolicy.Default;
        ValidateCrossGroups(courses, crosses);
        var model = new CpModel();
        var profMap = professors.ToDictionary(p => p.Id);

        // x[(c, d, p, r)] — full grid (periods 1..9 incl. lunch; HC-12 zeroes lunch)
        var x = new XDict();
        foreach (var c in courses)
            for (int d = 0; d < Constants.Days; d++)
                foreach (var p in Constants.Periods)
                    foreach (var r in rooms)
                        x[(c.Id, d, p, r.Id)] = model.NewBoolVar($"x_{c.Id}_{d}_{p}_{r.Id}");

        // y[(c, d, p)] — slot occupancy indicator. Σ_r x = K * y
        var y = new YDict();
        foreach (var c in courses)
        {
            int K = Math.Max(c.FixedRooms.Count, 1);
            for (int d = 0; d < Constants.Days; d++)
                foreach (var p in Constants.Periods)
                {
                    var yv = model.NewBoolVar($"y_{c.Id}_{d}_{p}");
                    y[(c.Id, d, p)] = yv;
                    var sum = LinearExpr.Sum(rooms.Select(r => x[(c.Id, d, p, r.Id)]).ToArray());
                    model.Add(sum == K * yv);
                }
        }

        BasicHcs.AddHc01_RoomSingle(model, x, courses, rooms, schedulePolicy);
        BasicHcs.AddHc02_ProfSingle(model, y, courses, schedulePolicy);
        BasicHcs.AddHc03_ProfUnavailable(model, x, courses, rooms, profMap);
        BasicHcs.AddHc04_Hours(model, x, courses, rooms, schedulePolicy);
        var lunchBanVars = BasicHcs.AddHc12_Lunch(model, x, courses, rooms, schedulePolicy);
        var allowGraduateDaytimeOverflow =
            AcademicLevelTimePolicy.AllowsGraduateDaytimeOverflow(courses, crosses);
        var (startVarsByBlock, dayVarsByCourse) =
            BlockHcs.AddHc06_BlockSplit(
                model, x, courses, rooms, schedulePolicy, lunchBanVars,
                allowGraduateDaytimeOverflow);
        BasicHcs.AddHc08_SectionNoOverlap(model, y, courses, schedulePolicy);
        BasicHcs.AddHc11_GradeNoOverlap(model, y, courses, crosses, schedulePolicy);
        BasicHcs.AddHc13_Fixed(model, y, courses);
        BasicHcs.AddHc24_SchoolFixedTimeBlocks(
            model,
            y,
            courses,
            schoolFixedCourses ?? Array.Empty<Course>(),
            schedulePolicy,
            lunchBanVars);
        BasicHcs.AddHc23_AcademicLevelTimeBands(
            model, x, y, courses, rooms, crosses, schedulePolicy);
        BlockHcs.AddHc14_FixedRooms(model, x, courses, rooms);
        BlockHcs.AddHc14_UnavailableRooms(model, x, courses, rooms);
        BlockHcs.AddHc15_SectionBackToBack(model, startVarsByBlock, courses);
        GroupingHcs.AddHc16_Cross(model, y, courses, crosses);
        GroupingHcs.AddHc17_Retake(model, y, courses, retakes);
        BlockHcs.AddHc19_Len2StartPeriods(
            model, startVarsByBlock, courses, crosses, schedulePolicy,
            lunchBanVars, allowGraduateDaytimeOverflow);
        BlockHcs.AddHc20_BlockDaysDistinct(model, dayVarsByCourse);
        BlockHcs.AddHc21_ProfRoomEligibility(model, x, courses, rooms, profMap);
        BlockHcs.AddHc22_AutoCourseRoomConsistent(model, x, courses, rooms);

        return new BuildResult
        {
            Model = model,
            X = x,
            Y = y,
            StartVarsByBlock = startVarsByBlock,
            DayVarsByCourse = dayVarsByCourse,
            LunchBanVars = lunchBanVars,
            SchedulePolicy = schedulePolicy,
            Courses = courses,
            Rooms = rooms,
        };
    }

    private static void ValidateCrossGroups(
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses)
    {
        if (crosses == null || crosses.Count == 0) return;

        var courseBaseIds = courses
            .Select(c => DomainHelpers.BaseId(c.Id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var cross in crosses)
        {
            var rawIds = cross.BaseIds
                .Select(id => id?.Trim() ?? "")
                .ToList();
            if (rawIds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException(
                    $"Invalid CrossGroup '{cross.Id}': BaseIds contains an empty course id.");

            var distinct = rawIds
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (distinct.Count != rawIds.Count)
                throw new InvalidOperationException(
                    $"Invalid CrossGroup '{cross.Id}': duplicate course id '{rawIds.GroupBy(id => id, StringComparer.Ordinal).First(g => g.Count() > 1).Key}'.");

            if (distinct.Count < 2)
                throw new InvalidOperationException(
                    $"Invalid CrossGroup '{cross.Id}': at least two different course base ids are required.");

            var missing = distinct
                .Where(id => !courseBaseIds.Contains(id))
                .ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"Invalid CrossGroup '{cross.Id}': unknown course base id(s): {string.Join(", ", missing)}.");
        }
    }
}
