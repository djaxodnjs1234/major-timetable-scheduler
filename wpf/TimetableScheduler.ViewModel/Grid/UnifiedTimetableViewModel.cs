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
    public static CrossHoverState Available(string reason = "크로스 가능: + 클릭 시 선택 수업이 대상 수업 시간대로 이동하고 학년 충돌 예외가 생성됩니다.") => new(true, reason);
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
    public SchedulePolicy SchedulePolicy { get; private set; } = SchedulePolicy.Default;
    public IReadOnlyDictionary<int, int> LunchPeriodsByDay { get; private set; } =
        SchedulePolicyRules.StaticLunchPeriodsByDay(SchedulePolicy.Default, Constants.Days);

    public bool IsLunch(int day, int period) =>
        SchedulePolicyRules.IsLunch(SchedulePolicy, LunchPeriodsByDay, day, period);

    public event EventHandler? Rebuilt;

    public static string BuildManualCrossDisplayKey(
        CellAssignment assignment,
        int day,
        int period) =>
        BuildManualCrossDisplayKey(
            assignment.CourseId,
            assignment.Section,
            day,
            period,
            assignment.RowSpan,
            assignment.Rooms);

    public static string BuildManualCrossDisplayKey(
        string courseId,
        int section,
        int day,
        int period,
        int rowSpan,
        IEnumerable<string> roomIds) =>
        string.Join("\u001f", new[]
        {
            courseId.Trim(),
            section > 0 ? section.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
            day.ToString(System.Globalization.CultureInfo.InvariantCulture),
            period.ToString(System.Globalization.CultureInfo.InvariantCulture),
            rowSpan.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BuildRoomIdsKey(roomIds),
        });

    partial void OnExpandAllGradesChanged(bool value)
    {
        if (_lastAssignment != null && _lastCourses != null)
            Render(
                _lastAssignment,
                _lastCourses,
                _lastProfessors,
                _lastRooms,
                SchedulePolicy,
                LunchPeriodsByDay);
    }

    private IReadOnlyList<SolutionAssignment>? _lastAssignment;
    private IReadOnlyList<Course>? _lastCourses;
    private IReadOnlyList<Professor>? _lastProfessors;
    private IReadOnlyList<Room>? _lastRooms;

    public void Render(
        IReadOnlyList<SolutionAssignment> assignment,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor>? professors = null,
        IReadOnlyList<Room>? rooms = null,
        SchedulePolicy? schedulePolicy = null,
        IReadOnlyDictionary<int, int>? lunchPeriodsByDay = null)
    {
        SchedulePolicy = schedulePolicy ?? SchedulePolicy.Default;
        LunchPeriodsByDay = lunchPeriodsByDay
            ?? SchedulePolicyRules.StaticLunchPeriodsByDay(SchedulePolicy, Constants.Days);
        _lastAssignment = assignment;
        _lastCourses = courses;
        _lastProfessors = professors;
        _lastRooms = rooms;

        var display = BuildSchoolFixedDisplayAssignments(assignment, courses, ExpandAllGrades);
        var displayAssignments = display.Assignments;
        var displayCourses = display.Courses;

        var courseMap = BuildAssignmentCourseMap(displayAssignments, displayCourses);
        var multiSectionBaseIds = displayCourses
            .GroupBy(course => DomainHelpers.BaseId(course.Id), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var professorNames = BuildProfessorNameMap(professors);
        var roomNames = BuildRoomNameMap(rooms);

        // (course metadata key, d, p) -> set of rooms. The metadata key is not just
        // CourseId because some imported/saved sessions can contain duplicate Course.Id.
        var roomsBySlot = new Dictionary<(string CourseKey, int Day, int Period), HashSet<string>>();
        var assignmentIdsBySlot = new Dictionary<(string CourseKey, int Day, int Period), HashSet<string>>();
        foreach (var a in displayAssignments)
        {
            var courseKey = BuildAssignmentCourseKey(a, displayCourses);
            if (!courseMap.ContainsKey(courseKey)) continue;
            var key = (courseKey, a.Day, a.Period);
            if (!roomsBySlot.TryGetValue(key, out var set))
                roomsBySlot[key] = set = new HashSet<string>();
            set.Add(a.RoomId);
            if (!assignmentIdsBySlot.TryGetValue(key, out var idSet))
                assignmentIdsBySlot[key] = idSet = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(a.AssignmentId))
                idSet.Add(a.AssignmentId);
        }

        var runs = ComputeVisualRuns(roomsBySlot, courseMap, MergeOnlyStructuredBlocks);

        var visualBlocks = BuildVisualBlocks(roomsBySlot, assignmentIdsBySlot, courseMap, runs);
        AssignSubColumns(visualBlocks, courseMap);

        // For each (day, grade), use the number of sub-columns needed by visual blocks.
        var maxWidth = new Dictionary<(int Day, int Grade), int>();
        for (int d = 0; d < Constants.Days; d++)
            foreach (var g in AcademicLevels.AllGrades)
                maxWidth[(d, g)] = 0;

        foreach (var block in visualBlocks)
        {
            var g = courseMap[block.CourseKey].Grade;
            var requiredWidth = block.SubColumnIdx + 1;
            if (requiredWidth > maxWidth[(block.Day, g)])
                maxWidth[(block.Day, g)] = requiredWidth;
        }

        // Build day groups.
        var dayGroups = new List<DayGroup>();
        for (int d = 0; d < Constants.Days; d++)
        {
            var entries = new List<GradeColumn>();
            foreach (var g in AcademicLevels.AllGrades)
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
                // For each grade in this day, find all (cid, rooms) at (d, p) of that grade,
                // sorted by section.
                var dayGroup = dayGroups[d];
                foreach (var col in dayGroup.Grades)
                {
                    if (col.Grade is not int g) continue;
                    var coursesHere = visualBlocks
                        .Where(b => b.Day == d && b.StartPeriod == p && courseMap[b.CourseKey].Grade == g)
                        .OrderBy(b => b.SubColumnIdx)
                        .ToList();

                    foreach (var block in coursesHere)
                    {
                        var c = courseMap[block.CourseKey];
                        cells.Add(new UnifiedCell(
                            d, p, g, block.SubColumnIdx,
                            CellAssignment.FromCourse(
                                c, block.Rooms, block.RowSpan, professorNames, roomNames,
                                block.AssignmentId, block.CourseKey,
                                multiSectionBaseIds.Contains(DomainHelpers.BaseId(c.Id)))));
                    }
                }
            }
        }
        Cells = cells;

        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    private static (IReadOnlyList<SolutionAssignment> Assignments, IReadOnlyList<Course> Courses)
        BuildSchoolFixedDisplayAssignments(
            IReadOnlyList<SolutionAssignment> assignments,
            IReadOnlyList<Course> courses,
            bool expandAllGrades)
    {
        var schoolFixedCourses = courses
            .Where(course => course.IsSchoolFixed && course.IsFixed && course.FixedSlots.Count > 0)
            .ToList();
        if (schoolFixedCourses.Count == 0)
            return (assignments, courses);

        var displayAssignments = assignments.ToList();
        var displayCourses = courses
            .Where(course => !course.IsSchoolFixed)
            .Select(CloneDisplayCourse)
            .ToList();
        var normalCourseMap = courses
            .Where(course => !course.IsSchoolFixed)
            .GroupBy(course => course.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var visibleGradesByDay = Enumerable.Range(0, Constants.Days)
            .ToDictionary(day => day, _ => new HashSet<int>());

        foreach (var assignment in assignments)
            if (normalCourseMap.TryGetValue(assignment.CourseId, out var course))
                visibleGradesByDay[assignment.Day].Add(course.Grade);

        foreach (var course in schoolFixedCourses.Where(course => course.SchoolFixedTargetGrade != SchoolFixedTimePolicy.AllGrades))
            foreach (var slot in course.FixedSlots)
                if (AcademicLevels.AllGrades.Contains(course.SchoolFixedTargetGrade))
                    visibleGradesByDay[slot.Day].Add(course.SchoolFixedTargetGrade);

        if (expandAllGrades)
            foreach (var grades in visibleGradesByDay.Values)
                foreach (var grade in AcademicLevels.AllGrades)
                    grades.Add(grade);

        var addedCourseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var course in schoolFixedCourses)
        {
            foreach (var slot in course.FixedSlots)
            {
                var grades = course.SchoolFixedTargetGrade == SchoolFixedTimePolicy.AllGrades
                    ? visibleGradesByDay[slot.Day].OrderBy(grade => grade).ToList()
                    : new List<int> { course.SchoolFixedTargetGrade };
                if (grades.Count == 0)
                    grades = AcademicLevels.AllGrades.ToList();

                foreach (var grade in grades.Where(grade => AcademicLevels.AllGrades.Contains(grade)))
                {
                    var displayCourseId = SchoolFixedDisplayCourseId(course.Id, grade);
                    if (addedCourseIds.Add(displayCourseId))
                    {
                        var clone = CloneDisplayCourse(course);
                        clone.Id = displayCourseId;
                        clone.Grade = grade;
                        clone.Section = 0;
                        clone.CourseType = "";
                        clone.ProfessorId = "";
                        clone.CoteachProfs.Clear();
                        clone.FixedRooms.Clear();
                        clone.UnavailableRooms.Clear();
                        displayCourses.Add(clone);
                    }

                    displayAssignments.Add(new SolutionAssignment(
                        displayCourseId,
                        slot.Day,
                        slot.Period,
                        "",
                        $"school-fixed\u001f{course.Id}\u001f{grade}"));
                }
            }
        }

        return (displayAssignments, displayCourses);
    }

    private static string SchoolFixedDisplayCourseId(string courseId, int grade) =>
        $"{courseId}__school_fixed__{grade}";

    private static Course CloneDisplayCourse(Course course) => new()
    {
        Id = course.Id,
        Name = course.Name,
        Grade = course.Grade,
        HoursPerWeek = course.HoursPerWeek,
        CourseType = course.CourseType,
        ProfessorId = course.ProfessorId,
        Section = course.Section,
        Department = course.Department,
        FixedRooms = course.FixedRooms.ToList(),
        UnavailableRooms = course.UnavailableRooms.ToList(),
        BlockStructure = course.BlockStructure.ToList(),
        IsFixed = course.IsFixed,
        FixedSlots = course.FixedSlots.ToList(),
        IsSchoolFixed = course.IsSchoolFixed,
        SchoolFixedTargetGrade = course.SchoolFixedTargetGrade,
        CoteachProfs = course.CoteachProfs.ToList(),
    };

    private static Dictionary<string, Course> BuildAssignmentCourseMap(
        IReadOnlyList<SolutionAssignment> assignments,
        IReadOnlyList<Course> courses)
    {
        var map = new Dictionary<string, Course>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            var key = BuildAssignmentCourseKey(assignment, courses);
            if (string.IsNullOrWhiteSpace(key)) continue;
            map.TryAdd(key, ResolveCourseForAssignment(assignment, courses));
        }
        return map;
    }

    private static string BuildAssignmentCourseKey(
        SolutionAssignment assignment,
        IReadOnlyList<Course> courses)
    {
        var candidates = courses
            .Select((Course, Index) => new { Course, Index })
            .Where(c => c.Course.Id == assignment.CourseId)
            .ToList();
        if (candidates.Count == 0) return "";
        if (candidates.Count == 1) return assignment.CourseId;

        var resolved = ResolveCourseForAssignment(assignment, courses);
        var resolvedIndex = courses
            .Select((Course, Index) => new { Course, Index })
            .FirstOrDefault(c => ReferenceEquals(c.Course, resolved))?.Index ?? 0;
        var roomKey = NormalizeRoomId(assignment.RoomId);
        return string.Join("\u001f", assignment.CourseId, resolvedIndex, roomKey);
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

        var assignmentIdCourse = ResolveCourseFromAssignmentId(assignment.AssignmentId, candidates);
        if (assignmentIdCourse != null) return assignmentIdCourse;

        var roomId = NormalizeRoomId(assignment.RoomId);
        var fixedRoomMatches = candidates
            .Where(c => c.FixedRooms.Any(room => string.Equals(NormalizeRoomId(room), roomId, StringComparison.Ordinal)))
            .ToList();
        if (fixedRoomMatches.Count == 1) return fixedRoomMatches[0];

        var unavailableRoomExclusions = candidates
            .Where(c => c.UnavailableRooms.All(room => !string.Equals(NormalizeRoomId(room), roomId, StringComparison.Ordinal)))
            .ToList();
        if (unavailableRoomExclusions.Count == 1) return unavailableRoomExclusions[0];

        return candidates[0];
    }

    private static Course? ResolveCourseFromAssignmentId(
        string? assignmentId,
        IReadOnlyList<Course> candidates)
    {
        if (string.IsNullOrWhiteSpace(assignmentId)) return null;

        var parts = assignmentId.Split('\u001f');
        if (parts.Length < 7 || !string.Equals(parts[0], "occ", StringComparison.Ordinal))
            return null;

        var courseId = parts[1];
        var sectionText = parts[2];
        var professorId = parts[3];
        if (!int.TryParse(sectionText, out var section)) return null;

        var matches = candidates
            .Where(c => string.Equals(c.Id, courseId, StringComparison.Ordinal)
                && c.Section == section
                && string.Equals(c.ProfessorId, professorId, StringComparison.Ordinal))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string NormalizeRoomId(string? roomId) =>
        string.IsNullOrWhiteSpace(roomId) ? "" : roomId.Trim();

    private static RunInfo ComputeVisualRuns(
        IReadOnlyDictionary<(string CourseKey, int Day, int Period), HashSet<string>> roomsBySlot,
        IReadOnlyDictionary<string, Course> courseMap,
        bool mergeOnlyStructuredBlocks)
    {
        var runLen = new Dictionary<(string CourseId, int Day, int Period), int>();
        var inside = new HashSet<(string CourseId, int Day, int Period)>();

        foreach (var group in roomsBySlot.Keys.GroupBy(k => (k.CourseKey, k.Day)))
        {
            var ordered = group.OrderBy(k => k.Period).ToList();
            int i = 0;
            while (i < ordered.Count)
            {
                int j = i;
                while (j + 1 < ordered.Count
                       && ordered[j + 1].Period == ordered[j].Period + 1
                       && (!mergeOnlyStructuredBlocks
                           || (courseMap.TryGetValue(ordered[i].CourseKey, out var course)
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
                runLen[(start.CourseKey, start.Day, start.Period)] = j - i + 1;
                for (int k = i + 1; k <= j; k++)
                {
                    var current = ordered[k];
                    inside.Add((current.CourseKey, current.Day, current.Period));
                }
                i = j + 1;
            }
        }

        return new RunInfo(runLen, inside);
    }

    private sealed class VisualBlock
    {
        public required string CourseKey { get; init; }
        public required int Day { get; init; }
        public required int StartPeriod { get; init; }
        public required int RowSpan { get; init; }
        public required IReadOnlySet<string> Rooms { get; init; }
        public required string AssignmentId { get; init; }
        public int EndPeriod => StartPeriod + RowSpan - 1;
        public int SubColumnIdx { get; set; }
    }

    private static List<VisualBlock> BuildVisualBlocks(
        IReadOnlyDictionary<(string CourseKey, int Day, int Period), HashSet<string>> roomsBySlot,
        IReadOnlyDictionary<(string CourseKey, int Day, int Period), HashSet<string>> assignmentIdsBySlot,
        IReadOnlyDictionary<string, Course> courseMap,
        RunInfo runs)
    {
        var blocks = new List<VisualBlock>();
        foreach (var ((courseKey, d, p), slotRooms) in roomsBySlot)
        {
            if (!courseMap.ContainsKey(courseKey)) continue;
            if (runs.Inside.Contains((courseKey, d, p))) continue;
            var rowSpan = runs.RunLen.TryGetValue((courseKey, d, p), out var n) ? n : 1;
            var ids = Enumerable.Range(p, rowSpan)
                .SelectMany(period => assignmentIdsBySlot.TryGetValue((courseKey, d, period), out var slotIds)
                    ? slotIds
                    : Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            blocks.Add(new VisualBlock
            {
                CourseKey = courseKey,
                Day = d,
                StartPeriod = p,
                RowSpan = rowSpan,
                Rooms = slotRooms,
                AssignmentId = ids.Count == 1 ? ids[0] : "",
            });
        }

        return blocks;
    }

    private void AssignSubColumns(
        IReadOnlyList<VisualBlock> blocks,
        IReadOnlyDictionary<string, Course> courseMap)
    {
        foreach (var group in blocks.GroupBy(b => (b.Day, courseMap[b.CourseKey].Grade)))
        {
            var columns = new List<VisualBlock>();
            foreach (var block in group
                         .OrderBy(b => b.StartPeriod)
                         .ThenBy(b => CrossParallelOrder.GetValueOrDefault(BuildManualCrossDisplayKey(b, courseMap), int.MaxValue))
                         .ThenBy(b => courseMap[b.CourseKey].Section)
                         .ThenBy(b => courseMap[b.CourseKey].Id, StringComparer.Ordinal)
                         .ThenBy(b => b.CourseKey, StringComparer.Ordinal))
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

    private static string BuildManualCrossDisplayKey(
        VisualBlock block,
        IReadOnlyDictionary<string, Course> courseMap)
    {
        var course = courseMap[block.CourseKey];
        return BuildManualCrossDisplayKey(
            course.Id,
            course.Section,
            block.Day,
            block.StartPeriod,
            block.RowSpan,
            block.Rooms);
    }

    private static string BuildRoomIdsKey(IEnumerable<string> roomIds) =>
        string.Join("|", roomIds
            .Select(roomId => roomId.Trim())
            .Where(roomId => !string.IsNullOrEmpty(roomId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(roomId => roomId, StringComparer.Ordinal));

    private static bool CanMergeAsSameVisualBlock(
        (string CourseKey, int Day, int Period) a,
        (string CourseKey, int Day, int Period) b,
        IReadOnlyDictionary<(string CourseKey, int Day, int Period), HashSet<string>> roomsBySlot,
        IReadOnlyDictionary<string, Course> courseMap,
        bool mergeOnlyStructuredBlocks)
    {
        if (a.CourseKey != b.CourseKey || a.Day != b.Day) return false;
        if (!courseMap.TryGetValue(a.CourseKey, out var left) ||
            !courseMap.TryGetValue(b.CourseKey, out var right))
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
            Render(
                _lastAssignment,
                _lastCourses,
                _lastProfessors,
                _lastRooms,
                SchedulePolicy,
                LunchPeriodsByDay);
    }

    private static Dictionary<string, string>? BuildProfessorNameMap(IReadOnlyList<Professor>? professors) =>
        professors?.ToDictionary(p => p.Id, p => p.Name);

    private static Dictionary<string, string>? BuildRoomNameMap(IReadOnlyList<Room>? rooms) =>
        rooms?.ToDictionary(r => r.Id, r => r.Name);
}
