using Google.OrTools.Sat;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

using XDict = Dictionary<(string CourseId, int Day, int Period, string RoomId), BoolVar>;
using YDict = Dictionary<(string CourseId, int Day, int Period), BoolVar>;
using StartVarMap = Dictionary<(string CourseId, int BlockIdx), Dictionary<(int Day, int StartPeriod), BoolVar>>;
using DayVarMap = Dictionary<string, List<IntVar>>;

public sealed class BuildResult
{
    public required CpModel Model { get; init; }
    public required XDict X { get; init; }
    public required YDict Y { get; init; }
    public required StartVarMap StartVarsByBlock { get; init; }
    public required DayVarMap DayVarsByCourse { get; init; }
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
        IReadOnlyList<RetakeScenario>? retakes = null)
    {
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

        BasicHcs.AddHc01_RoomSingle(model, x, courses, rooms);
        BasicHcs.AddHc02_ProfSingle(model, y, courses);
        BasicHcs.AddHc03_ProfUnavailable(model, x, courses, rooms, profMap);
        BasicHcs.AddHc04_Hours(model, x, courses, rooms);
        var (startVarsByBlock, dayVarsByCourse) =
            BlockHcs.AddHc06_BlockSplit(model, x, courses, rooms);
        BasicHcs.AddHc08_SectionNoOverlap(model, y, courses);
        BasicHcs.AddHc11_GradeNoOverlap(model, y, courses, crosses);
        BasicHcs.AddHc12_Lunch(model, x, courses, rooms);
        BasicHcs.AddHc13_Fixed(model, y, courses);
        BlockHcs.AddHc14_FixedRooms(model, x, courses, rooms);
        BlockHcs.AddHc15_SectionBackToBack(model, startVarsByBlock, courses);
        GroupingHcs.AddHc16_Cross(model, y, courses, crosses);
        GroupingHcs.AddHc17_Retake(model, y, courses, retakes);
        BlockHcs.AddHc18_BlockDayGap(model, dayVarsByCourse);
        BlockHcs.AddHc19_Len2StartPeriods(model, startVarsByBlock, courses, crosses);
        BlockHcs.AddHc20_BlockDaysDistinct(model, dayVarsByCourse);
        BlockHcs.AddHc21_ProfRoomConsistent(model, x, courses, rooms, profMap);

        return new BuildResult
        {
            Model = model,
            X = x,
            Y = y,
            StartVarsByBlock = startVarsByBlock,
            DayVarsByCourse = dayVarsByCourse,
            Courses = courses,
            Rooms = rooms,
        };
    }
}
