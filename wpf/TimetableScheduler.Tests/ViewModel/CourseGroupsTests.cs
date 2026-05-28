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
    public void NonFixed_TwoSections_ProducesOneGroupRow()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));

        var vm = MakeVm();

        Assert.Single(vm.CourseGroups);
        Assert.Equal(2, vm.CourseGroups[0].Sections.Count);
        Assert.Equal("GA1004", vm.CourseGroups[0].BaseId);
        Assert.False(vm.CourseGroups[0].IsFixedIndividual);
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
}
