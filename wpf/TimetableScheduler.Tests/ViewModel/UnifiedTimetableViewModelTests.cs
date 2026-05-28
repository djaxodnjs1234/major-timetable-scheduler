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
    public void Render_DifferentSectionInsideExistingRowSpan_UsesSeparateSubColumn()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 2, ProfessorId = "P1" },
            new() { Id = "X-02", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
            new("X-02", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        var rowSpanBlock = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "X-01");
        var otherSection = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "X-02");
        Assert.Equal(2, rowSpanBlock.Assignment.RowSpan);
        Assert.Equal(1, otherSection.Assignment.RowSpan);
        Assert.Equal(2, otherSection.Period);
        Assert.NotEqual(rowSpanBlock.SubColumnIdx, otherSection.SubColumnIdx);
        Assert.Equal(2, vm.DayGroups[0].Grades.Single(g => g.Grade == 2).Width);
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
    public void Render_StructuredMergeMode_SameCourseTwoHourBlock_Merged()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 2 } },
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
    public void Render_StructuredMergeMode_EmptyBlockStructureUsesHoursPerWeek()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", HoursPerWeek = 2 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        var cell = Assert.Single(vm.Cells);
        Assert.Equal(2, cell.Assignment.RowSpan);
    }

    [Fact]
    public void Render_StructuredMergeMode_SameCourseWithoutBlockEvidence_NotMerged()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 1, 1 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        Assert.Equal(2, vm.Cells.Count(c => c.Assignment.CourseId == "X-01"));
        Assert.All(vm.Cells.Where(c => c.Assignment.CourseId == "X-01"), c => Assert.Equal(1, c.Assignment.RowSpan));
    }

    [Fact]
    public void Render_StructuredMergeMode_RowSpanDoesNotHideDifferentSection()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 2 } },
            new() { Id = "X-02", Name = "전공", Grade = 2, Section = 2, ProfessorId = "P1", BlockStructure = new List<int> { 1 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
            new("X-02", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        var rowSpanBlock = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "X-01");
        var otherSection = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "X-02");
        Assert.Equal(2, rowSpanBlock.Assignment.RowSpan);
        Assert.Equal(1, otherSection.Assignment.RowSpan);
        Assert.Equal(2, otherSection.Period);
        Assert.NotEqual(rowSpanBlock.SubColumnIdx, otherSection.SubColumnIdx);
        Assert.Equal(2, vm.DayGroups[0].Grades.Single(g => g.Grade == 2).Width);
    }

    [Fact]
    public void Render_StructuredMergeMode_OneHourAboveTwoHourBlock_KeepsOriginalRows()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "한시간", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 1 } },
            new() { Id = "B-02", Name = "두시간", Grade = 2, Section = 1, ProfessorId = "P2", BlockStructure = new List<int> { 2 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("A-01", 0, 1, "R1"),
            new("B-02", 0, 2, "R1"),
            new("B-02", 0, 3, "R1"),
        };

        vm.Render(assignment, courses);

        var oneHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "A-01");
        var twoHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "B-02");
        Assert.Equal(1, oneHour.Period);
        Assert.Equal(1, oneHour.Assignment.RowSpan);
        Assert.Equal(2, twoHour.Period);
        Assert.Equal(2, twoHour.Assignment.RowSpan);
    }

    [Fact]
    public void Render_StructuredMergeMode_TwoHourBlockAboveOneHour_KeepsOriginalRows()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "한시간", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 1 } },
            new() { Id = "B-02", Name = "두시간", Grade = 2, Section = 1, ProfessorId = "P2", BlockStructure = new List<int> { 2 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("B-02", 0, 1, "R1"),
            new("B-02", 0, 2, "R1"),
            new("A-01", 0, 3, "R1"),
        };

        vm.Render(assignment, courses);

        var twoHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "B-02");
        var oneHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "A-01");
        Assert.Equal(1, twoHour.Period);
        Assert.Equal(2, twoHour.Assignment.RowSpan);
        Assert.Equal(3, oneHour.Period);
        Assert.Equal(1, oneHour.Assignment.RowSpan);
    }

    [Fact]
    public void Render_StructuredMergeMode_OverlappingOneHourAndTwoHourBlock_UsesSeparateSubColumns()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "한시간", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 1 } },
            new() { Id = "B-02", Name = "두시간", Grade = 2, Section = 1, ProfessorId = "P2", BlockStructure = new List<int> { 2 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("A-01", 0, 2, "R1"),
            new("B-02", 0, 2, "R1"),
            new("B-02", 0, 3, "R1"),
        };

        vm.Render(assignment, courses);

        var oneHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "A-01");
        var twoHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "B-02");
        Assert.Equal(2, oneHour.Period);
        Assert.Equal(1, oneHour.Assignment.RowSpan);
        Assert.Equal(2, twoHour.Period);
        Assert.Equal(2, twoHour.Assignment.RowSpan);
        Assert.NotEqual(oneHour.SubColumnIdx, twoHour.SubColumnIdx);
    }

    [Fact]
    public void Render_StructuredMergeMode_DoesNotMergePastDeclaredBlockLength()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 2, 1 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
            new("X-01", 0, 3, "R1"),
        };

        vm.Render(assignment, courses);

        Assert.Contains(vm.Cells, c => c.Period == 1 && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Cells, c => c.Period == 3 && c.Assignment.RowSpan == 1);
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
