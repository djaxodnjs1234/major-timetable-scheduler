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

    public IReadOnlyList<int> Periods => Constants.Periods;

    public event EventHandler? Rebuilt;

    partial void OnExpandAllGradesChanged(bool value)
    {
        if (_lastAssignment != null && _lastCourses != null)
            Render(_lastAssignment, _lastCourses);
    }

    private IReadOnlyList<SolutionAssignment>? _lastAssignment;
    private IReadOnlyList<Course>? _lastCourses;

    public void Render(
        IReadOnlyList<SolutionAssignment> assignment,
        IReadOnlyList<Course> courses)
    {
        _lastAssignment = assignment;
        _lastCourses = courses;

        var courseMap = courses.ToDictionary(c => c.Id);

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

        var runs = ComputeVisualRuns(roomsBySlot, courseMap);

        // For each (day, period), count distinct courses per grade — used to determine column width.
        // max_width[(d, grade)] = max over periods of (# courses of that grade in that slot)
        var maxWidth = new Dictionary<(int Day, int Grade), int>();
        for (int d = 0; d < Constants.Days; d++)
            foreach (var g in new[] { 1, 2, 3, 4 })
                maxWidth[(d, g)] = 0;

        for (int d = 0; d < Constants.Days; d++)
        {
            foreach (var p in Constants.Periods)
            {
                if (p == Constants.LunchPeriod) continue;
                var countByGrade = new Dictionary<int, int>();
                foreach (var ((cid, day, period), _) in roomsBySlot)
                {
                    if (day != d || period != p) continue;
                    var g = courseMap[cid].Grade;
                    countByGrade[g] = countByGrade.GetValueOrDefault(g) + 1;
                }
                foreach (var (g, cnt) in countByGrade)
                    if (cnt > maxWidth[(d, g)]) maxWidth[(d, g)] = cnt;
            }
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
                    var coursesHere = roomsBySlot
                        .Where(kv => kv.Key.Day == d && kv.Key.Period == p && courseMap[kv.Key.Cid].Grade == g)
                        .OrderBy(kv => CrossParallelOrder.GetValueOrDefault(kv.Key.Cid, int.MaxValue))
                        .ThenBy(kv => courseMap[kv.Key.Cid].Section)
                        .ToList();

                    int subIdx = 0;
                    foreach (var kv in coursesHere)
                    {
                        if (runs.Inside.Contains((kv.Key.Cid, d, p))) { subIdx++; continue; }
                        var c = courseMap[kv.Key.Cid];
                        int rs = runs.RunLen.TryGetValue((kv.Key.Cid, d, p), out int n) ? n : 1;
                        cells.Add(new UnifiedCell(
                            d, p, g, subIdx,
                            CellAssignment.FromCourse(c, kv.Value, rs)));
                        subIdx++;
                    }
                }
            }
        }
        Cells = cells;

        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    private static RunInfo ComputeVisualRuns(
        IReadOnlyDictionary<(string Cid, int Day, int Period), HashSet<string>> roomsBySlot,
        IReadOnlyDictionary<string, Course> courseMap)
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
                       && CanMergeAsSameVisualBlock(
                           ordered[j],
                           ordered[j + 1],
                           roomsBySlot,
                           courseMap))
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

    private static bool CanMergeAsSameVisualBlock(
        (string Cid, int Day, int Period) a,
        (string Cid, int Day, int Period) b,
        IReadOnlyDictionary<(string Cid, int Day, int Period), HashSet<string>> roomsBySlot,
        IReadOnlyDictionary<string, Course> courseMap)
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
            && roomsBySlot[a].SetEquals(roomsBySlot[b]);
    }

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
            Render(_lastAssignment, _lastCourses);
    }
}
