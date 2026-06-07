using CommunityToolkit.Mvvm.ComponentModel;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel.Grid;

/// <summary>
/// One sub-column inside a day. Grade=null means an empty placeholder.
/// Width = how many parallel courses can fit in that grade for that day's max period.
/// </summary>
public sealed record GradeColumn(int? Grade, int Width);

public sealed record DayGroup(int Day, IReadOnlyList<GradeColumn> Grades)
{
    public int TotalWidth => Grades.Sum(g => g.Width);
}

/// <summary>
/// One cell in the unified grid, anchored at a specific (Day, Period, Grade, SubColumnIdx).
/// </summary>
public sealed record UnifiedCell(
    int Day, int Period, int Grade, int SubColumnIdx, CellAssignment Assignment);

public enum ManualMoveCellState
{
    Normal,
    Selected,
    Movable,
    Warning,
    Blocked,
}

public sealed record UnifiedCellKey(int Day, int Period, int Grade, int SubColumnIdx);

public sealed record EditCellState(ManualMoveCellState State, string Reason)
{
    public bool CanMove => State is ManualMoveCellState.Movable or ManualMoveCellState.Warning;
}

public sealed record CrossHoverState(bool CanCreate, string Reason)
{
    public static CrossHoverState Hidden(string reason = "") => new(false, reason);
    public static CrossHoverState Available(string reason = "크로스 가능: + 클릭 시 선택 수업이 대상 수업 시간대로 이동하고 HC-11 예외가 생성됩니다.") => new(true, reason);
}

public sealed record SwapHoverState(bool CanSwap, string Reason)
{
    public static SwapHoverState Hidden(string reason = "") => new(false, reason);
    public static SwapHoverState Available(string reason = "교환 가능: 클릭 시 선택 수업과 대상 수업의 위치를 서로 바꿉니다.") => new(true, reason);
}

public sealed partial class UnifiedTimetableViewModel : ObservableObject
{
    [ObservableProperty]
    private bool expandAllGrades;

    public IReadOnlyList<DayGroup> DayGroups { get; private set; } = Array.Empty<DayGroup>();

    public IReadOnlyList<UnifiedCell> Cells { get; private set; } = Array.Empty<UnifiedCell>();

    public UnifiedCellKey? SelectedCell { get; private set; }

    public IReadOnlyDictionary<UnifiedCellKey, EditCellState> EditStates { get; private set; } =
        new Dictionary<UnifiedCellKey, EditCellState>();

    public IReadOnlyDictionary<string, string> CrossLinkLabels { get; private set; } =
        new Dictionary<string, string>();

    public IReadOnlyDictionary<string, int> CrossParallelOrder { get; private set; } =
        new Dictionary<string, int>();

    public bool MergeOnlyStructuredBlocks { get; set; }

    public IReadOnlyList<int> Periods => Constants.Periods;

    public event EventHandler? Rebuilt;

    partial void OnExpandAllGradesChanged(bool value)
    {
        if (_lastAssignment != null && _lastCourses != null)
            Render(_lastAssignment, _lastCourses, _lastProfessors, _lastRooms);
    }

    private IReadOnlyList<SolutionAssignment>? _lastAssignment;
    private IReadOnlyList<Course>? _lastCourses;
    private IReadOnlyList<Professor>? _lastProfessors;
    private IReadOnlyList<Room>? _lastRooms;

    public void Render(
        IReadOnlyList<SolutionAssignment> assignment,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor>? professors = null,
        IReadOnlyList<Room>? rooms = null)
    {
        _lastAssignment = assignment;
        _lastCourses = courses;
        _lastProfessors = professors;
        _lastRooms = rooms;

        var courseMap = courses.ToDictionary(c => c.Id);
        var professorNames = BuildProfessorNameMap(professors);
        var roomNames = BuildRoomNameMap(rooms);

        // (cid, d, p) → set of rooms
        var roomsBySlot = new Dictionary<(string Cid, int Day, int Period), HashSet<string>>();
        foreach (var a in assignment)
        {
            if (!courseMap.ContainsKey(a.CourseId)) continue;
            var key = (a.CourseId, a.Day, a.Period);
            if (!roomsBySlot.TryGetValue(key, out var set))
                roomsBySlot[key] = set = new HashSet<string>();
            set.Add(a.RoomId);
        }

        var runs = ComputeVisualRuns(roomsBySlot, courseMap, MergeOnlyStructuredBlocks);

        var visualBlocks = BuildVisualBlocks(roomsBySlot, courseMap, runs);
        AssignSubColumns(visualBlocks, courseMap);

        // For each (day, grade), use the number of sub-columns needed by visual blocks.
        var maxWidth = new Dictionary<(int Day, int Grade), int>();
        for (int d = 0; d < Constants.Days; d++)
            foreach (var g in new[] { 1, 2, 3, 4 })
                maxWidth[(d, g)] = 0;

        foreach (var block in visualBlocks)
        {
            var g = courseMap[block.CourseId].Grade;
            var requiredWidth = block.SubColumnIdx + 1;
            if (requiredWidth > maxWidth[(block.Day, g)])
                maxWidth[(block.Day, g)] = requiredWidth;
        }

        // Build day groups.
        var dayGroups = new List<DayGroup>();
        for (int d = 0; d < Constants.Days; d++)
        {
            var entries = new List<GradeColumn>();
            foreach (var g in new[] { 1, 2, 3, 4 })
            {
                int w = maxWidth[(d, g)];
                if (ExpandAllGrades && w == 0) w = 1;
                if (w > 0) entries.Add(new GradeColumn(g, w));
            }
            if (entries.Count == 0) entries.Add(new GradeColumn(null, 1));
            dayGroups.Add(new DayGroup(d, entries));
        }
        DayGroups = dayGroups;

        // Build cells.
        var cells = new List<UnifiedCell>();
        foreach (var d in Enumerable.Range(0, Constants.Days))
        {
            foreach (var p in Constants.Periods)
            {
                if (p == Constants.LunchPeriod) continue;

                // For each grade in this day, find all (cid, rooms) at (d, p) of that grade,
                // sorted by section.
                var dayGroup = dayGroups[d];
                foreach (var col in dayGroup.Grades)
                {
                    if (col.Grade is not int g) continue;
                    var coursesHere = visualBlocks
                        .Where(b => b.Day == d && b.StartPeriod == p && courseMap[b.CourseId].Grade == g)
                        .OrderBy(b => b.SubColumnIdx)
                        .ToList();

                    foreach (var block in coursesHere)
                    {
                        var c = courseMap[block.CourseId];
                        cells.Add(new UnifiedCell(
                            d, p, g, block.SubColumnIdx,
                            CellAssignment.FromCourse(c, block.Rooms, block.RowSpan, professorNames, roomNames)));
                    }
                }
            }
        }
        Cells = cells;

        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    private static RunInfo ComputeVisualRuns(
        IReadOnlyDictionary<(string Cid, int Day, int Period), HashSet<string>> roomsBySlot,
        IReadOnlyDictionary<string, Course> courseMap,
        bool mergeOnlyStructuredBlocks)
    {
        var runLen = new Dictionary<(string CourseId, int Day, int Period), int>();
        var inside = new HashSet<(string CourseId, int Day, int Period)>();

        foreach (var group in roomsBySlot.Keys.GroupBy(k => (k.Cid, k.Day)))
        {
            var ordered = group.OrderBy(k => k.Period).ToList();
            int i = 0;
            while (i < ordered.Count)
            {
                int j = i;
                while (j + 1 < ordered.Count
                       && ordered[j + 1].Period == ordered[j].Period + 1
                       && (!mergeOnlyStructuredBlocks
                           || (courseMap.TryGetValue(ordered[i].Cid, out var course)
                               && j - i + 1 < MaxStructuredBlockLength(course)))
                       && CanMergeAsSameVisualBlock(
                           ordered[j],
                           ordered[j + 1],
                           roomsBySlot,
                           courseMap,
                           mergeOnlyStructuredBlocks))
                {
                    j++;
                }

                var start = ordered[i];
                runLen[(start.Cid, start.Day, start.Period)] = j - i + 1;
                for (int k = i + 1; k <= j; k++)
                {
                    var current = ordered[k];
                    inside.Add((current.Cid, current.Day, current.Period));
                }
                i = j + 1;
            }
        }

        return new RunInfo(runLen, inside);
    }

    private sealed class VisualBlock
    {
        public required string CourseId { get; init; }
        public required int Day { get; init; }
        public required int StartPeriod { get; init; }
        public required int RowSpan { get; init; }
        public required IReadOnlySet<string> Rooms { get; init; }
        public int EndPeriod => StartPeriod + RowSpan - 1;
        public int SubColumnIdx { get; set; }
    }

    private static List<VisualBlock> BuildVisualBlocks(
        IReadOnlyDictionary<(string Cid, int Day, int Period), HashSet<string>> roomsBySlot,
        IReadOnlyDictionary<string, Course> courseMap,
        RunInfo runs)
    {
        var blocks = new List<VisualBlock>();
        foreach (var ((cid, d, p), slotRooms) in roomsBySlot)
        {
            if (!courseMap.ContainsKey(cid)) continue;
            if (runs.Inside.Contains((cid, d, p))) continue;
            var rowSpan = runs.RunLen.TryGetValue((cid, d, p), out var n) ? n : 1;
            blocks.Add(new VisualBlock
            {
                CourseId = cid,
                Day = d,
                StartPeriod = p,
                RowSpan = rowSpan,
                Rooms = slotRooms,
            });
        }

        return blocks;
    }

    private void AssignSubColumns(
        IReadOnlyList<VisualBlock> blocks,
        IReadOnlyDictionary<string, Course> courseMap)
    {
        foreach (var group in blocks.GroupBy(b => (b.Day, courseMap[b.CourseId].Grade)))
        {
            var columns = new List<VisualBlock>();
            foreach (var block in group
                         .OrderBy(b => b.StartPeriod)
                         .ThenBy(b => CrossParallelOrder.GetValueOrDefault(b.CourseId, int.MaxValue))
                         .ThenBy(b => courseMap[b.CourseId].Section)
                         .ThenBy(b => b.CourseId, StringComparer.Ordinal))
            {
                var col = 0;
                while (col < columns.Count && BlocksOverlap(columns[col], block))
                    col++;

                block.SubColumnIdx = col;
                if (col == columns.Count)
                    columns.Add(block);
                else
                    columns[col] = block;
            }
        }
    }

    private static bool BlocksOverlap(VisualBlock a, VisualBlock b) =>
        a.Day == b.Day && a.StartPeriod <= b.EndPeriod && b.StartPeriod <= a.EndPeriod;

    private static bool CanMergeAsSameVisualBlock(
        (string Cid, int Day, int Period) a,
        (string Cid, int Day, int Period) b,
        IReadOnlyDictionary<(string Cid, int Day, int Period), HashSet<string>> roomsBySlot,
        IReadOnlyDictionary<string, Course> courseMap,
        bool mergeOnlyStructuredBlocks)
    {
        if (a.Cid != b.Cid || a.Day != b.Day) return false;
        if (!courseMap.TryGetValue(a.Cid, out var left) ||
            !courseMap.TryGetValue(b.Cid, out var right))
            return false;

        return left.Id == right.Id
            && left.Grade == right.Grade
            && left.Section == right.Section
            && left.ProfessorId == right.ProfessorId
            && left.Name == right.Name
            && roomsBySlot[a].SetEquals(roomsBySlot[b])
            && (!mergeOnlyStructuredBlocks || HasConsecutiveBlockEvidence(left));
    }

    private static bool HasConsecutiveBlockEvidence(Course course) =>
        course.BlockStructure.Any(length => length > 1)
        || (course.BlockStructure.Count == 0 && course.HoursPerWeek > 1);

    private static int MaxStructuredBlockLength(Course course) =>
        course.BlockStructure.Count == 0 ? Math.Max(1, course.HoursPerWeek) : course.BlockStructure.Max();

    public void SetEditState(
        UnifiedCellKey? selectedCell,
        IReadOnlyDictionary<UnifiedCellKey, EditCellState> editStates)
    {
        SelectedCell = selectedCell;
        EditStates = editStates;
        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    public void ClearEditState() =>
        SetEditState(null, new Dictionary<UnifiedCellKey, EditCellState>());

    public void SetCrossLinkLabels(IReadOnlyDictionary<string, string> labels)
    {
        CrossLinkLabels = labels;
        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    public void SetCrossParallelOrder(IReadOnlyDictionary<string, int> order)
    {
        CrossParallelOrder = order;
        if (_lastAssignment != null && _lastCourses != null)
            Render(_lastAssignment, _lastCourses, _lastProfessors, _lastRooms);
    }

    private static Dictionary<string, string>? BuildProfessorNameMap(IReadOnlyList<Professor>? professors) =>
        professors?.ToDictionary(p => p.Id, p => p.Name);

    private static Dictionary<string, string>? BuildRoomNameMap(IReadOnlyList<Room>? rooms) =>
        rooms?.ToDictionary(r => r.Id, r => r.Name);
}
