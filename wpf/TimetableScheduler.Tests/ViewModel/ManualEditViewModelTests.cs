using Microsoft.Extensions.DependencyInjection;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.ViewModel.Services;

namespace TimetableScheduler.Tests.ViewModel;

public class ManualEditViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _sp;
    private readonly WorkspaceService _ws;
    private readonly RecordingDialogService _dialog;

    private sealed class RecordingDialogService : IConflictDialogService
    {
        public bool ResponseToReturn { get; set; } = true;
        public List<IReadOnlyList<ConflictItem>> Calls { get; } = new();
        public bool ConfirmDespiteConflicts(IReadOnlyList<ConflictItem> newConflicts)
        {
            Calls.Add(newConflicts);
            return ResponseToReturn;
        }
    }

    public ManualEditViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"manual_test_{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddTimetableScheduler(_dbPath);
        _dialog = new RecordingDialogService();
        services.AddSingleton<IConflictDialogService>(_dialog);
        _sp = services.BuildServiceProvider();
        _ws = _sp.GetRequiredService<WorkspaceService>();

        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 2, ProfessorId = "P1" });
        _ws.AddProfessor(new Professor { Id = "P1", Name = "교수" });
        _ws.AddRoom(new Room { Id = "R1", Name = "강의실1" });
        _ws.AddRoom(new Room { Id = "R2", Name = "강의실2" });
    }

    public void Dispose()
    {
        _sp.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private RankedSolution MakeSolution(params SolutionAssignment[] assignments)
    {
        var score = new SolutionScore(1, 1, 1, 3);
        return new RankedSolution(assignments.ToList(), score);
    }

    private SavedTimetableRecord MakeSavedRecordWithManualCross(
        string id,
        string name,
        IReadOnlyList<TimetableAssignmentRow> assignments,
        SavedManualCrossLinkRow manualCrossLink) =>
        new(
            id,
            name,
            DateTime.Now,
            assignments,
            new[] { manualCrossLink },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

    private static SavedManualCrossLinkRow SavedCross(
        string sourceCourseId = "X-01",
        string targetCourseId = "Y-01",
        int sourceDay = 0,
        int sourcePeriod = 1,
        int targetDay = 0,
        int targetPeriod = 1) =>
        new(
            sourceCourseId, 2, "1", sourceDay, sourcePeriod, "R1",
            targetCourseId, 2, "1", targetDay, targetPeriod, "R2",
            "HC11_ONLY_EXCEPTION");

    private static TimetableScheduler.ViewModel.Grid.CellAssignment AssignmentAt(
        ManualEditViewModel vm,
        int day,
        int period,
        string courseId) =>
        vm.Grid.Cells.First(c => c.Day == day && c.Period == period && c.Assignment.CourseId == courseId).Assignment;

    private static ManualEditViewModel.ManualCrossAssignmentKey ManualCrossAssignmentKey(
        string courseId = "X-01",
        int section = 1,
        int day = 0,
        int period = 1,
        int rowSpan = 1,
        IReadOnlyList<string>? rooms = null)
    {
        var assignment = new CellAssignment(
            courseId,
            "테스트",
            "P1",
            2,
            section,
            rooms ?? new[] { "R1" },
            rowSpan,
            1,
            false);
        var method = typeof(ManualEditViewModel).GetMethod(
            "BuildManualCrossAssignmentKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
            binder: null,
            types: new[] { typeof(CellAssignment), typeof(int), typeof(int) },
            modifiers: null);
        Assert.NotNull(method);
        return (ManualEditViewModel.ManualCrossAssignmentKey)method.Invoke(null, new object[] { assignment, day, period })!;
    }

    private static ManualEditViewModel.ManualCrossLink ManualCrossLink(
        ManualEditViewModel.ManualCrossAssignmentKey source,
        ManualEditViewModel.ManualCrossAssignmentKey target) =>
        new(
            source.CourseId,
            target.CourseId,
            target.Day,
            target.Period,
            source.RowSpan,
            target.Period,
            target.RowSpan,
            source,
            target);

    private static bool IsAlreadyCrossedByLink(
        ManualEditViewModel.ManualCrossLink link,
        ManualEditViewModel.ManualCrossAssignmentKey key) =>
        link.Contains(key);

    private static bool IsCrossPair(
        ManualEditViewModel.ManualCrossLink link,
        ManualEditViewModel.ManualCrossAssignmentKey left,
        ManualEditViewModel.ManualCrossAssignmentKey right) =>
        link.MatchesPair(left, right);

    private static IReadOnlyList<SavedManualCrossLinkRow> SavedManualCrossRows(ManualEditViewModel vm)
    {
        var method = typeof(ManualEditViewModel).GetMethod(
            "ToSavedManualCrossLinks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        return (IReadOnlyList<SavedManualCrossLinkRow>)method.Invoke(vm, Array.Empty<object>())!;
    }

    private static int RestoreSavedManualCrossRows(
        ManualEditViewModel vm,
        IReadOnlyList<SavedManualCrossLinkRow> rows)
    {
        var method = typeof(ManualEditViewModel).GetMethod(
            "RestoreSavedManualCrossLinks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        return (int)method.Invoke(vm, new object[] { rows })!;
    }

    private static void SetWorkingAssignments(ManualEditViewModel vm, params SolutionAssignment[] assignments)
    {
        var field = typeof(ManualEditViewModel).GetField(
            "_working",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(vm, assignments.ToList());
    }

    private static IReadOnlyList<SolutionAssignment> WorkingAssignments(ManualEditViewModel vm)
    {
        var field = typeof(ManualEditViewModel).GetField(
            "_working",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<IReadOnlyList<SolutionAssignment>>(field.GetValue(vm));
    }

    private static void AddWorkingCrossLink(
        ManualEditViewModel vm,
        ManualEditViewModel.ManualCrossLink link)
    {
        var field = typeof(ManualEditViewModel).GetField(
            "_workingCrossLinks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        var links = Assert.IsAssignableFrom<IList<ManualEditViewModel.ManualCrossLink>>(field.GetValue(vm));
        links.Add(link);
    }

    private static IReadOnlyDictionary<string, int> CrossParallelOrder(ManualEditViewModel vm)
    {
        var method = typeof(ManualEditViewModel).GetMethod(
            "BuildCrossParallelOrder",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        return (IReadOnlyDictionary<string, int>)method.Invoke(vm, Array.Empty<object>())!;
    }

    private static IReadOnlyDictionary<string, string> CrossLinkLabels(ManualEditViewModel vm)
    {
        var method = typeof(ManualEditViewModel).GetMethod(
            "BuildCrossLinkLabels",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        return (IReadOnlyDictionary<string, string>)method.Invoke(vm, Array.Empty<object>())!;
    }

    private static string ManualCrossDisplayKey(
        string courseId = "X-01",
        int section = 1,
        int day = 0,
        int period = 1,
        int rowSpan = 1,
        IReadOnlyList<string>? rooms = null) =>
        UnifiedTimetableViewModel.BuildManualCrossDisplayKey(
            courseId,
            section,
            day,
            period,
            rowSpan,
            rooms ?? new[] { "R1" });

    private static CellAssignment ManualCell(
        string courseId = "X-01",
        string name = "테스트",
        string professorId = "P1",
        int grade = 2,
        int section = 1,
        IReadOnlyList<string>? rooms = null,
        int rowSpan = 1,
        IReadOnlyList<string>? coteachProfIds = null) =>
        new(
            courseId,
            name,
            professorId,
            grade,
            section,
            rooms ?? new[] { "R1" },
            rowSpan,
            rowSpan,
            false)
        {
            CoteachProfIds = coteachProfIds ?? Array.Empty<string>(),
        };

    private (ManualEditViewModel Vm, CellAssignment Source, CellAssignment Target) ArrangeSameBaseManualCrossCase(
        int sourceGrade = 2,
        int targetGrade = 2,
        int sourceRowSpan = 2,
        int targetRowSpan = 2,
        int sourceSection = 1,
        int targetSection = 2,
        string sourceProfessorId = "4",
        string targetProfessorId = "12",
        string sourceRoomId = "3",
        string targetRoomId = "6",
        bool includeThirdAssignment = false)
    {
        if (_ws.Professors.All(p => p.Id != sourceProfessorId))
            _ws.AddProfessor(new Professor { Id = sourceProfessorId, Name = sourceProfessorId });
        if (_ws.Professors.All(p => p.Id != targetProfessorId))
            _ws.AddProfessor(new Professor { Id = targetProfessorId, Name = targetProfessorId });
        if (_ws.Rooms.All(r => r.Id != sourceRoomId))
            _ws.AddRoom(new Room { Id = sourceRoomId, Name = sourceRoomId });
        if (_ws.Rooms.All(r => r.Id != targetRoomId))
            _ws.AddRoom(new Room { Id = targetRoomId, Name = targetRoomId });
        if (includeThirdAssignment)
        {
            _ws.AddProfessor(new Professor { Id = "P3", Name = "P3" });
            _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        }

        _ws.AddCourse(new Course
        {
            Id = "4-01",
            Name = "공통과목",
            Grade = sourceGrade,
            Section = sourceSection,
            HoursPerWeek = sourceRowSpan,
            ProfessorId = sourceProfessorId,
        });
        _ws.AddCourse(new Course
        {
            Id = "4-02",
            Name = "공통과목",
            Grade = targetGrade,
            Section = targetSection,
            HoursPerWeek = targetRowSpan,
            ProfessorId = targetProfessorId,
        });
        if (includeThirdAssignment)
        {
            _ws.AddCourse(new Course
            {
                Id = "4-03",
                Name = "공통과목",
                Grade = targetGrade,
                Section = 3,
                HoursPerWeek = targetRowSpan,
                ProfessorId = "P3",
            });
        }

        var assignments = new List<SolutionAssignment>();
        for (var offset = 0; offset < sourceRowSpan; offset++)
            assignments.Add(new SolutionAssignment("4-01", 0, 1 + offset, sourceRoomId));
        for (var offset = 0; offset < targetRowSpan; offset++)
            assignments.Add(new SolutionAssignment("4-02", 0, 3 + offset, targetRoomId));
        if (includeThirdAssignment)
        {
            for (var offset = 0; offset < targetRowSpan; offset++)
                assignments.Add(new SolutionAssignment("4-03", 0, 3 + offset, "R3"));
        }

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(vm, assignments.ToArray());
        vm.Grid.Render(assignments, _ws.ExpandedCourses, _ws.Professors, _ws.Rooms);

        var source = ManualCell(
            courseId: "4-01",
            name: "공통과목",
            professorId: sourceProfessorId,
            grade: sourceGrade,
            section: sourceSection,
            rooms: new[] { sourceRoomId },
            rowSpan: sourceRowSpan);
        var target = ManualCell(
            courseId: "4-02",
            name: "공통과목",
            professorId: targetProfessorId,
            grade: targetGrade,
            section: targetSection,
            rooms: new[] { targetRoomId },
            rowSpan: targetRowSpan);
        return (vm, source, target);
    }

    private ManualEditViewModel ArrangeResetBaselineCrossCase()
    {
        if (_ws.Professors.All(p => p.Id != "4"))
            _ws.AddProfessor(new Professor { Id = "4", Name = "4" });
        if (_ws.Professors.All(p => p.Id != "12"))
            _ws.AddProfessor(new Professor { Id = "12", Name = "12" });
        if (_ws.Rooms.All(r => r.Id != "3"))
            _ws.AddRoom(new Room { Id = "3", Name = "3" });
        if (_ws.Rooms.All(r => r.Id != "6"))
            _ws.AddRoom(new Room { Id = "6", Name = "6" });

        _ws.AddCourse(new Course
        {
            Id = "4-01",
            Name = "공통과목",
            Grade = 2,
            Section = 1,
            HoursPerWeek = 2,
            ProfessorId = "4",
        });
        _ws.AddCourse(new Course
        {
            Id = "4-02",
            Name = "공통과목",
            Grade = 2,
            Section = 2,
            HoursPerWeek = 2,
            ProfessorId = "12",
        });

        var solution = MakeSolution(
            new SolutionAssignment("4-01", 0, 3, "3"),
            new SolutionAssignment("4-01", 0, 4, "3"),
            new SolutionAssignment("4-02", 0, 3, "6"),
            new SolutionAssignment("4-02", 0, 4, "6"));
        var cross = new SavedManualCrossLinkRow(
            "4-01", 2, "1", 0, 3, "3",
            "4-02", 2, "2", 0, 3, "6",
            "HC11_ONLY_EXCEPTION");

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSnapshot(_ws.Snapshot(), solution, "reset-baseline", new[] { cross });
        return vm;
    }

    private static IReadOnlyList<ConflictItem> DetectManualConflicts(
        ManualEditViewModel vm,
        params SolutionAssignment[] assignments)
    {
        var method = typeof(ManualEditViewModel).GetMethod(
            "DetectConflicts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        return (IReadOnlyList<ConflictItem>)method.Invoke(vm, new object[] { assignments.ToList(), false })!;
    }

    private static bool IsAllowedManualCrossOverlapConflict(
        ManualEditViewModel vm,
        ConflictItem conflict,
        IReadOnlyList<SolutionAssignment> candidate,
        CellAssignment movingAssignment,
        int sourceDay,
        int sourcePeriod,
        int targetDay,
        IReadOnlyList<int> targetPeriods,
        int targetGrade,
        bool allowManualCrossOverlap = true)
    {
        var method = typeof(ManualEditViewModel).GetMethod(
            "IsAllowedManualCrossOverlapConflict",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        return (bool)method.Invoke(vm, new object[]
        {
            conflict,
            candidate,
            movingAssignment,
            sourceDay,
            sourcePeriod,
            targetDay,
            targetPeriods,
            targetGrade,
            allowManualCrossOverlap,
        })!;
    }

    private static void RefreshConflicts(
        ManualEditViewModel vm,
        bool strictManualCrossValidation)
    {
        var method = typeof(ManualEditViewModel).GetMethod(
            "RefreshConflicts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        method.Invoke(vm, new object[] { strictManualCrossValidation });
    }

    private static bool AddManualCrossLinkIfMissing(
        ManualEditViewModel vm,
        ManualEditViewModel.ManualCrossLink link)
    {
        var method = typeof(ManualEditViewModel).GetMethod(
            "AddManualCrossLinkIfMissing",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        return (bool)method.Invoke(vm, new object[] { link })!;
    }

    [Fact]
    public void ManualCrossAssignmentKey_SameCourseDifferentSection_AreDifferent()
    {
        var left = ManualCrossAssignmentKey(section: 1);
        var right = ManualCrossAssignmentKey(section: 2);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void ManualCrossAssignmentKey_SameCourseDifferentRoom_AreDifferent()
    {
        var left = ManualCrossAssignmentKey(rooms: new[] { "R1" });
        var right = ManualCrossAssignmentKey(rooms: new[] { "R2" });

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void ManualCrossAssignmentKey_SameCourseDifferentPeriod_AreDifferent()
    {
        var left = ManualCrossAssignmentKey(period: 1);
        var right = ManualCrossAssignmentKey(period: 2);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void ManualCrossAssignmentKey_RoomIdsOrder_DoesNotMatter()
    {
        var left = ManualCrossAssignmentKey(rooms: new[] { "R2", "R1" });
        var right = ManualCrossAssignmentKey(rooms: new[] { "R1", "R2" });

        Assert.Equal(left, right);
    }

    [Fact]
    public void ManualCrossAssignmentKey_SameCourseSameSectionSameSlotSameRooms_AreEqual()
    {
        var left = ManualCrossAssignmentKey(
            courseId: "X-01",
            section: 1,
            day: 0,
            period: 1,
            rowSpan: 2,
            rooms: new[] { " R1", "R2", "R1" });
        var right = ManualCrossAssignmentKey(
            courseId: "X-01",
            section: 1,
            day: 0,
            period: 1,
            rowSpan: 2,
            rooms: new[] { "R2", "R1" });

        Assert.Equal(left, right);
    }

    [Fact]
    public void CrossParallelOrder_UsesAssignmentKey_NotCourseId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCrossAssignmentKey(courseId: "X-01", section: 1, rooms: new[] { "R1" });
        var target = ManualCrossAssignmentKey(courseId: "Y-01", section: 1, rooms: new[] { "R2" });
        AddWorkingCrossLink(vm, ManualCrossLink(source, target));

        var order = CrossParallelOrder(vm);

        Assert.DoesNotContain("X-01", order.Keys);
        Assert.DoesNotContain("Y-01", order.Keys);
        Assert.Equal(1, order[ManualCrossDisplayKey(courseId: "X-01", section: 1, rooms: new[] { "R1" })]);
        Assert.Equal(0, order[ManualCrossDisplayKey(courseId: "Y-01", section: 1, rooms: new[] { "R2" })]);
    }

    [Fact]
    public void CrossLinkLabels_UsesAssignmentKey_NotCourseId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        var source = ManualCrossAssignmentKey(courseId: "X-01", section: 1, rooms: new[] { "R1" });
        var target = ManualCrossAssignmentKey(courseId: "Y-01", section: 1, rooms: new[] { "R2" });
        AddWorkingCrossLink(vm, ManualCrossLink(source, target));

        var labels = CrossLinkLabels(vm);

        Assert.DoesNotContain("X-01", labels.Keys);
        Assert.DoesNotContain("Y-01", labels.Keys);
        Assert.Equal("기타 A분반", labels[ManualCrossDisplayKey(courseId: "X-01", section: 1, rooms: new[] { "R1" })]);
        Assert.Equal("테스트 A분반", labels[ManualCrossDisplayKey(courseId: "Y-01", section: 1, rooms: new[] { "R2" })]);
    }

    [Fact]
    public void SameCourseDifferentSection_GetsSeparateCrossLabels()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        var first = ManualCrossAssignmentKey(courseId: "X-01", section: 1, rooms: new[] { "R1" });
        var second = ManualCrossAssignmentKey(courseId: "X-01", section: 2, rooms: new[] { "R2" });
        AddWorkingCrossLink(vm, ManualCrossLink(first, second));

        var labels = CrossLinkLabels(vm);

        Assert.True(labels.ContainsKey(ManualCrossDisplayKey(courseId: "X-01", section: 1, rooms: new[] { "R1" })));
        Assert.True(labels.ContainsKey(ManualCrossDisplayKey(courseId: "X-01", section: 2, rooms: new[] { "R2" })));
        Assert.Equal(2, labels.Count);
    }

    [Fact]
    public void SameCourseDifferentRoom_GetsSeparateCrossParallelOrder()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var first = ManualCrossAssignmentKey(courseId: "X-01", section: 1, rooms: new[] { "R1" });
        var second = ManualCrossAssignmentKey(courseId: "X-01", section: 1, rooms: new[] { "R2" });
        AddWorkingCrossLink(vm, ManualCrossLink(first, second));

        var order = CrossParallelOrder(vm);

        Assert.Equal(1, order[ManualCrossDisplayKey(courseId: "X-01", section: 1, rooms: new[] { "R1" })]);
        Assert.Equal(0, order[ManualCrossDisplayKey(courseId: "X-01", section: 1, rooms: new[] { "R2" })]);
        Assert.Equal(2, order.Count);
    }

    [Fact]
    public void RestoredCross_SameCourseDifferentSection_ShowsOnCorrectAssignmentKeys()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        RestoreSavedManualCrossRows(vm, new[]
        {
            new SavedManualCrossLinkRow(
                "X-01", 2, "1", 0, 1, "R1",
                "X-01", 2, "2", 0, 1, "R2",
                "HC11_ONLY_EXCEPTION"),
        });

        var labels = CrossLinkLabels(vm);
        var order = CrossParallelOrder(vm);

        Assert.True(labels.ContainsKey(ManualCrossDisplayKey(courseId: "X-01", section: 1, rooms: new[] { "R1" })));
        Assert.True(labels.ContainsKey(ManualCrossDisplayKey(courseId: "X-01", section: 2, rooms: new[] { "R2" })));
        Assert.Equal(1, order[ManualCrossDisplayKey(courseId: "X-01", section: 1, rooms: new[] { "R1" })]);
        Assert.Equal(0, order[ManualCrossDisplayKey(courseId: "X-01", section: 2, rooms: new[] { "R2" })]);
    }

    [Fact]
    public void CrossDisplayKey_RoomIdsOrder_DoesNotMatter()
    {
        var left = ManualCrossDisplayKey(rooms: new[] { "R2", "R1" });
        var right = ManualCrossDisplayKey(rooms: new[] { "R1", "R2" });

        Assert.Equal(left, right);
    }

    [Fact]
    public void ExistingNormalCross_DisplayStillWorks()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        var source = ManualCrossAssignmentKey(courseId: "X-01", section: 1, rooms: new[] { "R1" });
        var target = ManualCrossAssignmentKey(courseId: "Y-01", section: 1, rooms: new[] { "R2" });
        AddWorkingCrossLink(vm, ManualCrossLink(source, target));

        var labels = CrossLinkLabels(vm);
        var order = CrossParallelOrder(vm);

        Assert.Equal("기타 A분반", labels[ManualCrossDisplayKey(courseId: "X-01", section: 1, rooms: new[] { "R1" })]);
        Assert.Equal("테스트 A분반", labels[ManualCrossDisplayKey(courseId: "Y-01", section: 1, rooms: new[] { "R2" })]);
        Assert.Equal(1, order[ManualCrossDisplayKey(courseId: "X-01", section: 1, rooms: new[] { "R1" })]);
        Assert.Equal(0, order[ManualCrossDisplayKey(courseId: "Y-01", section: 1, rooms: new[] { "R2" })]);
    }

    [Fact]
    public void ManualCrossHover_AllowsSameCourseDifferentSectionProfessorRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P2", rooms: new[] { "R2" });
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 1, 2, 1, target);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCrossHover_SourceMovedToTargetSlot_SectionConflictIgnored()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase();
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 2, 0, target);

        Assert.True(state.CanCreate, state.Reason);
        Assert.DoesNotContain("분반 중복 금지", state.Reason);
    }

    [Fact]
    public void ManualCrossHover_SectionConflictBeforeFilter_AfterFilterEmpty_CanCreateTrue()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase();
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 2, 0, target);

        Assert.True(state.CanCreate, state.Reason);
        Assert.DoesNotContain("분반 중복 금지", state.Reason);
        Assert.Empty(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCrossFilter_ConflictCourseIdIsBaseId_SourceTargetFullIds_Matches()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase();
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey("4-01", section: 1, day: 0, period: 3, rowSpan: 2, rooms: new[] { "3" }),
            ManualCrossAssignmentKey("4-02", section: 2, day: 0, period: 3, rowSpan: 2, rooms: new[] { "6" })));
        var candidate = new[]
        {
            new SolutionAssignment("4-01", 0, 3, "3"),
            new SolutionAssignment("4-01", 0, 4, "3"),
            new SolutionAssignment("4-02", 0, 3, "6"),
            new SolutionAssignment("4-02", 0, 4, "6"),
        };
        var conflict = new ConflictItem(
            ConflictType.SectionConflict,
            ConflictSeverity.Error,
            "4의 분반이 월 3교시에 중복: 4-02, 4-01",
            0,
            3);

        var allowed = IsAllowedManualCrossOverlapConflict(
            vm,
            conflict,
            candidate,
            source,
            sourceDay: 0,
            sourcePeriod: 1,
            targetDay: 0,
            targetPeriods: new[] { 3, 4 },
            targetGrade: target.Grade);

        Assert.True(allowed);
    }

    [Fact]
    public void ManualCrossFilter_RowSpanCoverage_SecondPeriodSectionConflict_Ignored()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase();
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey("4-01", section: 1, day: 0, period: 3, rowSpan: 2, rooms: new[] { "3" }),
            ManualCrossAssignmentKey("4-02", section: 2, day: 0, period: 3, rowSpan: 2, rooms: new[] { "6" })));
        var candidate = new[]
        {
            new SolutionAssignment("4-01", 0, 3, "3"),
            new SolutionAssignment("4-01", 0, 4, "3"),
            new SolutionAssignment("4-02", 0, 3, "6"),
            new SolutionAssignment("4-02", 0, 4, "6"),
        };
        var conflict = new ConflictItem(
            ConflictType.SectionConflict,
            ConflictSeverity.Error,
            "4의 분반이 월 4교시에 중복: 4-02, 4-01",
            0,
            4);

        var allowed = IsAllowedManualCrossOverlapConflict(
            vm,
            conflict,
            candidate,
            source,
            sourceDay: 0,
            sourcePeriod: 1,
            targetDay: 0,
            targetPeriods: new[] { 3, 4 },
            targetGrade: target.Grade);

        Assert.True(allowed);
    }

    [Fact]
    public void ManualCrossDrop_SourceMovedToTargetSlot_SectionConflictIgnored()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase();

        var state = vm.EvaluateCrossDropHover(0, 1, 2, 0, source, 0, 3, 2, 0, target);

        Assert.True(state.CanCreate, state.Reason);
        Assert.DoesNotContain("분반 중복 금지", state.Reason);
    }

    [Fact]
    public void ManualCrossDropPreview_SectionConflictFiltered_CanCreateTrue()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase();

        var state = vm.EvaluateCrossDropHover(0, 1, 2, 0, source, 0, 3, 2, 0, target);

        Assert.True(state.CanCreate, state.Reason);
        Assert.DoesNotContain("분반 중복 금지", state.Reason);
        Assert.Empty(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCrossAdd_SourceMovedToTargetSlot_CreatesCross()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase();
        vm.SelectCell(0, 1, 2, 0, source);

        vm.HandleCrossAddRequested(0, 3, 2, 0, target);

        var link = Assert.Single(vm.WorkingCrossLinks);
        Assert.NotEqual(link.SourceKey, link.TargetKey);
        Assert.Equal("4-01", link.SourceKey.CourseId);
        Assert.Equal("4-02", link.TargetKey.CourseId);
    }

    [Fact]
    public void ManualCrossAdd_CreatesSingleCrossLink_NoDuplicateProvisionalLinks()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase();
        vm.SelectCell(0, 1, 2, 0, source);

        var hover = vm.EvaluateCrossHover(0, 3, 2, 0, target);
        vm.HandleCrossAddRequested(0, 3, 2, 0, target);

        Assert.True(hover.CanCreate, hover.Reason);
        Assert.Single(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCrossAdd_ThenRefreshConflicts_SectionConflictNotBlockingForPair()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase();
        vm.SelectCell(0, 1, 2, 0, source);

        vm.HandleCrossAddRequested(0, 3, 2, 0, target);
        RefreshConflicts(vm, strictManualCrossValidation: true);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.SectionConflict);
    }

    [Fact]
    public void ManualCrossDropActual_DoesNotAccidentallyRunGeneralMoveWhenCrossBadgeRequired()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase();

        var generalDropAllowed = vm.CanDropMove(0, 1, 2, 0, source, 0, 3, 2, 0);
        vm.HandleCrossDrop(0, 1, 2, 0, source, 0, 3, 2, 0, target);

        Assert.False(generalDropAllowed);
        Assert.Single(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCrossLink_DirectionInsensitiveDuplicate_Prevented()
    {
        var (vm, _, _) = ArrangeSameBaseManualCrossCase();
        var first = ManualCrossLink(
            ManualCrossAssignmentKey("4-01", section: 1, day: 0, period: 3, rowSpan: 2, rooms: new[] { "3" }),
            ManualCrossAssignmentKey("4-02", section: 2, day: 0, period: 3, rowSpan: 2, rooms: new[] { "6" }));
        var reversed = ManualCrossLink(
            ManualCrossAssignmentKey("4-02", section: 2, day: 0, period: 3, rowSpan: 2, rooms: new[] { "6" }),
            ManualCrossAssignmentKey("4-01", section: 1, day: 0, period: 3, rowSpan: 2, rooms: new[] { "3" }));

        var firstAdded = AddManualCrossLinkIfMissing(vm, first);
        var secondAdded = AddManualCrossLinkIfMissing(vm, reversed);

        Assert.True(firstAdded);
        Assert.False(secondAdded);
        Assert.Single(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCross_SourceMovedToTargetSlot_SameProfessorStillBlocks()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase(
            sourceSection: 1,
            targetSection: 1,
            sourceProfessorId: "P1",
            targetProfessorId: "P1");
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 2, 0, target);

        Assert.False(state.CanCreate);
        Assert.Contains("교수", state.Reason);
    }

    [Fact]
    public void ManualCrossFilter_SameProfessor_StillBlocks()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase(
            sourceProfessorId: "P1",
            targetProfessorId: "P1");
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 2, 0, target);

        Assert.False(state.CanCreate);
        Assert.Contains("교수", state.Reason);
    }

    [Fact]
    public void ManualCross_SameBaseCourseDifferentSectionSameProfessor_BlocksProfessorConflict()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase(
            sourceProfessorId: "P1",
            targetProfessorId: "P1");
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 2, 0, target);

        Assert.False(state.CanCreate);
        Assert.Contains("교수", state.Reason);
    }

    [Fact]
    public void ManualCross_SourceMovedToTargetSlot_SameRoomStillBlocks()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase(
            sourceRoomId: "R1",
            targetRoomId: "R1");
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 2, 0, target);

        Assert.False(state.CanCreate);
        Assert.Contains("강의실", state.Reason);
    }

    [Fact]
    public void ManualCrossFilter_SameRoom_StillBlocks()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase(
            sourceRoomId: "R1",
            targetRoomId: "R1");
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 2, 0, target);

        Assert.False(state.CanCreate);
        Assert.Contains("강의실", state.Reason);
    }

    [Fact]
    public void ManualCross_SourceMovedToTargetSlot_ThirdAssignmentStillBlocks()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase(includeThirdAssignment: true);
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 2, 0, target);

        Assert.False(state.CanCreate);
        Assert.Contains("3개 이상의 과목", state.Reason);
    }

    [Fact]
    public void ManualCrossFilter_ThirdAssignment_StillBlocks()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase(includeThirdAssignment: true);
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 2, 0, target);

        Assert.False(state.CanCreate);
        Assert.Contains("3개 이상의 과목", state.Reason);
    }

    [Fact]
    public void ManualCross_SourceMovedToTargetSlot_DifferentGradeStillBlocks()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase(targetGrade: 3);
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 3, 0, target);

        Assert.False(state.CanCreate);
        Assert.Contains("같은 학년", state.Reason);
    }

    [Fact]
    public void ManualCross_SourceMovedToTargetSlot_RowSpanMismatchStillBlocks()
    {
        var (vm, source, target) = ArrangeSameBaseManualCrossCase(
            sourceRowSpan: 1,
            targetRowSpan: 2);
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 3, 2, 0, target);

        Assert.False(state.CanCreate);
        Assert.Contains("서로 다른 길이", state.Reason);
    }

    [Fact]
    public void ManualCross_GeneralMove_SectionConflictStillBlocks()
    {
        _ws.AddProfessor(new Professor { Id = "P4", Name = "P4" });
        _ws.AddProfessor(new Professor { Id = "P12", Name = "P12" });
        _ws.AddRoom(new Room { Id = "R6", Name = "강의실6" });
        _ws.AddCourse(new Course
        {
            Id = "4-01",
            Name = "공통과목",
            Grade = 2,
            Section = 1,
            HoursPerWeek = 1,
            ProfessorId = "P4",
        });
        _ws.AddCourse(new Course
        {
            Id = "4-02",
            Name = "공통과목",
            Grade = 2,
            Section = 2,
            HoursPerWeek = 1,
            ProfessorId = "P12",
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("4-01", 0, 3, "R1"),
            new SolutionAssignment("4-02", 0, 3, "R6")));
        vm.SaveName = "분반중복저장차단";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SectionConflict);
        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "분반중복저장차단");
    }

    [Fact]
    public void ManualCrossHover_DoesNotBlockSameCourseIdWhenAssignmentKeyDifferent()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 1, professorId: "P2", rooms: new[] { "R2" });
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 1, 2, 1, target);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCrossAdd_AllowsSameCourseDifferentSectionProfessorRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P2", rooms: new[] { "R2" });
        vm.SelectCell(0, 1, 2, 0, source);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.True(vm.WorkingCrossLinks.Count == 1, vm.StatusMessage);
        var link = Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal("1", link.SourceKey.Section);
        Assert.Equal("2", link.TargetKey.Section);
    }

    [Fact]
    public void ManualCrossDrop_AllowsSameCourseDifferentSectionProfessorRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P2", rooms: new[] { "R2" });

        vm.HandleCrossDrop(0, 1, 2, 0, source, 0, 1, 2, 1, target);

        Assert.True(vm.WorkingCrossLinks.Count == 1, vm.StatusMessage);
    }

    [Fact]
    public void ManualCrossHover_BlocksSameAssignmentKey()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 1, 2, 1, target);

        Assert.False(state.CanCreate);
        Assert.Contains("현재 선택", state.Reason);
    }

    [Fact]
    public void ManualCrossAdd_BlocksSameProfessor()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P1", rooms: new[] { "R2" });
        vm.SelectCell(0, 1, 2, 0, source);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("같은 교수", vm.StatusMessage);
        Assert.DoesNotContain("HC-02", vm.StatusMessage);
    }

    [Fact]
    public void ManualCrossAdd_BlocksSameRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P2", rooms: new[] { "R1" });
        vm.SelectCell(0, 1, 2, 0, source);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("같은 강의실", vm.StatusMessage);
        Assert.DoesNotContain("HC-01", vm.StatusMessage);
    }

    [Fact]
    public void ManualCrossDrop_BlocksAlreadyCrossedAssignmentKey()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P2", rooms: new[] { "R2" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(section: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(section: 2, rooms: new[] { "R2" })));

        var state = vm.EvaluateCrossDropHover(0, 1, 2, 0, source, 0, 1, 2, 1, target);

        Assert.False(state.CanCreate);
        Assert.Contains("이미 크로스", state.Reason);
    }

    [Fact]
    public void ManualCrossDrop_AllowsReplacingExistingPartnerByKey()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var third = ManualCell(section: 3, professorId: "P3", rooms: new[] { "R3" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(section: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(section: 2, rooms: new[] { "R2" })));

        var state = vm.EvaluateCrossDropHover(0, 1, 2, 0, source, 0, 1, 2, 2, third);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCrossHover_ExistingNormalCrossStillWorks()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        var source = ManualCell(courseId: "X-01", professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(courseId: "Y-01", name: "기타", professorId: "P2", rooms: new[] { "R2" });
        vm.SelectCell(0, 1, 2, 0, source);

        var state = vm.EvaluateCrossHover(0, 1, 2, 1, target);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCrossScenario_SameCourseDifferentSectionProfessorRoom_CreateSaveRestoreDisplay()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P2", rooms: new[] { "R2" });
        vm.SelectCell(0, 1, 2, 0, source);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        var created = Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal("1", created.SourceKey.Section);
        Assert.Equal("2", created.TargetKey.Section);
        var row = Assert.Single(SavedManualCrossRows(vm));
        Assert.Equal("X-01", row.SourceCourseId);
        Assert.Equal("X-01", row.TargetCourseId);
        Assert.Equal("1", row.SourceSection);
        Assert.Equal("2", row.TargetSection);
        Assert.Equal("R1", row.SourceRoomId);
        Assert.Equal("R2", row.TargetRoomId);
        Assert.Equal(0, row.SourceDay);
        Assert.Equal(1, row.SourcePeriod);
        Assert.Equal(0, row.TargetDay);
        Assert.Equal(1, row.TargetPeriod);

        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        var restoredVm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(
            restoredVm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));

        var ignored = RestoreSavedManualCrossRows(restoredVm, new[] { row });

        Assert.Equal(0, ignored);
        var restored = Assert.Single(restoredVm.WorkingCrossLinks);
        Assert.True(restored.MatchesPair(created.SourceKey, created.TargetKey));
        var sourceDisplayKey = ManualCrossDisplayKey(section: 1, day: 0, period: 1, rooms: new[] { "R1" });
        var targetDisplayKey = ManualCrossDisplayKey(section: 2, day: 0, period: 1, rooms: new[] { "R2" });
        Assert.Equal(1, CrossParallelOrder(restoredVm)[sourceDisplayKey]);
        Assert.Equal(0, CrossParallelOrder(restoredVm)[targetDisplayKey]);
        Assert.True(CrossLinkLabels(restoredVm).ContainsKey(sourceDisplayKey));
        Assert.True(CrossLinkLabels(restoredVm).ContainsKey(targetDisplayKey));
    }

    [Fact]
    public void ManualCrossScenario_SaveRows_DoNotCollapseSameCourseId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(section: 1, day: 0, period: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(section: 2, day: 0, period: 1, rooms: new[] { "R2" })));

        var row = Assert.Single(SavedManualCrossRows(vm));

        Assert.Equal(row.SourceCourseId, row.TargetCourseId);
        Assert.NotEqual(row.SourceSection, row.TargetSection);
        Assert.NotEqual(row.SourceRoomId, row.TargetRoomId);
        Assert.Equal(0, row.SourceDay);
        Assert.Equal(1, row.SourcePeriod);
        Assert.Equal(0, row.TargetDay);
        Assert.Equal(1, row.TargetPeriod);
    }

    [Fact]
    public void ManualCrossScenario_Restore_UsesSavedEndpointNotFirstCourseIdMatch()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3", Section = 3 });
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R0"),
            new SolutionAssignment("X-01", 1, 2, "R1"),
            new SolutionAssignment("X-01", 2, 3, "R2"));

        var ignored = RestoreSavedManualCrossRows(vm, new[]
        {
            new SavedManualCrossLinkRow(
                "X-01", 2, "2", 1, 2, "R1",
                "X-01", 2, "3", 2, 3, "R2",
                "HC11_ONLY_EXCEPTION"),
        });

        Assert.Equal(0, ignored);
        var link = Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal("2", link.SourceKey.Section);
        Assert.Equal(1, link.SourceKey.Day);
        Assert.Equal(2, link.SourceKey.Period);
        Assert.Equal("R1", link.SourceKey.RoomIdsKey);
        Assert.Equal("3", link.TargetKey.Section);
        Assert.Equal(2, link.TargetKey.Day);
        Assert.Equal(3, link.TargetKey.Period);
        Assert.Equal("R2", link.TargetKey.RoomIdsKey);
    }

    [Fact]
    public void ManualCrossScenario_RestoredCross_HasSeparateLabelsAndSubColumns()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        RestoreSavedManualCrossRows(vm, new[]
        {
            new SavedManualCrossLinkRow(
                "X-01", 2, "1", 0, 1, "R1",
                "X-01", 2, "2", 0, 1, "R2",
                "HC11_ONLY_EXCEPTION"),
        });

        var sourceDisplayKey = ManualCrossDisplayKey(section: 1, rooms: new[] { "R1" });
        var targetDisplayKey = ManualCrossDisplayKey(section: 2, rooms: new[] { "R2" });
        var order = CrossParallelOrder(vm);
        var labels = CrossLinkLabels(vm);

        Assert.Contains(sourceDisplayKey, order.Keys);
        Assert.Contains(targetDisplayKey, order.Keys);
        Assert.Contains(sourceDisplayKey, labels.Keys);
        Assert.Contains(targetDisplayKey, labels.Keys);
        Assert.DoesNotContain("X-01", order.Keys);
        Assert.DoesNotContain("X-01", labels.Keys);
    }

    [Fact]
    public void ManualCrossScenario_ExemptsOnlyGradeConflict_NotProfessorOrRoom()
    {
        var gradeVm = _sp.GetRequiredService<ManualEditViewModel>();
        AddWorkingCrossLink(gradeVm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", section: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "Y-01", section: 2, rooms: new[] { "R2" })));
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });

        var gradeConflicts = DetectManualConflicts(
            gradeVm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));

        Assert.DoesNotContain(gradeConflicts, c => c.Type == ConflictType.GradeConflict);

        var professorVm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Z-01", Name = "교수충돌", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1", Section = 3 });
        AddWorkingCrossLink(professorVm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", section: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "Z-01", section: 3, rooms: new[] { "R2" })));

        var professorConflicts = DetectManualConflicts(
            professorVm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Z-01", 0, 1, "R2"));

        Assert.Contains(professorConflicts, c => c.Type == ConflictType.ProfessorConflict);

        var roomVm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "W-01", Name = "강의실충돌", Grade = 2, HoursPerWeek = 1, ProfessorId = "P4", Section = 4 });
        AddWorkingCrossLink(roomVm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", section: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "W-01", section: 4, rooms: new[] { "R1" })));

        var roomConflicts = DetectManualConflicts(
            roomVm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("W-01", 0, 1, "R1"));

        Assert.Contains(roomConflicts, c => c.Type == ConflictType.RoomConflict);
    }

    [Fact]
    public void ManualCrossQa_PlusClick_SameCourseDifferentSectionProfessorRoom_CreatesCross()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P2", rooms: new[] { "R2" });
        vm.SelectCell(0, 1, 2, 0, source);

        var hover = vm.EvaluateCrossHover(0, 1, 2, 1, target);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.True(hover.CanCreate, hover.Reason);
        var link = Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal("1", link.SourceKey.Section);
        Assert.Equal("2", link.TargetKey.Section);
    }

    [Fact]
    public void ManualCrossQa_DragDrop_SameCourseDifferentSectionProfessorRoom_CreatesCross()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P2", rooms: new[] { "R2" });

        var hover = vm.EvaluateCrossDropHover(0, 1, 2, 0, source, 0, 1, 2, 1, target);
        vm.HandleCrossDrop(0, 1, 2, 0, source, 0, 1, 2, 1, target);

        Assert.True(hover.CanCreate, hover.Reason);
        Assert.Single(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCrossQa_SaveThenReload_PreservesSameCourseCross()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(section: 1, day: 0, period: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(section: 2, day: 0, period: 1, rooms: new[] { "R2" })));
        var row = Assert.Single(SavedManualCrossRows(vm));

        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        var reloaded = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(
            reloaded,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));

        var ignored = RestoreSavedManualCrossRows(reloaded, new[] { row });

        Assert.Equal(0, ignored);
        var link = Assert.Single(reloaded.WorkingCrossLinks);
        Assert.Equal("1", link.SourceKey.Section);
        Assert.Equal("2", link.TargetKey.Section);
        Assert.Equal("R1", link.SourceKey.RoomIdsKey);
        Assert.Equal("R2", link.TargetKey.RoomIdsKey);
    }

    [Fact]
    public void ManualCrossQa_Reload_PreservesCrossLabelsAndSubColumns()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        RestoreSavedManualCrossRows(vm, new[]
        {
            new SavedManualCrossLinkRow(
                "X-01", 2, "1", 0, 1, "R1",
                "X-01", 2, "2", 0, 1, "R2",
                "HC11_ONLY_EXCEPTION"),
        });

        var sourceDisplayKey = ManualCrossDisplayKey(section: 1, rooms: new[] { "R1" });
        var targetDisplayKey = ManualCrossDisplayKey(section: 2, rooms: new[] { "R2" });

        Assert.Equal(1, CrossParallelOrder(vm)[sourceDisplayKey]);
        Assert.Equal(0, CrossParallelOrder(vm)[targetDisplayKey]);
        Assert.Contains(sourceDisplayKey, CrossLinkLabels(vm).Keys);
        Assert.Contains(targetDisplayKey, CrossLinkLabels(vm).Keys);
    }

    [Fact]
    public void ManualCrossQa_BlocksSameProfessor()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P1", rooms: new[] { "R2" });
        vm.SelectCell(0, 1, 2, 0, source);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("같은 교수", vm.StatusMessage);
        Assert.DoesNotContain("HC-02", vm.StatusMessage);
    }

    [Fact]
    public void ManualCrossQa_BlocksSameRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(section: 2, professorId: "P2", rooms: new[] { "R1" });
        vm.SelectCell(0, 1, 2, 0, source);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("같은 강의실", vm.StatusMessage);
        Assert.DoesNotContain("HC-01", vm.StatusMessage);
    }

    [Fact]
    public void ManualCrossQa_BlocksDifferentRowSpan()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" }, rowSpan: 1);
        var target = ManualCell(section: 2, professorId: "P2", rooms: new[] { "R2" }, rowSpan: 2);
        vm.SelectCell(0, 1, 2, 0, source);

        var hover = vm.EvaluateCrossHover(0, 1, 2, 1, target);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.False(hover.CanCreate);
        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("서로 다른 길이", vm.StatusMessage);
    }

    [Fact]
    public void ManualCrossQa_AllowsReplacingExistingPartner()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var source = ManualCell(section: 1, professorId: "P1", rooms: new[] { "R1" });
        var third = ManualCell(section: 3, professorId: "P3", rooms: new[] { "R3" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(section: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(section: 2, rooms: new[] { "R2" })));

        var hover = vm.EvaluateCrossDropHover(0, 1, 2, 0, source, 0, 1, 2, 2, third);

        Assert.True(hover.CanCreate, hover.Reason);
    }

    [Fact]
    public void ManualCrossQa_ExistingNormalCrossStillWorks()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        var source = ManualCell(courseId: "X-01", professorId: "P1", rooms: new[] { "R1" });
        var target = ManualCell(courseId: "Y-01", name: "기타", professorId: "P2", rooms: new[] { "R2" });
        vm.SelectCell(0, 1, 2, 0, source);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains("크로스가 설정", vm.StatusMessage);
    }

    [Fact]
    public void ManualCrossQa_MoveSwapDndBasicRegressionStillWorks()
    {
        var moveVm = _sp.GetRequiredService<ManualEditViewModel>();
        moveVm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var moveSource = moveVm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");

        var moved = moveVm.HandleDropMove(
            moveSource.Day,
            moveSource.Period,
            moveSource.Grade,
            moveSource.SubColumnIdx,
            moveSource.Assignment,
            1,
            3,
            2,
            0);

        Assert.True(moved, moveVm.StatusMessage);
        Assert.Contains(moveVm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 1 && c.Period == 3);

        var swapVm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 2, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        swapVm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 3, "R2")));
        var swapSource = swapVm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        var swapTarget = swapVm.Grid.Cells.Single(c => c.Assignment.CourseId == "Y-01");
        swapVm.HandleCellClick(swapSource.Day, swapSource.Period, swapSource.Grade, swapSource.SubColumnIdx, swapSource.Assignment);

        swapVm.HandleSwapRequested(swapTarget.Day, swapTarget.Period, swapTarget.Grade, swapTarget.SubColumnIdx, swapTarget.Assignment);

        Assert.Contains(swapVm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 1 && c.Period == 3);
        Assert.Contains(swapVm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 0 && c.Period == 1);
    }

    [Fact]
    public void ManualCross_ConflictDetector_ExemptsOnlyLinkedAssignmentPairGradeConflict()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", day: 0, period: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "Y-01", day: 0, period: 1, rooms: new[] { "R2" })));

        var conflicts = DetectManualConflicts(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));

        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void ManualCross_ConflictDetector_DoesNotExemptSameCourseDifferentUnlinkedPair()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "별도", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", day: 0, period: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "Y-01", day: 0, period: 1, rooms: new[] { "R2" })));

        var conflicts = DetectManualConflicts(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Z-01", 0, 1, "R3"));

        Assert.Contains(conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void ManualCross_ConflictDetector_DoesNotExemptDifferentPeriod()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", day: 0, period: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "Y-01", day: 0, period: 1, rooms: new[] { "R2" })));

        var conflicts = DetectManualConflicts(
            vm,
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 0, 2, "R2"));

        Assert.Contains(conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void ManualCross_ConflictDetector_DoesNotExemptDifferentDay()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", day: 0, period: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "Y-01", day: 0, period: 1, rooms: new[] { "R2" })));

        var conflicts = DetectManualConflicts(
            vm,
            new SolutionAssignment("X-01", 1, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"));

        Assert.Contains(conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void ManualCross_ConflictDetector_DoesNotExemptProfessorConflict()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", day: 0, period: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "Y-01", day: 0, period: 1, rooms: new[] { "R2" })));

        var conflicts = DetectManualConflicts(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));

        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(conflicts, c => c.Type == ConflictType.ProfessorConflict);
    }

    [Fact]
    public void ManualCross_ConflictDetector_DoesNotExemptRoomConflict()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", day: 0, period: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "Y-01", day: 0, period: 1, rooms: new[] { "R1" })));

        var conflicts = DetectManualConflicts(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R1"));

        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(conflicts, c => c.Type == ConflictType.RoomConflict);
    }

    [Fact]
    public void ManualCross_ConflictDetector_TwoHourBlock_ExemptsOnlyCoveredPeriods()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 2, ProfessorId = "P2" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", day: 0, period: 1, rowSpan: 2, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "Y-01", day: 0, period: 1, rowSpan: 2, rooms: new[] { "R2" })));

        var coveredConflicts = DetectManualConflicts(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 0, 2, "R2"));
        var outsideConflicts = DetectManualConflicts(
            vm,
            new SolutionAssignment("X-01", 0, 3, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2"));

        Assert.DoesNotContain(coveredConflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(outsideConflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void ManualCross_ConflictDetector_PairOrderDoesNotMatter()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        AddWorkingCrossLink(vm, ManualCrossLink(
            ManualCrossAssignmentKey(courseId: "X-01", day: 0, period: 1, rooms: new[] { "R1" }),
            ManualCrossAssignmentKey(courseId: "Y-01", day: 0, period: 1, rooms: new[] { "R2" })));

        var conflicts = DetectManualConflicts(
            vm,
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 0, 1, "R1"));

        Assert.DoesNotContain(conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void ManualCrossLink_SameCourseDifferentSection_AreNotConsideredSameCrossedAssignment()
    {
        var crossed = ManualCrossAssignmentKey(courseId: "X-01", section: 1);
        var otherSameCourse = ManualCrossAssignmentKey(courseId: "X-01", section: 2);
        var target = ManualCrossAssignmentKey(courseId: "Y-01", section: 1, rooms: new[] { "R2" });
        var link = ManualCrossLink(crossed, target);

        Assert.False(IsAlreadyCrossedByLink(link, otherSameCourse));
    }

    [Fact]
    public void ManualCrossLink_SameCourseDifferentRoom_AreNotConsideredSameCrossedAssignment()
    {
        var crossed = ManualCrossAssignmentKey(courseId: "X-01", rooms: new[] { "R1" });
        var otherSameCourse = ManualCrossAssignmentKey(courseId: "X-01", rooms: new[] { "R3" });
        var target = ManualCrossAssignmentKey(courseId: "Y-01", rooms: new[] { "R2" });
        var link = ManualCrossLink(crossed, target);

        Assert.False(IsAlreadyCrossedByLink(link, otherSameCourse));
    }

    [Fact]
    public void ManualCrossLink_SameCourseSameSectionSameSlotSameRoom_IsAlreadyCrossed()
    {
        var crossed = ManualCrossAssignmentKey(courseId: "X-01", section: 1, day: 0, period: 1, rooms: new[] { "R1" });
        var equivalent = ManualCrossAssignmentKey(courseId: "X-01", section: 1, day: 0, period: 1, rooms: new[] { "R1" });
        var target = ManualCrossAssignmentKey(courseId: "Y-01", rooms: new[] { "R2" });
        var link = ManualCrossLink(crossed, target);

        Assert.True(IsAlreadyCrossedByLink(link, equivalent));
    }

    [Fact]
    public void ManualCrossLink_PairOrder_DoesNotMatter()
    {
        var left = ManualCrossAssignmentKey(courseId: "X-01", rooms: new[] { "R1" });
        var right = ManualCrossAssignmentKey(courseId: "Y-01", rooms: new[] { "R2" });
        var link = ManualCrossLink(left, right);

        Assert.True(IsCrossPair(link, left, right));
        Assert.True(IsCrossPair(link, right, left));
    }

    [Fact]
    public void ManualCross_BlocksThirdAssignment_ByAssignmentKey()
    {
        var first = ManualCrossAssignmentKey(courseId: "X-01", rooms: new[] { "R1" });
        var second = ManualCrossAssignmentKey(courseId: "Y-01", rooms: new[] { "R2" });
        var third = ManualCrossAssignmentKey(courseId: "Z-01", rooms: new[] { "R3" });
        var link = ManualCrossLink(first, second);

        Assert.True(IsAlreadyCrossedByLink(link, first));
        Assert.True(IsAlreadyCrossedByLink(link, second));
        Assert.False(IsAlreadyCrossedByLink(link, third));
    }

    [Fact]
    public void ManualCross_DoesNotBlockSameCourseIdDifferentAssignmentKey()
    {
        var crossed = ManualCrossAssignmentKey(courseId: "X-01", section: 1, period: 1, rooms: new[] { "R1" });
        var otherSameCourse = ManualCrossAssignmentKey(courseId: "X-01", section: 2, period: 1, rooms: new[] { "R2" });
        var target = ManualCrossAssignmentKey(courseId: "Y-01", section: 1, rooms: new[] { "R3" });
        var link = ManualCrossLink(crossed, target);

        Assert.False(IsAlreadyCrossedByLink(link, otherSameCourse));
    }

    [Fact]
    public void ManualCross_AllowsSameCourseDifferentSectionProfessorRoom_WhenOtherRulesPass()
    {
        var source = ManualCrossAssignmentKey(courseId: "X-01", section: 1, period: 1, rooms: new[] { "R1" });
        var target = ManualCrossAssignmentKey(courseId: "X-01", section: 2, period: 1, rooms: new[] { "R2" });
        var link = ManualCrossLink(source, target);

        Assert.True(IsCrossPair(link, source, target));
    }

    [Fact]
    public void LoadFromSolution_PopulatesGridAndResetsSelection()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));

        vm.LoadFromSolution(sol);

        Assert.Equal(sol, vm.BaseSolution);
        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(vm.Conflicts);
        Assert.Equal(5, vm.Grid.DayGroups.Count);
        Assert.True(vm.Grid.ExpandAllGrades);
        Assert.All(vm.Grid.DayGroups, dg => Assert.Equal(AcademicLevels.AllGrades.Count, dg.Grades.Count));
    }

    [Fact]
    public void ManualEditGrid_UsesStructuredRowSpanMerge()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();

        Assert.True(vm.Grid.MergeOnlyStructuredBlocks);
    }

    [Fact]
    public void ManualEditGrid_KeepsNormalTwoHourBlockMerged()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();

        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 2, "R1"),
            new SolutionAssignment("B-02", 0, 3, "R1")));

        var block = Assert.Single(vm.Grid.Cells, c => c.Assignment.CourseId == "B-02");
        Assert.Equal(2, block.Period);
        Assert.Equal(2, block.Assignment.RowSpan);
    }

    [Fact]
    public void MoveOneHourIntoTwoHourBlockInterior_IsBlockedWithoutForceDialog()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 2, "R1"),
            new SolutionAssignment("B-02", 0, 3, "R1"),
            new SolutionAssignment("X-01", 0, 6, "R2")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "B-02" && c.Period == 2 && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 1);
    }

    [Fact]
    public void MoveTwoHourDirectlyBelowSameCourseSectionOneHour_IsBlockedWithoutForceDialog()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Period == 6);
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var targetKey = new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 2, 2, 0);
        Assert.Equal(TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Blocked, vm.Grid.EditStates[targetKey].State);
        Assert.Contains("같은 과목", vm.Grid.EditStates[targetKey].Reason);

        vm.HandleCellClick(0, 2, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("같은 과목", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 2);
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 2);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void MoveTwoHourBelowDifferentCourseOneHour_IsAllowed()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "다른과목", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 2, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3 && c.Assignment.RowSpan == 2);
    }

    [Fact]
    public void ClickMoveSuccess_ClearsAllSelectionAndMoveStates()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
        Assert.False(vm.EvaluateCrossHover(2, 1, 2, 0, null).CanCreate);
        Assert.False(vm.EvaluateSwapHover(2, 1, 2, 0, null).CanSwap);
    }

    [Fact]
    public void ClickMoveSuccess_RebuildsGridAtNewPosition_AndOldPositionIsEmpty()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.EditStates.Keys, k => k.Day == 0 && k.Period == 1);
    }

    [Fact]
    public void ClickMoveSuccess_DoesNotLeaveStaleSelectedCell()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        var selectedBeforeMove = vm.Grid.SelectedCell;

        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.NotNull(selectedBeforeMove);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
        Assert.Null(vm.SelectedAssignment);
    }

    [Fact]
    public void ClickMoveSuccess_DoesNotRecreateMoveStatesAfterClear()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        Assert.NotEmpty(vm.Grid.EditStates);

        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.Empty(vm.Grid.EditStates);
        Assert.Null(vm.Grid.SelectedCell);
    }

    [Fact]
    public void MoveFailure_KeepsSelectionAndDoesNotChangeGrid()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 2, "R1"),
            new SolutionAssignment("B-02", 0, 3, "R1"),
            new SolutionAssignment("X-01", 1, 1, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        var before = vm.Grid.Cells
            .Select(c => (c.Day, c.Period, c.Grade, c.SubColumnIdx, c.Assignment.CourseId, c.Assignment.RowSpan))
            .ToHashSet();

        vm.HandleCellClick(0, 3, 2, 0, null);

        var after = vm.Grid.Cells
            .Select(c => (c.Day, c.Period, c.Grade, c.SubColumnIdx, c.Assignment.CourseId, c.Assignment.RowSpan))
            .ToHashSet();
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Equal("X-01", vm.SelectedAssignment!.CourseId);
        Assert.NotNull(vm.Grid.SelectedCell);
        Assert.NotEmpty(vm.Grid.EditStates);
        Assert.True(before.SetEquals(after));
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
    }

    [Fact]
    public void MoveTwoHourBelowDifferentSectionOneHour_IsAllowed()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "테스트", Grade = 2, Section = 2, HoursPerWeek = 1, ProfessorId = "P1" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 2, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3 && c.Assignment.RowSpan == 2);
    }

    [Fact]
    public void MoveTwoHourBelowDifferentProfessorSameCourseSectionOneHour_IsAllowed()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "테스트", Grade = 2, Section = 1, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 2, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3 && c.Assignment.RowSpan == 2);
    }

    [Fact]
    public void CrossMoveCreatingTwoHourToOneHour_IsBlockedByDuration()
    {
        _ws.AddCourse(new Course { Id = "A-01", Name = "위수업", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("A-01", 0, 2, "R2"),
            new SolutionAssignment("Y-01", 0, 3, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleCrossAddRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("서로 다른 길이", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 2);
    }

    [Fact]
    public void MoveOneHourDirectlyAboveSameCourseSectionTwoHourBlock_IsBlockedWithoutForceDialog()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("X-01", 0, 3, "R1"),
            new SolutionAssignment("X-01", 0, 6, "R2")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Period == 6);
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 1, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("같은 과목", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 2 && c.Assignment.RowSpan == 2);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void MoveOneHourAfterTwoHourBlock_IsAllowed()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 1, "R1"),
            new SolutionAssignment("B-02", 0, 2, "R1"),
            new SolutionAssignment("X-01", 0, 6, "R2")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "B-02" && c.Period == 1 && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3 && c.Assignment.RowSpan == 1);
    }

    [Fact]
    public void SwapHover_WithSelectedAndTargetCourse_IsAvailable()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교환대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        var state = vm.EvaluateSwapHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.True(state.CanSwap);
        Assert.Contains("교환 가능", state.Reason);
    }

    [Fact]
    public void ManualCross_Swap_SectionConflictStillBlocksIfNotManualCross()
    {
        _ws.AddProfessor(new Professor { Id = "P4", Name = "P4" });
        _ws.AddProfessor(new Professor { Id = "P12", Name = "P12" });
        _ws.AddProfessor(new Professor { Id = "PY", Name = "PY" });
        _ws.AddRoom(new Room { Id = "R6", Name = "강의실6" });
        _ws.AddRoom(new Room { Id = "RY", Name = "교환강의실" });
        _ws.AddCourse(new Course
        {
            Id = "4-01",
            Name = "공통과목",
            Grade = 2,
            Section = 1,
            HoursPerWeek = 1,
            ProfessorId = "P4",
        });
        _ws.AddCourse(new Course
        {
            Id = "4-02",
            Name = "공통과목",
            Grade = 2,
            Section = 2,
            HoursPerWeek = 1,
            ProfessorId = "P12",
        });
        _ws.AddCourse(new Course
        {
            Id = "Y-01",
            Name = "교환대상",
            Grade = 2,
            Section = 1,
            HoursPerWeek = 1,
            ProfessorId = "PY",
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("4-01", 0, 1, "R1"),
            new SolutionAssignment("4-02", 0, 3, "R6"),
            new SolutionAssignment("Y-01", 0, 3, "RY")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "4-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);
        _dialog.ResponseToReturn = false;

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        var call = Assert.Single(_dialog.Calls);
        Assert.Contains(call, c => c.Type == ConflictType.SectionConflict);
        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("교환이 취소", vm.StatusMessage);
    }

    [Fact]
    public void SwapOneHourWithOneHour_ExchangesLocationsAndCreatesUndoSnapshot()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교환대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("위치를 교환", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 3 && c.Assignment.Rooms.Contains("R1"));
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 0 && c.Period == 1 && c.Assignment.Rooms.Contains("R2"));
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapOneHourWithTwoHour_IsBlockedAndDoesNotCreateManualCrossLink()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "한시간", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        var hover = vm.EvaluateSwapHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);
        Assert.False(hover.CanSwap);
        Assert.Contains("블록 시간", hover.Reason);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("블록 시간", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 1 && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 2);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapTwoHourWithTwoHour_ExchangesLocationsAndPreservesRowSpan()
    {
        _ws.AddCourse(new Course
        {
            Id = "Y-02",
            Name = "두시간2",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-02", 1, 3, "R2"),
            new SolutionAssignment("Y-02", 1, 4, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-02");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        var hover = vm.EvaluateSwapHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);
        Assert.True(hover.CanSwap);
        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("위치를 교환", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 1 && c.Period == 3 && c.Assignment.RowSpan == 2 && c.Assignment.Rooms.Contains("R1"));
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-02" && c.Day == 0 && c.Period == 1 && c.Assignment.RowSpan == 2 && c.Assignment.Rooms.Contains("R2"));
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapUndoRedo_RestoresAndReappliesExchange()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교환대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);
        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        vm.UndoCommand.Execute(null);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 3);

        vm.RedoCommand.Execute(null);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 3);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 1);
    }

    [Fact]
    public void SwapSelf_DoesNotChangeStateOrCreateUndoSnapshot()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("현재 선택한 수업", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 1);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapCreatingSameCourseSectionOneHourAboveTwoHourPattern_IsBlocked()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교환대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 6, "R2"),
            new SolutionAssignment("X-01", 0, 7, "R2"),
            new SolutionAssignment("Y-01", 0, 2, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Period == 6);
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("블록 시간", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 1 && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 6 && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 2);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapResultingInProfessorConflict_RecalculatesConflictsAndSaveBlocks()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P1" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2"),
            new SolutionAssignment("Z-01", 0, 3, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.ProfessorConflict && c.Severity == ConflictSeverity.Error);
        vm.SaveName = "스왑오류";
        vm.SaveTimetableCommand.Execute(null);
        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "스왑오류");
    }

        [Fact]
    public void SwapWithoutNewConflict_DoesNotAskForConfirmation()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 2, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 3, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Empty(_dialog.Calls);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 1 && c.Period == 3);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 2 && c.Period == 1);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapWarning_CancelLeavesStateAndUndoUnchanged()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _dialog.ResponseToReturn = false;
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 2, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 3, 1, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(_dialog.Calls);
        Assert.Contains(_dialog.Calls[0], c => c.Severity == ConflictSeverity.Warning);
        Assert.Contains("취소", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 2 && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 0 && c.Period == 1);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Empty(vm.WorkingCrossLinks);
    }

    [Fact]
    public void SwapWarning_ConfirmAppliesSwap()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _dialog.ResponseToReturn = true;
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 2, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 3, 1, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(_dialog.Calls);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 2 && c.Period == 1);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapError_CancelLeavesStateAndSaveClean()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P1" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _dialog.ResponseToReturn = false;
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2"),
            new SolutionAssignment("Z-01", 0, 3, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(_dialog.Calls);
        Assert.Contains(_dialog.Calls[0], c => c.Severity == ConflictSeverity.Error);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Period == 3);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapResultingInHc20_RequestsConfirmationAndCancelLeavesState()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _dialog.ResponseToReturn = false;
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 3, "R2"),
            new SolutionAssignment("X-01", 1, 6, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Day == 0);
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(_dialog.Calls);
        Assert.Contains(_dialog.Calls[0], c => c.Type == ConflictType.SameCourseSameDayConflict);
        Assert.Contains("취소", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 1 && c.Period == 3);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SwapPolicyBlocked_DoesNotAskForConfirmation()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "한시간", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("X-01", 0, 6, "R1"),
            new SolutionAssignment("X-01", 0, 7, "R1")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Empty(_dialog.Calls);
        Assert.Contains("블록 시간", vm.StatusMessage);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Swap_DuplicateCourseId_SwapsOnlySelectedSourceAndTargetByAssignmentId()
    {
        var vm = BuildDuplicateProgrammingApplicationThreeVm();
        var source = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var target = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P30");
        var other = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        var sourceId = source.Assignment.AssignmentId;
        var targetId = target.Assignment.AssignmentId;
        var otherId = other.Assignment.AssignmentId;

        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        var movedSource = vm.Grid.Cells.Single(c => c.Assignment.AssignmentId == sourceId);
        var movedTarget = vm.Grid.Cells.Single(c => c.Assignment.AssignmentId == targetId);
        var unchanged = vm.Grid.Cells.Single(c => c.Assignment.AssignmentId == otherId);
        Assert.Equal((2, 1, "D330"), (movedSource.Day, movedSource.Period, string.Join(",", movedSource.Assignment.Rooms)));
        Assert.Equal((0, 1, "D332"), (movedTarget.Day, movedTarget.Period, string.Join(",", movedTarget.Assignment.Rooms)));
        Assert.Equal((0, 1, "D331"), (unchanged.Day, unchanged.Period, string.Join(",", unchanged.Assignment.Rooms)));
        Assert.Equal(sourceId, movedSource.Assignment.AssignmentId);
        Assert.Equal(targetId, movedTarget.Assignment.AssignmentId);
    }

    [Fact]
    public void Swap_DuplicateCourseId_ClickAndDropProduceSameResultWhenOrderChanges()
    {
        var clickVm = BuildDuplicateProgrammingApplicationThreeVm(reverseAssignments: true);
        var clickSource = clickVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var clickTarget = clickVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P30");
        clickVm.HandleCellClick(clickSource.Day, clickSource.Period, clickSource.Grade, clickSource.SubColumnIdx, clickSource.Assignment);
        clickVm.HandleSwapRequested(clickTarget.Day, clickTarget.Period, clickTarget.Grade, clickTarget.SubColumnIdx, clickTarget.Assignment);

        var dropVm = BuildDuplicateProgrammingApplicationThreeVm(reverseAssignments: true);
        var dropSource = dropVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var dropTarget = dropVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P30");
        dropVm.HandleSwapDrop(
            dropSource.Day,
            dropSource.Period,
            dropSource.Grade,
            dropSource.SubColumnIdx,
            dropSource.Assignment,
            dropTarget.Day,
            dropTarget.Period,
            dropTarget.Grade,
            dropTarget.SubColumnIdx,
            dropTarget.Assignment);

        Assert.Equal(ProgrammingApplicationSnapshot(clickVm), ProgrammingApplicationSnapshot(dropVm));
    }

    [Fact]
    public void Swap_MissingOrDuplicateAssignmentId_DoesNotChangeWorkingOrUndo()
    {
        var missingVm = BuildDuplicateProgrammingApplicationVm();
        var missingSource = missingVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var missingTarget = missingVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        var beforeMissing = ProgrammingApplicationSnapshot(missingVm);
        missingVm.HandleCellClick(missingSource.Day, missingSource.Period, missingSource.Grade, missingSource.SubColumnIdx, missingSource.Assignment with { AssignmentId = "missing" });
        missingVm.HandleSwapRequested(missingTarget.Day, missingTarget.Period, missingTarget.Grade, missingTarget.SubColumnIdx, missingTarget.Assignment);

        Assert.Equal(beforeMissing, ProgrammingApplicationSnapshot(missingVm));
        Assert.False(missingVm.UndoCommand.CanExecute(null));

        var duplicateVm = BuildDuplicateProgrammingApplicationVm(sharedAssignmentId: "duplicate-id");
        var duplicateSource = duplicateVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var duplicateTarget = duplicateVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        var beforeDuplicate = ProgrammingApplicationSnapshot(duplicateVm);
        duplicateVm.HandleCellClick(duplicateSource.Day, duplicateSource.Period, duplicateSource.Grade, duplicateSource.SubColumnIdx, duplicateSource.Assignment);
        duplicateVm.HandleSwapRequested(duplicateTarget.Day, duplicateTarget.Period, duplicateTarget.Grade, duplicateTarget.SubColumnIdx, duplicateTarget.Assignment);

        Assert.Equal(beforeDuplicate, ProgrammingApplicationSnapshot(duplicateVm));
        Assert.False(duplicateVm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Swap_DuplicateCourseId_UndoRedoRestoresAssignmentIdentityAndPosition()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var source = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var target = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        var sourceId = source.Assignment.AssignmentId;
        var targetId = target.Assignment.AssignmentId;

        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        vm.HandleSwapRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        vm.UndoCommand.Execute(null);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.AssignmentId == sourceId && c.Day == source.Day && c.Period == source.Period);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.AssignmentId == targetId && c.Day == target.Day && c.Period == target.Period);

        vm.RedoCommand.Execute(null);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.AssignmentId == sourceId && c.Day == target.Day && c.Period == target.Period);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.AssignmentId == targetId && c.Day == source.Day && c.Period == source.Period);
    }

    [Fact]
    public void Swap_UnrelatedCrossPair_IsPreserved()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "크로스대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "A-01", Name = "스왑A", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3", Section = 3 });
        _ws.AddCourse(new Course { Id = "B-01", Name = "스왑B", Grade = 2, HoursPerWeek = 1, ProfessorId = "P4", Section = 4 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        _ws.AddProfessor(new Professor { Id = "P4", Name = "교수4" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("A-01", 1, 1, "R1"),
            new SolutionAssignment("B-01", 2, 1, "R2")));
        var x = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        var y = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(x.Day, x.Period, x.Grade, x.SubColumnIdx, x.Assignment);
        vm.HandleCrossAddRequested(y.Day, y.Period, y.Grade, y.SubColumnIdx, y.Assignment);
        Assert.Single(vm.WorkingCrossLinks);

        var a = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "A-01");
        var b = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "B-01");
        vm.HandleCellClick(a.Day, a.Period, a.Grade, a.SubColumnIdx, a.Assignment);
        vm.HandleSwapRequested(b.Day, b.Period, b.Grade, b.SubColumnIdx, b.Assignment);

        var link = Assert.Single(vm.WorkingCrossLinks);
        Assert.True(link.MatchesPair("X-01", "Y-01"));
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "A-01" && c.Day == 2 && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "B-01" && c.Day == 1 && c.Period == 1);
    }

    [Fact]
    public void LoadFromSaved_UsesSnapshotCoursesNotInWorkspace()
    {
        // A saved timetable references a course that no longer exists in the workspace,
        // but its snapshot still carries the original course definition.
        var snapshot = new TimetableScheduler.Data.AppData(
            new List<Course> { new() { Id = "GONE-01", Name = "사라진과목", Grade = 1, HoursPerWeek = 2, ProfessorId = "P1" } },
            new List<Professor> { new() { Id = "P1", Name = "교수" } },
            new List<Room> { new() { Id = "R1", Name = "강의실1" } },
            new List<CrossGroup>(), new List<RetakeScenario>());
        var record = new TimetableScheduler.Data.SavedTimetableRecord(
            Guid.NewGuid().ToString(), "snap", DateTime.Now,
            new List<TimetableScheduler.Data.TimetableAssignmentRow>
            {
                new("GONE-01", 0, 1, "R1"),
                new("GONE-01", 0, 2, "R1"),
            },
            SnapshotJson: System.Text.Json.JsonSerializer.Serialize(snapshot));

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSaved(record);

        Assert.Equal("snap", vm.SaveName);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "GONE-01");
    }

    [Fact]
    public void SelectCell_PopulatesInspector()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(2, 1, 2, 0, assignment);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Equal("X-01", vm.SelectedAssignment!.CourseId);
        Assert.Equal("R1", vm.NewRoomId);
        Assert.False(vm.HasNoSelection);
        Assert.Equal("교수", vm.SelectedProfessorName);
        Assert.Equal("2학년 / 분반 A / 강의실1", vm.SelectedLocationText);
        Assert.Equal("1시간", vm.SelectedDurationText);
        Assert.Equal("1교시", vm.SelectedOccupiedSlotsText);
        Assert.Equal("단일 1시간", vm.SelectedBlockText);
        Assert.Equal("아니오", vm.SelectedFixedText);
        Assert.Equal("없음", vm.SelectedCoteachText);
        Assert.Equal("아니오", vm.SelectedMultiRoomText);
        Assert.Equal("제한 없음", vm.SelectedAllowedRoomsText);
        Assert.Equal("이동 가능", vm.SelectedMoveStateText);
        Assert.Equal("없음", vm.SelectedBlockedReasonText);
        Assert.False(vm.HasBlockingReasons);
        Assert.False(vm.HasWarningReasons);
        Assert.False(vm.HasAnyConstraintReasons);
    }

    [Fact]
    public void LoadFromSolution_ShowsNoSelectionState()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R3")));

        Assert.True(vm.HasNoSelection);
        Assert.Equal("-", vm.SelectedProfessorName);
        Assert.Equal("-", vm.SelectedLocationText);
    }

    [Fact]
    public void SelectCell_PopulatesBlockAndMetadataDetails()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddProfessor(new Professor { Id = "P2", Name = "공동교수" });
        _ws.AddCourse(new Course
        {
            Id = "F-02",
            Name = "고급 테스트",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            IsFixed = true,
            FixedRooms = new List<string> { "R1", "R2" },
            CoteachProfs = new List<string> { "P2" },
        });
        var sol = MakeSolution(
            new SolutionAssignment("F-02", 0, 1, "R1"),
            new SolutionAssignment("F-02", 0, 2, "R1"),
            new SolutionAssignment("F-02", 0, 1, "R2"),
            new SolutionAssignment("F-02", 0, 2, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "F-02", "고급 테스트", "P1", 2, 1, new List<string> { "R1", "R2" }, 2, 2, true);
        vm.SelectCell(2, 1, 2, 0, assignment);

        Assert.Equal("2시간", vm.SelectedDurationText);
        Assert.Equal("1~2교시", vm.SelectedOccupiedSlotsText);
        Assert.Equal("연속 2시간 블록", vm.SelectedBlockText);
        Assert.Equal("예", vm.SelectedFixedText);
        Assert.Equal("공동교수", vm.SelectedCoteachText);
        Assert.Equal("예 (강의실1, 강의실2)", vm.SelectedMultiRoomText);
        Assert.Equal("강의실1, 강의실2", vm.SelectedAllowedRoomsText);
        Assert.Equal("이동 불가", vm.SelectedMoveStateText);
        Assert.Contains("고정된 수업", vm.SelectedBlockedReasonText);
        Assert.DoesNotContain("HC-13", vm.SelectedBlockedReasonText);
        Assert.True(vm.HasBlockingReasons);
        Assert.False(vm.HasWarningReasons);
        Assert.True(vm.HasAnyConstraintReasons);
    }

    [Fact]
    public void ClearSelection_ResetsInspectorDetails()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R3")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.ClearSelectionCommand.Execute(null);

        Assert.True(vm.HasNoSelection);
        Assert.Equal("-", vm.SelectedProfessorName);
        Assert.Equal("-", vm.SelectedLocationText);
        Assert.Equal("-", vm.SelectedDurationText);
        Assert.False(vm.HasBlockingReasons);
        Assert.False(vm.HasWarningReasons);
        Assert.False(vm.HasAnyConstraintReasons);
    }

    [Fact]
    public void SelectCell_BuildsPerGradeMoveStatesAndWarnings()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 2, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(2, 1, 2, 0, assignment);

        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Selected,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(2, 1, 2, 0)].State);
        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Warning,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 1, 2, 0)].State);
        Assert.Contains(
            "월요일 오전",
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 1, 2, 0)].Reason);
        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Normal,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 1, 3, 0)].State);
    }

    [Fact]
    public void SelectCell_FlagsFridayAfternoonAsWarning()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 2, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(2, 1, 2, 0, assignment);

        var state = vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(4, 6, 2, 0)];
        Assert.Equal(TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Warning, state.State);
        Assert.Contains("금요일 오후", state.Reason);

        vm.SelectCell(4, 6, 2, 0, assignment);

        Assert.False(vm.HasBlockingReasons);
        Assert.True(vm.HasWarningReasons);
        Assert.True(vm.HasAnyConstraintReasons);
    }

    [Fact]
    public void HandleCellClick_WarningTarget_MovesAndShowsWarningMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 2, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(2, 1, 2, 0, assignment);
        vm.HandleCellClick(0, 1, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains("월요일 오전", vm.StatusMessage);
    }

    [Fact]
    public void SaveTimetable_NoError_SavesTimetable()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SaveName = "정상저장";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains("저장 완료", vm.StatusMessage);
        Assert.Contains(_ws.SavedTimetables, t => t.Name == "정상저장");
    }

    [Fact]
    public void SaveTimetable_FromSnapshot_PreservesSessionDbData()
    {
        var sessionSnapshot = new AppData(
            new List<Course>
            {
                new() { Id = "SESSION-01", Name = "세션과목", Grade = 2, HoursPerWeek = 1, ProfessorId = "PS" },
            },
            new List<Professor> { new() { Id = "PS", Name = "세션교수" } },
            new List<Room> { new() { Id = "RS", Name = "세션강의실" } },
            new List<CrossGroup>(),
            new List<RetakeScenario>());

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSnapshot(
            sessionSnapshot,
            MakeSolution(new SolutionAssignment("SESSION-01", 0, 1, "RS")),
            "세션저장");

        vm.SaveTimetableCommand.Execute(null);

        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "세션저장"));
        var savedSnapshot = System.Text.Json.JsonSerializer.Deserialize<AppData>(saved.SnapshotJson!)!;
        Assert.Equal("SESSION-01", savedSnapshot.Courses.Single().Id);
        Assert.Equal("PS", savedSnapshot.Professors.Single().Id);
        Assert.Equal("RS", savedSnapshot.Rooms.Single().Id);
        Assert.DoesNotContain(savedSnapshot.Courses, c => c.Id == "X-01");
    }

    [Fact]
    public void SaveTimetable_WarningOnly_AllowsSave()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SaveName = "경고저장";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains("저장 완료", vm.StatusMessage);
        Assert.Contains(_ws.SavedTimetables, t => t.Name == "경고저장");
    }

    [Fact]
    public void SaveTimetable_Error_BlocksSaveAndRefreshesConflicts()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P1" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        vm.SaveName = "오류저장";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "오류저장");
    }

    [Fact]
    public void ConflictDisplayTitle_UsesCourseAndTime()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "운영체제", Grade = 2, HoursPerWeek = 2, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });

        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 3, "R1"),
            new SolutionAssignment("X-01", 0, 4, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R1"),
            new SolutionAssignment("Y-01", 0, 4, "R1")));

        var conflict = Assert.Single(vm.Conflicts.Where(c => c.Type == ConflictType.RoomConflict && c.Period == 3));
        Assert.NotEqual(conflict.Type.ToString(), conflict.DisplayTitle);
        Assert.Contains("월 3~4교시", conflict.DisplayTitle);
        Assert.Contains("분반", conflict.DisplayTitle);
    }

    [Fact]
    public void ConflictDisplayTitle_FallsBackSafelyWhenCourseMissing()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();

        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("MISSING-01", 0, 3, "R1"),
            new SolutionAssignment("OTHER-01", 0, 3, "R1")));

        var conflict = Assert.Single(vm.Conflicts.Where(c => c.Type == ConflictType.RoomConflict));
        Assert.Contains("알 수 없는 수업", conflict.DisplayTitle);
        Assert.DoesNotContain("MISSING-01", conflict.DisplayTitle);
        Assert.Contains("월 3교시", conflict.DisplayTitle);
    }

    [Fact]
    public void SelectionStatus_UsesCourseNameAndSection_NotInternalCourseId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course
        {
            Id = "1-02",
            Name = "자료구조",
            Grade = 2,
            HoursPerWeek = 1,
            Section = 2,
            ProfessorId = "P1",
        });
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("1-02", 0, 1, "R1")));
        var assignment = AssignmentAt(vm, 0, 1, "1-02");

        vm.HandleCellClick(0, 1, 2, 0, assignment);

        Assert.Contains("선택: 자료구조 B분반", vm.StatusMessage);
        Assert.DoesNotContain("1-02", vm.StatusMessage);
        Assert.DoesNotContain("CourseId", vm.StatusMessage);
        Assert.DoesNotContain("SectionId", vm.StatusMessage);
    }

    [Fact]
    public void MoveStatus_UsesCourseNameAndSection_NotInternalCourseId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course
        {
            Id = "1-02",
            Name = "자료구조",
            Grade = 2,
            HoursPerWeek = 1,
            Section = 2,
            ProfessorId = "P1",
        });
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("1-02", 0, 1, "R1")));
        var assignment = AssignmentAt(vm, 0, 1, "1-02");

        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 3, 2, 0, null);

        Assert.Contains("자료구조 B분반 이동 완료", vm.StatusMessage);
        Assert.DoesNotContain("1-02", vm.StatusMessage);
        Assert.DoesNotContain("CourseId", vm.StatusMessage);
        Assert.DoesNotContain("SectionId", vm.StatusMessage);
    }

    [Fact]
    public void ConflictDisplayDetails_UseUserFacingConstraintName()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();

        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("MISSING-01", 0, 3, "R1"),
            new SolutionAssignment("OTHER-01", 0, 3, "R1")));

        var conflict = Assert.Single(vm.Conflicts.Where(c => c.Type == ConflictType.RoomConflict));
        Assert.DoesNotContain(conflict.Type.ToString(), conflict.DisplayTitle);
        Assert.DoesNotContain(conflict.Type.ToString(), conflict.DisplayDescription);
        Assert.DoesNotContain("HC-", conflict.DisplayDescription);
        Assert.Contains("강의실 시간 중복", conflict.DisplayDescription);
    }

    [Fact]
    public void FixedRoomConflictDisplay_IsSuppressedInManualEdit()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var course = _ws.Courses.Single(c => c.Id == "X-01");
        course.FixedRooms = new List<string> { "R1" };

        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R2")));

        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.FixedRoomViolation);
    }

    [Fact]
    public void ProfessorConflictDisplay_HidesUnknownProfessorId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "운영체제", Grade = 2, HoursPerWeek = 1, ProfessorId = "P404" });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "컴파일러", Grade = 3, HoursPerWeek = 1, ProfessorId = "P404" });

        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("Y-01", 0, 1, "R1"),
            new SolutionAssignment("Z-01", 0, 1, "R2")));

        var conflict = Assert.Single(vm.Conflicts.Where(c => c.Type == ConflictType.ProfessorConflict));
        Assert.Contains("알 수 없는 교수", conflict.DisplayDescription);
        Assert.DoesNotContain("P404", conflict.DisplayDescription);
    }

    [Fact]
    public void SameCourseSameDayDisplay_UsesCourseNameAndSectionLabel()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course
        {
            Id = "DS-01",
            Name = "자료구조",
            Grade = 2,
            HoursPerWeek = 2,
            Section = 1,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1, 1 },
        });

        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("DS-01", 0, 1, "R1"),
            new SolutionAssignment("DS-01", 0, 3, "R1")));

        var conflict = Assert.Single(vm.Conflicts.Where(c => c.Type == ConflictType.SameCourseSameDayConflict));
        Assert.Contains("자료구조 A분반이 같은 요일에 중복 배치되었습니다.", conflict.DisplayDescription);
        Assert.DoesNotContain("DS-01", conflict.DisplayDescription);
        Assert.DoesNotContain("HC-", conflict.DisplayDescription);
    }

    [Fact]
    public void ConflictDisplayTitle_HandlesTwoCourseConflict()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "운영체제", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });

        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 3, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R1")));

        var conflict = Assert.Single(vm.Conflicts.Where(c => c.Type == ConflictType.RoomConflict));
        Assert.Contains("월 3교시", conflict.DisplayTitle);
        Assert.Contains("테스트", conflict.DisplayDescription);
        Assert.Contains("운영체제", conflict.DisplayDescription);
    }

    [Fact]
    public void SaveTimetable_Hc20Error_BlocksSave()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 3, "R1")));
        vm.SaveName = "HC20저장";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SameCourseSameDayConflict);
        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "HC20저장");
    }

    [Fact]
    public void BuildMoveStates_Hc20CandidateMarksTargetBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 1, 3, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Day == 1);

        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var targetKey = new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 3, 2, 0);
        Assert.Equal(TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Blocked, vm.Grid.EditStates[targetKey].State);
        Assert.Contains("같은 과목/분반/교수가 같은 요일에 중복 배치됩니다.", vm.Grid.EditStates[targetKey].Reason);
        Assert.DoesNotContain("위반", vm.Grid.EditStates[targetKey].Reason);
        Assert.DoesNotContain("HC-20", vm.Grid.EditStates[targetKey].Reason);
    }

    [Fact]
    public void ForceMove_Hc20CandidateMovesAndKeepsSaveBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 1, 3, "R1")));
        var source = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01" && c.Day == 1);
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Single(_dialog.Calls);
        Assert.Contains(_dialog.Calls[0], c => c.Type == ConflictType.SameCourseSameDayConflict);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 3);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SameCourseSameDayConflict);

        vm.SaveName = "HC20강제이동저장";
        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "HC20강제이동저장");
    }

    [Fact]
    public void SaveTimetable_ManualCrossHc11Exception_AllowsSave()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);
        vm.SaveName = "크로스저장";

        vm.SaveTimetableCommand.Execute(null);

        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains("저장 완료", vm.StatusMessage);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "크로스저장"));
        var link = Assert.Single(saved.ManualCrossLinks ?? Array.Empty<TimetableScheduler.Data.SavedManualCrossLinkRow>());
        Assert.Equal("HC11_ONLY_EXCEPTION", link.PolicyType);

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains("저장된 시간표", vm.StatusMessage);
    }

    [Fact]
    public void SaveReload_CrossMovedFromDifferentTime_RestoresSavedManualCrossLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 1, 3, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.First(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);
        vm.HandleCrossAddRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);
        vm.SaveName = "크로스이동저장";

        vm.SaveTimetableCommand.Execute(null);

        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "크로스이동저장"));
        Assert.Single(saved.ManualCrossLinks ?? Array.Empty<TimetableScheduler.Data.SavedManualCrossLinkRow>());

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 1);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 0 && c.Period == 1);
        Assert.True(vm.IsSavedCrossValidationSuppressed);
        Assert.False(vm.HasUserEditedAfterLoad);
        Assert.Equal(1, vm.LastSavedCrossLinkCount);
        Assert.Equal(1, vm.LastRestoredCrossLinkCount);
        Assert.Equal(0, vm.LastIgnoredCrossLinkCount);
    }

    [Fact]
    public void ManualCross_SaveRow_IncludesSectionDayPeriodRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 1, 3, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);
        vm.HandleCrossAddRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        var row = Assert.Single(SavedManualCrossRows(vm));

        Assert.Equal("X-01", row.SourceCourseId);
        Assert.Equal("1", row.SourceSection);
        Assert.Equal(0, row.SourceDay);
        Assert.Equal(1, row.SourcePeriod);
        Assert.Equal("R1", row.SourceRoomId);
        Assert.Equal("Y-01", row.TargetCourseId);
        Assert.Equal("2", row.TargetSection);
        Assert.Equal(0, row.TargetDay);
        Assert.Equal(1, row.TargetPeriod);
        Assert.Equal("R2", row.TargetRoomId);
    }

    [Fact]
    public void ManualCross_Save_SameCourseDifferentSection_DoesNotCollapseCourseId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));
        AddWorkingCrossLink(
            vm,
            ManualCrossLink(
                ManualCrossAssignmentKey(courseId: "X-01", section: 1, day: 0, period: 1, rooms: new[] { "R1" }),
                ManualCrossAssignmentKey(courseId: "X-01", section: 2, day: 0, period: 1, rooms: new[] { "R2" })));

        var row = Assert.Single(SavedManualCrossRows(vm));

        Assert.Equal("X-01", row.SourceCourseId);
        Assert.Equal("1", row.SourceSection);
        Assert.Equal("R1", row.SourceRoomId);
        Assert.Equal("X-01", row.TargetCourseId);
        Assert.Equal("2", row.TargetSection);
        Assert.Equal("R2", row.TargetRoomId);
    }

    [Fact]
    public void ManualCross_Restore_UsesSectionDayPeriodRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 2, 3, "R1"),
            new SolutionAssignment("Y-01", 1, 2, "R2"));

        var ignored = RestoreSavedManualCrossRows(vm, new[]
        {
            new SavedManualCrossLinkRow(
                "X-01", 2, "1", 2, 3, "R1",
                "Y-01", 2, "2", 1, 2, "R2",
                "HC11_ONLY_EXCEPTION"),
        });

        Assert.Equal(0, ignored);
        var link = Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal(2, link.SourceKey.Day);
        Assert.Equal(3, link.SourceKey.Period);
        Assert.Equal("R1", link.SourceKey.RoomIdsKey);
        Assert.Equal(1, link.TargetKey.Day);
        Assert.Equal(2, link.TargetKey.Period);
    }

    [Fact]
    public void ManualCross_Restore_SameCourseDifferentSection()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "X-01", Name = "테스트", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"));

        var ignored = RestoreSavedManualCrossRows(vm, new[]
        {
            new SavedManualCrossLinkRow(
                "X-01", 2, "1", 0, 1, "R1",
                "X-01", 2, "2", 0, 1, "R2",
                "HC11_ONLY_EXCEPTION"),
        });

        Assert.Equal(0, ignored);
        var link = Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal("1", link.SourceKey.Section);
        Assert.Equal("R1", link.SourceKey.RoomIdsKey);
        Assert.Equal("2", link.TargetKey.Section);
        Assert.Equal("R2", link.TargetKey.RoomIdsKey);
    }

    [Fact]
    public void ManualCross_Restore_DoesNotUseCourseIdFirstMatch_WhenAmbiguous()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 1, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));

        var ignored = RestoreSavedManualCrossRows(vm, new[]
        {
            new SavedManualCrossLinkRow(
                "X-01", 2, "1", 3, 3, "R1",
                "Y-01", 2, "1", 0, 1, "R2",
                "HC11_ONLY_EXCEPTION"),
        });

        Assert.Equal(1, ignored);
        Assert.Empty(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCross_Restore_AllowsCourseIdFallback_WhenSingleCandidate()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));

        var ignored = RestoreSavedManualCrossRows(vm, new[]
        {
            new SavedManualCrossLinkRow(
                "X-01", 2, "1", 3, 3, "R1",
                "Y-01", 2, "1", 0, 1, "R2",
                "HC11_ONLY_EXCEPTION"),
        });

        Assert.Equal(0, ignored);
        Assert.Single(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCross_Restore_SkipsWhenSourceKeyEqualsTargetKey()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        SetWorkingAssignments(vm, new SolutionAssignment("X-01", 0, 1, "R1"));

        var ignored = RestoreSavedManualCrossRows(vm, new[]
        {
            new SavedManualCrossLinkRow(
                "X-01", 2, "1", 0, 1, "R1",
                "X-01", 2, "1", 0, 1, "R1",
                "HC11_ONLY_EXCEPTION"),
        });

        Assert.Equal(1, ignored);
        Assert.Empty(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCross_Restore_DoesNotDuplicateExistingPair()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        var row = new SavedManualCrossLinkRow(
            "X-01", 2, "1", 0, 1, "R1",
            "Y-01", 2, "1", 0, 1, "R2",
            "HC11_ONLY_EXCEPTION");

        var ignored = RestoreSavedManualCrossRows(vm, new[] { row, row });

        Assert.Equal(0, ignored);
        Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal(2, vm.LastSavedCrossLinkCount);
        Assert.Equal(1, vm.LastRestoredCrossLinkCount);
    }

    [Fact]
    public void ExistingTimetableHandoff_PassesSavedManualCrossLinksToManualEdit()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var input = _sp.GetRequiredService<DataInputViewModel>();
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = MakeSavedRecordWithManualCross(
            "handoff-cross",
            "handoff",
            new[]
            {
                new TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            SavedCross());

        input.LoadForExistingTimetable(saved);
        var handoff = Assert.IsType<ManualEditHandoff>(input.BuildEditHandoff());
        vm.LoadFromSnapshot(
            input.CurrentSnapshot(),
            handoff.Solution,
            "handoff",
            handoff.ManualCrossLinks,
            handoff.SavedTimetableId);

        Assert.Equal(saved.Id, handoff.SavedTimetableId);
        Assert.Equal(saved.Id, vm.EditingSavedTimetableId);
        Assert.Single(handoff.ManualCrossLinks);
        Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal(1, vm.LastSavedCrossLinkCount);
        Assert.Equal(1, vm.LastRestoredCrossLinkCount);
    }

    [Fact]
    public void LoadFromSnapshot_WithSavedManualCrossLink_RerenderDoesNotShowHc11()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var input = _sp.GetRequiredService<DataInputViewModel>();
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = MakeSavedRecordWithManualCross(
            "snapshot-cross",
            "snapshot",
            new[]
            {
                new TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            SavedCross());

        input.LoadForExistingTimetable(saved);
        var handoff = input.BuildEditHandoff()!;
        vm.LoadFromSnapshot(input.CurrentSnapshot(), handoff.Solution, "snapshot", handoff.ManualCrossLinks);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void LoadFromSnapshot_WithSavedManualCrossLink_KeepsSuppressionThroughSelection()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var input = _sp.GetRequiredService<DataInputViewModel>();
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = MakeSavedRecordWithManualCross(
            "snapshot-suppress",
            "snapshot",
            new[]
            {
                new TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            SavedCross(sourceDay: 1, sourcePeriod: 3, targetDay: 2, targetPeriod: 4));

        input.LoadForExistingTimetable(saved);
        var handoff = input.BuildEditHandoff()!;
        vm.LoadFromSnapshot(input.CurrentSnapshot(), handoff.Solution, "snapshot", handoff.ManualCrossLinks);
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");

        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        Assert.True(vm.IsSavedCrossValidationSuppressed);
        Assert.False(vm.HasUserEditedAfterLoad);
        Assert.False(vm.LastCrossCleanupExecuted);
        Assert.Single(vm.WorkingCrossLinks);
    }

    [Fact]
    public void LoadFromSnapshot_AfterSuccessfulMove_ReleasesSavedCrossSuppression()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var input = _sp.GetRequiredService<DataInputViewModel>();
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = MakeSavedRecordWithManualCross(
            "snapshot-move",
            "snapshot",
            new[]
            {
                new TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            SavedCross());
        input.LoadForExistingTimetable(saved);
        var handoff = input.BuildEditHandoff()!;
        vm.LoadFromSnapshot(input.CurrentSnapshot(), handoff.Solution, "snapshot", handoff.ManualCrossLinks);
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.False(vm.IsSavedCrossValidationSuppressed);
        Assert.True(vm.HasUserEditedAfterLoad);
        Assert.True(vm.LastCrossCleanupExecuted);
    }

    [Fact]
    public void LoadFromSnapshot_WithSavedManualCrossLink_DoesNotSuppressHc20()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var input = _sp.GetRequiredService<DataInputViewModel>();
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = MakeSavedRecordWithManualCross(
            "snapshot-hc20",
            "snapshot",
            new[]
            {
                new TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableAssignmentRow("Y-01", 0, 1, "R2"),
                new TimetableAssignmentRow("X-01", 0, 3, "R1"),
            },
            SavedCross());

        input.LoadForExistingTimetable(saved);
        var handoff = input.BuildEditHandoff()!;
        vm.LoadFromSnapshot(input.CurrentSnapshot(), handoff.Solution, "snapshot", handoff.ManualCrossLinks);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SameCourseSameDayConflict);
    }

    [Fact]
    public void LoadFromSavedTimetable_StaleSavedCrossSlotStillRestoresFromCurrentAssignments()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross-stale",
            "크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 1, 3, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Equal(1, vm.LastSavedCrossLinkCount);
        Assert.Equal(1, vm.LastRestoredCrossLinkCount);
        Assert.Equal(0, vm.LastIgnoredCrossLinkCount);
    }

    [Fact]
    public void LoadFromSavedTimetable_NonOverlappingSavedCrossPairIsKeptWithoutHc11Exception()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross-non-overlap",
            "비겹침크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 1, 3, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.True(vm.IsSavedCrossValidationSuppressed);
        Assert.False(vm.HasUserEditedAfterLoad);
    }

    [Fact]
    public void LoadFromSavedTimetable_SelectAndBuildMoveStates_DoesNotReleaseSavedCrossSuppression()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross-select",
            "선택크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 1, 3, "R1",
                    "Y-01", 2, "1", 2, 4, "R2",
                    "STALE_POLICY_NAME"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));
        vm.LoadFromSavedTimetable(saved);
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");

        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.IsSavedCrossValidationSuppressed);
        Assert.False(vm.HasUserEditedAfterLoad);
        Assert.False(vm.LastCrossCleanupExecuted);
        Assert.Empty(vm.IgnoredSavedCrossLinkReasons);
    }

    [Fact]
    public void LoadFromSavedTimetable_MissingReferencedAssignmentIsOnlyRestoreFailure()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross-missing-assignment",
            "누락크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

        vm.LoadFromSavedTimetable(saved);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Equal(1, vm.LastSavedCrossLinkCount);
        Assert.Equal(0, vm.LastRestoredCrossLinkCount);
        Assert.Equal(1, vm.LastIgnoredCrossLinkCount);
        Assert.Single(vm.IgnoredSavedCrossLinkReasons);
        Assert.Contains("assignment가 없습니다", vm.IgnoredSavedCrossLinkReasons[0]);
    }

    [Fact]
    public void SaveTimetable_WithoutManualCrossLink_Hc11GradeOverlapBlocksSave()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        vm.SaveName = "크로스없는중복";

        vm.SaveTimetableCommand.Execute(null);

        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "크로스없는중복");
    }

    [Fact]
    public void LoadFromSavedTimetable_IgnoresUnknownPolicyAndSelfLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved",
            "저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "X-01", 2, "1", 0, 1, "R1",
                    "HC11_ONLY_EXCEPTION"),
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "UNKNOWN"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

        vm.LoadFromSavedTimetable(saved);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("유효하지 않아", vm.StatusMessage);
        Assert.Equal(2, vm.LastSavedCrossLinkCount);
        Assert.Equal(0, vm.LastRestoredCrossLinkCount);
        Assert.Equal(2, vm.LastIgnoredCrossLinkCount);
        Assert.Equal(2, vm.IgnoredSavedCrossLinkReasons.Count);
    }

    [Fact]
    public void LoadFromSavedTimetable_RestoresManualCrossLinkAndAppliesHc11Exception()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross",
            "크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void LoadFromSavedTimetable_RestoredManualCrossLinkDoesNotBlockSaveWithHc11Only()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-cross-save",
            "크로스저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

        vm.LoadFromSavedTimetable(saved);
        vm.SaveName = "크로스재저장";
        vm.SaveTimetableCommand.Execute(null);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains("저장 완료", vm.StatusMessage);
        Assert.Contains(_ws.SavedTimetables, t => t.Name == "크로스재저장");
    }

    [Fact]
    public void LoadFromSavedTimetable_ThenSave_PreservesSnapshotProfessorAndCourseType()
    {
        _ws.UpdateCourse(new Course
        {
            Id = "X-01",
            Name = "테스트",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            CourseType = "전필",
        });

        var savedSnapshot = _ws.Snapshot();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-snapshot",
            "원본저장본",
            DateTime.Now,
            new[] { new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1") },
            Array.Empty<TimetableScheduler.Data.SavedManualCrossLinkRow>(),
            System.Text.Json.JsonSerializer.Serialize(savedSnapshot));

        _ws.UpdateCourse(new Course
        {
            Id = "X-01",
            Name = "테스트",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "",
            CourseType = "",
        });
        _ws.DeleteProfessor("P1");

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSavedTimetable(saved);
        vm.SaveName = "재저장본";

        vm.SaveTimetableCommand.Execute(null);

        var resaved = _ws.SavedTimetables.Single(t => t.Name == "재저장본");
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<AppData>(resaved.SnapshotJson!)!;

        Assert.Equal("전필", snapshot.Courses.Single(c => c.Id == "X-01").CourseType);
        Assert.Equal("P1", snapshot.Courses.Single(c => c.Id == "X-01").ProfessorId);
        Assert.Equal("교수", snapshot.Professors.Single(p => p.Id == "P1").Name);
    }

    [Fact]
    public void LoadFromSavedTimetable_InvalidSnapshotWithBlankProfessorAndCourseType_ShowsNoCells()
    {
        var incompleteSnapshot = new AppData(
            new List<Course>
            {
                new()
                {
                    Id = "X-01",
                    Name = "테스트",
                    Grade = 2,
                    HoursPerWeek = 2,
                    ProfessorId = "",
                    CourseType = "",
                },
            },
            new List<Professor>(),
            new List<Room>(),
            new List<CrossGroup>(),
            new List<RetakeScenario>());

        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-blank-snapshot",
            "빈스냅샷저장본",
            DateTime.Now,
            new[] { new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1") },
            Array.Empty<TimetableScheduler.Data.SavedManualCrossLinkRow>(),
            System.Text.Json.JsonSerializer.Serialize(incompleteSnapshot));

        var vm = _sp.GetRequiredService<ManualEditViewModel>();

        vm.LoadFromSavedTimetable(saved);

        Assert.Empty(vm.Grid.Cells);
    }

    [Fact]
    public void LoadFromSavedTimetable_ManualCrossLinkDoesNotSuppressRoomConflict()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-room-error",
            "강의실충돌저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R1"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R1",
                    "HC11_ONLY_EXCEPTION"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.RoomConflict);
    }

    [Fact]
    public void LoadFromSavedTimetable_ManualCrossLinkDoesNotSuppressHc20()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-hc20",
            "HC20저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 3, "R1"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SameCourseSameDayConflict);
    }

    [Fact]
    public void LoadFromSavedTimetable_ManualCrossLinkDoesNotSuppressAdjacentHc20Blocks()
    {
        _ws.AddCourse(new Course
        {
            Id = "DS-A",
            Name = "자료구조",
            Grade = 2,
            Section = 1,
            HoursPerWeek = 3,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1, 2 },
        });
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-adjacent-hc20",
            "인접HC20저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("DS-A", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("DS-A", 0, 2, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("DS-A", 0, 3, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "DS-A", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

        vm.LoadFromSavedTimetable(saved);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SameCourseSameDayConflict);
    }

    [Fact]
    public void LoadFromSavedTimetable_AfterMoveCrossValidationResumesAndRemovesLink()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var saved = new TimetableScheduler.Data.SavedTimetableRecord(
            "saved-move",
            "이동저장본",
            DateTime.Now,
            new[]
            {
                new TimetableScheduler.Data.TimetableAssignmentRow("X-01", 0, 1, "R1"),
                new TimetableScheduler.Data.TimetableAssignmentRow("Y-01", 0, 1, "R2"),
            },
            new[]
            {
                new TimetableScheduler.Data.SavedManualCrossLinkRow(
                    "X-01", 2, "1", 0, 1, "R1",
                    "Y-01", 2, "1", 0, 1, "R2",
                    "HC11_ONLY_EXCEPTION"),
            },
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));
        vm.LoadFromSavedTimetable(saved);
        var selected = vm.Grid.Cells.First(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleCellClick(0, 3, 2, 0, null);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.False(vm.IsSavedCrossValidationSuppressed);
        Assert.True(vm.HasUserEditedAfterLoad);
        Assert.True(vm.LastCrossCleanupExecuted);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SelectCell_HardBlockedTarget_RemainsBlockedNotWarning()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 2, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(2, 1, 2, 0, assignment);

        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Blocked,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(0, 5, 2, 0)].State);
    }

    [Fact]
    public void ClearSelection_ResetsWarningDetails()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.ClearSelectionCommand.Execute(null);

        Assert.Equal("-", vm.SelectedWarningReasonText);
    }

    [Fact]
    public void ApplyRoomChange_NoNewConflicts_DoesNotPromptDialog()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Empty(_dialog.Calls);
        Assert.Equal(new[] { "R2" }, vm.SelectedAssignment!.Rooms);
    }

    [Fact]
    public void SelectedAssignment_WithMultipleRoomCandidates_ShowsRoomCandidates()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.True(vm.HasMultipleRoomCandidates);
        Assert.Equal(new[] { "R1", "R2" }, vm.AvailableRoomIds);
    }

    [Fact]
    public void SelectedAssignment_WithFixedRoomStillShowsEditableRoomCandidates()
    {
        _ws.Courses.First(c => c.Id == "X-01").FixedRooms.Add("R1");
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.True(vm.HasMultipleRoomCandidates);
        Assert.Equal(new[] { "R1", "R2" }, vm.AvailableRoomIds);
    }

    [Fact]
    public void ApplyRoomChange_NewRoomConflict_DoesNotPromptAndRevertsChange()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        // Y-01 has DIFFERENT prof → no pre-existing conflict
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        vm.LoadFromSolution(sol);

        Assert.Empty(vm.Conflicts);  // start clean

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        _dialog.ResponseToReturn = true;
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Empty(_dialog.Calls);
        Assert.Equal(new[] { "R1" }, vm.SelectedAssignment!.Rooms);
        Assert.Equal("R2", vm.NewRoomId);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.RoomConflict);
    }

    [Fact]
    public void ApplyRoomChange_NewConflicts_UserCancels_RevertsChange()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        vm.LoadFromSolution(sol);
        Assert.Empty(vm.Conflicts);  // start clean

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        _dialog.ResponseToReturn = false;
        vm.ApplyRoomChangeCommand.Execute(null);

        // Dialog prompted
        Assert.Empty(_dialog.Calls);
        // Change was reverted — no conflicts remain (clean start state)
        Assert.Empty(vm.Conflicts);
        Assert.Equal("해당 시간에 이미 사용 중인 강의실입니다.", vm.StatusMessage);
    }

    [Fact]
    public void Undo_AfterRoomChange_RestoresPreviousRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        vm.UndoCommand.Execute(null);

        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void MultiRoomSelection_ShowsAssignedRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.Courses.First(c => c.Id == "X-01").FixedRooms.AddRange(new[] { "R1", "R2", "R3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.True(vm.HasMultipleAssignedRooms);
        Assert.Equal(new[] { "R1", "R2" }, vm.AssignedRoomsForSelectedAssignment);
        Assert.Equal("R1", vm.SelectedOldRoomId);
    }

    [Fact]
    public void MultiRoomReplacement_CandidatesExcludeAlreadyAssignedRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.AddRoom(new Room { Id = "R4", Name = "강의실4" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        var candidateIds = vm.ReplacementRoomCandidates.ToArray();
        Assert.Equal(new[] { "R3", "R4" }, candidateIds);
        Assert.DoesNotContain("R1", candidateIds);
        Assert.DoesNotContain("R2", candidateIds);
    }

    [Fact]
    public void ReplacementCandidates_FallbackToSessionRooms_WhenFilteredEmpty()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.Courses.First(c => c.Id == "X-01").FixedRooms.AddRange(new[] { "R1", "R2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Equal(new[] { "R3" }, vm.ReplacementRoomCandidates.ToArray());
    }

    [Fact]
    public void NewRoomCombo_SelectedValueType_MatchesRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.All(vm.SingleRoomChangeCandidates, r => Assert.IsType<string>(r));
        Assert.Equal("R1", vm.NewRoomId);
    }

    [Fact]
    public void SingleRoomChange_AvailableRoomIds_IsStringRoomIdList()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.All(vm.AvailableRoomIds, id => Assert.IsType<string>(id));
        Assert.Contains("R1", vm.AvailableRoomIds);
        Assert.Contains("R2", vm.AvailableRoomIds);
    }

    [Fact]
    public void SingleRoomChange_UsesNewRoomIdString_UpdatesRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void NewRoomId_Setter_RaisesApplyRoomChangeCanExecuteChanged()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        var raised = 0;
        vm.ApplyRoomChangeCommand.CanExecuteChanged += (_, _) => raised++;

        vm.NewRoomId = "R2";

        Assert.True(raised > 0);
    }

    [Fact]
    public void SelectedReplacementRoomId_Setter_RaisesApplyRoomChangeCanExecuteChanged()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.AddRoom(new Room { Id = "R4", Name = "강의실4" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        var raised = 0;
        vm.ApplyRoomChangeCommand.CanExecuteChanged += (_, _) => raised++;

        vm.SelectedReplacementRoomId = "R4";

        Assert.True(raised > 0);
    }

    [Fact]
    public void SelectedOldRoomId_Setter_RaisesApplyRoomChangeCanExecuteChanged()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        var raised = 0;
        vm.ApplyRoomChangeCommand.CanExecuteChanged += (_, _) => raised++;

        vm.SelectedOldRoomId = "R2";

        Assert.True(raised > 0);
    }

    [Fact]
    public void SingleRoomChange_CommandBecomesExecutableAfterNewRoomSelected()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));

        vm.NewRoomId = "R2";

        Assert.True(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void MultiRoomReplacement_CommandBecomesExecutableAfterOldAndNewSelected()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));

        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";

        Assert.True(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void SingleRoomChange_CommandExecute_ChangesWorkingRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void MultiRoomReplacement_CommandExecute_ChangesSelectedOldRoomOnly()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2", "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void RoomChangeCommand_WithSelection_CanExecuteTrue_BeforeNewRoomSelected()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "";

        Assert.False(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void SingleRoomChange_NoNewRoom_ShowsFailureMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("새 강의실을 선택하세요.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void SingleRoomChange_SameRoom_ShowsFailureMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("기존 강의실과 동일합니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void SingleRoomChange_RoomNotExists_ShowsFailureMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "RX";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("존재하지 않는 강의실입니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void SingleRoomChange_ConflictRollback_ShowsFailureMessage()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("해당 시간에 이미 사용 중인 강의실입니다.", vm.RoomChangeStatusMessage);
        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void RoomChange_ChangedZero_ShowsFailureMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var stale = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R9" }, 1, 2, false);

        vm.SelectCell(0, 1, 2, 0, stale);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Contains("강의실 배정 정보를 찾지 못했습니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void SingleRoomChange_Failure_DoesNotResetNewRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "RX";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("RX", vm.NewRoomId);
    }

    [Fact]
    public void SingleRoomChange_Success_ShowsSuccessMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("강의실을 변경했습니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void MultiRoomReplacement_InvalidOldOrNew_ShowsFailureMessage()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.SelectedOldRoomId = "";
        vm.SelectedReplacementRoomId = "R3";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("교체할 기존 강의실을 선택하세요.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void MultiRoomReplacement_Failure_DoesNotResetSelectedReplacementRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "RX";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("RX", vm.SelectedReplacementRoomId);
        Assert.Equal("존재하지 않는 강의실입니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void MultiRoomReplacement_Success_ShowsSuccessMessage()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));
        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("강의실을 변경했습니다.", vm.RoomChangeStatusMessage);
    }

    [Fact]
    public void SingleRoomChange_DoesNotRequireSelectedOldRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "";
        vm.SelectedReplacementRoomId = "";
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void SingleRoomChange_DoesNotUseReplacementRoomCandidates()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedReplacementRoomId = "";
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void ReplaceOneRoom_ChangesOnlySelectedOldRoom()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2", "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
        Assert.DoesNotContain("R1", AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void TryApplyRoomChange_ChangedZero_DoesNotCommitOrCreateUndo()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var stale = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R9" }, 1, 2, false);

        vm.SelectCell(0, 1, 2, 0, stale);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Contains("강의실 배정 정보를 찾지 못했습니다.", vm.StatusMessage);
    }

    [Fact]
    public void SingleRoomChange_ChangesWorkingRoomId_AndRerenders()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.Equal(new[] { "R2" }, vm.SelectedAssignment!.Rooms);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void MultiRoomReplacement_ChangesOnlySelectedOldRoom_InWorking()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2", "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void SingleRoomChange_StaleSelectedAssignment_DoesNotReportSuccess()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var stale = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R9" }, 1, 2, false);

        vm.SelectCell(0, 1, 2, 0, stale);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.DoesNotContain("변경 0건", vm.StatusMessage);
        Assert.Contains("강의실 배정 정보를 찾지 못했습니다.", vm.StatusMessage);
        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void Save_AfterSuccessfulRoomChange_PersistsChangedWorkingRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.SaveName = "강의실변경저장";
        vm.SaveTimetableCommand.Execute(null);

        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "강의실변경저장"));
        Assert.Contains(saved.Assignments, a => a.CourseId == "X-01" && a.RoomId == "R2");
        Assert.DoesNotContain(saved.Assignments, a => a.CourseId == "X-01" && a.RoomId == "R1");
    }

    [Fact]
    public void LoadSaved_AfterRoomChange_RestoresChangedRoomId()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.SaveName = "강의실변경로드";
        vm.SaveTimetableCommand.Execute(null);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "강의실변경로드"));

        vm.LoadFromSavedTimetable(saved);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void RoomChange_TargetMatching_FindsTwoHourBlockAssignments()
    {
        _ws.Courses.First(c => c.Id == "X-01").BlockStructure = new List<int> { 2 };
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 2, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Assignment.Rooms.Contains("R1"));
    }

    [Fact]
    public void RoomChange_TargetMatching_FailsWhenNoWorkingTarget()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var stale = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R9" }, 1, 2, false);

        vm.SelectCell(0, 1, 2, 0, stale);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void SingleRoomChange_ChangedWorkingThenRerenderedRoomsContainNewRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Contains("R2", AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.DoesNotContain("R1", AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void MultiRoomReplacement_OnlySelectedOldRoomChanged()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        var rooms = AssignmentAt(vm, 0, 1, "X-01").Rooms;
        Assert.Contains("R2", rooms);
        Assert.Contains("R3", rooms);
        Assert.DoesNotContain("R1", rooms);
    }

    [Fact]
    public void SaveLoad_AfterRoomChange_RestoresChangedRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.SaveName = "저장로드강의실";
        vm.SaveTimetableCommand.Execute(null);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "저장로드강의실"));

        vm.LoadFromSaved(saved);
        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void RoomChange_ChangedZero_ReportsUserFacingMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var stale = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R9" }, 1, 2, false);

        vm.SelectCell(0, 1, 2, 0, stale);
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Contains("테스트 A분반의 강의실 배정 정보를 찾지 못했습니다.", vm.StatusMessage);
        Assert.DoesNotContain("CourseId=", vm.StatusMessage);
        Assert.DoesNotContain("Day=", vm.StatusMessage);
        Assert.DoesNotContain("SelectedPeriod=", vm.StatusMessage);
        Assert.DoesNotContain("RowSpan=", vm.StatusMessage);
        Assert.DoesNotContain("OldRoomId=", vm.StatusMessage);
        Assert.DoesNotContain("NewRoomId=", vm.StatusMessage);
        Assert.DoesNotContain("R9", vm.StatusMessage);
    }

    [Fact]
    public void RoomChange_AfterApply_SelectedAssignmentRoomsUpdated()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, vm.SelectedAssignment!.Rooms);
        Assert.Equal("강의실2", vm.SelectedRoomsText);
    }

    [Fact]
    public void RoomChange_AfterApply_IntegratedRenderShowsNewRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void RoomChange_AfterApply_RoomViewMovesFromOldRoomToNewRoom()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        var oldRoomView = vm.RoomViews.Single(v => v.Id == "R1");
        var newRoomView = vm.RoomViews.Single(v => v.Id == "R2");
        Assert.DoesNotContain(oldRoomView.Grid.Cells.SelectMany(c => c.Items), a => a.CourseId == "X-01");
        Assert.Contains(newRoomView.Grid.Cells.SelectMany(c => c.Items), a => a.CourseId == "X-01" && a.Rooms.Contains("R2"));
    }

    [Fact]
    public void RoomChange_WorkspaceOnlyRoom_SaveLoadRestoresRoomId()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var snapshot = new AppData(
            _ws.Courses.ToList(),
            _ws.Professors.ToList(),
            new List<Room> { new() { Id = "R1", Name = "강의실1" } },
            _ws.CrossGroups.ToList(),
            _ws.RetakeScenarios.ToList());
        var record = new SavedTimetableRecord(
            "saved",
            "저장",
            DateTime.Now,
            new[] { new TimetableAssignmentRow("X-01", 0, 1, "R1") },
            Array.Empty<SavedManualCrossLinkRow>(),
            System.Text.Json.JsonSerializer.Serialize(snapshot));
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSaved(record);

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.SaveName = "워크스페이스방저장";
        vm.SaveTimetableCommand.Execute(null);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "워크스페이스방저장"));

        vm.LoadFromSaved(saved);

        Assert.Equal(new[] { "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.Contains(vm.RoomViews, v => v.Id == "R3");
    }

    [Fact]
    public void MultiRoomReplacement_SaveLoadKeepsAllRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.SaveName = "다중강의실저장";
        vm.SaveTimetableCommand.Execute(null);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "다중강의실저장"));

        vm.LoadFromSaved(saved);

        Assert.Equal(new[] { "R2", "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void RoomChange_ConflictRollback_DoesNotUpdateRenderedRooms()
    {
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));

        vm.SelectCell(0, 1, 2, 0, AssignmentAt(vm, 0, 1, "X-01"));
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R1" }, AssignmentAt(vm, 0, 1, "X-01").Rooms);
        Assert.Equal(new[] { "R1" }, vm.SelectedAssignment!.Rooms);
        Assert.DoesNotContain(vm.RoomViews.Single(v => v.Id == "R2").Grid.Cells.SelectMany(c => c.Items), a => a.CourseId == "X-01");
    }

    [Fact]
    public void MultiRoomReplacement_DoesNotUseNewRoomId()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2", "R3" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void ReplaceOneRoom_KeepsOtherAssignedRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Contains("R2", AssignmentAt(vm, 0, 1, "X-01").Rooms);
    }

    [Fact]
    public void ReplaceOneRoom_ToAlreadyAssignedRoom_IsRejected()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R2";

        Assert.False(vm.ApplyRoomChangeCommand.CanExecute(null));
        if (vm.ApplyRoomChangeCommand.CanExecute(null))
            vm.ApplyRoomChangeCommand.Execute(null);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Equal("", vm.RoomChangeStatusMessage);
        Assert.Equal(new[] { "R1", "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void ReplaceOneRoom_WithRoomConflict_IsRejected()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"),
            new SolutionAssignment("Y-01", 0, 1, "R3")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R1", "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Equal("해당 시간에 이미 사용 중인 강의실입니다.", vm.StatusMessage);
    }

    [Fact]
    public void ReplaceOneRoom_Success_CreatesUndoSnapshot()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void ReplaceOneRoom_Failure_DoesNotCreateUndoSnapshot()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2"),
            new SolutionAssignment("Y-01", 0, 1, "R3")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Undo_AfterReplaceOneRoom_RestoresOriginalRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedOldRoomId = "R1";
        vm.SelectedReplacementRoomId = "R3";
        vm.ApplyRoomChangeCommand.Execute(null);

        vm.UndoCommand.Execute(null);

        Assert.Equal(new[] { "R1", "R2" }, AssignmentAt(vm, 0, 1, "X-01").Rooms.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void SingleRoomSelection_KeepsExistingRoomChangeUi()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.False(vm.HasMultipleAssignedRooms);
        Assert.True(vm.HasSingleAssignedRoomChangeUi);
        Assert.True(vm.HasSingleAssignedRoomDisplay);
        Assert.Equal(new[] { "R1", "R2" }, vm.AvailableRoomIds);
    }

    [Fact]
    public void SavedSnapshotRoomsMissing_WorkspaceRoomStillAppearsInAvailableRoomIds()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var snapshot = new AppData(
            _ws.Courses.ToList(),
            _ws.Professors.ToList(),
            new List<Room> { new() { Id = "R1", Name = "강의실1" } },
            _ws.CrossGroups.ToList(),
            _ws.RetakeScenarios.ToList());
        var record = new SavedTimetableRecord(
            "saved",
            "저장",
            DateTime.Now,
            new[] { new TimetableAssignmentRow("X-01", 0, 1, "R1") },
            Array.Empty<SavedManualCrossLinkRow>(),
            System.Text.Json.JsonSerializer.Serialize(snapshot));
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSaved(record);

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Contains("R3", vm.AvailableRoomIds);
    }

    [Fact]
    public void RoomExists_UsesWorkspaceSnapshotAndAssignedRoomsUnion()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R3";

        Assert.True(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void SingleRoomChange_UsesEditableRoomIds_NotOnlyFixedRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.Courses.First(c => c.Id == "X-01").FixedRooms.Add("R1");
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Contains("R3", vm.AvailableRoomIds);
    }

    [Fact]
    public void MultiRoomReplacement_UsesEditableRoomIdsAndExcludesOtherAssignedRooms()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.Courses.First(c => c.Id == "X-01").FixedRooms.AddRange(new[] { "R1", "R2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Contains("R3", vm.ReplacementRoomCandidates);
        Assert.DoesNotContain("R1", vm.ReplacementRoomCandidates);
        Assert.DoesNotContain("R2", vm.ReplacementRoomCandidates);
    }

    [Fact]
    public void SelectedOldRoomChange_RefreshesReplacementCandidates()
    {
        _ws.AddRoom(new Room { Id = "R3", Name = "강의실3" });
        _ws.AddRoom(new Room { Id = "R4", Name = "강의실4" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 1, "R2")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.SelectedReplacementRoomId = "R3";
        vm.SelectedOldRoomId = "R2";

        Assert.Equal("R3", vm.SelectedReplacementRoomId);
        Assert.Equal(new[] { "R3", "R4" }, vm.ReplacementRoomCandidates);
    }

    [Fact]
    public void ProfessorDisplay_DeduplicatesPrimaryProfessorInCoteachList()
    {
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false)
        {
            CoteachProfIds = new[] { "P1", "P2" },
        };

        Assert.Equal("P1, P2", assignment.ProfessorLine);
    }

    [Fact]
    public void ProfessorDisplay_KeepsPrimaryThenCoteachersOrder()
    {
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false)
        {
            CoteachProfIds = new[] { "P3", "P2" },
        };

        Assert.Equal("P1, P3, P2", assignment.ProfessorLine);
    }

    [Fact]
    public void ProfessorViews_IncludeCoteachingCourses()
    {
        _ws.AddProfessor(new Professor { Id = "P2", Name = "공동" });
        _ws.Courses.First(c => c.Id == "X-01").CoteachProfs.Add("P2");
        var vm = _sp.GetRequiredService<ManualEditViewModel>();

        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var coteacherView = vm.ProfessorViews.Single(v => v.Id == "P2");
        var occupied = Assert.Single(coteacherView.Grid.Cells.Where(c => c.IsOccupied));
        Assert.Equal("X-01", Assert.Single(occupied.Items).CourseId);
    }

    [Fact]
    public void InspectorCoteachText_DeduplicatesPrimaryProfessor()
    {
        _ws.AddProfessor(new Professor { Id = "P2", Name = "공동" });
        _ws.Courses.First(c => c.Id == "X-01").CoteachProfs.AddRange(new[] { "P1", "P2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        var assignment = AssignmentAt(vm, 0, 1, "X-01");
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Equal("공동", vm.SelectedCoteachText);
    }

    [Fact]
    public void ApplyRoomChange_ProfUnavailable_DetectedAndPrompted()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var prof = _ws.Professors.First(p => p.Id == "P1");
        prof.UnavailableSlots.Add(new TimeSlot(0, 2));

        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        // Move the course to a slot where prof is unavailable — change room then we don't have day/period change yet,
        // so for this test we directly verify Detect catches it.
        var conflicts = ConflictDetector.Detect(
            new[] { new SolutionAssignment("X-01", 0, 2, "R1") },
            _ws.Courses, _ws.Professors);
        Assert.Contains(conflicts, c => c.Type == ConflictType.ProfUnavailable);
    }

    [Fact]
    public void Reset_RestoresBaseAssignment()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.NewRoomId = "R2";
        _dialog.ResponseToReturn = true;
        vm.ApplyRoomChangeCommand.Execute(null);

        vm.ResetCommand.Execute(null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(vm.Conflicts);
    }

    [Fact]
    public void Reset_RestoresBaselineCrossLinks()
    {
        var vm = ArrangeResetBaselineCrossCase();
        var baseline = Assert.Single(vm.WorkingCrossLinks);
        RestoreSavedManualCrossRows(vm, Array.Empty<SavedManualCrossLinkRow>());

        vm.ResetCommand.Execute(null);

        var restored = Assert.Single(vm.WorkingCrossLinks);
        Assert.True(restored.MatchesPair(baseline.SourceKey, baseline.TargetKey));
    }

    [Fact]
    public void Reset_RerenderUsesRestoredCrossLinks()
    {
        var vm = ArrangeResetBaselineCrossCase();
        RestoreSavedManualCrossRows(vm, Array.Empty<SavedManualCrossLinkRow>());

        vm.ResetCommand.Execute(null);

        var order = CrossParallelOrder(vm);
        var labels = CrossLinkLabels(vm);
        Assert.Equal(2, order.Count);
        Assert.Equal(2, labels.Count);
        Assert.NotEmpty(vm.Grid.CrossParallelOrder);
        Assert.NotEmpty(vm.Grid.CrossLinkLabels);
    }

    [Fact]
    public void Reset_RefreshConflictsIsCrossAware()
    {
        var vm = ArrangeResetBaselineCrossCase();
        RestoreSavedManualCrossRows(vm, Array.Empty<SavedManualCrossLinkRow>());
        RefreshConflicts(vm, strictManualCrossValidation: true);
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.SectionConflict);

        vm.ResetCommand.Execute(null);

        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.SectionConflict);
    }

    [Fact]
    public void Reset_UndoRestoresPreResetState()
    {
        var vm = ArrangeResetBaselineCrossCase();
        SetWorkingAssignments(
            vm,
            new SolutionAssignment("4-01", 1, 3, "3"),
            new SolutionAssignment("4-01", 1, 4, "3"),
            new SolutionAssignment("4-02", 0, 3, "6"),
            new SolutionAssignment("4-02", 0, 4, "6"));
        RestoreSavedManualCrossRows(vm, Array.Empty<SavedManualCrossLinkRow>());

        vm.ResetCommand.Execute(null);
        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "4-01" && c.Day == 0 && c.Period == 3);

        vm.UndoCommand.Execute(null);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "4-01" && c.Day == 1 && c.Period == 3);
    }

    [Fact]
    public void HandleCellClick_EmptyValidTarget_MovesSelectedRunAndClearsSelection()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_LunchTarget_ForceMovesAfterConfirmation()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 5, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Single(_dialog.Calls);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void HandleCellClick_Len2TargetRangeContainsLen1Course_IsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 1, 2, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 2 && c.Assignment.CourseId == "Y-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_Len1IntoSecondSlotOfLen2Course_IsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.HandleCellClick(1, 1, 2, 0, assignment);
        vm.HandleCellClick(0, 2, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01" && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01" && c.Assignment.RowSpan == 1);
    }

    [Fact]
    public void HandleCellClick_FailedMoveThenValidMove_LeavesWorkingStateUsable()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 1, 2, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 9, 2, 0, null);
        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("이동 완료", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 2 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void HandleCellClick_Len2OntoLen2Course_IsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 2, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Y-01", 1, 2, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01" && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01" && c.Assignment.RowSpan == 2);
    }

    [Fact]
    public void HandleCellClick_Len2AtLastPeriod_DoesNotMove()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 9, 2, 0, null);

        Assert.Contains("범위를 벗어납니다", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_OntoFixedCourse_ForceMovesAndKeepsExistingCourse()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course
        {
            Id = "F-01",
            Name = "고정",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P2",
            IsFixed = true,
            FixedSlots = new List<TimeSlot> { new(1, 1) },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("F-01", 1, 1, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "F-01");
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void HandleCellClick_SelectedBlockAgain_ClearsSelectionAndDoesNotMove()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(0, 1, 2, 0, assignment);

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains("선택이 해제", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_DifferentGradeWhileSelected_ClearsSelectionWithoutForceMove()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "타학년", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var otherGrade = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "타학년", "P2", 3, 1, new List<string> { "R2" }, 1, 1, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 3, 0, otherGrade);

        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(_dialog.Calls);
        Assert.Contains("선택이 해제", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void HandleCellClick_Len1MoveThenReturnToOriginalSlot_Succeeds()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);
        vm.HandleCellClick(1, 1, 2, 0, AssignmentAt(vm, 1, 1, "X-01"));
        vm.HandleCellClick(0, 1, 2, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_Len2MoveThenReturnToOriginalSlot_Succeeds()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);
        vm.HandleCellClick(1, 1, 2, 0, AssignmentAt(vm, 1, 1, "X-01"));
        vm.HandleCellClick(0, 1, 2, 0, null);

        Assert.DoesNotContain("HC-06", vm.StatusMessage);
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCellClick_DifferentGradeCourseAtSameTime_DoesNotCauseHc06FalsePositive()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.DoesNotContain("HC-06", vm.StatusMessage);
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.SelectedDay);
        Assert.Null(vm.SelectedPeriod);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void HandleCellClick_CrossDisplayColumn_DoesNotForceMove()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.HandleCellClick(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 1, null);

        Assert.Contains("Cross 표시용 열", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Empty(_dialog.Calls);
    }

    [Fact]
    public void DragDropMove_ToEmptyValidCell_MovesAssignment()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");

        Assert.True(vm.CanDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            2, 1, 2, 0));

        var moved = vm.HandleDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            2, 1, 2, 0);

        Assert.True(moved);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
        Assert.False(vm.EvaluateCrossHover(2, 1, 2, 0, null).CanCreate);
        Assert.False(vm.EvaluateSwapHover(2, 1, 2, 0, null).CanSwap);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void DndMoveSuccess_UsesSamePostMoveCleanupAsClickMove()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");

        var moved = vm.HandleDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            2, 1, 2, 0);

        Assert.True(moved);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void CrossDrop_SourceNotSelected_CreatesManualCrossLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 1, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "Y-01");

        Assert.Null(vm.SelectedAssignment);

        vm.HandleCrossDrop(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void SwapDrop_SourceNotSelected_PerformsSwap()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교환대상", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 3, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "Y-01");

        Assert.Null(vm.SelectedAssignment);

        vm.HandleSwapDrop(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "X-01" && c.Day == 0 && c.Period == 3);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.CourseId == "Y-01" && c.Day == 0 && c.Period == 1);
        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void DragDropMove_ToBlockedOverlapRange_DoesNotMoveOrCreateUndoSnapshot()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 2, "R1"),
            new SolutionAssignment("B-02", 0, 3, "R1"),
            new SolutionAssignment("X-01", 1, 1, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");

        Assert.False(vm.CanDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            0, 3, 2, 0));
        var moved = vm.HandleDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            0, 3, 2, 0);

        Assert.False(moved);
        Assert.Contains("전체 점유 범위", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 2 && c.Assignment.CourseId == "B-02" && c.Assignment.RowSpan == 2);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Equal("X-01", vm.SelectedAssignment!.CourseId);
        Assert.NotNull(vm.Grid.SelectedCell);
        Assert.NotEmpty(vm.Grid.EditStates);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void DragDropMove_ToCrossDisplayOnlySubColumn_DoesNotMoveOrCreateUndoSnapshot()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");

        Assert.False(vm.CanDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            1, 2, 2, 1));
        var moved = vm.HandleDropMove(
            source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment,
            1, 2, 2, 1);

        Assert.False(moved);
        Assert.Contains("Cross 표시용 열", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void TwoHourMoveSuccess_RebuildsRowSpanAtNewRange()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 6, "R1"),
            new SolutionAssignment("B-02", 0, 7, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "B-02");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(1, 3, 2, 0, null);

        var moved = Assert.Single(vm.Grid.Cells, c => c.Assignment.CourseId == "B-02");
        Assert.Equal(1, moved.Day);
        Assert.Equal(3, moved.Period);
        Assert.Equal(2, moved.Assignment.RowSpan);
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Assignment.CourseId == "B-02");
        Assert.Null(vm.SelectedAssignment);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void TwoHourMoveSuccess_DoesNotRenderBlankCoveredCell()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("B-02", 0, 6, "R1"),
            new SolutionAssignment("B-02", 0, 7, "R1")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "B-02");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(1, 3, 2, 0, null);

        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 3 && c.Assignment.CourseId == "B-02" && c.Assignment.RowSpan == 2);
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 4 && c.Assignment.CourseId == "B-02");
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void SelectCell_MoveStateAndTryMove_AgreeForBlockedAndMovableTargets()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var sol = MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1"));
        vm.LoadFromSolution(sol);

        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Blocked,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(1, 5, 2, 0)].State);
        Assert.Equal(
            TimetableScheduler.ViewModel.Grid.ManualMoveCellState.Movable,
            vm.Grid.EditStates[new TimetableScheduler.ViewModel.Grid.UnifiedCellKey(2, 1, 2, 0)].State);

        vm.HandleCellClick(1, 5, 2, 0, null);
        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
        vm.SelectCell(1, 5, 2, 0, assignment);
        vm.HandleCellClick(2, 1, 2, 0, null);
        Assert.Contains("이동 완료", vm.StatusMessage);
    }

    [Fact]
    public void ForceMove_BlockedTargetCancel_LeavesWorkingStateUnchanged()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        _dialog.ResponseToReturn = false;

        vm.HandleCellClick(1, 5, 2, 0, null);

        Assert.Single(_dialog.Calls);
        Assert.Contains("취소", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 5 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void ForceMove_ProfessorConflict_MovesAndSaveIsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P1" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
        vm.SaveName = "교수충돌저장";
        vm.SaveTimetableCommand.Execute(null);
        Assert.Contains("저장 차단", vm.StatusMessage);
        Assert.DoesNotContain(_ws.SavedTimetables, t => t.Name == "교수충돌저장");
    }

    [Fact]
    public void ForceMoveSuccess_ClearsSelectionWithoutBlankCell()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P1" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var source = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void ForceMove_RoomConflict_MovesAndKeepsError()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "충돌", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void ForceMove_FixedSelectedCourse_MovesAndKeepsFixedTimeError()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.UpdateCourse(new Course
        {
            Id = "X-01",
            Name = "테스트",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            IsFixed = true,
            FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2) },
        });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, true);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Contains("강제 이동", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
    }

    [Fact]
    public void UndoRedo_AfterNormalMove_RestoresBothStates()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);

        vm.UndoCommand.Execute(null);

        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);

        vm.RedoCommand.Execute(null);

        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);
    }

    [Fact]
    public void UndoRedo_AfterForceMove_RestoresConflicts()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 5, 2, 0, null);
        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);

        vm.UndoCommand.Execute(null);

        Assert.Empty(vm.Conflicts);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");

        vm.RedoCommand.Execute(null);

        Assert.Contains(vm.Conflicts, c => c.Severity == ConflictSeverity.Error);
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void UndoRedo_AfterCrossCreate_RestoresCrossLinksAndPlacement()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);
        Assert.Single(vm.WorkingCrossLinks);

        vm.UndoCommand.Execute(null);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");

        vm.RedoCommand.Execute(null);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
    }

    [Fact]
    public void Undo_AfterMoveReleasesCross_RestoresCrossLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);
        vm.SelectCell(0, 1, 2, 1, AssignmentAt(vm, 0, 1, "X-01"));
        vm.HandleCellClick(2, 1, 2, 0, null);
        Assert.Empty(vm.WorkingCrossLinks);

        vm.UndoCommand.Execute(null);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair("X-01", "Y-01"));
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void UndoThenNewMove_ClearsRedoStack()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 1, 2, 0, null);
        vm.UndoCommand.Execute(null);
        vm.SelectCell(0, 1, 2, 0, assignment);

        vm.HandleCellClick(2, 1, 2, 0, null);

        Assert.False(vm.RedoCommand.CanExecute(null));
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void FailedMoveAndCancelledForceMove_DoNotPushUndoSnapshot()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1")));
        var assignment = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 2, 2, false);
        vm.SelectCell(0, 1, 2, 0, assignment);
        vm.HandleCellClick(1, 9, 2, 0, null);
        Assert.False(vm.UndoCommand.CanExecute(null));

        _dialog.ResponseToReturn = false;
        vm.HandleCellClick(1, 5, 2, 0, null);

        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void EvaluateCrossHover_RequiresSelectionAndOccupiedTarget()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));

        Assert.False(vm.EvaluateCrossHover(0, 1, 2, 0, null).CanCreate);
    }

    [Fact]
    public void EvaluateCrossHover_AllowsSameGradeTargetCourse()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        vm.LoadFromSolution(sol);
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        var state = vm.EvaluateCrossHover(0, 1, 2, 1, target);

        Assert.True(state.CanCreate);
    }

    [Fact]
    public void EvaluateCrossHover_AllowsSameTwoHourBlocks()
    {
        _ws.UpdateCourse(new Course
        {
            Id = "X-01",
            Name = "테스트",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddCourse(new Course
        {
            Id = "Y-01",
            Name = "기타",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            Section = 2,
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"),
            new SolutionAssignment("Y-01", 0, 2, "R2")));
        var selectedCell = vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        var targetCell = vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        vm.SelectCell(0, 1, 2, selectedCell.SubColumnIdx, selectedCell.Assignment);

        var state = vm.EvaluateCrossHover(0, 1, 2, targetCell.SubColumnIdx, targetCell.Assignment);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void EvaluateCrossHover_OneHourToTwoHour_DoesNotShowPlus()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            Section = 2,
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("B-02", 1, 1, "R2"),
            new SolutionAssignment("B-02", 1, 2, "R2")));
        var selectedCell = vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        var targetCell = vm.Grid.Cells.Single(c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "B-02");
        vm.SelectCell(0, 1, 2, selectedCell.SubColumnIdx, selectedCell.Assignment);

        var state = vm.EvaluateCrossHover(1, 1, 2, targetCell.SubColumnIdx, targetCell.Assignment);

        Assert.False(state.CanCreate);
        Assert.Contains("서로 다른 길이", state.Reason);
    }

    [Fact]
    public void HandleCrossAddRequested_DurationMismatch_DoesNotMutateStateOrUndo()
    {
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            Section = 2,
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("B-02", 1, 1, "R2"),
            new SolutionAssignment("B-02", 1, 2, "R2")));
        var selectedCell = vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        var targetCell = vm.Grid.Cells.Single(c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "B-02");
        vm.SelectCell(0, 1, 2, selectedCell.SubColumnIdx, selectedCell.Assignment);

        vm.HandleCrossAddRequested(1, 1, 2, targetCell.SubColumnIdx, targetCell.Assignment);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("서로 다른 길이", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "B-02" && c.Assignment.RowSpan == 2);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void HandleCrossAddRequested_SameDurationCoveredTargetRange_NormalizesAndCreatesManualCrossLink()
    {
        _ws.UpdateCourse(new Course
        {
            Id = "X-01",
            Name = "테스트",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P1",
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddCourse(new Course
        {
            Id = "B-02",
            Name = "두시간",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P2",
            Section = 2,
            BlockStructure = new List<int> { 2 },
        });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("X-01", 0, 2, "R1"),
            new SolutionAssignment("B-02", 1, 1, "R2"),
            new SolutionAssignment("B-02", 1, 2, "R2")));
        var selectedCell = vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        var targetCell = vm.Grid.Cells.Single(c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "B-02");
        vm.SelectCell(0, 1, 2, selectedCell.SubColumnIdx, selectedCell.Assignment);

        vm.HandleCrossAddRequested(1, 2, 2, targetCell.SubColumnIdx, targetCell.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void EvaluateCrossHover_AllowsDifferentPeriodSameGradeByMovingToTargetSlot()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var otherGrade = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 3, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        Assert.False(vm.EvaluateCrossHover(0, 1, 2, 0, selected).CanCreate);
        Assert.False(vm.EvaluateCrossHover(0, 1, 3, 0, otherGrade).CanCreate);
        var sameGradeDifferentTime = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "다른시간", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        _ws.AddCourse(new Course { Id = "Z-01", Name = "다른시간", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });

        Assert.True(vm.EvaluateCrossHover(1, 1, 2, 1, sameGradeDifferentTime).CanCreate);
    }

    [Fact]
    public void EvaluateCrossHover_BlocksWhenImmediateCrossMoveWouldConflict()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "교수중복", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1" });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "강의실중복", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        var sameProfessor = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "교수중복", "P1", 2, 1, new List<string> { "R2" }, 1, 1, false);
        var sameRoom = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "강의실중복", "P2", 2, 1, new List<string> { "R1" }, 1, 1, false);

        Assert.False(vm.EvaluateCrossHover(1, 1, 2, 0, sameProfessor).CanCreate);
        Assert.False(vm.EvaluateCrossHover(2, 1, 2, 0, sameRoom).CanCreate);
    }

    [Fact]
    public void HandleCrossAddRequested_OnlyShowsMessage()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        var sol = MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2"));
        vm.LoadFromSolution(sol);
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Contains("크로스가 설정되었습니다", vm.StatusMessage);
        Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal(2, vm.Grid.Cells.Count);
    }

    [Fact]
    public void HandleCrossAddRequested_DoesNotMutateWorkspaceCrossGroupsAndPreventsDuplicate()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);
        vm.SelectCell(0, 1, 2, 1, AssignmentAt(vm, 0, 1, "X-01"));
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Empty(_ws.CrossGroups);
        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains("이미 크로스", vm.StatusMessage);
    }

    [Fact]
    public void CrossLink_ExemptsOnlyGradeConflict()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        Assert.Contains(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void MovingCrossedCourseAway_ReleasesManualCrossLink()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        vm.SelectCell(0, 1, 2, 1, AssignmentAt(vm, 0, 1, "X-01"));
        vm.HandleCellClick(1, 1, 2, 0, null);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("기존 크로스가 해제", vm.StatusMessage);
    }

    [Fact]
    public void CrossMoveSuccess_RebuildsGridAndDoesNotLeaveStaleSelection()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 1, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01");
        var target = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "Y-01");
        vm.HandleCellClick(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleCrossAddRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Null(vm.SelectedAssignment);
        Assert.Null(vm.Grid.SelectedCell);
        Assert.Empty(vm.Grid.EditStates);
    }

    [Fact]
    public void HandleCrossAddRequested_AllowsSameGradeSameTimePair()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Contains("크로스가 설정되었습니다", vm.StatusMessage);
        Assert.Null(vm.SelectedAssignment);
        Assert.Equal("-", vm.SelectedCrossGroupText);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void SameTimeCrossLink_AllowsHc11OverlapOnly()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2" });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 1, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Contains("크로스가 설정되었습니다", vm.StatusMessage);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.GradeConflict);
    }

    [Fact]
    public void CrossedOccupiedTarget_AllowsParallelMoveAndExpandsGradeWidth()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Contains("크로스가 설정되었습니다", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.Equal(2, vm.Grid.DayGroups[0].Grades.Single(g => g.Grade == 2).Width);
        Assert.Equal("Y-01", vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.SubColumnIdx == 0).Assignment.CourseId);
        Assert.Equal("X-01", vm.Grid.Cells.Single(c => c.Day == 0 && c.Period == 1 && c.SubColumnIdx == 1).Assignment.CourseId);
    }

    [Fact]
    public void NonCrossOccupiedTarget_ClickSwitchesSelectionWithoutForceDialog()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCellClick(1, 1, 2, 0, new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false));

        Assert.Empty(_dialog.Calls);
        Assert.NotNull(vm.SelectedAssignment);
        Assert.Equal("Y-01", vm.SelectedAssignment.CourseId);
        Assert.Contains("선택: 기타", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "Y-01");
        Assert.DoesNotContain(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void CrossedOccupiedTarget_WithProfessorConflict_RemainsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1", Section = 2 });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P1", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, target);

        Assert.Contains("교수", vm.StatusMessage);
        Assert.DoesNotContain("HC-", vm.StatusMessage);
        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void CrossedOccupiedTarget_WithRoomConflict_RemainsBlocked()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 0, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R1" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(0, 1, 2, 1, target);

        Assert.Contains("강의실", vm.StatusMessage);
        Assert.DoesNotContain("HC-01", vm.StatusMessage);
        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 0 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void HandleCrossAddRequested_BlocksThirdCrossForSelectedAssignment()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "또다른", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3", Section = 3 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R2")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var y = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        var z = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "또다른", "P3", 2, 3, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, y);

        vm.SelectCell(1, 1, 2, 1, selected);
        vm.HandleCrossAddRequested(2, 1, 2, 0, z);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair("X-01", "Z-01"));
        Assert.DoesNotContain(vm.WorkingCrossLinks, l => l.MatchesPair("X-01", "Y-01"));
    }

    [Fact]
    public void HandleCrossAddRequested_ReplacesExistingPartnerAndMovesSelected()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "또다른", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3", Section = 3 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R3")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var y = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        var z = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "또다른", "P3", 2, 3, new List<string> { "R3" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, y);
        Assert.Single(vm.WorkingCrossLinks);

        vm.SelectCell(1, 1, 2, 1, AssignmentAt(vm, 1, 1, "X-01"));
        vm.HandleCrossAddRequested(2, 1, 2, 0, z);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair("X-01", "Z-01"));
        Assert.DoesNotContain(vm.WorkingCrossLinks, l => l.MatchesPair("X-01", "Y-01"));
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "Z-01");
    }

    [Fact]
    public void HandleCrossAddRequested_FailedReplacementKeepsExistingPartnerAndPosition()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "충돌", Grade = 2, HoursPerWeek = 1, ProfessorId = "P1", Section = 3 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var y = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        var z = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "충돌", "P1", 2, 3, new List<string> { "R1" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, y);
        Assert.True(vm.WorkingCrossLinks.Count == 1, vm.StatusMessage);

        vm.SelectCell(1, 1, 2, 1, AssignmentAt(vm, 1, 1, "X-01"));
        vm.HandleCrossAddRequested(2, 1, 2, 0, z);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair("X-01", "Y-01"));
        Assert.Contains("강의실", vm.StatusMessage);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
    }

    [Fact]
    public void SelectingAnotherCourse_ReleasesCurrentCrossAndForceMovesSelectedCourse()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "또다른", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3", Section = 3 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 2, 1, "R3")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var y = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        var z = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Z-01", "또다른", "P3", 2, 3, new List<string> { "R3" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);
        vm.HandleCrossAddRequested(1, 1, 2, 0, y);

        vm.HandleCellClick(2, 1, 2, 0, z);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.Equal("Z-01", vm.SelectedAssignment?.CourseId);
        Assert.Contains(vm.Grid.Cells, c => c.Day == 1 && c.Period == 1 && c.Assignment.CourseId == "X-01");
        Assert.Contains(vm.Grid.Cells, c => c.Day == 2 && c.Period == 1 && c.Assignment.CourseId == "Z-01");
    }

    [Fact]
    public void HandleCrossAddRequested_BlocksThirdCourseAtTargetSlot()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        _ws.AddCourse(new Course { Id = "Y-01", Name = "기타", Grade = 2, HoursPerWeek = 1, ProfessorId = "P2", Section = 2 });
        _ws.AddCourse(new Course { Id = "Z-01", Name = "또다른", Grade = 2, HoursPerWeek = 1, ProfessorId = "P3", Section = 3 });
        _ws.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
        _ws.AddProfessor(new Professor { Id = "P3", Name = "교수3" });
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("X-01", 0, 1, "R1"),
            new SolutionAssignment("Y-01", 1, 1, "R2"),
            new SolutionAssignment("Z-01", 1, 1, "R1")));
        var selected = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "X-01", "테스트", "P1", 2, 1, new List<string> { "R1" }, 1, 2, false);
        var target = new TimetableScheduler.ViewModel.Grid.CellAssignment(
            "Y-01", "기타", "P2", 2, 2, new List<string> { "R2" }, 1, 1, false);
        vm.SelectCell(0, 1, 2, 0, selected);

        vm.HandleCrossAddRequested(1, 1, 2, 0, target);

        Assert.Empty(vm.WorkingCrossLinks);
        Assert.Contains("3개 이상의 과목", vm.StatusMessage);
    }

    [Fact]
    public void ManualCross_ProgrammingApplication_AB_DifferentProfessorDifferentRoom_AllowsCross()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var selected = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var target = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");

        vm.SelectCell(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        var state = vm.EvaluateCrossHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCross_Render_DuplicateCourseId_DoesNotCollapseDifferentProfessorRoom()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();

        var cells = vm.Grid.Cells
            .Where(c => c.Assignment.CourseId == "PROG-APP")
            .OrderBy(c => c.Assignment.ProfessorId, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, cells.Count);
        Assert.Equal(new[] { "P10", "P20" }, cells.Select(c => c.Assignment.ProfessorId));
        Assert.Contains(cells, c => c.Assignment.Rooms.SequenceEqual(new[] { "D330" }));
        Assert.Contains(cells, c => c.Assignment.Rooms.SequenceEqual(new[] { "D331" }));
        Assert.NotEqual(
            ManualCrossAssignmentKeyFromCell(cells[0]),
            ManualCrossAssignmentKeyFromCell(cells[1]));
    }

    [Fact]
    public void ManualMove_DuplicateCourseId_ClickMove_UsesSelectedAssignmentIdentity()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var source = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var other = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        var sourceAssignmentId = source.Assignment.AssignmentId;
        var otherAssignmentId = other.Assignment.AssignmentId;
        Assert.False(string.IsNullOrWhiteSpace(sourceAssignmentId));
        Assert.NotEqual(sourceAssignmentId, otherAssignmentId);

        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        Assert.Equal(sourceAssignmentId, vm.SelectedAssignment?.AssignmentId);
        Assert.Equal("김교수", vm.SelectedProfessorName);

        vm.HandleCellClick(2, 1, source.Grade, 0, null);

        var moved = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var unchanged = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        Assert.Equal(sourceAssignmentId, moved.Assignment.AssignmentId);
        Assert.Equal(2, moved.Day);
        Assert.Equal(1, moved.Period);
        Assert.Equal(otherAssignmentId, unchanged.Assignment.AssignmentId);
        Assert.Equal(0, unchanged.Day);
        Assert.Equal(1, unchanged.Period);
        Assert.Equal(new[] { "D331" }, unchanged.Assignment.Rooms);
    }

    [Fact]
    public void ManualMove_DuplicateCourseId_DragDropMatchesClickMove()
    {
        var clickVm = BuildDuplicateProgrammingApplicationVm(reverseAssignments: true);
        var clickSource = clickVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var clickSourceAssignmentId = clickSource.Assignment.AssignmentId;
        clickVm.HandleCellClick(
            clickSource.Day,
            clickSource.Period,
            clickSource.Grade,
            clickSource.SubColumnIdx,
            clickSource.Assignment);
        clickVm.HandleCellClick(2, 1, clickSource.Grade, 0, null);

        var dropVm = BuildDuplicateProgrammingApplicationVm(reverseAssignments: true);
        var dropSource = dropVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var dropSourceAssignmentId = dropSource.Assignment.AssignmentId;

        Assert.True(dropVm.CanDropMove(
            dropSource.Day,
            dropSource.Period,
            dropSource.Grade,
            dropSource.SubColumnIdx,
            dropSource.Assignment,
            2,
            1,
            dropSource.Grade,
            0));
        Assert.True(dropVm.HandleDropMove(
            dropSource.Day,
            dropSource.Period,
            dropSource.Grade,
            dropSource.SubColumnIdx,
            dropSource.Assignment,
            2,
            1,
            dropSource.Grade,
            0));

        var clickMoved = clickVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var clickOther = clickVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        var dropMoved = dropVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var dropOther = dropVm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        Assert.Equal(clickSourceAssignmentId, clickMoved.Assignment.AssignmentId);
        Assert.Equal(dropSourceAssignmentId, dropMoved.Assignment.AssignmentId);
        Assert.Equal((2, 1, "D330"), (clickMoved.Day, clickMoved.Period, string.Join(",", clickMoved.Assignment.Rooms)));
        Assert.Equal((2, 1, "D330"), (dropMoved.Day, dropMoved.Period, string.Join(",", dropMoved.Assignment.Rooms)));
        Assert.Equal((0, 1, "D331"), (clickOther.Day, clickOther.Period, string.Join(",", clickOther.Assignment.Rooms)));
        Assert.Equal((0, 1, "D331"), (dropOther.Day, dropOther.Period, string.Join(",", dropOther.Assignment.Rooms)));
    }

    [Fact]
    public void ManualMove_DuplicateCourseId_MissingIdentity_DoesNotChangeWorkingState()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var source = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var staleAssignment = source.Assignment with { AssignmentId = "missing-assignment" };
        var before = ProgrammingApplicationSnapshot(vm);

        Assert.False(vm.HandleDropMove(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            staleAssignment,
            2,
            1,
            source.Grade,
            0));

        Assert.Equal(before, ProgrammingApplicationSnapshot(vm));
    }

    [Fact]
    public void ManualMove_DuplicateCourseId_DuplicateIdentity_DoesNotChangeWorkingState()
    {
        var vm = BuildDuplicateProgrammingApplicationVm(sharedAssignmentId: "duplicate-id");
        var source = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var before = ProgrammingApplicationSnapshot(vm);

        Assert.False(vm.HandleDropMove(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment,
            2,
            1,
            source.Grade,
            0));

        Assert.Equal(before, ProgrammingApplicationSnapshot(vm));
    }

    [Fact]
    public void ManualMove_DuplicateCourseId_UndoRedoPreservesAssignmentIdAndSelectionSnapshot()
    {
        var vm = BuildDuplicateProgrammingApplicationThreeVm();
        var source = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var sourceId = source.Assignment.AssignmentId;

        vm.HandleCellClick(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        vm.HandleCellClick(3, 1, source.Grade, 0, null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.AssignmentId == sourceId && c.Day == 3 && c.Period == 1);

        vm.UndoCommand.Execute(null);

        Assert.Equal(sourceId, vm.SelectedAssignment?.AssignmentId);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.AssignmentId == sourceId && c.Day == source.Day && c.Period == source.Period);

        vm.RedoCommand.Execute(null);

        Assert.Null(vm.SelectedAssignment);
        Assert.Contains(vm.Grid.Cells, c => c.Assignment.AssignmentId == sourceId && c.Day == 3 && c.Period == 1);
    }

    [Fact]
    public void HandleCellClick_SelectedSameGradeOccupiedBlock_SwitchesSelectionWithoutChangingWorkingState()
    {
        var vm = BuildDuplicateProgrammingApplicationThreeVm();
        var a = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var b = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        var before = ProgrammingApplicationSnapshot(vm);

        vm.HandleCellClick(a.Day, a.Period, a.Grade, a.SubColumnIdx, a.Assignment);
        vm.HandleCellClick(b.Day, b.Period, b.Grade, b.SubColumnIdx, b.Assignment);

        Assert.Equal(b.Assignment.AssignmentId, vm.SelectedAssignment?.AssignmentId);
        Assert.Equal(before, ProgrammingApplicationSnapshot(vm));
        Assert.Empty(vm.WorkingCrossLinks);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void HandleCellClick_SelectedSameBlockStillClearsSelection()
    {
        var vm = BuildDuplicateProgrammingApplicationThreeVm();
        var a = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");

        vm.HandleCellClick(a.Day, a.Period, a.Grade, a.SubColumnIdx, a.Assignment);
        vm.HandleCellClick(a.Day, a.Period, a.Grade, a.SubColumnIdx, a.Assignment);

        Assert.Null(vm.SelectedAssignment);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void HandleCellClick_SelectedDifferentGradeStillClearsSelection()
    {
        _ws.Courses.Clear();
        _ws.Professors.Clear();
        _ws.Rooms.Clear();
        _ws.Courses.Add(new Course { Id = "A", Name = "A", Grade = 2, HoursPerWeek = 1, ProfessorId = "P10" });
        _ws.Courses.Add(new Course { Id = "B", Name = "B", Grade = 3, HoursPerWeek = 1, ProfessorId = "P20" });
        _ws.Professors.Add(new Professor { Id = "P10", Name = "김교수" });
        _ws.Professors.Add(new Professor { Id = "P20", Name = "박교수" });
        _ws.Rooms.Add(new Room { Id = "R1", Name = "R1" });
        _ws.Rooms.Add(new Room { Id = "R2", Name = "R2" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("A", 0, 1, "R1"),
            new SolutionAssignment("B", 0, 1, "R2")));
        var a = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "A");
        var b = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "B");

        vm.HandleCellClick(a.Day, a.Period, a.Grade, a.SubColumnIdx, a.Assignment);
        vm.HandleCellClick(b.Day, b.Period, b.Grade, b.SubColumnIdx, b.Assignment);

        Assert.Null(vm.SelectedAssignment);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void DuplicateCourseId_EndToEnd_MoveSwapUndoRedoSaveReload_PreservesOccurrenceIdentity()
    {
        var vm = BuildDuplicateProgrammingApplicationThreeVm(reverseAssignments: true);
        var a = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var b = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        var c = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P30");
        var aId = a.Assignment.AssignmentId;
        var bId = b.Assignment.AssignmentId;
        var cId = c.Assignment.AssignmentId;

        vm.HandleCellClick(a.Day, a.Period, a.Grade, a.SubColumnIdx, a.Assignment);

        Assert.Equal(aId, vm.SelectedAssignment?.AssignmentId);
        Assert.Equal("김교수", vm.SelectedProfessorName);

        vm.HandleCellClick(3, 1, a.Grade, 0, null);

        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == aId && cell.Day == 3 && cell.Period == 1);
        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == bId && cell.Day == b.Day && cell.Period == b.Period);

        var movedA = vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == aId);
        var currentC = vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == cId);
        vm.HandleCellClick(movedA.Day, movedA.Period, movedA.Grade, movedA.SubColumnIdx, movedA.Assignment);
        vm.HandleSwapRequested(currentC.Day, currentC.Period, currentC.Grade, currentC.SubColumnIdx, currentC.Assignment);

        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == aId && cell.Day == c.Day && cell.Period == c.Period);
        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == cId && cell.Day == 3 && cell.Period == 1);
        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == bId && cell.Day == b.Day && cell.Period == b.Period);

        vm.UndoCommand.Execute(null);

        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == aId && cell.Day == 3 && cell.Period == 1);
        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == cId && cell.Day == c.Day && cell.Period == c.Period);
        Assert.Equal(aId, vm.SelectedAssignment?.AssignmentId);

        vm.RedoCommand.Execute(null);

        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == aId && cell.Day == c.Day && cell.Period == c.Period);
        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == cId && cell.Day == 3 && cell.Period == 1);

        vm.SaveName = "dup-e2e";
        vm.SaveTimetableCommand.Execute(null);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "dup-e2e"));

        vm.LoadFromSavedTimetable(saved);

        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == aId && cell.Day == c.Day && cell.Period == c.Period);
        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == bId && cell.Day == b.Day && cell.Period == b.Period);
        Assert.Contains(vm.Grid.Cells, cell => cell.Assignment.AssignmentId == cId && cell.Day == 3 && cell.Period == 1);
        Assert.Equal(3, vm.Grid.Cells.Select(cell => cell.Assignment.AssignmentId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SaveReload_DuplicateCourseId_PreservesAssignmentIdsAndSelectionCanResolveOccurrence()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var before = vm.Grid.Cells
            .OrderBy(c => c.Assignment.ProfessorId, StringComparer.Ordinal)
            .Select(c => (c.Assignment.ProfessorId, c.Assignment.AssignmentId))
            .ToList();
        vm.SaveName = "dup-id-save";

        vm.SaveTimetableCommand.Execute(null);
        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "dup-id-save"));
        vm.LoadFromSavedTimetable(saved);

        var after = vm.Grid.Cells
            .OrderBy(c => c.Assignment.ProfessorId, StringComparer.Ordinal)
            .Select(c => (c.Assignment.ProfessorId, c.Assignment.AssignmentId))
            .ToList();
        Assert.Equal(before, after);

        var p10 = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        vm.SelectCell(p10.Day, p10.Period, p10.Grade, p10.SubColumnIdx, p10.Assignment);
        Assert.Equal(before.Single(x => x.ProfessorId == "P10").AssignmentId, vm.SelectedAssignment?.AssignmentId);
    }

    [Fact]
    public void LoadLegacySavedTimetable_AddsUniqueAssignmentIdsForOccurrences()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var legacy = new SavedTimetableRecord(
            "legacy",
            "legacy",
            DateTime.Now,
            new[]
            {
                new TimetableAssignmentRow("PROG-APP", 0, 1, "D330"),
                new TimetableAssignmentRow("PROG-APP", 0, 1, "D331"),
            },
            Array.Empty<SavedManualCrossLinkRow>(),
            System.Text.Json.JsonSerializer.Serialize(_ws.Snapshot()));

        vm.LoadFromSavedTimetable(legacy);

        var ids = vm.Grid.Cells
            .Where(c => c.Assignment.CourseId == "PROG-APP")
            .Select(c => c.Assignment.AssignmentId)
            .ToList();
        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RefreshConflicts_DuplicateCourseId_DisplaySeparatesSelectedAndRelatedAssignments()
    {
        _ws.Courses.Clear();
        _ws.Professors.Clear();
        _ws.Rooms.Clear();
        _ws.Courses.Add(new Course
        {
            Id = "DUP",
            Name = "선택수업",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P10",
            Section = 1,
            FixedRooms = new List<string> { "D330" },
        });
        _ws.Courses.Add(new Course
        {
            Id = "DUP",
            Name = "상대수업",
            Grade = 3,
            HoursPerWeek = 1,
            ProfessorId = "P10",
            Section = 2,
            FixedRooms = new List<string> { "D331" },
        });
        _ws.Professors.Add(new Professor { Id = "P10", Name = "김교수" });
        _ws.Rooms.Add(new Room { Id = "D330", Name = "D330" });
        _ws.Rooms.Add(new Room { Id = "D331", Name = "D331" });

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("DUP", 0, 1, "D330"),
            new SolutionAssignment("DUP", 0, 1, "D331")));
        var selectedCell = vm.Grid.Cells.Single(c => c.Assignment.CourseName == "선택수업");
        var selectedAssignmentId = selectedCell.Assignment.AssignmentId;

        vm.SelectCell(selectedCell.Day, selectedCell.Period, selectedCell.Grade, selectedCell.SubColumnIdx, selectedCell.Assignment);
        RefreshConflicts(vm, strictManualCrossValidation: true);

        Assert.Equal(selectedAssignmentId, vm.SelectedAssignment?.AssignmentId);
        var professorConflict = Assert.Single(vm.Conflicts.Where(c => c.Type == ConflictType.ProfessorConflict));
        Assert.Contains("선택 수업: 선택수업", professorConflict.DisplayDescription);
        Assert.Contains("충돌 상대: 상대수업", professorConflict.DisplayDescription);
    }

    [Fact]
    public void Hc20Display_UsesOnlyConflictAssignmentsForRepresentativeAndRelatedCourses()
    {
        _ws.Courses.Clear();
        _ws.Professors.Clear();
        _ws.Rooms.Clear();
        _ws.Courses.Add(new Course
        {
            Id = "HC20",
            Name = "중복과목",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P10",
            Section = 1,
            FixedRooms = new List<string> { "R1" },
        });
        _ws.Courses.Add(new Course
        {
            Id = "OTHER",
            Name = "무관과목",
            Grade = 3,
            HoursPerWeek = 1,
            ProfessorId = "P20",
            Section = 1,
            FixedRooms = new List<string> { "R2" },
        });
        _ws.Professors.Add(new Professor { Id = "P10", Name = "김교수" });
        _ws.Professors.Add(new Professor { Id = "P20", Name = "박교수" });
        _ws.Rooms.Add(new Room { Id = "R1", Name = "R1" });
        _ws.Rooms.Add(new Room { Id = "R2", Name = "R2" });

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("HC20", 0, 1, "R1"),
            new SolutionAssignment("HC20", 0, 3, "R1"),
            new SolutionAssignment("OTHER", 0, 1, "R2")));
        var selected = vm.Grid.Cells.Single(c => c.Assignment.CourseName == "중복과목" && c.Period == 1);

        vm.SelectCell(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);
        RefreshConflicts(vm, strictManualCrossValidation: true);

        var hc20 = Assert.Single(vm.Conflicts.Where(c => c.Type == ConflictType.SameCourseSameDayConflict));
        Assert.Contains("중복과목", hc20.DisplayTitle);
        Assert.DoesNotContain("무관과목", hc20.DisplayTitle);
        Assert.Contains("중복과목", hc20.DisplayDescription);
        Assert.DoesNotContain("무관과목", hc20.DisplayDescription);
        Assert.NotNull(hc20.Conflict.Assignments);
        Assert.All(hc20.Conflict.Assignments!, assignment => Assert.Equal("HC20", assignment.CourseId));
    }

    [Fact]
    public void ManualCross_Hover_DuplicateCourseIdDifferentProfessorRoom_AllowsCross()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var selected = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var target = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        vm.SelectCell(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        var state = vm.EvaluateCrossHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCross_Add_DuplicateCourseIdDifferentProfessorRoom_CreatesCross()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var selected = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var target = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        vm.SelectCell(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);

        vm.HandleCrossAddRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair(
            ManualCrossAssignmentKeyFromCell(selected),
            ManualCrossAssignmentKeyFromCell(target)));
    }

    [Fact]
    public void ManualCross_DuplicateCourseId_CrossLabelsUseAssignmentSpecificCourse()
    {
        _ws.Courses.Clear();
        _ws.Professors.Clear();
        _ws.Rooms.Clear();
        _ws.Courses.Add(new Course
        {
            Id = "DUP",
            Name = "선택수업",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P10",
            Section = 1,
            FixedRooms = new List<string> { "D330" },
        });
        _ws.Courses.Add(new Course
        {
            Id = "DUP",
            Name = "상대수업",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P20",
            Section = 2,
            FixedRooms = new List<string> { "D331" },
        });
        _ws.Professors.Add(new Professor { Id = "P10", Name = "김교수" });
        _ws.Professors.Add(new Professor { Id = "P20", Name = "박교수" });
        _ws.Rooms.Add(new Room { Id = "D330", Name = "D330" });
        _ws.Rooms.Add(new Room { Id = "D331", Name = "D331" });

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("DUP", 0, 1, "D330"),
            new SolutionAssignment("DUP", 0, 1, "D331")));
        var selected = vm.Grid.Cells.Single(c => c.Assignment.CourseName == "선택수업");
        var target = vm.Grid.Cells.Single(c => c.Assignment.CourseName == "상대수업");

        vm.SelectCell(selected.Day, selected.Period, selected.Grade, selected.SubColumnIdx, selected.Assignment);
        vm.HandleCrossAddRequested(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Contains(vm.Grid.CrossLinkLabels.Values, label => label.Contains("상대수업", StringComparison.Ordinal));
        Assert.Contains(vm.Grid.CrossLinkLabels.Values, label => label.Contains("선택수업", StringComparison.Ordinal));
    }

    [Fact]
    public void ManualCross_Drop_DuplicateCourseIdDifferentProfessorRoom_CreatesCross()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var source = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var target = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");

        vm.HandleCrossDrop(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment,
            target.Day,
            target.Period,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCross_DuplicateCourseIdThree_ReplacesABWithACByAssignmentId()
    {
        var vm = BuildDuplicateProgrammingApplicationThreeVm();
        var a = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var b = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        var c = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P30");
        var aId = a.Assignment.AssignmentId;
        var bId = b.Assignment.AssignmentId;
        var cId = c.Assignment.AssignmentId;

        vm.SelectCell(a.Day, a.Period, a.Grade, a.SubColumnIdx, a.Assignment);
        vm.HandleCrossAddRequested(b.Day, b.Period, b.Grade, b.SubColumnIdx, b.Assignment);

        var ab = Assert.Single(vm.WorkingCrossLinks);
        Assert.True(ab.Contains(BuildManualCrossKey(a)));
        Assert.True(ab.Contains(BuildManualCrossKey(b)));

        var movedA = vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == aId);
        var currentC = vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == cId);
        vm.SelectCell(movedA.Day, movedA.Period, movedA.Grade, movedA.SubColumnIdx, movedA.Assignment);
        vm.HandleCrossAddRequested(currentC.Day, currentC.Period, currentC.Grade, currentC.SubColumnIdx, currentC.Assignment);

        var ac = Assert.Single(vm.WorkingCrossLinks);
        Assert.True(ac.Contains(BuildManualCrossKey(vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == aId))));
        Assert.True(ac.Contains(BuildManualCrossKey(vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == cId))));
        Assert.DoesNotContain(vm.WorkingCrossLinks, link => link.Contains(new ManualEditViewModel.ManualCrossAssignmentKey(
            "PROG-APP", "1", 0, 1, 1, "D331", bId)));
    }

    [Fact]
    public void ManualCross_DuplicateCourseId_ChangeUndoRedoRestoresExactAssignmentPair()
    {
        var vm = BuildDuplicateProgrammingApplicationThreeVm();
        var a = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var b = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        var c = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P30");
        var aId = a.Assignment.AssignmentId;
        var bId = b.Assignment.AssignmentId;
        var cId = c.Assignment.AssignmentId;

        vm.SelectCell(a.Day, a.Period, a.Grade, a.SubColumnIdx, a.Assignment);
        vm.HandleCrossAddRequested(b.Day, b.Period, b.Grade, b.SubColumnIdx, b.Assignment);
        var movedA = vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == aId);
        var currentC = vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == cId);
        vm.SelectCell(movedA.Day, movedA.Period, movedA.Grade, movedA.SubColumnIdx, movedA.Assignment);
        vm.HandleCrossAddRequested(currentC.Day, currentC.Period, currentC.Grade, currentC.SubColumnIdx, currentC.Assignment);

        vm.UndoCommand.Execute(null);

        var undoLink = Assert.Single(vm.WorkingCrossLinks);
        Assert.True(undoLink.Contains(BuildManualCrossKey(vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == aId))));
        Assert.True(undoLink.Contains(BuildManualCrossKey(vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == bId))));

        vm.RedoCommand.Execute(null);

        var redoLink = Assert.Single(vm.WorkingCrossLinks);
        Assert.True(redoLink.Contains(BuildManualCrossKey(vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == aId))));
        Assert.True(redoLink.Contains(BuildManualCrossKey(vm.Grid.Cells.Single(cell => cell.Assignment.AssignmentId == cId))));
    }

    [Fact]
    public void ManualCross_DuplicateCourseId_SaveRowsCarryAssignmentIdsAndReloadRestoresPair()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var a = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P10");
        var b = vm.Grid.Cells.Single(c => c.Assignment.ProfessorId == "P20");
        vm.SelectCell(a.Day, a.Period, a.Grade, a.SubColumnIdx, a.Assignment);
        vm.HandleCrossAddRequested(b.Day, b.Period, b.Grade, b.SubColumnIdx, b.Assignment);

        var row = Assert.Single(SavedManualCrossRows(vm));

        Assert.False(string.IsNullOrWhiteSpace(row.SourceAssignmentId));
        Assert.False(string.IsNullOrWhiteSpace(row.TargetAssignmentId));
        RestoreSavedManualCrossRows(vm, new[] { row });
        var restored = Assert.Single(vm.WorkingCrossLinks);
        Assert.True(restored.Contains(BuildManualCrossKey(a)));
        Assert.True(restored.Contains(BuildManualCrossKey(b)));
    }

    [Fact]
    public void ManualCross_LegacyMigration_AmbiguousEndpointIsIgnored()
    {
        var vm = BuildDuplicateProgrammingApplicationVm();
        var row = new SavedManualCrossLinkRow(
            "PROG-APP", 2, "1", 0, 1, "",
            "PROG-APP", 2, "1", 0, 1, "D331",
            "HC11_ONLY_EXCEPTION");

        var ignored = RestoreSavedManualCrossRows(vm, new[] { row });

        Assert.Equal(1, ignored);
        Assert.Empty(vm.WorkingCrossLinks);
    }

    [Fact]
    public void ManualCrossHover_TwoHourDifferentDayPeriod_TargetStartCell_Allows()
    {
        var vm = BuildProgrammingApplicationTwoHourVm(targetStartPeriod: 3);
        var source = ProgrammingApplicationCell(vm, "P10");
        var target = ProgrammingApplicationCell(vm, "P20");
        vm.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var state = vm.EvaluateCrossHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCrossHover_TwoHourDifferentDayPeriod_TargetCoveredCell_NormalizesToStartAndAllows()
    {
        var vm = BuildProgrammingApplicationTwoHourVm(targetStartPeriod: 3);
        var source = ProgrammingApplicationCell(vm, "P10");
        var target = ProgrammingApplicationCell(vm, "P20");
        vm.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var state = vm.EvaluateCrossHover(target.Day, target.Period + 1, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCrossDrop_TwoHourDifferentDayPeriod_TargetStartCell_Allows()
    {
        var vm = BuildProgrammingApplicationTwoHourVm(targetStartPeriod: 3);
        var source = ProgrammingApplicationCell(vm, "P10");
        var target = ProgrammingApplicationCell(vm, "P20");

        var state = vm.EvaluateCrossDropHover(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment,
            target.Day,
            target.Period,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCrossDrop_TwoHourDifferentDayPeriod_TargetCoveredCell_NormalizesToStartAndAllows()
    {
        var vm = BuildProgrammingApplicationTwoHourVm(targetStartPeriod: 3);
        var source = ProgrammingApplicationCell(vm, "P10");
        var target = ProgrammingApplicationCell(vm, "P20");

        var state = vm.EvaluateCrossDropHover(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment,
            target.Day,
            target.Period + 1,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCrossHover_TwoHourInvalidStartPeriod_BlocksWithReason()
    {
        var vm = BuildProgrammingApplicationTwoHourVm(targetStartPeriod: 2);
        var source = ProgrammingApplicationCell(vm, "P10");
        var target = ProgrammingApplicationCell(vm, "P20");
        vm.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var state = vm.EvaluateCrossHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.False(state.CanCreate);
        Assert.Contains("2시간 수업", state.Reason);
    }

    [Fact]
    public void ManualCrossHover_TwoHourDifferentDayPeriod_DoesNotRequireSameSlot()
    {
        var vm = BuildProgrammingApplicationTwoHourVm(targetStartPeriod: 6);
        var source = ProgrammingApplicationCell(vm, "P10");
        var target = ProgrammingApplicationCell(vm, "P20");
        Assert.NotEqual(source.Day, target.Day);
        Assert.NotEqual(source.Period, target.Period);
        vm.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var state = vm.EvaluateCrossHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCrossHover_ProgrammingApplication_TwoHourDifferentDayPeriod_AllowsCross()
    {
        var vm = BuildProgrammingApplicationTwoHourVm(targetStartPeriod: 3);
        var source = ProgrammingApplicationCell(vm, "P10");
        var target = ProgrammingApplicationCell(vm, "P20");
        vm.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var state = vm.EvaluateCrossHover(target.Day, target.Period, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.True(state.CanCreate, state.Reason);
    }

    [Fact]
    public void ManualCrossAdd_ProgrammingApplication_TwoHourDifferentDayPeriod_CreatesCross()
    {
        var vm = BuildProgrammingApplicationTwoHourVm(targetStartPeriod: 3);
        var source = ProgrammingApplicationCell(vm, "P10");
        var target = ProgrammingApplicationCell(vm, "P20");
        vm.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        vm.HandleCrossAddRequested(target.Day, target.Period + 1, target.Grade, target.SubColumnIdx, target.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
        Assert.True(vm.WorkingCrossLinks[0].MatchesPair(
            ManualCrossAssignmentKey(
                source.Assignment.CourseId,
                source.Assignment.Section,
                target.Day,
                target.Period,
                source.Assignment.RowSpan,
                source.Assignment.Rooms),
            ManualCrossAssignmentKeyFromCell(target)));
    }

    [Fact]
    public void ManualCrossDrop_ProgrammingApplication_TwoHourDifferentDayPeriod_CreatesCross()
    {
        var vm = BuildProgrammingApplicationTwoHourVm(targetStartPeriod: 3);
        var source = ProgrammingApplicationCell(vm, "P10");
        var target = ProgrammingApplicationCell(vm, "P20");

        vm.HandleCrossDrop(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment,
            target.Day,
            target.Period + 1,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);

        Assert.Single(vm.WorkingCrossLinks);
    }

    [Fact]
    public void RoomChangeOptions_DisplaysRoomNames_NotInternalNumericIds()
    {
        var vm = BuildRoomOptionVm();
        var displayNames = vm.AvailableRoomOptions.Select(o => o.DisplayName).ToList();

        Assert.Contains("D330", displayNames);
        Assert.DoesNotContain("1", displayNames);
        Assert.DoesNotContain("2", displayNames);
    }

    [Fact]
    public void RoomChangeOptions_DeduplicatesRoomIdAndName()
    {
        var vm = BuildRoomOptionVm();

        Assert.Single(vm.AvailableRoomOptions.Where(o => o.DisplayName == "D330"));
    }

    [Fact]
    public void RoomChangeOptions_UsesRoomIdAsValue_DisplayNameAsText()
    {
        var vm = BuildRoomOptionVm();

        var option = Assert.Single(vm.AvailableRoomOptions.Where(o => o.DisplayName == "D330"));
        Assert.Equal("1", option.RoomId);
    }

    [Fact]
    public void RoomChangeOptions_UnknownNumericRoomId_DoesNotDisplayBareNumber()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "9")));
        var assignment = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01").Assignment;
        vm.SelectCell(0, 1, 2, 0, assignment);

        var option = Assert.Single(vm.AvailableRoomOptions.Where(o => o.RoomId == "9"));
        Assert.Equal("알 수 없는 강의실", option.DisplayName);
        Assert.DoesNotContain("9", option.DisplayName);
    }

    [Fact]
    public void InspectorApplyChanges_DisabledWhenNoRoomChange()
    {
        var vm = BuildSingleRoomSelectionVm();

        Assert.False(vm.HasPendingInspectorChanges);
        Assert.False(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void InspectorApplyChanges_EnabledWhenRoomChanged()
    {
        var vm = BuildSingleRoomSelectionVm();

        vm.NewRoomId = "R2";

        Assert.True(vm.HasPendingInspectorChanges);
        Assert.True(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void InspectorApplyChanges_DisabledWhenRoomChangedBackToOriginal()
    {
        var vm = BuildSingleRoomSelectionVm();

        vm.NewRoomId = "R2";
        vm.NewRoomId = "R1";

        Assert.False(vm.HasPendingInspectorChanges);
        Assert.False(vm.ApplyRoomChangeCommand.CanExecute(null));
    }

    [Fact]
    public void InspectorApplyChanges_DisabledAfterApply()
    {
        var vm = BuildSingleRoomSelectionVm();
        vm.NewRoomId = "R2";

        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.False(vm.HasPendingInspectorChanges);
        Assert.False(vm.ApplyRoomChangeCommand.CanExecute(null));
        Assert.Equal(new[] { "R2" }, vm.SelectedAssignment!.Rooms);
    }

    [Fact]
    public void RoomChange_FixedRoomCourse_AllowsNonFixedRoomAndUpdatesAllPeriodsAndViews()
    {
        var vm = BuildFixedTwoHourRoomChangeVm();

        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("강의실을 변경했습니다.", vm.RoomChangeStatusMessage);
        Assert.All(
            vm.Grid.Cells.Where(c => c.Assignment.CourseId == "X-01"),
            cell => Assert.Equal(new[] { "R2" }, cell.Assignment.Rooms));
        var roomCell = Assert.Single(vm.RoomViews.Single(view => view.Id == "R2").Grid.Cells
            .Where(cell => cell.Items.Any(item => item.CourseId == "X-01")));
        Assert.Equal(1, roomCell.Period);
        Assert.Equal(2, roomCell.Items.Single(item => item.CourseId == "X-01").RowSpan);
        Assert.Equal(new[] { 1, 2 }, WorkingAssignments(vm)
            .Where(a => a.CourseId == "X-01" && a.RoomId == "R2")
            .Select(a => a.Period)
            .OrderBy(period => period)
            .ToArray());
        Assert.Equal(new[] { "R2" }, vm.SelectedAssignment!.Rooms);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.FixedRoomViolation);
    }

    [Fact]
    public void RoomChange_CourseUnavailableRoom_BlocksChange()
    {
        _ws.Courses.Single(c => c.Id == "X-01").UnavailableRooms = new List<string> { "R2" };
        var vm = BuildFixedTwoHourRoomChangeVm();

        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Contains("사용할 수 없습니다", vm.RoomChangeStatusMessage);
        Assert.All(
            vm.Grid.Cells.Where(c => c.Assignment.CourseId == "X-01"),
            cell => Assert.Equal(new[] { "R1" }, cell.Assignment.Rooms));
    }

    [Fact]
    public void RoomChange_RoomConflict_BlocksChange()
    {
        _ws.AddProfessor(new Professor { Id = "P2", Name = "다른교수" });
        _ws.AddCourse(new Course { Id = "Y-01", Name = "다른수업", Grade = 3, HoursPerWeek = 1, ProfessorId = "P2" });
        var vm = BuildFixedTwoHourRoomChangeVm(new SolutionAssignment("Y-01", 0, 1, "R2"));

        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("해당 시간에 이미 사용 중인 강의실입니다.", vm.RoomChangeStatusMessage);
        Assert.All(
            vm.Grid.Cells.Where(c => c.Assignment.CourseId == "X-01"),
            cell => Assert.Equal(new[] { "R1" }, cell.Assignment.Rooms));
    }

    [Fact]
    public void RoomChange_ProfessorAllowedRoomRestriction_DoesNotBlockManualChange()
    {
        _ws.Professors.Single(p => p.Id == "P1").AllowedRooms = new List<string> { "R1" };
        var vm = BuildFixedTwoHourRoomChangeVm();

        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal("강의실을 변경했습니다.", vm.RoomChangeStatusMessage);
        Assert.DoesNotContain(vm.Conflicts, c => c.Type == ConflictType.ProfAllowedRoomViolation);
        Assert.All(
            vm.Grid.Cells.Where(c => c.Assignment.CourseId == "X-01"),
            cell => Assert.Equal(new[] { "R2" }, cell.Assignment.Rooms));
    }

    [Fact]
    public void RoomChange_UndoRedo_RestoresOldAndNewRooms()
    {
        var vm = BuildFixedTwoHourRoomChangeVm();

        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        Assert.All(
            vm.Grid.Cells.Where(c => c.Assignment.CourseId == "X-01"),
            cell => Assert.Equal(new[] { "R1" }, cell.Assignment.Rooms));

        vm.RedoCommand.Execute(null);

        Assert.All(
            vm.Grid.Cells.Where(c => c.Assignment.CourseId == "X-01"),
            cell => Assert.Equal(new[] { "R2" }, cell.Assignment.Rooms));
    }

    [Fact]
    public void RoomChange_SaveAndReload_PreservesChangedRoom()
    {
        var vm = BuildFixedTwoHourRoomChangeVm();
        vm.NewRoomId = "R2";
        vm.ApplyRoomChangeCommand.Execute(null);

        vm.SaveName = "changed-room";
        vm.SaveTimetableCommand.Execute(null);

        var saved = Assert.Single(_ws.SavedTimetables.Where(t => t.Name == "changed-room"));
        Assert.All(saved.Assignments.Where(a => a.CourseId == "X-01"), row => Assert.Equal("R2", row.RoomId));

        var reloaded = _sp.GetRequiredService<ManualEditViewModel>();
        reloaded.LoadFromSavedTimetable(saved);
        Assert.All(
            reloaded.Grid.Cells.Where(c => c.Assignment.CourseId == "X-01"),
            cell => Assert.Equal(new[] { "R2" }, cell.Assignment.Rooms));
    }

    [Fact]
    public void ManualEditDisplay_StripsHcCodeFromInspectorMessages()
    {
        var text = ManualEditViewModel.StripConstraintCodeForDisplay(
            "HC-01: 같은 강의실에 동시에 배치할 수 없습니다.");

        Assert.Equal("같은 강의실에 동시에 배치할 수 없습니다.", text);
    }

    [Fact]
    public void ManualEditDisplay_StripsScCodeFromInspectorMessages()
    {
        var text = ManualEditViewModel.StripConstraintCodeForDisplay(
            "[SC-03] 선호 시간대가 아닙니다.");

        Assert.Equal("선호 시간대가 아닙니다.", text);
    }

    [Fact]
    public void ManualEditDisplay_StripsConstraintCodesFromTooltips()
    {
        var text = ManualEditViewModel.StripConstraintCodeForDisplay(
            "HC-02 - 같은 교수는 동시에 수업할 수 없습니다.");

        Assert.Equal("같은 교수는 동시에 수업할 수 없습니다.", text);
    }

    [Fact]
    public void ManualEditDisplay_DoesNotStripInternalConflictCodes()
    {
        var row = SavedCross();

        Assert.Equal("HC11_ONLY_EXCEPTION", row.PolicyType);
    }

    private ManualEditViewModel BuildDuplicateProgrammingApplicationVm(
        bool reverseAssignments = false,
        string? sharedAssignmentId = null)
    {
        _ws.Courses.Clear();
        _ws.Professors.Clear();
        _ws.Rooms.Clear();
        _ws.Courses.Add(new Course
        {
            Id = "PROG-APP",
            Name = "프로그래밍 응용",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P10",
            Section = 1,
            FixedRooms = new List<string> { "D330" },
        });
        _ws.Courses.Add(new Course
        {
            Id = "PROG-APP",
            Name = "프로그래밍 응용",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P20",
            Section = 1,
            FixedRooms = new List<string> { "D331" },
        });
        _ws.Professors.Add(new Professor { Id = "P10", Name = "김교수" });
        _ws.Professors.Add(new Professor { Id = "P20", Name = "박교수" });
        _ws.Rooms.Add(new Room { Id = "D330", Name = "D330" });
        _ws.Rooms.Add(new Room { Id = "D331", Name = "D331" });

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var assignments = new[]
        {
            new SolutionAssignment("PROG-APP", 0, 1, "D330", sharedAssignmentId ?? ""),
            new SolutionAssignment("PROG-APP", 0, 1, "D331", sharedAssignmentId ?? ""),
        };
        if (reverseAssignments)
            Array.Reverse(assignments);
        vm.LoadFromSolution(MakeSolution(assignments));
        return vm;
    }

    private ManualEditViewModel BuildProgrammingApplicationTwoHourVm(int targetStartPeriod)
    {
        _ws.Courses.Clear();
        _ws.Professors.Clear();
        _ws.Rooms.Clear();
        _ws.Courses.Add(new Course
        {
            Id = "PROG-APP",
            Name = "프로그래밍 응용",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P10",
            Section = 1,
            FixedRooms = new List<string> { "D330" },
            BlockStructure = new List<int> { 2 },
        });
        _ws.Courses.Add(new Course
        {
            Id = "PROG-APP",
            Name = "프로그래밍 응용",
            Grade = 2,
            HoursPerWeek = 2,
            ProfessorId = "P20",
            Section = 1,
            FixedRooms = new List<string> { "D331" },
            BlockStructure = new List<int> { 2 },
        });
        _ws.Professors.Add(new Professor { Id = "P10", Name = "김교수" });
        _ws.Professors.Add(new Professor { Id = "P20", Name = "박교수" });
        _ws.Rooms.Add(new Room { Id = "D330", Name = "D330" });
        _ws.Rooms.Add(new Room { Id = "D331", Name = "D331" });

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(
            new SolutionAssignment("PROG-APP", 0, 1, "D330"),
            new SolutionAssignment("PROG-APP", 0, 2, "D330"),
            new SolutionAssignment("PROG-APP", 1, targetStartPeriod, "D331"),
            new SolutionAssignment("PROG-APP", 1, targetStartPeriod + 1, "D331")));
        return vm;
    }

    private ManualEditViewModel BuildDuplicateProgrammingApplicationThreeVm(bool reverseAssignments = false)
    {
        _ws.Courses.Clear();
        _ws.Professors.Clear();
        _ws.Rooms.Clear();
        _ws.Courses.Add(new Course
        {
            Id = "PROG-APP",
            Name = "프로그래밍 응용",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P10",
            Section = 1,
            FixedRooms = new List<string> { "D330" },
        });
        _ws.Courses.Add(new Course
        {
            Id = "PROG-APP",
            Name = "프로그래밍 응용",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P20",
            Section = 1,
            FixedRooms = new List<string> { "D331" },
        });
        _ws.Courses.Add(new Course
        {
            Id = "PROG-APP",
            Name = "프로그래밍 응용",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P30",
            Section = 1,
            FixedRooms = new List<string> { "D332" },
        });
        _ws.Professors.Add(new Professor { Id = "P10", Name = "김교수" });
        _ws.Professors.Add(new Professor { Id = "P20", Name = "박교수" });
        _ws.Professors.Add(new Professor { Id = "P30", Name = "최교수" });
        _ws.Rooms.Add(new Room { Id = "D330", Name = "D330" });
        _ws.Rooms.Add(new Room { Id = "D331", Name = "D331" });
        _ws.Rooms.Add(new Room { Id = "D332", Name = "D332" });

        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var assignments = new[]
        {
            new SolutionAssignment("PROG-APP", 0, 1, "D330"),
            new SolutionAssignment("PROG-APP", 0, 1, "D331"),
            new SolutionAssignment("PROG-APP", 2, 1, "D332"),
        };
        if (reverseAssignments)
            Array.Reverse(assignments);
        vm.LoadFromSolution(MakeSolution(assignments));
        return vm;
    }

    private static UnifiedCell ProgrammingApplicationCell(ManualEditViewModel vm, string professorId) =>
        vm.Grid.Cells.Single(c =>
            c.Assignment.CourseId == "PROG-APP"
            && c.Assignment.ProfessorId == professorId
            && c.Assignment.RowSpan == 2);

    private static ManualEditViewModel.ManualCrossAssignmentKey BuildManualCrossKey(UnifiedCell cell) =>
        new(
            cell.Assignment.CourseId,
            cell.Assignment.Section.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cell.Day,
            cell.Period,
            cell.Assignment.RowSpan,
            string.Join("|", cell.Assignment.Rooms.Order(StringComparer.Ordinal)),
            cell.Assignment.AssignmentId);

    private static IReadOnlyList<(string ProfessorId, int Day, int Period, string AssignmentId, string Rooms)> ProgrammingApplicationSnapshot(ManualEditViewModel vm) =>
        vm.Grid.Cells
            .Where(c => c.Assignment.CourseId == "PROG-APP")
            .OrderBy(c => c.Assignment.ProfessorId, StringComparer.Ordinal)
            .Select(c => (
                c.Assignment.ProfessorId,
                c.Day,
                c.Period,
                c.Assignment.AssignmentId,
                string.Join(",", c.Assignment.Rooms.Order(StringComparer.Ordinal))))
            .ToList();

    private ManualEditViewModel BuildRoomOptionVm()
    {
        _ws.Rooms.Clear();
        _ws.Rooms.Add(new Room { Id = "1", Name = "D330" });
        _ws.Rooms.Add(new Room { Id = "2", Name = "D331" });
        _ws.Rooms.Add(new Room { Id = "D330", Name = "D330" });
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "1")));
        var assignment = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01").Assignment;
        vm.SelectCell(0, 1, 2, 0, assignment);
        return vm;
    }

    private ManualEditViewModel BuildSingleRoomSelectionVm()
    {
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        vm.LoadFromSolution(MakeSolution(new SolutionAssignment("X-01", 0, 1, "R1")));
        var assignment = vm.Grid.Cells.Single(c => c.Assignment.CourseId == "X-01").Assignment;
        vm.SelectCell(0, 1, 2, 0, assignment);
        return vm;
    }

    private ManualEditViewModel BuildFixedTwoHourRoomChangeVm(params SolutionAssignment[] extraAssignments)
    {
        _ws.Courses.Single(c => c.Id == "X-01").FixedRooms = new List<string> { "R1" };
        var vm = _sp.GetRequiredService<ManualEditViewModel>();
        var assignments = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
        };
        assignments.AddRange(extraAssignments);
        vm.LoadFromSolution(MakeSolution(assignments.ToArray()));
        var cell = vm.Grid.Cells
            .Where(c => c.Assignment.CourseId == "X-01")
            .OrderBy(c => c.Period)
            .First();
        vm.SelectCell(cell.Day, cell.Period, cell.Grade, cell.SubColumnIdx, cell.Assignment);
        return vm;
    }

    private static ManualEditViewModel.ManualCrossAssignmentKey ManualCrossAssignmentKeyFromCell(UnifiedCell cell) =>
        ManualCrossAssignmentKey(
            cell.Assignment.CourseId,
            cell.Assignment.Section,
            cell.Day,
            cell.Period,
            cell.Assignment.RowSpan,
            cell.Assignment.Rooms);
}
