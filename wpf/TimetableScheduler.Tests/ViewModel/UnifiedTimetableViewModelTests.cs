using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.Tests.ViewModel;

public class UnifiedTimetableViewModelTests
{
    [Fact]
    public void Render_EmptyAssignment_AllDaysCollapsedToPlaceholder()
    {
        var vm = new UnifiedTimetableViewModel();
        vm.Render(Array.Empty<SolutionAssignment>(), Array.Empty<Course>());

        Assert.Equal(5, vm.DayGroups.Count);
        Assert.All(vm.DayGroups, dg =>
        {
            Assert.Single(dg.Grades);
            Assert.Null(dg.Grades[0].Grade);
        });
    }

    [Fact]
    public void Render_TwoGradesOnOneDay_TwoColumns()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "G2", Grade = 2 },
            new() { Id = "G3", Grade = 3 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("G2", 0, 1, "R"),
            new("G3", 0, 1, "R"),
        };
        vm.Render(assignment, courses);

        // day 0 has grade 2 + grade 3; other days collapsed
        var d0 = vm.DayGroups[0];
        Assert.Equal(2, d0.Grades.Count);
        Assert.Equal(2, d0.Grades[0].Grade);
        Assert.Equal(3, d0.Grades[1].Grade);
    }

    [Fact]
    public void ExpandAllGrades_True_AllFourGradesShown()
    {
        var vm = new UnifiedTimetableViewModel { ExpandAllGrades = true };
        var courses = new List<Course>
        {
            new() { Id = "G2", Grade = 2 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("G2", 0, 1, "R"),
        };
        vm.Render(assignment, courses);

        // ExpandAllGrades=true → all 4 grades shown even if empty
        Assert.Equal(4, vm.DayGroups[0].Grades.Count);
    }

    [Fact]
    public void Render_TwoSectionsSameSlot_GradeColumnWidthIsTwo()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Grade = 2, Section = 1 },
            new() { Id = "X-02", Grade = 2, Section = 2 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-02", 0, 1, "R2"),
        };
        vm.Render(assignment, courses);

        var d0 = vm.DayGroups[0];
        Assert.Single(d0.Grades);
        Assert.Equal(2, d0.Grades[0].Grade);
        Assert.Equal(2, d0.Grades[0].Width);  // 2 sections → width 2
    }

    [Fact]
    public void Render_DifferentSectionsInConsecutivePeriods_NotMerged()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1" },
            new() { Id = "X-02", Name = "전공", Grade = 2, Section = 2, ProfessorId = "P1" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-02", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        Assert.Contains(vm.Cells, c => c.Period == 1 && c.Assignment.CourseId == "X-01" && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Cells, c => c.Period == 2 && c.Assignment.CourseId == "X-02" && c.Assignment.RowSpan == 1);
    }

    [Fact]
    public void Render_SameCourseConsecutivePeriodsSameRoom_Merged()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        var cell = Assert.Single(vm.Cells);
        Assert.Equal(1, cell.Period);
        Assert.Equal(2, cell.Assignment.RowSpan);
    }

    [Fact]
    public void Render_SameCourseConsecutivePeriodsDifferentRooms_NotMerged()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R2"),
        };

        vm.Render(assignment, courses);

        Assert.Contains(vm.Cells, c => c.Period == 1 && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Cells, c => c.Period == 2 && c.Assignment.RowSpan == 1);
    }
}
