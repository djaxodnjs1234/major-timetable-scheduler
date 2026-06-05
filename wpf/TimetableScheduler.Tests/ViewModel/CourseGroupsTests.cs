using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.Tests.ViewModel;

public class CourseGroupsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteRepository _repo;
    private readonly WorkspaceService _workspace;

    public CourseGroupsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cg_test_{Guid.NewGuid():N}.db");
        _repo = new SqliteRepository(_dbPath);
        _workspace = new WorkspaceService(_repo);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static Course MakeCourse(string id, int section, bool isFixed = false) => new()
    {
        Id = id, Name = "Test", Grade = 2, HoursPerWeek = 3, Section = section,
        CourseType = "전필", IsFixed = isFixed,
        BlockStructure = new List<int> { 2, 1 },
    };

    private DataInputViewModel MakeVm() =>
        new(_workspace, null!);

    [Fact]
    public void SolverAdvancedPerAttemptTime_DefaultsDisabledAndFiveSeconds()
    {
        var vm = MakeVm();

        Assert.False(vm.UseAdvancedPerSolveTimeSec);
        Assert.Equal(5, vm.PerSolveTimeSec);
    }

    [Fact]
    public void NonFixed_TwoSections_ProducesOneGroupRow()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));

        var vm = MakeVm();

        Assert.Single(vm.CourseGroups);
        Assert.Equal(2, vm.CourseGroups[0].Sections.Count);
        Assert.Equal("GA1004", vm.CourseGroups[0].BaseId);
        Assert.Equal("2개", vm.CourseGroups[0].HeaderSectionInfo);
        Assert.False(vm.CourseGroups[0].IsFixedIndividual);
    }

    [Fact]
    public void AddNew_Course_GeneratesNextNumericId()
    {
        _workspace.AddCourse(MakeCourse("1-01", 1));
        var vm = MakeVm();
        vm.SelectedCategory = InputCategory.Course;
        vm.NewName = "자동생성과목";

        vm.AddNewCommand.Execute(null);

        var added = _workspace.Courses.Single(c => c.Name == "자동생성과목");
        Assert.Equal("2", added.Id);
        Assert.Equal(1, added.Section);
    }

    [Fact]
    public void ProfessorUnavailableRooms_HidesEmptyRoomMetadata()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "D330" });
        _workspace.AddRoom(new Room { Id = "R2", Name = "LAB1", IsLab = true });
        _workspace.AddRoom(new Room { Id = "R3", Name = "D331", Capacity = 40 });
        _workspace.AddProfessor(new Professor
        {
            Id = "P1",
            Name = "교수1",
            UnavailableRooms = new List<string> { "R1", "R2", "R3" },
        });

        var vm = MakeVm();

        Assert.Equal("D330, LAB1 (실습실), D331 (40명)", vm.ProfessorItems.Single().HeaderUnavailableRooms);
    }

    [Fact]
    public void RoomRows_MarkFixedRoomsAsImportedEvenWhenDepartmentIsBlank()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "D330" });
        _workspace.AddRoom(new Room { Id = "R2", Name = "수동강의실" });
        var course = MakeCourse("1-01", 1);
        course.FixedRooms = new List<string> { "R1" };
        _workspace.AddCourse(course);

        var vm = MakeVm();

        Assert.True(vm.RoomItems.Single(r => r.Room.Id == "R1").IsImportedFromExcel);
        Assert.False(vm.RoomItems.Single(r => r.Room.Id == "R2").IsImportedFromExcel);
    }

    [Fact]
    public void NonFixed_OneSection_ProducesOneGroupRow()
    {
        _workspace.AddCourse(MakeCourse("GA1005-01", 1));

        var vm = MakeVm();

        Assert.Single(vm.CourseGroups);
        Assert.Single(vm.CourseGroups[0].Sections);
    }

    [Fact]
    public void Fixed_TwoSections_ProducesTwoIndividualRows()
    {
        _workspace.AddCourse(MakeCourse("GA1005-01", 1, isFixed: true));
        _workspace.AddCourse(MakeCourse("GA1005-02", 2, isFixed: true));

        var vm = MakeVm();

        Assert.Equal(2, vm.CourseGroups.Count);
        Assert.True(vm.CourseGroups[0].IsFixedIndividual);
        Assert.True(vm.CourseGroups[1].IsFixedIndividual);
        Assert.Single(vm.CourseGroups[0].Sections);
        Assert.Single(vm.CourseGroups[1].Sections);
    }

    [Fact]
    public void Mixed_FixedAndNonFixed_SplitsCorrectly()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));           // non-fixed group
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));
        _workspace.AddCourse(MakeCourse("GA1005-01", 1, isFixed: true));  // fixed individual
        _workspace.AddCourse(MakeCourse("GA1005-02", 2, isFixed: true));

        var vm = MakeVm();

        // GA1004 group (1 row) + GA1005-01 + GA1005-02 = 3 rows total
        Assert.Equal(3, vm.CourseGroups.Count);
    }

    [Fact]
    public void CourseGroups_UpdatesWhenWorkspaceChanges()
    {
        var vm = MakeVm();
        Assert.Empty(vm.CourseGroups);

        _workspace.AddCourse(MakeCourse("GA1004-01", 1));

        Assert.Single(vm.CourseGroups);
    }

    [Fact]
    public void DisplayLabel_NonFixed_ContainsSectionLetters()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));

        var vm = MakeVm();

        Assert.Contains("A·B", vm.CourseGroups[0].DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_Fixed_ContainsStar()
    {
        _workspace.AddCourse(MakeCourse("GA1005-01", 1, isFixed: true));

        var vm = MakeVm();

        Assert.Contains("★", vm.CourseGroups[0].DisplayLabel);
    }

    [Fact]
    public void AddSection_AddsNextSectionAndRebuildsGroups()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        vm.AddSectionCommand.Execute(item);

        Assert.Single(vm.CourseGroups); // still 1 group row
        Assert.Equal(2, vm.CourseGroups[0].Sections.Count);
        Assert.Equal("GA1004-02", vm.CourseGroups[0].Sections[1].Id);
    }

    [Fact]
    public void AddSection_CopiesSharedFieldsFromFirstSection()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        vm.AddSectionCommand.Execute(item);

        var added = vm.CourseGroups[0].Sections[1];
        Assert.Equal("Test", added.Name);
        Assert.Equal(2, added.Grade);
        Assert.Equal(3, added.HoursPerWeek);
        Assert.Equal("전필", added.CourseType);
    }

    [Fact]
    public void RemoveSection_RemovesLastSection()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        vm.RemoveSectionCommand.Execute(item);

        Assert.Single(vm.CourseGroups);
        Assert.Single(vm.CourseGroups[0].Sections);
        Assert.Equal("GA1004-01", vm.CourseGroups[0].Sections[0].Id);
    }

    [Fact]
    public void RemoveSection_NoOp_WhenOnlyOneSection()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        vm.RemoveSectionCommand.Execute(item);

        Assert.Single(vm.CourseGroups[0].Sections); // unchanged
        Assert.Equal("GA1004-01", _workspace.Courses[0].Id);
    }

    [Fact]
    public void SaveGroup_PropagatesIsFixed_ToAllSections()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];
        item.Sections[0].IsFixed = true;

        vm.SaveGroupCommand.Execute(item);

        var courses = _workspace.Courses.OrderBy(c => c.Id).ToList();
        Assert.True(courses[0].IsFixed);
        Assert.True(courses[1].IsFixed);
    }

    [Fact]
    public void SaveGroup_PropagatesUnavailableRooms_ToAllSectionsAndStopsEditing()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];
        item.IsEditing = true;
        item.Sections[0].FixedRooms = new List<string> { "R1" };
        item.Sections[0].UnavailableRooms = new List<string> { "R2" };

        vm.SaveGroupCommand.Execute(item);

        var courses = _workspace.Courses.OrderBy(c => c.Id).ToList();
        Assert.Equal(new[] { "R1" }, courses[0].FixedRooms);
        Assert.Equal(new[] { "R1" }, courses[1].FixedRooms);
        Assert.Equal(new[] { "R2" }, courses[0].UnavailableRooms);
        Assert.Equal(new[] { "R2" }, courses[1].UnavailableRooms);
        Assert.False(item.IsEditing);
    }

    [Fact]
    public void CrossManager_AddsSelectedCoursesAndRebuildsLists()
    {
        _workspace.AddCourse(MakeCourse("A-01", 1));
        _workspace.AddCourse(MakeCourse("B-01", 1));
        var vm = MakeVm();

        Assert.Equal(new[] { "A", "B" }, vm.CrossCandidateItems.Select(i => i.Id));
        vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked = true;
        vm.CrossCandidateItems.Single(i => i.Id == "B").IsChecked = true;

        vm.AddCrossCommand.Execute(null);

        var cross = Assert.Single(_workspace.CrossGroups);
        Assert.Equal("X001", cross.Id);
        Assert.Equal(new[] { "A", "B" }, cross.BaseIds);
        Assert.Single(vm.CrossGroupItems);
        Assert.Contains("A(Test)", vm.CrossGroupItems[0].Display);
        Assert.All(vm.CrossCandidateItems, item => Assert.False(item.IsChecked));
        Assert.Equal("Cross를 추가했습니다.", vm.CrossStatusMessage);
        Assert.Equal("B", vm.CourseGroups.Single(g => g.BaseId == "A").HeaderCrossGroups);
    }

    [Fact]
    public void CrossManager_DeleteSelectedCross_RemovesWorkspaceGroup()
    {
        _workspace.AddCourse(MakeCourse("A-01", 1));
        _workspace.AddCourse(MakeCourse("B-01", 1));
        _workspace.AddCrossGroup(new CrossGroup { Id = "legacy", BaseIds = new List<string> { "A", "B" } });
        var vm = MakeVm();

        var item = Assert.Single(vm.CrossGroupItems);
        vm.DeleteCrossCommand.Execute(item);

        Assert.Empty(_workspace.CrossGroups);
        Assert.Empty(vm.CrossGroupItems);
        Assert.Equal("선택한 Cross를 삭제했습니다.", vm.CrossStatusMessage);
    }

    [Fact]
    public void CrossManager_RejectsInvalidSelection()
    {
        _workspace.AddCourse(MakeCourse("A-01", 1));
        var differentHours = MakeCourse("B-01", 1);
        differentHours.HoursPerWeek = 4;
        _workspace.AddCourse(differentHours);
        var vm = MakeVm();

        vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked = true;
        vm.AddCrossCommand.Execute(null);
        Assert.Equal("Cross는 과목 2개만 선택할 수 있습니다.", vm.CrossStatusMessage);

        vm.CrossCandidateItems.Single(i => i.Id == "B").IsChecked = true;
        vm.AddCrossCommand.Execute(null);

        Assert.Empty(_workspace.CrossGroups);
        Assert.Equal("Cross로 묶으려면 총 시수가 동일해야 합니다.", vm.CrossStatusMessage);
    }

    [Fact]
    public void CrossManager_RejectsDuplicateMembership()
    {
        _workspace.AddCourse(MakeCourse("A-01", 1));
        _workspace.AddCourse(MakeCourse("B-01", 1));
        _workspace.AddCourse(MakeCourse("C-01", 1));
        _workspace.AddCrossGroup(new CrossGroup { Id = "X001", BaseIds = new List<string> { "A", "B" } });
        var vm = MakeVm();

        vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked = true;
        vm.CrossCandidateItems.Single(i => i.Id == "C").IsChecked = true;
        vm.AddCrossCommand.Execute(null);

        Assert.Single(_workspace.CrossGroups);
        Assert.Equal("이미 다른 Cross에 속한 과목이 있습니다: A", vm.CrossStatusMessage);
    }

    [Fact]
    public async Task Solve_Infeasible_DoesNotEnablePreviewOrFireCompleted()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "강의실1" });
        _workspace.AddProfessor(new Professor
        {
            Id = "P1",
            Name = "교수1",
            UnavailableRooms = new List<string> { "R1" },
        });
        _workspace.AddCourse(new Course
        {
            Id = "1-01",
            Name = "불가능과목",
            Grade = 1,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            Section = 1,
            BlockStructure = new List<int> { 1 },
        });
        var vm = new DataInputViewModel(_workspace, new SolverService())
        {
            TotalSolutions = 1,
            TimeLimitSec = 1,
            UseSc01 = false,
            UseSc02 = false,
            UseSc03 = false,
        };
        var completedCount = 0;
        vm.SolveCompleted += (_, _) => completedCount++;

        await vm.SolveCommand.ExecuteAsync(null);

        Assert.False(vm.IsSolveComplete);
        Assert.Empty(vm.RankedResults);
        Assert.Equal(0, completedCount);
        Assert.Contains("원인", vm.StatusMessage);
        Assert.DoesNotContain("top", vm.StatusMessage);
    }
}
