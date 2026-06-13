using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel.Grid;

public sealed partial class TimetableGridViewModel : ObservableObject
{
    public IReadOnlyList<int> Days { get; } = Enumerable.Range(0, Constants.Days).ToList();
    public IReadOnlyList<int> Periods { get; } = Constants.Periods;

    public ObservableCollection<SlotCellViewModel> Cells { get; } = new();

    public TimetableGridViewModel()
    {
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in Constants.Periods)
                Cells.Add(new SlotCellViewModel(d, p));
    }

    public SlotCellViewModel CellAt(int day, int period)
    {
        var idx = day * Constants.Periods.Count + (period - 1);
        return Cells[idx];
    }

    public void Render(
        IReadOnlyList<SolutionAssignment> assignment,
        IReadOnlyList<Course> courses,
        Func<Course, string, bool>? accept = null,
        IReadOnlyList<Professor>? professors = null,
        IReadOnlyList<Room>? rooms = null)
    {
        foreach (var c in Cells) c.Clear();

        var assignmentCourseKeys = assignment
            .Select(a => (Assignment: a, CourseKey: BuildAssignmentCourseKey(a, courses)))
            .Where(a => !string.IsNullOrWhiteSpace(a.CourseKey))
            .ToList();
        var courseMap = assignmentCourseKeys
            .GroupBy(a => a.CourseKey, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => ResolveCourseForAssignment(g.First().Assignment, courses),
                StringComparer.Ordinal);
        var professorNames = BuildNameMap(professors);
        var roomNames = BuildNameMap(rooms);
        var runs = TimetableRuns.ComputeRuns(assignmentCourseKeys
            .Select(a => new SolutionAssignment(a.CourseKey, a.Assignment.Day, a.Assignment.Period, a.Assignment.RoomId))
            .ToList());

        // group by (course metadata key, day, period) -> set of rooms (for multi-room courses)
        var roomsBySlot = new Dictionary<(string CourseKey, int Day, int Period), HashSet<string>>();
        foreach (var (a, courseKey) in assignmentCourseKeys)
        {
            if (!courseMap.TryGetValue(courseKey, out var c)) continue;
            if (accept != null && !accept(c, a.RoomId)) continue;
            var key = (courseKey, a.Day, a.Period);
            if (!roomsBySlot.TryGetValue(key, out var set))
                roomsBySlot[key] = set = new HashSet<string>();
            set.Add(a.RoomId);
        }

        // For each unique (cid, day, period) entry, place CellAssignment at the START of the run only.
        // Skip cells that are "inside" the run.
        foreach (var ((courseKey, d, p), slotRooms) in roomsBySlot)
        {
            if (runs.Inside.Contains((courseKey, d, p))) continue;
            var c = courseMap[courseKey];
            int rs = runs.RunLen.TryGetValue((courseKey, d, p), out int n) ? n : 1;
            var cell = CellAt(d, p);
            cell.Add(CellAssignment.FromCourse(c, slotRooms, rs, professorNames, roomNames));
        }
    }

    private static string BuildAssignmentCourseKey(
        SolutionAssignment assignment,
        IReadOnlyList<Course> courses)
    {
        var candidates = courses
            .Where(c => c.Id == assignment.CourseId)
            .ToList();
        if (candidates.Count == 0) return "";
        if (candidates.Count == 1) return assignment.CourseId;

        var resolved = ResolveCourseForAssignment(assignment, courses);
        var resolvedIndex = courses
            .Select((Course, Index) => new { Course, Index })
            .FirstOrDefault(c => ReferenceEquals(c.Course, resolved))?.Index ?? 0;
        return string.Join("\u001f", assignment.CourseId, resolvedIndex, NormalizeRoomId(assignment.RoomId));
    }

    private static Course ResolveCourseForAssignment(
        SolutionAssignment assignment,
        IReadOnlyList<Course> courses)
    {
        var candidates = courses
            .Where(c => c.Id == assignment.CourseId)
            .ToList();
        if (candidates.Count == 0) return new Course { Id = assignment.CourseId };
        if (candidates.Count == 1) return candidates[0];

        var roomId = NormalizeRoomId(assignment.RoomId);
        var fixedRoomMatches = candidates
            .Where(c => c.FixedRooms.Any(room => string.Equals(NormalizeRoomId(room), roomId, StringComparison.Ordinal)))
            .ToList();
        if (fixedRoomMatches.Count == 1) return fixedRoomMatches[0];
        if (fixedRoomMatches.Count > 0) candidates = fixedRoomMatches;

        return candidates[0];
    }

    private static string NormalizeRoomId(string? roomId) =>
        string.IsNullOrWhiteSpace(roomId) ? "" : roomId.Trim();

    private static Dictionary<string, string>? BuildNameMap<T>(IReadOnlyList<T>? items)
    {
        if (items == null) return null;

        return items switch
        {
            IReadOnlyList<Professor> professors => professors.ToDictionary(p => p.Id, p => p.Name),
            IReadOnlyList<Room> rooms => rooms.ToDictionary(r => r.Id, r => r.Name),
            _ => null,
        };
    }
}
